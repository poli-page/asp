namespace PoliPage.AspNetCore.Internal;

internal static class Validators
{
    // Why http/https only: ProblemDetails.Type is an RFC 7807 "URI reference" that points to
    // human-readable documentation for the error class. Bare absolute paths like "/relative"
    // would round-trip as file:// URIs on Unix (Uri.TryCreate is platform-aware), so we
    // explicitly gate to the HTTP schemes the spec assumes.
    public static bool ValidateProblemDetailsTypeUri(PoliPageAspNetCoreOptions options)
        => Uri.TryCreate(options.ProblemDetailsTypeUri, UriKind.Absolute, out var uri)
           && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
