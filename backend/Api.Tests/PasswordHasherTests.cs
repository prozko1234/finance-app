using FinanceApp.Infrastructure.Auth;

namespace FinanceApp.Api.Tests;

/// What stands between a stolen database file and someone's money.
public class PasswordHasherTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new(iterations: 1_000);

    [Fact]
    public void The_password_is_nowhere_in_what_is_stored()
    {
        var hash = Hasher.Hash("correct horse battery");

        Assert.DoesNotContain("correct horse", hash);
        Assert.StartsWith("pbkdf2-sha256$1000$", hash);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // A shared salt would mean two accounts with the same password have the same hash,
        // and one precomputed table cracks both.
        Assert.NotEqual(Hasher.Hash("correct horse battery"), Hasher.Hash("correct horse battery"));
    }

    [Fact]
    public void The_right_password_verifies_and_a_wrong_one_does_not()
    {
        var hash = Hasher.Hash("correct horse battery");

        Assert.True(Hasher.Verify("correct horse battery", hash).Matches);
        Assert.False(Hasher.Verify("correct horse batter", hash).Matches);
        Assert.False(Hasher.Verify("", hash).Matches);
    }

    [Fact]
    public void A_hash_made_with_a_lower_cost_still_verifies_and_asks_to_be_upgraded()
    {
        // The whole point of writing the cost into the hash: raising it later must not lock
        // anyone out, it must upgrade them on their next login.
        var old = new Pbkdf2PasswordHasher(iterations: 500).Hash("correct horse battery");

        var check = Hasher.Verify("correct horse battery", old);

        Assert.True(check.Matches);
        Assert.True(check.NeedsRehash);
    }

    [Fact]
    public void A_hash_at_the_current_cost_is_left_alone()
    {
        var check = Hasher.Verify("correct horse battery", Hasher.Hash("correct horse battery"));

        Assert.True(check.Matches);
        Assert.False(check.NeedsRehash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text password")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$not base64!$aGFzaA==")]
    [InlineData("bcrypt$10$abc$def")]
    public void Garbage_in_the_stored_hash_is_a_refusal_not_a_crash(string stored)
    {
        // A row corrupted or written by some other tool must fail closed, not 500 — and
        // certainly not let anyone in.
        Assert.False(Hasher.Verify("correct horse battery", stored).Matches);
    }
}
