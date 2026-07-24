using System.Globalization;
using System.Xml.Linq;
using FinanceApp.Domain.Fx;

namespace FinanceApp.Infrastructure.Fx;

/// Фолбек — European Central Bank (90-денний історичний файл). ECB дає units-per-EUR,
/// тож PLN за 1 одиницю = rate_PLN / rate_currency. Увага: ECB НЕ містить UAH —
/// для гривні фолбек не спрацює, її покриває лише NBP.
public sealed class EcbRateProvider(HttpClient http) : IFxRateProvider
{
    private static readonly XNamespace Ns = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    public string Name => "ECB";

    public async Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();

        var xml = await http.GetStringAsync("stats/eurofxref/eurofxref-hist-90d.xml", ct);
        var doc = XDocument.Parse(xml);

        // Дні (Cube time=...), найближчий <= запитаної дати.
        var days = doc.Descendants(Ns + "Cube")
            .Where(c => c.Attribute("time") is not null)
            .Select(c => (Date: DateOnly.Parse(c.Attribute("time")!.Value), Node: c))
            .Where(x => x.Date <= date)
            .OrderByDescending(x => x.Date);

        foreach (var day in days)
        {
            var rates = day.Node.Elements(Ns + "Cube")
                .ToDictionary(
                    e => e.Attribute("currency")!.Value,
                    e => decimal.Parse(e.Attribute("rate")!.Value, CultureInfo.InvariantCulture));

            if (!rates.TryGetValue("PLN", out var plnPerEur) || plnPerEur <= 0)
                return null;
            if (currency == "EUR")
                return new FxQuote(plnPerEur, day.Date);
            if (rates.TryGetValue(currency, out var cPerEur) && cPerEur > 0)
                return new FxQuote(plnPerEur / cPerEur, day.Date);

            return null; // валюта не в списку ECB (напр. UAH)
        }

        return null;
    }
}
