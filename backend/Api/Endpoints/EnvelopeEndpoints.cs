using FinanceApp.Application.Contracts;
using FinanceApp.Application.Envelopes;

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

        return app;
    }
}
