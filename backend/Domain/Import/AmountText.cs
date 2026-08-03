using System.Globalization;

namespace FinanceApp.Domain.Import;

/// Reads the amount out of a cell, whatever shape the bank wrote it in.
///
/// Polish exports are the reason this cannot be `decimal.Parse`: they use a comma for the
/// decimal point, a space (often a non-breaking one) for thousands, sometimes a currency
/// code glued to the number, and sometimes a minus written as parentheses.
public static class AmountText
{
    /// The characters banks use as thousands separators. Apostrophes come from Swiss-style
    /// exports, which Revolut has been known to produce.
    private static readonly char[] GroupSeparators = [' ', ' ', ' ', '\'', '_'];

    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();

        // "(1 234,56)" is how some exports write a debit. Unwrap before anything else, or the
        // brackets end up as stray characters and the row is dropped as unreadable.
        var negatedByBrackets = s.StartsWith('(') && s.EndsWith(')');
        if (negatedByBrackets) s = s[1..^1].Trim();

        // Strip everything that is not part of a number: "PLN", "zł", "EUR", stray quotes.
        s = new string(s.Where(c => char.IsDigit(c) || c is ',' or '.' or '-' or '+'
                                    || GroupSeparators.Contains(c)).ToArray());
        foreach (var sep in GroupSeparators) s = s.Replace(sep.ToString(), "");
        if (s.Length == 0) return false;

        var negative = negatedByBrackets || s.StartsWith('-');
        s = s.TrimStart('-', '+');

        if (!TryParseDecimalPoint(s, out amount)) return false;

        if (negative) amount = -amount;
        return true;
    }

    /// Decides whether "." or "," is the decimal point. Both appear, sometimes in the same
    /// file, so the separator has to be read off the number rather than configured.
    private static bool TryParseDecimalPoint(string s, out decimal amount)
    {
        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');

        // Whichever comes last is the decimal point: in "1.234,56" it is the comma, in
        // "1,234.56" the dot. With only one of them present, it is the decimal point unless
        // it splits the number into groups of three — "1.234" is a thousand, not 1.234.
        int decimalAt;
        if (lastComma >= 0 && lastDot >= 0) decimalAt = Math.Max(lastComma, lastDot);
        else if (lastComma >= 0) decimalAt = GroupingOnly(s, lastComma) ? -1 : lastComma;
        else if (lastDot >= 0) decimalAt = GroupingOnly(s, lastDot) ? -1 : lastDot;
        else decimalAt = -1;

        var cleaned = decimalAt < 0
            ? new string(s.Where(char.IsDigit).ToArray())
            : new string(s[..decimalAt].Where(char.IsDigit).ToArray())
              + "." + new string(s[(decimalAt + 1)..].Where(char.IsDigit).ToArray());

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    /// True when the separator is grouping rather than a decimal point: exactly three digits
    /// after it and something before it.
    private static bool GroupingOnly(string s, int at) =>
        s.Length - at - 1 == 3 && at > 0;
}
