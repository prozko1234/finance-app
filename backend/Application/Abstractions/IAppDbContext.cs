using FinanceApp.Domain;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Abstractions;

/// Persistence abstraction for the Application layer. Implemented by AppDbContext in
/// Infrastructure. Gives testability (an in-memory context can be substituted) without
/// repositories: an EF DbContext is itself a Unit of Work + repositories.
public interface IAppDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<Category> Categories { get; }
    DbSet<OpeningBalance> OpeningBalances { get; }
    DbSet<PeriodCarryover> PeriodCarryovers { get; }
    DbSet<FxRate> FxRates { get; }
    DbSet<RecurringExpense> RecurringExpenses { get; }
    DbSet<TaxProfile> TaxProfiles { get; }
    DbSet<SavingsPlan> SavingsPlans { get; }
    DbSet<SavingsEntry> SavingsEntries { get; }
    DbSet<Envelope> Envelopes { get; }
    DbSet<AllocationScheme> AllocationSchemes { get; }
    DbSet<AllocationBucket> AllocationBuckets { get; }
    DbSet<AppSettings> AppSettings { get; }
    DbSet<User> Users { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<Invite> Invites { get; }
    DbSet<MerchantRule> MerchantRules { get; }
    DbSet<RecurringSkip> RecurringSkips { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
