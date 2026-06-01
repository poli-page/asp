using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using PoliPage.AspNetCore.Results;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Results;

public class PdfResultTests
{
    [Fact]
    public async Task Writes_pdf_with_default_headers()
    {
        var pdf = "%PDF-1.7\n%fake"u8.ToArray();
        var (httpContext, body) = CreateContext();

        await PoliPageResults.Pdf(pdf, "invoice.pdf").ExecuteAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("application/pdf");
        httpContext.Response.ContentLength.Should().Be(pdf.Length);
        httpContext.Response.Headers.ContentDisposition.ToString()
            .Should().Be("attachment; filename=\"invoice.pdf\"");
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        body.ToArray().Should().Equal(pdf);
    }

    [Fact]
    public async Task Inline_flag_produces_inline_disposition()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var (httpContext, _) = CreateContext();

        await PoliPageResults.Pdf(pdf, "invoice.pdf", inline: true).ExecuteAsync(httpContext);

        httpContext.Response.Headers.ContentDisposition.ToString()
            .Should().Be("inline; filename=\"invoice.pdf\"");
    }

    [Fact]
    public async Task Omits_disposition_when_filename_null()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var (httpContext, _) = CreateContext();

        await PoliPageResults.Pdf(pdf).ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Content-Disposition");
    }

    [Fact]
    public async Task Omits_cache_control_when_default_null()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var (httpContext, _) = CreateContext(opts => opts.DefaultCacheControl = null);

        await PoliPageResults.Pdf(pdf, "x.pdf").ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Cache-Control");
    }

    [Fact]
    public async Task Omits_nosniff_when_disabled()
    {
        var pdf = "%PDF-x"u8.ToArray();
        var (httpContext, _) = CreateContext(opts => opts.SetNoSniffHeader = false);

        await PoliPageResults.Pdf(pdf, "x.pdf").ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("X-Content-Type-Options");
    }

    [Fact]
    public void Populates_openapi_metadata_for_200_application_pdf()
    {
        var endpointBuilder = new TestEndpointBuilder();
        var method = typeof(PdfResult).GetMethod(
            nameof(PdfResult.ExecuteAsync),
            BindingFlags.Public | BindingFlags.Instance)!;

        PdfResult.PopulateMetadata(method, endpointBuilder);

        var metadata = endpointBuilder.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Should().ContainSingle(m => m.StatusCode == 200).Subject;
        metadata.ContentTypes.Should().Contain("application/pdf");
    }

    [Fact]
    public void Pdf_throws_on_null_byte_array()
    {
        Action act = () => PoliPageResults.Pdf(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pdf");
    }

    private static (DefaultHttpContext context, MemoryStream body) CreateContext(
        Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x", configure);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        return (httpContext, body);
    }
}
