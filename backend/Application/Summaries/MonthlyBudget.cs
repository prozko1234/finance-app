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
public sealed class MonthlyBudget(IAppDbContext db, IBudgetPeriods periods) : IMonthlyBudget
{
    public async Task<MonthlyBudgetResult> ResolveAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (first, last) = await periods.CurrentAsync(ct);

        // An opening balance taken earlier this period wins over everything: whatever the
        // income was, what the user actually HAS is the only number that can be divided over
        // the days that are left. It expires by itself — next period there is no row in range.
        var opening = await db.OpeningBalances
            .Where(x => x.Date >= first && x.Date <= today)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (opening is not null)
        {
            // Income that landed AFTER the count is money the user did not have when they
            // counted, so it is added on top. Taxes on it are computed over that income
            // alone — slightly off if an earlier invoice in the same month already carried
            // the flat ZUS, but this only ever affects the first partial period.
            //
            // Income on the COUNT'S OWN DAY used to be dropped entirely, on the assumption
            // that it was already inside the counted figure. That assumption is wrong half
            // the time and it is expensive: count your balance in the morning, get paid in
            // the afternoon, and the salary vanishes from the app for the rest of the
            // period. The row's timestamp settles it without asking — money recorded after
            // the count was made is money that came after it.
            var (later, laterTaxes) = await TakeHomeAsync(opening.Date, last, opening.UpdatedAt, ct);
            return new MonthlyBudgetResult(
                opening.AmountBase + later, laterTaxes, opening.Date, true);
        }

        var (takeHome, taxes) = await TakeHomeAsync(first, last, null, ct);

        // No income and no count = no budget, and the app says so instead of inventing one.
        // There used to be a "запасний бюджет" here: a monthly amount typed once in settings
        // that quietly took over whenever income was missing. It was a second answer to
        // "скільки в мене грошей" that could disagree with the first one for months without
        // anybody noticing — and one more thing to set up before the app said anything
        // useful. The budget now has exactly one source: money that actually arrived.
        return takeHome > 0
            ? new MonthlyBudgetResult(takeHome, taxes, first, false)
            : new MonthlyBudgetResult(null, null, first, false);
    }

    /// Revenue over a date range, run through the tax profile. Zero when there is no income.
    /// <param name="recordedAfter">When set, income dated on <paramref name="from"/> only
    /// counts if it was entered after this moment — the tie-break for the day an opening
    /// balance was taken.</param>
    private async Task<(decimal TakeHome, TakeHomeBreakdown? Taxes)> TakeHomeAsync(
        DateOnly from, DateOnly to, DateTimeOffset? recordedAfter, CancellationToken ct)
    {
        // Income rows store revenue (przychód, VAT excluded) in AmountBase.
        var rows = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && t.Kind == TransactionKind.Income)
            .Select(t => new { t.Date, t.AmountBase, t.CreatedAt })
            .ToListAsync(ct);

        // The timestamp comparison happens here rather than in SQL: SQLite has no real
        // DateTimeOffset, and it only ever applies to one day's worth of rows anyway.
        var revenue = rows
            .Where(t => recordedAfter is not { } cutoff || t.Date > from || t.CreatedAt > cutoff)
            .Sum(t => t.AmountBase);

        if (revenue <= 0) return (0m, null);

        var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (profile is null) return (revenue, null); // no profile to tax it with

        // Taxes are MONTHLY in Poland, so they are applied once to the range's total
        // revenue — never per invoice, which would double-count contributions.
        var take = TakeHomeCalculator.Calculate(profile, revenue, amountIncludesVat: false);
        return take.IsSuccess ? (take.Value!.TakeHome, take.Value) : (revenue, null);
    }
}
