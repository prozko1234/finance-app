using System.Linq.Expressions;
using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Debts;
using FinanceApp.Domain.Push;
using FinanceApp.Domain.Savings;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null)
    : DbContext(options), IAppDbContext
{
    /// Read by the query filters below. A property rather than a captured local because EF
    /// treats member access on the context as a query parameter: the filter is compiled once
    /// and re-evaluated per request, instead of baking one user id into the query cache.
    public int? CurrentUserId => currentUser?.UserId;

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<OpeningBalance> OpeningBalances => Set<OpeningBalance>();
    public DbSet<PeriodCarryover> PeriodCarryovers => Set<PeriodCarryover>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<TaxActuals> TaxActuals => Set<TaxActuals>();
    public DbSet<SavingsPlan> SavingsPlans => Set<SavingsPlan>();
    public DbSet<Envelope> Envelopes => Set<Envelope>();
    public DbSet<SavingsEntry> SavingsEntries => Set<SavingsEntry>();
    public DbSet<AllocationScheme> AllocationSchemes => Set<AllocationScheme>();
    public DbSet<AllocationBucket> AllocationBuckets => Set<AllocationBucket>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();
    public DbSet<RecurringSkip> RecurringSkips => Set<RecurringSkip>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60).IsRequired();
            e.Property(c => c.Icon).HasMaxLength(16);
            e.Property(c => c.Color).HasMaxLength(9);
            e.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10)
                // Every category that existed before income had its own is an expense one.
                .HasDefaultValue(CategoryKind.Expense);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(t => t.AmountOriginal).HasPrecision(18, 2);
            e.Property(t => t.AmountBase).HasPrecision(18, 2);
            e.Property(t => t.FxRate).HasPrecision(18, 6);
            e.Property(t => t.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(t => t.MerchantRaw).HasMaxLength(200);
            e.Property(t => t.Note).HasMaxLength(500);
            // Store enums as text — readable in the DB and stable if the order changes.
            e.Property(t => t.Frequency).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Source).HasConversion<string>().HasMaxLength(15);
            e.Property(t => t.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(10)
                // Every row written before charges could be pending was money that had
                // already moved, so the backfill has to be Posted, not the CLR default.
                .HasDefaultValue(TxStatus.Posted);
            e.Property(t => t.GrossWithVat).HasPrecision(18, 2);
            e.Property(t => t.VatAmount).HasPrecision(18, 2);
            e.HasOne(t => t.Category)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.Date);
            // One materialized transaction per recurring expense per due date (idempotency).
            e.HasIndex(t => new { t.RecurringExpenseId, t.Date })
                .IsUnique()
                .HasFilter("\"RecurringExpenseId\" IS NOT NULL");
            e.HasOne(t => t.RecurringExpense)
                .WithMany()
                .HasForeignKey(t => t.RecurringExpenseId)
                .OnDelete(DeleteBehavior.SetNull);
            // SetNull, not Restrict: losing the envelope must not lose the expense. The row
            // falls back to being paid out of ordinary money, which is what it becomes.
            e.HasOne(t => t.Envelope)
                .WithMany()
                .HasForeignKey(t => t.EnvelopeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TaxProfile>(e =>
        {
            e.Property(x => x.RyczaltRate).HasPrecision(6, 4);
            e.Property(x => x.VatRate).HasPrecision(6, 4);
            e.Property(x => x.ZusSocial).HasPrecision(18, 2);
            e.Property(x => x.HealthContribution).HasPrecision(18, 2);
            e.Property(x => x.Regime).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ZusType).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<TaxActuals>(e =>
        {
            e.Property(x => x.ZusSocial).HasPrecision(18, 2);
            e.Property(x => x.Health).HasPrecision(18, 2);
            e.Property(x => x.Pit).HasPrecision(18, 2);
            // One row per month per account: two sets of "what the bookkeeper said" for the
            // same March could only ever disagree.
            e.HasIndex(x => new { x.UserId, x.Month }).IsUnique();
        });

        b.Entity<SavingsPlan>(e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 2);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<SavingsEntry>(e =>
        {
            e.Property(x => x.AmountOriginal).HasPrecision(18, 2);
            e.Property(x => x.AmountBase).HasPrecision(18, 2);
            e.Property(x => x.FxRate).HasPrecision(18, 6);
            e.Property(x => x.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(x => x.TransferKey).HasMaxLength(36);
            // Both halves of a transfer are always read together, and there are only ever two.
            e.HasIndex(x => x.TransferKey);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.Date);
            // Restrict, not Cascade: deleting an envelope must never take a history of real
            // money movements with it.
            e.HasOne(x => x.Envelope)
                .WithMany()
                .HasForeignKey(x => x.EnvelopeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Envelope>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TargetAmount).HasPrecision(18, 2);
            // The name is the identity a scheme's bucket is matched against, so two
            // envelopes called the same thing would split one balance in two. Scoped to the
            // account: "Подушка" is one jar per person, not one jar in the world.
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        });

        b.Entity<RecurringExpense>(e =>
        {
            e.Property(r => r.AmountOriginal).HasPrecision(18, 2);
            e.Property(r => r.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Unit).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Note).HasMaxLength(500);
            e.HasOne(r => r.Category)
                .WithMany()
                .HasForeignKey(r => r.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RecurringSkip>(e =>
        {
            // One skip per occurrence: a second row for the same date would mean the same
            // charge was refused twice, which is not a thing that can happen.
            e.HasIndex(x => new { x.RecurringExpenseId, x.Date }).IsUnique();
            // Cascade: without the rule there are no occurrences left to refuse.
            e.HasOne(x => x.RecurringExpense)
                .WithMany()
                .HasForeignKey(x => x.RecurringExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Debt>(e =>
        {
            e.Property(x => x.Person).HasMaxLength(60).IsRequired();
            e.Property(x => x.AmountOriginal).HasPrecision(18, 2);
            e.Property(x => x.AmountBase).HasPrecision(18, 2);
            e.Property(x => x.FxRate).HasPrecision(18, 6);
            e.Property(x => x.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.Date);
            // SetNull for the same reason a payment's jar is: losing the envelope must not lose
            // the debt. The row falls back to having been lent out of ordinary money.
            e.HasOne(x => x.OriginEnvelope)
                .WithMany()
                .HasForeignKey(x => x.OriginEnvelopeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DebtPayment>(e =>
        {
            e.Property(x => x.AmountOriginal).HasPrecision(18, 2);
            e.Property(x => x.AmountBase).HasPrecision(18, 2);
            e.Property(x => x.FxRate).HasPrecision(18, 6);
            e.Property(x => x.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.Date);
            // Cascade: payments are part of the debt, not records of their own. Deleting the
            // debt has to take them, or the sums that read them would keep charging the daily
            // norm for repayments on something that no longer exists.
            e.HasOne(x => x.Debt)
                .WithMany()
                .HasForeignKey(x => x.DebtId)
                .OnDelete(DeleteBehavior.Cascade);
            // SetNull, like an expense paid from a jar: losing the envelope must not lose the
            // payment. The row falls back to having come out of ordinary money.
            e.HasOne(x => x.Envelope)
                .WithMany()
                .HasForeignKey(x => x.EnvelopeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PushSubscription>(e =>
        {
            e.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(100).IsRequired();
            // A browser that re-subscribes gets the same endpoint back, and a second row for
            // it would deliver the same reminder twice.
            e.HasIndex(x => x.Endpoint).IsUnique();
        });

        b.Entity<FxRate>(e =>
        {
            e.HasKey(r => new { r.Currency, r.Date });
            e.Property(r => r.Currency).HasMaxLength(3);
            e.Property(r => r.PlnPerUnit).HasPrecision(18, 6);
            e.Property(r => r.Source).HasMaxLength(10);
        });

        b.Entity<AppSettings>(e =>
        {
            e.Property(x => x.DisplayCurrency).HasMaxLength(3).IsRequired();
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            // Emails are stored normalized, so this index is what makes one address one account.
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<DeviceToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(DeviceToken.MaxNameLength).IsRequired();
            e.Property(x => x.IssuedStamp).HasMaxLength(64).IsRequired();
            // Every authenticated request from a device is a lookup by this column, so it
            // has to be an index rather than a scan. Unique because two rows for one secret
            // could only ever be a bug.
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        b.Entity<Invite>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Note).HasMaxLength(60);
            // Redeeming is a lookup by this column and nothing else, and two rows for one
            // code could only ever be a bug.
            e.HasIndex(x => x.CodeHash).IsUnique();
        });

        b.Entity<MerchantRule>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(MerchantRule.MaxKeyLength).IsRequired();
            // One rule per shop: a second row for the same key could only ever be two
            // different answers to the same question. Per account — two people may well
            // file the same shop under different categories.
            e.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
            // Restrict: losing a category must not silently take its rules with it —
            // CategoryService moves transactions to "Інше", and rules follow the same way.
            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OpeningBalance>(e =>
        {
            e.Property(x => x.AmountOriginal).HasPrecision(18, 2);
            e.Property(x => x.AmountBase).HasPrecision(18, 2);
            e.Property(x => x.CurrencyOriginal).HasMaxLength(3).IsRequired();
        });

        b.Entity<PeriodCarryover>(e =>
        {
            e.Property(x => x.AmountBase).HasPrecision(18, 2);
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
            // Asked once per period, enforced here and not only in the service: two answers
            // for one period would mean the leftover was moved twice. Per account, or one
            // person answering would answer for everyone whose period starts that day.
            e.HasIndex(x => new { x.UserId, x.PeriodStart }).IsUnique();
            e.HasOne<Envelope>()
                .WithMany()
                .HasForeignKey(x => x.EnvelopeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AllocationScheme>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Preset).HasMaxLength(40);
            // Exactly one scheme may be active PER ACCOUNT; the DB enforces it, not just the
            // service. Without the UserId in front, the first account to activate a scheme
            // would be the only account in the database allowed to have one — and the second
            // would meet a unique-constraint violation with nothing to explain it.
            e.HasIndex(x => new { x.UserId, x.IsActive }).IsUnique().HasFilter("\"IsActive\" = 1");
            e.HasMany(x => x.Buckets)
                .WithOne(x => x.Scheme!)
                .HasForeignKey(x => x.SchemeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AllocationBucket>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Percent).HasPrecision(5, 2);
        });

        ApplyOwnershipFilters(b);
    }

    /// One filter per owned entity, built by walking the model rather than written out by
    /// hand. An entity added later is covered the moment it implements the marker, and there
    /// is no line for a future change to forget — the failure being prevented here is silent
    /// and shows one person another person's money.
    ///
    /// Note what is deliberately NOT filtered: <see cref="User"/> is the tenant itself, and
    /// <see cref="DeviceToken"/> is looked up by its hash to discover WHO is asking, before
    /// there is a current user to filter by. Both are scoped by their own services instead.
    private void ApplyOwnershipFilters(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (!typeof(IOwnedByUser).IsAssignableFrom(entity.ClrType)) continue;

            entity.AddIndex(entity.FindProperty(nameof(IOwnedByUser.UserId))!);

            // e => e.UserId == this.CurrentUserId
            var row = Expression.Parameter(entity.ClrType, "e");
            var owner = Expression.Property(row, nameof(IOwnedByUser.UserId));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentUserId));

            b.Entity(entity.ClrType).HasQueryFilter(
                Expression.Lambda(
                    Expression.Equal(Expression.Convert(owner, typeof(int?)), current), row));
        }
    }

    /// New rows are stamped with the signed-in account here, so no service ever has to
    /// remember to. A write with nobody signed in is a bug rather than a public row, and
    /// says so loudly instead of landing somewhere unowned.
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampOwner();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        StampOwner();
        return base.SaveChanges();
    }

    private void StampOwner()
    {
        foreach (var entry in ChangeTracker.Entries<IOwnedByUser>())
        {
            if (entry.State != EntityState.Added || entry.Entity.UserId != 0) continue;

            entry.Entity.UserId = CurrentUserId
                ?? throw new InvalidOperationException(
                    $"Cannot save {entry.Entity.GetType().Name} with no signed-in account.");
        }
    }

}
