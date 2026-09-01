namespace FinanceApp.Domain.Import;

/// One line of a statement, once it has been understood.
/// <param name="Line">Where it came from in the file, so a rejected row can be pointed at.</param>
public record StatementRow(int Line, DateOnly Date, decimal Amount, string Currency, string Description);

/// A line that could not be read, and why — in words the user can act on.
public record StatementProblem(int Line, string Reason, string Raw);

/// <param name="Delimiter">What the file turned out to be separated by.</param>
/// <param name="HeaderFound">Whether a header row was recognised and skipped.</param>
public record StatementReadResult(
    IReadOnlyList<StatementRow> Rows,
    IReadOnlyList<StatementProblem> Problems,
    char Delimiter,
    bool HeaderFound,
    StatementColumns Columns);

/// Turns the text of a bank export into transactions, without being told its format.
///
/// The whole point is that no bank has to be supported by name. Polish exports differ in
/// delimiter, decimal comma, encoding and column order, and a new one appears whenever a bank
/// redesigns its web app — so the reader works out the shape from the file itself and reports
/// what it understood, rather than matching a list of known layouts.
public static class StatementReader
{
    /// Currency is per-row when the file says so, and otherwise the account's own. PLN is not
    /// hard-coded here — the caller passes what the account is in.
    public static StatementReadResult Read(string text, string defaultCurrency)
    {
        var lines = DelimitedText.SplitLines(text);
        var delimiter = DelimitedText.DetectDelimiter(lines);

        var split = lines
            .Select((line, i) => (Line: i + 1, Raw: line, Cells: DelimitedText.SplitLine(line, delimiter)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Raw))
            .ToList();

        if (split.Count == 0)
        {
            return new StatementReadResult([], [], delimiter, false,
                StatementColumns.Detect(null, []));
        }

        // The table's width is whatever most lines agree on: the preamble is narrow, the body
        // is not.
        var width = split.Select(x => x.Cells.Count)
            .GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;

        var table = split.Where(x => x.Cells.Count == width).ToList();
        var body = table;

        // A first row whose cells are mostly unparseable text, above rows that parse, is a
        // header — not a transaction that failed.
        var headerFound = false;
        IReadOnlyList<string>? header = null;

        if (table.Count > 1 && LooksLikeHeader(table[0].Cells, table.Skip(1).Take(5).ToList()))
        {
            header = table[0].Cells;
            headerFound = true;
            body = table.Skip(1).ToList();
        }

        var columns = StatementColumns.Detect(
            header, body.Select(x => (IReadOnlyList<string>)x.Cells).ToList());

        var rows = new List<StatementRow>();
        var problems = new List<StatementProblem>();

        if (!columns.IsUsable)
        {
            problems.Add(new StatementProblem(0,
                "Не видно, де в файлі дата й сума. Потрібні щонайменше ці дві колонки.", ""));
            return new StatementReadResult(rows, problems, delimiter, headerFound, columns);
        }

        foreach (var (line, raw, cells) in body)
        {
            if (!TryDate(cells, columns, out var date))
            {
                problems.Add(new StatementProblem(line, "Не вдалось прочитати дату", raw));
                continue;
            }

            if (!AmountText.TryParse(At(cells, columns.AmountAt), out var amount) || amount == 0m)
            {
                problems.Add(new StatementProblem(line, "Не вдалось прочитати суму", raw));
                continue;
            }

            var currency = At(cells, columns.CurrencyAt).Trim().ToUpperInvariant();
            if (currency.Length != 3) currency = defaultCurrency;

            rows.Add(new StatementRow(line, date, amount, currency, Describe(cells, columns)));
        }

        return new StatementReadResult(rows, problems, delimiter, headerFound, columns);
    }

    /// Banks split the merchant across "typ operacji", "nadawca" and "tytuł". Joining them
    /// keeps the part that actually names the shop, whichever column the bank put it in.
    private static string Describe(IReadOnlyList<string> cells, StatementColumns columns)
    {
        var parts = new List<string>();
        for (var i = 0; i < cells.Count && i < columns.Roles.Count; i++)
        {
            if (columns.Roles[i] != ColumnRole.Description) continue;
            var value = cells[i].Trim();
            if (value.Length > 0 && !parts.Contains(value)) parts.Add(value);
        }

        return string.Join(" · ", parts).Trim();
    }

    /// The row's date, from the date column — or from any other column holding one when that
    /// cell is empty.
    ///
    /// A card payment that has not settled yet is exported by PKO with "Data operacji" blank
    /// and only "Data waluty" filled in. Those rows are the most RECENT purchases in the file,
    /// which makes them the ones somebody catching up on a week actually needs, and dropping
    /// them let real money vanish out of an import that reported no error worth reading.
    ///
    /// Only ever a fallback: the chosen column still wins whenever it has anything in it, so a
    /// file where booking and value dates differ keeps importing by the booking date.
    private static bool TryDate(IReadOnlyList<string> cells, StatementColumns columns, out DateOnly date)
    {
        if (DateText.TryParse(At(cells, columns.DateAt), out date)) return true;

        for (var i = 0; i < cells.Count; i++)
            if (i != columns.DateAt && DateText.TryParse(cells[i], out date)) return true;

        date = default;
        return false;
    }

    private static string At(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index] : "";

    /// A header names its columns; it does not contain a date or an amount. Comparing against
    /// the rows below is what tells the two apart in a file whose words we do not know.
    private static bool LooksLikeHeader(IReadOnlyList<string> candidate, IReadOnlyList<(int, string, List<string>)> below)
    {
        var candidateParses = candidate.Count(c => DateText.TryParse(c, out _) || AmountText.TryParse(c, out _));
        if (candidateParses > 0) return false;

        // And the rows below must actually parse — otherwise this is not a header above a
        // table, it is just a file we do not understand.
        return below.Any(r => r.Item3.Any(c => DateText.TryParse(c, out _)));
    }
}
