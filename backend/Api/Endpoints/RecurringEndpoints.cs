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

        // The id is a transaction's, not a subscription's: what gets confirmed is one charge
        // on one date, and the same subscription can have another still waiting behind it.
        g.MapPost("/charges/{transactionId:int}/confirm",
            async (int transactionId, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.ConfirmChargeAsync(transactionId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        g.MapPost("/charges/{transactionId:int}/unconfirm",
            async (int transactionId, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.UnconfirmChargeAsync(transactionId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        g.MapDelete("/{id:int}", async (int id, IRecurringService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        return app;
    }
}
