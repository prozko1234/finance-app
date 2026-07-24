using System.Net.Http.Json;
using FinanceApp.Domain.Fx;

namespace FinanceApp.Infrastructure.Fx;

/// Основне джерело — Narodowy Bank Polski (table A, mid rate). PLN за 1 одиницю валюти.
/// Офіційний, безкоштовний, покриває UAH/USD/EUR та ін.
public sealed class NbpRateProvider(HttpClient http) : IFxRateProvider
{
    public string Name => "NBP";

    public async Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        // Вікно date-7..date і беремо останній доступний курс — так обходимо вихідні/свята.
        var start = date.AddDays(-7);
        var url = $"exchangerates/rates/A/{currency}/{start:yyyy-MM-dd}/{date:yyyy-MM-dd}/?format=json";

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null; // 404 = нема в таблиці A або нема даних

        var dto = await resp.Content.ReadFromJsonAsync<NbpResponse>(ct);
        var last = dto?.Rates?.LastOrDefault();
        if (last is null || last.Mid <= 0) return null;

        return new FxQuote(last.Mid, DateOnly.Parse(last.EffectiveDate));
    }

    private sealed record NbpResponse(List<NbpRate>? Rates);
    private sealed record NbpRate(string EffectiveDate, decimal Mid);
}
