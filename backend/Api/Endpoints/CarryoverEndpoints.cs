using FinanceApp.Api.Common;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Endpoints;

/// Where last period's leftover goes. There is no GET: the question travels with the summary,
/// so the home screen already knows whether to ask without a second request.
public static class CarryoverEndpoints
{
    public static IEndpointRouteBuilder MapCarryoverEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/carryover").WithTags("Budget");

        g.MapPost("/", async (DecideCarryoverRequest req, ICarryoverService svc, CancellationToken ct) =>
        {
            if (!Enum.TryParse<CarryoverDecision>(req.Decision, ignoreCase: true, out var decision))
                return Results.BadRequest($"Невідоме рішення: {req.Decision}.");

            var r = await svc.DecideAsync(decision, req.EnvelopeId, ct);
            return r.IsSuccess ? Results.Ok() : r.Error.ToProblem();
        });

        return app;
    }
}
