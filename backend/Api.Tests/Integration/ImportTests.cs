using System.Net;
using System.Net.Http.Json;
using System.Text;
using FinanceApp.Domain.Import;

namespace FinanceApp.Api.Tests.Integration;

/// Importing is the one flow that writes hundreds of money rows at once, so the tests care
/// about the two ways that goes wrong quietly: the same statement imported twice, and a file
/// whose encoding was guessed wrong.
public class ImportTests
{
    private const string Statement = """
        "Data operacji";"Typ transakcji";"Kwota";"Waluta";"Saldo";"Opis"
        "2026-07-28";"Płatność kartą";"-45,60";"PLN";"3 214,08";"ŻABKA KRAKÓW"
        "2026-07-29";"Płatność kartą";"-120,00";"PLN";"3 094,08";"ORLEN"
        """;

    private static MultipartFormDataContent Upload(byte[] bytes, string name = "wyciag.csv")
    {
        var content = new ByteArrayContent(bytes);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private sealed record Preview(
        List<PreviewRow> Rows, List<Problem> Problems, string Delimiter, bool HeaderFound, string Encoding);
    private sealed record PreviewRow(
        int Line, DateOnly Date, decimal Amount, string Currency, string Description, string Kind, int? DuplicateOfId);
    private sealed record Problem(int Line, string Reason, string Raw);
    private sealed record ImportResult(int Created, int Failed, List<Problem> Problems);

    private static async Task<Preview> PreviewAsync(HttpClient client, byte[] bytes)
    {
        var res = await client.PostAsync("/api/import/preview", Upload(bytes));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Preview>())!;
    }

    [Fact]
    public async Task A_utf8_statement_is_read_and_nothing_is_written_yet()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();

        var preview = await PreviewAsync(client, Encoding.UTF8.GetBytes(Statement));

        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("utf-8", preview.Encoding);
        Assert.Equal(";", preview.Delimiter);
        Assert.True(preview.HeaderFound);
        Assert.All(preview.Rows, r => Assert.Equal("Expense", r.Kind));

        // Preview writes nothing: the count of transactions is unchanged.
        var after = await client.GetFromJsonAsync<List<object>>("/api/transactions");
        Assert.Empty(after!);
    }

    /// Polish banks still export windows-1250. Decoded as UTF-8 the Polish letters break, and
    /// every description in the import is quietly wrong.
    [Fact]
    public async Task A_windows_1250_statement_keeps_its_polish_letters()
    {
        using var api = new TestApiFactory();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1250).GetBytes(Statement);

        var preview = await PreviewAsync(api.CreateClient(), bytes);

        Assert.Equal("windows-1250", preview.Encoding);
        Assert.Contains(preview.Rows, r => r.Description.Contains("ŻABKA KRAKÓW"));
    }

    [Fact]
    public async Task Committing_writes_the_rows_that_were_kept()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();
        var preview = await PreviewAsync(client, Encoding.UTF8.GetBytes(Statement));

        var res = await client.PostAsJsonAsync("/api/import/commit", new
        {
            rows = preview.Rows.Select(r => new
            {
                line = r.Line, date = r.Date, amount = r.Amount,
                currency = r.Currency, categoryId = 1, note = r.Description,
            }),
        });

        var result = await res.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(2, result!.Created);
        Assert.Equal(0, result.Failed);

        var after = await client.GetFromJsonAsync<List<object>>("/api/transactions");
        Assert.Equal(2, after!.Count);
    }

    /// The point of a "поступовий" import: next month's export overlaps last month's, and
    /// re-uploading it must not double the money.
    [Fact]
    public async Task Rows_already_in_the_app_come_back_flagged_as_duplicates()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(Statement);

        var first = await PreviewAsync(client, bytes);
        Assert.All(first.Rows, r => Assert.Null(r.DuplicateOfId));

        await client.PostAsJsonAsync("/api/import/commit", new
        {
            rows = first.Rows.Select(r => new
            {
                line = r.Line, date = r.Date, amount = r.Amount,
                currency = r.Currency, categoryId = 1, note = r.Description,
            }),
        });

        var second = await PreviewAsync(client, bytes);

        Assert.All(second.Rows, r => Assert.NotNull(r.DuplicateOfId));
    }

    /// The same expense typed by hand earlier is a duplicate too — the description will not
    /// match, so the check is deliberately on the money and the day.
    [Fact]
    public async Task A_row_entered_by_hand_is_recognised_as_the_same_expense()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();

        await client.PostAsJsonAsync("/api/transactions", new
        {
            amount = 45.60m, currency = "PLN", categoryId = 1,
            frequency = "OneOff", date = "2026-07-28", note = "продукти",
        });

        var preview = await PreviewAsync(client, Encoding.UTF8.GetBytes(Statement));

        Assert.NotNull(preview.Rows.Single(r => r.Amount == -45.60m).DuplicateOfId);
        Assert.Null(preview.Rows.Single(r => r.Amount == -120.00m).DuplicateOfId);
    }

    [Fact]
    public async Task A_file_that_is_not_a_statement_says_what_is_missing()
    {
        using var api = new TestApiFactory();

        var preview = await PreviewAsync(api.CreateClient(),
            Encoding.UTF8.GetBytes("Sklep;Miasto\nBiedronka;Kraków"));

        Assert.Empty(preview.Rows);
        Assert.Contains(preview.Problems, p => p.Reason.Contains("дата"));
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        using var api = new TestApiFactory();

        var res = await api.CreateClient().PostAsync("/api/import/preview", Upload([]));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
