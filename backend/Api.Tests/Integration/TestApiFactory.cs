using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceApp.Api.Tests.Integration;

/// Spins up the real API in-memory. Swaps SQLite for an in-memory connection and the
/// FX converter for a deterministic fake, so tests never touch a file or the network.
public class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// The password the app should demand, or null for an open (development-like) app.
    protected virtual string? Password => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        if (Password is not null)
            builder.UseSetting("Auth:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<IFxConverter>();
            services.AddScoped<IFxConverter, FakeFxConverter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}

/// Deterministic FX for tests: known rates, everything else unsupported.
public sealed class FakeFxConverter : IFxConverter
{
    public Task<Result<FxConversion>> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        var rate = RateFor(currency);
        if (rate is null)
            return Task.FromResult(Result<FxConversion>.Fail(Error.Unsupported($"no rate for {currency}")));

        var baseAmount = Math.Round(amount * rate.Value, 2, MidpointRounding.AwayFromZero);
        return Task.FromResult(Result<FxConversion>.Ok(new FxConversion(baseAmount, rate.Value, date)));
    }

    public Task<Result<FxConversion>> ConvertFromBaseAsync(
        decimal baseAmount, string currency, DateOnly date, CancellationToken ct = default)
    {
        var rate = RateFor(currency);
        if (rate is null)
            return Task.FromResult(Result<FxConversion>.Fail(Error.Unsupported($"no rate for {currency}")));

        var amount = Math.Round(baseAmount / rate.Value, 2, MidpointRounding.AwayFromZero);
        return Task.FromResult(Result<FxConversion>.Ok(new FxConversion(amount, rate.Value, date)));
    }

    private static decimal? RateFor(string currency) => currency.ToUpperInvariant() switch
    {
        "PLN" => 1m,
        "USD" => 4.0m,
        "EUR" => 4.3m,
        "UAH" => 0.1m,
        _ => null,
    };
}
