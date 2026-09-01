using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// A subscription's due date is a schedule, not a receipt. The app knows when Netflix INTENDS
/// to charge; it does not know whether the card went through, whether the trial was still
/// running, or whether the money left an account the app has never seen.
///
/// Treating the calendar as proof produced the complaint these tests exist for: «поки не
/// оплатив, а рахує що уже». A charge is written on its day, held back from the daily norm
/// exactly as it was the day before — and waits for «оплачено ✓» before it becomes history.
public class RecurringConfirmationTests
{
    [Fact]
    public async Task A_charge_that_fell_due_waits_to_be_confirmed()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var r = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(0m, r.SpentThisPeriod);     // nobody said it was paid
        Assert.Equal(100m, r.ReservedRecurring); // still held back, exactly once
        Assert.Equal(0m, r.SpentToday);          // and it was never a choice made today

        var charge = Assert.Single(r.PendingCharges!);
        Assert.Equal(100m, charge.AmountOriginal);
        Assert.Equal(today, charge.Date);
    }

    /// The invariant the whole design rests on. Confirming is bookkeeping, not a payment: the
    /// money was already missing from the norm, so the only thing that may change is which
    /// column it sits in. If any figure the user reads moves here, the reserve and `spent`
    /// are counting the same charge differently — which is the bug in its original form.
    [Fact]
    public async Task Confirming_moves_a_charge_between_columns_and_changes_nothing_else()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var before = await TestSummary.Sut(mem).GetSafeToSpendAsync();
        var charge = Assert.Single(before.PendingCharges!);

        var confirmed = await Recurring(mem).ConfirmChargeAsync(charge.TransactionId);
        Assert.True(confirmed.IsSuccess);

        var after = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod, after.RemainingThisPeriod);
        Assert.Equal(before.DailyNorm, after.DailyNorm);
        Assert.Equal(before.LeftToday, after.LeftToday);

        Assert.Equal(100m, after.SpentThisPeriod);
        Assert.Equal(0m, after.ReservedRecurring);
        Assert.Equal(0m, after.SpentToday);
        Assert.Empty(after.PendingCharges!);
    }

    /// The other half of the same complaint: «не зарезервувало гроші наперед бо я отримав
    /// мінус до витрат в день підписки». Adding a subscription on the 10th whose charge day
    /// was the 5th used to write it straight into history as money already spent — the reserve
    /// only ever covered occurrences still ahead, so a row created after its own due date was
    /// never held back at all and landed as a lump the moment it was saved.
    [Fact]
    public async Task A_subscription_added_after_its_charge_day_is_not_money_already_spent()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day < 3) return; // needs a due date behind us but inside this period

        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 300m, today.AddDays(-2));

        var r = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(0m, r.SpentThisPeriod);
        Assert.Equal(300m, r.ReservedRecurring);
        Assert.Single(r.PendingCharges!);
    }

    /// A charge still ahead has nothing for the user to answer, so it must not appear in the
    /// list of things to confirm — a list that fills up with tomorrow's business is a list
    /// nobody reads. It is reserved all the same.
    [Fact]
    public async Task A_charge_still_ahead_is_reserved_but_not_asked_about()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day >= 26) return; // needs a due date still ahead in this period

        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 100m, today.AddDays(2));

        var r = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(100m, r.ReservedRecurring);
        Assert.Empty(r.PendingCharges!);
    }

    /// Confirming an already-confirmed charge comes from a screen that had not caught up. The
    /// state it asks for is the state the row is in, so it is not an error to report.
    [Fact]
    public async Task Confirming_twice_is_not_an_error()
    {
        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 100m, DateOnly.FromDateTime(DateTime.Now));

        var charge = Assert.Single((await TestSummary.Sut(mem).GetSafeToSpendAsync()).PendingCharges!);

        Assert.True((await Recurring(mem).ConfirmChargeAsync(charge.TransactionId)).IsSuccess);
        Assert.True((await Recurring(mem).ConfirmChargeAsync(charge.TransactionId)).IsSuccess);

        Assert.Equal(100m, (await TestSummary.Sut(mem).GetSafeToSpendAsync()).SpentThisPeriod);
    }

    /// A dollar subscription is reserved at today's rate while it is only a schedule, and at
    /// the amount it was actually written with once the charge exists. Re-converting a written
    /// charge would move what is left by the gap between two days' rates every time the screen
    /// was opened — which is what made a subscription in dollars take an amount the user could
    /// not reconcile with anything.
    [Fact]
    public async Task A_written_charge_is_reserved_at_the_amount_it_was_written_with()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today, currency: "USD");

        // Materialize at 4.0, then read the summary again with the rate moved to 5.0. The
        // reserve must still be the 400 that was written, not a fresh 500.
        var moving = new MovingRateFxConverter(4.0m);
        Assert.Equal(400m, (await TestSummary.Sut(mem, moving).GetSafeToSpendAsync()).ReservedRecurring);

        moving.PlnPerUsd = 5.0m;
        var r = await TestSummary.Sut(mem, moving).GetSafeToSpendAsync();

        Assert.Equal(400m, r.ReservedRecurring);
    }

    /// Income is not held back: a salary that fails to arrive is noticed without being asked,
    /// and making the budget wait for confirmation would leave the app saying there is no
    /// money on the one day there certainly is.
    [Fact]
    public async Task Recurring_income_is_posted_straight_away()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 4_000m, today, kind: TransactionKind.Income);

        var r = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Empty(r.PendingCharges!);
        Assert.True(r.BudgetSet);
    }

    /// Moving the day leaves the charge written under the old one with nothing to attach to:
    /// it is no longer an occurrence, so nothing will ask about it and nothing will write it
    /// back. Left alone it would sit in the list forever as a question with no answer, and the
    /// budget would go on holding money for a date that no longer exists.
    [Fact]
    public async Task Moving_the_day_takes_the_unconfirmed_charge_with_it()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day is < 3 or >= 26) return; // needs room on both sides inside this period

        using var mem = new SqliteInMemory();
        var id = await SubscriptionAsync(mem, 100m, today.AddDays(-2));

        var before = await TestSummary.Sut(mem).GetSafeToSpendAsync();
        Assert.Equal(today.AddDays(-2), Assert.Single(before.PendingCharges!).Date);

        await MoveToAsync(mem, id, today.AddDays(2), amount: 100m);

        var after = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        // Nothing to confirm any more — the new day has not come yet — and the reserve moved
        // with it rather than doubling.
        Assert.Empty(after.PendingCharges!);
        Assert.Equal(100m, after.ReservedRecurring);
    }

    /// A price change has the same problem in a quieter form: the written charge keeps the old
    /// amount, so the app would go on holding 45,99 for a subscription that now costs 59,99.
    [Fact]
    public async Task Changing_the_price_rewrites_the_unconfirmed_charge()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var id = await SubscriptionAsync(mem, 45.99m, today);

        Assert.Equal(45.99m, (await TestSummary.Sut(mem).GetSafeToSpendAsync()).ReservedRecurring);

        await MoveToAsync(mem, id, today, amount: 59.99m);

        var after = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(59.99m, after.ReservedRecurring);
        Assert.Equal(59.99m, Assert.Single(after.PendingCharges!).AmountOriginal);
    }

    /// Pausing is not the same edit. It means "no more of these", not "the one that already
    /// fell due never happened" — dropping its charge would hand back money that has probably
    /// already gone, and nothing would ever ask about it again.
    [Fact]
    public async Task Pausing_leaves_a_charge_that_already_fell_due_alone()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var id = await SubscriptionAsync(mem, 100m, today);

        await TestSummary.Sut(mem).GetSafeToSpendAsync(); // writes the charge
        await MoveToAsync(mem, id, today, amount: 100m, active: false);

        var after = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(100m, after.ReservedRecurring);
        Assert.Single(after.PendingCharges!);
    }

    /// Telling the app in August about a subscription that started last year backfills a year
    /// of charges. Eleven of them are not questions anybody can answer, and a badge on a row
    /// that never appears in the card asking about it is a state with no way out. Only the
    /// period being lived in is asked about; older charges are history.
    [Fact]
    public async Task Charges_from_periods_already_over_are_history_not_questions()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today.AddMonths(-3));

        var r = await TestSummary.Sut(mem).GetSafeToSpendAsync();

        Assert.Single(r.PendingCharges!); // this period's, and only this period's

        var written = await mem.Db.Transactions
            .Where(t => t.RecurringExpenseId != null).ToListAsync();
        Assert.True(written.Count >= 3, "three months back should have written more than one");
        Assert.Single(written, t => t.Status == TxStatus.Pending);
    }

    /// Income, and a monthly recurring row first due on the given date.
    private static async Task<int> SubscriptionAsync(
        SqliteInMemory mem, decimal amount, DateOnly due,
        string currency = "PLN", TransactionKind kind = TransactionKind.Expense)
    {
        var category = new Category { Name = "Підписки" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();

        var r = new RecurringExpense
        {
            Kind = kind,
            AmountOriginal = amount,
            CurrencyOriginal = currency,
            CategoryId = category.Id,
            StartsOn = due,
            Unit = RecurrenceUnit.Month,
            Interval = 1,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.RecurringExpenses.Add(r);
        await mem.Db.SaveChangesAsync();
        return r.Id;
    }

    /// What the edit form sends: the same row with a new schedule or a new price.
    private static async Task MoveToAsync(
        SqliteInMemory mem, int id, DateOnly startsOn, decimal amount, bool active = true)
    {
        var categoryId = await mem.Db.RecurringExpenses
            .Where(x => x.Id == id).Select(x => x.CategoryId).FirstAsync();

        var updated = await Recurring(mem).UpdateAsync(id, new SaveRecurringRequest(
            amount, "PLN", categoryId, startsOn, Note: null, Active: active));

        Assert.True(updated.IsSuccess);
    }

    private static RecurringService Recurring(SqliteInMemory mem) =>
        new(mem.Db, new BudgetPeriodResolver(mem.Db));

    /// «Оплачено ✓» is one tap on a card that appears unbidden at the top of the home screen,
    /// so it gets mis-tapped. Deleting the charge was the only way out, and that says something
    /// else entirely: that it never happened, which leaves a skip behind.
    [Fact]
    public async Task A_confirmation_can_be_taken_back()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var charge = Assert.Single((await TestSummary.Sut(mem).GetSafeToSpendAsync()).PendingCharges!);
        Assert.True((await Recurring(mem).ConfirmChargeAsync(charge.TransactionId)).IsSuccess);
        Assert.Equal(TxStatus.Posted, mem.Db.Transactions.Single(t => t.Id == charge.TransactionId).Status);

        Assert.True((await Recurring(mem).UnconfirmChargeAsync(charge.TransactionId)).IsSuccess);
        Assert.Equal(TxStatus.Pending, mem.Db.Transactions.Single(t => t.Id == charge.TransactionId).Status);
    }

    /// Forgiving in the same way confirming is: a charge already waiting is already in the
    /// state being asked for, and a stale screen must not be shown a problem that is not one.
    [Fact]
    public async Task Taking_back_a_confirmation_that_never_happened_is_not_an_error()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var charge = Assert.Single((await TestSummary.Sut(mem).GetSafeToSpendAsync()).PendingCharges!);

        Assert.True((await Recurring(mem).UnconfirmChargeAsync(charge.TransactionId)).IsSuccess);
        Assert.Equal(TxStatus.Pending, mem.Db.Transactions.Single(t => t.Id == charge.TransactionId).Status);
    }

    /// The subscriptions screen acts on a CHARGE, so it has to be told which one — the rule's
    /// own id is no use, and the same rule can have another occurrence behind this one.
    /// Without this the screen could show a status and do nothing about it.
    [Fact]
    public async Task The_list_names_the_charge_its_buttons_act_on()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var charge = Assert.Single((await TestSummary.Sut(mem).GetSafeToSpendAsync()).PendingCharges!);
        var row = Assert.Single(await Recurring(mem).GetAllAsync());

        Assert.True(row.AwaitingConfirmation);
        Assert.False(row.ChargedThisPeriod);
        Assert.Equal(charge.TransactionId, row.ChargeId);
        Assert.Equal(today, row.ChargeOn);
    }

    /// And it keeps naming it once confirmed, which is what makes the tick reversible.
    [Fact]
    public async Task A_confirmed_charge_is_still_named_so_it_can_be_undone()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await SubscriptionAsync(mem, 100m, today);

        var charge = Assert.Single((await TestSummary.Sut(mem).GetSafeToSpendAsync()).PendingCharges!);
        await Recurring(mem).ConfirmChargeAsync(charge.TransactionId);

        var row = Assert.Single(await Recurring(mem).GetAllAsync());

        Assert.True(row.ChargedThisPeriod);
        Assert.Equal(charge.TransactionId, row.ChargeId);
    }
}
