using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using PoliPage.AspNetCore.Results;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Results;

public class DocumentRedirectResultTests
{
    [Fact]
    public async Task Writes_302_with_location_header()
    {
        var (httpContext, body) = ResultTestContext.Create();

        await PoliPageResults.DocumentRedirect("https://example.com/doc.pdf").ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        httpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/doc.pdf");
        body.ToArray().Should().BeEmpty();
    }

    [Fact]
    public void Populates_openapi_metadata_for_302()
    {
        var endpointBuilder = new TestEndpointBuilder();
        var method = typeof(DocumentRedirectResult).GetMethod(
            nameof(DocumentRedirectResult.ExecuteAsync),
            BindingFlags.Public | BindingFlags.Instance)!;

        DocumentRedirectResult.PopulateMetadata(method, endpointBuilder);

        endpointBuilder.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Should().ContainSingle(m => m.StatusCode == 302);
    }

    [Fact]
    public void DocumentRedirect_throws_on_null_url()
    {
        Action act = () => PoliPageResults.DocumentRedirect(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("presignedUrl");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DocumentRedirect_throws_on_blank_url(string url)
    {
        Action act = () => PoliPageResults.DocumentRedirect(url);
        act.Should().Throw<ArgumentException>().WithParameterName("presignedUrl");
    }
}
