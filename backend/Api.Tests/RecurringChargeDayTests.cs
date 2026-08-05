using static FinanceApp.Api.Tests.TestIncome;
using FinanceApp.Application.Common;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Display;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// The day a subscription falls due. Its money was set aside at the start of the period and
/// has been missing from the daily norm ever since — so the day it actually leaves must be an
/// ordinary day, not a day the user is told they overspent.
///
/// The bug these tests pin: the charge materializes as a transaction dated today, and "spent
/// today" counted it like a purchase. The user got a minus for a decision they never made,
/// and the norm itself jumped, because the calculator adds today's spending back when it
/// works out what the day started with.
public class RecurringChargeDayTests
{
    [Fact]
    public async Task A_subscription_charging_today_does_not_eat_todays_norm()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionDueOnAsync(mem, 100m, today);

        var r = await Sut(mem).GetSafeToSpendAsync();

        // It really did charge, and the period figure carries it.
        Assert.Equal(100m, r.SpentThisPeriod);
        Assert.Equal(0m, r.ReservedRecurring); // moved from reserved to spent

        // But the day is untouched: nothing was chosen today, so nothing is spent today.
        Assert.Equal(0m, r.SpentToday);
        Assert.Equal(r.DailyNorm, r.LeftToday);
    }

    /// The other half of the same rule: a real purchase on the charge day still counts, and
    /// counts once. Excluding the subscription must not excuse the coffee.
    [Fact]
    public async Task A_real_purchase_on_the_charge_day_still_counts()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (_, categoryId) = await SubscriptionDueOnAsync(mem, 100m, today);

        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense,
            CurrencyOriginal = "PLN", AmountOriginal = 40m, AmountBase = 40m,
            FxRate = 1m, FxDate = today, Date = today, CategoryId = categoryId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(40m, r.SpentToday);
        Assert.Equal(140m, r.SpentThisPeriod); // the coffee and the subscription
        Assert.Equal(r.DailyNorm - 40m, r.LeftToday);
    }

    /// The reserve is a promise that a transaction is coming. The user can delete a single
    /// occurrence, and then none ever comes — the materializer honours the skip. If the
    /// reserve does not honour it too, the money is held back for the rest of the period and
    /// the daily norm is quietly lower with nothing on screen saying why.
    [Fact]
    public async Task A_deleted_occurrence_stops_being_reserved()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day >= 26) return; // needs a due date still ahead in this period

        using var mem = new SqliteInMemory();
        var due = today.AddDays(2);
        var (id, _) = await SubscriptionDueOnAsync(mem, 100m, due);

        // Control: without the skip the charge is reserved, so the assertion below is not
        // passing for the trivial reason that nothing was ever counted.
        Assert.Equal(100m, (await Sut(mem).GetSafeToSpendAsync()).ReservedRecurring);

        mem.Db.RecurringSkips.Add(new RecurringSkip
        {
            RecurringExpenseId = id, Date = due, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(0m, r.ReservedRecurring);
    }

    /// Income, and a monthly subscription first charged on the given date. Both ids come back:
    /// the recurring one to skip an occurrence, the category one to hang an ordinary purchase
    /// off the same category.
    private static async Task<(int Recurring, int Category)> SubscriptionDueOnAsync(
        SqliteInMemory mem, decimal amount, DateOnly due)
    {
        var category = new Category { Name = "Підписки" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();

        var r = new RecurringExpense
        {
            Kind = TransactionKind.Expense,
            AmountOriginal = amount,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            StartsOn = due,
            Unit = RecurrenceUnit.Month,
            Interval = 1,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.RecurringExpenses.Add(r);
        await mem.Db.SaveChangesAsync();
        return (r.Id, category.Id);
    }

    private static SummaryService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, NullLogger<EnvelopeService>.Instance),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx),
            new BudgetPeriodResolver(mem.Db),
            new CarryoverService(
                mem.Db, new BudgetPeriodResolver(mem.Db),
                new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)),
                NullLogger<CarryoverService>.Instance));
    }
}
