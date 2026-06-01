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

    /// <summary>
    /// Streams the supplied PDF <see cref="Stream"/> to the response. The result takes
    /// ownership of <paramref name="pdfStream"/> and disposes it after the body is fully
    /// written, so callers must not reuse the stream after passing it in.
    /// </summary>
    /// <param name="pdfStream">A read-positioned stream over the rendered PDF.</param>
    /// <param name="filename">Filename for the <c>Content-Disposition</c> header, or <see langword="null"/> to omit it.</param>
    /// <param name="inline">When <see langword="true"/> uses <c>inline</c> disposition; otherwise <c>attachment</c>.</param>
    public static IResult PdfStream(Stream pdfStream, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        return new PdfStreamResult(pdfStream, filename, inline);
    }

    /// <summary>
    /// Writes the supplied HTML to the response as <c>text/html; charset=utf-8</c>. Empty strings are
    /// allowed — a template may legitimately render to nothing for some inputs, and surfacing that
    /// as a 200 with an empty body is more useful than throwing.
    /// </summary>
    /// <param name="html">The pre-rendered HTML body.</param>
    public static IResult Preview(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new HtmlPreviewResult(html);
    }

    /// <summary>
    /// Returns a <c>302 Found</c> with the supplied presigned URL in the <c>Location</c> header.
    /// Use this when the host wants the browser to fetch the PDF directly from the CDN instead
    /// of streaming it through the application — useful for very large documents.
    /// </summary>
    /// <param name="presignedUrl">The presigned URL returned by the SDK's Render or Documents APIs.</param>
    public static IResult DocumentRedirect(string presignedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presignedUrl);
        return new DocumentRedirectResult(presignedUrl);
    }
}
