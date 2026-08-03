using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Import;

public interface IImportService
{
    /// Reads the file and says what it understood, without writing anything. Importing money
    /// sight-unseen is not a thing anyone should be asked to agree to.
    Task<Result<ImportPreviewResponse>> PreviewAsync(byte[] file, CancellationToken ct = default);

    /// Writes the rows the user kept. Returns what went in and what did not, per row.
    Task<Result<ImportResultResponse>> CommitAsync(CommitImportRequest req, CancellationToken ct = default);
}

public sealed class ImportService(IAppDbContext db, ITransactionService transactions, ISettingsService settings)
    : IImportService
{
    public async Task<Result<ImportPreviewResponse>> PreviewAsync(byte[] file, CancellationToken ct = default)
    {
        if (file.Length == 0) return Error.Validation("Файл порожній.");

        var text = StatementEncoding.Decode(file, out var encoding);
        var baseCurrency = (await settings.GetAsync(ct)).BaseCurrency;
        var read = StatementReader.Read(text, baseCurrency);

        var previews = new List<ImportRowPreview>(read.Rows.Count);
        if (read.Rows.Count > 0)
        {
            // Duplicates are looked for once, over the span the file covers, rather than with
            // a query per row: a year of statements is thousands of rows and would otherwise
            // be thousands of round trips.
            var from = read.Rows.Min(r => r.Date);
            var to = read.Rows.Max(r => r.Date);

            // Learned rules first, the built-in list second. What the user has filed
            // themselves is a fact about them; the list is a guess about people in general.
            var learned = await db.MerchantRules
                .Select(r => new { r.Key, r.CategoryId })
                .ToDictionaryAsync(r => r.Key, r => r.CategoryId, ct);
            var byName = await db.Categories
                .ToDictionaryAsync(c => c.Name, c => c.Id, ct);

            var existing = await db.Transactions
                .Where(t => t.Date >= from && t.Date <= to)
                .Select(t => new { t.Id, t.Date, t.AmountOriginal, t.CurrencyOriginal, t.Kind })
                .ToListAsync(ct);

            foreach (var row in read.Rows)
            {
                var kind = row.Amount < 0 ? TransactionKind.Expense : TransactionKind.Income;
                var size = Math.Abs(row.Amount);

                // Same day, same money, same direction. Deliberately not compared on the
                // description: the bank writes "BIEDRONKA 1234 KRAKOW" where the user typed
                // "продукти", and the point is to catch the row they already entered by hand
                // as much as the one they already imported.
                var duplicate = existing.FirstOrDefault(t =>
                    t.Date == row.Date
                    && t.Kind == kind
                    && t.CurrencyOriginal == row.Currency
                    && t.AmountOriginal == size);

                var key = MerchantKey.From(row.Description);
                previews.Add(new ImportRowPreview(
                    row.Line, row.Date, row.Amount, row.Currency, row.Description,
                    MerchantKey.Clean(row.Description), key,
                    kind.ToString(), duplicate?.Id,
                    SuggestFor(key, learned, byName)));
            }
        }

        return Result<ImportPreviewResponse>.Ok(new ImportPreviewResponse(
            previews,
            read.Problems.Select(p => new ImportProblemResponse(p.Line, p.Reason, Shorten(p.Raw))).ToList(),
            read.Delimiter.ToString(),
            read.HeaderFound,
            encoding,
            read.Columns.Roles.Select(r => r.ToString()).ToList()));
    }

    public async Task<Result<ImportResultResponse>> CommitAsync(
        CommitImportRequest req, CancellationToken ct = default)
    {
        if (req.Rows.Count == 0) return Error.Validation("Нема чого імпортувати.");

        var created = 0;
        var problems = new List<ImportProblemResponse>();

        foreach (var row in req.Rows)
        {
            // Row by row, and a failure only costs its own row: a rate missing for one day
            // must not throw away an import of three hundred others.
            var result = row.Amount < 0
                ? await ImportExpenseAsync(row, ct)
                : await ImportIncomeAsync(row, ct);

            if (result.IsSuccess)
            {
                created++;
                // Only expenses teach: an income row's description is a client or an employer,
                // and filing "every payment from ACME is Дохід" would be a rule about one
                // category that already has only one member.
                if (row.Amount < 0) await RememberAsync(MerchantKey.From(row.Note), row.CategoryId, ct);
            }
            else problems.Add(new ImportProblemResponse(row.Line, result.Error.Message, row.Note ?? ""));
        }

        return Result<ImportResultResponse>.Ok(new ImportResultResponse(created, problems.Count, problems));
    }

    /// The category this shop most likely belongs to, or null when nothing knows it — and
    /// then the screen asks rather than guessing, because a wrong category is silently wrong
    /// and stays that way.
    private static int? SuggestFor(
        string key, IReadOnlyDictionary<string, int> learned, IReadOnlyDictionary<string, int> byName)
    {
        if (key.Length == 0) return null;
        if (learned.TryGetValue(key, out var learnedId)) return learnedId;

        var name = BuiltInMerchants.CategoryNameFor(key);
        return name is not null && byName.TryGetValue(name, out var id) ? id : null;
    }

    /// Remembers where the user filed this shop, so the same shop never has to be filed
    /// twice. Called on commit rather than on every keystroke in the preview: a category
    /// chosen and then changed again should not leave a rule behind.
    private async Task RememberAsync(string key, int categoryId, CancellationToken ct)
    {
        if (key.Length == 0 || key.Length > MerchantRule.MaxKeyLength) return;

        var rule = await db.MerchantRules.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is null)
        {
            db.MerchantRules.Add(new MerchantRule
            {
                Key = key, CategoryId = categoryId, Hits = 1,
                CreatedAt = DateTimeOffset.UtcNow, LastUsedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            // The newest answer wins: filing a shop somewhere else is a correction, and a
            // rule that argued with the user would be a rule they cannot get rid of.
            rule.CategoryId = categoryId;
            rule.Hits++;
            rule.LastUsedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Result<TransactionResponse>> ImportExpenseAsync(
        ImportRowRequest row, CancellationToken ct) =>
        await transactions.CreateAsync(new SaveTransactionRequest(
            Math.Abs(row.Amount), row.Currency, row.CategoryId,
            Frequency.OneOff, row.Date, Merchant: row.Note, Note: row.Note), ct);

    /// Income goes through the income path, not the plain one: it carries a VAT split, and a
    /// salary imported as an ordinary row would put gross where revenue belongs and move the
    /// month's tax figure by the whole VAT.
    private async Task<Result<TransactionResponse>> ImportIncomeAsync(
        ImportRowRequest row, CancellationToken ct) =>
        await transactions.CreateIncomeAsync(new SaveIncomeRequest(
            row.Amount, row.AmountIncludesVat, row.Currency, row.Date, row.Note), ct);

    /// The raw line is shown next to the problem so the user can see what confused it. A
    /// whole line of a wide export would push everything else off the screen.
    private static string Shorten(string raw) =>
        raw.Length <= 120 ? raw : raw[..120] + "…";
}
