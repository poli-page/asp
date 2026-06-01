using System.Globalization;
using System.Text;

namespace PoliPage.AspNetCore.Internal;

// RFC 5987 / RFC 6266 Content-Disposition encoder. Mirrors the algorithm in
// /Users/mickael/Projects/nextjs/src/headers.ts so all Poli Page SDKs emit the
// same header for the same filename — important for cross-language consistency
// when a customer hits the same template from different stacks.
internal static class ContentDispositionHeader
{
    public static string Build(string filename, bool inline)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);

        var disposition = inline ? "inline" : "attachment";

        if (IsAsciiSafe(filename))
            return $"{disposition}; filename=\"{EscapeQuotes(filename)}\"";

        var asciiFallback = ReplaceNonAscii(filename);
        var encoded = Rfc5987Encode(filename);
        return $"{disposition}; filename=\"{EscapeQuotes(asciiFallback)}\"; filename*=UTF-8''{encoded}";
    }

    private static bool IsAsciiSafe(string s)
    {
        foreach (var c in s)
        {
            if (c is < (char)0x20 or > (char)0x7E)
                return false;
        }
        return true;
    }

    private static string ReplaceNonAscii(string s)
    {
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = src[i] is < (char)0x20 or > (char)0x7E ? '_' : src[i];
        });
    }

    // Equivalent to encodeURIComponent + percent-escaping of ', (, ) — matches headers.ts:
    // encodeURIComponent leaves -_.!~*'() unencoded; RFC 5987 additionally requires '()
    // to be encoded. The combined safe set is alphanumerics plus -_.!~*.
    private static string Rfc5987Encode(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (IsAttrChar(b))
                sb.Append((char)b);
            else
                sb.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
        }
        return sb.ToString();
    }

    private static bool IsAttrChar(byte b)
        => b is (>= (byte)'A' and <= (byte)'Z')
                or (>= (byte)'a' and <= (byte)'z')
                or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'!' or (byte)'~' or (byte)'*';

    private static string EscapeQuotes(string s)
        => s.Replace("\"", "\\\"", StringComparison.Ordinal);
}
