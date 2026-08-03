namespace FinanceApp.Domain.Import;

/// What a column turned out to hold.
public enum ColumnRole { Ignored, Date, Amount, Currency, Description, Balance }

/// Works out which column is which, so any export with enough information in it can be
/// imported without the user describing its layout.
///
/// Two signals, and the second is the one that matters: header names are matched against the
/// words Polish, Ukrainian and English exports actually use, but a file with no header, or
/// with one nobody anticipated, is still readable because the *content* of a column says what
/// it is — a column where four fifths of the cells parse as dates is the date column.
public sealed class StatementColumns
{
    private StatementColumns(IReadOnlyList<ColumnRole> roles) => Roles = roles;

    public IReadOnlyList<ColumnRole> Roles { get; }

    public int DateAt => IndexOf(ColumnRole.Date);
    public int AmountAt => IndexOf(ColumnRole.Amount);
    public int CurrencyAt => IndexOf(ColumnRole.Currency);
    public int DescriptionAt => IndexOf(ColumnRole.Description);

    /// The two columns without which a row is not a transaction. Everything else can be
    /// guessed, defaulted or left blank.
    public bool IsUsable => DateAt >= 0 && AmountAt >= 0;

    private int IndexOf(ColumnRole role)
    {
        for (var i = 0; i < Roles.Count; i++)
            if (Roles[i] == role) return i;
        return -1;
    }

    /// How sure a header word makes us. Kept apart from content scoring so a header that
    /// says "Data operacji" wins over a column that merely looks date-shaped.
    private static readonly (ColumnRole Role, string[] Words)[] HeaderWords =
    [
        (ColumnRole.Date, ["data operacji", "data ksieg", "data księg", "data transakcji", "data waluty",
                           "data", "dата", "дата", "date", "completed date", "started date", "booking"]),
        (ColumnRole.Amount, ["kwota operacji", "kwota", "obciazenia", "obciążenia", "uznania", "amount",
                             "сума", "сумма", "wplyw", "wpływ", "wydatek", "value"]),
        (ColumnRole.Currency, ["waluta", "currency", "валюта"]),
        (ColumnRole.Balance, ["saldo", "balance", "stan konta", "залишок"]),
        (ColumnRole.Description, ["opis", "tytul", "tytuł", "nadawca", "odbiorca", "kontrahent",
                                  "description", "details", "nazwa", "опис", "призначення", "merchant",
                                  "typ operacji", "rodzaj"]),
    ];

    /// <param name="header">The header row, or null when the file has none.</param>
    /// <param name="rows">Data rows, used to check what the columns actually contain.</param>
    public static StatementColumns Detect(IReadOnlyList<string>? header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var width = Math.Max(header?.Count ?? 0, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        var roles = new ColumnRole[width];

        var dateScores = new double[width];
        var amountScores = new double[width];

        for (var col = 0; col < width; col++)
        {
            var cells = rows.Where(r => col < r.Count).Select(r => r[col]).ToList();
            var filled = cells.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (filled.Count == 0) continue;

            dateScores[col] = filled.Count(c => DateText.TryParse(c, out _)) / (double)filled.Count;
            amountScores[col] = filled.Count(c => AmountText.TryParse(c, out _)) / (double)filled.Count;

            var name = col < (header?.Count ?? 0) ? header![col].ToLowerInvariant() : "";
            if (name.Length > 0 && MatchHeader(name) is { } named) roles[col] = named;
        }

        // A number is also a valid date to nobody, but a date column often parses as a number
        // once the separators are stripped — so dates are claimed first.
        ClaimByContent(roles, dateScores, ColumnRole.Date, threshold: 0.8);

        // The amount is the numeric column that is NOT the balance. A running balance parses
        // just as well, which is why the header hint for "saldo" earns its place.
        ClaimByContent(roles, amountScores, ColumnRole.Amount, threshold: 0.8, skip: ColumnRole.Date);

        // Whatever is left and holds text becomes the description: banks spread the merchant
        // over two or three columns, and the reader joins them.
        for (var col = 0; col < width; col++)
        {
            if (roles[col] != ColumnRole.Ignored) continue;
            if (dateScores[col] > 0.5 || amountScores[col] > 0.5) continue;
            roles[col] = ColumnRole.Description;
        }

        return new StatementColumns(roles);
    }

    private static ColumnRole? MatchHeader(string name)
    {
        foreach (var (role, words) in HeaderWords)
            if (words.Any(name.Contains)) return role;

        return null;
    }

    /// Gives the role to the best-scoring column that has not already been claimed — but only
    /// if nothing claimed it by header first, and only above the threshold. Below it, the
    /// column is not that thing and guessing would import nonsense.
    private static void ClaimByContent(
        ColumnRole[] roles, double[] scores, ColumnRole role, double threshold, ColumnRole? skip = null)
    {
        if (roles.Contains(role)) return;

        var best = -1;
        var bestScore = threshold;

        for (var col = 0; col < roles.Length; col++)
        {
            if (roles[col] != ColumnRole.Ignored && roles[col] != skip) continue;
            if (roles[col] == skip) continue;
            if (scores[col] > bestScore)
            {
                bestScore = scores[col];
                best = col;
            }
        }

        if (best >= 0) roles[best] = role;
    }
}
