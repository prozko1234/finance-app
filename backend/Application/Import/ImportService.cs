using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;
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

public sealed class ImportService(
    IAppDbContext db, ITransactionService transactions, ISettingsService settings,
    IRecurringService recurring, Recurring.IRecurringMaterializer materializer)
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
            // Grouped rather than keyed straight into a dictionary: nothing stops two
            // categories sharing a name — only envelopes have a unique index on theirs — and
            // a duplicate used to throw, so one renamed category could break the whole import
            // screen with a 500 that said nothing. The oldest wins, arbitrarily but stably.
            var byName = (await db.Categories
                    .OrderBy(c => c.Id)
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync(ct))
                .GroupBy(c => c.Name)
                .ToDictionary(g => g.Key, g => g.First().Id);

            var existing = await db.Transactions
                .Where(t => t.Date >= from && t.Date <= to)
                .Select(t => new { t.Id, t.Date, t.AmountOriginal, t.CurrencyOriginal, t.Kind })
                .ToListAsync(ct);

            var subscriptions = await SubscriptionsAsync(from, to, ct);

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
                    SuggestFor(key, learned, byName),
                    kind == TransactionKind.Expense
                        ? MatchSubscription(row, key, subscriptions, row.Currency)
                        : null));
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

        var confirmed = 0;
        var repriced = 0;

        foreach (var row in req.Rows)
        {
            // Answered as a subscription's charge: nothing is created. The charge the app had
            // already written is the one real record of this payment, and importing the row as
            // well would make the period pay for it twice.
            if (row.RecurringId is { } recurringId)
            {
                var linked = await ConfirmSubscriptionAsync(row, recurringId, ct);
                if (linked.IsSuccess)
                {
                    confirmed++;
                    if (linked.Value) repriced++;
                }
                else problems.Add(new ImportProblemResponse(row.Line, linked.Error.Message, row.Note ?? ""));
                continue;
            }

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

        return Result<ImportResultResponse>.Ok(
            new ImportResultResponse(created, problems.Count, problems, confirmed, repriced));
    }

    /// Answers one statement row as a subscription's charge: optionally corrects the price,
    /// confirms the charge, and teaches the subscription which shop it shows up as.
    ///
    /// Returns whether the price was changed, so the screen can say how many subscriptions
    /// this import repriced — that is the half of the result worth reading.
    private async Task<Result<bool>> ConfirmSubscriptionAsync(
        ImportRowRequest row, int recurringId, CancellationToken ct)
    {
        var rule = await db.RecurringExpenses.FirstOrDefaultAsync(r => r.Id == recurringId, ct);
        if (rule is null) return Error.NotFound($"Регулярний платіж {recurringId} не знайдено.");

        var amount = Math.Abs(row.Amount);
        var repriced = false;

        if (row.UpdateRecurringAmount)
        {
            // Refused rather than converted. The statement shows złoty; a rule kept in euro
            // would have its price set to the złoty figure — 6,63 EUR becoming 29,60 EUR —
            // and every later charge would be wrong by the exchange rate.
            if (!string.Equals(rule.CurrencyOriginal, row.Currency, StringComparison.OrdinalIgnoreCase))
                return Error.Validation(
                    $"Підписка в {rule.CurrencyOriginal}, а виписка в {row.Currency} — " +
                    "ціну з такого рядка взяти не вийде, виправ її вручну.");

            if (rule.AmountOriginal != amount)
            {
                rule.AmountOriginal = amount;
                repriced = true;
            }
        }

        // Learned on the way through, exactly as a category is: said once, recognised for good.
        var key = MerchantKey.From(row.Note);
        if (key.Length > 0 && key.Length <= 60) rule.MerchantKey = key;

        await db.SaveChangesAsync(ct);

        // After the rule is saved, so a re-priced charge is re-read at its new figure. The
        // materializer rewrites an unconfirmed charge when the price changes, which is why the
        // charge is looked up now rather than taken from the request.
        await materializer.MaterializeDueAsync(ct);

        var charge = (await db.Transactions
                .Where(t => t.RecurringExpenseId == recurringId)
                .Select(t => new { t.Id, t.Date, t.Status })
                .ToListAsync(ct))
            .Where(t => Math.Abs(t.Date.DayNumber - row.Date.DayNumber) <= ChargeWindowDays)
            .OrderBy(t => t.Status == TxStatus.Pending ? 0 : 1)
            .ThenBy(t => Math.Abs(t.Date.DayNumber - row.Date.DayNumber))
            .FirstOrDefault();

        if (charge is null)
            return Error.Validation(
                $"Для «{rule.Note ?? rule.Id.ToString()}» немає списання біля {row.Date:dd.MM} — " +
                "підтверджувати нічого.");

        var confirmed = await recurring.ConfirmChargeAsync(charge.Id, ct);
        return confirmed.IsSuccess ? Result<bool>.Ok(repriced) : confirmed.Error;
    }

    /// How far apart a statement row and the charge the app wrote for it may be and still be
    /// the same payment.
    ///
    /// Generous on purpose, and measured in real data: the app writes a charge on the day the
    /// schedule says, the bank posts it when the merchant claims it, and over one September
    /// those differed by ten days for LuxMed and eight for MyBenefit. Too tight a window and
    /// the app quietly imports a second copy of a bill it is already holding.
    private const int ChargeWindowDays = 12;

    /// One subscription, with the charge nearest the window the file covers.
    private sealed record Subscription(
        int Id, string Name, decimal Amount, string Currency, string? MerchantKey,
        IReadOnlyList<(int Id, DateOnly Date, decimal Amount, TxStatus Status)> Charges);

    private async Task<List<Subscription>> SubscriptionsAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rules = await db.RecurringExpenses
            .Where(r => r.Active && r.Kind == TransactionKind.Expense)
            .Select(r => new
            {
                r.Id, r.Note, r.AmountOriginal, r.CurrencyOriginal, r.MerchantKey,
                CategoryName = r.Category!.Name,
            })
            .ToListAsync(ct);

        if (rules.Count == 0) return [];

        // Widened by the window on both sides: a charge written just outside the file's range
        // can still be the one a row at its edge belongs to.
        var charges = await db.Transactions
            .Where(t => t.RecurringExpenseId != null
                        && t.Date >= from.AddDays(-ChargeWindowDays)
                        && t.Date <= to.AddDays(ChargeWindowDays))
            .Select(t => new { Rule = t.RecurringExpenseId!.Value, t.Id, t.Date, t.AmountBase, t.Status })
            .ToListAsync(ct);

        return rules
            .Select(r => new Subscription(
                r.Id,
                string.IsNullOrWhiteSpace(r.Note) ? r.CategoryName : r.Note!,
                r.AmountOriginal, r.CurrencyOriginal, r.MerchantKey,
                charges.Where(c => c.Rule == r.Id)
                    .Select(c => (c.Id, c.Date, c.AmountBase, c.Status))
                    .ToList()))
            .ToList();
    }

    /// The subscription a statement row is a charge for, if any.
    ///
    /// Matched on the SHOP and the date, never on the amount — the amount is the thing that
    /// changes, and it is the whole reason this matters. A subscription whose price rose writes
    /// its charge at the old figure, so the row does not look like a duplicate of it, and
    /// importing it as an ordinary expense makes the period pay twice at two prices.
    ///
    /// A key stored on the subscription is a fact and matches outright. Without one the date is
    /// all there is, so the nearest unanswered charge is offered as a GUESS for the user to
    /// confirm — and confirming is what teaches the key, so each shop is asked about once.
    private static RecurringMatchResponse? MatchSubscription(
        StatementRow row, string key, List<Subscription> subscriptions, string currency)
    {
        var learned = key.Length > 0
            ? subscriptions.FirstOrDefault(s => string.Equals(s.MerchantKey, key, StringComparison.OrdinalIgnoreCase))
            : null;

        var subscription = learned;
        if (subscription is null)
        {
            // Only subscriptions nothing is known about yet: one with a key of its own has
            // already said which shop it is, and it is not this one.
            subscription = subscriptions
                .Where(s => string.IsNullOrEmpty(s.MerchantKey))
                .Where(s => s.Charges.Any(c => Near(c.Date, row.Date)))
                .OrderBy(s => s.Charges.Where(c => Near(c.Date, row.Date))
                    .Min(c => Math.Abs(c.Date.DayNumber - row.Date.DayNumber)))
                .FirstOrDefault();
            if (subscription is null) return null;
        }

        // The unanswered charge first: that is the one worth acting on. Nearest by date after
        // that. A learned subscription with no charge in range is still reported — it names
        // the shop, and the row must not be imported as an ordinary expense on its own.
        var charge = subscription.Charges
            .Where(c => Near(c.Date, row.Date))
            .OrderBy(c => c.Status == TxStatus.Pending ? 0 : 1)
            .ThenBy(c => Math.Abs(c.Date.DayNumber - row.Date.DayNumber))
            .Cast<(int Id, DateOnly Date, decimal Amount, TxStatus Status)?>()
            .FirstOrDefault();

        return new RecurringMatchResponse(
            subscription.Id,
            subscription.Name,
            subscription.Amount,
            subscription.Currency,
            Learned: learned is not null,
            // A rule in euro seen as złoty on a statement would have its price overwritten
            // with the converted figure — 6,63 EUR becoming 29,60 EUR.
            CanUpdateAmount: string.Equals(subscription.Currency, currency, StringComparison.OrdinalIgnoreCase),
            charge?.Id,
            charge?.Date,
            charge?.Amount,
            charge?.Status.ToString());
    }

    private static bool Near(DateOnly a, DateOnly b) =>
        Math.Abs(a.DayNumber - b.DayNumber) <= ChargeWindowDays;

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
