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
        if (tokens.Count == 0) return (description ?? "").Trim();

        // Two tokens, not one: "ORLEN STACJA" reads better than "ORLEN", and "CARREFOUR
        // EXPRESS" is a different shop from "CARREFOUR" to the person reading the list.
        return string.Join(' ', tokens.Take(2)).ToUpperInvariant();
    }

    private static List<string> Tokenize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];

        var text = description.ToLowerInvariant();
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
