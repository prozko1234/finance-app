using System.Text;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Import;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Transactions;
using FinanceApp.Application.Auth;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// Importing a statement over a period the app has already charged subscriptions for.
///
/// The trap is the price. A subscription whose price has risen writes its charge at the OLD
/// figure, so the statement row is not a duplicate of it by amount — and importing the row as
/// an ordinary expense leaves the period paying for the same bill twice, once at each price.
/// Claude went 99,16 → 502,67 in one month, so this is not an edge case.
public class ImportSubscriptionTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static ImportService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        var periods = new BudgetPeriodResolver(mem.Db);
        var materializer = new RecurringMaterializer(mem.Db, fx, periods);
        return new ImportService(
            mem.Db,
            new TransactionService(mem.Db, fx, materializer, new MoneyViewFactory(mem.Db, fx),
                new UserProvisioningService(mem.Db)),
            new SettingsService(mem.Db, fx),
            new RecurringService(mem.Db, periods),
            materializer);
    }

    /// A monthly subscription charging today, at the price the app believes.
    private static async Task<int> SubscriptionAsync(SqliteInMemory mem, decimal amount, string note)
    {
        var category = new Category { Name = $"Підписки {note}" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(10_000m));
        await mem.Db.SaveChangesAsync();

        var rule = new RecurringExpense
        {
            Kind = TransactionKind.Expense, AmountOriginal = amount, CurrencyOriginal = "PLN",
            CategoryId = category.Id, StartsOn = Today, Unit = RecurrenceUnit.Month,
            Interval = 1, Active = true, Note = note, CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.RecurringExpenses.Add(rule);
        await mem.Db.SaveChangesAsync();

        // Writes the pending charge, the way opening any screen does.
        await new RecurringMaterializer(
            mem.Db, new FakeFxConverter(), new BudgetPeriodResolver(mem.Db)).MaterializeDueAsync();
        return rule.Id;
    }

    private static byte[] Statement(decimal amount, string shop, DateOnly? on = null) =>
        Encoding.UTF8.GetBytes(
            "\"Data operacji\";\"Kwota\";\"Waluta\";\"Opis transakcji\"\n"
            + $"\"{on ?? Today:yyyy-MM-dd}\";\"{-amount:0.00}\";\"PLN\";\"{shop}\"\n");

    private static async Task<ImportRowPreview> RowAsync(SqliteInMemory mem, byte[] file)
    {
        var preview = await Sut(mem).PreviewAsync(file);
        Assert.True(preview.IsSuccess);
        return Assert.Single(preview.Value!.Rows);
    }

    /// The price rose, so the amounts differ and the app's own duplicate check — same day, same
    /// money — cannot see it. This is the row that would otherwise be imported on top of a
    /// charge already being held.
    [Fact]
    public async Task A_row_whose_price_has_risen_is_still_recognised_as_the_subscription()
    {
        using var mem = new SqliteInMemory();
        var id = await SubscriptionAsync(mem, 99.16m, "Claude");

        var row = await RowAsync(mem, Statement(502.67m, "ANTHROPIC* CLAUDE SUB"));

        Assert.Null(row.DuplicateOfId); // the amounts differ, so nothing else would catch it
        Assert.NotNull(row.Recurring);
        Assert.Equal(id, row.Recurring!.RecurringId);
        Assert.Equal("Claude", row.Recurring.Name);
        Assert.Equal(99.16m, row.Recurring.RuleAmount);
        Assert.Equal("Pending", row.Recurring.ChargeStatus);
        Assert.False(row.Recurring.Learned); // nothing has taught the shop yet — it is a guess
    }

    /// Answering the row confirms the charge the app wrote and creates NOTHING. One payment,
    /// one record.
    [Fact]
    public async Task Answering_a_row_as_the_subscription_confirms_it_instead_of_adding_an_expense()
    {
        using var mem = new SqliteInMemory();
        var id = await SubscriptionAsync(mem, 99.16m, "Claude");
        var row = await RowAsync(mem, Statement(502.67m, "ANTHROPIC* CLAUDE SUB"));
        var before = await mem.Db.Transactions.CountAsync();

        var result = await Sut(mem).CommitAsync(new CommitImportRequest([
            new ImportRowRequest(row.Line, row.Date, row.Amount, row.Currency,
                CategoryId: 1, Note: row.Description, RecurringId: id,
                UpdateRecurringAmount: true),
        ]));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Created);
        Assert.Equal(1, result.Value.Confirmed);
        Assert.Equal(1, result.Value.Repriced);

        Assert.Equal(before, await mem.Db.Transactions.CountAsync());
        Assert.Equal(502.67m, mem.Db.RecurringExpenses.Single(r => r.Id == id).AmountOriginal);
        Assert.Equal(TxStatus.Posted,
            mem.Db.Transactions.Single(t => t.RecurringExpenseId == id).Status);
    }

    /// And the shop is remembered, so the next statement recognises it outright rather than
    /// guessing — the same bargain the category learning makes.
    [Fact]
    public async Task Answering_it_once_teaches_the_shop_for_good()
    {
        using var mem = new SqliteInMemory();
        var id = await SubscriptionAsync(mem, 99.16m, "Claude");
        var row = await RowAsync(mem, Statement(502.67m, "ANTHROPIC* CLAUDE SUB"));

        await Sut(mem).CommitAsync(new CommitImportRequest([
            new ImportRowRequest(row.Line, row.Date, row.Amount, row.Currency, 1,
                row.Description, RecurringId: id),
        ]));

        Assert.Equal("ANTHROPIC", mem.Db.RecurringExpenses.Single(r => r.Id == id).MerchantKey);

        var again = await RowAsync(mem, Statement(502.67m, "ANTHROPIC* CLAUDE SUB"));
        Assert.True(again.Recurring!.Learned);
    }

    /// The guard that matters most. A rule kept in euro, seen on a złoty statement, must not
    /// have its price set from the converted figure: 6,63 EUR would become 29,60 EUR and every
    /// later charge would be wrong by the exchange rate.
    [Fact]
    public async Task A_price_in_another_currency_is_refused_rather_than_converted()
    {
        using var mem = new SqliteInMemory();
        var id = await SubscriptionAsync(mem, 6.63m, "Spotify");
        var rule = mem.Db.RecurringExpenses.Single(r => r.Id == id);
        rule.CurrencyOriginal = "EUR";
        await mem.Db.SaveChangesAsync();

        var row = await RowAsync(mem, Statement(29.60m, "PAYPAL *SPOTIFY"));
        Assert.False(row.Recurring!.CanUpdateAmount);

        var result = await Sut(mem).CommitAsync(new CommitImportRequest([
            new ImportRowRequest(row.Line, row.Date, row.Amount, row.Currency, 1,
                row.Description, RecurringId: id, UpdateRecurringAmount: true),
        ]));

        Assert.Equal(1, result.Value!.Failed);
        Assert.Equal(6.63m, mem.Db.RecurringExpenses.Single(r => r.Id == id).AmountOriginal);
    }

    /// The bank posts a charge when the merchant claims it, not on the day the schedule said.
    /// Over one real September that was ten days out for LuxMed and eight for MyBenefit.
    [Fact]
    public async Task A_charge_posted_days_later_is_still_the_same_payment()
    {
        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 280m, "LuxMed");

        var row = await RowAsync(mem, Statement(280m, "emarket.luxmed.pl", Today.AddDays(10)));

        Assert.NotNull(row.Recurring);
        Assert.Equal("LuxMed", row.Recurring!.Name);
    }

    /// An ordinary shop is left alone. Offering to file a Żabka run as a subscription would
    /// make the screen's question worthless.
    [Fact]
    public async Task An_ordinary_expense_is_not_offered_as_a_subscription()
    {
        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 99.16m, "Claude");

        var row = await RowAsync(mem, Statement(45.60m, "ZABKA Z1234", Today.AddDays(-40)));

        Assert.Null(row.Recurring);
    }

    /// Two categories may share a name — only envelopes have a unique index on theirs — and a
    /// duplicate used to throw straight out of the preview, so one renamed category broke the
    /// whole import screen with a 500 that explained nothing.
    [Fact]
    public async Task Two_categories_with_one_name_do_not_break_the_preview()
    {
        using var mem = new SqliteInMemory();
        await SubscriptionAsync(mem, 99.16m, "Claude");
        mem.Db.Categories.AddRange(
            new Category { Name = "Кава" }, new Category { Name = "Кава" });
        await mem.Db.SaveChangesAsync();

        var row = await RowAsync(mem, Statement(12m, "ZABKA Z1", Today.AddDays(-40)));

        Assert.Equal(12m, Math.Abs(row.Amount));
    }
}
