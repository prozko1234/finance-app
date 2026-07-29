using System.Security.Cryptography;
using FinanceApp.Domain.Auth;

namespace FinanceApp.Infrastructure.Auth;

/// PBKDF2-HMAC-SHA256, the strongest password hash available without adding a dependency.
/// Argon2id would be better against GPU cracking, but it means pulling in a native library
/// for a one-user app; PBKDF2 at this iteration count is the accepted fallback (OWASP).
///
/// Stored as `pbkdf2-sha256$iterations$salt$hash`, all base64. Everything needed to check a
/// password is in the string, so raising <see cref="Iterations"/> later keeps old hashes
/// working — they simply come back as NeedsRehash and get upgraded on the next login.
/// <param name="iterations">Lowered only by tests, which would otherwise spend most of their
/// time proving that a deliberately slow function is slow.</param>
public sealed class Pbkdf2PasswordHasher(int iterations = Pbkdf2PasswordHasher.DefaultIterations)
    : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    public const int DefaultIterations = 600_000; // OWASP 2023 for PBKDF2-HMAC-SHA256
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, iterations);

        return string.Join('$', Prefix, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public PasswordCheck Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var storedRounds))
            return new PasswordCheck(false, false);

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return new PasswordCheck(false, false);
        }

        var actual = Derive(password, salt, storedRounds, expected.Length);

        // Fixed-time: a comparison that stops at the first wrong byte leaks, through timing,
        // how much of a guess was right.
        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);

        return new PasswordCheck(matches, matches && storedRounds < iterations);
    }

    private static byte[] Derive(string password, byte[] salt, int rounds, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, length);
}
