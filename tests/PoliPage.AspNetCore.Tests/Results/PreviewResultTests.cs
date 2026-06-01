using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http.Metadata;
using PoliPage.AspNetCore.Results;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Results;

public class HtmlPreviewResultTests
{
    [Fact]
    public async Task Writes_html_with_default_headers()
    {
        var html = "<h1>Welcome</h1>";
        var (httpContext, body) = ResultTestContext.Create();

        await PoliPageResults.Preview(html).ExecuteAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("text/html; charset=utf-8");
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        Encoding.UTF8.GetString(body.ToArray()).Should().Be(html);
    }

    [Fact]
    public async Task Empty_html_does_not_throw()
    {
        var (httpContext, body) = ResultTestContext.Create();

        await PoliPageResults.Preview(string.Empty).ExecuteAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("text/html; charset=utf-8");
        body.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Omits_cache_control_when_default_null()
    {
        var (httpContext, _) = ResultTestContext.Create(opts => opts.DefaultCacheControl = null);

        await PoliPageResults.Preview("<p>x</p>").ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Cache-Control");
    }

    [Fact]
    public void Populates_openapi_metadata_for_200_text_html()
    {
        var endpointBuilder = new TestEndpointBuilder();
        var method = typeof(HtmlPreviewResult).GetMethod(
            nameof(HtmlPreviewResult.ExecuteAsync),
            BindingFlags.Public | BindingFlags.Instance)!;

        HtmlPreviewResult.PopulateMetadata(method, endpointBuilder);

        var metadata = endpointBuilder.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Should().ContainSingle(m => m.StatusCode == 200).Subject;
        metadata.ContentTypes.Should().Contain("text/html; charset=utf-8");
    }

    [Fact]
    public void Preview_throws_on_null_html()
    {
        Action act = () => PoliPageResults.Preview(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("html");
    }
}
