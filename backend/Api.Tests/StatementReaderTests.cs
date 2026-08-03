using FinanceApp.Domain.Import;

namespace FinanceApp.Api.Tests;

/// The importer's whole claim is that it does not need to know the bank. These tests are
/// shaped like the exports it will actually meet — Polish banks first, since that is where
/// the money is — and none of them tells the reader anything about the layout.
public class StatementReaderTests
{
    private static StatementReadResult Read(string text) => StatementReader.Read(text, "PLN");

    /// PKO BP: semicolon-separated, day-first dates, decimal comma, the merchant spread over
    /// several columns, and a running balance that parses as a number just as well as the
    /// amount does.
    private const string Pko = """
        "Data operacji";"Data waluty";"Typ transakcji";"Kwota";"Waluta";"Saldo po transakcji";"Opis transakcji"
        "2026-07-28";"2026-07-28";"Płatność kartą";"-45,60";"PLN";"3 214,08";"BIEDRONKA 1234 KRAKOW"
        "2026-07-29";"2026-07-29";"Przelew przychodzący";"12 300,00";"PLN";"15 514,08";"FAKTURA 07/2026"
        "2026-07-30";"2026-07-30";"Płatność kartą";"-1 234,56";"PLN";"14 279,52";"IKEA KRAKOW"
        """;

    [Fact]
    public void A_pko_export_is_read_without_being_told_anything_about_it()
    {
        var result = Read(Pko);

        Assert.Empty(result.Problems);
        Assert.Equal(';', result.Delimiter);
        Assert.True(result.HeaderFound);
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void The_amount_is_told_apart_from_the_running_balance()
    {
        // Both columns are numbers and both parse. Picking the balance would import a
        // completely wrong figure while looking like it worked — the worst failure available.
        var result = Read(Pko);

        Assert.Equal(-45.60m, result.Rows[0].Amount);
        Assert.Equal(12_300.00m, result.Rows[1].Amount);
        Assert.Equal(-1_234.56m, result.Rows[2].Amount);
    }

    [Fact]
    public void The_merchant_survives_even_though_it_is_spread_over_columns()
    {
        var result = Read(Pko);

        Assert.Contains("BIEDRONKA", result.Rows[0].Description);
        Assert.Contains("Płatność kartą", result.Rows[0].Description);
    }

    [Fact]
    public void Currency_is_taken_from_the_file_when_it_is_there()
    {
        Assert.All(Read(Pko).Rows, r => Assert.Equal("PLN", r.Currency));
    }

    /// mBank: comma-separated, dots in the dates, a preamble above the table.
    [Fact]
    public void A_preamble_above_the_table_is_not_imported_as_transactions()
    {
        var result = Read("""
            mBank S.A.
            Lista operacji

            #Data operacji,#Opis operacji,#Kwota,#Saldo po operacji
            03.08.2026,ZABKA Z1234,-12.99,1000.00
            04.08.2026,ORLEN STACJA 55,-250.00,750.00
            """);

        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new DateOnly(2026, 8, 3), result.Rows[0].Date);
        Assert.Equal(-12.99m, result.Rows[0].Amount);
    }

    /// Revolut writes English headers, ISO dates and a dot decimal — the opposite of PKO in
    /// every axis, and it has to work with the same code.
    [Fact]
    public void An_english_dot_decimal_export_reads_the_same_way()
    {
        var result = Read("""
            Type,Started Date,Completed Date,Description,Amount,Currency,Balance
            CARD_PAYMENT,2026-08-01 10:11:12,2026-08-01,Spotify,-23.99,PLN,410.55
            TOPUP,2026-08-02 08:00:00,2026-08-02,Payment from Bohdan,500.00,PLN,910.55
            """);

        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(-23.99m, result.Rows[0].Amount);
        Assert.Contains("Spotify", result.Rows[0].Description);
    }

    [Fact]
    public void A_description_containing_the_delimiter_does_not_lose_its_tail()
    {
        var result = Read("""
            "Data";"Kwota";"Opis"
            "2026-08-01";"-10,00";"SKLEP, ul. Długa 5"
            """);

        Assert.Single(result.Rows);
        Assert.Contains("ul. Długa 5", result.Rows[0].Description);
    }

    [Fact]
    public void A_file_without_a_header_is_still_read()
    {
        // Some exports are just rows. The columns are worked out from what they contain.
        var result = Read("""
            2026-08-01;-45,60;BIEDRONKA
            2026-08-02;-12,00;ZABKA
            """);

        Assert.False(result.HeaderFound);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(-45.60m, result.Rows[0].Amount);
    }

    [Fact]
    public void A_file_with_no_date_or_amount_says_so_instead_of_importing_nothing_quietly()
    {
        var result = Read("""
            Sklep;Miasto
            Biedronka;Kraków
            """);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Problems, p => p.Reason.Contains("дата"));
    }

    [Fact]
    public void One_unreadable_row_does_not_cost_the_rest_of_the_file()
    {
        var result = Read("""
            "Data";"Kwota";"Opis"
            "2026-08-01";"-45,60";"OK"
            "";"";"порожній рядок посеред файлу"
            "2026-08-03";"-12,00";"теж OK"
            """);

        Assert.Equal(2, result.Rows.Count);
        Assert.Single(result.Problems);
        Assert.Equal(3, result.Problems[0].Line);
    }
}
