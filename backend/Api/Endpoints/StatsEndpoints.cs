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

        // The window a person actually lives in, in money rather than in counts.
        app.MapGet("/api/stats/recent", async (IStatsService svc, string? window, CancellationToken ct) =>
            Results.Ok(await svc.GetRecentAsync(window, ct))).WithTags("Stats");

        return app;
    }
}
