using FinanceApp.Application.Stats;

namespace FinanceApp.Api.Endpoints;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        // months = how many columns back including this one; month = "yyyy-MM" to break down.
        app.MapGet("/api/stats", async (
                IStatsService svc, int? months, string? month, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(months ?? 6, month, ct))).WithTags("Stats");

        return app;
    }
}
