using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests.Integration;

/// The setting has to reach the number on the home screen, not just the settings row —
/// that is the whole point of it. Its own factory: changing when the month starts would
/// otherwise leak into every other integration test sharing the database.
public class PeriodStartDayTests : IClassFixture<PeriodStartDayTests.Api>
{
    public sealed class Api : TestApiFactory;

    private readonly HttpClient _client;

    public PeriodStartDayTests(Api factory) => _client = factory.CreateClient();

    private static Task<HttpResponseMessage> SetDay(HttpClient client, int day) =>
        client.PutAsJsonAsync("/api/settings/period-start-day", new { day });

    [Fact]
    public async Task The_default_is_the_first_so_an_untouched_app_reads_as_before()
    {
        // Its own app: the shared one has had its day changed by the tests below, and
        // "untouched" is the whole claim here.
        using var untouched = new Api();
        var settings = await untouched.CreateClient().GetFromJsonAsync<JsonElement>("/api/settings");

        Assert.Equal(BudgetPeriods.FirstOfMonth, settings.GetProperty("periodStartDay").GetInt32());
    }

    [Fact]
    public async Task Setting_the_day_reports_the_period_it_produces()
    {
        var res = await SetDay(_client, 10);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var start = body.GetProperty("periodStart").GetDateTime();
        var end = body.GetProperty("periodEnd").GetDateTime();

        Assert.Equal(10, body.GetProperty("periodStartDay").GetInt32());
        Assert.Equal(10, start.Day);
        // Ends the day before the next payday, whatever month that lands in.
        Assert.Equal(9, end.Day);
        Assert.True(end > start);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public async Task A_day_that_is_not_in_every_month_is_refused(int day)
    {
        // Beyond the 28th "the 30th" would mean four different dates a year.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetDay(_client, day)).StatusCode);
    }

    [Fact]
    public async Task The_daily_norm_counts_days_to_the_next_payday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        // A payday the day after today makes this period end today: one day left, whatever
        // the calendar says — which is exactly what the old code could never report.
        var tomorrow = today.AddDays(1).Day;
        if (tomorrow > 28) return; // the setting is capped at 28; nothing to prove near month end

        await SetDay(_client, tomorrow);
        var byPayday = await _client.GetFromJsonAsync<JsonElement>("/api/summary/safe-to-spend");

        Assert.Equal(1, byPayday.GetProperty("daysLeftInPeriod").GetInt32());
    }
}
