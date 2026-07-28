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

        var o = app.MapGroup("/api/opening-balance").WithTags("Budget");

        o.MapGet("/", async (IOpeningBalanceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        o.MapPut("/", async (SetOpeningBalanceRequest req, IOpeningBalanceService svc, CancellationToken ct) =>
        {
            var r = await svc.SetAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        o.MapDelete("/", async (IOpeningBalanceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ClearAsync(ct)));

        return app;
    }
}
