using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Envelopes;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;

namespace FinanceApp.Api.Endpoints;

public static class EnvelopeEndpoints
{
    public static IEndpointRouteBuilder MapEnvelopeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/envelopes").WithTags("Envelopes");

        // Period by period rather than movement by movement: «за місяць скільки пішло і
        // скільки там тепер» is the question the screen exists to answer.
        g.MapGet("/{id:int}/history", async (
            int id, int? periods, IEnvelopeService svc, CancellationToken ct) =>
        {
            var history = await svc.HistoryAsync(id, periods ?? 6, ct);

            return Results.Ok(history
                .Select(p => new EnvelopePeriodResponse(p.Start, p.End, p.Moved, p.BalanceAfter))
                .ToList());
        });

        // A pot under a goal of one's own: «Відпустка», «Ремонт». The scheme brings its own
        // pots along, but until now there was no way to make one without editing the scheme.
        g.MapPost("/", async (SaveEnvelopeRequest req, IEnvelopeService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(req.Name, Kind(req), ct);
            return r.IsSuccess
                ? Results.Created($"/api/envelopes/{r.Value!.Id}", Response(r.Value))
                : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveEnvelopeRequest>>();

        g.MapPut("/{id:int}", async (
            int id, SaveEnvelopeRequest req, IEnvelopeService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req.Name, Kind(req), ct);
            return r.IsSuccess ? Results.Ok(Response(r.Value!)) : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveEnvelopeRequest>>();

        // DELETE archives: the deposits and withdrawals point at the envelope, and a history of
        // real money movements must not go with it.
        g.MapDelete("/{id:int}", async (int id, IEnvelopeService svc, CancellationToken ct) =>
        {
            var r = await svc.ArchiveAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        // «Відпустка 6 000 до червня» → «950 за період». A null amount takes the target off.
        g.MapPut("/{id:int}/target", async (
            int id, SetEnvelopeTargetRequest req, IEnvelopeService svc, CancellationToken ct) =>
        {
            var r = await svc.SetTargetAsync(id, req.Amount, req.Currency, req.Date, ct);
            return r.IsSuccess ? Results.Ok(Response(r.Value!)) : r.Error.ToProblem();
        });

        return app;
    }

    /// The validator has already checked the name parses, so this cannot fall back silently
    /// onto a wrong kind — Spending is rejected by the service, with a sentence to read.
    private static BucketKind Kind(SaveEnvelopeRequest req) =>
        Enum.TryParse<BucketKind>(req.Kind, out var kind) ? kind : BucketKind.Savings;

    private static EnvelopeResponse Response(Envelope e) =>
        new(e.Id, e.Name, e.Kind.ToString(), e.IsDefault);
}
