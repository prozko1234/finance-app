using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<OpeningBalance> OpeningBalances => Set<OpeningBalance>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<SavingsPlan> SavingsPlans => Set<SavingsPlan>();
    public DbSet<Envelope> Envelopes => Set<Envelope>();
    public DbSet<SavingsEntry> SavingsEntries => Set<SavingsEntry>();
    public DbSet<AllocationScheme> AllocationSchemes => Set<AllocationScheme>();
    public DbSet<AllocationBucket> AllocationBuckets => Set<AllocationBucket>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60).IsRequired();
            e.Property(c => c.Icon).HasMaxLength(16);
            e.Property(c => c.Color).HasMaxLength(9);
            e.HasData(SeedCategories);
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
            // envelopes called the same thing would split one balance in two.
            e.HasIndex(x => x.Name).IsUnique();
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

        b.Entity<MerchantRule>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(MerchantRule.MaxKeyLength).IsRequired();
            // One rule per shop: a second row for the same key could only ever be two
            // different answers to the same question.
            e.HasIndex(x => x.Key).IsUnique();
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

        b.Entity<AllocationScheme>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Preset).HasMaxLength(40);
            // Exactly one scheme may be active; the DB enforces it, not just the service.
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("\"IsActive\" = 1");
            e.HasMany(x => x.Buckets)
                .WithOne(x => x.Scheme!)
                .HasForeignKey(x => x.SchemeId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasData(DefaultScheme);
        });

        b.Entity<AllocationBucket>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.HasData(DefaultBuckets);
        });
    }

    // Every database starts with the app's pre-schemes behaviour expressed as a scheme:
    // one Spending bucket at 100%. Seeded rather than created on demand so existing
    // databases get an active scheme from the migration alone.
    private static readonly AllocationScheme DefaultScheme = new()
    {
        Id = 1,
        Name = AllocationPresets.Find(AllocationPresets.DailyNormOnly)!.Name,
        Preset = AllocationPresets.DailyNormOnly,
        IsActive = true,
        // Fixed, not UtcNow: seed data must be deterministic or every migration differs.
        UpdatedAt = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
    };

    private static readonly AllocationBucket[] DefaultBuckets =
    [
        new() { Id = 1, SchemeId = 1, Name = "На витрати", Kind = BucketKind.Spending, Percent = 100m, SortOrder = 0 },
    ];

    private static readonly Category[] SeedCategories =
    [
        new() { Id = 1, Name = "Їжа", Icon = "🍽", SortOrder = 1 },
        new() { Id = 2, Name = "Транспорт", Icon = "🚌", SortOrder = 2 },
        new() { Id = 3, Name = "Житло", Icon = "🏠", SortOrder = 3 },
        new() { Id = 4, Name = "Здоров'я", Icon = "💊", SortOrder = 4 },
        new() { Id = 5, Name = "Розваги", Icon = "🎮", SortOrder = 5 },
        // Fallback category: orphaned transactions land here, so it cannot be deleted.
        new() { Id = 6, Name = "Інше", Icon = "📦", SortOrder = 99, IsSystem = true },
    ];
}
