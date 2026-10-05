using System.Text;
using Microsoft.Net.Http.Headers;

namespace PoliPage.AspNetCore.Internal;

// RFC 6266 / RFC 8187 (ex-5987) Content-Disposition builder. The quoting, the `\` and `"`
// quoted-pair escaping, the ASCII fallback and the UTF-8 percent-encoded filename* are all
// delegated to ASP.NET Core's ContentDispositionHeaderValue — the same helper MVC's
// FileResult uses, so the Minimal API results and the MVC factory emit the same header.
// The framework keeps control characters (as `_` in the fallback, percent-encoded in
// filename*), so they are stripped first.
internal static class ContentDispositionHeader
{
    public static string Build(string filename, bool inline)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);

        var clean = StripControlChars(filename);
        if (clean.Length == 0)
            throw new ArgumentException("The filename contains only control characters.", nameof(filename));

        var value = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        value.SetHttpFileName(clean);
        return value.ToString();
    }

    // C0 controls (incl. TAB, CR, LF), DEL and C1 controls — exactly char.IsControl. None of
    // them belong in a filename, and CR/LF would split the header (response splitting).
    public static string StripControlChars(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!char.IsControl(c))
                sb.Append(c);
        }
        return sb.Length == s.Length ? s : sb.ToString();
    }
}
