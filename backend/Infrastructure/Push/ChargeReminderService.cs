using System.Text.Json;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceApp.Infrastructure.Push;

/// Tells a phone, at an hour a person is awake, that a subscription charges today.
///
/// The point of the whole feature is the hour. A charge falls due at midnight and that is
/// exactly the wrong moment to say so — the notification is read the next morning with the
/// rest of the night's noise, by which time the money has gone and there is nothing left to
/// decide. So the sender wakes up often and says nothing until the clock reaches the hour the
/// user picked, in the SERVER's local time, which is the same zone the rest of the app already
/// treats as "today".
///
/// Every query here ignores the global user filter. There is no request behind this work and
/// therefore no current user, so the filter would quietly match nothing and the job would run
/// forever finding no subscriptions at all.
public sealed class ChargeReminderService(
    IServiceScopeFactory scopes,
    PushServiceClient push,
    IOptions<VapidOptions> options,
    ILogger<ChargeReminderService> log) : BackgroundService
{
    /// Often enough that an hour is never missed — a container that restarts at 09:58 must not
    /// skip a 10:00 reminder — and rare enough to be free. The per-device "already sent today"
    /// stamp is what makes running this repeatedly harmless.
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var vapid = options.Value;
        if (!vapid.IsConfigured)
        {
            // Said once, loudly. A reminder that never arrives looks exactly like one that was
            // never due, and that is a bug nobody can see from the outside.
            log.LogWarning(
                "Push: no VAPID keys configured ({Section}:PublicKey / PrivateKey) — charge reminders are off",
                VapidOptions.Section);
            return;
        }

        push.DefaultAuthentication = new VapidAuthentication(vapid.PublicKey!, vapid.PrivateKey!)
        {
            Subject = vapid.Subject,
        };

        using var timer = new PeriodicTimer(Tick);
        do
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Never let one bad round kill the loop: tomorrow's reminder is worth more than
                // a clean stack trace today.
                log.LogError(e, "Push: reminder round failed");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Whose hour it is right now, and who has not already been told today.
        var users = await db.AppSettings
            .IgnoreQueryFilters()
            .Where(s => s.ReminderHour != null && s.ReminderHour <= now.Hour)
            .Select(s => s.UserId)
            .ToListAsync(ct);

        foreach (var userId in users)
        {
            var devices = await db.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(x => x.UserId == userId && (x.LastSentOn == null || x.LastSentOn < today))
                .ToListAsync(ct);

            if (devices.Count == 0) continue;

            var due = await DueTodayAsync(db, userId, today, ct);
            if (due.Count == 0)
            {
                // Nothing to say. The stamp is deliberately NOT moved: if a subscription is
                // added later today, the reminder should still go out.
                continue;
            }

            var payload = Payload(due, today);
            foreach (var device in devices)
            {
                switch (await SendAsync(device.Endpoint, device.P256dh, device.Auth, payload, ct))
                {
                    case Delivery.Sent:
                        device.LastSentOn = today;
                        break;
                    case Delivery.Gone:
                        db.PushSubscriptions.Remove(device);
                        break;
                    // Failed: the stamp stays where it is, so the next tick tries again.
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// The charges whose day is today: every active expense rule whose schedule lands on it,
    /// less the occurrences the user has already deleted, less anything already confirmed as
    /// paid. Confirmed ones are dropped because the reminder exists to ask a question, and a
    /// question already answered is a notification that teaches people to ignore notifications.
    private static async Task<List<(string Name, decimal Amount, string Currency)>> DueTodayAsync(
        AppDbContext db, int userId, DateOnly today, CancellationToken ct)
    {
        var rules = await db.RecurringExpenses
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.Active && r.Kind == TransactionKind.Expense)
            .Select(r => new
            {
                r.Id, r.StartsOn, r.Unit, r.Interval, r.AmountOriginal, r.CurrencyOriginal,
                r.Note, CategoryName = r.Category!.Name,
            })
            .ToListAsync(ct);

        if (rules.Count == 0) return [];

        var skipped = (await db.RecurringSkips
                .IgnoreQueryFilters()
                .Where(s => s.UserId == userId && s.Date == today)
                .Select(s => s.RecurringExpenseId)
                .ToListAsync(ct))
            .ToHashSet();

        var confirmed = (await db.Transactions
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.Date == today
                            && t.RecurringExpenseId != null && t.Status == TxStatus.Posted)
                .Select(t => t.RecurringExpenseId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        return rules
            .Where(r => !skipped.Contains(r.Id) && !confirmed.Contains(r.Id))
            .Where(r => RecurringSchedule.Occurrences(r.StartsOn, r.Unit, r.Interval, today, today).Any())
            .Select(r => (
                Name: string.IsNullOrWhiteSpace(r.Note) ? r.CategoryName : r.Note!,
                r.AmountOriginal,
                r.CurrencyOriginal))
            .ToList();
    }

    /// One charge is named; several are counted. A notification that lists five subscriptions
    /// is a notification nobody finishes reading, and the app is one tap away either way.
    private static string Payload(
        IReadOnlyList<(string Name, decimal Amount, string Currency)> due, DateOnly today)
    {
        var total = due.Sum(d => d.Amount);
        var body = due.Count == 1
            ? $"{due[0].Name} — {due[0].Amount:0.##} {due[0].Currency}"
            : $"{due.Count} платежі на {total:0.##} {due[0].Currency}";

        return JsonSerializer.Serialize(new
        {
            title = "Сьогодні списується",
            body,
            tag = $"charges-{today:yyyy-MM-dd}",
        });
    }

    /// What became of one attempt. Three outcomes, not two: a push service answering 404 or 410
    /// means the browser is gone for good and the row should go with it, where a timeout means
    /// try again in ten minutes — and collapsing those two into "failed" would either forget
    /// working devices or keep dead ones forever.
    private enum Delivery { Sent, Gone, Failed }

    private async Task<Delivery> SendAsync(
        string endpoint, string p256dh, string auth, string payload, CancellationToken ct)
    {
        try
        {
            await push.RequestPushMessageDeliveryAsync(
                new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = endpoint,
                    Keys = new Dictionary<string, string> { ["p256dh"] = p256dh, ["auth"] = auth },
                },
                new PushMessage(payload) { Urgency = PushMessageUrgency.Normal },
                ct);
            return Delivery.Sent;
        }
        catch (HttpRequestException e) when (
            e.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            log.LogInformation("Push: endpoint is gone, forgetting the device");
            return Delivery.Gone;
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Push: delivery failed");
            return Delivery.Failed;
        }
    }
}
