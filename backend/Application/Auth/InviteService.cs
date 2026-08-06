using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Auth;

/// An invite as the owner sees it in the list. The code is absent on purpose — it exists in
/// readable form for one response, the one that created it.
public record InviteView(
    int Id, string Note, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string? UsedByEmail, DateTimeOffset? UsedAt, bool Expired);

/// A freshly made invite. <paramref name="Code"/> is shown once and never again.
public record NewInvite(int Id, string Code);

public interface IInviteService
{
    Task<Result<NewInvite>> CreateAsync(int byUserId, string note, CancellationToken ct = default);
    Task<Result<IReadOnlyList<InviteView>>> ListAsync(int byUserId, CancellationToken ct = default);
    Task<Result<bool>> RevokeAsync(int byUserId, int inviteId, CancellationToken ct = default);

    /// Turns a code into an account. The only way to register on this instance.
    Task<Result<Account>> RedeemAsync(
        string code, string email, string password, CancellationToken ct = default);
}

public sealed class InviteService(
    IAppDbContext db, IPasswordHasher hasher, IUserProvisioning provisioning) : IInviteService
{
    public async Task<Result<NewInvite>> CreateAsync(
        int byUserId, string note, CancellationToken ct = default)
    {
        if (!await IsOwnerAsync(byUserId, ct))
            return Error.Validation("Запрошувати може лише власник цього застосунку.");

        var code = Invite.NewCode();
        var invite = new Invite
        {
            CodeHash = Invite.HashOf(code),
            CreatedByUserId = byUserId,
            Note = (note ?? string.Empty).Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + Invite.Lifetime,
        };

        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);

        return Result<NewInvite>.Ok(new NewInvite(invite.Id, code));
    }

    public async Task<Result<IReadOnlyList<InviteView>>> ListAsync(
        int byUserId, CancellationToken ct = default)
    {
        if (!await IsOwnerAsync(byUserId, ct))
            return Error.Validation("Запрошення бачить лише власник цього застосунку.");

        var now = DateTimeOffset.UtcNow;
        var rows = await db.Invites
            .Where(i => i.CreatedByUserId == byUserId)
            .OrderByDescending(i => i.Id)
            .Select(i => new
            {
                i.Id, i.Note, i.CreatedAt, i.ExpiresAt, i.UsedAt, i.UsedByUserId,
            })
            .ToListAsync(ct);

        // Resolved separately: the address belongs to a table the query filter does not
        // touch, and a join for a handful of rows buys nothing.
        var usedBy = rows.Where(r => r.UsedByUserId is not null)
            .Select(r => r.UsedByUserId!.Value).ToList();
        var emails = await db.Users.Where(u => usedBy.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return Result<IReadOnlyList<InviteView>>.Ok(rows.Select(r => new InviteView(
            r.Id, r.Note, r.CreatedAt, r.ExpiresAt,
            r.UsedByUserId is null ? null : emails.GetValueOrDefault(r.UsedByUserId.Value),
            r.UsedAt,
            r.UsedByUserId is null && r.ExpiresAt <= now)).ToList());
    }

    /// Revoking a spent invite is refused rather than silently accepted: the row is the only
    /// record of who was let in, and deleting it would erase that.
    public async Task<Result<bool>> RevokeAsync(
        int byUserId, int inviteId, CancellationToken ct = default)
    {
        if (!await IsOwnerAsync(byUserId, ct))
            return Error.Validation("Запрошення відкликає лише власник цього застосунку.");

        var invite = await db.Invites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.CreatedByUserId == byUserId, ct);
        if (invite is null) return Error.NotFound("Запрошення не знайдено.");
        if (invite.UsedByUserId is not null)
            return Error.Validation("Це запрошення вже використане — акаунт створено.");

        db.Invites.Remove(invite);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<Account>> RedeemAsync(
        string code, string email, string password, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var invite = await db.Invites
            .FirstOrDefaultAsync(i => i.CodeHash == Invite.HashOf(code ?? string.Empty), ct);

        // One indistinguishable answer for "no such code", "already used" and "too old". The
        // person holding a good link is never told any of this; the person trying links
        // learns nothing about which part was wrong.
        if (invite is null || invite.IsSpent(now))
            return Error.Validation("Запрошення недійсне або вже використане.");

        if (password is null || password.Length < AccountService.MinPasswordLength)
            return Error.Validation(
                $"Пароль має бути не коротшим за {AccountService.MinPasswordLength} символів");

        var normalized = User.NormalizeEmail(email);
        if (normalized.Length < 3 || !normalized.Contains('@'))
            return Error.Validation("Схоже, це не пошта");
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return Error.Conflict("Такий акаунт уже існує.");

        var user = new User
        {
            Email = normalized,
            PasswordHash = hasher.Hash(password),
            SecurityStamp = User.NewStamp(),
            CreatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        invite.UsedByUserId = user.Id;
        invite.UsedAt = now;
        await db.SaveChangesAsync(ct);

        // Without this the new account opens to an app with no categories and no allocation
        // scheme, which cannot record a single expense.
        await provisioning.ProvisionAsync(user.Id, ct);

        return Result<Account>.Ok(new Account(user.Id, user.Email, user.SecurityStamp));
    }

    private async Task<bool> IsOwnerAsync(int userId, CancellationToken ct) =>
        await db.Users.AnyAsync(u => u.Id == userId && u.IsOwner, ct);
}
