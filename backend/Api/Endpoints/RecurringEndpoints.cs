using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;

namespace FinanceApp.Api.Endpoints;

public static class RecurringEndpoints
{
    public static IEndpointRouteBuilder MapRecurringEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/recurring").WithTags("Recurring");

        g.MapGet("/", async (IRecurringService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllAsync(ct)));

        g.MapPost("/", async (SaveRecurringRequest req, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/recurring/{r.Value!.Id}", r.Value)
                : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveRecurringRequest>>();

        g.MapPut("/{id:int}", async (int id, SaveRecurringRequest req, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveRecurringRequest>>();

        g.MapDelete("/{id:int}", async (int id, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        return app;
    }
}
