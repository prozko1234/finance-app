using FinanceApp.Domain.Import;

namespace FinanceApp.Api.Tests;

/// The whole of "learn once, filed forever" rests on this: the same shop has to produce the
/// same key across statements, banks and branches, and two different shops must not collide.
public class MerchantKeyTests
{
    [Theory]
    [InlineData("BIEDRONKA 1234 KRAKOW")]
    [InlineData("BIEDRONKA 7781 WARSZAWA")]
    [InlineData("Płatność kartą · BIEDRONKA 0042 GDANSK")]
    [InlineData("ZAKUP PRZY UŻYCIU KARTY BIEDRONKA 999")]
    public void The_same_shop_keys_the_same_however_the_bank_wrote_it(string description)
    {
        Assert.Equal("BIEDRONKA", MerchantKey.From(description));
    }

    [Fact]
    public void A_branch_number_glued_to_the_name_does_not_split_the_shop()
    {
        // Żabka writes "Z1234" as the branch. Keeping the digits would make every corner
        // shop its own merchant and the learning would never fire twice.
        Assert.Equal(MerchantKey.From("ZABKA Z9999 KRAKOW"), MerchantKey.From("ZABKA Z1234 WARSZAWA"));
    }

    [Fact]
    public void Different_shops_do_not_collide()
    {
        Assert.NotEqual(MerchantKey.From("ORLEN STACJA 55"), MerchantKey.From("LIDL 221 KRAKOW"));
    }

    [Fact]
    public void The_banks_own_words_are_not_mistaken_for_a_shop()
    {
        // "Przelew" is the bank describing itself. Keying on it would file every transfer in
        // the country under one merchant.
        Assert.Equal("ACME", MerchantKey.From("Przelew wychodzący ACME SP Z O O"));
    }

    [Fact]
    public void A_description_with_no_name_in_it_teaches_nothing()
    {
        Assert.Equal("", MerchantKey.From("Przelew 12 34"));
        Assert.Equal("", MerchantKey.From(""));
        Assert.Equal("", MerchantKey.From(null));
    }

    [Fact]
    public void A_generic_word_is_not_a_merchant()
    {
        // Otherwise every "SKLEP ..." in Poland becomes one shop.
        Assert.Equal("MARIA", MerchantKey.From("SKLEP SPOŻYWCZY MARIA"));
    }

    [Fact]
    public void The_label_a_human_reads_keeps_two_words()
    {
        // "CARREFOUR EXPRESS" is not "CARREFOUR" to the person scanning the list, even though
        // both are filed the same way.
        Assert.Equal("CARREFOUR EXPRESS", MerchantKey.Clean("CARREFOUR EXPRESS 1234 KRAKOW"));
    }

    [Fact]
    public void An_unreadable_description_is_shown_as_it_came()
    {
        // Better a raw line than an empty cell: the user can still tell what the row was.
        Assert.Equal("12 34 56", MerchantKey.Clean("12 34 56"));
    }

    [Theory]
    [InlineData("BIEDRONKA 1234", BuiltInMerchants.Groceries)]
    [InlineData("ORLEN STACJA 55", BuiltInMerchants.Transport)]
    [InlineData("ROSSMANN 88", BuiltInMerchants.Health)]
    [InlineData("NETFLIX.COM", BuiltInMerchants.Subscriptions)]
    [InlineData("GLOVO * ORDER", BuiltInMerchants.Delivery)]
    [InlineData("STEAMGAMES.COM", BuiltInMerchants.Fun)]
    [InlineData("TAURON SPRZEDAZ", BuiltInMerchants.Home)]
    public void Well_known_polish_shops_are_filed_out_of_the_box(string description, string category)
    {
        Assert.Equal(category, BuiltInMerchants.CategoryNameFor(MerchantKey.From(description)));
    }

    [Fact]
    public void An_unknown_shop_is_left_for_the_user_to_decide()
    {
        // Guessing here would be silently wrong and stay wrong.
        Assert.Null(BuiltInMerchants.CategoryNameFor(MerchantKey.From("KWIACIARNIA U ANI")));
    }

    [Fact]
    public void Every_built_in_rule_points_at_a_category_the_app_actually_seeds()
    {
        // The list maps to names, so a typo would produce a rule that matches nothing and
        // quietly does nothing at all.
        string[] seeded =
        [
            "Продукти", "Доставка", "Кафе й бари", "Транспорт",
            "Здоров'я", "Житло", "Підписки", "Розваги", "Перекази",
        ];

        Assert.All(BuiltInMerchants.ByKey.Values, name => Assert.Contains(name, seeded));
    }

    /// PKO does not write a merchant name — it writes a small form:
    /// "Lokalizacja: Adres: SHELL Miasto: Rzeszow Kraj: POLSKA". Tokenising that whole string
    /// took its FIRST word, which is the label "Tytuł" — so every card payment in a statement
    /// keyed identically and the import screen showed one group of 62 rows called
    /// "TYTUŁ LOKALIZACJA". Grouping by shop is the entire point of that screen.
    [Fact]
    public void A_shop_is_read_out_of_the_labelled_form_a_bank_writes()
    {
        const string pko =
            "Płatność kartą · Tytuł:  74838496243381684421864 · " +
            "Lokalizacja: Adres: SHELL Miasto: Rzeszow Kraj: POLSKA";

        Assert.Equal("SHELL", MerchantKey.From(pko));
        Assert.Equal("SHELL", MerchantKey.Clean(pko));
    }

    /// The city and the country come after the shop in the same field and change nothing about
    /// which shop it is — the value has to stop at the next label.
    [Fact]
    public void The_city_and_country_are_not_part_of_the_name()
    {
        const string pko = "Lokalizacja: Adres: ZABKA Z7655 K.1 Miasto: RZESZOW Kraj: POLSKA";

        Assert.DoesNotContain("RZESZOW", MerchantKey.Clean(pko));
        Assert.Equal("ZABKA", MerchantKey.From(pko));
    }

    /// A transfer has no shop. The person on the other side is the thing worth grouping by —
    /// and the title, which is usually a reference number, is the last thing to fall back on.
    [Fact]
    public void A_transfer_keys_on_the_person_rather_than_on_its_title()
    {
        const string outgoing =
            "Przelew z rachunku · Rachunek odbiorcy: 44 1020 4405 0000 2202 0562 9938 · " +
            "Nazwa odbiorcy: BOHDAN FILIMONYUK · Tytuł: PRZELEW IKO NA NUMER RACHUNKU";
        const string incoming =
            "Przelew na telefon przychodz. zew. · Rachunek nadawcy: 08 1090 2750 · " +
            "Nazwa nadawcy: MYKOLA MARCHUK · Tytuł: PALIWO";

        Assert.Equal("BOHDAN", MerchantKey.From(outgoing));
        Assert.Equal("MYKOLA", MerchantKey.From(incoming));
    }

    /// "Adres:" is the shop; "Adres nadawcy:" is a postcode and a city. The colon sits in a
    /// different place, which is what keeps the two apart — worth a test, because merging them
    /// would key every incoming transfer on the sender's town.
    [Fact]
    public void A_senders_postal_address_is_not_a_shop()
    {
        const string salary =
            "Przelew na konto · Nazwa nadawcy: SII SP. Z O.O. ALEJA NIEPODLEGŁOŚCI 69 · " +
            "Adres nadawcy: 02-626 WARSZAWA PL · Tytuł: 0720261";

        Assert.Equal("SII", MerchantKey.From(salary));
    }

    /// Plenty of banks write a plain merchant name and no labels at all. That must go on
    /// working exactly as it did.
    [Fact]
    public void A_description_without_labels_is_read_as_before()
    {
        Assert.Equal("BIEDRONKA", MerchantKey.From("BIEDRONKA 1234 KRAKOW"));
    }

    /// All initials and legal form, so nothing survives tokenising. Showing the named field
    /// beats showing the whole form the bank wrote.
    [Fact]
    public void A_name_that_tokenises_to_nothing_still_reads_as_itself()
    {
        const string pko = "Płatność kartą · Lokalizacja: Adres: J&R SP. Z O.O. Miasto: RZESZOW";

        Assert.Equal("", MerchantKey.From(pko));
        Assert.Equal("J&R SP. Z O.O.", MerchantKey.Clean(pko));
    }
}
