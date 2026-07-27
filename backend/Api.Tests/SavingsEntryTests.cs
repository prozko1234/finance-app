using FinanceApp.Application.Allocations;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Domain.Common;

namespace FinanceApp.Api.Tests;

/// Movements of the envelope: money can arrive in any currency, and a mistyped entry
/// must be correctable without deleting it. Both paths write the balance, so both are
/// tested against the same invariant — the balance is the sum of what is stored.
public class SavingsEntryTests
{
    private static SavingsService Sut(SqliteInMemory mem) =>
        new(mem.Db, new MonthlyBudget(mem.Db), new FakeFxConverter(), new AllocationService(mem.Db));

    private static SaveSavingsEntryRequest Deposit(decimal amount, string? currency = null) =>
        new("Deposit", amount, null, null, currency);

    [Fact]
    public async Task Deposit_in_another_currency_is_converted_but_remembers_what_was_typed()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(100m, "USD"));

        Assert.True(r.IsSuccess);
        Assert.Equal(400m, r.Value!.Balance); // 100 USD at the test rate of 4.0
        var entry = Assert.Single(r.Value.Recent);
        Assert.Equal(100m, entry.AmountOriginal);
        Assert.Equal("USD", entry.CurrencyOriginal);
        Assert.Equal(400m, entry.Amount);
    }

    [Fact]
    public async Task Currency_defaults_to_base_when_omitted()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(250m));

        Assert.Equal("PLN", Assert.Single(r.Value!.Recent).CurrencyOriginal);
        Assert.Equal(250m, r.Value.Balance);
    }

    [Fact]
    public async Task A_currency_with_no_rate_is_refused_rather_than_stored_wrong()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).AddEntryAsync(Deposit(100m, "JPY"));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Unsupported, r.Error.Type);
    }

    [Fact]
    public async Task Editing_a_deposit_moves_the_balance_to_the_new_amount()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        var added = await sut.AddEntryAsync(Deposit(800m));
        var id = added.Value!.Recent[0].Id;

        var r = await sut.UpdateEntryAsync(id, new SaveSavingsEntryRequest("Deposit", 500m, null, "виправив", null));

        Assert.True(r.IsSuccess);
        Assert.Equal(500m, r.Value!.Balance);
        Assert.Equal("виправив", Assert.Single(r.Value.Recent).Note);
    }

    /// The trap this guards: the row being edited is already in the balance, so a naive
    /// check would refuse to correct a withdrawal that is fine once itself is excluded.
    [Fact]
    public async Task A_withdrawal_can_be_corrected_against_the_balance_without_itself()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        await sut.AddEntryAsync(Deposit(1000m));
        var w = await sut.AddEntryAsync(new SaveSavingsEntryRequest("Withdrawal", 900m, null, null, null));
        var id = w.Value!.Recent[0].Id;

        // 950 exceeds the current balance of 100, but not the 1000 available without this row.
        var r = await sut.UpdateEntryAsync(id, new SaveSavingsEntryRequest("Withdrawal", 950m, null, null, null));

        Assert.True(r.IsSuccess);
        Assert.Equal(50m, r.Value!.Balance);
    }

    [Fact]
    public async Task Withdrawing_more_than_there_is_still_fails()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);
        await sut.AddEntryAsync(Deposit(100m));

        var r = await sut.AddEntryAsync(new SaveSavingsEntryRequest("Withdrawal", 300m, null, null, null));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
    }

    [Fact]
    public async Task Editing_a_missing_entry_is_a_not_found()
    {
        using var mem = new SqliteInMemory();

        var r = await Sut(mem).UpdateEntryAsync(404, Deposit(10m));

        Assert.Equal(ErrorType.NotFound, r.Error.Type);
    }
}
