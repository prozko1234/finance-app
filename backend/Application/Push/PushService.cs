using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceApp.Application.Push;

public interface IPushService
{
    /// Remembers a browser that agreed to be told about today's charges. Re-subscribing with
    /// the same endpoint updates the keys in place — a browser can rotate them, and a second
    /// row would deliver the same reminder twice.
    Task<Result<bool>> SubscribeAsync(SavePushSubscriptionRequest req, CancellationToken ct = default);

    /// Forgets one browser. Called when permission is revoked, and by the sender itself when
    /// the push service says the endpoint is gone.
    Task<Result<bool>> UnsubscribeAsync(string endpoint, CancellationToken ct = default);

    /// Whether this account has any device signed up, and at what hour it wants to hear from
    /// the app. What the settings screen needs to show the switch in the right position.
    Task<PushStatusResponse> StatusAsync(CancellationToken ct = default);

    /// Sets the hour of the day reminders go out, 0–23 local time. Null turns them off without
    /// forgetting the devices — turning it back on should not mean granting permission again.
    Task<Result<PushStatusResponse>> SetHourAsync(int? hour, CancellationToken ct = default);
}

public sealed class PushService(IAppDbContext db, ILogger<PushService> log) : IPushService
{
    public async Task<Result<bool>> SubscribeAsync(
        SavePushSubscriptionRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Endpoint)
            || string.IsNullOrWhiteSpace(req.P256dh)
            || string.IsNullOrWhiteSpace(req.Auth))
            return Error.Validation("Підписка на сповіщення прийшла неповна.");

        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint, ct);

        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Endpoint = req.Endpoint,
                P256dh = req.P256dh,
                Auth = req.Auth,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            log.LogInformation("Push: a new device signed up for reminders");
        }
        else
        {
            existing.P256dh = req.P256dh;
            existing.Auth = req.Auth;
        }

        // Signing up with no hour set would be a permission granted and never used. Ten in the
        // morning: a charge is worth hearing about while there is still a day to do something
        // about it, and the hour is one tap away on the settings screen.
        var settings = await SettingsAsync(ct);
        settings.ReminderHour ??= DefaultHour;

        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<bool>> UnsubscribeAsync(string endpoint, CancellationToken ct = default)
    {
        var row = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint, ct);
        if (row is null) return Result<bool>.Ok(false);

        db.PushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<PushStatusResponse> StatusAsync(CancellationToken ct = default)
    {
        var settings = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return new PushStatusResponse(
            await db.PushSubscriptions.AnyAsync(ct),
            settings?.ReminderHour);
    }

    public async Task<Result<PushStatusResponse>> SetHourAsync(
        int? hour, CancellationToken ct = default)
    {
        if (hour is { } h && h is < 0 or > 23)
            return Error.Validation("Година має бути від 0 до 23.");

        var settings = await SettingsAsync(ct);
        settings.ReminderHour = hour;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<PushStatusResponse>.Ok(await StatusAsync(ct));
    }

    /// Ten in the morning — early enough to still be a day with decisions left in it, late
    /// enough that the phone is in a hand. Midnight, when the charge technically falls due, is
    /// exactly the wrong moment: it is read the next morning with the rest of the night.
    public const int DefaultHour = 10;

    private async Task<Domain.AppSettings> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        settings = new Domain.AppSettings { UpdatedAt = DateTimeOffset.UtcNow };
        db.AppSettings.Add(settings);
        return settings;
    }
}
