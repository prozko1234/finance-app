using FinanceApp.Application.Contracts;
using FinanceApp.Domain.Common;

namespace FinanceApp.Application.Transactions;

public interface ITransactionService
{
    Task<IReadOnlyList<TransactionResponse>> GetRecentAsync(int take, CancellationToken ct = default);
    Task<Result<TransactionResponse>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<TransactionResponse>> CreateAsync(SaveTransactionRequest req, CancellationToken ct = default);
    Task<Result<TransactionResponse>> CreateIncomeAsync(SaveIncomeRequest req, CancellationToken ct = default);

    /// Correcting an invoice. Separate from UpdateAsync because an income row carries the VAT
    /// split, and the expense path would put the gross figure where the revenue belongs.
    Task<Result<TransactionResponse>> UpdateIncomeAsync(int id, SaveIncomeRequest req, CancellationToken ct = default);
    Task<Result<TransactionResponse>> UpdateAsync(int id, SaveTransactionRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}
