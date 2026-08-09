using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Application.Common;

public static class BudgetPeriodCounting
{
    /// Periods from the one being lived in up to and including the one the date falls in.
    /// Walked rather than divided by 30: the period start day can be the 31st, and the months
    /// it produces are not the same length.
    ///
    /// Shared by the jars and by debts because both answer the same question — «скільки
    /// відкладати за період, щоб встигнути» — and two copies of this walk would eventually
    /// give the two screens different answers for the same date.
    /// <returns>Null when there is no date, and therefore no pace to keep.</returns>
    public static async Task<int?> CountUntilAsync(
        this IBudgetPeriods periods, DateOnly? date, BudgetPeriod current, CancellationToken ct = default)
    {
        if (date is not { } target) return null;
        if (target <= current.End) return target < current.Start ? 0 : 1;

        var count = 1;
        var end = current.End;

        // 600 periods is fifty years — past that the figure is noise anyway, and the loop must
        // not be able to hang on a date somebody typed with four extra digits.
        while (end < target && count < 600)
        {
            var next = await periods.ForAsync(end.AddDays(1), ct);
            end = next.End;
            count++;
        }

        return count;
    }
}
