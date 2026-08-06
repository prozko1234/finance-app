using System.Security.Cryptography;

namespace FinanceApp.Domain.Auth;

/// A one-time permission to create an account on this instance.
///
/// Registration is closed to everyone else on purpose. This is one person's server holding
/// other people's finances, not a public product: the way in is that the owner hands someone
/// a link, which is a decision made once per person rather than a door left open.
///
/// Stored as a SHA-256 digest, exactly like <see cref="DeviceToken"/> and for the same
/// reason — a code that creates accounts is a working credential, and a leaked database must
/// not hand out working credentials. The consequence is that the link is shown once, when it
/// is made; a lost one is replaced rather than looked up.
///
/// Not <see cref="IOwnedByUser"/>: redeeming happens with nobody signed in, so a filter on
/// the current account would hide the very row being redeemed. Scoped by CreatedByUserId in
/// the service instead — the same shape as DeviceToken.
public class Invite
{
    /// How long a link stays good for. Long enough to be sent and acted on at leisure, short
    /// enough that one forgotten in a chat window stops working.
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public int Id { get; set; }

    /// SHA-256 of the code, base64. The code itself is shown once and never stored.
    public string CodeHash { get; set; } = "";

    /// Who handed it out. Only the owner can, today, but the column says who rather than
    /// assuming, so it still reads correctly the day that changes.
    public int CreatedByUserId { get; set; }

    /// What the owner calls this invite in the list — "Оля", "брат". Without it a list of
    /// invites is a list of dates, and revoking the right one is a guess.
    public string Note { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// The account it produced, or null while it is still unused. Kept rather than deleted:
    /// "who invited whom" is the only trail this instance has.
    public int? UsedByUserId { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public bool IsSpent(DateTimeOffset now) => UsedByUserId is not null || ExpiresAt <= now;

    /// 256 bits, URL-safe. Guessing is not a threat model at this size; the point of the
    /// length is that it never has to be.
    public static string NewCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashOf(string code) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
}
