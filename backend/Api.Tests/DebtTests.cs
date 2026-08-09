using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// Debts, both ways round.
///
/// The half that had no home in the app at all is money coming BACK. It arrives, but it is not
/// earnings: run it through the Polish tax engine and it is charged VAT, ZUS and PIT on money
/// that was the user's before they ever lent it out. So a debt payment is not a transaction,
/// and every figure it belongs in has to ask the ledger for it — which is what most of these
/// tests are really checking.
public class DebtTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static DebtLedger Ledger(SqliteInMemory mem) =>
        new(mem.Db, new BudgetPeriodResolver(mem.Db));

    private static EnvelopeService Envelopes(SqliteInMemory mem) =>
        new(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db),
            new FakeFxConverter(), Ledger(mem), NullLogger<EnvelopeService>.Instance);

    private static DebtService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new DebtService(
            mem.Db, fx, new BudgetPeriodResolver(mem.Db), Ledger(mem),
            Envelopes(mem), new MoneyViewFactory(mem.Db, fx), NullLogger<DebtService>.Instance);
    }

    private static SummaryService Summary(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        var periods = new BudgetPeriodResolver(mem.Db);
        return new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db, periods, Ledger(mem)),
            Envelopes(mem),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx),
            periods,
            new CarryoverService(
                mem.Db, periods, new MonthlyBudget(mem.Db, periods, Ledger(mem)),
                NullLogger<CarryoverService>.Instance),
            Ledger(mem));
    }

    private static SaveDebtRequest Owed(
        decimal amount, DateOnly? deadline = null, bool reserve = false) =>
        new("IOwe", "Сергій", amount, null, Today, deadline, reserve, null);

    private static SaveDebtRequest Lent(decimal amount) =>
        new("TheyOweMe", "Оля", amount, null, Today, null, false, null);

    private static SaveDebtPaymentRequest Pays(
        decimal amount, string source = "Spendable", int? envelopeId = null) =>
        new(amount, null, Today, source, envelopeId, null);

    private static async Task IncomeAsync(SqliteInMemory mem, decimal amount = 5_000m)
    {
        mem.Db.Transactions.Add(Income(amount));
        await mem.Db.SaveChangesAsync();
    }

    private static async Task<Envelope> JarAsync(SqliteInMemory mem, decimal balance)
    {
        var jar = new Envelope { Name = "Подушка", Kind = BucketKind.Savings, IsDefault = true };
        mem.Db.Envelopes.Add(jar);
        await mem.Db.SaveChangesAsync();

        mem.Db.SavingsEntries.Add(new SavingsEntry
        {
            EnvelopeId = jar.Id, Date = Today, Kind = SavingsEntryKind.Deposit,
            CurrencyOriginal = "PLN", AmountOriginal = balance, AmountBase = balance,
            FxRate = 1m, FxDate = Today, AlreadySetAside = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();
        return jar;
    }

    /// Paying somebody back is spending. The money is gone, and a daily norm that did not feel
    /// it would be describing an account the user no longer has.
    [Fact]
    public async Task Repaying_out_of_spendable_money_lowers_what_is_left()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var before = await Summary(mem).GetSafeToSpendAsync();
        var debt = await Sut(mem).AddAsync(Owed(1_000m));
        await Sut(mem).AddPaymentAsync(debt.Value!.IOwe.Single().Id, Pays(400m));

        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod - 400m, after.RemainingThisPeriod);
        Assert.Equal(before.SpentToday + 400m, after.SpentToday);
    }

    /// Money taken out of a jar was held back from the norm when it went in. Charging for it
    /// again as it leaves takes the same złoty twice — the mistake the "already set aside"
    /// deposits were written to stop, arriving through a different door.
    [Fact]
    public async Task Repaying_out_of_a_jar_costs_the_period_nothing_and_empties_the_jar()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);
        var jar = await JarAsync(mem, 2_000m);

        var before = await Summary(mem).GetSafeToSpendAsync();
        var debt = await Sut(mem).AddAsync(Owed(1_000m));
        var paid = await Sut(mem).AddPaymentAsync(
            debt.Value!.IOwe.Single().Id, Pays(600m, "Envelope", jar.Id));

        Assert.True(paid.IsSuccess);

        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod, after.RemainingThisPeriod);
        Assert.Equal(1_400m, after.Envelopes.Single(e => e.Id == jar.Id).Balance);
    }

    /// A jar cannot be emptied twice, however the money leaves it.
    [Fact]
    public async Task A_jar_cannot_pay_out_more_than_it_holds()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);
        var jar = await JarAsync(mem, 300m);

        var debt = await Sut(mem).AddAsync(Owed(1_000m));
        var paid = await Sut(mem).AddPaymentAsync(
            debt.Value!.IOwe.Single().Id, Pays(600m, "Envelope", jar.Id));

        Assert.False(paid.IsSuccess);
    }

    /// A repayment made months ago and only written down today never left this period.
    [Fact]
    public async Task Writing_down_a_repayment_that_already_happened_costs_the_period_nothing()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var before = await Summary(mem).GetSafeToSpendAsync();
        var debt = await Sut(mem).AddAsync(Owed(1_000m));
        await Sut(mem).AddPaymentAsync(debt.Value!.IOwe.Single().Id, Pays(400m, "AlreadyHappened"));

        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod, after.RemainingThisPeriod);
        // It still counts against the debt: the money really did go.
        Assert.Equal(600m, (await Sut(mem).GetAsync()).IOwe.Single().Outstanding);
    }

    /// The heart of it. Money coming back joins the budget whole — it is not revenue, and the
    /// tax engine must never see it. Booked as income, 1 000 back would arrive as a few hundred
    /// after VAT, ZUS and PIT, and the app would quietly keep the difference.
    [Fact]
    public async Task Money_coming_back_raises_the_budget_by_all_of_itself_and_is_not_taxed()
    {
        using var mem = new SqliteInMemory();
        mem.Db.TaxProfiles.Add(new TaxProfile
        {
            VatPayer = true, VatRate = 0.23m, Regime = TaxRegime.Ryczalt, RyczaltRate = 0.12m,
        });
        await IncomeAsync(mem);

        var before = await Summary(mem).GetSafeToSpendAsync();
        var debt = await Sut(mem).AddAsync(Lent(1_000m));
        await Sut(mem).AddPaymentAsync(debt.Value!.TheyOweMe.Single().Id, Pays(1_000m));

        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Equal(before.PeriodBudget + 1_000m, after.PeriodBudget);
        // And the tax split is untouched: nothing here was revenue.
        Assert.Equal(before.MonthTaxes!.SetAside, after.MonthTaxes!.SetAside);
    }

    /// Off by default. Entering an old debt must not drop the daily norm through the floor for
    /// money the user is not paying back this month.
    [Fact]
    public async Task A_debt_holds_nothing_back_unless_asked_to()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var before = await Summary(mem).GetSafeToSpendAsync();
        await Sut(mem).AddAsync(Owed(1_200m, Today.AddMonths(3)));

        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod, after.RemainingThisPeriod);
        Assert.Equal(0m, after.ReservedDebts);
    }

    /// Asked to, it holds back this period's share — and says how much, so the money missing
    /// from the norm has something on screen explaining it.
    [Fact]
    public async Task A_debt_with_a_deadline_holds_back_its_share_of_the_period()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var before = await Summary(mem).GetSafeToSpendAsync();
        await Sut(mem).AddAsync(Owed(1_000m, Today.AddDays(20), reserve: true));

        var after = await Summary(mem).GetSafeToSpendAsync();

        // The deadline falls inside this period or the next one, so the whole of it is due
        // now: what matters here is that something is held back and reported.
        Assert.True(after.ReservedDebts > 0m);
        Assert.Equal(before.RemainingThisPeriod - after.ReservedDebts, after.RemainingThisPeriod);
    }

    /// The trap this feature could easily have walked into: paying out of spendable money AND
    /// reserving for the same debt would charge the daily norm twice for one obligation.
    [Fact]
    public async Task Paying_this_period_counts_towards_what_the_period_had_to_reserve()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var added = await Sut(mem).AddAsync(Owed(1_000m, Today.AddDays(20), reserve: true));
        var reservedBefore = added.Value!.ReservedThisPeriod;

        var paid = await Sut(mem).AddPaymentAsync(
            added.Value.IOwe.Single().Id, Pays(reservedBefore));

        Assert.True(paid.IsSuccess);
        Assert.Equal(0m, paid.Value!.ReservedThisPeriod);
    }

    /// A switch that is on and does nothing is worse than one that explains itself.
    [Fact]
    public async Task Reserving_needs_a_deadline_to_divide_by()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var r = await Sut(mem).AddAsync(Owed(1_000m, deadline: null, reserve: true));

        Assert.False(r.IsSuccess);
    }

    /// Money owed TO the user is not money to set aside — there is nothing of theirs to hold.
    [Fact]
    public async Task Money_owed_to_the_user_cannot_be_reserved_for()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var r = await Sut(mem).AddAsync(
            new SaveDebtRequest("TheyOweMe", "Оля", 500m, null, Today, Today.AddMonths(1), true, null));

        Assert.False(r.IsSuccess);
    }

    /// Money coming back is arriving, not leaving: it cannot be taken out of a jar.
    [Fact]
    public async Task Money_coming_back_cannot_be_taken_out_of_a_jar()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);
        var jar = await JarAsync(mem, 1_000m);

        var debt = await Sut(mem).AddAsync(Lent(500m));
        var r = await Sut(mem).AddPaymentAsync(
            debt.Value!.TheyOweMe.Single().Id, Pays(200m, "Envelope", jar.Id));

        Assert.False(r.IsSuccess);
    }

    /// Overpaying would leave the debt owing money back, which is not a thing the screen can say.
    [Fact]
    public async Task A_debt_cannot_be_paid_past_zero()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var debt = await Sut(mem).AddAsync(Owed(500m));
        var id = debt.Value!.IOwe.Single().Id;

        Assert.True((await Sut(mem).AddPaymentAsync(id, Pays(500m))).IsSuccess);
        Assert.False((await Sut(mem).AddPaymentAsync(id, Pays(1m))).IsSuccess);
    }

    /// Payments mean nothing without the debt. Left behind, they would go on being counted as
    /// spending on something that no longer exists.
    [Fact]
    public async Task Deleting_a_debt_takes_its_payments_with_it()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var debt = await Sut(mem).AddAsync(Owed(500m));
        var id = debt.Value!.IOwe.Single().Id;
        await Sut(mem).AddPaymentAsync(id, Pays(100m));

        var before = await Summary(mem).GetSafeToSpendAsync();
        await Sut(mem).DeleteAsync(id);
        var after = await Summary(mem).GetSafeToSpendAsync();

        Assert.Empty(await mem.Db.DebtPayments.ToListAsync());
        Assert.Equal(before.RemainingThisPeriod + 100m, after.RemainingThisPeriod);
    }

    /// Debts get forgiven and rounded off. An app that only closed them when the sums came out
    /// even would leave a list of settled business nobody can clear.
    [Fact]
    public async Task A_debt_can_be_called_finished_with_money_still_on_it()
    {
        using var mem = new SqliteInMemory();
        await IncomeAsync(mem);

        var debt = await Sut(mem).AddAsync(Owed(500m));
        var closed = await Sut(mem).SetClosedAsync(debt.Value!.IOwe.Single().Id, true);

        Assert.True(closed.IsSuccess);
        Assert.NotNull(closed.Value!.IOwe.Single().ClosedOn);
        // Closed debts leave the totals; they are history, not an obligation.
        Assert.Equal(0m, closed.Value.IOweTotal);
    }
}
