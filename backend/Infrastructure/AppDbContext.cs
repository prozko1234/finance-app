using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<SavingsPlan> SavingsPlans => Set<SavingsPlan>();
    public DbSet<SavingsEntry> SavingsEntries => Set<SavingsEntry>();

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
            e.Property(t => t.Priority).HasConversion<string>().HasMaxLength(10);
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
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.Date);
        });

        b.Entity<RecurringExpense>(e =>
        {
            e.Property(r => r.AmountOriginal).HasPrecision(18, 2);
            e.Property(r => r.CurrencyOriginal).HasMaxLength(3).IsRequired();
            e.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10);
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

        b.Entity<Budget>(e =>
        {
            e.Property(x => x.MonthlyAmount).HasPrecision(18, 2);
        });
    }

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
