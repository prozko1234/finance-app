using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure.Fx;

namespace FinanceApp.Api.Tests;

public class FxConverterTests
{
    private static readonly DateOnly D = new(2026, 7, 24);

    [Fact]
    public async Task Pln_is_one_to_one_and_never_calls_provider()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(9.99m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertToBaseAsync(123.45m, "PLN", D);

        Assert.Equal(123.45m, r.AmountBase);
        Assert.Equal(1m, r.Rate);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Converts_using_provider_rate()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(3.95m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertToBaseAsync(10m, "USD", D);

        Assert.Equal(39.50m, r.AmountBase);
        Assert.Equal(3.95m, r.Rate);
        Assert.Equal(D, r.RateDate);
    }

    [Fact]
    public async Task Rounds_half_up_to_two_decimals()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(0.125m, D)); // 1 * 0.125 = 0.125 -> 0.13
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertToBaseAsync(1m, "UAH", D);

        Assert.Equal(0.13m, r.AmountBase);
    }

    [Fact]
    public async Task Second_call_same_currency_and_date_uses_cache()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(3.95m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        await sut.ConvertToBaseAsync(10m, "USD", D);
        await sut.ConvertToBaseAsync(20m, "USD", D);

        Assert.Equal(1, fake.Calls); // друге звернення — з кешу
    }

    [Fact]
    public async Task Falls_back_to_next_provider_when_first_has_no_rate()
    {
        using var mem = new SqliteInMemory();
        var nbp = new FakeRateProvider(null, "NBP");
        var ecb = new FakeRateProvider(new FxQuote(4.30m, D), "ECB");
        var sut = new CachingFxConverter(mem.Db, new IFxRateProvider[] { nbp, ecb });

        var r = await sut.ConvertToBaseAsync(10m, "EUR", D);

        Assert.Equal(43.00m, r.AmountBase);
        Assert.Equal(1, nbp.Calls);
        Assert.Equal(1, ecb.Calls);
    }

    [Fact]
    public async Task Throws_when_no_provider_returns_a_rate()
    {
        using var mem = new SqliteInMemory();
        var sut = new CachingFxConverter(mem.Db, new[] { new FakeRateProvider(null) });

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.ConvertToBaseAsync(10m, "USD", D));
    }
}
