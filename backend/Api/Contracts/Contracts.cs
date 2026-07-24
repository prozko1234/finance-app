using FinanceApp.Domain;

namespace FinanceApp.Api.Contracts;

/// Запит на створення/оновлення транзакції. Валюта — 3-літерний ISO-код.
/// Date опційна (дефолт — сьогодні). Base-сума і курс рахуються на сервері.
public record SaveTransactionRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    Priority Priority,
    Frequency Frequency,
    DateOnly? Date,
    string? Merchant,
    string? Note);

public record TransactionResponse(
    int Id,
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

public record CategoryResponse(int Id, string Name, string? Icon);

public record SetBudgetRequest(decimal Amount);

public record BudgetResponse(bool Set, decimal? MonthlyAmount, string Currency, DateTimeOffset? UpdatedAt);

public record SafeToSpendResponse(
    DateOnly Date,
    string Currency,
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? SafeToSpendToday);
