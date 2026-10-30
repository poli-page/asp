using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.IntegrationTests;

// The example app's /errors/not-found demo against a canned API answer, so it runs without a
// key (no Category=Integration trait). The live round trip is in RenderAgainstDevelopApiTests.
public class ExampleAppNotFoundDemoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExampleAppNotFoundDemoTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Not_found_demo_renders_does_not_exist_and_answers_404_with_the_error_payload()
    {
        using var api = new TemplateNotFoundApi();
        using var apiClient = new HttpClient(api, disposeHandler: false) { BaseAddress = new Uri("https://api.stub.invalid") };
        using var app = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["PoliPage:ApiKey"] = TestKeys.Sandbox }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PoliPageClient>();
                services.AddSingleton(sp => new PoliPageClient(
                    sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value with { HttpClient = apiClient }));
            });
        });
        using var client = app.CreateClient();

        var response = await client.GetAsync("/errors/not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.EnumerateObject().Select(p => p.Name).Should()
            .Equal("code", "message", "status", "requestId");
        payload.RootElement.GetProperty("code").GetString().Should().Be("TEMPLATE_NOT_FOUND");
        payload.RootElement.GetProperty("message").GetString().Should().Be("Template 'does-not-exist' not found.");
        payload.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        payload.RootElement.GetProperty("requestId").GetString().Should().Be("req_stub");

        using var sent = JsonDocument.Parse(api.LastBody!);
        sent.RootElement.GetProperty("project").GetString().Should().Be("getting-started");
        sent.RootElement.GetProperty("template").GetString().Should().Be("does-not-exist");
        sent.RootElement.GetProperty("data").GetRawText().Should().Be("{}");
        sent.RootElement.TryGetProperty("version", out _).Should().BeFalse();
    }

    private sealed class TemplateNotFoundApi : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string body = """
                {"error":"TEMPLATE_NOT_FOUND","detail":"Template 'does-not-exist' not found.","requestId":"req_stub"}
                """;
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
