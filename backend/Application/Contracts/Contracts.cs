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

public record SafeToSpendResponse(
    DateOnly Date,
    string Currency,
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal ReservedRecurring,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? SafeToSpendToday);

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
