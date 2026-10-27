using System.Text.Json;

namespace PoliPage.AspNetCore.IntegrationTests;

// Round-trips the example-app against the live API. Tagged Category=Integration so
// CI / dotnet test runs that filter on "Category!=Integration" skip them by default.
// [SandboxFact] also reports each test as skipped unless POLI_PAGE_API_KEY is a well-formed
// sandbox key.
[Trait("Category", "Integration")]
public class RenderAgainstDevelopApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RenderAgainstDevelopApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [SandboxFact]
    public async Task Smoke_endpoint_round_trips_against_develop()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }

    [SandboxFact]
    public async Task Render_pdf_endpoint_round_trips_against_develop()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/render/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }

    [SandboxFact]
    public async Task Documents_create_round_trips_against_develop()
    {
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

    [SandboxFact]
    public async Task Not_found_demo_answers_404_template_not_found_against_develop()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/errors/not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("TEMPLATE_NOT_FOUND");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        problem.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        // The SDK contract types the request id as optional (PoliPageErrorPayload.RequestId is
        // "the server-assigned request identifier, or null"), and the factory omits the field then.
        if (problem.RootElement.TryGetProperty("poliPageRequestId", out var requestId))
            requestId.GetString().Should().NotBeNullOrWhiteSpace();
    }
}
