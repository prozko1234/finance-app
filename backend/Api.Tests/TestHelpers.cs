using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// AppDbContext on in-memory SQLite (the connection is kept open for the test's lifetime).
public sealed class SqliteInMemory : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }

    public SqliteInMemory()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}

/// Stub rate provider: returns a given quote and counts calls.
public sealed class FakeRateProvider(FxQuote? quote, string name = "FAKE") : IFxRateProvider
{
    public int Calls { get; private set; }
    public string Name => name;

    public Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(quote);
    }
}
