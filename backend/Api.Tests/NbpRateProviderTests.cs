using System.Net;
using System.Text;
using FinanceApp.Infrastructure.Fx;

namespace FinanceApp.Api.Tests;

public class NbpRateProviderTests
{
    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static NbpRateProvider Provider(HttpStatusCode code, string body) =>
        new(new HttpClient(new StubHandler(code, body)) { BaseAddress = new Uri("https://api.nbp.pl/api/") });

    [Fact]
    public async Task Parses_mid_and_effective_date()
    {
        const string json =
            """{"table":"A","currency":"dolar amerykański","code":"USD","rates":[{"no":"140/A/NBP/2026","effectiveDate":"2026-07-23","mid":3.9512}]}""";
        var sut = Provider(HttpStatusCode.OK, json);

        var q = await sut.GetPlnPerUnitAsync("USD", new DateOnly(2026, 7, 24));

        Assert.NotNull(q);
        Assert.Equal(3.9512m, q!.PlnPerUnit);
        Assert.Equal(new DateOnly(2026, 7, 23), q.EffectiveDate);
    }

    [Fact]
    public async Task Takes_last_rate_in_a_multi_day_window()
    {
        const string json =
            """{"rates":[{"effectiveDate":"2026-07-21","mid":3.90},{"effectiveDate":"2026-07-23","mid":3.95}]}""";
        var sut = Provider(HttpStatusCode.OK, json);

        var q = await sut.GetPlnPerUnitAsync("USD", new DateOnly(2026, 7, 24));

        Assert.Equal(3.95m, q!.PlnPerUnit);
        Assert.Equal(new DateOnly(2026, 7, 23), q.EffectiveDate);
    }

    [Fact]
    public async Task Returns_null_on_404()
    {
        var sut = Provider(HttpStatusCode.NotFound, "404 NotFound - Not Found");

        var q = await sut.GetPlnPerUnitAsync("XXX", new DateOnly(2026, 7, 24));

        Assert.Null(q);
    }
}
