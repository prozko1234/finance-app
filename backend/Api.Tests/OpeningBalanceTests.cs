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
    private static readonly DateOnly First = BudgetPeriods.For(Today, BudgetPeriods.FirstOfMonth).Start;

    private static Transaction Income(decimal amount, DateOnly date) => new()
    {
        Kind = TransactionKind.Income, CurrencyOriginal = "PLN", AmountOriginal = amount,
        AmountBase = amount, FxRate = 1m, FxDate = date, Date = date, CategoryId = 1,
    };

    [Fact]
    public async Task Without_an_opening_balance_the_window_is_the_whole_month()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Transactions.Add(Income(6000m, Today));
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

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
        sut.Db.Transactions.Add(Income(6000m, Today));
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, AmountOriginal = 1800m, CurrencyOriginal = "PLN", AmountBase = 1800m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

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

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

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

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

        Assert.Equal(1000m, r.Budget);
    }

    /// Count your balance in the morning, get paid in the afternoon. The salary used to be
    /// dropped for the whole period, on the assumption that a same-day income was already
    /// inside the counted figure — which left the app with no budget at all.
    [Fact]
    public async Task Income_recorded_after_the_count_counts_even_on_the_same_day()
    {
        using var sut = new SqliteInMemory();
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, AmountOriginal = 741.69m, CurrencyOriginal = "PLN", AmountBase = 741.69m,
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        var salary = Income(26_000m, Today);
        salary.CreatedAt = DateTimeOffset.UtcNow;
        sut.Db.Transactions.Add(salary);
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

        Assert.Equal(26_741.69m, r.Budget);
    }

    /// The other half of the same day: money already on the account when it was counted is
    /// inside that figure, and adding it again would double it.
    [Fact]
    public async Task Income_recorded_before_the_count_stays_inside_it()
    {
        using var sut = new SqliteInMemory();
        var salary = Income(26_000m, Today);
        salary.CreatedAt = DateTimeOffset.UtcNow.AddHours(-2);
        sut.Db.Transactions.Add(salary);
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, AmountOriginal = 741.69m, CurrencyOriginal = "PLN", AmountBase = 741.69m,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

        Assert.Equal(741.69m, r.Budget);
    }

    [Fact]
    public async Task Last_months_count_expires_instead_of_steering_this_month()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Transactions.Add(Income(6000m, Today));
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = First.AddDays(-1), AmountOriginal = 200m, CurrencyOriginal = "PLN", AmountBase = 200m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

        Assert.Equal(6000m, r.Budget); // not 200 — the ordinary month is back
        Assert.Equal(First, r.WindowStart);
        Assert.False(r.FromOpeningBalance);
    }

    [Fact]
    public async Task A_count_dated_in_the_future_is_ignored()
    {
        using var sut = new SqliteInMemory();
        sut.Db.Transactions.Add(Income(6000m, Today));
        sut.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today.AddDays(1), AmountOriginal = 200m, CurrencyOriginal = "PLN", AmountBase = 200m,
        });
        await sut.Db.SaveChangesAsync();

        var r = await new MonthlyBudget(sut.Db, new BudgetPeriodResolver(sut.Db)).ResolveAsync();

        Assert.Equal(6000m, r.Budget);
    }
}
