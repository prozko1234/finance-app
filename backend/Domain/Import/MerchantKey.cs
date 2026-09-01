namespace FinanceApp.Domain.Import;

/// Turns a bank's description into something that can be recognised again.
///
/// Banks write the same shop differently every time — "BIEDRONKA 1234 KRAKOW",
/// "BIEDRONKA 7781 WARSZAWA", "PŁATNOŚĆ KARTĄ · BIEDRONKA 1234" — so the raw text is useless
/// as a key. Stripping the parts that change (branch numbers, cities, the bank's own words
/// for "card payment") leaves the part that does not: the name of the shop.
///
/// This is what makes learning possible. Categorise "BIEDRONKA 1234 KRAKOW" once and every
/// Biedronka since and hence lands in the same place, in any statement from any bank.
public static class MerchantKey
{
    /// The bank talking about itself rather than about the shop. Longest first, so
    /// "przelew wychodzący" is removed as a phrase and not left as a stray "wychodzący".
    private static readonly string[] BankNoise =
    [
        "zakup przy użyciu karty", "zakup przy uzyciu karty",
        "transakcja bezgotówkowa", "transakcja bezgotowkowa",
        "przelew przychodzący", "przelew przychodzacy",
        "przelew wychodzący", "przelew wychodzacy",
        "płatność kartą", "platnosc karta", "płatnosc karta",
        "wypłata z bankomatu", "wyplata z bankomatu",
        "przelew na telefon", "przelew własny", "przelew wlasny",
        "card payment", "payment from", "payment to", "transfer to", "transfer from",
        "tytuł przelewu", "tytul przelewu", "nr ref", "ref:", "data transakcji",
        "przelew", "transakcja", "zakup", "blik", "paypal", "przelew24", "p24",
    ];

    /// Words that survive noise removal but name nothing: legal forms and generic shop words.
    /// Without this "SKLEP SPOŻYWCZY MARIA" keys on "SKLEP" and lumps every corner shop in
    /// the country together.
    private static readonly string[] Meaningless =
    [
        "sp", "z", "o", "oo", "sa", "spzoo", "sklep", "market", "store", "shop",
        "pl", "com", "www", "ul", "al", "nr", "the", "and",
        // Polish shop-sign filler. Not an exhaustive list of adjectives and never will be —
        // just the handful common enough that leaving them in would merge unrelated shops.
        "spożywczy", "spozywczy", "wielobranżowy", "wielobranzowy", "handlowy",
        "usługi", "uslugi", "firma", "przedsiębiorstwo", "przedsiebiorstwo",
    ];

    /// Below this a token is an abbreviation or noise, not a name.
    private const int MinTokenLength = 3;

    /// A stable key for the same shop across statements. Empty when the description carries
    /// no name at all — a bare transfer, say — and then nothing is learned from it.
    public static string From(string? description)
    {
        var tokens = Tokenize(description);
        if (tokens.Count == 0) return "";

        // The name comes first in practically every export: "BIEDRONKA 1234 KRAKOW",
        // "ORLEN STACJA 55", "MPK KRAKOW". What follows is the branch and the city.
        return tokens[0];
    }

    /// The same description, tidied for a human to read: the shop, without the bank's
    /// boilerplate and without the branch number that changes every visit.
    public static string Clean(string? description)
    {
        var tokens = Tokenize(description);
        // Nothing survived tokenising — "J&R SP. Z O.O." is all initials and legal form. The
        // named field is still far better to look at than the whole labelled form the bank
        // wrote, so that is what falls through rather than the raw description.
        if (tokens.Count == 0)
            return string.IsNullOrWhiteSpace(description) ? "" : Named(description).Trim();

        // Two tokens, not one: "ORLEN STACJA" reads better than "ORLEN", and "CARREFOUR
        // EXPRESS" is a different shop from "CARREFOUR" to the person reading the list.
        return string.Join(' ', tokens.Take(2)).ToUpperInvariant();
    }

    /// The labelled fields Polish banks assemble a description out of. PKO does not write a
    /// merchant name — it writes a small form: "Lokalizacja: Adres: SHELL Miasto: Rzeszow
    /// Kraj: POLSKA". Every one of these has to be known, so a value can be cut at the next
    /// label rather than running on into the city and the country.
    private static readonly string[] Labels =
    [
        "tytuł:", "tytul:", "lokalizacja:", "adres:", "miasto:", "kraj:",
        "nazwa odbiorcy:", "nazwa nadawcy:", "adres odbiorcy:", "adres nadawcy:",
        "rachunek odbiorcy:", "rachunek nadawcy:", "numer karty:",
        "data wykonania operacji:", "data przetworzenia:", "oryginalna kwota operacji:",
        "referencje własne zleceniodawcy:", "referencje wlasne zleceniodawcy:",
        "nazwa i nr identyfikatora:", "symbol formularza:", "okres płatności:",
        "okres platnosci:",
    ];

    /// The fields that actually name the other side, best first. "Adres" is the shop on a card
    /// payment; the two "Nazwa" fields are the person or company on a transfer; the title is a
    /// last resort because it is free text and often just a reference number.
    ///
    /// Note that "adres:" cannot match "adres nadawcy:" — the colon is in a different place —
    /// so a sender's postal address never gets mistaken for a shop.
    private static readonly string[] NameLabels =
    [
        "adres:", "nazwa odbiorcy:", "nazwa nadawcy:", "tytuł:", "tytul:",
    ];

    /// The part of a labelled description that names the other side.
    ///
    /// Without this every PKO card payment tokenised to the same first word — the label
    /// "Tytuł" — so all of them landed in one group called "TYTUŁ LOKALIZACJA", and the import
    /// screen, whose whole purpose is to categorise a shop at a time, had nothing to group by.
    ///
    /// A description with no labels in it is returned untouched: plenty of banks do write a
    /// plain merchant name, and this must not get in their way.
    private static string Named(string description)
    {
        foreach (var label in NameLabels)
        {
            var at = description.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;

            var value = description[(at + label.Length)..];

            // Cut at whatever comes next — another field, or the separator between the parts
            // the reader joined together.
            var end = value.Length;
            foreach (var other in Labels)
            {
                var i = value.IndexOf(other, StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && i < end) end = i;
            }
            var separator = value.IndexOf('·');
            if (separator >= 0 && separator < end) end = separator;

            var named = value[..end].Trim();
            if (named.Length > 0) return named;
        }

        return description;
    }

    private static List<string> Tokenize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];

        var text = Named(description).ToLowerInvariant();
        foreach (var noise in BankNoise) text = text.Replace(noise, " ");

        // Digits go with the letters they are glued to: "z1234" is a Żabka branch, and
        // keeping it would make every branch its own merchant.
        var cleaned = new string(text.Select(c => char.IsLetter(c) ? c : ' ').ToArray());

        return cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= MinTokenLength && !Meaningless.Contains(t))
            .Select(t => t.ToUpperInvariant())
            .ToList();
    }
}
