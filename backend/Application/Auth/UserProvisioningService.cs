using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Auth;

public interface IUserProvisioning
{
    /// Gives a brand-new account the rows it cannot function without. Does nothing when the
    /// account already has categories, so a retry after a half-finished registration cannot
    /// hand someone two of everything.
    Task ProvisionAsync(int userId, CancellationToken ct = default);

    /// A top-up for accounts made before income had categories of its own. Provisioning only
    /// runs at registration, so without this an existing account would open the income form to
    /// an empty list and be unable to write an invoice at all.
    ///
    /// Idempotent by construction: it fires only when there is not a single income category,
    /// which after the first call is never true again. Somebody who deletes all of theirs on
    /// purpose gets the starting set back — the alternative is a form with nothing in it.
    /// Unlike ProvisionAsync this runs as the signed-in user, so the query filter is the scope.
    Task EnsureIncomeCategoriesAsync(CancellationToken ct = default);
}

/// What a new account starts life with.
///
/// This used to be <c>HasData</c> in the model, which put the rows in a migration — one
/// copy per DATABASE, with fixed ids. That works for exactly one account and cannot be
/// stretched: a migration runs once, and the second person to register would find someone
/// else's categories or none at all. So the seed moved here, where it runs per account.
///
/// The rows are written with an explicit UserId rather than leaning on the context's
/// stamping, because provisioning happens while registering — the new account is not the one
/// signed in, and may be nobody at all.
public sealed class UserProvisioningService(IAppDbContext db) : IUserProvisioning
{
    public async Task ProvisionAsync(int userId, CancellationToken ct = default)
    {
        // Query filters are scoped to whoever is signed in, which during registration is not
        // this user — so the check has to ask about the account by name.
        var already = await db.Categories.IgnoreQueryFilters()
            .AnyAsync(c => c.UserId == userId, ct);
        if (already) return;

        foreach (var c in StartingCategories())
        {
            c.UserId = userId;
            db.Categories.Add(c);
        }

        var scheme = new AllocationScheme
        {
            UserId = userId,
            Name = AllocationPresets.Find(AllocationPresets.DailyNormOnly)!.Name,
            Preset = AllocationPresets.DailyNormOnly,
            IsActive = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            Buckets =
            [
                new AllocationBucket
                {
                    UserId = userId,
                    Name = "На витрати",
                    Kind = BucketKind.Spending,
                    Percent = 100m,
                    SortOrder = 0,
                },
            ],
        };
        db.AllocationSchemes.Add(scheme);

        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureIncomeCategoriesAsync(CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(c => c.Kind == CategoryKind.Income, ct)) return;

        foreach (var c in IncomeCategories()) db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
    }

    /// Named after where the money actually goes, from a year of real statements rather than
    /// from a tidy-looking list. Two splits earn their place: delivery is not groceries (in
    /// that year, 22 765 zł against 11 127 zł — one category would have hidden the bigger
    /// half), and subscriptions are not games, which is the difference between a charge you
    /// can cancel in one move and one you chose to make.
    ///
    /// Transfers to people are a category because they were 12 866 zł with nowhere to go, and
    /// "Інше" that large answers nothing.
    ///
    /// Built fresh per call rather than held in a static array: these are tracked entities,
    /// and handing the same instances to a second account would re-attach rows that already
    /// belong to the first.
    private static List<Category> StartingCategories() =>
    [
        new() { Name = "Продукти", Icon = "🛒", SortOrder = 1 },
        new() { Name = "Транспорт", Icon = "🚌", SortOrder = 2 },
        new() { Name = "Житло", Icon = "🏠", SortOrder = 3 },
        new() { Name = "Здоров'я", Icon = "💊", SortOrder = 4 },
        new() { Name = "Розваги", Icon = "🎮", SortOrder = 5 },
        // Fallback category: orphaned transactions land here, so it cannot be deleted.
        new() { Name = "Інше", Icon = "📦", SortOrder = 99, IsSystem = true },
        new() { Name = "Доставка", Icon = "🛵", SortOrder = 6 },
        new() { Name = "Кафе й бари", Icon = "☕", SortOrder = 7 },
        new() { Name = "Підписки", Icon = "🔁", SortOrder = 8 },
        new() { Name = "Перекази", Icon = "👤", SortOrder = 9 },
        .. IncomeCategories(),
    ];

    /// Where money comes FROM. Income used to hang off whatever expense category was first, so
    /// the app filed a salary under "Продукти" and covered for it on every screen that showed
    /// a row.
    ///
    /// Six, not a taxonomy: for someone invoicing from Poland the answer is almost always the
    /// same two, and the rest exist so that the odd one out has somewhere to go that is not
    /// "Інше". Money coming back from a person is deliberately absent — a repaid debt is not
    /// revenue and never becomes a transaction at all.
    internal static List<Category> IncomeCategories() =>
    [
        new() { Kind = CategoryKind.Income, Name = "Зарплата", Icon = "💼", SortOrder = 1 },
        new() { Kind = CategoryKind.Income, Name = "Фактура", Icon = "🧾", SortOrder = 2 },
        new() { Kind = CategoryKind.Income, Name = "Фріланс", Icon = "💻", SortOrder = 3 },
        new() { Kind = CategoryKind.Income, Name = "Продаж", Icon = "🏷", SortOrder = 4 },
        new() { Kind = CategoryKind.Income, Name = "Подарунок", Icon = "🎁", SortOrder = 5 },
        // The fallback for income, separate from the expense one: a salary moved into the
        // spending "Інше" would sit in a list that only ever sums what went out.
        new() { Kind = CategoryKind.Income, Name = "Інше надходження", Icon = "💰", SortOrder = 99, IsSystem = true },
    ];
}
