namespace FinanceApp.Infrastructure.Push;

/// The keys that let a push service believe a notification really came from this app.
///
/// Generated once and kept in configuration, never in the repository. Without them the
/// reminder simply does not run — and says so at startup rather than failing silently every
/// hour, because a notification that never arrives looks exactly like one that was never due.
public sealed class VapidOptions
{
    public const string Section = "Push";

    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }

    /// Contact for the push service if it ever needs to complain — "mailto:..." or a URL.
    public string Subject { get; set; } = "mailto:admin@localhost";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
