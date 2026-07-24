using FinanceApp.Api.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Endpoints;

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        // Головна цифра застосунку.
        app.MapGet("/api/summary/safe-to-spend", async (AppDbContext db) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var first = new DateOnly(today.Year, today.Month, 1);
            var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

            var spent = await db.Transactions
                .Where(t => t.Date >= first && t.Date <= last)
                .SumAsync(t => (decimal?)t.AmountBase) ?? 0m;

            var budget = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync();
            var r = SafeToSpendCalculator.Calculate(budget?.MonthlyAmount, spent, today);

            return Results.Ok(new SafeToSpendResponse(
                today, Money.BaseCurrency, r.BudgetSet, r.MonthlyBudget,
                r.SpentThisMonth, r.RemainingThisMonth, r.DaysLeftInMonth, r.SafeToSpendToday));
        }).WithTags("Summary");

        return app;
    }
}
