using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Savings;

namespace FinanceApp.Api.Endpoints;

public static class SavingsEndpoints
{
    public static IEndpointRouteBuilder MapSavingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/savings").WithTags("Savings");

        g.MapGet("/", async (ISavingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        // How much to put aside each month — a fixed amount or a % of take-home.
        g.MapPut("/plan", async (SaveSavingsPlanRequest req, ISavingsService svc, CancellationToken ct) =>
        {
            var r = await svc.SavePlanAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        // Manual movement in or out of the envelope.
        g.MapPost("/entries", async (SaveSavingsEntryRequest req, ISavingsService svc, CancellationToken ct) =>
        {
            var r = await svc.AddEntryAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapDelete("/entries/{id:int}", async (int id, ISavingsService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteEntryAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
