using FinanceApp.Domain;

namespace FinanceApp.Application.Contracts;

/// Create/update transaction request. Currency is a 3-letter ISO code.
/// Date is optional (defaults to today). Base amount and rate are computed on the server.
public record SaveTransactionRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    Priority Priority,
    Frequency Frequency,
    DateOnly? Date,
    string? Merchant,
    string? Note);

public record SaveIncomeRequest(
    decimal Amount,
    bool AmountIncludesVat,
    string Currency,
    DateOnly? Date,
    string? Note);

public record TransactionResponse(
    int Id,
    string Kind,
    decimal? GrossWithVat,
    decimal? VatAmount,
    decimal AmountOriginal,
    string CurrencyOriginal,
    decimal AmountBase,
    decimal FxRate,
    DateOnly FxDate,
    int CategoryId,
    string CategoryName,
    Priority Priority,
    Frequency Frequency,
    string Source,
    DateOnly Date,
    string? Merchant,
    string? Note,
    DateTimeOffset CreatedAt,
    /// The same amount as the user reads it, converted at THIS transaction's date — so a
    /// July expense keeps its July size. Equals AmountBase while reading in PLN.
    decimal AmountDisplay,
    string DisplayCurrency);

public record CategoryResponse(int Id, string Name, string? Icon, string? Color, int SortOrder, bool IsSystem);

public record SaveCategoryRequest(string Name, string? Icon, string? Color);

public record SetBudgetRequest(decimal Amount);

public record BudgetResponse(bool Set, decimal? MonthlyAmount, string Currency, DateTimeOffset? UpdatedAt);

/// Where this month's income went before it became a budget. Explains the gap between
/// "money on the account" and "money you may actually spend". Null when there is no
/// income this month (or no usable tax profile) — then the budget is just the manual one.
public record MonthTaxBreakdown(
    decimal Gross,        // скільки реально прийшло на рахунок (з VAT)
    decimal Revenue,      // przychód — без VAT, база для податків
    decimal Vat,
    decimal ZusSocial,
    decimal Health,
    decimal Tax,
    decimal SetAside,     // VAT + ZUS + здоровотна + податок
    decimal TakeHome,     // = MonthlyBudget
    /// Валюта цього розкладу — завжди базова. Польський рушій рахує у злотих, і саме ці
    /// цифри побачить книгова, тож вони не конвертуються разом з рештою екрана.
    string Currency);

public record SafeToSpendResponse(
    DateOnly Date,
    string Currency,
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal ReservedRecurring,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? DailyNorm,
    decimal SpentToday,
    decimal? LeftToday,
    decimal? TomorrowIfStop,
    decimal? TomorrowIfOnPlan,
    MonthTaxBreakdown? MonthTaxes,
    SavingsSummary Savings,
    AllocationSummary? Allocation = null);

/// Where the month's budget went before the daily norm was computed — the "куди пішов
/// бюджет" row. Null-ish case (the default one-bucket scheme) still comes through, so the
/// UI can decide on its own whether a single 100% bucket is worth showing.
public record AllocationSummary(
    string SchemeName,
    string? Preset,
    decimal Spendable,
    decimal Reserved,
    IReadOnlyList<BucketShareResponse> Buckets);

public record BucketShareResponse(
    int Id, string Name, string Kind, decimal Percent, decimal Amount);

/// The scheme screen: what is active now, and the ready-made schemes to switch to.
public record AllocationResponse(
    AllocationSchemeResponse Active,
    IReadOnlyList<AllocationPresetResponse> Presets);

public record AllocationSchemeResponse(
    string Name, string? Preset, IReadOnlyList<AllocationBucketResponse> Buckets);

public record AllocationBucketResponse(string Name, string Kind, decimal Percent);

public record AllocationPresetResponse(
    string Key, string Name, string Hint, IReadOnlyList<AllocationBucketResponse> Buckets);

/// Either a preset key, or a name plus the user's own buckets.
public record SaveAllocationRequest(
    string? Preset = null, string? Name = null, IReadOnlyList<AllocationBucketResponse>? Buckets = null);

/// The savings envelope, shown on its own: a balance that survives across months,
/// plus how much of this month's goal is still being held back from safe-to-spend.
public record SavingsSummary(
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve);

public record SaveSavingsPlanRequest(string Mode, decimal Value, bool Active);

/// Currency is optional: most movements are in base currency, and an omitted field
/// must not turn into a validation error on the common path.
public record SaveSavingsEntryRequest(
    string Kind, decimal Amount, DateOnly? Date, string? Note, string? Currency = null);

public record SavingsEntryResponse(
    int Id,
    DateOnly Date,
    string Kind,
    /// In base currency — what this movement did to the balance.
    decimal Amount,
    /// What the user actually typed, and in which currency.
    decimal AmountOriginal,
    string CurrencyOriginal,
    string? Note);

public record SavingsResponse(
    string Mode,
    decimal Value,
    bool Active,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve,
    string Currency,
    IReadOnlyList<SavingsEntryResponse> Recent,
    /// Name of the allocation scheme that dictates the goal, or null when the plan below
    /// still decides it. Set = the plan's own value is ignored, and the UI must say so.
    string? GoalFromScheme = null);

public record SaveRecurringRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    int DayOfMonth,
    string? Note,
    bool Active,
    // "Expense" (default) or "Income" — a stable monthly salary is recurring too.
    string? Kind = null,
    bool AmountIncludesVat = true);

public record TaxProfileResponse(
    string Regime,
    decimal RyczaltRate,
    bool VatPayer,
    decimal VatRate,
    string ZusType,
    decimal ZusSocial,
    decimal HealthContribution,
    bool Chorobowe,
    bool StudentUnder26,
    DateOnly ValidFrom,
    decimal MonthlyContributionsTotal);

public record SaveTaxProfileRequest(
    string Regime,
    decimal RyczaltRate,
    bool VatPayer,
    decimal VatRate,
    string ZusType,
    decimal ZusSocial,
    decimal HealthContribution,
    bool Chorobowe,
    bool StudentUnder26 = false);

public record TaxDefaultsResponse(
    int Year,
    decimal DuzyWithChorobowe,
    decimal DuzyWithoutChorobowe,
    decimal PreferencyjnyWithChorobowe,
    decimal PreferencyjnyWithoutChorobowe,
    decimal HealthUnder60k,
    decimal Health60kTo300k,
    decimal HealthOver300k);

public record CalculateTakeHomeRequest(decimal Amount, bool AmountIncludesVat);

/// Answers "what does this invoice actually add to my budget?" while the user is still typing.
/// Deliberately expressed as a DELTA over the month, not as a standalone invoice calculation:
/// ZUS and health are monthly, so a second invoice adds more take-home than the first one did.
/// Showing a per-invoice figure here would contradict the home screen.
public record IncomePreviewResponse(
    decimal InvoiceGross,     // з VAT — скільки прийде на рахунок
    decimal InvoiceVat,
    decimal InvoiceRevenue,   // przychód цієї фактури
    decimal BudgetBefore,     // бюджет місяця зараз
    decimal BudgetAfter,
    decimal BudgetDelta,      // += до бюджету за цю фактуру
    bool IsFirstIncomeThisMonth,
    MonthTaxBreakdown MonthAfter,
    // The savings plan as it would apply to the month's budget after this invoice — shown
    // (and editable) right in the income form, so putting money aside is not a second trip.
    string SavingsMode,
    decimal SavingsValue,
    bool SavingsActive,
    decimal SavingsGoalAfter,
    string Currency);

public record RecurringResponse(
    int Id,
    decimal AmountOriginal,
    string CurrencyOriginal,
    int CategoryId,
    string CategoryName,
    int DayOfMonth,
    bool Active,
    string? Note,
    string Kind,
    bool AmountIncludesVat);

/// App-wide settings. <paramref name="BaseCurrency"/> is what the app stores in; the user
/// only chooses what to read. <paramref name="TaxesInBaseCurrency"/> tells the UI it must
/// say the tax split is still computed in PLN.
/// The statistics screen in one response: the bars, and the breakdown of one month.
/// <paramref name="SelectedMonth"/> is echoed back ("yyyy-MM") because an out-of-range or
/// unparsable request falls back to the current month, and the UI must label what it shows.
public record StatsResponse(
    string Currency,
    IReadOnlyList<MonthStatsResponse> Months,
    string SelectedMonth,
    decimal SelectedExpense,
    IReadOnlyList<CategoryStatsResponse> Categories);

/// Income is revenue (przychód, VAT excluded) — the same number the budget is built from,
/// so the bars cannot claim a month earned more than the home screen let the user spend.
public record MonthStatsResponse(string Month, decimal Income, decimal Expense, decimal Net);

public record CategoryStatsResponse(
    int CategoryId, string Name, string? Icon, decimal Amount, decimal Percent, int Count);

public record AppSettingsResponse(
    string DisplayCurrency,
    string BaseCurrency,
    bool TaxesInBaseCurrency);

public record SetDisplayCurrencyRequest(string Currency);
