using System.Globalization;

namespace FinanceApp.Domain.Import;

/// Reads a date out of a cell. Deliberately a fixed list of formats rather than
/// <c>DateTime.Parse</c>: culture-guessing turns 03.08.2026 into the 8th of March somewhere
/// in the world, and a statement whose dates are silently wrong is worse than one that fails
/// to import.
public static class DateText
{
    /// Day-first everywhere, because every Polish bank writes day-first and no exporter in
    /// this list writes month-first. ISO is first: it is unambiguous and the most common.
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
        "dd.MM.yyyy", "dd-MM-yyyy", "dd/MM/yyyy",
        "dd.MM.yy", "dd-MM-yy", "dd/MM/yy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm",
    ];

    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim().Trim('"');

        foreach (var format in Formats)
        {
            if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                date = DateOnly.FromDateTime(parsed);
                return true;
            }
        }

        return false;
    }
}
