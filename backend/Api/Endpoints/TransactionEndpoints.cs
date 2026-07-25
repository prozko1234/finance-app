using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Transactions;

namespace FinanceApp.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/transactions").WithTags("Transactions");

        g.MapGet("/", async (ITransactionService svc, CancellationToken ct, int take = 50) =>
            Results.Ok(await svc.GetRecentAsync(take, ct)));

        g.MapGet("/{id:int}", async (int id, ITransactionService svc, CancellationToken ct) =>
        {
            var r = await svc.GetByIdAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapPost("/", async (SaveTransactionRequest req, ITransactionService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/transactions/{r.Value!.Id}", r.Value)
                : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveTransactionRequest>>();

        g.MapPost("/income", async (SaveIncomeRequest req, ITransactionService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateIncomeAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/transactions/{r.Value!.Id}", r.Value)
                : r.Error.ToProblem();
        });

        g.MapPut("/{id:int}", async (int id, SaveTransactionRequest req, ITransactionService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveTransactionRequest>>();

        g.MapDelete("/{id:int}", async (int id, ITransactionService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        return app;
    }
}
