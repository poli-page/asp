using System.Text.Json;

namespace PoliPage.AspNetCore.IntegrationTests;

// Round-trips the example-app against the live API. Tagged Category=Integration so
// CI / dotnet test runs that filter on "Category!=Integration" skip them by default —
// these tests cost real API quota. Each test also self-skips when POLI_PAGE_API_KEY
// is absent, so an accidental run on an unconfigured environment passes silently
// rather than failing red.
[Trait("Category", "Integration")]
public class RenderAgainstDevelopApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RenderAgainstDevelopApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Smoke_endpoint_round_trips_against_develop()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
            return;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }

    [Fact]
    public async Task Render_pdf_endpoint_round_trips_against_develop()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
            return;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/render/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }

    [Fact]
    public async Task Render_preview_endpoint_round_trips_against_develop()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
            return;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/render/preview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // MediaTypeHeaderValue splits "text/html; charset=utf-8" into MediaType + CharSet.
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        response.Content.Headers.ContentType?.CharSet.Should().Be("utf-8");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotBeNullOrWhiteSpace();
        html.Length.Should().BeGreaterThan(1024);
    }

    [Fact]
    public async Task Documents_create_round_trips_against_develop()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
            return;

        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsync("/documents", content: null);

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var documentId = doc.RootElement.GetProperty("documentId").GetString();
        // The develop API returns bare UUIDs (no "doc_" prefix). Tests run against this
        // shape, not against the prefixed IDs the StubPoliPageHttpHandler happens to use.
        documentId.Should().NotBeNullOrWhiteSpace();

        // Clean up immediately so the develop-environment storage doesn't bloat per test
        // run. A failed delete here is non-fatal — the document expires on its retention
        // window anyway — but assert success when it's available so we notice regressions.
        var deleteResponse = await client.DeleteAsync($"/documents/{documentId}");
        deleteResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
    }
}
