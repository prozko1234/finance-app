namespace FinanceApp.Domain.Push;

/// One browser that agreed to be told when a subscription charges today.
///
/// A row per device, not per user: the phone is the point of this feature, and somebody who
/// also opens the app on a laptop should not have the phone quietly unsubscribed. The push
/// service hands out the endpoint and the two keys; none of it is ours to invent, and all of
/// it is useless to anybody who is not that browser.
public class PushSubscription : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// The push service's URL for this browser. Unique — a browser that re-subscribes gets the
    /// same endpoint back, and a second row for it would send the same reminder twice.
    public string Endpoint { get; set; } = "";

    /// The browser's public key and auth secret, needed to encrypt the payload for it.
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";

    /// The last day a reminder was sent to this device. The sender wakes up repeatedly through
    /// the day, so without this a subscription due today would be announced every time it ran.
    public DateOnly? LastSentOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
