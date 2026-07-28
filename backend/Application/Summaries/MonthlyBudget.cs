using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Summaries;

/// <param name="Budget">Money available over the window, already after tax.</param>
/// <param name="WindowStart">The day spending is counted from — the 1st, or the day an
/// opening balance was taken. Everything before it is none of the app's business.</param>
/// <param name="FromOpeningBalance">True when the figure came from "how much I have right
/// now" rather than from income or a set budget. The UI says so instead of showing a month
/// summary whose arithmetic the user cannot check.</param>
public record MonthlyBudgetResult(
    decimal? Budget,
    TakeHomeBreakdown? Taxes,
    DateOnly WindowStart,
    bool FromOpeningBalance);

public interface IMonthlyBudget
{
    Task<MonthlyBudgetResult> ResolveAsync(CancellationToken ct = default);
}

/// This month's budget: income after tax, or the manually set amount when there is no income.
/// Extracted so the summary and the savings goal (which can be a % of take-home) agree —
/// two places deriving take-home separately is how the numbers start to drift.
public sealed class MonthlyBudget(IAppDbContext db) : IMonthlyBudget
{
    public async Task<MonthlyBudgetResult> ResolveAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (first, last) = MonthRange.Of(today);

        // An opening balance taken earlier this month wins over everything: whatever the
        // income was, what the user actually HAS is the only number that can be divided over
        // the days that are left. It expires by itself — next month there is no row in range.
        var opening = await db.OpeningBalances
            .Where(x => x.Date >= first && x.Date <= today)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (opening is not null)
        {
            // Income that landed AFTER the count is money the user did not have when they
            // counted, so it is added on top. Taxes on it are computed over that income
            // alone — slightly off if an earlier invoice in the same month already carried
            // the flat ZUS, but this only ever affects the first partial month.
            var (later, laterTaxes) = await TakeHomeAsync(opening.Date.AddDays(1), last, ct);
            return new MonthlyBudgetResult(
                opening.AmountBase + later, laterTaxes, opening.Date, true);
        }

        var (takeHome, taxes) = await TakeHomeAsync(first, last, ct);
        if (takeHome > 0) return new MonthlyBudgetResult(takeHome, taxes, first, false);

        var manual = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return new MonthlyBudgetResult(manual?.MonthlyAmount, null, first, false);
    }

    /// Revenue over a date range, run through the tax profile. Zero when there is no income.
    private async Task<(decimal TakeHome, TakeHomeBreakdown? Taxes)> TakeHomeAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Income rows store revenue (przychód, VAT excluded) in AmountBase.
        var revenue = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && t.Kind == TransactionKind.Income)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        if (revenue <= 0) return (0m, null);

        var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (profile is null) return (revenue, null); // no profile to tax it with

        // Taxes are MONTHLY in Poland, so they are applied once to the range's total
        // revenue — never per invoice, which would double-count contributions.
        var take = TakeHomeCalculator.Calculate(profile, revenue, amountIncludesVat: false);
        return take.IsSuccess ? (take.Value!.TakeHome, take.Value) : (revenue, null);
    }
}
