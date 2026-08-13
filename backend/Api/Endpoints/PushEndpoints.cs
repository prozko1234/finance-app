using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Push;
using FinanceApp.Infrastructure.Push;
using Microsoft.Extensions.Options;

namespace FinanceApp.Api.Endpoints;

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/push").WithTags("Push");

        // The VAPID public key, which the browser needs before it can subscribe at all. Public
        // by design — it is the half of the pair that identifies the sender, and the private
        // one never leaves the server. Null when no keys are configured, which is how the
        // settings screen knows to say reminders are unavailable rather than offering a switch
        // that does nothing.
        g.MapGet("/key", (IOptions<VapidOptions> options) =>
            Results.Ok(new { publicKey = options.Value.IsConfigured ? options.Value.PublicKey : null }));

        g.MapGet("/", async (IPushService svc, CancellationToken ct) =>
            Results.Ok(await svc.StatusAsync(ct)));

        g.MapPost("/", async (SavePushSubscriptionRequest req, IPushService svc, CancellationToken ct) =>
        {
            var r = await svc.SubscribeAsync(req, ct);
            return r.IsSuccess ? Results.Ok() : r.Error.ToProblem();
        });

        g.MapDelete("/", async (string endpoint, IPushService svc, CancellationToken ct) =>
        {
            var r = await svc.UnsubscribeAsync(endpoint, ct);
            return r.IsSuccess ? Results.Ok() : r.Error.ToProblem();
        });

        g.MapPut("/hour", async (SetReminderHourRequest req, IPushService svc, CancellationToken ct) =>
        {
            var r = await svc.SetHourAsync(req.Hour, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        });

        return app;
    }
}
