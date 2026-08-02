using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Auth;

/// A device as the owner sees it in the list. Carries no secret — there is nothing here
/// that could not be shown on screen.
public record DeviceTokenInfo(int Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// The one moment the secret exists outside the device that will hold it.
public record IssuedDeviceToken(int Id, string Name, string Token);

public interface IDeviceTokenService
{
    /// Trades a password for a device credential. The password is required even though the
    /// caller may already have a session: a token outlives the session that made it, so
    /// minting one has to cost the same as signing in.
    Task<Result<IssuedDeviceToken>> IssueAsync(
        string email, string password, string name, CancellationToken ct = default);

    /// Who this token belongs to, or null if it is unknown, revoked, or older than the
    /// account's current security stamp.
    Task<Account?> AuthenticateAsync(string secret, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceTokenInfo>> ListAsync(int userId, CancellationToken ct = default);

    Task<Result<bool>> RevokeAsync(int userId, int tokenId, CancellationToken ct = default);
}

public sealed class DeviceTokenService(IAppDbContext db, IAccountService accounts) : IDeviceTokenService
{
    /// How stale "last used" is allowed to get. Without this, every request from the phone
    /// would be a database write; with it, the figure is accurate enough to answer the only
    /// question it is ever asked — "is this device still in use?"
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromHours(1);

    public async Task<Result<IssuedDeviceToken>> IssueAsync(
        string email, string password, string name, CancellationToken ct = default)
    {
        var auth = await accounts.AuthenticateAsync(email, password, ct);
        if (!auth.IsSuccess) return auth.Error;

        var account = auth.Value!;
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return Error.Validation("Вкажи, що це за пристрій");
        if (trimmed.Length > DeviceToken.MaxNameLength)
            return Error.Validation($"Назва пристрою не довша за {DeviceToken.MaxNameLength} символів");

        var secret = DeviceToken.NewSecret();
        var token = new DeviceToken
        {
            UserId = account.Id,
            TokenHash = DeviceToken.HashSecret(secret),
            Name = trimmed,
            IssuedStamp = account.SecurityStamp,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.DeviceTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return Result<IssuedDeviceToken>.Ok(new IssuedDeviceToken(token.Id, token.Name, secret));
    }

    public async Task<Account?> AuthenticateAsync(string secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret)) return null;

        var hash = DeviceToken.HashSecret(secret);
        var token = await db.DeviceTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null) return null;

        // The stamp check is what ties this token to the account's current state: it is the
        // same test a cookie passes on every request, so both kinds of session end together.
        var account = await accounts.FindValidAsync(token.UserId, token.IssuedStamp, ct);
        if (account is null) return null;

        var now = DateTimeOffset.UtcNow;
        if (token.LastUsedAt is null || now - token.LastUsedAt.Value > LastUsedPrecision)
        {
            token.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return account;
    }

    public async Task<IReadOnlyList<DeviceTokenInfo>> ListAsync(int userId, CancellationToken ct = default) =>
        await db.DeviceTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            // By id, not by CreatedAt: SQLite cannot sort DateTimeOffset, and the id is
            // monotonic anyway, so "newest first" comes out the same.
            .OrderByDescending(t => t.Id)
            .Select(t => new DeviceTokenInfo(t.Id, t.Name, t.CreatedAt, t.LastUsedAt))
            .ToListAsync(ct);

    public async Task<Result<bool>> RevokeAsync(int userId, int tokenId, CancellationToken ct = default)
    {
        var token = await db.DeviceTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, ct);

        if (token is null) return Error.NotFound("Пристрій не знайдено");

        // Revoking is a soft delete: the row is what proves a device once had access, and
        // that history is worth more than the few bytes it costs.
        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Result<bool>.Ok(true);
    }
}
