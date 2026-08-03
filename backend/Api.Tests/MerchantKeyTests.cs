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
    [InlineData("BIEDRONKA 1234", BuiltInMerchants.Food)]
    [InlineData("ORLEN STACJA 55", BuiltInMerchants.Transport)]
    [InlineData("ROSSMANN 88", BuiltInMerchants.Health)]
    [InlineData("NETFLIX.COM", BuiltInMerchants.Fun)]
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
        string[] seeded = ["Їжа", "Транспорт", "Житло", "Здоров'я", "Розваги"];

        Assert.All(BuiltInMerchants.ByKey.Values, name => Assert.Contains(name, seeded));
    }
}
