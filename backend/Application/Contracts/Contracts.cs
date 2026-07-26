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
    DateTimeOffset CreatedAt);

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
    decimal TakeHome);    // = MonthlyBudget

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
    SavingsSummary Savings);

/// The savings envelope, shown on its own: a balance that survives across months,
/// plus how much of this month's goal is still being held back from safe-to-spend.
public record SavingsSummary(
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve);

public record SaveSavingsPlanRequest(string Mode, decimal Value, bool Active);

public record SaveSavingsEntryRequest(string Kind, decimal Amount, DateOnly? Date, string? Note);

public record SavingsEntryResponse(int Id, DateOnly Date, string Kind, decimal Amount, string? Note);

public record SavingsResponse(
    string Mode,
    decimal Value,
    bool Active,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve,
    string Currency,
    IReadOnlyList<SavingsEntryResponse> Recent);

public record SaveRecurringRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    int DayOfMonth,
    string? Note,
    bool Active);

public record TaxProfileResponse(
    string Regime,
    decimal RyczaltRate,
    bool VatPayer,
    decimal VatRate,
    string ZusType,
    decimal ZusSocial,
    decimal HealthContribution,
    bool Chorobowe,
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
    bool Chorobowe);

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
    string Currency);

public record TakeHomeResponse(
    decimal GrossWithVat,
    decimal VatAmount,
    decimal Revenue,
    decimal ZusSocial,
    decimal HealthContribution,
    decimal HealthDeducted,
    decimal TaxBase,
    decimal Tax,
    decimal TakeHome,
    string Currency);

public record RecurringResponse(
    int Id,
    decimal AmountOriginal,
    string CurrencyOriginal,
    int CategoryId,
    string CategoryName,
    int DayOfMonth,
    bool Active,
    string? Note);
