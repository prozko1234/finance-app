using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Savings;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Dev;

public interface IDevDataService
{
    Task ResetAsync(CancellationToken ct = default);
    Task SeedExampleAsync(CancellationToken ct = default);
}

/// Local development only — wiping and re-seeding the database from the UI, so a flow can
/// be re-tested from a known state without hand-deleting rows. The endpoints that expose
/// this are registered ONLY in the Development environment; there is no auth yet, so this
/// must never be reachable from a deployed build.
public sealed class DevDataService(IAppDbContext db) : IDevDataService
{
    /// Everything except categories — those are seeded by the migration and everything
    /// else references them.
    public async Task ResetAsync(CancellationToken ct = default)
    {
        db.Transactions.RemoveRange(await db.Transactions.ToListAsync(ct));
        db.RecurringExpenses.RemoveRange(await db.RecurringExpenses.ToListAsync(ct));
        db.SavingsEntries.RemoveRange(await db.SavingsEntries.ToListAsync(ct));
        db.SavingsPlans.RemoveRange(await db.SavingsPlans.ToListAsync(ct));
        db.TaxProfiles.RemoveRange(await db.TaxProfiles.ToListAsync(ct));

        await db.SaveChangesAsync(ct);
    }

    /// A believable month for Bohdan's real scenario: one B2B invoice, a few everyday
    /// expenses, two subscriptions, a savings plan with some history.
    public async Task SeedExampleAsync(CancellationToken ct = default)
    {
        await ResetAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var first = new DateOnly(today.Year, today.Month, 1);
        var categories = await db.Categories.OrderBy(c => c.Id).ToListAsync(ct);
        int CategoryId(string name) =>
            categories.FirstOrDefault(c => c.Name == name)?.Id ?? categories[0].Id;

        db.TaxProfiles.Add(new TaxProfile
        {
            Regime = TaxRegime.Ryczalt,
            RyczaltRate = 0.12m,
            VatPayer = true,
            VatRate = 0.23m,
            ZusType = ZusType.Duzy,
            ZusSocial = PolishTaxDefaults2026.SuggestZusSocial(ZusType.Duzy, chorobowe: false),
            HealthContribution = PolishTaxDefaults2026.HealthRyczalt60kTo300k,
            Chorobowe = false,
            ValidFrom = new DateOnly(PolishTaxDefaults2026.Year, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // One invoice: 24 600 brutto = 20 000 przychód + 4 600 VAT.
        db.Transactions.Add(Row(TransactionKind.Income, 20_000m, CategoryId("Інше"), first, "Фактура за місяць"));

        var food = CategoryId("Їжа");
        var transport = CategoryId("Транспорт");
        db.Transactions.Add(Row(TransactionKind.Expense, 84.30m, food, Recent(today, 6), "Продукти"));
        db.Transactions.Add(Row(TransactionKind.Expense, 23m, food, Recent(today, 4), "Кава"));
        db.Transactions.Add(Row(TransactionKind.Expense, 129.99m, food, Recent(today, 2), "Продукти"));
        db.Transactions.Add(Row(TransactionKind.Expense, 12m, transport, Recent(today, 1), "Трамвай"));
        db.Transactions.Add(Row(TransactionKind.Expense, 46m, food, today, "Обід"));

        db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 49.99m, CurrencyOriginal = "PLN",
            CategoryId = CategoryId("Розваги"), DayOfMonth = 10, Active = true, Note = "Netflix",
            // Without this the row lands on year 1 and materialization walks every month since.
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 9.99m, CurrencyOriginal = "USD",
            CategoryId = CategoryId("Інше"), DayOfMonth = 20, Active = true, Note = "iCloud",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.SavingsPlans.Add(new SavingsPlan
        {
            Mode = SavingsMode.Percent, Value = 10m, Active = true, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SavingsEntries.Add(new SavingsEntry
        {
            Date = first, Kind = SavingsEntryKind.Deposit,
            AmountOriginal = 800m, AmountBase = 800m, FxRate = 1m, FxDate = first,
            Note = "Перший внесок", CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    /// Clamped so a seed run early in the month does not land in the previous one.
    private static DateOnly Recent(DateOnly today, int daysAgo) =>
        today.Day > daysAgo ? today.AddDays(-daysAgo) : new DateOnly(today.Year, today.Month, 1);

    private static Transaction Row(
        TransactionKind kind, decimal amount, int categoryId, DateOnly date, string note) => new()
    {
        Kind = kind,
        AmountOriginal = amount,
        CurrencyOriginal = "PLN",
        AmountBase = amount,
        FxRate = 1m,
        FxDate = date,
        CategoryId = categoryId,
        Frequency = Frequency.OneOff,
        Date = date,
        Note = note,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
