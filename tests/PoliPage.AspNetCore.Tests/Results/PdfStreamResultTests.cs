using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using PoliPage.AspNetCore.Results;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Results;

public class PdfStreamResultTests
{
    [Fact]
    public async Task Writes_stream_with_default_headers()
    {
        var pdf = "%PDF-stream-bytes"u8.ToArray();
        var source = new MemoryStream(pdf);
        var (httpContext, body) = ResultTestContext.Create();

        await PoliPageResults.PdfStream(source, "invoice.pdf").ExecuteAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("application/pdf");
        httpContext.Response.Headers.ContentDisposition.ToString()
            .Should().Be("attachment; filename=\"invoice.pdf\"");
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        body.ToArray().Should().Equal(pdf);
    }

    [Fact]
    public async Task Inline_flag_produces_inline_disposition()
    {
        var source = new MemoryStream("%PDF-x"u8.ToArray());
        var (httpContext, _) = ResultTestContext.Create();

        await PoliPageResults.PdfStream(source, "invoice.pdf", inline: true).ExecuteAsync(httpContext);

        httpContext.Response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=\"invoice.pdf\"");
    }

    [Fact]
    public async Task Omits_disposition_when_filename_null()
    {
        var source = new MemoryStream("%PDF-x"u8.ToArray());
        var (httpContext, _) = ResultTestContext.Create();

        await PoliPageResults.PdfStream(source).ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Content-Disposition");
    }

    [Fact]
    public async Task Disposes_source_stream_after_writing()
    {
        var source = new MemoryStream("%PDF-x"u8.ToArray());
        var (httpContext, _) = ResultTestContext.Create();

        await PoliPageResults.PdfStream(source, "x.pdf").ExecuteAsync(httpContext);

        // The "await using" inside PdfStreamResult.ExecuteAsync takes ownership and disposes
        // the source stream so callers don't leak it (matters more for HTTP-backed streams
        // from SDK Render.PdfStreamAsync than for in-memory ones, but the contract is uniform).
        source.CanRead.Should().BeFalse();
    }

    [Fact]
    public void Populates_openapi_metadata_for_200_application_pdf()
    {
        var endpointBuilder = new TestEndpointBuilder();
        var method = typeof(PdfStreamResult).GetMethod(
            nameof(PdfStreamResult.ExecuteAsync),
            BindingFlags.Public | BindingFlags.Instance)!;

        PdfStreamResult.PopulateMetadata(method, endpointBuilder);

        var metadata = endpointBuilder.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Should().ContainSingle(m => m.StatusCode == 200).Subject;
        metadata.ContentTypes.Should().Contain("application/pdf");
    }

    [Fact]
    public void PdfStream_throws_on_null_stream()
    {
        Action act = () => PoliPageResults.PdfStream(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pdfStream");
    }
}
