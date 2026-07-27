using FinanceApp.Domain.Common;

namespace FinanceApp.Domain.Fx;

/// Port for currency conversion. Phase 1 implementation — NBP + ECB (CachingFxConverter).
public interface IFxConverter
{
    /// Converts <paramref name="amount"/> in <paramref name="currency"/> to base (PLN)
    /// using the rate effective on <paramref name="date"/>. Returns Fail(Unsupported) if no rate.
    Task<Result<FxConversion>> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default);

    /// The way back: a stored base amount shown in <paramref name="currency"/>, using the
    /// rate effective on <paramref name="date"/> — the record's own date, not today's. That
    /// is what keeps a July expense looking the same in December: nothing is recomputed,
    /// the number is simply read out in another currency at the rate it happened under.
    Task<Result<FxConversion>> ConvertFromBaseAsync(
        decimal baseAmount, string currency, DateOnly date, CancellationToken ct = default);
}
