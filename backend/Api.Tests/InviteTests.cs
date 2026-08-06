using FinanceApp.Application.Auth;
using FinanceApp.Domain.Auth;
using FinanceApp.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// The only way to make an account on this instance. Everything here is about who may not:
/// registration that answers "yes" to the wrong person hands a stranger a place on somebody's
/// personal server, next to their finances.
public class InviteTests
{
    private static InviteService Sut(SqliteInMemory mem) =>
        new(mem.Db, Hasher, new UserProvisioningService(mem.Db));

    /// Real algorithm, cheap settings — what PBKDF2 costs in production has its own test.
    private static readonly Pbkdf2PasswordHasher Hasher = new(iterations: 1_000);

    private static async Task<User> AccountAsync(
        SqliteInMemory mem, string email, bool owner)
    {
        var user = new User
        {
            Email = email, PasswordHash = Hasher.Hash("irrelevant-but-long"),
            SecurityStamp = User.NewStamp(), IsOwner = owner,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.Users.Add(user);
        await mem.Db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task An_invite_lets_exactly_one_account_be_made()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);

        var created = await sut.CreateAsync(owner.Id, "Оля");
        Assert.True(created.IsSuccess);

        var first = await sut.RedeemAsync(created.Value!.Code, "olya@x.com", "dovhyi-parol");
        Assert.True(first.IsSuccess);

        // The same link a second time is refused — this is the whole point of "one-time".
        var second = await sut.RedeemAsync(created.Value.Code, "hto@x.com", "dovhyi-parol");
        Assert.False(second.IsSuccess);
    }

    /// A new account that opens to no categories and no allocation scheme cannot record a
    /// single expense, so registering has to provision as well as create.
    [Fact]
    public async Task A_new_account_starts_with_its_own_categories_and_scheme()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);

        var created = await sut.CreateAsync(owner.Id, "Оля");
        var joined = await sut.RedeemAsync(created.Value!.Code, "olya@x.com", "dovhyi-parol");

        await using var theirs = mem.As(joined.Value!.Id);
        Assert.NotEmpty(await theirs.Categories.ToListAsync());
        Assert.True(await theirs.AllocationSchemes.AnyAsync(s => s.IsActive));

        // And none of it is the owner's — the two sets are separate rows.
        Assert.Empty(await theirs.Transactions.ToListAsync());
    }

    /// The person who was invited must not be able to invite. Otherwise one link handed to
    /// one friend quietly becomes an open door.
    [Fact]
    public async Task Only_the_owner_may_invite()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var guest = await AccountAsync(mem, "olya@x.com", owner: false);
        var sut = Sut(mem);

        Assert.False((await sut.CreateAsync(guest.Id, "хтось")).IsSuccess);
        Assert.False((await sut.ListAsync(guest.Id)).IsSuccess);
        Assert.True((await sut.CreateAsync(owner.Id, "хтось")).IsSuccess);
    }

    [Fact]
    public async Task A_made_up_code_registers_nobody()
    {
        using var mem = new SqliteInMemory();
        await AccountAsync(mem, "bohdan@x.com", owner: true);

        var result = await Sut(mem).RedeemAsync(Invite.NewCode(), "hto@x.com", "dovhyi-parol");

        Assert.False(result.IsSuccess);
        Assert.Single(await mem.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task An_expired_invite_is_refused()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);
        var created = await sut.CreateAsync(owner.Id, "Оля");

        var invite = await mem.Db.Invites.SingleAsync();
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await mem.Db.SaveChangesAsync();

        Assert.False((await sut.RedeemAsync(created.Value!.Code, "olya@x.com", "dovhyi-parol")).IsSuccess);
    }

    /// The code is a credential that creates accounts, so the database holds only its digest
    /// — the same rule device tokens follow, for the same reason.
    [Fact]
    public async Task The_code_is_never_stored_in_readable_form()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);

        var created = await Sut(mem).CreateAsync(owner.Id, "Оля");
        var stored = await mem.Db.Invites.SingleAsync();

        Assert.NotEqual(created.Value!.Code, stored.CodeHash);
        Assert.Equal(Invite.HashOf(created.Value.Code), stored.CodeHash);

        // And the list the owner reads back never carries it.
        var listed = await Sut(mem).ListAsync(owner.Id);
        Assert.DoesNotContain(created.Value.Code, System.Text.Json.JsonSerializer.Serialize(listed.Value));
    }

    [Fact]
    public async Task A_short_password_is_refused_before_an_account_exists()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);
        var created = await sut.CreateAsync(owner.Id, "Оля");

        Assert.False((await sut.RedeemAsync(created.Value!.Code, "olya@x.com", "korotkyi")).IsSuccess);

        // And the invite survives the failed attempt, or a typo would burn the link.
        Assert.Single(await mem.Db.Users.ToListAsync());
        Assert.True((await sut.RedeemAsync(created.Value.Code, "olya@x.com", "dovhyi-parol")).IsSuccess);
    }

    /// Deleting a spent invite would erase the only record of who was let in.
    [Fact]
    public async Task A_used_invite_cannot_be_revoked_away()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);
        var created = await sut.CreateAsync(owner.Id, "Оля");
        await sut.RedeemAsync(created.Value!.Code, "olya@x.com", "dovhyi-parol");

        Assert.False((await sut.RevokeAsync(owner.Id, created.Value.Id)).IsSuccess);

        var listed = await sut.ListAsync(owner.Id);
        Assert.Equal("olya@x.com", listed.Value!.Single().UsedByEmail);
    }

    [Fact]
    public async Task An_unused_invite_can_be_taken_back()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);
        var created = await sut.CreateAsync(owner.Id, "Оля");

        Assert.True((await sut.RevokeAsync(owner.Id, created.Value!.Id)).IsSuccess);
        Assert.False((await sut.RedeemAsync(created.Value.Code, "olya@x.com", "dovhyi-parol")).IsSuccess);
    }

    [Fact]
    public async Task One_address_is_one_account()
    {
        using var mem = new SqliteInMemory();
        var owner = await AccountAsync(mem, "bohdan@x.com", owner: true);
        var sut = Sut(mem);
        var created = await sut.CreateAsync(owner.Id, "Оля");

        var result = await sut.RedeemAsync(created.Value!.Code, "BOHDAN@x.com ", "dovhyi-parol");

        Assert.False(result.IsSuccess);
    }
}
