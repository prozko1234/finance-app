using FinanceApp.Api.Contracts;
using FinanceApp.Domain;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/budget").WithTags("Budget");

        g.MapGet("/", async (AppDbContext db) =>
        {
            var b = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync();
            return Results.Ok(b is null
                ? new BudgetResponse(false, null, Money.BaseCurrency, null)
                : new BudgetResponse(true, b.MonthlyAmount, Money.BaseCurrency, b.UpdatedAt));
        });

        g.MapPut("/", async (SetBudgetRequest req, AppDbContext db) =>
        {
            if (req.Amount < 0)
                return Results.BadRequest(new { error = "Бюджет не може бути від'ємним." });

            // MVP: один активний бюджет — upsert першого запису.
            var b = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (b is null)
            {
                b = new Budget { MonthlyAmount = req.Amount, UpdatedAt = DateTimeOffset.UtcNow };
                db.Budgets.Add(b);
            }
            else
            {
                b.MonthlyAmount = req.Amount;
                b.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new BudgetResponse(true, b.MonthlyAmount, Money.BaseCurrency, b.UpdatedAt));
        });

        return app;
    }
}
