using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore;

/// <summary>
/// DI-resolvable factory returning <see cref="IActionResult"/> instances for MVC controllers
/// wrapping Poli Page render output. Holds a snapshot of <see cref="PoliPageAspNetCoreOptions"/>
/// for parity with the Minimal API path, but defers most response shaping to MVC's built-in
/// result types — <see cref="FileContentResult"/>, <see cref="FileStreamResult"/>,
/// <see cref="ContentResult"/>, <see cref="RedirectResult"/>.
/// </summary>
/// <remarks>
/// RFC 5987 non-ASCII filename encoding is intentionally not handled here; the README and
/// <c>docs/responses.md</c> document the manual header-writing pattern for controllers that
/// need it. The Minimal API helpers in <c>PoliPageResults</c> handle it automatically.
/// </remarks>
public sealed class PoliPageResponseFactory
{
    private readonly PoliPageAspNetCoreOptions _options;

    /// <summary>
    /// Initialises a new <see cref="PoliPageResponseFactory"/>.
    /// </summary>
    /// <param name="options">The ASP.NET Core integration options snapshot.</param>
    public PoliPageResponseFactory(IOptions<PoliPageAspNetCoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Returns a <see cref="FileContentResult"/> wrapping the supplied PDF bytes with
    /// <c>application/pdf</c> as the content type.
    /// </summary>
    /// <param name="pdf">The rendered PDF bytes.</param>
    /// <param name="filename">
    /// Filename for the download dialog. Ignored when <paramref name="inline"/> is
    /// <see langword="true"/>. ASCII filenames only — see remarks on
    /// <see cref="PoliPageResponseFactory"/>.
    /// </param>
    /// <param name="inline">
    /// When <see langword="true"/> clears <see cref="FileResult.FileDownloadName"/> so MVC
    /// omits <c>Content-Disposition</c>; the browser renders the PDF in-place.
    /// </param>
    public FileContentResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return new FileContentResult(pdf, "application/pdf")
        {
            FileDownloadName = inline ? string.Empty : (filename ?? string.Empty),
        };
    }

    /// <summary>
    /// Returns a <see cref="FileStreamResult"/> wrapping the supplied PDF stream with
    /// <c>application/pdf</c> as the content type. MVC takes ownership of the stream and
    /// disposes it after the response is written.
    /// </summary>
    /// <param name="pdfStream">A read-positioned stream over the rendered PDF.</param>
    /// <param name="filename">Filename for the download dialog. Same caveats as <see cref="Pdf"/>.</param>
    /// <param name="inline">When <see langword="true"/> clears the download name for in-place rendering.</param>
    public FileStreamResult PdfStream(Stream pdfStream, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        return new FileStreamResult(pdfStream, "application/pdf")
        {
            FileDownloadName = inline ? string.Empty : (filename ?? string.Empty),
        };
    }

    /// <summary>
    /// Returns a <see cref="ContentResult"/> with <c>text/html; charset=utf-8</c> wrapping
    /// the supplied HTML body. Mirrors <see cref="Results.PoliPageResults.Preview"/>.
    /// </summary>
    /// <param name="html">The pre-rendered HTML body.</param>
    public ContentResult Preview(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }

    /// <summary>
    /// Returns a non-permanent <see cref="RedirectResult"/> (HTTP 302) targeting the
    /// supplied presigned URL. Use for very large documents that should be fetched
    /// directly from the CDN instead of streamed through the application.
    /// </summary>
    /// <param name="presignedUrl">The presigned URL returned by the SDK.</param>
    public RedirectResult DocumentRedirect(string presignedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presignedUrl);
        return new RedirectResult(presignedUrl, permanent: false);
    }

    // Held for parity with the Minimal API path and for future header-customisation work.
    internal PoliPageAspNetCoreOptions Options => _options;
}
