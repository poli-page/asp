using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore;

/// <summary>
/// DI-resolvable factory returning <see cref="IActionResult"/> instances for MVC controllers
/// wrapping Poli Page render output. The results are MVC's own <see cref="FileContentResult"/>,
/// <see cref="FileStreamResult"/>, <see cref="ContentResult"/> and <see cref="RedirectResult"/>,
/// and write the same headers as <see cref="Results.PoliPageResults"/>: <c>Cache-Control</c> from
/// <see cref="PoliPageAspNetCoreOptions.DefaultCacheControl"/> and
/// <c>X-Content-Type-Options: nosniff</c> when <see cref="PoliPageAspNetCoreOptions.SetNoSniffHeader"/>
/// is on.
/// </summary>
/// <remarks>
/// For a download, the <c>Content-Disposition</c> header is written by MVC's <see cref="FileResult"/>
/// from <see cref="FileResult.FileDownloadName"/> (RFC 6266 dual form, non-ASCII safe). With
/// <c>inline: true</c>, <see cref="FileResult.FileDownloadName"/> stays empty and the result writes
/// <c>Content-Disposition: inline</c> with the same encoding. Control characters are stripped from
/// the filename first.
/// </remarks>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public instance API resolved through DI; making these static would break every consumer that calls them on the injected factory.")]
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
    /// Filename for the <c>Content-Disposition</c> header, or <see langword="null"/> to omit it.
    /// Control characters are stripped — see remarks on <see cref="PoliPageResponseFactory"/>.
    /// </param>
    /// <param name="inline">
    /// When <see langword="true"/> the disposition is <c>inline</c> so the browser renders the
    /// PDF in place; otherwise <c>attachment</c> triggers a download dialog.
    /// </param>
    public FileContentResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var (downloadName, inlineDisposition) = Disposition(filename, inline);
        return new PoliPageFileContentResult(pdf, _options, inlineDisposition)
        {
            FileDownloadName = downloadName,
        };
    }

    /// <summary>
    /// Returns a <see cref="FileStreamResult"/> wrapping the supplied PDF stream with
    /// <c>application/pdf</c> as the content type. MVC takes ownership of the stream and
    /// disposes it after the response is written.
    /// </summary>
    /// <param name="pdfStream">A read-positioned stream over the rendered PDF.</param>
    /// <param name="filename">Filename for the download dialog. Same caveats as <see cref="Pdf"/>.</param>
    /// <param name="inline">When <see langword="true"/> uses <c>inline</c> disposition; otherwise <c>attachment</c>.</param>
    public FileStreamResult PdfStream(Stream pdfStream, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        var (downloadName, inlineDisposition) = Disposition(filename, inline);
        return new PoliPageFileStreamResult(pdfStream, _options, inlineDisposition)
        {
            FileDownloadName = downloadName,
        };
    }

    /// <summary>
    /// Returns a <see cref="ContentResult"/> with <c>text/html; charset=utf-8</c> wrapping
    /// the supplied HTML body, with the configured cache and nosniff headers. Mirrors
    /// <see cref="Results.PoliPageResults.Preview"/>.
    /// </summary>
    /// <param name="html">The pre-rendered HTML body.</param>
    public ContentResult Preview(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new PoliPageContentResult(_options)
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

    // An attachment goes through MVC's FileDownloadName. MVC only writes `attachment`, so an
    // inline disposition is built here (the Minimal API results' builder) and written by the result.
    private static (string DownloadName, string? InlineDisposition) Disposition(string? filename, bool inline)
    {
        var clean = ContentDispositionHeader.StripControlChars(filename ?? string.Empty);
        if (!inline)
            return (clean, null);

        return (string.Empty, clean.Length == 0 ? null : ContentDispositionHeader.Build(clean, inline: true));
    }
}
