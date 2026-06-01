using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace PoliPage.AspNetCore.Results;

/// <summary>
/// Helpers returning <see cref="IResult"/> instances for Minimal API endpoints. Each result
/// honours the host's <see cref="PoliPageAspNetCoreOptions"/> snapshot at request time
/// (Cache-Control, X-Content-Type-Options) and contributes OpenAPI metadata via
/// <see cref="IEndpointMetadataProvider"/>.
/// </summary>
public static class PoliPageResults
{
    /// <summary>
    /// Writes the supplied PDF bytes to the response with <c>application/pdf</c>, an
    /// RFC 5987-encoded <c>Content-Disposition</c>, and the configured cache + nosniff headers.
    /// </summary>
    /// <param name="pdf">The rendered PDF bytes.</param>
    /// <param name="filename">
    /// Filename to surface in the <c>Content-Disposition</c> header. When <see langword="null"/>
    /// the header is omitted entirely and the browser falls back to its own naming heuristic.
    /// </param>
    /// <param name="inline">
    /// When <see langword="true"/> the disposition is <c>inline</c> so the browser renders the
    /// PDF in place; otherwise <c>attachment</c> triggers a download dialog. Defaults to
    /// <see langword="false"/>.
    /// </param>
    public static IResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return new PdfResult(pdf, filename, inline);
    }
}
