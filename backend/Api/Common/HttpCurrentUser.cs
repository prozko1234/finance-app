using System.Security.Claims;
using FinanceApp.Application.Abstractions;

namespace FinanceApp.Api.Common;

/// The one account every request belongs to while the app runs OPEN — no password
/// configured, which locally means no login screen (see the auth ADR: asking for a password
/// on localhost every morning is friction for nothing).
///
/// Owned data is filtered by account, so "open" cannot mean "no account" — that would be an
/// app which reads nothing and refuses every write. It means "you are the owner, and nobody
/// had to prove it". Left null whenever a password IS configured, so a deployed build has no
/// such fallback to fall through to.
public sealed class OpenModeOwner
{
    public int? UserId { get; set; }
}

/// The signed-in account, taken from the claims the cookie (or device token) was validated
/// into. Null when nobody is signed in and the app is locked — and null reads nothing,
/// because every owned query filters on it.
///
/// Reads the claim on every access rather than caching it at construction: the same scope
/// covers authentication, so the identity can arrive after this object does.
public sealed class HttpCurrentUser(IHttpContextAccessor accessor, OpenModeOwner open)
    : ICurrentUser
{
    public int? UserId => FromClaim() ?? open.UserId;

    private int? FromClaim() =>
        int.TryParse(
            accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id) && id > 0
            ? id
            : null;
}
