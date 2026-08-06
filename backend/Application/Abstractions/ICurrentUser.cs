namespace FinanceApp.Application.Abstractions;

/// Whose data this request is allowed to touch.
///
/// Null means nobody is signed in — the login screen, the health check, the moment before a
/// cookie is validated. Null is not "everybody": every owned query filters to a user id, so a
/// request with no user reads nothing at all. That is the whole point. The dangerous default
/// is the permissive one, and it fails silently.
public interface ICurrentUser
{
    int? UserId { get; }
}

/// The current user when there is no request to ask — startup migrations, the seeding of the
/// first owner, tests that predate accounts entirely.
public sealed class FixedCurrentUser(int? userId) : ICurrentUser
{
    public int? UserId { get; } = userId;
}
