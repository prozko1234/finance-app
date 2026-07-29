using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Settings;

namespace FinanceApp.Api.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings").WithTags("Settings");

        g.MapGet("/", async (ISettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        g.MapPut("/currency", async (SetDisplayCurrencyRequest req, ISettingsService svc, CancellationToken ct) =>
        {
            var r = await svc.SetDisplayCurrencyAsync(req.Currency, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        })
            .AddEndpointFilter<ValidationFilter<SetDisplayCurrencyRequest>>();

        g.MapPut("/period-start-day", async (SetPeriodStartDayRequest req, ISettingsService svc, CancellationToken ct) =>
        {
            var r = await svc.SetPeriodStartDayAsync(req.Day, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
