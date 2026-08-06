using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Auth;

/// Who is signed in, as far as the API needs to know.
public record Account(int Id, string Email, string SecurityStamp);

public interface IAccountService
{
    /// True once an owner exists — after that, there is nothing to bootstrap and no way
    /// to register.
    Task<bool> HasOwnerAsync(CancellationToken ct = default);

    /// Creates the one account the app allows, from configuration, on first start. Does
    /// nothing when an owner already exists: the database is the truth about the password,
    /// not the deployment config, or changing the password would be undone by a redeploy.
    Task<bool> EnsureOwnerAsync(string email, string password, CancellationToken ct = default);

    /// The password check itself. Failure is deliberately one indistinguishable error: an
    /// answer that says "no such account" tells whoever is guessing which half to work on.
    Task<Result<Account>> AuthenticateAsync(string email, string password, CancellationToken ct = default);

    /// Confirms a session is still allowed to exist. False after a password change or a
    /// "sign out everywhere" — the stamp in the cookie no longer matches the stored one.
    Task<Account?> FindValidAsync(int userId, string securityStamp, CancellationToken ct = default);

    /// Requires the current password: a cookie left open on someone else's machine must not
    /// be enough to take the account over. Ends every other session by rotating the stamp.
    Task<Result<Account>> ChangePasswordAsync(
        int userId, string currentPassword, string newPassword, CancellationToken ct = default);

    /// The bootstrap address is a placeholder the owner never chose, so it has to be
    /// changeable. Guarded by the password for the same reason a password change is.
    Task<Result<Account>> ChangeEmailAsync(
        int userId, string password, string newEmail, CancellationToken ct = default);

    /// Ends every session, including the one asking. The way back in is the password.
    Task<Result<Account>> SignOutEverywhereAsync(int userId, CancellationToken ct = default);
}

public sealed class AccountService(
    IAppDbContext db, IPasswordHasher hasher, IUserProvisioning provisioning) : IAccountService
{
    /// Long enough that guessing is hopeless, short enough to be typed on a phone. No
    /// character-class rules: they push people towards Password1! and are worth less than
    /// two more characters.
    public const int MinPasswordLength = 10;

    public Task<bool> HasOwnerAsync(CancellationToken ct = default) =>
        db.Users.AnyAsync(ct);

    public async Task<bool> EnsureOwnerAsync(string email, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return false;

        var user = new User
        {
            Email = User.NormalizeEmail(email),
            PasswordHash = hasher.Hash(password),
            SecurityStamp = User.NewStamp(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);

        await db.SaveChangesAsync(ct);

        // An account with no categories and no allocation scheme cannot record a single
        // expense, so the two are created together or the app opens broken.
        await provisioning.ProvisionAsync(user.Id, ct);
        return true;
    }

    public async Task<Result<Account>> AuthenticateAsync(
        string email, string password, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user is null)
        {
            // Hash anyway. Answering an unknown address faster than a known one is a way to
            // ask the app which addresses have accounts.
            hasher.Hash(password);
            return WrongCredentials;
        }

        var check = hasher.Verify(password, user.PasswordHash);
        if (!check.Matches) return WrongCredentials;

        if (check.NeedsRehash) user.PasswordHash = hasher.Hash(password);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<Account>.Ok(ToAccount(user));
    }

    public async Task<Account?> FindValidAsync(
        int userId, string securityStamp, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is not null && user.SecurityStamp == securityStamp ? ToAccount(user) : null;
    }

    public async Task<Result<Account>> ChangePasswordAsync(
        int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("Акаунт не знайдено");

        if (!hasher.Verify(currentPassword, user.PasswordHash).Matches)
            return Error.Validation("Поточний пароль невірний");

        if (Invalid(newPassword) is { } problem) return problem;

        user.PasswordHash = hasher.Hash(newPassword);
        user.SecurityStamp = User.NewStamp();
        await db.SaveChangesAsync(ct);

        return Result<Account>.Ok(ToAccount(user));
    }

    public async Task<Result<Account>> ChangeEmailAsync(
        int userId, string password, string newEmail, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("Акаунт не знайдено");

        if (!hasher.Verify(password, user.PasswordHash).Matches)
            return Error.Validation("Пароль невірний");

        var normalized = User.NormalizeEmail(newEmail);
        // Not a full address validation: nothing is ever sent here, it is a login name. The
        // check only catches a typo that would leave the owner unable to name their account.
        if (normalized.Length < 3 || !normalized.Contains('@'))
            return Error.Validation("Схоже, це не пошта");

        user.Email = normalized;
        await db.SaveChangesAsync(ct);

        return Result<Account>.Ok(ToAccount(user));
    }

    public async Task<Result<Account>> SignOutEverywhereAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("Акаунт не знайдено");

        user.SecurityStamp = User.NewStamp();
        await db.SaveChangesAsync(ct);

        return Result<Account>.Ok(ToAccount(user));
    }

    private static Error? Invalid(string? password) =>
        string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength
            ? Error.Validation($"Пароль має бути не коротшим за {MinPasswordLength} символів")
            : null;

    private static Result<Account> WrongCredentials =>
        Error.Validation("Невірна пошта або пароль");

    private static Account ToAccount(User u) => new(u.Id, u.Email, u.SecurityStamp);
}
