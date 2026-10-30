# Testing

> Drive `PoliPage.AspNetCore` endpoints through `WebApplicationFactory<Program>` (or an equivalent in-process host) without ever hitting the real Poli Page API.

## Why

`AddPoliPageAspNetCore` registers `PoliPageClient` as a singleton wired to `IHttpClientFactory`. Letting that client reach the network during unit tests is slow, flaky, and burns API quota. The SDK's own test suite already covers HTTP transport, retries, error mapping, idempotency, and stream chunking — your tests focus on endpoint behaviour and the integration's response / exception-handler wiring, not on the SDK.

## Make `Program` testable

`WebApplicationFactory<TEntryPoint>` requires `TEntryPoint` to be a public type. With top-level statements in `Program.cs`, add this line at the bottom:

```csharp
public partial class Program { }
```

This is the Microsoft-blessed pattern. Don't replace it with `[InternalsVisibleTo]` — `Program` ends up public anyway and the partial class is one line. This is the same pattern our `example-app/Program.cs` uses.

## Why not subclass `PoliPageClient`?

The SDK's `PoliPageClient` is **`public sealed class`** with an internal-only constructor seam, and `Render` + `Documents` are likewise `sealed` with `internal` constructors. None of the render or document methods are `virtual`. A `FakePoliPageClient : PoliPageClient` test double simply doesn't compile. See [`docs/sdk-surface-audit-2026-06-01.md`](sdk-surface-audit-2026-06-01.md) §0.2.

The recommended pattern below stubs the SDK's HTTP transport instead. The integration's response factory, exception handler, and endpoint wiring all run unchanged — only the bytes coming back from the network change.

## The stub `HttpMessageHandler` pattern

A `DelegatingHandler` that short-circuits the SDK's HTTP pipeline is the minimum viable test double. Inspect the request URI, return a canned response.

```csharp
using System.Net;
using System.Text;
using PoliPage;

public sealed class StubPoliPageHttpHandler : DelegatingHandler
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
                  "projectSlug": "getting-started",
                  "templateSlug": "welcome",
                  "environment": "sandbox",
                  "format": "pdf",
                  "pageCount": 1,
                  "sizeBytes": {{PdfBytes.Length}},
                  "createdAt": "2026-06-08T00:00:00Z",
                  "presignedPdfUrl": "https://stub.invalid/storage/doc_stub.pdf",
                  "expiresAt": "2026-06-08T00:15:00Z"
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
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoliPage;
using Xunit;

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

## Asserting on the error payload

The error handler `AddPoliPageAspNetCore` turns on answers a `PoliPageException` with the SDK's `ToPayload()` as `application/json`: `{ "code", "message", "status", "requestId" }`, with the payload's status (500 when it has none). Throw a typed `PoliPageException` from the stub and assert against the body:

```csharp
[Fact]
public async Task Validation_failure_answers_422_with_the_error_payload()
{
    _factory.Stub.SetException(new PoliPageValidationException(
        PoliPageErrorCode.ValidationError,
        422,
        "template data missing required field 'amount'",
        requestId: "req_123"));

    using var client = _factory.CreateClient();
    var response = await client.GetAsync("/invoices/INV-42.pdf");

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

    var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
    payload.GetProperty("code").GetString().Should().Be(PoliPageErrorCode.ValidationError);
    payload.GetProperty("message").GetString().Should().Be("template data missing required field 'amount'");
    payload.GetProperty("status").GetInt32().Should().Be(422);
    payload.GetProperty("requestId").GetString().Should().Be("req_123");
}
```

See the README "Errors" section for the statuses each SDK exception carries.

### Testing with the handler turned off

When your app handles `PoliPageException` itself, turn the handler off in the factory with the same option your app sets, and assert on your own answer:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PoliPage;

public sealed class HandlerOffFactory : WebApplicationFactory<Program>
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

The flag is read when `AddPoliPageAspNetCore` runs, so it must come from configuration your `Program.cs` binds (`PoliPage:AspNetCore:RegisterExceptionHandler`), not from a later `services.Configure<PoliPageAspNetCoreOptions>(...)`.

## Integration tests against the real API

Tag with `[Trait("Category", "Integration")]` so `dotnet test --filter "Category!=Integration"` skips them in the default CI run, and skip them unless the API key is a well-formed sandbox key: an unset secret shows up as a skipped test (not a flaky failure, and not a green pass that never sent a request), and a `live` (or other counted) key never spends quota or creates real documents from a test run. xUnit 2 has no runtime skip (`Assert.Skip` is xUnit 3), so set `Skip` in a `FactAttribute` subclass:

```csharp
using System.Text.RegularExpressions;
using Xunit;

public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute()
    {
        // \z, not $: .NET's $ also matches before a final "\n".
        if (Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY") is not { } key
            || !Regex.IsMatch(key, @"^pp_sandbox_[0-9a-f]{32}\z"))
            Skip = "POLI_PAGE_API_KEY is not a sandbox key.";
    }
}
```

Then use it in place of `[Fact]`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[Trait("Category", "Integration")]
public class SmokeEndpointTests
{
    [SandboxFact]
    public async Task Smoke_endpoint_round_trips()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }
}
```

`tests/PoliPage.AspNetCore.IntegrationTests/` in this repo demonstrates this exact pattern.

## Gotchas

- **Use a well-formed placeholder API key in `appsettings.Testing.json`.** The SDK validates `ApiKey` non-empty at host startup via `ValidateOnStart`, so an empty string throws `OptionsValidationException` before any test runs. A key that does not match `pp_{env}_{32 lowercase hex}` still starts the host but logs a format warning; `pp_sandbox_0123456789abcdef0123456789abcdef` is a fine placeholder.
- **`WebApplicationFactory<Program>` boots the whole `Program.cs` pipeline.** If your `Program.cs` opens database connections or calls external services at startup, gate those behind `app.Environment.IsDevelopment()` / `IsProduction()` and set the environment to `Testing` in the factory's `UseEnvironment("Testing")`.
- **`services.RemoveAll<T>()` is in `Microsoft.Extensions.DependencyInjection.Extensions`.** The single-line variant `services.Remove(...)` only removes one registration — for a singleton client, that's the same thing, but for multi-registered services (named `HttpClient`s, for example), `RemoveAll` is what you want.
- **The named `HttpClient`s (`"PoliPage"`, `"PoliPage.Download"`) survive the replacement.** They're registered by the SDK's `AddPoliPage` and your custom `PoliPageClient` singleton no longer uses them. Leaving them registered is harmless — they're lazy factories.
- **Don't mock the SDK's transport directly** (`HttpClient`, `DelegatingHandler`, `WireMock.Net` against `BaseUrl`). You're testing your endpoint, not the SDK. The `StubPoliPageHttpHandler` pattern above is a transport stub, but it returns **one canned response per request shape** — if it grows branching logic over status codes, you're duplicating `sdk-csharp`'s test suite.
- **The audit's `PostConfigure<PoliPageClientOptions>` shortcut doesn't work** against the SDK as it ships today — `services.AddPoliPage` constructs the singleton with `opts with { HttpClient = factory.CreateClient(...) }`, overwriting anything `PostConfigure` set. The `RemoveAll<PoliPageClient>()` + re-register pattern above is the verified path.

## Related

- [README → Quick start](../README.md#quick-start) — production-shaped endpoints these tests exercise.
- [README → Errors](../README.md#errors) — full exception → status code → `code` mapping table.
- [responses.md](responses.md) — header reference for assertions.
- [streaming.md](streaming.md) — `Response.Body` content assertions for `PdfStream`.
- [sdk-surface-audit-2026-06-01.md](sdk-surface-audit-2026-06-01.md) — why `PoliPageClient` is sealed and what that means for testing.
