using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// AppDbContext на in-memory SQLite (з'єднання тримаємо відкритим, поки живе тест).
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

/// Підставний провайдер курсів: повертає заданий quote і рахує виклики.
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
