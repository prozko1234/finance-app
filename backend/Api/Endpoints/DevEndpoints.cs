using FinanceApp.Application.Dev;

namespace FinanceApp.Api.Endpoints;

public static class DevEndpoints
{
    /// Destructive helpers for local testing. The caller must only map these in the
    /// Development environment — there is no auth yet, so they are gated by wiring, not
    /// by a check inside the handler.
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/dev").WithTags("Dev");

        g.MapPost("/reset", async (IDevDataService svc, CancellationToken ct) =>
        {
            await svc.ResetAsync(ct);
            return Results.Ok(new { message = "Дані очищено." });
        });

        g.MapPost("/seed", async (IDevDataService svc, CancellationToken ct) =>
        {
            await svc.SeedExampleAsync(ct);
            return Results.Ok(new { message = "Приклад створено." });
        });

        return app;
    }
}
