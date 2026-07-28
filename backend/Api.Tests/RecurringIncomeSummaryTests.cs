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
        mem.Db.Budgets.Add(new Budget { MonthlyAmount = 5_000m, UpdatedAt = DateTimeOffset.UtcNow });
        await mem.Db.SaveChangesAsync();

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Income,
            AmountOriginal = 9_000m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            DayOfMonth = today.Day + 2,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Expense,
            AmountOriginal = 50m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            DayOfMonth = today.Day + 2,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var fx = new FakeFxConverter();
        var sut = new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db)),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx));

        var r = await sut.GetSafeToSpendAsync();

        Assert.Equal(50m, r.ReservedRecurring); // the subscription only
    }
}
