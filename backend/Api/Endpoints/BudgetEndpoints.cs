using FinanceApp.Api.Common;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Contracts;

namespace FinanceApp.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/budget").WithTags("Budget");

        g.MapGet("/", async (IBudgetService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        g.MapPut("/", async (SetBudgetRequest req, IBudgetService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetAsync(req.Amount, ct)))
            .AddEndpointFilter<ValidationFilter<SetBudgetRequest>>();

        return app;
    }
}
