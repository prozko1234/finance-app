using FinanceApp.Application.Contracts;
using FinanceApp.Application.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Api.Tests;

/// Charge reminders, from the server's side.
///
/// The point of the feature is the HOUR. A subscription falls due at midnight, and midnight is
/// exactly the wrong moment to say so — the notification is read the next morning with the rest
/// of the night's noise, by which time the money has gone and there is nothing left to decide.
public class PushTests
{
    private static PushService Sut(SqliteInMemory mem) =>
        new(mem.Db, NullLogger<PushService>.Instance);

    private static SavePushSubscriptionRequest Device(string endpoint = "https://push.example/abc") =>
        new(endpoint, "p256dh-key", "auth-secret");

    [Fact]
    public async Task Signing_up_a_device_turns_reminders_on_at_a_sensible_hour()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).SubscribeAsync(Device());
        Assert.True(r.IsSuccess);

        var status = await Sut(mem).StatusAsync();
        Assert.True(status.Enabled);
        // Permission granted and never used would be the worst of both: the browser asked, and
        // then nothing ever arrives.
        Assert.Equal(PushService.DefaultHour, status.Hour);
    }

    /// A browser that re-subscribes gets the same endpoint back, and a second row for it would
    /// deliver the same reminder twice.
    [Fact]
    public async Task Re_subscribing_the_same_browser_updates_it_instead_of_doubling_it()
    {
        using var mem = new SqliteInMemory();

        await Sut(mem).SubscribeAsync(Device());
        await Sut(mem).SubscribeAsync(new SavePushSubscriptionRequest(
            "https://push.example/abc", "rotated-key", "rotated-secret"));

        var row = Assert.Single(await mem.Db.PushSubscriptions.ToListAsync());
        Assert.Equal("rotated-key", row.P256dh);
    }

    /// Turning reminders off must not forget the device: switching them back on should not mean
    /// asking the browser for permission all over again.
    [Fact]
    public async Task Turning_reminders_off_keeps_the_device()
    {
        using var mem = new SqliteInMemory();
        await Sut(mem).SubscribeAsync(Device());

        await Sut(mem).SetHourAsync(null);

        var status = await Sut(mem).StatusAsync();
        Assert.True(status.Enabled);
        Assert.Null(status.Hour);
        Assert.Single(await mem.Db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task An_hour_outside_the_day_is_refused()
    {
        using var mem = new SqliteInMemory();

        Assert.False((await Sut(mem).SetHourAsync(24)).IsSuccess);
        Assert.False((await Sut(mem).SetHourAsync(-1)).IsSuccess);
        Assert.True((await Sut(mem).SetHourAsync(0)).IsSuccess);
        Assert.True((await Sut(mem).SetHourAsync(23)).IsSuccess);
    }

    [Fact]
    public async Task An_incomplete_subscription_is_refused_rather_than_stored()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).SubscribeAsync(new SavePushSubscriptionRequest("https://x", "", "a"));

        Assert.False(r.IsSuccess);
        Assert.Empty(await mem.Db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Forgetting_a_device_that_was_never_known_is_not_an_error()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).UnsubscribeAsync("https://push.example/never");

        Assert.True(r.IsSuccess);
        Assert.False(r.Value);
    }

    [Fact]
    public async Task An_account_with_no_devices_reads_as_off()
    {
        using var mem = new SqliteInMemory();

        var status = await Sut(mem).StatusAsync();

        Assert.False(status.Enabled);
        Assert.Null(status.Hour);
    }
}
