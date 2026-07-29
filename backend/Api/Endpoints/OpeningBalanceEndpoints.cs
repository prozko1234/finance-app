using FinanceApp.Api.Common;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Contracts;

namespace FinanceApp.Api.Endpoints;

/// "Скільки в мене зараз є" — the mid-period start. Used to share a file with the fallback
/// budget, which is gone; this one stays, because counting what is actually in the account
/// is a fact about today, not a second opinion about the month.
public static class OpeningBalanceEndpoints
{
    public static IEndpointRouteBuilder MapOpeningBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/opening-balance").WithTags("Budget");

        g.MapGet("/", async (IOpeningBalanceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        g.MapPut("/", async (SetOpeningBalanceRequest req, IOpeningBalanceService svc, CancellationToken ct) =>
        {
            var r = await svc.SetAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapDelete("/", async (IOpeningBalanceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ClearAsync(ct)));

        return app;
    }
}
