using System.Text;

namespace FinanceApp.Domain.Import;

/// Turns the uploaded bytes into text.
///
/// Polish banks still export windows-1250: open such a file as UTF-8 and "Żabka" arrives as
/// "?abka", which then fails to match anything and quietly poisons every description in the
/// import. Guessing wrong is worse than failing, so the guess is made on evidence — UTF-8 is
/// self-validating, and a byte sequence that decodes cleanly as UTF-8 essentially never is
/// anything else.
public static class StatementEncoding
{
    /// .NET outside Windows ships only a handful of encodings; windows-1250 comes from this
    /// provider. Registered here rather than at startup so the decoder works wherever it is
    /// called from, including tests.
    static StatementEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// What Polish exports fall back to. Latin-1 would also "work" on every byte and silently
    /// mangle ą, ć, ę, ł, ń, ó, ś, ź, ż — the letters that make up half the merchant names.
    private const int PolishCodePage = 1250;

    /// <param name="name">Which encoding was used, for the screen to report.</param>
    public static string Decode(byte[] bytes, out string name)
    {
        if (bytes.Length == 0)
        {
            name = "utf-8";
            return "";
        }

        // A BOM is the file telling us outright; nothing to guess.
        if (HasBom(bytes, 0xEF, 0xBB, 0xBF)) { name = "utf-8"; return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3); }
        if (HasBom(bytes, 0xFF, 0xFE)) { name = "utf-16"; return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); }
        if (HasBom(bytes, 0xFE, 0xFF)) { name = "utf-16be"; return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strict.GetString(bytes);
            name = "utf-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8. In this corner of the world that means a Polish code page.
            name = "windows-1250";
            return Encoding.GetEncoding(PolishCodePage).GetString(bytes);
        }
    }

    private static bool HasBom(byte[] bytes, params byte[] bom) =>
        bytes.Length >= bom.Length && bom.Select((b, i) => bytes[i] == b).All(x => x);
}
