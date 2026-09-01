using FinanceApp.Application.Export;
using FinanceApp.Domain;

namespace FinanceApp.Api.Endpoints;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/export").WithTags("Export");

        // Dated in the filename: the answer to "чому не сходиться" is usually two exports a
        // week apart, and two files called "finance.csv" in a Downloads folder are one file.
        g.MapGet("/ledger.csv", async (IExportService svc, CancellationToken ct) =>
            Results.File(
                LedgerCsv.Write(await svc.LedgerAsync(ct), Money.BaseCurrency),
                "text/csv; charset=utf-8",
                $"finance-{DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.csv"));

        g.MapGet("/backup.json", async (IExportService svc, CancellationToken ct) =>
            Results.Json(
                await svc.BackupAsync(ct),
                contentType: "application/json",
                statusCode: 200));

        return app;
    }
}
