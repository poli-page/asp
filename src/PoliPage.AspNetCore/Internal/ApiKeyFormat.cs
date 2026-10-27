using System.Text.RegularExpressions;

namespace PoliPage.AspNetCore.Internal;

// The SDK contract's key grammar, pp_{env}_{32 lowercase hex}. The API stays the only authority
// on keys: this decides whether startup warns, and how much of the key that warning may show.
internal static partial class ApiKeyFormat
{
    // pp_ then one environment name and its closing underscore: a lowercase letter first, then
    // lowercase letters and digits with single inner hyphens, 20 characters at most (the
    // lookahead). The full-key check and the shown prefix both derive from this one pattern.
    private const string EnvironmentPrefix = "pp_(?=[a-z][a-z0-9-]{0,19}_)[a-z][a-z0-9]*(?:-[a-z0-9]+)*_";

    // The pattern has no nested ambiguity, so matching stays linear; the timeout only satisfies
    // the analyzer's ReDoS rule (MA0009).
    private const int MatchTimeoutMilliseconds = 1000;

    // \z, not $: .NET's $ also matches before a final "\n".
    [GeneratedRegex("^" + EnvironmentPrefix + "[0-9a-f]{32}\\z", RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex FullKey();

    [GeneratedRegex("^" + EnvironmentPrefix, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex Prefix();

    public static bool IsWellFormed(string apiKey) => FullKey().IsMatch(apiKey);

    // The pp_{env}_ part when the environment name follows the grammar; null otherwise, so a
    // malformed key never puts any of its text in a log line.
    public static string? ShownPrefix(string apiKey)
    {
        var match = Prefix().Match(apiKey);
        return match.Success ? match.Value : null;
    }
}
