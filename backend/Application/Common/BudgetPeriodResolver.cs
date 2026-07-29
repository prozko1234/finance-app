using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Common;

/// Everything that says "this month" asks here, so the summary, the envelopes and the
/// income preview can never disagree about when the month starts. Replaced MonthRange,
/// which was the same idea with the answer hard-coded to the 1st.
public interface IBudgetPeriods
{
    Task<BudgetPeriod> CurrentAsync(CancellationToken ct = default);

    Task<BudgetPeriod> ForAsync(DateOnly date, CancellationToken ct = default);
}

public sealed class BudgetPeriodResolver(IAppDbContext db) : IBudgetPeriods
{
    /// Read once per request. The settings row cannot change mid-request, and a service
    /// that asks for the period three times should not hit the database three times.
    private int? _startDay;

    public Task<BudgetPeriod> CurrentAsync(CancellationToken ct = default) =>
        ForAsync(DateOnly.FromDateTime(DateTime.Now), ct);

    public async Task<BudgetPeriod> ForAsync(DateOnly date, CancellationToken ct = default) =>
        BudgetPeriods.For(date, await StartDayAsync(ct));

    private async Task<int> StartDayAsync(CancellationToken ct)
    {
        if (_startDay is { } cached) return cached;

        var settings = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        _startDay = settings?.PeriodStartDay ?? BudgetPeriods.FirstOfMonth;

        return _startDay.Value;
    }
}
