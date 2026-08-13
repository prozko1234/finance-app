using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Debts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Common;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Display;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Domain.Common;

namespace FinanceApp.Api.Tests;

/// Movements of the envelope: money can arrive in any currency, and a mistyped entry
/// must be correctable without deleting it. Both paths write the balance, so both are
/// tested against the same invariant — the balance is the sum of what is stored.
public class SavingsEntryTests
{
    private static SavingsService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))), fx, new AllocationService(mem.Db),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance), new MoneyViewFactory(mem.Db, fx), NullLogger<SavingsService>.Instance);
    }

    private static SaveSavingsEntryRequest Deposit(decimal amount, string? currency = null) =>
        new("Deposit", amount, null, null, currency);

    [Fact]
    public async Task Deposit_in_another_currency_is_converted_but_remembers_what_was_typed()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(100m, "USD"));

        Assert.True(r.IsSuccess);
        Assert.Equal(400m, r.Value!.Balance); // 100 USD at the test rate of 4.0
        var entry = Assert.Single(r.Value.Recent);
        Assert.Equal(100m, entry.AmountOriginal);
        Assert.Equal("USD", entry.CurrencyOriginal);
        Assert.Equal(400m, entry.Amount);
    }

    [Fact]
    public async Task Currency_defaults_to_base_when_omitted()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(250m));

        Assert.Equal("PLN", Assert.Single(r.Value!.Recent).CurrencyOriginal);
        Assert.Equal(250m, r.Value.Balance);
    }

    [Fact]
    public async Task A_currency_with_no_rate_is_refused_rather_than_stored_wrong()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(100m, "JPY"));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Unsupported, r.Error.Type);
    }

    [Fact]
    public async Task Editing_a_deposit_moves_the_balance_to_the_new_amount()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        var added = await sut.AddEntryAsync(Deposit(800m));
        var id = added.Value!.Recent[0].Id;

        var r = await sut.UpdateEntryAsync(id, new SaveSavingsEntryRequest("Deposit", 500m, null, "виправив", null));

        Assert.True(r.IsSuccess);
        Assert.Equal(500m, r.Value!.Balance);
        Assert.Equal("виправив", Assert.Single(r.Value.Recent).Note);
    }

    /// The trap this guards: the row being edited is already in the balance, so a naive
    /// check would refuse to correct a withdrawal that is fine once itself is excluded.
    [Fact]
    public async Task A_withdrawal_can_be_corrected_against_the_balance_without_itself()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        await sut.AddEntryAsync(Deposit(1000m));
        var w = await sut.AddEntryAsync(new SaveSavingsEntryRequest("Withdrawal", 900m, null, null, null));
        var id = w.Value!.Recent[0].Id;

        // 950 exceeds the current balance of 100, but not the 1000 available without this row.
        var r = await sut.UpdateEntryAsync(id, new SaveSavingsEntryRequest("Withdrawal", 950m, null, null, null));

        Assert.True(r.IsSuccess);
        Assert.Equal(50m, r.Value!.Balance);
    }

    [Fact]
    public async Task Withdrawing_more_than_there_is_still_fails()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        await sut.AddEntryAsync(Deposit(100m));

        var r = await sut.AddEntryAsync(new SaveSavingsEntryRequest("Withdrawal", 300m, null, null, null));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
    }

    /// Money leaves a jar in three ways, and only one of them is a withdrawal. This check used
    /// to count deposits minus withdrawals alone, so a jar that had already been spent from
    /// still offered that money to be taken out — and then showed the balance it really had,
    /// in minus. The screen was right and the check was wrong, which is the worst way round.
    [Fact]
    public async Task What_was_already_spent_out_of_the_jar_cannot_be_withdrawn_again()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        var jar = (await sut.AddEntryAsync(Deposit(1_000m))).Value!.Envelopes[0];

        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense, EnvelopeId = jar.Id,
            CategoryId = mem.Db.Categories.OrderBy(c => c.Id).First().Id,
            CurrencyOriginal = "PLN", AmountOriginal = 800m, AmountBase = 800m,
            FxRate = 1m, FxDate = Today, Date = Today, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await sut.AddEntryAsync(
            new SaveSavingsEntryRequest("Withdrawal", 500m, null, null, EnvelopeId: jar.Id));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
        // The 200 that really is in there still comes out.
        Assert.True((await sut.AddEntryAsync(
            new SaveSavingsEntryRequest("Withdrawal", 200m, null, null, EnvelopeId: jar.Id))).IsSuccess);
    }

    /// The same hole, through the other door: a debt repaid out of the jar is not a withdrawal
    /// either, and the money is just as gone.
    [Fact]
    public async Task What_a_debt_took_out_of_the_jar_cannot_be_withdrawn_again()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        var jar = (await sut.AddEntryAsync(Deposit(1_000m))).Value!.Envelopes[0];

        var debt = new Debt
        {
            Direction = DebtDirection.IOwe, Person = "Сергій",
            CurrencyOriginal = "PLN", AmountOriginal = 800m, AmountBase = 800m,
            FxRate = 1m, FxDate = Today, Date = Today, CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.Debts.Add(debt);
        await mem.Db.SaveChangesAsync();

        mem.Db.DebtPayments.Add(new DebtPayment
        {
            DebtId = debt.Id, Date = Today, Source = MoneySource.Envelope, EnvelopeId = jar.Id,
            CurrencyOriginal = "PLN", AmountOriginal = 800m, AmountBase = 800m,
            FxRate = 1m, FxDate = Today, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await sut.AddEntryAsync(
            new SaveSavingsEntryRequest("Withdrawal", 500m, null, null, EnvelopeId: jar.Id));

        Assert.False(r.IsSuccess);
    }

    /// A transfer empties a jar exactly as a withdrawal does, so it is checked against the same
    /// figure — otherwise the money that could not be withdrawn could still be moved next door.
    [Fact]
    public async Task A_transfer_cannot_move_money_the_jar_no_longer_has()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        var jar = (await sut.AddEntryAsync(Deposit(1_000m))).Value!.Envelopes[0];

        var other = await new EnvelopeService(
            mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db),
            new FakeFxConverter(), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)),
            NullLogger<EnvelopeService>.Instance).CreateAsync("Відпустка", BucketKind.Savings);

        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense, EnvelopeId = jar.Id,
            CategoryId = mem.Db.Categories.OrderBy(c => c.Id).First().Id,
            CurrencyOriginal = "PLN", AmountOriginal = 800m, AmountBase = 800m,
            FxRate = 1m, FxDate = Today, Date = Today, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await sut.TransferAsync(new TransferRequest(jar.Id, other.Value!.Id, 500m, null, null, null));

        Assert.False(r.IsSuccess);
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public async Task Editing_a_missing_entry_is_a_not_found()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).UpdateEntryAsync(404, Deposit(10m));

        Assert.Equal(ErrorType.NotFound, r.Error.Type);
    }
}
