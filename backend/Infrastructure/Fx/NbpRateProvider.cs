using System.Net.Http.Json;
using FinanceApp.Domain.Fx;

namespace FinanceApp.Infrastructure.Fx;

/// Primary source — Narodowy Bank Polski (table A, mid rate). PLN per 1 unit of currency.
/// Official, free, covers UAH/USD/EUR and others.
public sealed class NbpRateProvider(HttpClient http) : IFxRateProvider
{
    public string Name => "NBP";

    public async Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        // Window date-7..date, take the last available rate — this skips weekends/holidays.
        var start = date.AddDays(-7);
        var url = $"exchangerates/rates/A/{currency}/{start:yyyy-MM-dd}/{date:yyyy-MM-dd}/?format=json";

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null; // 404 = not in table A or no data

        var dto = await resp.Content.ReadFromJsonAsync<NbpResponse>(ct);
        var last = dto?.Rates?.LastOrDefault();
        if (last is null || last.Mid <= 0) return null;

        return new FxQuote(last.Mid, DateOnly.Parse(last.EffectiveDate));
    }

    private sealed record NbpResponse(List<NbpRate>? Rates);
    private sealed record NbpRate(string EffectiveDate, decimal Mid);
}
