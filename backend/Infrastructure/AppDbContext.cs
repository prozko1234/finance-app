using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60).IsRequired();
            e.Property(c => c.Icon).HasMaxLength(16);
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
            // Enum-и зберігаємо як текст — читабельно в БД, стабільно при зміні порядку.
            e.Property(t => t.Priority).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Frequency).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Source).HasConversion<string>().HasMaxLength(15);
            e.HasOne(t => t.Category)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.Date);
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
        new() { Id = 1, Name = "Їжа", Icon = "🍽" },
        new() { Id = 2, Name = "Транспорт", Icon = "🚌" },
        new() { Id = 3, Name = "Житло", Icon = "🏠" },
        new() { Id = 4, Name = "Здоров'я", Icon = "💊" },
        new() { Id = 5, Name = "Розваги", Icon = "🎮" },
        new() { Id = 6, Name = "Інше", Icon = "📦" },
    ];
}
