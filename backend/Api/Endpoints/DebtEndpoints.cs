using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Debts;

namespace FinanceApp.Api.Endpoints;

public static class DebtEndpoints
{
    public static IEndpointRouteBuilder MapDebtEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/debts").WithTags("Debts");

        g.MapGet("/", async (IDebtService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        g.MapPost("/", async (SaveDebtRequest req, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.AddAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapPut("/{id:int}", async (int id, SaveDebtRequest req, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapDelete("/{id:int}", async (int id, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        // Settled — by payment, by forgiveness, or by both sides deciding to forget it.
        g.MapPost("/{id:int}/closed", async (int id, CloseDebtRequest req, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.SetClosedAsync(id, req.Closed, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapPost("/{id:int}/payments", async (
            int id, SaveDebtPaymentRequest req, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.AddPaymentAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        g.MapDelete("/payments/{paymentId:int}", async (
            int paymentId, IDebtService svc, CancellationToken ct) =>
        {
            var r = await svc.DeletePaymentAsync(paymentId, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
