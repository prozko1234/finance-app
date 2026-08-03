using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinanceApp.Api.Tests.Integration;

public class EndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Categories_are_seeded()
    {
        var res = await _client.GetAsync("/api/categories");
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, json.GetArrayLength());
    }

    [Fact]
    public async Task Create_valid_pln_transaction_returns_201_with_base_amount()
    {
        var res = await _client.PostAsJsonAsync("/api/transactions", new
        {
            amount = 49.90m, currency = "PLN", categoryId = 1,
            priority = "Must", frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(49.90m, body.GetProperty("amountBase").GetDecimal());
    }

    [Fact]
    public async Task Create_usd_transaction_converts_via_fx()
    {
        var res = await _client.PostAsJsonAsync("/api/transactions", new
        {
            amount = 10m, currency = "USD", categoryId = 1,
            priority = "Should", frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40.00m, body.GetProperty("amountBase").GetDecimal()); // 10 * 4.0
    }

    [Fact]
    public async Task Create_with_zero_amount_returns_400_validation_problem()
    {
        var res = await _client.PostAsJsonAsync("/api/transactions", new
        {
            amount = 0m, currency = "PLN", categoryId = 1,
            priority = "Must", frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_with_unsupported_currency_returns_422()
    {
        var res = await _client.PostAsJsonAsync("/api/transactions", new
        {
            amount = 10m, currency = "GBP", categoryId = 1,
            priority = "Want", frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Get_missing_transaction_returns_404()
    {
        var res = await _client.GetAsync("/api/transactions/999999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// Money that arrived is now the only thing a budget can be made of — the fallback
    /// amount typed into settings is gone.
    [Fact]
    public async Task Income_becomes_the_budget()
    {
        var put = await _client.PostAsJsonAsync("/api/transactions/income", new
        {
            amount = 3000m, amountIncludesVat = false, currency = "PLN",
        });
        put.EnsureSuccessStatusCode();

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/summary/safe-to-spend");

        Assert.True(body.GetProperty("budgetSet").GetBoolean());
        Assert.Equal(3000m, body.GetProperty("periodBudget").GetDecimal());
    }

    [Fact]
    public async Task Settings_default_to_reading_in_the_base_currency()
    {
        var res = await _client.GetAsync("/api/settings");
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PLN", json.GetProperty("displayCurrency").GetString());
        Assert.False(json.GetProperty("taxesInBaseCurrency").GetBoolean());
    }

    [Fact]
    public async Task Choosing_a_currency_without_a_rate_is_refused()
    {
        var res = await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "XYZ" });

        // Shape is fine, so this is a business refusal, not a validation one.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Choosing_uah_flags_that_taxes_stay_in_pln()
    {
        var res = await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "uah" });
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UAH", json.GetProperty("displayCurrency").GetString());
        Assert.True(json.GetProperty("taxesInBaseCurrency").GetBoolean());

        // Put it back — the fixture's database is shared across tests in this class.
        await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "PLN" });
    }

    [Fact]
    public async Task Reading_in_another_currency_converts_the_summary_and_the_rows()
    {
        await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "USD" });
        try
        {
            // FakeFxConverter: 1 USD = 4 PLN, so 100 PLN reads as 25 USD.
            var create = await _client.PostAsJsonAsync("/api/transactions", new
            {
                amount = 100m,
                currency = "PLN",
                categoryId = 1,
                date = DateOnly.FromDateTime(DateTime.Now),
            });
            create.EnsureSuccessStatusCode();

            var tx = await create.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100m, tx.GetProperty("amountBase").GetDecimal());
            Assert.Equal(25m, tx.GetProperty("amountDisplay").GetDecimal());
            Assert.Equal("USD", tx.GetProperty("displayCurrency").GetString());

            var summary = await _client.GetFromJsonAsync<JsonElement>("/api/summary/safe-to-spend");
            Assert.Equal("USD", summary.GetProperty("currency").GetString());
        }
        finally
        {
            await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "PLN" });
        }
    }

    [Fact]
    public async Task Storage_stays_in_pln_whatever_the_user_reads()
    {
        await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "USD" });
        try
        {
            var create = await _client.PostAsJsonAsync("/api/transactions", new
            {
                amount = 40m,
                currency = "PLN",
                categoryId = 1,
                date = DateOnly.FromDateTime(DateTime.Now),
            });
            var tx = await create.Content.ReadFromJsonAsync<JsonElement>();

            // The anchor is untouched: switching the display currency must never rewrite
            // what was stored, or history would drift every time the setting changes.
            Assert.Equal(40m, tx.GetProperty("amountBase").GetDecimal());
            Assert.Equal(1m, tx.GetProperty("fxRate").GetDecimal());
        }
        finally
        {
            await _client.PutAsJsonAsync("/api/settings/currency", new { currency = "PLN" });
        }
    }

    [Fact]
    public async Task Counting_what_is_left_takes_over_the_month_and_moves_the_window()
    {
        // Starting mid-period: what is in the account beats whatever income says, and
        // spending is counted from the day of the count — the rest is already inside that
        // figure.
        try
        {
            var res = await _client.PutAsJsonAsync("/api/opening-balance", new { amount = 1800m });
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("appliesNow").GetBoolean());

            var summary = await _client.GetFromJsonAsync<JsonElement>("/api/summary/safe-to-spend");
            Assert.Equal(1800m, summary.GetProperty("periodBudget").GetDecimal());
            Assert.True(summary.GetProperty("fromOpeningBalance").GetBoolean());
            Assert.Equal(
                DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                summary.GetProperty("windowStart").GetString());
        }
        finally
        {
            await _client.DeleteAsync("/api/opening-balance");
        }
    }

    [Fact]
    public async Task A_count_dated_tomorrow_is_refused()
    {
        var res = await _client.PutAsJsonAsync("/api/opening-balance", new
        {
            amount = 100m, date = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
