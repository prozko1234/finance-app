using System.Net.Http.Json;
using System.Text;

namespace FinanceApp.Api.Tests.Integration;

/// What turns the importer from a form into something that gets out of the way: shops
/// everyone in Poland uses are known out of the box, and anything else is filed by hand
/// exactly once.
public class ImportLearningTests
{
    private sealed record Preview(List<Row> Rows);
    private sealed record Row(
        int Line, DateOnly Date, decimal Amount, string Currency, string Description,
        string Merchant, string MerchantKey, string Kind, int? DuplicateOfId, int? SuggestedCategoryId);

    private static async Task<Preview> PreviewAsync(HttpClient client, string csv)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        var form = new MultipartFormDataContent { { content, "file", "wyciag.csv" } };

        var res = await client.PostAsync("/api/import/preview", form);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Preview>())!;
    }

    private static Task<HttpResponseMessage> CommitAsync(HttpClient client, Row row, int categoryId) =>
        client.PostAsJsonAsync("/api/import/commit", new
        {
            rows = new[]
            {
                new
                {
                    line = row.Line, date = row.Date, amount = row.Amount,
                    currency = row.Currency, categoryId, note = row.Description,
                },
            },
        });

    private const string KnownShops =
        "\"Data\";\"Kwota\";\"Opis\"\n"
        + "\"2026-07-28\";\"-45,60\";\"ZABKA Z1234 KRAKOW\"\n"
        + "\"2026-07-29\";\"-120,00\";\"ORLEN STACJA 55\"\n";

    [Fact]
    public async Task Known_polish_shops_arrive_already_filed()
    {
        using var api = new TestApiFactory();

        var preview = await PreviewAsync(api.CreateClient(), KnownShops);

        // 1 = Їжа, 2 = Транспорт, as the app seeds them.
        Assert.Equal(1, preview.Rows.Single(r => r.MerchantKey == "ZABKA").SuggestedCategoryId);
        Assert.Equal(2, preview.Rows.Single(r => r.MerchantKey == "ORLEN").SuggestedCategoryId);
    }

    [Fact]
    public async Task A_shop_filed_by_hand_is_remembered_for_next_time()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();

        var first = await PreviewAsync(client,
            "\"Data\";\"Kwota\";\"Opis\"\n\"2026-07-20\";\"-88,00\";\"KWIACIARNIA U ANI KRAKOW\"\n");
        Assert.Null(first.Rows[0].SuggestedCategoryId);

        await CommitAsync(client, first.Rows[0], categoryId: 5);

        // Another branch, another day, another amount — the same shop.
        var later = await PreviewAsync(client,
            "\"Data\";\"Kwota\";\"Opis\"\n\"2026-08-14\";\"-140,00\";\"KWIACIARNIA U ANI WARSZAWA\"\n");

        Assert.Equal(5, later.Rows[0].SuggestedCategoryId);
    }

    /// The built-in list is a guess about people in general; this is a fact about this
    /// person. A rule that argued with them would be one they cannot get rid of.
    [Fact]
    public async Task Filing_a_shop_somewhere_else_beats_the_built_in_guess()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();
        const string file = "\"Data\";\"Kwota\";\"Opis\"\n\"2026-07-20\";\"-88,00\";\"ZABKA Z1234\"\n";

        var preview = await PreviewAsync(client, file);
        Assert.Equal(1, preview.Rows[0].SuggestedCategoryId); // built-in: Їжа

        await CommitAsync(client, preview.Rows[0], categoryId: 5);

        var again = await PreviewAsync(client, file);
        Assert.Equal(5, again.Rows[0].SuggestedCategoryId);
    }

    /// The screen groups by this key. Getting it wrong would either scatter one shop across
    /// a dozen rows or merge two shops into one choice.
    [Fact]
    public async Task Rows_of_one_shop_share_a_key_however_the_branch_differs()
    {
        using var api = new TestApiFactory();

        var preview = await PreviewAsync(api.CreateClient(),
            "\"Data\";\"Kwota\";\"Opis\"\n"
            + "\"2026-07-20\";\"-12,00\";\"ZABKA Z1234 KRAKOW\"\n"
            + "\"2026-07-21\";\"-18,50\";\"ZABKA Z7788 WARSZAWA\"\n"
            + "\"2026-07-22\";\"-40,00\";\"LIDL 221\"\n");

        Assert.Equal(2, preview.Rows.Count(r => r.MerchantKey == "ZABKA"));
        Assert.Single(preview.Rows.Where(r => r.MerchantKey == "LIDL"));
    }

    /// An income row's description is a client or an employer. Learning from it would file
    /// "every payment from ACME" into a category that has one member anyway.
    [Fact]
    public async Task Income_does_not_teach_the_rules()
    {
        using var api = new TestApiFactory();
        var client = api.CreateClient();

        var preview = await PreviewAsync(client,
            "\"Data\";\"Kwota\";\"Opis\"\n\"2026-07-20\";\"12300,00\";\"ACME FAKTURA 07\"\n");
        Assert.Equal("Income", preview.Rows[0].Kind);

        await CommitAsync(client, preview.Rows[0], categoryId: 5);

        var again = await PreviewAsync(client,
            "\"Data\";\"Kwota\";\"Opis\"\n\"2026-08-20\";\"-50,00\";\"ACME SKLEP\"\n");

        Assert.Null(again.Rows[0].SuggestedCategoryId);
    }
}
