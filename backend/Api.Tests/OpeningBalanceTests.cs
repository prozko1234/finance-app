using FinanceApp.Application.Common;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// Starting the app mid-month. The trap being guarded against: spreading a WHOLE month's
/// budget over the days that are left, while the money spent before install is invisible —
/// the daily norm then promises money that is already gone.
public class OpeningBalanceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DateOnly First = MonthRange.Of(Today).First;

    private static Transaction Income(decimal amount, DateOnly date) => new()
    {
        Kind = TransactionKind.Income, CurrencyOriginal = "PLN", AmountOriginal = amount,
        AmountBase = amount, FxRate = 1m, FxDate = date, Date = date, CategoryId = 1,
    };

    [Fact]
    public async Task Without_an_opening_balance_the_window_is_the_whole_month()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Budgets.Add(new Budget { MonthlyAmount = 6000m });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(6000m, r.Budget);
        Assert.Equal(First, r.WindowStart);
        Assert.False(r.FromOpeningBalance);
    }

    [Fact]
    public async Task Opening_balance_replaces_the_budget_and_moves_the_window_start()
    {
        using var sut = new SqliteInMemory();
        // A set budget exists — the count of what is actually in the account still wins:
        // half of that budget may already have been spent before the app existed.
        sut.Db.Budgets.Add(new Budget { MonthlyAmount = 6000m });
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, AmountOriginal = 1800m, CurrencyOriginal = "PLN", AmountBase = 1800m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(1800m, r.Budget);
        Assert.Equal(Today, r.WindowStart);
        Assert.True(r.FromOpeningBalance);
    }

    [Fact]
    public async Task Income_that_lands_after_the_count_is_added_on_top()
    {
        using var sut = new SqliteInMemory();
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = First, AmountOriginal = 1000m, CurrencyOriginal = "PLN", AmountBase = 1000m,
        });
        // Salary arriving later is money that was not in the account when it was counted.
        sut.Db.Transactions.Add(Income(5000m, First.AddDays(1)));
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(6000m, r.Budget);
    }

    [Fact]
    public async Task Income_already_in_the_account_when_it_was_counted_is_not_added_twice()
    {
        using var sut = new SqliteInMemory();
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = First, AmountOriginal = 1000m, CurrencyOriginal = "PLN", AmountBase = 1000m,
        });
        sut.Db.Transactions.Add(Income(5000m, First)); // same day as the count
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(1000m, r.Budget);
    }

    [Fact]
    public async Task Last_months_count_expires_instead_of_steering_this_month()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Budgets.Add(new Budget { MonthlyAmount = 6000m });
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = First.AddDays(-1), AmountOriginal = 200m, CurrencyOriginal = "PLN", AmountBase = 200m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(6000m, r.Budget); // not 200 — the ordinary month is back
        Assert.Equal(First, r.WindowStart);
        Assert.False(r.FromOpeningBalance);
    }

    [Fact]
    public async Task A_count_dated_in_the_future_is_ignored()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Budgets.Add(new Budget { MonthlyAmount = 6000m });
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today.AddDays(1), AmountOriginal = 200m, CurrencyOriginal = "PLN", AmountBase = 200m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db).ResolveAsync();

        Assert.Equal(6000m, r.Budget);
    }
}
