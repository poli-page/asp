using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace PoliPage.AspNetCore.Tests.Mvc;

public class PoliPageResponseFactoryTests
{
    [Fact]
    public void Pdf_returns_FileContentResult_with_filename()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var result = CreateFactory().Pdf(pdf, "invoice.pdf");

        result.Should().BeAssignableTo<FileContentResult>();
        result.ContentType.Should().Be("application/pdf");
        result.FileContents.Should().BeSameAs(pdf);
        result.FileDownloadName.Should().Be("invoice.pdf");
    }

    [Fact]
    public void Pdf_with_inline_true_clears_FileDownloadName()
    {
        var result = CreateFactory().Pdf("%PDF-x"u8.ToArray(), "invoice.pdf", inline: true);

        // MVC would write `attachment` from FileDownloadName, so it stays empty; the result
        // writes the inline disposition itself (see Pdf_with_inline_true_writes_inline_disposition).
        result.FileDownloadName.Should().BeEmpty();
    }

    [Fact]
    public void Pdf_strips_control_chars_from_FileDownloadName()
    {
        var result = CreateFactory().Pdf("%PDF-x"u8.ToArray(), "résumé\r\n\t\u0085\u007f.pdf");

        // MVC builds the header with ContentDispositionHeaderValue, which escapes `\` and `"`
        // but would keep control characters (as `_` and %0D%0A); they are stripped up front.
        result.FileDownloadName.Should().Be("résumé.pdf");
    }

    [Fact]
    public void PdfStream_strips_control_chars_from_FileDownloadName()
    {
        using var source = new MemoryStream("%PDF-x"u8.ToArray());
        var result = CreateFactory().PdfStream(source, "evil.pdf\r\nSet-Cookie: sid=1");

        result.FileDownloadName.Should().Be("evil.pdfSet-Cookie: sid=1");
    }

    [Fact]
    public void Pdf_throws_on_null_byte_array()
    {
        Action act = () => CreateFactory().Pdf(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pdf");
    }

    [Fact]
    public void PdfStream_returns_FileStreamResult_with_filename()
    {
        using var source = new MemoryStream("%PDF-x"u8.ToArray());
        var result = CreateFactory().PdfStream(source, "invoice.pdf");

        result.Should().BeAssignableTo<FileStreamResult>();
        result.ContentType.Should().Be("application/pdf");
        result.FileStream.Should().BeSameAs(source);
        result.FileDownloadName.Should().Be("invoice.pdf");
    }

    [Fact]
    public void PdfStream_throws_on_null_stream()
    {
        Action act = () => CreateFactory().PdfStream(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pdfStream");
    }

    [Fact]
    public void Preview_returns_html_content_result()
    {
        var result = CreateFactory().Preview("<h1>x</h1>");

        result.Should().BeAssignableTo<ContentResult>();
        result.Content.Should().Be("<h1>x</h1>");
        result.ContentType.Should().Be("text/html; charset=utf-8");
        result.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void Preview_throws_on_null_html()
    {
        Action act = () => CreateFactory().Preview(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("html");
    }

    [Fact]
    public void DocumentRedirect_returns_temporary_redirect()
    {
        var result = CreateFactory().DocumentRedirect("https://example.com/doc.pdf");

        result.Should().BeOfType<RedirectResult>();
        result.Url.Should().Be("https://example.com/doc.pdf");
        result.Permanent.Should().BeFalse();
    }

    [Fact]
    public void DocumentRedirect_throws_on_null_url()
    {
        Action act = () => CreateFactory().DocumentRedirect(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("presignedUrl");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DocumentRedirect_throws_on_blank_url(string url)
    {
        Action act = () => CreateFactory().DocumentRedirect(url);
        act.Should().Throw<ArgumentException>().WithParameterName("presignedUrl");
    }

    // The Minimal API results and the MVC factory read the same PoliPageAspNetCoreOptions, so
    // the headers they write must match (README: "The two surfaces are equivalent").

    [Fact]
    public async Task Pdf_writes_cache_control_and_nosniff_by_default()
    {
        var response = await ExecuteAsync(CreateFactory().Pdf("%PDF-x"u8.ToArray(), "invoice.pdf"));

        response.ContentType.Should().Be("application/pdf");
        response.Headers.ContentDisposition.ToString()
            .Should().Be("attachment; filename=invoice.pdf; filename*=UTF-8''invoice.pdf");
        response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task PdfStream_writes_cache_control_and_nosniff_by_default()
    {
        var response = await ExecuteAsync(
            CreateFactory().PdfStream(new MemoryStream("%PDF-x"u8.ToArray()), "invoice.pdf"));

        response.ContentType.Should().Be("application/pdf");
        response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task Preview_writes_cache_control_and_nosniff_by_default()
    {
        var response = await ExecuteAsync(CreateFactory().Preview("<h1>x</h1>"));

        response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task Pdf_uses_the_configured_DefaultCacheControl()
    {
        var factory = CreateFactory(opts => opts.DefaultCacheControl = "private, max-age=60");

        var response = await ExecuteAsync(factory.Pdf("%PDF-x"u8.ToArray(), "x.pdf"));

        response.Headers.CacheControl.ToString().Should().Be("private, max-age=60");
    }

    [Fact]
    public async Task Pdf_omits_cache_control_when_default_null()
    {
        var factory = CreateFactory(opts => opts.DefaultCacheControl = null);

        var response = await ExecuteAsync(factory.Pdf("%PDF-x"u8.ToArray(), "x.pdf"));

        response.Headers.Should().NotContainKey("Cache-Control");
    }

    [Fact]
    public async Task Pdf_omits_nosniff_when_disabled()
    {
        var factory = CreateFactory(opts => opts.SetNoSniffHeader = false);

        var response = await ExecuteAsync(factory.Pdf("%PDF-x"u8.ToArray(), "x.pdf"));

        response.Headers.Should().NotContainKey("X-Content-Type-Options");
    }

    [Fact]
    public async Task Pdf_with_inline_true_writes_inline_disposition()
    {
        var response = await ExecuteAsync(
            CreateFactory().Pdf("%PDF-x"u8.ToArray(), "résumé.pdf", inline: true));

        response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=r_sum_.pdf; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf");
    }

    [Fact]
    public async Task PdfStream_with_inline_true_writes_inline_disposition()
    {
        var response = await ExecuteAsync(
            CreateFactory().PdfStream(new MemoryStream("%PDF-x"u8.ToArray()), "invoice.pdf", inline: true));

        response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=invoice.pdf; filename*=UTF-8''invoice.pdf");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pdf_without_filename_writes_no_disposition(bool inline)
    {
        var response = await ExecuteAsync(CreateFactory().Pdf("%PDF-x"u8.ToArray(), inline: inline));

        response.Headers.Should().NotContainKey("Content-Disposition");
    }

    private static async Task<HttpResponse> ExecuteAsync(IActionResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteResultAsync(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

        return httpContext.Response;
    }

    private static PoliPageResponseFactory CreateFactory(Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        // Fully qualify: the test namespace has a sibling "Options" folder/namespace
        // (PoliPage.AspNetCore.Tests.Options) so the bare "Options" identifier resolves there.
        return new PoliPageResponseFactory(Microsoft.Extensions.Options.Options.Create(aspnet));
    }
}
