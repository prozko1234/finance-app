using System.Text;
using FinanceApp.Application.Export;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Debts;
using FinanceApp.Domain.Savings;

namespace FinanceApp.Api.Tests;

/// «Вивантажити все».
///
/// Written for one question — "на руках було більше грошей, ніж в апці, чому" — so what it has
/// to get right is not the arithmetic but the COVERAGE: jar movements and debts are not
/// transactions, appear in no list in the app, and are therefore exactly where a missing few
/// hundred hides.
public class ExportTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static ExportService Sut(SqliteInMemory mem) => new(mem.Db);

    private static async Task<(Category Cat, Envelope Jar)> SetUpAsync(SqliteInMemory mem)
    {
        var cat = new Category { Name = "Їжа" };
        var jar = new Envelope { Name = "Подушка", Kind = BucketKind.Savings, IsDefault = true };
        mem.Db.Categories.Add(cat);
        mem.Db.Envelopes.Add(jar);
        await mem.Db.SaveChangesAsync();
        return (cat, jar);
    }

    private static Transaction Spend(
        int categoryId, decimal amount, DateOnly on,
        int? envelopeId = null, TxStatus status = TxStatus.Posted) => new()
    {
        Kind = TransactionKind.Expense, CategoryId = categoryId,
        CurrencyOriginal = "PLN", AmountOriginal = amount, AmountBase = amount,
        FxRate = 1m, FxDate = on, Date = on, EnvelopeId = envelopeId, Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// The whole point of the export. Every one of these is a real movement that leaves the
    /// daily norm alone on purpose — and every one of them is a way for the account to hold
    /// more money than the app admits to.
    [Fact]
    public async Task Says_of_every_line_whether_it_touched_what_can_be_spent()
    {
        using var mem = new SqliteInMemory();
        var (cat, jar) = await SetUpAsync(mem);

        mem.Db.Transactions.AddRange(
            Spend(cat.Id, 100m, Today),
            Spend(cat.Id, 200m, Today, envelopeId: jar.Id),
            Spend(cat.Id, 300m, Today, status: TxStatus.Pending));
        await mem.Db.SaveChangesAsync();

        var rows = await Sut(mem).LedgerAsync();

        Assert.Equal("так", rows.Single(r => r.Amount == 100m).Counts);
        Assert.Contains("з банки", rows.Single(r => r.Amount == 200m).Counts);
        Assert.Contains("не підтверджене", rows.Single(r => r.Amount == 300m).Counts);
    }

    /// Jar movements and debts are deliberately NOT transactions — which means they show up in
    /// no list anywhere in the app. If the export missed them too, the one file meant to
    /// explain a difference would be missing the usual cause of it.
    [Fact]
    public async Task Carries_the_movements_that_are_not_transactions()
    {
        using var mem = new SqliteInMemory();
        var (_, jar) = await SetUpAsync(mem);

        mem.Db.SavingsEntries.Add(new SavingsEntry
        {
            EnvelopeId = jar.Id, Date = Today, Kind = SavingsEntryKind.Deposit,
            CurrencyOriginal = "PLN", AmountOriginal = 500m, AmountBase = 500m,
            FxRate = 1m, FxDate = Today, CreatedAt = DateTimeOffset.UtcNow,
        });

        var debt = new Debt
        {
            Direction = DebtDirection.TheyOweMe, Person = "Оля", Date = Today,
            CurrencyOriginal = "PLN", AmountOriginal = 400m, AmountBase = 400m,
            FxRate = 1m, FxDate = Today, Origin = MoneySource.Spendable,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.Debts.Add(debt);
        await mem.Db.SaveChangesAsync();

        mem.Db.DebtPayments.Add(new DebtPayment
        {
            DebtId = debt.Id, Date = Today, CurrencyOriginal = "PLN",
            AmountOriginal = 150m, AmountBase = 150m, FxRate = 1m, FxDate = Today,
            Source = MoneySource.Spendable, CreatedAt = DateTimeOffset.UtcNow,
        });
        mem.Db.OpeningBalances.Add(new OpeningBalance
        {
            Date = Today, CurrencyOriginal = "PLN", AmountOriginal = 7_000m,
            AmountBase = 7_000m, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var rows = await Sut(mem).LedgerAsync();

        Assert.Contains(rows, r => r.What == "Відкладено в банку" && r.Amount == 500m);
        Assert.Contains(rows, r => r.What == "Позичив комусь" && r.Where == "Оля");
        Assert.Contains(rows, r => r.What == "Повернули мені" && r.Amount == 150m);
        Assert.Contains(rows, r => r.What == "Перерахунок залишку");
    }

    /// A deposit written down as "це вже було відкладено" costs the period nothing, and a debt
    /// entered after the money had already changed hands costs it nothing either. Both read as
    /// money the app is not accounting for this month, and the column has to say so.
    [Fact]
    public async Task Names_the_movements_that_happened_before_the_app_was_told()
    {
        using var mem = new SqliteInMemory();
        var (_, jar) = await SetUpAsync(mem);

        mem.Db.SavingsEntries.Add(new SavingsEntry
        {
            EnvelopeId = jar.Id, Date = Today, Kind = SavingsEntryKind.Deposit,
            CurrencyOriginal = "PLN", AmountOriginal = 900m, AmountBase = 900m,
            FxRate = 1m, FxDate = Today, AlreadySetAside = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        mem.Db.Debts.Add(new Debt
        {
            Direction = DebtDirection.IOwe, Person = "Сергій", Date = Today,
            CurrencyOriginal = "PLN", AmountOriginal = 250m, AmountBase = 250m,
            FxRate = 1m, FxDate = Today, Origin = MoneySource.AlreadyHappened,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var rows = await Sut(mem).LedgerAsync();

        Assert.Contains("раніше", rows.Single(r => r.Amount == 900m).Counts);
        Assert.Contains("раніше", rows.Single(r => r.Amount == 250m).Counts);
    }

    [Fact]
    public async Task Comes_back_in_date_order()
    {
        using var mem = new SqliteInMemory();
        var (cat, _) = await SetUpAsync(mem);

        mem.Db.Transactions.AddRange(
            Spend(cat.Id, 10m, Today),
            Spend(cat.Id, 20m, Today.AddDays(-5)),
            Spend(cat.Id, 30m, Today.AddDays(-2)));
        await mem.Db.SaveChangesAsync();

        var rows = await Sut(mem).LedgerAsync();

        Assert.Equal([20m, 30m, 10m], rows.Select(r => r.Amount));
    }

    /// The file is opened, not parsed: semicolons, decimal commas and a byte-order mark are
    /// what a Polish-locale Excel or Numbers reads without an import dialog — and without the
    /// mark the Cyrillic headings arrive as mojibake.
    [Fact]
    public async Task The_csv_opens_in_a_spreadsheet_without_a_dialog()
    {
        using var mem = new SqliteInMemory();
        var (cat, _) = await SetUpAsync(mem);
        mem.Db.Transactions.Add(Spend(cat.Id, 1_234.50m, Today));
        await mem.Db.SaveChangesAsync();

        var bytes = LedgerCsv.Write(await Sut(mem).LedgerAsync(), "PLN");

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Дата;Що;Сума", text);
        Assert.Contains("1234,50", text);
    }

    /// A note with a semicolon in it would otherwise silently push every later column one to
    /// the right — the kind of corruption nobody notices until the totals are wrong.
    [Fact]
    public async Task A_note_with_a_semicolon_does_not_break_the_columns()
    {
        using var mem = new SqliteInMemory();
        var (cat, _) = await SetUpAsync(mem);

        var tx = Spend(cat.Id, 50m, Today);
        tx.Note = "хліб; молоко";
        mem.Db.Transactions.Add(tx);
        await mem.Db.SaveChangesAsync();

        var text = Encoding.UTF8.GetString(LedgerCsv.Write(await Sut(mem).LedgerAsync(), "PLN"));

        Assert.Contains("\"хліб; молоко\"", text);
    }

    [Fact]
    public async Task An_empty_app_exports_a_file_with_only_headings()
    {
        using var mem = new SqliteInMemory();

        var rows = await Sut(mem).LedgerAsync();
        var text = Encoding.UTF8.GetString(LedgerCsv.Write(rows, "PLN"));

        Assert.Empty(rows);
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
