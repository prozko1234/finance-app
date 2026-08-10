using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Auth;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Common;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
        Db = new AppDbContext(options, new FixedCurrentUser(UserId));
        Db.Database.EnsureCreated();

        // The starting categories and allocation scheme used to arrive with the schema, as
        // model seed data. They are per-account now, so a test database is provisioned the
        // same way a real account is — otherwise every test would have to invent its own
        // "Інше" before it could delete a category.
        new UserProvisioningService(Db).ProvisionAsync(UserId).GetAwaiter().GetResult();
    }

    /// The account every test writes as unless it says otherwise.
    public const int UserId = 1;

    /// A second view of the SAME database, read as somebody else. The connection is shared,
    /// so this is one database with two accounts looking at it — which is the only way to
    /// prove that what keeps them apart is the filter and not the storage.
    public AppDbContext As(int? userId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options,
            new FixedCurrentUser(userId));

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}

/// Money that arrived. The only way a budget exists at all now that the fallback amount in
/// settings is gone, so most tests need one line of it.
public static class TestIncome
{
    public static Transaction Income(decimal amount, DateOnly? date = null)
    {
        var on = date ?? DateOnly.FromDateTime(DateTime.Now);

        return new Transaction
        {
            Kind = TransactionKind.Income,
            CurrencyOriginal = "PLN",
            AmountOriginal = amount,
            AmountBase = amount,
            FxRate = 1m,
            FxDate = on,
            Date = on,
            CategoryId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// The safe-to-spend service with every collaborator wired the way the API wires it. Built
/// here rather than in each test file because the graph is a dozen lines deep and it is the
/// same graph every time — a second copy drifts the moment one of them gains a dependency.
public static class TestSummary
{
    public static SummaryService Sut(SqliteInMemory mem, IFxConverter? fx = null)
    {
        var converter = fx ?? new FakeFxConverter();
        // One resolver for the whole graph: it caches the period start day per instance, and
        // the setting cannot change inside one request anyway.
        var periods = new BudgetPeriodResolver(mem.Db);
        var debts = new DebtLedger(mem.Db, periods);
        var budget = new MonthlyBudget(mem.Db, periods, debts);
        var allocations = new AllocationService(mem.Db);

        return new SummaryService(
            mem.Db, converter,
            new RecurringMaterializer(mem.Db, converter, new BudgetPeriodResolver(mem.Db)),
            budget,
            new EnvelopeService(
                mem.Db, allocations, periods, converter, debts, NullLogger<EnvelopeService>.Instance),
            allocations,
            new MoneyViewFactory(mem.Db, converter),
            periods,
            new CarryoverService(mem.Db, periods, budget, NullLogger<CarryoverService>.Instance),
            debts);
    }
}

/// An FX converter whose dollar rate can be moved between two reads. Everything else behaves
/// like <see cref="FakeFxConverter"/>; the point is only that a rate is not the same number
/// twice, which is the one condition under which re-converting a written charge is visible.
public sealed class MovingRateFxConverter(decimal plnPerUsd) : IFxConverter
{
    public decimal PlnPerUsd { get; set; } = plnPerUsd;

    public Task<Result<FxConversion>> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        var rate = Rate(currency);
        return Task.FromResult(Result<FxConversion>.Ok(
            new FxConversion(Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero), rate, date)));
    }

    public Task<Result<FxConversion>> ConvertFromBaseAsync(
        decimal baseAmount, string currency, DateOnly date, CancellationToken ct = default)
    {
        var rate = Rate(currency);
        return Task.FromResult(Result<FxConversion>.Ok(
            new FxConversion(Math.Round(baseAmount / rate, 2, MidpointRounding.AwayFromZero), rate, date)));
    }

    private decimal Rate(string currency) => currency.ToUpperInvariant() == "USD" ? PlnPerUsd : 1m;
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
