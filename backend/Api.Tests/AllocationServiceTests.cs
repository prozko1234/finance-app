using FinanceApp.Application.Allocations;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// Choosing a scheme. The one rule that protects money: what is stored always adds up
/// to 100%, and there is never more than one active scheme.
public class AllocationServiceTests
{
    [Fact]
    public async Task A_fresh_database_starts_on_the_default_scheme()
    {
        using var mem = new SqliteInMemory();

        var r = await new AllocationService(mem.Db).GetAsync();

        Assert.Equal(AllocationPresets.DailyNormOnly, r.Active.Preset);
        Assert.Equal(100m, Assert.Single(r.Active.Buckets).Percent);
        Assert.Equal(AllocationPresets.All.Count, r.Presets.Count);
    }

    [Fact]
    public async Task Switching_preset_replaces_the_active_scheme_rather_than_adding_one()
    {
        using var mem = new SqliteInMemory();
        var sut = new AllocationService(mem.Db);

        await sut.SaveAsync(new(Preset: "50-30-20"));
        var r = await sut.SaveAsync(new(Preset: "70-20-10"));

        Assert.True(r.IsSuccess);
        Assert.Equal("70-20-10", r.Value!.Active.Preset);
        Assert.Equal(1, await mem.Db.AllocationSchemes.CountAsync(s => s.IsActive));
        Assert.Equal(3, await mem.Db.AllocationBuckets.CountAsync()); // old buckets are gone
    }

    [Fact]
    public async Task A_custom_split_that_does_not_add_up_is_refused()
    {
        using var mem = new SqliteInMemory();

        var r = await new AllocationService(mem.Db).SaveAsync(new(
            Name: "Мій",
            Buckets: [new("Витрати", "Spending", 60m), new("Заощадження", "Savings", 30m)]));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
    }

    [Fact]
    public async Task A_custom_split_keeps_the_order_it_was_sent_in()
    {
        using var mem = new SqliteInMemory();

        var r = await new AllocationService(mem.Db).SaveAsync(new(
            Name: "Мій",
            Buckets:
            [
                new("Заощадження", "Savings", 25m),
                new("Витрати", "Spending", 65m),
                new("Борг", "Debt", 10m),
            ]));

        Assert.True(r.IsSuccess);
        Assert.Null(r.Value!.Active.Preset);
        Assert.Equal(["Заощадження", "Витрати", "Борг"], r.Value.Active.Buckets.Select(b => b.Name));
    }

    [Fact]
    public async Task The_breakdown_holds_back_everything_that_is_not_spending()
    {
        using var mem = new SqliteInMemory();
        var sut = new AllocationService(mem.Db);
        await sut.SaveAsync(new(Preset: "60-solution"));

        var b = await sut.BreakdownAsync(10_000m);

        Assert.Equal(7_000m, b.Spendable);   // 60 + 10 розваги
        Assert.Equal(3_000m, b.Reserved);
        Assert.Equal(1_000m, b.SavingsGoal); // the single Savings bucket
    }
}
