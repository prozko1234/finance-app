using FinanceApp.Application.Debts;
using static FinanceApp.Api.Tests.TestIncome;
using FinanceApp.Application.Common;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Savings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Api.Tests;

/// Before envelopes, a scheme's pension bucket only ever subtracted from the daily norm: the
/// app held the money back every month and could never say how much had piled up, or let
/// the user actually put it anywhere. These tests are about that gap being closed.
public class EnvelopeTests
{
    private const decimal Budget = 6_000m;

    private static EnvelopeService Sut(SqliteInMemory mem) =>
        new(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db),
            new FakeFxConverter(), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    /// An ordinary period: the budget came from income, so the plan runs.
    private static MonthlyBudgetResult Month(decimal budget) =>
        new(budget, null, BudgetPeriods.For(Today, BudgetPeriods.FirstOfMonth).Start, false);

    /// A period started by counting what is in the account — the plan stands down.
    private static MonthlyBudgetResult Counted(decimal budget) => new(budget, null, Today, true);

    private static SavingsService Savings(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))), fx, new AllocationService(mem.Db),
            Sut(mem), new MoneyViewFactory(mem.Db, fx), NullLogger<SavingsService>.Instance);
    }

    private static async Task ActivateAsync(SqliteInMemory mem, string preset)
    {
        foreach (var s in mem.Db.AllocationSchemes) s.IsActive = false;
        await mem.Db.SaveChangesAsync();

        mem.Db.AllocationSchemes.Add(AllocationPresets.Find(preset)!.ToScheme(isActive: true));
        mem.Db.Transactions.Add(Income(Budget));
        await mem.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_non_spending_bucket_becomes_a_pot_money_can_go_into()
    {
        using var mem = new SqliteInMemory();
        // 60% commitments / 10 pension / 10 long-term savings / 10 irregular / 10 fun
        await ActivateAsync(mem, "60-solution");

        var envelopes = await Sut(mem).StatusAsync(Month(Budget));
        var names = envelopes.Select(e => e.Name).ToList();

        Assert.Contains("Пенсія", names);
        Assert.Contains("Нерегулярні витрати", names);
        Assert.DoesNotContain("Розваги", names); // spending money is not put aside
        Assert.Equal(600m, envelopes.Single(e => e.Name == "Пенсія").MonthGoal);
    }

    [Fact]
    public async Task The_default_pot_exists_even_with_no_scheme_at_all()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Transactions.Add(Income(Budget));
        await mem.Db.SaveChangesAsync();

        var envelopes = await Sut(mem).StatusAsync(Month(Budget));

        // Otherwise there would be nowhere to put money until a scheme is chosen.
        var def = Assert.Single(envelopes, e => e.IsDefault);
        Assert.Equal("Заощадження", def.Name);
    }

    [Fact]
    public async Task The_scheme_fills_the_pension_pot_by_itself()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        // Reading the status is enough: choosing a scheme is the decision, carrying it out
        // is not a chore to hand back to the user every month.
        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");

        Assert.Equal(600m, pension.Balance);
        Assert.Equal(600m, pension.DepositedThisMonth);
        Assert.Equal(0m, pension.StillToReserve); // nothing is "still to move" any more
        Assert.Equal(600m, pension.HeldBack);
    }

    [Fact]
    public async Task Reading_the_status_twice_does_not_fill_the_pot_twice()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        await Sut(mem).StatusAsync(Month(Budget));
        await Sut(mem).StatusAsync(Month(Budget));

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        Assert.Equal(600m, pension.Balance);
    }

    /// Reading a screen WRITES, and the home page fires several queries at once — so two
    /// requests can both find no scheme deposit and both insert one. The second was then never
    /// looked at again (the lookup only ever took the first), so it sat in the jar for good:
    /// the balance was overstated by it, AND it was held back from the daily norm a second
    /// time. That is what put a real user 3 231 zł "over budget" while the money was in the
    /// bank in front of them.
    ///
    /// Healed on the next read rather than merely prevented, because every database that had
    /// already raced would otherwise stay wrong forever.
    [Fact]
    public async Task A_pot_filled_twice_by_a_race_is_put_back_to_one_deposit()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        // The first read fills the pots the way it always does.
        await Sut(mem).StatusAsync(Month(Budget));

        // The duplicate a racing request would have left behind.
        var poured = mem.Db.SavingsEntries.First(x => x.IsAuto);
        mem.Db.SavingsEntries.Add(new SavingsEntry
        {
            EnvelopeId = poured.EnvelopeId, Date = poured.Date, Kind = SavingsEntryKind.Deposit,
            CurrencyOriginal = "PLN", AmountOriginal = 600m, AmountBase = 600m,
            FxRate = 1m, FxDate = poured.Date, IsAuto = true,
            Note = poured.Note, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");

        Assert.Equal(600m, pension.Balance);
        Assert.Equal(600m, pension.HeldBack);
        Assert.Single(mem.Db.SavingsEntries.Where(x => x.IsAuto && x.EnvelopeId == poured.EnvelopeId).ToList());
    }

    /// A second invoice raises the budget, so the goal rises with it. The app keeps its own
    /// deposit in step instead of leaving a trail of correcting top-ups.
    [Fact]
    public async Task A_bigger_budget_raises_what_the_app_put_aside()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        await Sut(mem).StatusAsync(Month(Budget));
        var pension = (await Sut(mem).StatusAsync(Month(Budget * 2))).Single(e => e.Name == "Пенсія");

        Assert.Equal(1_200m, pension.Balance);
        Assert.Equal(1_200m, pension.DepositedThisMonth);
    }

    [Fact]
    public async Task A_deposit_by_hand_is_extra_on_top_of_the_plan()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 250m, null, null, null, pension.Id));

        var after = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");

        // Moving money in by hand used to be the only way to meet the goal, so it counted
        // towards it. The app meets the goal now, so a hand-made deposit means "more than
        // planned" — and costs that much more of what is safe to spend.
        Assert.Equal(850m, after.Balance);
        Assert.Equal(850m, after.HeldBack);
    }

    /// The point of replacing "треба/варто/хочу": where the money comes from is a fact that
    /// changes a number, unlike a priority, which changed nothing on any screen.
    [Fact]
    public async Task Paying_out_of_an_envelope_empties_it_by_that_much()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense, CurrencyOriginal = "PLN",
            AmountOriginal = 150m, AmountBase = 150m, FxRate = 1m,
            FxDate = DateOnly.FromDateTime(DateTime.Now), Date = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = 1, EnvelopeId = pension.Id,
        });
        await mem.Db.SaveChangesAsync();

        var after = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");

        Assert.Equal(450m, after.Balance); // 600 put aside, 150 paid out of it

        // Still 600 out of reach, and that is the point: 450 sits in the pot and 150 has
        // been spent, but the summary leaves an envelope-paid expense out of "витрачено"
        // precisely because the envelope already holds it back. Dropping to 450 here would
        // hand the user back 150 they have already spent.
        Assert.Equal(600m, after.HeldBack);
    }

    /// The question the envelope screen exists to answer: over a period, how much moved and
    /// how much is in the jar now.
    [Fact]
    public async Task History_reports_what_moved_and_what_the_balance_became()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 250m, null, null, null, pension.Id));

        var history = await Sut(mem).HistoryAsync(pension.Id, 3);

        // Newest first, one row per period, and the running balance is what is in the pot.
        Assert.Equal(3, history.Count);
        Assert.Equal(850m, history[0].Moved);        // 600 by the scheme + 250 by hand
        Assert.Equal(850m, history[0].BalanceAfter);
        Assert.All(history.Skip(1), p => Assert.Equal(0m, p.Moved));
    }

    [Fact]
    public async Task History_carries_the_balance_across_periods()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        // Dated in an earlier period, so it must show up as an opening balance, not as
        // movement of the period being looked at.
        await Savings(mem).AddEntryAsync(new(
            "Deposit", 400m, DateOnly.FromDateTime(DateTime.Now).AddMonths(-2), null, null, pension.Id));

        var history = await Sut(mem).HistoryAsync(pension.Id, 3);

        Assert.Equal(1_000m, history[0].BalanceAfter); // 400 carried in + 600 this period
        Assert.Equal(600m, history[0].Moved);
    }

    [Fact]
    public async Task Money_cannot_be_taken_out_of_a_pot_it_was_never_put_into()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var all = await Sut(mem).StatusAsync(Month(Budget));
        var pension = all.Single(e => e.Name == "Пенсія");
        var savings = all.Single(e => e.IsDefault);

        await Savings(mem).AddEntryAsync(new("Deposit", 500m, null, null, null, pension.Id));

        // A full pension envelope must not fund a withdrawal from an empty savings one.
        var r = await Savings(mem).AddEntryAsync(new("Withdrawal", 100m, null, null, null, savings.Id));

        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task Starting_mid_month_stands_the_goals_down_but_keeps_the_balances()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        await Savings(mem).AddEntryAsync(new("Deposit", 400m, yesterday, null, null, pension.Id));

        // 1800 left to LIVE on, counted today. Reserving another 10% of it for four pots
        // would drop the daily norm to almost nothing — the exact thing the opening balance
        // is there to fix. The 400 put aside yesterday is already outside that 1800, and
        // what the app itself had set aside on paper this period is taken back out.
        var after = await Sut(mem).StatusAsync(Counted(1_800m));

        Assert.All(after, e => Assert.Equal(0m, e.HeldBack));
        Assert.Equal(400m, after.Single(e => e.Name == "Пенсія").Balance);
    }

    /// Two screens, one truth. The savings screen used to resolve the budget without the
    /// window the home screen used, so a counted balance stood the goals down on one and not
    /// on the other: every page load deleted or re-created the app's own deposit, the balance
    /// flipped between two numbers depending on which screen had loaded last, and the id churn
    /// turned an ordinary edit into "Операцію не знайдено".
    [Fact]
    public async Task A_counted_balance_stands_the_plan_down_on_the_savings_screen_too()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        // Filled by the scheme first, the way an ordinary period fills it.
        await Sut(mem).StatusAsync(Month(Budget));

        mem.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, AmountOriginal = 1_800m, CurrencyOriginal = "PLN",
            AmountBase = 1_800m, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var first = await Savings(mem).GetAsync();
        var ids = first.Recent.Select(e => e.Id).ToList();

        // The home screen in between: whatever it writes, the savings screen must read back.
        await Sut(mem).StatusAsync(Counted(1_800m));
        var second = await Savings(mem).GetAsync();

        Assert.All(first.Envelopes, e => Assert.Equal(0m, e.MonthGoal));
        Assert.Equal(Today, first.PlanPausedFrom);
        Assert.Equal(ids, second.Recent.Select(e => e.Id).ToList());
        Assert.Equal(first.Balance, second.Balance);
        Assert.Equal(
            first.Envelopes.Select(e => e.Balance).ToList(),
            second.Envelopes.Select(e => e.Balance).ToList());
    }

    [Fact]
    public async Task A_pot_left_behind_by_an_old_scheme_keeps_its_balance_and_stops_reserving()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 400m, null, null, null, pension.Id));

        await ActivateAsync(mem, "80-20"); // no pension bucket any more

        var after = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        Assert.Equal(400m, after.Balance);  // the money did not evaporate with the scheme
        Assert.Equal(0m, after.MonthGoal);  // but it no longer takes anything from the norm
        Assert.Equal(0m, after.StillToReserve);
    }

    // Jars as a thing in their own right: until now a jar could only be had as a scheme
    // bucket, so "Відпустка" meant opening the scheme and inventing a percentage for it.

    [Fact]
    public async Task A_pot_can_be_made_by_hand_and_stands_beside_the_scheme_ones()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");

        var made = await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings);
        Assert.True(made.IsSuccess);

        var all = await Sut(mem).StatusAsync(Month(Budget));
        var holiday = all.Single(e => e.Name == "Відпустка");
        Assert.Equal(0m, holiday.Balance);
        Assert.False(holiday.IsFromScheme);       // нічого не диктує ні назву, ні ціль
        Assert.Equal(0m, holiday.MonthGoal);      // і нічого не тримає з норми
        Assert.Contains(all, e => e.IsFromScheme);
    }

    [Fact]
    public async Task Two_pots_cannot_share_a_name_because_the_name_is_what_a_bucket_matches()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        await Sut(mem).StatusAsync(Month(Budget));

        var again = await Sut(mem).CreateAsync(Envelope.DefaultName, BucketKind.Savings);

        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorType.Conflict, again.Error.Type);
    }

    [Fact]
    public async Task A_pot_put_away_comes_back_with_its_history_instead_of_being_made_twice()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Ремонт", BucketKind.Other)).Value!;

        // Money in and out again: the pot is empty, so it can be put away — but it has a past.
        await Savings(mem).AddEntryAsync(new("Deposit", 300m, null, null, null, made.Id));
        await Savings(mem).AddEntryAsync(new("Withdrawal", 300m, null, null, null, made.Id));
        Assert.True((await Sut(mem).ArchiveAsync(made.Id)).IsSuccess);

        var back = await Sut(mem).CreateAsync("Ремонт", BucketKind.Other);

        Assert.True(back.IsSuccess);
        Assert.Equal(made.Id, back.Value!.Id);
        Assert.Equal(2, (await Savings(mem).GetAsync()).Recent.Count(e => e.EnvelopeId == made.Id));
    }

    [Fact]
    public async Task A_pot_put_away_leaves_the_list_but_keeps_its_movements_readable()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Ремонт", BucketKind.Other)).Value!;
        await Savings(mem).AddEntryAsync(new("Deposit", 300m, null, null, null, made.Id));
        await Savings(mem).AddEntryAsync(new("Withdrawal", 300m, null, null, null, made.Id));

        Assert.True((await Sut(mem).ArchiveAsync(made.Id)).IsSuccess);

        Assert.DoesNotContain(await Sut(mem).StatusAsync(Month(Budget)), e => e.Id == made.Id);
        Assert.NotEmpty(await Sut(mem).HistoryAsync(made.Id));
        Assert.Equal(2, (await Savings(mem).GetAsync()).Recent.Count(e => e.EnvelopeId == made.Id));
        // And it is no longer a destination: money there would be money the list never shows.
        var blocked = await Savings(mem).AddEntryAsync(new("Deposit", 50m, null, null, null, made.Id));
        Assert.False(blocked.IsSuccess);
    }

    [Fact]
    public async Task A_pot_with_money_still_in_it_is_not_put_away()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Savings(mem).AddEntryAsync(new("Deposit", 240m, null, null, null, made.Id));

        var refused = await Sut(mem).ArchiveAsync(made.Id);

        Assert.False(refused.IsSuccess);
        Assert.Contains("240", refused.Error.Message);  // сказати, скільки саме там лежить
        Assert.Contains(await Sut(mem).StatusAsync(Month(Budget)), e => e.Id == made.Id);
    }

    [Fact]
    public async Task The_default_pot_and_the_schemes_own_pots_are_neither_renamed_nor_put_away()
    {
        using var mem = new SqliteInMemory();
        // 60-solution, not 50-30-20: there the savings bucket IS the default pot, and the two
        // reasons would not be told apart.
        await ActivateAsync(mem, "60-solution");
        var all = await Sut(mem).StatusAsync(Month(Budget));
        var def = all.Single(e => e.IsDefault);
        var fromScheme = all.First(e => e.IsFromScheme && !e.IsDefault);

        // Both are looked up BY NAME — by the app itself, or by the scheme's bucket — so a
        // rename would hand the balance to a pot nobody feeds, and a removal would undo itself
        // on the next screen load.
        Assert.False((await Sut(mem).UpdateAsync(def.Id, "Мої гроші", def.Kind)).IsSuccess);
        Assert.False((await Sut(mem).ArchiveAsync(def.Id)).IsSuccess);
        Assert.False((await Sut(mem).UpdateAsync(fromScheme.Id, "Інша назва", fromScheme.Kind)).IsSuccess);
        Assert.False((await Sut(mem).ArchiveAsync(fromScheme.Id)).IsSuccess);
    }

    [Fact]
    public async Task A_hand_made_pot_can_be_renamed_and_keeps_the_money_that_was_in_it()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Savings(mem).AddEntryAsync(new("Deposit", 240m, null, null, null, made.Id));

        var renamed = await Sut(mem).UpdateAsync(made.Id, "Відпустка 2027", BucketKind.Savings);

        Assert.True(renamed.IsSuccess);
        var after = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id);
        Assert.Equal("Відпустка 2027", after.Name);
        Assert.Equal(240m, after.Balance);
    }

    [Fact]
    public async Task A_bucket_added_back_to_the_scheme_revives_the_pot_that_holds_its_money()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");
        var pension = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 400m, null, null, null, pension.Id));
        await Savings(mem).AddEntryAsync(new("Withdrawal", 400m, null, null, null, pension.Id));

        // Emptied, the scheme dropped, then put away — and the scheme brought back. The screen
        // load in between is what withdraws what the old scheme had set aside by itself.
        await ActivateAsync(mem, "80-20");
        await Sut(mem).StatusAsync(Month(Budget));
        Assert.True((await Sut(mem).ArchiveAsync(pension.Id)).IsSuccess);
        await ActivateAsync(mem, "60-solution");

        var all = await Sut(mem).StatusAsync(Month(Budget));
        var again = all.Single(e => e.Name == "Пенсія");
        Assert.Equal(pension.Id, again.Id);  // не друга «Пенсія» поруч зі старою
        Assert.True(again.MonthGoal > 0m);
    }

    // A target on a jar: without one, a jar no scheme feeds is a pointless piggy bank. It
    // holds no money back — the pace is information to decide with, not another reservation.

    [Fact]
    public async Task A_target_with_a_date_says_what_has_to_go_in_each_period()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Savings(mem).AddEntryAsync(new("Deposit", 2_200m, null, null, null, made.Id));

        // The end of the third period from this one: 6,000 − 2,200 = 3,800 over three periods.
        var third = BudgetPeriods.For(Today, BudgetPeriods.FirstOfMonth);
        for (var i = 0; i < 2; i++) third = BudgetPeriods.For(third.End.AddDays(1), BudgetPeriods.FirstOfMonth);

        Assert.True((await Sut(mem).SetTargetAsync(made.Id, 6_000m, null, third.End)).IsSuccess);

        var jar = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id);
        Assert.NotNull(jar.Target);
        Assert.Equal(6_000m, jar.Target!.Amount);
        Assert.Equal(3_800m, jar.Target.Remaining);
        Assert.Equal(3, jar.Target.PeriodsLeft);
        Assert.Equal(1_266.67m, jar.Target.PerPeriod);  // округлено ВГОРУ, інакше ціль не добереться
        Assert.False(jar.Target.Reached);
        Assert.False(jar.Target.Overdue);
    }

    /// A target holds nothing back from the daily norm: otherwise it would compete with the
    /// scheme for the same money and hold it twice — and the app would be deciding for the user
    /// what they are allowed to want.
    [Fact]
    public async Task A_target_reserves_nothing()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;

        var before = (await Sut(mem).StatusAsync(Month(Budget))).Sum(e => e.HeldBack);
        await Sut(mem).SetTargetAsync(made.Id, 6_000m, null, Today.AddYears(1));
        var after = await Sut(mem).StatusAsync(Month(Budget));

        Assert.Equal(before, after.Sum(e => e.HeldBack));
        var jar = after.Single(e => e.Id == made.Id);
        Assert.Equal(0m, jar.MonthGoal);
        Assert.Equal(0m, jar.StillToReserve);
    }

    [Fact]
    public async Task A_target_without_a_date_is_a_goal_and_not_a_pace()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Ремонт", BucketKind.Other)).Value!;

        await Sut(mem).SetTargetAsync(made.Id, 4_000m, null, null);

        var jar = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id);
        Assert.Equal(4_000m, jar.Target!.Remaining);
        Assert.Equal(0, jar.Target.PeriodsLeft);
        Assert.Equal(0m, jar.Target.PerPeriod);   // дату не вигадуємо за людину
        Assert.False(jar.Target.Overdue);
    }

    [Fact]
    public async Task A_target_already_collected_says_so_instead_of_asking_for_more()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Savings(mem).AddEntryAsync(new("Deposit", 6_500m, null, null, null, made.Id));
        await Sut(mem).SetTargetAsync(made.Id, 6_000m, null, Today.AddMonths(2));

        var jar = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id);

        Assert.True(jar.Target!.Reached);
        Assert.Equal(0m, jar.Target.Remaining);
        Assert.Equal(0m, jar.Target.PerPeriod);
    }

    [Fact]
    public async Task A_target_is_taken_off_together_with_its_date()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Sut(mem).SetTargetAsync(made.Id, 6_000m, null, Today.AddMonths(3));

        Assert.True((await Sut(mem).SetTargetAsync(made.Id, null, null, null)).IsSuccess);

        var jar = (await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id);
        Assert.Null(jar.Target);
        Assert.Null((await mem.Db.Envelopes.FindAsync(made.Id))!.TargetDate);
    }

    [Fact]
    public async Task A_date_that_has_gone_by_is_refused_and_a_zero_target_too()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "50-30-20");
        var made = (await Sut(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;

        var past = await Sut(mem).SetTargetAsync(made.Id, 6_000m, null, Today.AddDays(-1));
        var zero = await Sut(mem).SetTargetAsync(made.Id, 0m, null, Today.AddMonths(2));

        Assert.Equal(ErrorType.Validation, past.Error.Type);
        Assert.Equal(ErrorType.Validation, zero.Error.Type);
        Assert.Null((await Sut(mem).StatusAsync(Month(Budget))).Single(e => e.Id == made.Id).Target);
    }
}
