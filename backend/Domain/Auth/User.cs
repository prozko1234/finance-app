namespace FinanceApp.Domain.Auth;

/// The owner of the app. Replaces the single configured password: a password living in an
/// environment variable cannot be changed without a redeploy, is readable by anyone who can
/// see the deployment config, and gives no way to end a session that was opened elsewhere.
///
/// There is deliberately one user. The data tables carry no owner column, so a second
/// account would see the first one's money — registration is therefore closed once this
/// user exists. Turning the app multi-tenant is a separate change, not a flag.
public class User
{
    public int Id { get; set; }

    /// Stored lowercased and trimmed; it is the login name, not a mailbox we send anything to.
    public string Email { get; set; } = "";

    /// Self-describing hash (see IPasswordHasher) — never the password itself.
    public string PasswordHash { get; set; } = "";

    /// Changes whenever every existing session must stop working: a password change, or an
    /// explicit "sign out everywhere". Cookies carry the stamp they were issued with, so a
    /// session whose stamp no longer matches is refused on its next request.
    public string SecurityStamp { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    /// One spelling for one person: comparing raw input against a stored address would let
    /// "Bogdan@x.com" and "bogdan@x.com " be two different logins for the same account.
    public static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
