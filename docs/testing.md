# Testing

> Replace `PoliPageClient` in `WebApplicationFactory<TEntryPoint>` so your endpoint tests never hit the real Poli Page API.

## Why

`AddPoliPageAspNetCore` registers `PoliPageClient` as a singleton wired to `IHttpClientFactory`. Letting that client reach the network during unit tests is slow, flaky, and burns API quota. `WebApplicationFactory<TEntryPoint>` lets you replace the registration with a fake — your endpoint and middleware code under test stays identical to production; only the dependency changes. The SDK's own test suite already covers HTTP transport, retries, error mapping, and idempotency (see [CLAUDE.md §4](../CLAUDE.md) *What NOT to test*), so your tests focus on endpoint behaviour, not on the SDK.

## Make `Program` testable

`WebApplicationFactory<TEntryPoint>` requires `TEntryPoint` to be a public type. With top-level statements in `Program.cs`, add this line at the bottom:

```csharp
public partial class Program { }
```

This is the Microsoft-blessed pattern. Do not replace it with `[InternalsVisibleTo]` — `Program` ends up public anyway and the partial class is one line.

## A fake `PoliPageClient`

The SDK's `PoliPageClient` is a sealed class. Tests build a derived test double **only** if you choose to inherit from `PoliPageClient` (it's `sealed` in current `sdk-csharp` versions; check before assuming). The simpler pattern is to write a wrapper interface in your own app that depends on `PoliPageClient` and fake the wrapper. The pattern below uses a `FakePoliPageClient` that derives from a hypothetical `PoliPageClientBase` exposed by the SDK; adapt to whatever extension point your SDK version actually offers.

For tests of **this package's own internals**, the test double lives at `tests/PoliPage.AspNetCore.Tests/Fixtures/FakePoliPageClient.cs` and records calls without touching the network.

For tests in **consumer applications**, the simplest approach is to invert the dependency: have your endpoints depend on a thin `IInvoiceRenderer` you own, implement `IInvoiceRenderer` against `PoliPageClient`, and stub `IInvoiceRenderer` in tests. Then `PoliPageClient` never needs to be mocked.

```csharp
// src/Invoices/IInvoiceRenderer.cs
public interface IInvoiceRenderer
{
    Task<byte[]> RenderAsync(string invoiceId, CancellationToken cancellationToken);
}

// src/Invoices/PoliPageInvoiceRenderer.cs
public sealed class PoliPageInvoiceRenderer(PoliPageClient client) : IInvoiceRenderer
{
    public Task<byte[]> RenderAsync(string invoiceId, CancellationToken cancellationToken)
        => client.Render.PdfAsync(
            new ProjectModeInput
            {
                Project = "invoices",
                Template = "default",
                Version = "1.0.0",
                Data = new { InvoiceId = invoiceId },
            },
            cancellationToken: cancellationToken);
}
```

## Replacing the dependency in `WebApplicationFactory`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class InvoiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeInvoiceRenderer Renderer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IInvoiceRenderer>();
            services.AddSingleton<IInvoiceRenderer>(Renderer);
        });
    }
}

public sealed class FakeInvoiceRenderer : IInvoiceRenderer
{
    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%fake"u8.ToArray();
    public string? LastInvoiceId { get; private set; }

    public Task<byte[]> RenderAsync(string invoiceId, CancellationToken cancellationToken)
    {
        LastInvoiceId = invoiceId;
        return Task.FromResult(PdfBytes);
    }
}
```

## A first test

```csharp
public class InvoiceEndpointTests : IClassFixture<InvoiceWebApplicationFactory>
{
    private readonly InvoiceWebApplicationFactory _factory;

    public InvoiceEndpointTests(InvoiceWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_invoice_returns_pdf()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/invoices/INV-42.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition?.FileName.Should().Be("\"invoice-INV-42.pdf\"");

        _factory.Renderer.LastInvoiceId.Should().Be("INV-42");
    }
}
```

## Replacing `PoliPageClient` directly

When inverting the dependency isn't an option (e.g., testing endpoints in this package itself, or testing a deeply integrated flow), replace `PoliPageClient` in DI. The SDK exposes the client as a class type rather than an interface, so the fake derives from it:

```csharp
public sealed class FakePoliPageClient : PoliPageClient
{
    public FakePoliPageClient() : base(new PoliPageClientOptions { ApiKey = "pp_test_fake" })
    {
    }

    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%fake"u8.ToArray();

    // Override the methods your tests touch …
}
```

(Verify `PoliPageClient` is not `sealed` and the relevant methods are `virtual` before assuming this works. If the SDK locks the surface down — which is the recommendation in `sdk-csharp/CLAUDE.md` for production usage — invert via your own interface as shown above.)

`ConfigureWebHost`:

```csharp
builder.ConfigureServices(services =>
{
    services.RemoveAll<PoliPageClient>();
    services.AddSingleton<PoliPageClient>(_ => new FakePoliPageClient());
});
```

## Asserting against `PoliPageException` mapping

The auto-registered `IExceptionHandler` (or the fallback middleware) delegates to `IProblemDetailsService` which writes `application/problem+json`. Tests assert the JSON shape via `JsonDocument` or by deserializing into `ProblemDetails`:

```csharp
[Fact]
public async Task Bad_request_maps_to_problem_details()
{
    _factory.Renderer.SetException(new PoliPageBadRequestException("Template missing.", "INVALID_TEMPLATE", "req_123"));

    using var httpClient = _factory.CreateClient();
    var response = await httpClient.GetAsync("/invoices/INV-42.pdf");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    problem.GetProperty("code").GetString().Should().Be("bad_request");
    problem.GetProperty("poliPageRequestId").GetString().Should().Be("req_123");
}
```

### Testing the `IExceptionHandler` path vs the fallback middleware

Two distinct code paths must each be exercised. The primary path (`IExceptionHandler` + `app.UseExceptionHandler()`) is what 99% of hosts will run; the fallback (`app.UsePoliPageExceptionHandler()` middleware) is what hosts without `UseExceptionHandler()` get.

```csharp
public class PrimaryPathTests : IClassFixture<PrimaryPathFactory>
{
    private readonly PrimaryPathFactory _factory;
    public PrimaryPathTests(PrimaryPathFactory factory) => _factory = factory;

    [Fact]
    public async Task Bad_request_maps_to_problem_details()
    {
        _factory.Fake.SetException(new PoliPageBadRequestException("invalid", null, "req_1"));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/invoices/INV-42.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}

// Factory boots a host with the default (primary) wiring.
public sealed class PrimaryPathFactory : WebApplicationFactory<Program>
{
    public FakePoliPageClient Fake { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<PoliPageClient>();
            s.AddSingleton<PoliPageClient>(Fake);
        });
}

// Second factory: opt out of the IExceptionHandler, swap to the middleware fallback.
public sealed class FallbackPathFactory : WebApplicationFactory<Program>
{
    public FakePoliPageClient Fake { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PoliPage:AspNetCore:RegisterExceptionHandler"] = "false",
        }));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<PoliPageClient>();
            s.AddSingleton<PoliPageClient>(Fake);
        });
        // The example app's Program.cs detects the toggle and switches to UsePoliPageExceptionHandler().
    }
}
```

### Asserting on `IProblemDetailsService` cooperation

A common reason to register your own `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callback is to add request-correlation fields (a tenant id, a feature flag context). Verify the package's handler picks the callback up:

```csharp
[Fact]
public async Task Customize_callback_applies_to_PoliPage_problem_details()
{
    await using var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
                ctx.ProblemDetails.Extensions["tenantId"] = "acme");

            s.RemoveAll<PoliPageClient>();
            s.AddSingleton<PoliPageClient>(new FakePoliPageClient
            {
                NextException = new PoliPageBadRequestException("x", null, "req_x"),
            });
        }));

    using var client = factory.CreateClient();
    var response = await client.GetAsync("/invoices/INV-42.pdf");

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    problem.GetProperty("tenantId").GetString().Should().Be("acme");
    problem.GetProperty("code").GetString().Should().Be("bad_request");
}
```

## Integration tests against the real API

Tag with `[Trait("Category", "Integration")]` so `dotnet test --filter "Category!=Integration"` skips them in the default CI run:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task Smoke_endpoint_round_trips()
{
    if (Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY") is null)
        return;     // skipped — no key, no test

    using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/poli-page/smoke");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

    var bytes = await response.Content.ReadAsByteArrayAsync();
    bytes.Should().StartWith("%PDF-"u8.ToArray());
}
```

## Gotchas

- **Use a valid-shaped placeholder API key in `appsettings.Testing.json`.** The SDK validates `ApiKey` shape (`pp_test_*` / `pp_live_*`) at host startup via `ValidateOnStart`. `pp_test_fake` is a fine placeholder; an empty string throws `OptionsValidationException` before any test runs.
- **`WebApplicationFactory<Program>` boots the whole `Program.cs` pipeline.** If your `Program.cs` opens database connections or calls external services at startup, gate those behind `app.Environment.IsDevelopment()` / `IsProduction()` and set the environment to `Testing` in the factory's `UseEnvironment("Testing")`.
- **`services.RemoveAll<T>()` is in `Microsoft.Extensions.DependencyInjection.Extensions`.** The single-line variant `services.Remove(...)` only removes one registration — for a singleton client, that's the same thing, but for multi-registered services (named `HttpClient`s, for example), `RemoveAll` is what you want.
- **The named `HttpClient`s (`"PoliPage"`, `"PoliPage.Download"`) survive the replacement.** They're registered by the SDK's `AddPoliPage` and your fake `PoliPageClient` no longer uses them. Leaving them registered is harmless (they're factories, not eagerly-instantiated clients), but if you care, call `services.RemoveAll<IHttpClientFactory>()` — though that wrecks any other `IHttpClientFactory` consumers in your app, so don't.
- **Do not mock `PoliPage` SDK transport** (`HttpClient`, `DelegatingHandler`, `WireMock.Net`). You are testing your endpoint, not the SDK. If you find yourself spinning up a mock HTTP server, you are duplicating `sdk-csharp`'s test suite — stub at the renderer-interface or client level instead. See [CLAUDE.md §4](../CLAUDE.md).
- **`Program` partial class lives in the example app's namespace, not yours.** When testing the example app, your test project's `using` lands on the right `Program`. When testing your own app, declare `public partial class Program { }` in your own `Program.cs`.

## Related

- [README → Quick start](../README.md#quick-start) — production-shaped endpoints these tests exercise.
- [responses.md](responses.md) — header reference for assertions.
- [streaming.md](streaming.md) — `Response.Body` content assertions for `PdfStream`.
- [minimal-apis.md](minimal-apis.md) — auth / rate limiting / cancellation patterns to cover in tests.
