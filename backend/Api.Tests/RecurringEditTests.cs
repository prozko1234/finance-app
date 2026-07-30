using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// Editing a subscription instead of deleting it and typing it again. The PUT existed all
/// along; what did not exist was a way to reach it — and one sharp edge on the way.
public class RecurringEditTests
{
    private static RecurringService Sut(SqliteInMemory mem) =>
        new(mem.Db, new BudgetPeriodResolver(mem.Db));

    private static async Task<RecurringExpense> SalaryAsync(SqliteInMemory mem)
    {
        var category = await mem.Db.Categories.FirstAsync();
        var salary = new RecurringExpense
        {
            Kind = TransactionKind.Income,
            AmountOriginal = 20_000m,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            DayOfMonth = 10,
            Active = true,
            Note = "Зарплата",
            AmountIncludesVat = true,
        };
        mem.Db.RecurringExpenses.Add(salary);
        await mem.Db.SaveChangesAsync();
        return salary;
    }

    /// Pausing a recurring income used to send everything EXCEPT the kind — and the update
    /// filled that blank with the creation default. A paused salary came back as a
    /// subscription: the month quietly lost an income and gained a charge.
    [Fact]
    public async Task An_update_that_says_nothing_about_the_kind_keeps_the_one_it_had()
    {
        using var mem = new SqliteInMemory();
        var salary = await SalaryAsync(mem);

        var paused = await Sut(mem).UpdateAsync(salary.Id, new SaveRecurringRequest(
            salary.AmountOriginal, "PLN", salary.CategoryId, salary.DayOfMonth, salary.Note, Active: false));

        Assert.True(paused.IsSuccess);
        var row = await mem.Db.RecurringExpenses.FindAsync(salary.Id);
        Assert.Equal(TransactionKind.Income, row!.Kind);
        Assert.False(row.Active);
    }

    [Fact]
    public async Task A_correction_changes_the_figures_and_leaves_the_rest_alone()
    {
        using var mem = new SqliteInMemory();
        var salary = await SalaryAsync(mem);

        await Sut(mem).UpdateAsync(salary.Id, new SaveRecurringRequest(
            21_000m, "PLN", salary.CategoryId, 12, "Зарплата (нова ставка)", Active: true,
            Kind: "Income", AmountIncludesVat: true));

        var row = await mem.Db.RecurringExpenses.FindAsync(salary.Id);
        Assert.Equal(21_000m, row!.AmountOriginal);
        Assert.Equal(12, row.DayOfMonth);
        Assert.Equal("Зарплата (нова ставка)", row.Note);
        Assert.Equal(TransactionKind.Income, row.Kind);
    }
}
