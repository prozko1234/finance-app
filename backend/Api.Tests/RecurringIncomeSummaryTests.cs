using static FinanceApp.Api.Tests.TestIncome;
using FinanceApp.Application.Common;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Display;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Api.Tests;

/// A recurring salary must never be reserved the way a subscription is. Reserving it
/// would subtract the salary from what the user may spend — the exact opposite of what
/// income does to the budget.
public class RecurringIncomeSummaryTests
{
    [Fact]
    public async Task Recurring_income_is_not_reserved_like_a_subscription()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day >= 28) return; // needs a due date still ahead in this month

        var category = new Category { Name = "Дохід" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Income,
            AmountOriginal = 9_000m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            StartsOn = today.AddDays(2),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Expense,
            AmountOriginal = 50m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            StartsOn = today.AddDays(2),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var fx = new FakeFxConverter();
        var sut = new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, NullLogger<EnvelopeService>.Instance),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx),
            new BudgetPeriodResolver(mem.Db));

        var r = await sut.GetSafeToSpendAsync();

        Assert.Equal(50m, r.ReservedRecurring); // the subscription only
    }

    /// A weekly charge falls due four or five times inside one period. Reserving only the
    /// next one would leave the daily norm promising money that three more charges have
    /// already claimed — the exact failure the reserve exists to prevent.
    [Fact]
    public async Task A_weekly_charge_is_reserved_for_every_time_it_falls_due_this_period()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var category = new Category { Name = "Їжа" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Expense,
            AmountOriginal = 100m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            StartsOn = today.AddDays(1),
            Unit = RecurrenceUnit.Week,
            Interval = 1,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var fx = new FakeFxConverter();
        var sut = new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, NullLogger<EnvelopeService>.Instance),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx),
            new BudgetPeriodResolver(mem.Db));

        var r = await sut.GetSafeToSpendAsync();

        // Counted from the schedule rather than hard-coded: the period is the calendar month
        // by default, so how many Mondays are left depends on the day the test runs.
        var end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var due = FinanceApp.Domain.Budgeting.RecurringSchedule
            .Occurrences(today.AddDays(1), RecurrenceUnit.Week, 1, today.AddDays(1), end)
            .Count();

        Assert.Equal(due * 100m, r.ReservedRecurring);
        Assert.True(due >= 1);
    }
}
