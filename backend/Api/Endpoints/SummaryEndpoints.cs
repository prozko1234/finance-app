using FinanceApp.Application.Summaries;

namespace FinanceApp.Api.Endpoints;

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        // The app's headline number.
        app.MapGet("/api/summary/safe-to-spend", async (ISummaryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSafeToSpendAsync(ct))).WithTags("Summary");

        // The other half of it: what the month will ask for, whatever is in the account.
        app.MapGet("/api/summary/monthly-need", async (IMonthlyNeedService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct))).WithTags("Summary");

        return app;
    }
}
