using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore.Tests.Mvc;

public class PoliPageResponseFactoryTests
{
    [Fact]
    public void Pdf_returns_FileContentResult_with_filename()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var result = CreateFactory().Pdf(pdf, "invoice.pdf");

        result.Should().BeOfType<FileContentResult>();
        result.ContentType.Should().Be("application/pdf");
        result.FileContents.Should().BeSameAs(pdf);
        result.FileDownloadName.Should().Be("invoice.pdf");
    }

    [Fact]
    public void Pdf_with_inline_true_clears_FileDownloadName()
    {
        var result = CreateFactory().Pdf("%PDF-x"u8.ToArray(), "invoice.pdf", inline: true);

        // Empty FileDownloadName tells MVC not to add Content-Disposition, so the browser
        // renders the PDF inline. RFC 5987 / non-ASCII filenames belong on the Minimal API
        // path; the MVC factory stays thin and delegates the disposition logic to MVC.
        result.FileDownloadName.Should().BeEmpty();
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

        result.Should().BeOfType<FileStreamResult>();
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

        result.Should().BeOfType<ContentResult>();
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

    private static PoliPageResponseFactory CreateFactory(Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        // Fully qualify: the test namespace has a sibling "Options" folder/namespace
        // (PoliPage.AspNetCore.Tests.Options) so the bare "Options" identifier resolves there.
        return new PoliPageResponseFactory(Microsoft.Extensions.Options.Options.Create(aspnet));
    }
}
