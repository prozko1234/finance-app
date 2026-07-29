namespace FinanceApp.Domain.Auth;

/// Turns a password into something safe to store, and checks one against it.
///
/// The hash string is self-describing (algorithm and cost live inside it), so the cost can be
/// raised later without invalidating anyone: <see cref="Verify"/> reports when a stored hash
/// was made with weaker settings, and the caller rehashes it during a successful login, while
/// the plain password is briefly in hand.
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string password, string storedHash);
}

/// <param name="Matches">Whether the password is the right one.</param>
/// <param name="NeedsRehash">The stored hash is valid but made with outdated settings.</param>
public readonly record struct PasswordCheck(bool Matches, bool NeedsRehash);
