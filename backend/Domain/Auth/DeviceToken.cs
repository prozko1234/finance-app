using System.Security.Cryptography;

namespace FinanceApp.Domain.Auth;

/// A long-lived credential for one device, used where a cookie cannot go: the native iOS
/// shell (which loads from its own origin) and the home-screen widget (a separate process
/// with no access to the browser's cookie jar).
///
/// Unlike a password, the secret here is 256 random bits, so it cannot be guessed and there
/// is nothing for a slow hash to protect. It is stored as a plain SHA-256 digest — which is
/// also what makes looking it up a single indexed read instead of a scan.
public class DeviceToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// SHA-256 of the secret, base64. The secret itself is shown once, at issue, and never
    /// stored: a leaked database must not hand over working credentials.
    public string TokenHash { get; set; } = "";

    /// What the owner sees in the device list. "iPhone" is more useful than an id when
    /// deciding which one to cut off.
    public string Name { get; set; } = "";

    /// The account's security stamp at the moment of issue. A password change or a
    /// "sign out everywhere" rotates that stamp, and this token dies with it — otherwise
    /// "everywhere" would quietly exclude the phone, which is the one device most likely
    /// to be lost.
    public string IssuedStamp { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// Roughly, not exactly — see DeviceTokenService: updating it on every call would mean
    /// a database write per API request for a figure only ever read by a human.
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// Longest a name can be before it stops being a label and starts being a note.
    public const int MaxNameLength = 40;

    private const int SecretBytes = 32;

    /// URL-safe so the token survives being put in a header, a config file, or a shortcut
    /// without anyone having to think about escaping.
    public static string NewSecret() =>
        Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string HashSecret(string secret) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
