using FinanceApp.Domain.Common;
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

        Assert.True(r.IsSuccess);
        Assert.Equal(123.45m, r.Value!.AmountBase);
        Assert.Equal(1m, r.Value.Rate);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Converts_using_provider_rate()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(3.95m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertToBaseAsync(10m, "USD", D);

        Assert.True(r.IsSuccess);
        Assert.Equal(39.50m, r.Value!.AmountBase);
        Assert.Equal(3.95m, r.Value.Rate);
        Assert.Equal(D, r.Value.RateDate);
    }

    [Fact]
    public async Task Rounds_half_up_to_two_decimals()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(0.125m, D)); // 1 * 0.125 = 0.125 -> 0.13
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertToBaseAsync(1m, "UAH", D);

        Assert.True(r.IsSuccess);
        Assert.Equal(0.13m, r.Value!.AmountBase);
    }

    [Fact]
    public async Task Second_call_same_currency_and_date_uses_cache()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(3.95m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        await sut.ConvertToBaseAsync(10m, "USD", D);
        await sut.ConvertToBaseAsync(20m, "USD", D);

        Assert.Equal(1, fake.Calls); // second lookup comes from cache
    }

    [Fact]
    public async Task Falls_back_to_next_provider_when_first_has_no_rate()
    {
        using var mem = new SqliteInMemory();
        var nbp = new FakeRateProvider(null, "NBP");
        var ecb = new FakeRateProvider(new FxQuote(4.30m, D), "ECB");
        var sut = new CachingFxConverter(mem.Db, new IFxRateProvider[] { nbp, ecb });

        var r = await sut.ConvertToBaseAsync(10m, "EUR", D);

        Assert.True(r.IsSuccess);
        Assert.Equal(43.00m, r.Value!.AmountBase);
        Assert.Equal(1, nbp.Calls);
        Assert.Equal(1, ecb.Calls);
    }

    [Fact]
    public async Task Fails_as_unsupported_when_no_provider_returns_a_rate()
    {
        using var mem = new SqliteInMemory();
        var sut = new CachingFxConverter(mem.Db, new[] { new FakeRateProvider(null) });

        var r = await sut.ConvertToBaseAsync(10m, "USD", D);

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Unsupported, r.Error.Type);
    }

    [Fact]
    public async Task From_base_reads_a_pln_amount_out_in_another_currency()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(4m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertFromBaseAsync(100m, "USD", D);

        Assert.True(r.IsSuccess);
        Assert.Equal(25m, r.Value!.AmountBase);
        Assert.Equal(4m, r.Value.Rate);
    }

    [Fact]
    public async Task From_base_uses_the_rate_of_the_given_date_not_of_today()
    {
        using var mem = new SqliteInMemory();
        // Two dates, two rates: the record's own date must decide, or a July expense
        // would quietly change size every time the zloty moves.
        var july = new DateOnly(2026, 7, 3);
        var december = new DateOnly(2026, 12, 3);
        var provider = new RatesByDateProvider(new()
        {
            [july] = new FxQuote(4m, july),
            [december] = new FxQuote(5m, december),
        });
        var sut = new CachingFxConverter(mem.Db, new[] { provider });

        var atJuly = await sut.ConvertFromBaseAsync(100m, "USD", july);
        var atDecember = await sut.ConvertFromBaseAsync(100m, "USD", december);

        Assert.Equal(25m, atJuly.Value!.AmountBase);
        Assert.Equal(20m, atDecember.Value!.AmountBase);
    }

    [Fact]
    public async Task From_base_is_one_to_one_for_pln()
    {
        using var mem = new SqliteInMemory();
        var fake = new FakeRateProvider(new FxQuote(9.99m, D));
        var sut = new CachingFxConverter(mem.Db, new[] { fake });

        var r = await sut.ConvertFromBaseAsync(123.45m, "PLN", D);

        Assert.True(r.IsSuccess);
        Assert.Equal(123.45m, r.Value!.AmountBase);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task From_base_fails_when_no_source_quotes_the_currency()
    {
        using var mem = new SqliteInMemory();
        var sut = new CachingFxConverter(mem.Db, new[] { new FakeRateProvider(null) });

        var r = await sut.ConvertFromBaseAsync(100m, "XYZ", D);

        Assert.False(r.IsSuccess);
    }

    /// Quotes a different rate per date — the point of the historical lookup.
    private sealed class RatesByDateProvider(Dictionary<DateOnly, FxQuote> rates) : IFxRateProvider
    {
        public string Name => "byDate";

        public Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(rates.TryGetValue(date, out var q) ? q : null);
    }
}
