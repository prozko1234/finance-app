using System.Globalization;
using System.Text;
using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Debts;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Export;

/// One line of the flat ledger — every movement of money the app knows about, whatever table
/// it lives in.
/// <param name="Counts">Whether this line moved «можна витратити», in words. This is the
/// column the whole export exists for: an expense paid out of a jar, an unconfirmed
/// subscription charge and a deposit marked as set aside earlier all leave the daily norm
/// alone, and each of them is a way for the account to hold more money than the app says.</param>
public record LedgerRow(
    DateOnly Date,
    string What,
    decimal Amount,
    string Currency,
    decimal AmountBase,
    string Counts,
    string Where,
    string? Note,
    string Source,
    string Id);

public interface IExportService
{
    /// Every movement, oldest first, as one flat table.
    Task<IReadOnlyList<LedgerRow>> LedgerAsync(CancellationToken ct = default);

    /// The same data with nothing left out and nothing interpreted — a copy to keep.
    Task<object> BackupAsync(CancellationToken ct = default);
}

/// «Вивантажити все».
///
/// Written for one question: "на руках було більше грошей, ніж в апці — чому". Nothing here
/// computes a verdict, because a verdict is exactly what cannot be trusted when the app and
/// the account already disagree. It lays every movement out in one table, in date order, with
/// a column saying whether that movement touched the daily norm — and lets the two be
/// compared by eye against a bank statement.
///
/// Debts and jar movements are in it precisely because they are NOT transactions: they are
/// deliberately invisible to categories and statistics, which makes them the first place a
/// missing few hundred hides.
public sealed class ExportService(IAppDbContext db) : IExportService
{
    public async Task<IReadOnlyList<LedgerRow>> LedgerAsync(CancellationToken ct = default)
    {
        var rows = new List<LedgerRow>();

        var transactions = await db.Transactions
            .Select(t => new
            {
                t.Id, t.Kind, t.Date, t.AmountOriginal, t.CurrencyOriginal, t.AmountBase,
                t.Status, t.Source, t.Note, t.MerchantRaw, t.EnvelopeId, t.RecurringExpenseId,
                CategoryName = t.Category!.Name, EnvelopeName = t.Envelope!.Name,
            })
            .ToListAsync(ct);

        foreach (var t in transactions)
        {
            var income = t.Kind == TransactionKind.Income;
            rows.Add(new LedgerRow(
                t.Date,
                income ? "Дохід" : t.RecurringExpenseId is null ? "Витрата" : "Списання підписки",
                t.AmountOriginal, t.CurrencyOriginal, t.AmountBase,
                income
                    ? "так — через бюджет, після податків"
                    : t.Status == TxStatus.Pending
                        ? "ні — списання ще не підтверджене"
                        : t.EnvelopeId is not null
                            ? "ні — заплачено з банки"
                            : "так",
                t.EnvelopeId is not null ? $"банка «{t.EnvelopeName}»" : t.CategoryName,
                t.Note ?? t.MerchantRaw,
                t.Source.ToString(),
                $"tx-{t.Id}"));
        }

        var jars = await db.SavingsEntries
            .Select(x => new
            {
                x.Id, x.Date, x.Kind, x.AmountOriginal, x.CurrencyOriginal, x.AmountBase,
                x.AlreadySetAside, x.IsAuto, x.Note, EnvelopeName = x.Envelope!.Name,
            })
            .ToListAsync(ct);

        foreach (var j in jars)
        {
            var deposit = j.Kind == SavingsEntryKind.Deposit;
            rows.Add(new LedgerRow(
                j.Date,
                deposit ? "Відкладено в банку" : "Знято з банки",
                j.AmountOriginal, j.CurrencyOriginal, j.AmountBase,
                j.AlreadySetAside
                    ? "ні — ці гроші були відкладені раніше"
                    : deposit
                        ? "так — пішло з норми в банку"
                        : "так — повернулось у бюджет",
                $"банка «{j.EnvelopeName}»",
                j.Note,
                j.IsAuto ? "Схема" : "Manual",
                $"jar-{j.Id}"));
        }

        var debts = await db.Debts
            .Select(d => new
            {
                d.Id, d.Date, d.Direction, d.Origin, d.Person, d.AmountOriginal,
                d.CurrencyOriginal, d.AmountBase, d.Note,
                EnvelopeName = d.OriginEnvelope!.Name,
            })
            .ToListAsync(ct);

        foreach (var d in debts)
        {
            var lent = d.Direction == DebtDirection.TheyOweMe;
            rows.Add(new LedgerRow(
                d.Date,
                lent ? "Позичив комусь" : "Взяв у борг",
                d.AmountOriginal, d.CurrencyOriginal, d.AmountBase,
                Moved(d.Origin, d.EnvelopeName, lent ? "пішло з норми" : "додалось у бюджет"),
                d.Person,
                d.Note,
                "Manual",
                $"debt-{d.Id}"));
        }

        var payments = await db.DebtPayments
            .Select(p => new
            {
                p.Id, p.Date, p.Source, p.AmountOriginal, p.CurrencyOriginal, p.AmountBase,
                p.Note, Direction = p.Debt!.Direction, Person = p.Debt.Person,
                EnvelopeName = p.Envelope!.Name,
            })
            .ToListAsync(ct);

        foreach (var p in payments)
        {
            var back = p.Direction == DebtDirection.TheyOweMe;
            rows.Add(new LedgerRow(
                p.Date,
                back ? "Повернули мені" : "Віддав борг",
                p.AmountOriginal, p.CurrencyOriginal, p.AmountBase,
                Moved(p.Source, p.EnvelopeName, back ? "повернулось у бюджет" : "пішло з норми"),
                p.Person,
                p.Note,
                "Manual",
                $"debt-pay-{p.Id}"));
        }

        // Not movements — the two places the app was TOLD what the truth is. They belong in the
        // table because they are where a discrepancy gets absorbed rather than explained: an
        // opening balance silently redefines the budget from that day on.
        var counted = await db.OpeningBalances
            .Select(o => new { o.Id, o.Date, o.AmountOriginal, o.CurrencyOriginal, o.AmountBase })
            .ToListAsync(ct);

        foreach (var o in counted)
            rows.Add(new LedgerRow(
                o.Date, "Перерахунок залишку", o.AmountOriginal, o.CurrencyOriginal, o.AmountBase,
                "не рух — бюджет перерахований від цієї суми",
                "—", null, "Manual", $"balance-{o.Id}"));

        var carried = await db.PeriodCarryovers
            .Select(c => new { c.Id, c.PeriodStart, c.AmountBase, c.Decision })
            .ToListAsync(ct);

        foreach (var c in carried)
            rows.Add(new LedgerRow(
                c.PeriodStart, "Залишок минулого періоду", c.AmountBase, Money.BaseCurrency,
                c.AmountBase,
                c.Decision switch
                {
                    CarryoverDecision.ToBudget => "так — додано в бюджет періоду",
                    CarryoverDecision.ToEnvelope => "ні — покладено в банку",
                    _ => "ні — вирішено не рахувати",
                },
                "—", null, "Manual", $"carryover-{c.Id}"));

        return rows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// How a debt's money moved, in the same words the ledger uses everywhere else.
    private static string Moved(MoneySource source, string? envelope, string effect) => source switch
    {
        MoneySource.Spendable => $"так — {effect}",
        MoneySource.Envelope => $"ні — з банки «{envelope}»",
        _ => "ні — рух був раніше, ніж записали",
    };

    public async Task<object> BackupAsync(CancellationToken ct = default) => new
    {
        exportedAt = DateTimeOffset.Now,
        baseCurrency = Money.BaseCurrency,
        transactions = await db.Transactions
            .Select(t => new
            {
                t.Id, Kind = t.Kind.ToString(), t.Date, t.AmountOriginal, t.CurrencyOriginal,
                t.AmountBase, t.FxRate, t.FxDate, t.CategoryId, CategoryName = t.Category!.Name,
                t.EnvelopeId, t.RecurringExpenseId, Status = t.Status.ToString(),
                Source = t.Source.ToString(), Frequency = t.Frequency.ToString(),
                t.GrossWithVat, t.VatAmount, t.MerchantRaw, t.Note, t.CreatedAt,
            })
            .ToListAsync(ct),
        categories = await db.Categories
            .Select(c => new { c.Id, c.Name, c.Icon, c.Color, Kind = c.Kind.ToString(), c.SortOrder })
            .ToListAsync(ct),
        envelopes = await db.Envelopes
            .Select(e => new
            {
                e.Id, e.Name, Kind = e.Kind.ToString(), e.IsDefault, e.TargetAmount,
                e.TargetDate, e.ArchivedAt,
            })
            .ToListAsync(ct),
        savingsEntries = await db.SavingsEntries
            .Select(x => new
            {
                x.Id, x.EnvelopeId, x.Date, Kind = x.Kind.ToString(), x.AmountOriginal,
                x.CurrencyOriginal, x.AmountBase, x.FxRate, x.FxDate, x.IsAuto,
                x.AlreadySetAside, x.TransferKey, x.Note, x.CreatedAt,
            })
            .ToListAsync(ct),
        recurring = await db.RecurringExpenses
            .Select(r => new
            {
                r.Id, r.CategoryId, Kind = r.Kind.ToString(), r.AmountOriginal,
                r.CurrencyOriginal, r.StartsOn, Unit = r.Unit.ToString(), r.Interval,
                r.Active, r.Note,
            })
            .ToListAsync(ct),
        debts = await db.Debts
            .Select(d => new
            {
                d.Id, Direction = d.Direction.ToString(), d.Person, d.AmountOriginal,
                d.CurrencyOriginal, d.AmountBase, d.Date, d.Deadline, d.ReserveFromBudget,
                Origin = d.Origin.ToString(), d.OriginEnvelopeId, d.ClosedOn, d.Note,
            })
            .ToListAsync(ct),
        debtPayments = await db.DebtPayments
            .Select(p => new
            {
                p.Id, p.DebtId, p.Date, p.AmountOriginal, p.CurrencyOriginal, p.AmountBase,
                Source = p.Source.ToString(), p.EnvelopeId, p.Note,
            })
            .ToListAsync(ct),
        openingBalances = await db.OpeningBalances
            .Select(o => new { o.Id, o.Date, o.AmountOriginal, o.CurrencyOriginal, o.AmountBase })
            .ToListAsync(ct),
        carryovers = await db.PeriodCarryovers
            .Select(c => new { c.Id, c.PeriodStart, c.AmountBase, Decision = c.Decision.ToString() })
            .ToListAsync(ct),
        settings = await db.AppSettings
            .Select(s => new { s.DisplayCurrency, s.PeriodStartDay })
            .ToListAsync(ct),
    };
}

/// The ledger as a spreadsheet opens it.
///
/// Semicolons and decimal commas, with a byte-order mark: that is the combination a
/// Polish-locale Excel or Numbers opens without an import dialog, and this file exists to be
/// looked at rather than parsed.
public static class LedgerCsv
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static byte[] Write(IReadOnlyList<LedgerRow> rows, string baseCurrency)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';',
        [
            "Дата", "Що", "Сума", "Валюта", $"Сума, {baseCurrency}",
            "Впливає на «можна витратити»", "Де / з ким", "Нотатка", "Звідки запис", "ID",
        ]));

        foreach (var r in rows)
            sb.AppendLine(string.Join(';',
            [
                r.Date.ToString("yyyy-MM-dd"),
                Escape(r.What),
                r.Amount.ToString("0.00", Pl),
                r.Currency,
                r.AmountBase.ToString("0.00", Pl),
                Escape(r.Counts),
                Escape(r.Where),
                Escape(r.Note ?? ""),
                r.Source,
                r.Id,
            ]));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// A note can hold a semicolon, a quote or a newline — all three break the column layout
    /// unless the field is quoted and its own quotes doubled.
    private static string Escape(string value)
    {
        if (!value.Contains(';') && !value.Contains('"') && !value.Contains('\n')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
