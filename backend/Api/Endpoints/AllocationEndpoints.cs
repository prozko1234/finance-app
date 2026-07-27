using FinanceApp.Api.Common;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Contracts;

namespace FinanceApp.Api.Endpoints;

public static class AllocationEndpoints
{
    public static IEndpointRouteBuilder MapAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/allocations").WithTags("Allocations");

        g.MapGet("/", async (IAllocationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        // Switch to a preset, or save the user's own split.
        g.MapPut("/", async (SaveAllocationRequest req, IAllocationService svc, CancellationToken ct) =>
        {
            var r = await svc.SaveAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
