# Testing

> Drive `PoliPage.AspNetCore` endpoints through `WebApplicationFactory<Program>` (or an equivalent in-process host) without ever hitting the real Poli Page API.

## Why

`AddPoliPageAspNetCore` registers `PoliPageClient` as a singleton wired to `IHttpClientFactory`. Letting that client reach the network during unit tests is slow, flaky, and burns API quota. The SDK's own test suite already covers HTTP transport, retries, error mapping, idempotency, and stream chunking (see [CLAUDE.md §4](../CLAUDE.md) *What NOT to test*) — your tests focus on endpoint behaviour and the integration's response / exception-handler wiring, not on the SDK.

## Make `Program` testable

`WebApplicationFactory<TEntryPoint>` requires `TEntryPoint` to be a public type. With top-level statements in `Program.cs`, add this line at the bottom:

```csharp
public partial class Program { }
```

This is the Microsoft-blessed pattern. Don't replace it with `[InternalsVisibleTo]` — `Program` ends up public anyway and the partial class is one line. This is the same pattern our `example-app/Program.cs` uses (CLAUDE.md §10.4).

## Why not subclass `PoliPageClient`?

The SDK's `PoliPageClient` is **`public sealed class`** with an internal-only constructor seam, and `Render` + `Documents` are likewise `sealed` with `internal` constructors. None of the render or document methods are `virtual`. A `FakePoliPageClient : PoliPageClient` test double simply doesn't compile. See [`docs/sdk-surface-audit-2026-06-01.md`](sdk-surface-audit-2026-06-01.md) §0.2.

The recommended pattern below stubs the SDK's HTTP transport instead. The integration's response factory, exception handler, and endpoint wiring all run unchanged — only the bytes coming back from the network change.

## The stub `HttpMessageHandler` pattern

A `DelegatingHandler` that short-circuits the SDK's HTTP pipeline is the minimum viable test double. Inspect the request URI, return a canned response.

```csharp
using System.Net;
using System.Text;
using PoliPage;

internal sealed class StubPoliPageHttpHandler : DelegatingHandler
{
    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%stub"u8.ToArray();
    public PoliPageException? NextException { get; private set; }

    public void SetException(PoliPageException ex) => NextException = ex;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (NextException is { } ex) throw ex;

        var path = request.RequestUri!.AbsolutePath;

        // POST /v1/render → descriptor JSON pointing at a stub presigned URL
        if (path.EndsWith("/v1/render", StringComparison.Ordinal))
        {
            var descriptor = $$"""
                {
                  "documentId": "doc_stub",
                  "organizationId": "org_stub",
                  "environment": "test",
                  "format": "pdf",
                  "pageCount": 1,
                  "sizeBytes": {{PdfBytes.Length}},
                  "presignedPdfUrl": "https://stub.invalid/storage/doc_stub.pdf"
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(descriptor, Encoding.UTF8, "application/json"),
            });
        }

        // GET /storage/... → the canned PDF bytes
        if (path.Contains("/storage/", StringComparison.Ordinal))
        {
            var content = new ByteArrayContent(PdfBytes);
            content.Headers.ContentType = new("application/pdf");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
```

**Discipline reminder.** This stub returns **one canned response per request shape**. It is not a WireMock-style retest of the SDK. Tests that need to exercise retry budgets, timeout behaviour, or 4xx→exception mapping belong on `sdk-csharp`, not here. If you find your stub growing branching logic over status codes, stop — you're testing the SDK.

## Injecting the stub via `WebApplicationFactory`

The SDK's `AddPoliPage` registers `PoliPageClient` as a singleton via a factory delegate that constructs `opts with { HttpClient = factory.CreateClient("PoliPage"), DownloadHttpClient = factory.CreateClient("PoliPage.Download") }`. That `opts with` clause **overwrites** any `HttpClient` set via `PostConfigure<PoliPageClientOptions>` — the options-level seam doesn't propagate. The working hook is to remove the SDK's registration and re-register a `PoliPageClient` that takes the stub-backed `HttpClient` directly through its constructor (which DOES honour `opts.HttpClient`):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PoliPage;

public sealed class PoliPageWebApplicationFactory : WebApplicationFactory<Program>
{
    public StubPoliPageHttpHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // disposeHandler:false so the WebApplication doesn't dispose the singleton
            // stub on shutdown — tests reuse the host across multiple [Fact] methods.
            var stubApi = new HttpClient(Stub, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.stub.invalid"),
            };
            var stubDownload = new HttpClient(Stub, disposeHandler: false);

            services.RemoveAll<PoliPageClient>();
            services.AddSingleton<PoliPageClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
                return new PoliPageClient(opts with
                {
                    HttpClient = stubApi,
                    DownloadHttpClient = stubDownload,
                });
            });
        });
    }
}
```

This package's own internal tests use the same shape — see [`tests/PoliPage.AspNetCore.Tests/Fixtures/PoliPageTestHost.cs`](../tests/PoliPage.AspNetCore.Tests/Fixtures/PoliPageTestHost.cs) and [`StubPoliPageHttpHandler.cs`](../tests/PoliPage.AspNetCore.Tests/Fixtures/StubPoliPageHttpHandler.cs).

## A first test

```csharp
public class InvoiceEndpointTests : IClassFixture<PoliPageWebApplicationFactory>
{
    private readonly PoliPageWebApplicationFactory _factory;

    public InvoiceEndpointTests(PoliPageWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_invoice_returns_pdf()
    {
        _factory.Stub.PdfBytes = "%PDF-1.7\n%fixture"u8.ToArray();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/invoices/INV-42.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("attachment");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(_factory.Stub.PdfBytes);
    }
}
```

## Asserting on `PoliPageException` → `ProblemDetails` mapping

The auto-registered `IExceptionHandler` (or the fallback middleware) delegates to `IProblemDetailsService` which writes `application/problem+json`. Throw a typed `PoliPageException` from the stub, assert against the resulting body:

```csharp
[Fact]
public async Task Validation_failure_maps_to_422_problem_details()
{
    _factory.Stub.SetException(new PoliPageValidationException(
        PoliPageErrorCode.Validation,
        422,
        "template data missing required field 'amount'",
        requestId: "req_123"));

    using var client = _factory.CreateClient();
    var response = await client.GetAsync("/invoices/INV-42.pdf");

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    problem.GetProperty("code").GetString().Should().Be("validation_failed");
    problem.GetProperty("poliPageRequestId").GetString().Should().Be("req_123");
}
```

The actual SDK exception classes the handler maps are: `PoliPageAuthException` (401), `PoliPagePaymentRequiredException` (402), `PoliPageNotFoundException` (404), `PoliPageGoneException` (410), `PoliPageValidationException` (400 or 422 depending on `StatusCode`), `PoliPageRateLimitException` (429), `PoliPageNetworkException` (502), `PoliPageDownloadException` (502), and `PoliPageException` itself (500). See the README "Errors" section for the full table.

### Testing the `IExceptionHandler` path vs the fallback middleware

Two distinct code paths must each be exercised. The primary path (`IExceptionHandler` + `app.UseExceptionHandler()`) is what 99% of hosts run; the fallback (`app.UsePoliPageExceptionHandler()` middleware) is what hosts without `UseExceptionHandler()` get. The integration's option flags toggle between them:

```csharp
// Primary path: use the factory above as-is — the default
// RegisterExceptionHandler == true wires the IExceptionHandler.

// Fallback path: opt out of the IExceptionHandler, opt into the middleware.
public sealed class FallbackPathFactory : WebApplicationFactory<Program>
{
    public StubPoliPageHttpHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PoliPage:AspNetCore:RegisterExceptionHandler"] = "false",
        }));
        builder.ConfigureServices(services =>
        {
            var stub = new HttpClient(Stub, disposeHandler: false) { BaseAddress = new("https://api.stub.invalid") };
            services.RemoveAll<PoliPageClient>();
            services.AddSingleton<PoliPageClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
                return new PoliPageClient(opts with { HttpClient = stub, DownloadHttpClient = stub });
            });
        });
    }
}
```

Your `Program.cs` flips between `app.UseExceptionHandler()` and `app.UsePoliPageExceptionHandler()` based on the flag — see the example app for the pattern. Never enable both ([CLAUDE.md §10.11](../CLAUDE.md)).

### Asserting on `IProblemDetailsService` cooperation

A common reason to register your own `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callback is to add request-correlation fields (a tenant id, a feature flag context). Verify the package's handler picks the callback up:

```csharp
[Fact]
public async Task Customize_callback_applies_to_PoliPage_problem_details()
{
    await using var factory = new PoliPageWebApplicationFactory();
    factory.Stub.SetException(new PoliPageAuthException(
        PoliPageErrorCode.Unauthorized, 401, "bad key", requestId: "req_x"));

    var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["tenantId"] = "acme");
    }));

    using var client = derived.CreateClient();
    var response = await client.GetAsync("/invoices/INV-42.pdf");

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    problem.GetProperty("tenantId").GetString().Should().Be("acme");
    problem.GetProperty("code").GetString().Should().Be("authentication_failed");
}
```

## Integration tests against the real API

Tag with `[Trait("Category", "Integration")]` so `dotnet test --filter "Category!=Integration"` skips them in the default CI run, and silent-skip when the API key is absent so an unset secret produces a green-passing test instead of a flaky failure:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task Smoke_endpoint_round_trips()
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
        return;

    using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/poli-page/smoke");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

    var bytes = await response.Content.ReadAsByteArrayAsync();
    bytes.Should().StartWith("%PDF-"u8.ToArray());
    bytes.Length.Should().BeGreaterThan(1024);
}
```

`tests/PoliPage.AspNetCore.IntegrationTests/` in this repo demonstrates this exact pattern.

## Gotchas

- **Use a valid-shaped placeholder API key in `appsettings.Testing.json`.** The SDK validates `ApiKey` non-empty at host startup via `ValidateOnStart`. `pp_test_fake` is a fine placeholder; an empty string throws `OptionsValidationException` before any test runs.
- **`WebApplicationFactory<Program>` boots the whole `Program.cs` pipeline.** If your `Program.cs` opens database connections or calls external services at startup, gate those behind `app.Environment.IsDevelopment()` / `IsProduction()` and set the environment to `Testing` in the factory's `UseEnvironment("Testing")`.
- **`services.RemoveAll<T>()` is in `Microsoft.Extensions.DependencyInjection.Extensions`.** The single-line variant `services.Remove(...)` only removes one registration — for a singleton client, that's the same thing, but for multi-registered services (named `HttpClient`s, for example), `RemoveAll` is what you want.
- **The named `HttpClient`s (`"PoliPage"`, `"PoliPage.Download"`) survive the replacement.** They're registered by the SDK's `AddPoliPage` and your custom `PoliPageClient` singleton no longer uses them. Leaving them registered is harmless — they're lazy factories.
- **Don't mock the SDK's transport directly** (`HttpClient`, `DelegatingHandler`, `WireMock.Net` against `BaseUrl`). You're testing your endpoint, not the SDK. The `StubPoliPageHttpHandler` pattern above is a transport stub, but it returns **one canned response per request shape** — if it grows branching logic over status codes, you're duplicating `sdk-csharp`'s test suite. See [CLAUDE.md §4](../CLAUDE.md).
- **The audit's `PostConfigure<PoliPageClientOptions>` shortcut doesn't work** against the SDK as it ships today — `services.AddPoliPage` constructs the singleton with `opts with { HttpClient = factory.CreateClient(...) }`, overwriting anything `PostConfigure` set. The `RemoveAll<PoliPageClient>()` + re-register pattern above is the verified path.

## Related

- [README → Quick start](../README.md#quick-start) — production-shaped endpoints these tests exercise.
- [README → Errors](../README.md#errors) — full exception → status code → `code` mapping table.
- [responses.md](responses.md) — header reference for assertions.
- [streaming.md](streaming.md) — `Response.Body` content assertions for `PdfStream`.
- [sdk-surface-audit-2026-06-01.md](sdk-surface-audit-2026-06-01.md) — why `PoliPageClient` is sealed and what that means for testing.
