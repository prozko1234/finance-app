using FinanceApp.Api.Tests.Integration;
using FinanceApp.Domain;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// The promise the whole multi-account change rests on: one person's money is invisible to
/// another, and that is enforced by the database context rather than by every query
/// remembering to ask. The failure these guard against is silent — nothing throws, nothing
/// logs, someone simply reads a stranger's finances.
public class OwnershipTests
{
    /// The one that cannot rot. A table added next year is covered the moment it implements
    /// the marker, and if somebody forgets, this fails with the type name in the message
    /// rather than leaking rows in production.
    ///
    /// The exceptions are listed by hand on purpose: each is a deliberate decision that
    /// should have to be re-argued to be added to.
    [Fact]
    public void Every_table_that_holds_a_persons_data_is_owned()
    {
        using var mem = new SqliteInMemory();

        var shared = new[]
        {
            // The NBP rate for a day is one fact for everybody; copying it per account would
            // multiply rows to say the same thing.
            typeof(FxRate),
            // The account itself cannot belong to an account.
            typeof(User),
            // Looked up by its hash to discover WHO is asking, before there is anyone to
            // filter by. Scoped by DeviceTokenService instead.
            typeof(DeviceToken),
        };

        var unowned = mem.Db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => !shared.Contains(t) && !typeof(IOwnedByUser).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unowned.Count == 0,
            $"These tables hold data with no owner, so every account would share them: " +
            $"{string.Join(", ", unowned)}. Implement IOwnedByUser, or add a reasoned " +
            $"exception to this test.");
    }

    [Fact]
    public async Task Another_account_sees_none_of_it()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var category = await mem.Db.Categories.FirstAsync();

        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense,
            CurrencyOriginal = "PLN", AmountOriginal = 250m, AmountBase = 250m,
            FxRate = 1m, FxDate = today, Date = today, CategoryId = category.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        mem.Db.Envelopes.Add(new Envelope { Name = "Подушка", Kind = BucketKind.Savings });
        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Expense,
            AmountOriginal = 60m, CurrencyOriginal = "PLN", CategoryId = category.Id,
            StartsOn = today, Unit = RecurrenceUnit.Month, Interval = 1, Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        await using var other = mem.As(userId: 2);

        Assert.Empty(await other.Transactions.ToListAsync());
        Assert.Empty(await other.Envelopes.ToListAsync());
        Assert.Empty(await other.RecurringExpenses.ToListAsync());
        Assert.Empty(await other.Categories.ToListAsync());
        Assert.Empty(await other.AllocationSchemes.ToListAsync());

        // And the rows are really there — the emptiness above is the filter doing its job,
        // not a test that saved nothing.
        Assert.Single(await mem.Db.Transactions.ToListAsync());
        Assert.NotEmpty(await other.Transactions.IgnoreQueryFilters().ToListAsync());
    }

    /// Reading someone else's row by its id is the way a filter is usually got around: the
    /// query names a primary key, so there is nothing left to scope. There is here.
    [Fact]
    public async Task Naming_someone_elses_row_by_id_finds_nothing()
    {
        using var mem = new SqliteInMemory();
        var mine = await mem.Db.Categories.FirstAsync();

        await using var other = mem.As(userId: 2);

        Assert.Null(await other.Categories.FirstOrDefaultAsync(c => c.Id == mine.Id));
        Assert.Null(await other.Categories.FindAsync(mine.Id));
    }

    /// Two people naming a jar the same thing is not a conflict — it was, while the unique
    /// index was on the name alone, and the second person would have met a database error
    /// with nothing to explain it.
    [Fact]
    public async Task Two_accounts_may_use_the_same_names()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Envelopes.Add(new Envelope { Name = "Подушка", Kind = BucketKind.Savings });
        await mem.Db.SaveChangesAsync();

        await using var other = mem.As(userId: 2);
        other.Envelopes.Add(new Envelope { Name = "Подушка", Kind = BucketKind.Savings });

        await other.SaveChangesAsync();

        Assert.Single(await other.Envelopes.ToListAsync());
    }

    /// Each account runs its own allocation scheme, and every one of them is active. With the
    /// uniqueness on IsActive alone, the first account to have one would have been the only
    /// account in the database allowed to.
    [Fact]
    public async Task Every_account_may_have_its_own_active_scheme()
    {
        using var mem = new SqliteInMemory();
        Assert.True(await mem.Db.AllocationSchemes.AnyAsync(s => s.IsActive));

        await using var other = mem.As(userId: 2);
        other.AllocationSchemes.Add(new AllocationScheme
        {
            Name = "Своя", IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
        });

        await other.SaveChangesAsync();

        Assert.True(await other.AllocationSchemes.AnyAsync(s => s.IsActive));
    }

    /// A write with nobody signed in used to be impossible to express; now it is a row that
    /// belongs to no one and that no query will ever return again. Better to refuse loudly.
    [Fact]
    public async Task A_write_with_nobody_signed_in_is_refused()
    {
        using var mem = new SqliteInMemory();
        await using var nobody = mem.As(userId: null);

        nobody.Envelopes.Add(new Envelope { Name = "Нічия", Kind = BucketKind.Savings });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nobody.SaveChangesAsync());
        Assert.Contains("no signed-in account", thrown.Message);
    }
}
