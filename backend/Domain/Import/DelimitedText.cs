namespace FinanceApp.Domain.Import;

/// Splits a statement file into cells without being told what shape it is.
///
/// Banks disagree about the delimiter — Polish exports mostly use a semicolon, because the
/// comma is already the decimal point — and about whether fields are quoted. Asking the user
/// which one their file is would be asking a question they cannot answer by looking.
public static class DelimitedText
{
    /// Ordered by how likely they are to be the real delimiter when several score equally.
    private static readonly char[] Candidates = [';', '\t', ',', '|'];

    /// The delimiter that splits the most lines into the same number of cells. Counting
    /// consistency rather than raw occurrences is what stops a comma inside descriptions
    /// from beating the semicolon that actually separates the columns.
    public static char DetectDelimiter(IReadOnlyList<string> lines)
    {
        var sample = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(20).ToList();
        if (sample.Count == 0) return ';';

        var best = Candidates[0];
        var bestScore = -1;

        foreach (var candidate in Candidates)
        {
            var counts = sample.Select(l => SplitLine(l, candidate).Count).ToList();
            var columns = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First();

            // One column means the delimiter is not there at all — never a winner.
            if (columns.Key < 2) continue;

            // Lines that agree, weighted by how many columns they agree on: a file that
            // splits into 8 identical columns is a better read than one that splits into 2.
            var score = (columns.Count() * 10) + columns.Key;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// One line into cells, honouring double quotes and the doubled-quote escape ("" inside
    /// a quoted field). Descriptions routinely contain the delimiter, so a plain Split loses
    /// data on exactly the rows that carry the merchant name.
    public static List<string> SplitLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
                continue;
            }

            if (c == delimiter && !inQuotes)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(c);
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    /// Splits the file into lines, tolerating all three line endings and dropping the BOM
    /// that Excel puts in front of a UTF-8 export.
    public static List<string> SplitLines(string text) =>
        text.TrimStart('﻿')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
}
