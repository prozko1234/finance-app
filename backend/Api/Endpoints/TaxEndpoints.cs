using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Tax;

namespace FinanceApp.Api.Endpoints;

public static class TaxEndpoints
{
    public static IEndpointRouteBuilder MapTaxEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tax").WithTags("Tax");

        g.MapGet("/profile", async (ITaxService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetProfileAsync(ct)));

        g.MapPut("/profile", async (SaveTaxProfileRequest req, ITaxService svc, CancellationToken ct) =>
        {
            var r = await svc.SaveProfileAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveTaxProfileRequest>>();

        // Suggested rates for the current year — data to prefill the form, not the source of truth.
        g.MapGet("/defaults", (ITaxService svc) => Results.Ok(svc.GetDefaults()));

        // What the bookkeeper actually said for a month. Absent month = the one being lived in.
        g.MapGet("/actuals", async (DateOnly? month, ITaxService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetActualsAsync(
                month ?? DateOnly.FromDateTime(DateTime.Now), ct)));

        g.MapPut("/actuals", async (SaveTaxActualsRequest req, ITaxService svc, CancellationToken ct) =>
        {
            var r = await svc.SaveActualsAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        // Live preview while typing an invoice — what it adds to this month's budget.
        g.MapPost("/income-preview", async (CalculateTakeHomeRequest req, ITaxService svc, CancellationToken ct) =>
        {
            var r = await svc.PreviewIncomeAsync(req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
