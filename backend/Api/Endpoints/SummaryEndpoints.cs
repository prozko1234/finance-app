using FinanceApp.Application.Summaries;

namespace FinanceApp.Api.Endpoints;

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        // The app's headline number.
        app.MapGet("/api/summary/safe-to-spend", async (ISummaryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSafeToSpendAsync(ct))).WithTags("Summary");

        return app;
    }
}
