using System.Text.RegularExpressions;

namespace PoliPage.AspNetCore.Internal;

// The SDK contract's key grammar, pp_{env}_{32 lowercase hex}. The API stays the only authority
// on keys: this only decides whether startup warns.
internal static partial class ApiKeyFormat
{
    // pp_ then one environment name and its closing underscore: a lowercase letter first, then
    // lowercase letters and digits with single inner hyphens, 20 characters at most (the
    // lookahead), then the 32-hex secret.
    private const string FullKeyPattern =
        "^pp_(?=[a-z][a-z0-9-]{0,19}_)[a-z][a-z0-9]*(?:-[a-z0-9]+)*_[0-9a-f]{32}\\z";

    // The pattern has no nested ambiguity, so matching stays linear; the timeout only satisfies
    // the analyzer's ReDoS rule (MA0009).
    private const int MatchTimeoutMilliseconds = 1000;

    // \z, not $: .NET's $ also matches before a final "\n".
    [GeneratedRegex(FullKeyPattern, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex FullKey();

    public static bool IsWellFormed(string apiKey) => FullKey().IsMatch(apiKey);
}
