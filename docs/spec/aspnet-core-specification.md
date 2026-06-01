# `PoliPage.AspNetCore` — Specification

> Source of truth for what we build, in what shape, and explicitly what we don't. Mirrors `nestjs/docs/spec/nestjs-implementation.md`, `laravel/docs/spec/laravel-package-specification.md`, and `symfony-bundle/docs/spec/bundle-specification.md` so reviewers can cross-reference. Read `/Users/mickael/Projects/INTEGRATIONS_PLAN.md` first; this is the per-repo expansion of the ASP.NET Core slot.

**Roadmap slot**: P5.1 (immediately after `sdk-csharp` v0.1).
**Target**: ship v0.1.0 as a working NuGet package, not a recipe.
**Stance**: thin idiomatic ASP.NET Core wrapper over `PoliPage` (the `sdk-csharp` package, NuGet id `PoliPage`). Anything the SDK already does — HTTP, retries, error classification, idempotency, stream handling, `IOptions` validation — does NOT get reimplemented here.

---

## 1. What this package is, and what it isn't

### Is

- An ASP.NET Core-native wrapper around `PoliPage` (`sdk-csharp`) that gives ASP.NET users:
  - A `services.AddPoliPageAspNetCore(...)` extension that composes the SDK's `AddPoliPage(...)` with the response helpers and middleware in this package. Three overloads: `Action<PoliPageClientOptions>`, `IConfiguration` section binding, and `IConfiguration` + post-configure callback.
  - A `PoliPageResults` static class returning `IResult` for Minimal APIs — `PoliPageResults.Pdf(...)`, `PoliPageResults.PdfStream(...)`, `PoliPageResults.Preview(...)`, `PoliPageResults.DocumentRedirect(...)`.
  - A `PoliPageResponseFactory` returning `IActionResult` for MVC controllers — `Pdf(...)`, `PdfStream(...)`, `Preview(...)`, `DocumentRedirect(...)`. Same headers as the Minimal API helpers.
  - A `UsePoliPageExceptionHandler()` middleware that maps `PoliPageException` to RFC 7807 `ProblemDetails` JSON with the right status (4xx/5xx pass-through, network/timeout → 502).
  - A `MapPoliPageSmokeTest("/poli-page/smoke")` endpoint-routing helper for end-to-end smoke testing (renders `getting-started/welcome` and returns the PDF).
  - An `IHealthChecksBuilder.AddPoliPage(...)` health-check registration for `Microsoft.Extensions.Diagnostics.HealthChecks` users.
  - An example ASP.NET Core 8 / 10 app at `example-app/` with the interactive demo UI served at `GET /`.

### Isn't

- **A reimplementation of SDK behaviour.** Tests do not cover transport, retries, 4xx mapping, idempotency, or stream chunking — `PoliPage` (the SDK)'s test suite owns those.
- **A Web Forms or MVC 5 (System.Web) integration.** ASP.NET Core only. .NET Framework users keep using the bare SDK.
- **An OpenAPI / Swashbuckle integration.** Defer to v0.2 — `Microsoft.AspNetCore.OpenApi` and Swashbuckle work on user endpoints without help from us.
- **Multi-client / keyed services.** Single client only in v0.1. Multi-tenant scenarios go through user-owned wrappers around `PoliPageClient`. See §17 "Out of scope".
- **A Polly retry handler.** The SDK already implements retries (`MaxRetries`, `RetryDelay`, exponential backoff with jitter, `Retry-After` honouring, 4xx-never-retry). Stacking Polly on top would double-retry and break the SDK's contract. Users wiring Polly to the named `"PoliPage"` `HttpClient` is their choice — we document the consequence in §7.4.
- **An `IExceptionFilter` (MVC) variant of the middleware.** The terminal `UsePoliPageExceptionHandler()` middleware works for both MVC and Minimal APIs because it runs in the request pipeline before MVC's filter pipeline. Users on classic MVC who want filter-level behaviour wire `PoliPageException` into their existing `ExceptionFilterAttribute` themselves — we document the pattern in §10.4.

---

## 2. Required reading (concrete file paths)

Before touching code, read in this order:

1. `/Users/mickael/Projects/INTEGRATIONS_PLAN.md` — cross-repo plan, scope verdicts, cross-cutting DX patterns. The §"CRITICAL: fix the per-repo `CLAUDE.md` before building" and §"Cross-cutting DX patterns" sections drive the boundaries of what this package owns.
2. `/Users/mickael/Projects/sdk-csharp/CLAUDE.md` and `/Users/mickael/Projects/sdk-csharp/README.md` — the SDK this package wraps. The .NET-specific README notes (XML doc comments, nullable reference types, `services.AddPoliPage`) drive the conventions inherited here.
3. `/Users/mickael/Projects/sdk-csharp/src/PoliPage/PoliPageClientOptions.cs` — the actual options surface. `PoliPageAspNetCoreOptions` extends this conceptually; do NOT invent fields the SDK doesn't expose.
4. `/Users/mickael/Projects/sdk-csharp/src/PoliPage/DependencyInjection/ServiceCollectionExtensions.cs` — the SDK's `AddPoliPage(...)` extension. `AddPoliPageAspNetCore(...)` delegates to it; do NOT duplicate validation, `IHttpClientFactory` wiring, or singleton registration.
5. `/Users/mickael/Projects/sdk-csharp/src/PoliPage/Exceptions/` — exception hierarchy. `PoliPageException` is the root; `PoliPageAuthenticationException`, `PoliPageRateLimitException`, `PoliPageBadRequestException`, `PoliPageNotFoundException`, `PoliPageConnectionException` are the cases the middleware maps.
6. `/Users/mickael/Projects/symfony-bundle/docs/spec/bundle-specification.md` — sister bundle for the response-factory pattern. ASP.NET Core's `IActionResult` / `IResult` split maps cleanly to Symfony's `Response` / `StreamedResponse` / `RedirectResponse` split; copy §8 design decisions where applicable.
7. `/Users/mickael/Projects/nestjs/docs/spec/nestjs-implementation.md` — sister integration for the response-helper class hierarchy (`PoliPagePdfFile extends StreamableFile`) and exception-mapping policy (§10).
8. `/Users/mickael/Projects/laravel/docs/spec/laravel-package-specification.md` — sister bundle for the smoke-test command pattern (the ASP.NET Core equivalent is an endpoint, not a CLI; §9 here documents the delta).
9. ASP.NET Core docs to skim:
   - <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis> — `IResult` surface.
   - <https://learn.microsoft.com/aspnet/core/web-api/handle-errors> — `ProblemDetails`, exception middleware.
   - <https://learn.microsoft.com/aspnet/core/host/web-host?#configuration-host> — `IOptions` + `IConfiguration` binding.
   - <https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory> — the SDK already uses this; we do not double-register.
10. Reference packages to compare patterns against: `Sentry.AspNetCore`, `Serilog.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Hangfire.AspNetCore`, `OpenTelemetry.Extensions.Hosting`. Their `Add*` + `Use*` shape is the bar.

---

## 3. Version targets

| Field | Value |
|---|---|
| Package name | `PoliPage.AspNetCore` |
| Initial version | `0.1.0` |
| Target frameworks | `net8.0` (LTS) and `net10.0` (LTS). No `netstandard2.0` — ASP.NET Core itself dropped that surface. |
| ASP.NET Core | `8.0.*` and `10.0.*` runtimes |
| C# language version | `latest` (12 on net8.0, 14 on net10.0) |
| Depends on | `PoliPage` (NuGet, `>= 1.0.0 < 2.0.0`); `Microsoft.AspNetCore.App` framework reference |
| Optional dep | `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` — referenced only by the optional health-check registration; users without the package never load the assembly. |
| Build-time deps | `Microsoft.SourceLink.GitHub` (PrivateAssets=all) for symbol→source navigation; `Microsoft.Extensions.Configuration.Binder` (with the source-generator opt-in `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`) so `IConfiguration.Bind(opts)` works under PublishAot. |
| Repo | `poli-page/asp` (GitHub) — short slug "asp" used across the workspace |
| Runtime support | Linux, macOS, Windows (whatever ASP.NET Core 8 / 10 supports) |
| AOT stance | The runtime code path (exception handler, result types, header building) is AOT-clean — `System.Text.Json` source-gen for ProblemDetails, no `Reflection.Emit`, no `Type.GetType(string)`. The `IConfiguration` binding overloads of `AddPoliPageAspNetCore` rely on the configuration-binder source generator (`EnableConfigurationBindingGenerator`) to stay AOT-safe. `<IsAotCompatible>true</IsAotCompatible>` is **gated to `net10.0`** in `Directory.Build.props` because the `net8.0` ASP.NET-flavored AOT analyzer still flags known false-positives; trimmer + AOT analyzers gate every change on net10. Net8 AOT is best-effort until net8 EOL (Nov 2026). |

The SDK is targeted via Central Package Management (`Directory.Packages.props`) — same convention as `sdk-csharp`. The `Microsoft.AspNetCore.App` framework reference (added via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in the `.csproj`) brings the ASP.NET runtime in without inflating the dependency graph.

CI matrix:
- `net8.0` on Ubuntu and Windows
- `net10.0` on Ubuntu, Windows, macOS
- Both TFMs tested against ASP.NET Core 8 and 10 (one runtime per TFM — runtime version follows the TFM).

Five cells. Matches `Sentry.AspNetCore`'s matrix shape.

---

## 4. Architecture style

Pragmatic, idiomatic ASP.NET Core. Five primitives:

1. **`PoliPageAspNetCoreOptions`** — extends the SDK's `PoliPageClientOptions` conceptually (held alongside, not subclassed — see §6.1 for why). Adds ASP.NET-specific knobs (`ProblemDetailsTypeUri`, `IncludeRequestIdInProblemDetails`).
2. **`ServiceCollectionExtensions.AddPoliPageAspNetCore(...)`** — composition extension on `IServiceCollection`. Three overloads.
3. **`PoliPageResults`** — static class returning `IResult` (Minimal APIs). `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`.
4. **`PoliPageResponseFactory`** — class returning `IActionResult` (MVC). Same four methods. Resolvable from DI; can also be `new`'d in user code.
5. **`PoliPageExceptionHandler`** — `IExceptionHandler` implementation (the .NET 8+ idiom; registered via `services.AddExceptionHandler<PoliPageExceptionHandler>()`) that catches `PoliPageException`, builds a `ProblemDetails`, and delegates the actual write to `IProblemDetailsService` — so any `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callbacks the user registered also run on our responses. The handler also sets `Activity.Current?.SetStatus(ActivityStatusCode.Error, ...)` so distributed traces show the failed span. Auto-registered by `AddPoliPageAspNetCore` (toggleable via `PoliPageAspNetCoreOptions.RegisterExceptionHandler`, default `true`).
6. **`PoliPageExceptionHandlerMiddleware`** — fallback terminal middleware (registered via `app.UsePoliPageExceptionHandler()`) for hosts that don't call `app.UseExceptionHandler()`. Same mapping; same `IProblemDetailsService` integration. Opt-in only.

Plus two optional pieces:

7. **`MapPoliPageSmokeTest(...)`** — endpoint-routing helper. Renders `getting-started/welcome` and returns the PDF. Replaces the symfony/laravel CLI smoke command — ASP.NET has no app-attached CLI; an endpoint is the idiomatic surface. Emits a startup-log warning when registered without `.RequireAuthorization(...)` or an explicit `.AllowAnonymous()` opt-in (see §9).
8. **`HealthChecksBuilderExtensions.AddPoliPage(...)`** — `IHealthChecksBuilder` extension for `Microsoft.Extensions.Diagnostics.HealthChecks`. Hits the SDK's `Documents.PingAsync()` (or a cheap GET); reports `Healthy` / `Degraded` / `Unhealthy` based on response code.

That's the public surface. No `IExceptionFilter`, no `ActionFilterAttribute`, no `IStartupFilter`, no custom `IConfigureOptions<MvcOptions>` — all of which would surprise users and conflict with their existing pipeline.

All public `IResult` implementations also implement `IEndpointMetadataProvider` (since .NET 9 they're expected to) so `Microsoft.AspNetCore.OpenApi` / Swashbuckle automatically discover the response content-type and 2xx status without users having to call `.Produces("application/pdf", 200)` on every endpoint.

All `ILogger` calls inside the package go through `[LoggerMessage]` source-generated extension methods (`LogMessages.cs`) — required by analyzer `CA1848` and required for AOT to elide reflection-based logger formatting.

The package targets **.NET 8** and **.NET 10**, nullable reference types are enabled, all public symbols carry XML doc comments (consumed by IntelliSense and the auto-generated reference at <https://poli-page.github.io/asp>).

---

## 5. File layout

```
asp/
├── src/
│   └── PoliPage.AspNetCore/
│       ├── PoliPage.AspNetCore.csproj
│       ├── DependencyInjection/
│       │   ├── ServiceCollectionExtensions.cs         # AddPoliPageAspNetCore overloads
│       │   └── HealthChecksBuilderExtensions.cs       # AddPoliPage on IHealthChecksBuilder
│       ├── Endpoints/
│       │   └── EndpointRouteBuilderExtensions.cs      # MapPoliPageSmokeTest
│       ├── ExceptionHandling/
│       │   ├── PoliPageExceptionHandler.cs            # IExceptionHandler — primary
│       │   ├── PoliPageExceptionHandlerMiddleware.cs  # fallback when UseExceptionHandler() not in pipeline
│       │   ├── ApplicationBuilderExtensions.cs        # UsePoliPageExceptionHandler (fallback path)
│       │   ├── PoliPageProblemDetailsFactory.cs       # internal: exception → ProblemDetails
│       │   └── ProblemDetailsJsonContext.cs           # System.Text.Json source-gen context
│       ├── Mvc/
│       │   ├── PoliPageResponseFactory.cs             # MVC IActionResult helpers
│       │   └── ContentDispositionHeader.cs            # internal: RFC 5987 encoder
│       ├── Results/
│       │   ├── PoliPageResults.cs                     # Minimal API IResult helpers
│       │   ├── PdfResult.cs                           # internal: IResult + IEndpointMetadataProvider
│       │   ├── PdfStreamResult.cs                     # internal: IResult + IEndpointMetadataProvider
│       │   ├── PreviewResult.cs                       # internal: IResult + IEndpointMetadataProvider
│       │   └── DocumentRedirectResult.cs              # internal: IResult + IEndpointMetadataProvider
│       ├── HealthChecks/
│       │   └── PoliPageHealthCheck.cs                 # IHealthCheck impl
│       ├── PoliPageAspNetCoreOptions.cs               # ASP.NET-specific options bag
│       └── Internal/
│           ├── Validators.cs                          # option validation (api key prefix, URL shape)
│           └── LogMessages.cs                         # [LoggerMessage] source-gen extension methods
├── tests/
│   ├── PoliPage.AspNetCore.Tests/                     # unit / WebApplicationFactory suite
│   │   ├── PoliPage.AspNetCore.Tests.csproj
│   │   ├── DependencyInjection/
│   │   │   ├── AddPoliPageAspNetCoreTests.cs
│   │   │   ├── ConfigurationBindingTests.cs
│   │   │   └── HealthChecksRegistrationTests.cs
│   │   ├── Endpoints/
│   │   │   └── MapPoliPageSmokeTestTests.cs
│   │   ├── ExceptionHandling/
│   │   │   ├── PoliPageExceptionHandlerTests.cs                 # IExceptionHandler primary path
│   │   │   ├── PoliPageExceptionHandlerMiddlewareTests.cs       # fallback path
│   │   │   └── ProblemDetailsMappingTests.cs
│   │   ├── Mvc/
│   │   │   ├── PoliPageResponseFactoryTests.cs
│   │   │   └── ContentDispositionHeaderTests.cs
│   │   ├── Results/
│   │   │   ├── PdfResultTests.cs
│   │   │   ├── PdfStreamResultTests.cs
│   │   │   ├── PreviewResultTests.cs
│   │   │   └── DocumentRedirectResultTests.cs
│   │   ├── HealthChecks/
│   │   │   └── PoliPageHealthCheckTests.cs
│   │   ├── Fixtures/
│   │   │   ├── FakePoliPageClient.cs                  # test double for PoliPageClient
│   │   │   └── PoliPageWebApplicationFactory.cs       # WebApplicationFactory<Program>
│   │   └── GlobalUsings.cs
│   └── PoliPage.AspNetCore.IntegrationTests/          # gated on POLI_PAGE_API_KEY
│       ├── PoliPage.AspNetCore.IntegrationTests.csproj
│       └── RenderAgainstDevelopApiTests.cs
├── example-app/                                       # see §13
├── docs/
│   ├── spec/aspnet-core-specification.md              # this file
│   ├── plan/2026-06-01-implementation.md
│   ├── responses.md
│   ├── minimal-apis.md
│   ├── streaming.md
│   └── testing.md
├── samples/                                           # tiny self-contained snippet projects (optional)
├── PoliPage.AspNetCore.sln
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── .editorconfig
├── .gitignore
├── README.md
├── CHANGELOG.md
├── CONTRIBUTING.md
├── CLAUDE.md                                          # integration-flavored
├── LICENSE                                            # MIT
└── MIGRATION.md
```

**File count**: 13 source files, 14 test files, 1 example app, 1 solution, 4 root MSBuild files. Anything beyond requires editing §15 first.

The `samples/` folder is optional and exists to let CI smoke-test the `dotnet add package PoliPage.AspNetCore` installation flow against a minimal-API-and-MVC pair without disturbing the full `example-app/`. Pattern lifted from `sdk-csharp/samples/`.

---

## 6. Configuration

### 6.1 Surface

`src/PoliPage.AspNetCore/PoliPageAspNetCoreOptions.cs`:

```csharp
namespace PoliPage.AspNetCore;

/// <summary>
/// ASP.NET Core-specific options for <see cref="PoliPage.PoliPageClient"/>. These augment
/// the SDK's <see cref="PoliPageClientOptions"/> with knobs that only matter inside an
/// HTTP request pipeline.
/// </summary>
public sealed class PoliPageAspNetCoreOptions
{
    /// <summary>
    /// The <c>type</c> URI returned in <see cref="ProblemDetails"/> responses when
    /// <see cref="UsePoliPageExceptionHandler"/> maps a <see cref="PoliPageException"/>.
    /// Defaults to <c>https://poli.page/errors</c>; the specific exception's
    /// <c>ErrorCode</c> is appended as a fragment.
    /// </summary>
    public string ProblemDetailsTypeUri { get; set; } = "https://poli.page/errors";

    /// <summary>
    /// Whether to include the SDK-provided request id (<see cref="PoliPageException.RequestId"/>)
    /// in the ProblemDetails <c>extensions</c> dictionary under the key
    /// <c>poliPageRequestId</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeRequestIdInProblemDetails { get; set; } = true;

    /// <summary>
    /// Default <c>Cache-Control</c> header value applied by <see cref="PoliPageResults"/>
    /// and <see cref="PoliPageResponseFactory"/> response helpers. Defaults to
    /// <c>no-store, private</c>. Set to <see langword="null"/> to omit the header.
    /// </summary>
    public string? DefaultCacheControl { get; set; } = "no-store, private";

    /// <summary>
    /// Whether response helpers set <c>X-Content-Type-Options: nosniff</c> on every
    /// response. Defaults to <see langword="true"/>.
    /// </summary>
    public bool SetNoSniffHeader { get; set; } = true;

    /// <summary>
    /// Whether <see cref="ServiceCollectionExtensions.AddPoliPageAspNetCore"/> registers
    /// <c>PoliPageExceptionHandler</c> as an <see cref="IExceptionHandler"/> via
    /// <c>services.AddExceptionHandler&lt;PoliPageExceptionHandler&gt;()</c>. Defaults
    /// to <see langword="true"/>. Set to <see langword="false"/> when the host wants to
    /// install its own <c>PoliPageException</c> handler (e.g. an MVC
    /// <see cref="IExceptionFilter"/>) and does not want ours competing for the same
    /// exception.
    /// </summary>
    public bool RegisterExceptionHandler { get; set; } = true;

    /// <summary>
    /// Whether <see cref="ServiceCollectionExtensions.AddPoliPageAspNetCore"/> also calls
    /// <c>services.AddProblemDetails()</c>. <c>AddProblemDetails</c> uses <c>TryAddXxx</c>
    /// internally so calling it twice is harmless; we still expose the toggle for hosts
    /// that intentionally avoid the built-in problem-details service. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AddProblemDetailsService { get; set; } = true;
}
```

The SDK's `PoliPageClientOptions` (api key, timeout, retries, hooks, …) is configured **separately and first** — via the SDK's existing `AddPoliPage(...)`. `PoliPageAspNetCoreOptions` is a second `IOptions<T>` registration that holds only ASP.NET concerns. This split keeps the SDK's options surface untouched by integration code; a future `Hangfire.PoliPage` or `Quartz.PoliPage` package can add its own options bag without conflict.

### 6.2 Default-value discipline

For every SDK option, defaults live in `PoliPageClientOptions` (see `/Users/mickael/Projects/sdk-csharp/src/PoliPage/PoliPageClientOptions.cs`). This package never duplicates a default literal. If the SDK changes `MaxRetries` from `2` to `3` in v1.1, that change propagates to integration users automatically.

ASP.NET-specific defaults live here:
- `ProblemDetailsTypeUri = "https://poli.page/errors"`
- `IncludeRequestIdInProblemDetails = true`
- `DefaultCacheControl = "no-store, private"`
- `SetNoSniffHeader = true`
- `RegisterExceptionHandler = true`
- `AddProblemDetailsService = true`

### 6.5 `IOptionsMonitor<PoliPageClientOptions>` reload behaviour

The SDK's `AddPoliPage` resolves `IOptions<PoliPageClientOptions>` **once at singleton construction time** and snapshots the values into the `PoliPageClient`. Subsequent `IConfiguration` reloads (e.g. `reloadOnChange: true` on `appsettings.json`, Azure App Configuration sentinel-key triggers, Key Vault rotation) update `IOptionsMonitor<PoliPageClientOptions>` but do **NOT** rebuild the client.

Practical implications:
- **API key rotation requires a process restart** in v0.1. A blue/green deploy or `kubectl rollout restart` is the supported rotation path.
- Hosts running `Microsoft.Azure.AppConfiguration.AspNetCore` with sentinel triggers do not pick up new keys without a restart — document this in the host's runbook.
- Per-request key overrides remain possible: the SDK accepts a per-call `RequestOptions` that can carry a fresh `ApiKey`; nothing in this package blocks that.

A live-reload story (subscribing to `IOptionsMonitor<PoliPageClientOptions>.OnChange` and rebuilding the inner `PoliPageClient` behind a thread-safe accessor) is **deferred to v0.2** — it requires SDK cooperation that does not yet exist. Tracked in §17.

### 6.3 Validation

The SDK already validates `ApiKey`, `MaxRetries`, `RetryDelay`, `RequestTimeout` via `AddOptions<PoliPageClientOptions>().Validate(...).ValidateOnStart()`. This package adds:

- `PoliPageAspNetCoreOptions.ProblemDetailsTypeUri` — must parse via `new Uri(value, UriKind.Absolute)`. Error: `"PoliPage.AspNetCore: ProblemDetailsTypeUri must be a well-formed absolute URI. Got: <value>"`.

Validation lives in `src/PoliPage.AspNetCore/Internal/Validators.cs` and is wired through `AddOptions<PoliPageAspNetCoreOptions>().Validate(...).ValidateOnStart()` inside `AddPoliPageAspNetCore`. Errors surface at `IHost.StartAsync()` — a misconfigured app fails fast on `dotnet run`, not on the first SDK call in production.

### 6.4 Environment / appsettings convention

The idiomatic ASP.NET Core source is `IConfiguration` — `appsettings.json`, `appsettings.{Environment}.json`, and environment-variable overrides via the colon-separated path convention (`PoliPage__ApiKey` on Linux, `PoliPage:ApiKey` on Windows).

```json
// appsettings.json
{
  "PoliPage": {
    "ApiKey": "pp_test_x",
    "MaxRetries": 3,
    "RequestTimeout": "00:00:30",
    "AspNetCore": {
      "ProblemDetailsTypeUri": "https://errors.acme.example/poli-page",
      "IncludeRequestIdInProblemDetails": true
    }
  }
}
```

Two overloads of `AddPoliPageAspNetCore` make this seamless — see §7.1.

The package does **NOT** read `Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")` itself. ASP.NET's `IConfiguration` already does so, and bypassing it would silently break users who layer `EnvironmentVariablesConfigurationSource` differently.

The example app's `appsettings.Development.json` shows the wiring; see §13.

---

## 7. DI registration

### 7.1 Service map

`src/PoliPage.AspNetCore/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Poli Page SDK and ASP.NET Core integration services.
    /// </summary>
    public static IServiceCollection AddPoliPageAspNetCore(
        this IServiceCollection services,
        Action<PoliPageClientOptions> configureClient,
        Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureClient);

        services.AddPoliPage(configureClient);                    // delegates to PoliPage SDK
        return AddAspNetCoreServices(services, configureAspNetCore);
    }

    /// <summary>
    /// Binds <see cref="PoliPageClientOptions"/> from the given configuration section
    /// (typically <c>configuration.GetSection("PoliPage")</c>), and registers the ASP.NET
    /// Core integration services.
    /// </summary>
    public static IServiceCollection AddPoliPageAspNetCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPoliPage(opts => configuration.Bind(opts));
        return AddAspNetCoreServices(
            services,
            aspNet =>
            {
                configuration.GetSection("AspNetCore").Bind(aspNet);
                configureAspNetCore?.Invoke(aspNet);
            });
    }

    /// <summary>
    /// Most-specific overload: full control over both option configuration callbacks.
    /// </summary>
    public static IServiceCollection AddPoliPageAspNetCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PoliPageClientOptions> configureClient,
        Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureClient);

        services.AddPoliPage(opts =>
        {
            configuration.Bind(opts);
            configureClient(opts);
        });
        return AddAspNetCoreServices(services, configureAspNetCore);
    }

    private static IServiceCollection AddAspNetCoreServices(
        IServiceCollection services,
        Action<PoliPageAspNetCoreOptions>? configure)
    {
        // Guard double-registration. A user who reads both READMEs and calls
        // `AddPoliPage(...)` themselves before `AddPoliPageAspNetCore(...)` would otherwise
        // double-register the singleton factory (last-wins on resolve, but the options
        // Configure callbacks both run, in registration order). The marker is keyed off
        // the response factory because the SDK's own services may or may not already
        // be present and we don't want to false-positive on those.
        if (services.Any(d => d.ServiceType == typeof(PoliPageResponseFactory)))
            return services;

        services.AddOptions<PoliPageAspNetCoreOptions>()
            .Configure(configure ?? (_ => { }))
            .Validate(Internal.Validators.ValidateProblemDetailsTypeUri,
                "PoliPage.AspNetCore: ProblemDetailsTypeUri must be a well-formed absolute URI.")
            .ValidateOnStart();

        services.AddSingleton<PoliPageResponseFactory>();
        services.AddSingleton<ExceptionHandling.PoliPageProblemDetailsFactory>();

        // Resolve the ASP.NET-side flags exactly once (config is now fully bound).
        services.AddSingleton<IPostConfigureOptions<PoliPageAspNetCoreOptions>>(sp =>
            new PostConfigure(sp, services));

        return services;
    }

    private sealed class PostConfigure(IServiceProvider sp, IServiceCollection services)
        : IPostConfigureOptions<PoliPageAspNetCoreOptions>
    {
        public void PostConfigure(string? name, PoliPageAspNetCoreOptions options)
        {
            if (options.AddProblemDetailsService) services.AddProblemDetails();
            if (options.RegisterExceptionHandler)
                services.AddExceptionHandler<ExceptionHandling.PoliPageExceptionHandler>();
        }
    }
}
```

The `PostConfigure` callback above is illustrative; in practice the flags are read inside `AddAspNetCoreServices` directly (their values are known at registration time because `Configure(configure)` ran synchronously above). The simpler shape is:

```csharp
private static IServiceCollection AddAspNetCoreServices(
    IServiceCollection services,
    Action<PoliPageAspNetCoreOptions>? configure)
{
    if (services.Any(d => d.ServiceType == typeof(PoliPageResponseFactory)))
        return services;

    var aspnet = new PoliPageAspNetCoreOptions();
    configure?.Invoke(aspnet);

    services.AddOptions<PoliPageAspNetCoreOptions>()
        .Configure(o =>
        {
            o.ProblemDetailsTypeUri = aspnet.ProblemDetailsTypeUri;
            o.IncludeRequestIdInProblemDetails = aspnet.IncludeRequestIdInProblemDetails;
            o.DefaultCacheControl = aspnet.DefaultCacheControl;
            o.SetNoSniffHeader = aspnet.SetNoSniffHeader;
            o.RegisterExceptionHandler = aspnet.RegisterExceptionHandler;
            o.AddProblemDetailsService = aspnet.AddProblemDetailsService;
        })
        .Validate(Internal.Validators.ValidateProblemDetailsTypeUri,
            "PoliPage.AspNetCore: ProblemDetailsTypeUri must be a well-formed absolute URI.")
        .ValidateOnStart();

    services.AddSingleton<PoliPageResponseFactory>();
    services.AddSingleton<ExceptionHandling.PoliPageProblemDetailsFactory>();

    if (aspnet.AddProblemDetailsService)
        services.AddProblemDetails();

    if (aspnet.RegisterExceptionHandler)
        services.AddExceptionHandler<ExceptionHandling.PoliPageExceptionHandler>();

    return services;
}
```

Pick the simpler shape unless the `IConfiguration`-overload binding path forces the deferred read.

| Service | Lifetime | Registered by |
|---|---|---|
| `PoliPageClient` | Singleton | SDK's `AddPoliPage` |
| `IOptions<PoliPageClientOptions>` | Singleton | SDK's `AddPoliPage` |
| `IOptions<PoliPageAspNetCoreOptions>` | Singleton | This package |
| `PoliPageResponseFactory` | Singleton | This package |
| `PoliPageProblemDetailsFactory` (internal) | Singleton | This package |
| `IExceptionHandler` → `PoliPageExceptionHandler` (internal) | Singleton | This package, when `RegisterExceptionHandler = true` (default) |
| `IProblemDetailsService` | Singleton | `AddProblemDetails()`, called by this package when `AddProblemDetailsService = true` (default) |
| `IHttpClientFactory` named `"PoliPage"`, `"PoliPage.Download"` | per `IHttpClientFactory` lifetime | SDK's `AddPoliPage` |

### 7.5 Double-registration safety

Calling `AddPoliPageAspNetCore(...)` twice on the same `IServiceCollection` (e.g., from two `services.AddX()` extension methods in different libraries) is **safe**: the second call short-circuits as soon as it finds `PoliPageResponseFactory` already registered. The same guard prevents a user who reads the SDK README first and writes `services.AddPoliPage(...)` themselves, then calls `services.AddPoliPageAspNetCore(...)`, from double-registering the client. The SDK's own `AddPoliPage` is **NOT** idempotent (it `Add`s rather than `TryAdd`s `PoliPageClient`); we rely on our marker to gate the whole composition.

Calling `services.AddPoliPage(...)` directly **after** `services.AddPoliPageAspNetCore(...)` still double-registers the SDK client — last-wins on resolve. We document this in [CLAUDE.md §10.14](../../CLAUDE.md) and the README "Configuration" section; no programmatic fix is possible without changing the SDK.

### 7.2 SDK constructor mapping

We do not call `new PoliPageClient(...)` ourselves. The SDK's `AddPoliPage(...)` already wires the singleton factory, named `HttpClient`s, options validation, and logger discovery (see `/Users/mickael/Projects/sdk-csharp/src/PoliPage/DependencyInjection/ServiceCollectionExtensions.cs`). Calling it from our overloads is the entirety of our DI contribution to the client itself.

### 7.3 Logger / HttpClient handling

- **Logger**: The SDK's `AddPoliPage` resolves `ILogger<PoliPageClient>` from DI when the user has not supplied an explicit `Logger` on `PoliPageClientOptions`. We rely on that — we do not register a logger ourselves.
- **HttpClient**: The SDK creates two named `HttpClient`s (`"PoliPage"`, `"PoliPage.Download"`). Users wanting Polly, mTLS, or a custom `DelegatingHandler` apply it to the named client in their composition root:

  ```csharp
  builder.Services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

  builder.Services.AddHttpClient("PoliPage")
      .AddStandardResilienceHandler();    // user's choice — see §1 caveat about double-retry
  ```

  We do not register handlers on either named client. Anything we add would shadow user customization.

### 7.4 `IConfiguration` binding helper

When users bind from `IConfiguration`, the binding source is whatever the host configured — `appsettings.json`, environment variables, Azure App Configuration, etc. The package emits no opinion about the source.

The `RequestTimeout` and `RetryDelay` properties on `PoliPageClientOptions` are `TimeSpan`. ASP.NET Core's configuration binder accepts the standard `TimeSpan` formats (`"00:00:30"`, `"0.00:01:00"`); document this in the README.

---

## 8. `PoliPageResults` and `PoliPageResponseFactory`

The two helpers expose the same four behaviours through the two ASP.NET Core response surfaces:

| Helper | Surface | Returns |
|---|---|---|
| `PoliPageResults.Pdf(bytes, filename?)` | Minimal API | `IResult` |
| `PoliPageResults.PdfStream(stream, filename?)` | Minimal API | `IResult` |
| `PoliPageResults.Preview(html)` | Minimal API | `IResult` |
| `PoliPageResults.DocumentRedirect(presignedUrl)` | Minimal API | `IResult` |
| `PoliPageResponseFactory.Pdf(bytes, filename?)` | MVC controller | `FileContentResult` |
| `PoliPageResponseFactory.PdfStream(stream, filename?)` | MVC controller | `FileStreamResult` |
| `PoliPageResponseFactory.Preview(html)` | MVC controller | `ContentResult` |
| `PoliPageResponseFactory.DocumentRedirect(presignedUrl)` | MVC controller | `RedirectResult` |

### 8.1 `PoliPageResults` (Minimal API)

`src/PoliPage.AspNetCore/Results/PoliPageResults.cs`:

```csharp
namespace PoliPage.AspNetCore;

public static class PoliPageResults
{
    /// <summary>
    /// Returns the rendered PDF bytes with <c>Content-Type: application/pdf</c>,
    /// RFC 5987-encoded <c>Content-Disposition</c>, and the configured cache + nosniff headers.
    /// </summary>
    public static IResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
        => new PdfResult(pdf, filename, inline);

    /// <summary>
    /// Streams the rendered PDF without buffering the full byte array.
    /// </summary>
    public static IResult PdfStream(Stream pdfStream, string? filename = null, bool inline = false)
        => new PdfStreamResult(pdfStream, filename, inline);

    /// <summary>
    /// Returns the preview HTML with <c>Content-Type: text/html; charset=utf-8</c>.
    /// </summary>
    public static IResult Preview(string html)
        => new PreviewResult(html);

    /// <summary>
    /// 302-redirects to the document's presigned URL.
    /// </summary>
    public static IResult DocumentRedirect(string presignedUrl)
        => new DocumentRedirectResult(presignedUrl);
}
```

Each result is a private `internal sealed class` implementing both `IResult` and `IEndpointMetadataProvider` (the latter introduced in .NET 9 — see <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses#iendpointmetadataprovider-and-iendpointparametermetadataprovider>). `IEndpointMetadataProvider` lets `Microsoft.AspNetCore.OpenApi` and Swashbuckle automatically discover the response content-type and 2xx status without users having to call `.Produces("application/pdf", 200)` on every endpoint.

Implementation pattern follows `Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult`:

```csharp
internal sealed class PdfResult(byte[] pdf, string? filename, bool inline)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "application/pdf";
        httpContext.Response.ContentLength = pdf.Length;

        if (filename is not null)
            httpContext.Response.Headers.ContentDisposition =
                ContentDispositionHeader.Build(filename, inline);

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await httpContext.Response.Body.WriteAsync(pdf, httpContext.RequestAborted);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK,
            type: typeof(byte[]),
            contentTypes: ["application/pdf"]));
    }
}
```

`PdfStreamResult` copies the stream to `Response.Body` using `Stream.CopyToAsync(..., CancellationToken)` so `HttpContext.RequestAborted` flows through. Its `PopulateMetadata` is identical (200, `application/pdf`).

`PreviewResult.PopulateMetadata` advertises 200 + `text/html; charset=utf-8`. `DocumentRedirectResult.PopulateMetadata` advertises 302 (the helper has no body schema).

With `IEndpointMetadataProvider` in place, this just works:

```csharp
app.MapGet("/invoices/{id}.pdf", (...) => PoliPageResults.Pdf(pdf, $"invoice-{id}.pdf"));
//                              ^ OpenAPI now knows: 200 application/pdf
```

Users can still chain `.Produces(...)` to refine the schema or document additional statuses (`.ProducesProblem(404)` for a 404 path); the metadata is additive, not replacement.

### 8.2 `PoliPageResponseFactory` (MVC)

`src/PoliPage.AspNetCore/Mvc/PoliPageResponseFactory.cs`:

```csharp
public sealed class PoliPageResponseFactory
{
    private readonly IOptions<PoliPageAspNetCoreOptions> _options;

    public PoliPageResponseFactory(IOptions<PoliPageAspNetCoreOptions> options)
        => _options = options;

    public FileContentResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        var result = new FileContentResult(pdf, "application/pdf");
        if (filename is not null)
        {
            result.FileDownloadName = inline ? null : filename;
            // Caller is expected to apply ContentDispositionHeader via HttpContext.Response.Headers
            // when they need RFC 5987 encoding — see §8.3.
        }
        return result;
    }

    public FileStreamResult PdfStream(Stream pdf, string? filename = null, bool inline = false)
    {
        var result = new FileStreamResult(pdf, "application/pdf");
        if (filename is not null && !inline) result.FileDownloadName = filename;
        return result;
    }

    public ContentResult Preview(string html) => new()
    {
        Content = html,
        ContentType = "text/html; charset=utf-8",
        StatusCode = StatusCodes.Status200OK,
    };

    public RedirectResult DocumentRedirect(string presignedUrl)
        => new(presignedUrl, permanent: false);
}
```

MVC's `FileContentResult.FileDownloadName` writes a basic `Content-Disposition: attachment; filename="<ascii>"`. For non-ASCII names, controllers explicitly write the header via `HttpContext.Response.Headers.ContentDisposition = ContentDispositionHeader.Build(...)` before returning the result. The README and `docs/responses.md` show both flows.

A controller-side filter that auto-applies `ContentDispositionHeader` is deferred to v0.2 (see §17) — the explicit pattern keeps surprise low and matches `IFileResult` conventions used by `Microsoft.AspNetCore.Mvc`.

### 8.3 Headers each helper sets

| Helper | Content-Type | Content-Disposition | Cache-Control | X-Content-Type-Options |
|---|---|---|---|---|
| `Pdf` | `application/pdf` | `attachment; filename="..."; filename*=UTF-8''...` when name set, else absent | from `DefaultCacheControl` | `nosniff` when `SetNoSniffHeader` |
| `PdfStream` | `application/pdf` | same as `Pdf` | from `DefaultCacheControl` | `nosniff` when `SetNoSniffHeader` |
| `Preview` | `text/html; charset=utf-8` | absent | from `DefaultCacheControl` | `nosniff` when `SetNoSniffHeader` |
| `DocumentRedirect` | absent (302) | absent | absent (Location header is what matters) | absent |

### 8.4 Filename encoding

`src/PoliPage.AspNetCore/Mvc/ContentDispositionHeader.cs` (internal):

```csharp
internal static class ContentDispositionHeader
{
    private static readonly SearchValues<char> Tspecials = SearchValues.Create("()<>@,;:\\\"/[]?={} \t");

    public static string Build(string filename, bool inline)
    {
        var disposition = inline ? "inline" : "attachment";

        if (IsAsciiSafe(filename))
            return $"{disposition}; filename=\"{Escape(filename)}\"";

        var asciiFallback = AsciiFallback(filename);
        var rfc5987 = "UTF-8''" + Uri.EscapeDataString(filename);
        return $"{disposition}; filename=\"{asciiFallback}\"; filename*={rfc5987}";
    }

    private static bool IsAsciiSafe(string s)
    {
        foreach (var ch in s)
            if (ch < 0x20 || ch > 0x7E || Tspecials.Contains(ch))
                return false;
        return true;
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");

    private static string AsciiFallback(string s)
    {
        Span<char> buffer = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
            buffer[i] = s[i] is >= (char)0x20 and <= (char)0x7E ? s[i] : '_';
        return new string(buffer);
    }
}
```

Algorithm identical to symfony-bundle's `PoliPageResponseFactory::makeDisposition()` and laravel's `PoliPageResponseFactory`. Tests are port-from-PHP — see `tests/PoliPage.AspNetCore.Tests/Mvc/ContentDispositionHeaderTests.cs`.

---

## 9. `MapPoliPageSmokeTest` endpoint

The symfony/laravel CLI smoke command (`bin/console poli-page:render`, `php artisan poli-page:render`) has no idiomatic equivalent in ASP.NET Core — there is no app-attached CLI. The closest pattern is a route the operator hits with `curl` once after deployment to confirm wiring.

### 9.1 Signature

`src/PoliPage.AspNetCore/Endpoints/EndpointRouteBuilderExtensions.cs`:

```csharp
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps an endpoint that renders <c>getting-started/welcome</c> and returns the PDF.
    /// Use this in non-production environments to verify the SDK is wired correctly.
    /// </summary>
    /// <remarks>
    /// Emits a startup-log warning when registered without an explicit authorization
    /// decision (neither <see cref="AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization"/>
    /// nor <see cref="AuthorizationEndpointConventionBuilderExtensions.AllowAnonymous"/>
    /// metadata is present at app-start). Production deployments should always pin one
    /// or the other to avoid burning API quota on unauthenticated bot traffic.
    /// </remarks>
    public static IEndpointConventionBuilder MapPoliPageSmokeTest(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/poli-page/smoke")
    {
        var convention = endpoints.MapGet(pattern, async (
            PoliPageClient client,
            CancellationToken cancellationToken) =>
        {
            var pdf = await client.Render.PdfAsync(
                new ProjectModeInput
                {
                    Project = "getting-started",
                    Template = "welcome",
                    Version = "1.0.0",
                    Data = new { name = "PoliPage.AspNetCore" },
                },
                cancellationToken: cancellationToken);

            return PoliPageResults.Pdf(pdf, "welcome.pdf", inline: true);
        });

        convention.Add(eb =>
        {
            // Marker so the startup-validator can find this endpoint later.
            eb.Metadata.Add(new PoliPageSmokeEndpointMarker());
        });

        // Register a startup task that scans for the marker and warns if neither
        // [Authorize] nor [AllowAnonymous] metadata is present.
        endpoints.ServiceProvider
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted
            .Register(() => WarnIfSmokeEndpointIsUnguarded(endpoints));

        return convention;
    }

    private static void WarnIfSmokeEndpointIsUnguarded(IEndpointRouteBuilder endpoints)
    {
        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PoliPage.AspNetCore.SmokeTest");

        var source = endpoints.DataSources.SelectMany(s => s.Endpoints);
        foreach (var endpoint in source)
        {
            if (endpoint.Metadata.GetMetadata<PoliPageSmokeEndpointMarker>() is null) continue;
            if (endpoint.Metadata.GetMetadata<AuthorizeAttribute>() is not null) return;
            if (endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>() is not null) return;
            LogMessages.SmokeEndpointUnguarded(logger);
            return;
        }
    }
}

internal sealed class PoliPageSmokeEndpointMarker { }
```

The warning message (in `Internal/LogMessages.cs`):

```csharp
[LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
    Message = "MapPoliPageSmokeTest is registered without explicit .RequireAuthorization(...) or .AllowAnonymous(). " +
              "In production this endpoint will burn API quota on unauthenticated callers — gate it or opt out explicitly.")]
public static partial void SmokeEndpointUnguarded(ILogger logger);
```

The warning is **once per app start**, not per request. Operators see it in the boot log; absence in CI is a regression worth catching.

### 9.2 Behaviour

- Hits the SDK with the well-known `getting-started/welcome` template (guaranteed present in every Poli Page org).
- Returns the PDF inline so a browser displays it directly.
- Honours `HttpContext.RequestAborted` (cancellation flows from the host).
- Inherits authn/authz from the endpoint group it's mapped into — users wrap it with `.RequireAuthorization()` or a route group for environment gating.
- Throws `PoliPageException` on failure; if the user has called `app.UsePoliPageExceptionHandler()`, the failure shape is `ProblemDetails`. Otherwise it bubbles to ASP.NET Core's developer exception page in dev and to a generic 500 in production.

### 9.3 What the smoke endpoint is NOT

- Not a health check. The dedicated `IHealthCheck` is `AddPoliPage()` on `IHealthChecksBuilder` — see §9.4. The smoke endpoint renders a PDF (heavier) and returns its bytes; the health check fires a cheap GET and returns `HealthCheckResult`.
- Not enabled by default. Users opt in with `app.MapPoliPageSmokeTest()` in `Program.cs`.
- Not for production traffic. The example app shows wrapping it behind `.RequireAuthorization("Operator")` or `if (app.Environment.IsDevelopment())`.

### 9.4 Registration

The `IEndpointConventionBuilder` return type lets users chain `.RequireAuthorization(...)`, `.WithTags("PoliPage")`, `.WithName("PoliPageSmoke")`, etc. Standard ASP.NET Core conventions.

---

## 10. Exception handling

Two delivery mechanisms map `PoliPageException` (and its subclasses) into `ProblemDetails` JSON, both routed through the same `PoliPageProblemDetailsFactory` and both delegating the **final write** to `IProblemDetailsService` so any `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callbacks the user registered also run on our responses. Net result: a host's 404, validation 422, and `PoliPageBadRequestException` 400 all emit the same JSON shape.

### 10.1 Primary path — `IExceptionHandler` (.NET 8+)

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageExceptionHandler.cs`:

```csharp
internal sealed class PoliPageExceptionHandler(
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
    ILogger<PoliPageExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PoliPageException poliEx)
            return false;     // not ours; let the next handler / fallback take it

        if (httpContext.Response.HasStarted)
        {
            LogMessages.ExceptionAfterResponseStarted(logger, poliEx);
            return false;     // can't rewrite; bubble for default 500 / disconnect
        }

        var problem = factory.Build(httpContext, poliEx);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        // Mark the current span as errored so distributed traces surface the failure.
        Activity.Current?.SetStatus(ActivityStatusCode.Error, poliEx.Message);
        Activity.Current?.AddTag("polipage.error.code", problem.Extensions["code"]);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
```

`AddPoliPageAspNetCore` auto-registers this via `services.AddExceptionHandler<PoliPageExceptionHandler>()` when `PoliPageAspNetCoreOptions.RegisterExceptionHandler` is `true` (default). The host wires it into the pipeline with the standard:

```csharp
var app = builder.Build();
app.UseExceptionHandler();   // <-- standard ASP.NET Core terminal middleware
app.UseStatusCodePages();    // optional but recommended
```

`UseExceptionHandler()` runs every registered `IExceptionHandler` in registration order until one returns `true`. Ours returns `false` for non-`PoliPageException` throws, so the rest of the host's chain (the default 500 page, `Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware`, etc.) still runs unmodified.

### 10.2 Fallback path — middleware (no `UseExceptionHandler()`)

For hosts that for any reason do not call `app.UseExceptionHandler()` (custom pipelines, minimal hosts with their own exception strategy), the legacy middleware path is kept:

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageExceptionHandlerMiddleware.cs`:

```csharp
internal sealed class PoliPageExceptionHandlerMiddleware(
    RequestDelegate next,
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
    ILogger<PoliPageExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await next(httpContext);
        }
        catch (PoliPageException ex)
        {
            if (httpContext.Response.HasStarted)
            {
                LogMessages.ExceptionAfterResponseStarted(logger, ex);
                throw;
            }

            var problem = factory.Build(httpContext, ex);
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = ex,
            });

            // If IProblemDetailsService refused (no AddProblemDetails() call),
            // fall back to writing the JSON ourselves via the source-gen context.
            if (!written)
            {
                httpContext.Response.ContentType = "application/problem+json";
                await httpContext.Response.WriteAsJsonAsync(
                    problem,
                    ProblemDetailsJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    cancellationToken: httpContext.RequestAborted);
            }
        }
    }
}
```

Registration extension (`ExceptionHandling/ApplicationBuilderExtensions.cs`):

```csharp
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the Poli Page exception handler middleware as a fallback for hosts that
    /// do not call <see cref="ExceptionHandlerExtensions.UseExceptionHandler"/>. For
    /// .NET 8+ hosts using <c>app.UseExceptionHandler()</c>, the package's
    /// <see cref="IExceptionHandler"/> is the primary path and this call is unnecessary.
    /// </summary>
    public static IApplicationBuilder UsePoliPageExceptionHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PoliPageExceptionHandlerMiddleware>();
    }
}
```

### 10.3 Ordering when both `UseExceptionHandler()` and `UsePoliPageExceptionHandler()` are in the pipeline

`app.UseExceptionHandler()` is a terminal handler — it catches any unhandled exception bubbling up the pipeline and dispatches to registered `IExceptionHandler`s. If `app.UsePoliPageExceptionHandler()` runs **before** it, the middleware catches `PoliPageException` first and never bubbles, so the `IExceptionHandler` never runs. If it runs **after**, the inner handler catches first.

**Recommendation**: pick one. If your host uses `UseExceptionHandler()`, do **not** call `UsePoliPageExceptionHandler()` — it's redundant and overrides the `IProblemDetailsService` cooperation path. The default `RegisterExceptionHandler = true` flag means new apps get the modern path automatically.

### 10.2 Status mapping

| SDK exception | HTTP status | `code` extension |
|---|---|---|
| `PoliPageAuthenticationException` (HTTP 401) | 401 | `authentication_failed` |
| `PoliPageRateLimitException` (HTTP 429) | 429 | `rate_limited` |
| `PoliPageBadRequestException` (HTTP 400) | 400 | `bad_request` |
| `PoliPageNotFoundException` (HTTP 404) | 404 | `not_found` |
| `PoliPageConnectionException` (no upstream status) | 502 | `upstream_unavailable` |
| `PoliPageException` (catch-all, has `StatusCode`) | `StatusCode` value | `poli_page_error` |
| `PoliPageException` (catch-all, no `StatusCode`) | 502 | `poli_page_error` |

`PoliPageConnectionException` maps to 502 (Bad Gateway) and not 504 (Gateway Timeout) on purpose — the SDK already collapses timeouts and DNS failures into `PoliPageConnectionException` (see `/Users/mickael/Projects/sdk-csharp/src/PoliPage/Exceptions/`). 502 is the honest answer in both cases. This matches the nestjs spec §10 ("network/timeout → 502").

### 10.3 `ProblemDetails` output

```json
{
  "type": "https://poli.page/errors#rate_limited",
  "title": "Rate limit exceeded",
  "status": 429,
  "detail": "Rate limit exceeded. Retry after 30 seconds.",
  "instance": "/invoices/INV-42",
  "code": "rate_limited",
  "poliPageRequestId": "req_01J4ZRYPK5W2N3K8M9X1Q7Y0PC",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

- `type`: configured via `PoliPageAspNetCoreOptions.ProblemDetailsTypeUri`, with the `code` appended as a fragment.
- `title`: from a static `code → title` lookup table (kept inside the middleware).
- `detail`: `PoliPageException.Message`.
- `instance`: `HttpContext.Request.Path + QueryString`.
- `code`: see table in §10.2.
- `poliPageRequestId`: present only when `IncludeRequestIdInProblemDetails` is `true` AND `PoliPageException.RequestId` is non-null.
- `traceId`: `Activity.Current?.Id` — ASP.NET Core's `ProblemDetailsService` adds this automatically when used; we mirror the behaviour for parity.

JSON source generation (`ProblemDetailsJsonContext`) keeps the middleware AoT-compatible and avoids `Newtonsoft.Json`.

### 10.4 Wiring into an existing `ExceptionFilterAttribute` (for MVC users)

For users who already have an MVC exception filter (`[ServiceFilter(typeof(MyExceptionFilter))]`) and prefer not to add another middleware, the README documents resolving `PoliPageProblemDetailsFactory` from DI inside their filter:

```csharp
public class MyExceptionFilter(PoliPageProblemDetailsFactory factory) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is PoliPageException ex)
        {
            var problem = factory.Build(context.HttpContext, ex);
            context.Result = new ObjectResult(problem)
            {
                StatusCode = problem.Status,
                ContentTypes = { "application/problem+json" },
            };
            context.ExceptionHandled = true;
        }
    }
}
```

`PoliPageProblemDetailsFactory` is registered as `Singleton` by `AddPoliPageAspNetCore` for exactly this reason.

---

## 11. Lifecycle hooks (`OnRetry`, `OnError`)

The SDK's `PoliPageClientOptions.OnRetry` and `OnError` are `Action<RetryEvent>` / `Action<Exception>` callbacks. They are NOT bridged into ASP.NET Core diagnostics (`DiagnosticListener`, `EventSource`, `ActivitySource`) by this package — that would be intrusive and opinionated.

The README documents two idiomatic wirings:

**ILogger bridge** (simple, no diagnostic plumbing):

```csharp
builder.Services.AddPoliPageAspNetCore(opts =>
{
    opts.ApiKey = builder.Configuration["PoliPage:ApiKey"]!;
    opts.OnRetry = retry => app.Logger.LogWarning(
        "PoliPage retry {Attempt} after {Delay}", retry.Attempt, retry.Delay);
});
```

**`ActivitySource` bridge** (OpenTelemetry users):

```csharp
private static readonly ActivitySource Source = new("PoliPage");

builder.Services.AddPoliPageAspNetCore(opts =>
{
    opts.ApiKey = builder.Configuration["PoliPage:ApiKey"]!;
    opts.OnRetry = retry =>
    {
        using var activity = Source.StartActivity("PoliPage.Retry");
        activity?.SetTag("attempt", retry.Attempt);
        activity?.SetTag("delay", retry.Delay);
    };
});
```

A dedicated `PoliPage.AspNetCore.OpenTelemetry` package may follow in v0.2 if users ask. For v0.1, the README pattern is the answer.

---

## 12. Unpublished-SDK workaround

`PoliPage` (the SDK) is **not yet on NuGet**. We use a `nuget.config` + a local-packages source pointing at `../sdk-csharp/artifacts/package/release/`. Local-only — published consumers resolve from nuget.org once the SDK ships.

### 12.1 Local source

`asp/nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="poli-page-local" value="../sdk-csharp/artifacts/package/release" />
  </packageSources>
</configuration>
```

`Directory.Packages.props` (Central Package Management) pins:

```xml
<PackageVersion Include="PoliPage" Version="1.0.0-local" />
```

The `1.0.0-local` placeholder resolves from the local source. When `sdk-csharp` ships `1.0.0`, the version pin moves to `1.0.0` and the local source line is removed in the same PR.

### 12.2 What changes when the SDK publishes

1. `git rm nuget.config`
2. Update `Directory.Packages.props`: `<PackageVersion Include="PoliPage" Version="1.0.0" />`
3. `dotnet restore` (resolves from nuget.org now)
4. Tag v0.1.0 of `PoliPage.AspNetCore`.

**Package source code is untouched** by this transition — only the dev environment is.

### 12.3 CI handling

CI checks out both repos side-by-side (`sdk-csharp/` and `asp/`), builds `sdk-csharp` first with `dotnet pack -c Release -o sdk-csharp/artifacts/package/release`, then `dotnet restore` in `asp/` finds the freshly-built package via the local source. Workflow snippet:

```yaml
- uses: actions/checkout@v4
  with: { path: asp }
- uses: actions/checkout@v4
  with: { repository: poli-page/sdk-csharp, path: sdk-csharp, ref: main }
- run: dotnet pack sdk-csharp/PoliPage.sln -c Release -o sdk-csharp/artifacts/package/release
- run: dotnet restore asp/PoliPage.AspNetCore.sln
```

Once `sdk-csharp` publishes `1.0.0`, the second checkout and the `dotnet pack` step disappear from the workflow.

### 12.4 Example app

`example-app/example-app.csproj` references the package by version, same as the integration project — it picks up the local source via the root `nuget.config` automatically. No special wiring.

---

## 13. Example app

`example-app/` is a self-contained ASP.NET Core 10 application that mirrors the symfony-bundle's `example-app/` feature-for-feature. It exists to prove the integration end-to-end, in code that users can read and copy.

### 13.1 Routes

The example app is **Minimal API-first** (the dominant ASP.NET Core 8+ idiom) with one MVC controller (`InvoicesController`) demonstrating the MVC code path.

| SDK demo step | Route | Surface |
|---|---|---|
| Demo dashboard | `GET /` | Minimal API + static file (`wwwroot/demo.html`) |
| 1. `Render.PdfAsync` | `GET /render/pdf` | `PoliPageResults.Pdf` |
| 2. `Render.PdfStreamAsync` | `GET /render/stream` | `PoliPageResults.PdfStream` |
| 3. `RenderToFileAsync` | `dotnet run --project example-app -- render-to-file` | top-level statement guard in `Program.cs` |
| 4. `Render.PreviewAsync` | `GET /render/preview` | `PoliPageResults.Preview` |
| 5. `Documents.CreateAsync` | `POST /documents` | JSON via `Results.Ok(...)` |
| 6. `Documents.GetAsync` | `GET /documents/{id}` | `PoliPageResults.DocumentRedirect` |
| 7. `Documents.ThumbnailsAsync` | `GET /documents/{id}/thumbnails` | `Results.Ok(...)` |
| 8. `Documents.PreviewAsync` | `GET /documents/{id}/preview` | `PoliPageResults.Preview` |
| 9. `Documents.DeleteAsync` | `DELETE /documents/{id}` | `Results.NoContent()` |
| 10. Error mapping | `GET /errors/bad-version` | throws → `UsePoliPageExceptionHandler` → ProblemDetails 400 |
| MVC sample | `GET /invoices/{id}.pdf` | `InvoicesController` using `PoliPageResponseFactory` |

`MapPoliPageSmokeTest("/poli-page/smoke")` is also mapped, providing the canonical `welcome.pdf` round-trip.

### 13.2 Interactive demo UI at `GET /`

`wwwroot/demo.html` is a single-page dashboard mirroring `/Users/mickael/Projects/symfony-bundle/example-app/templates/demo.html`:

- White surface, indigo `#4f5d99` accent.
- Manrope display sans + IBM Plex Sans body + JetBrains Mono code (loaded from Google Fonts).
- One button per SDK feature.
- Inline `<iframe>` PDF previews.
- `<iframe srcdoc>` sandboxed HTML previews.
- JSON pretty-print blocks (red on non-2xx).
- Document-lifecycle state machine in vanilla JS — `docId` captured into `window.state`, downstream buttons gated on its presence, cleared on Delete.

Served via:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/demo.html"));
```

No Razor, no Razor Pages, no Blazor. The HTML is a static asset — no server-side templating dependency, brand-consistent with the sister demos.

### 13.3 Layout

```
example-app/
├── example-app.csproj
├── Program.cs                                  # Minimal API + UsePoliPageExceptionHandler
├── appsettings.json
├── appsettings.Development.json
├── Controllers/
│   └── InvoicesController.cs                   # MVC sample
├── Endpoints/
│   ├── RenderEndpoints.cs                      # extension methods on IEndpointRouteBuilder
│   ├── DocumentEndpoints.cs
│   └── ErrorEndpoints.cs
├── wwwroot/
│   ├── demo.html
│   └── (favicon, fonts referenced via Google CDN)
└── Scripts/
    └── RenderToFile.cs                         # invoked when CLI arg "render-to-file" present
```

### 13.4 `Program.cs`

```csharp
using PoliPage;
using PoliPage.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddPoliPageWorkspaceEnvFile();    // see §13.5

builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));
// AddPoliPageAspNetCore already calls AddProblemDetails() + AddExceptionHandler<PoliPageExceptionHandler>()
// when RegisterExceptionHandler / AddProblemDetailsService = true (default).

builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddPoliPage();

var app = builder.Build();

if (args is ["render-to-file", ..])
{
    await Scripts.RenderToFile.RunAsync(app.Services, args[1..]);
    return;
}

app.UseExceptionHandler();          // runs every registered IExceptionHandler
app.UseStatusCodePages();           // adds ProblemDetails for 4xx without a body

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPoliPageSmokeTest("/poli-page/smoke")
   .AllowAnonymous();               // example app is dev-only; in prod, swap to .RequireAuthorization(...)

app.MapRenderEndpoints();
app.MapDocumentEndpoints();
app.MapErrorEndpoints();
app.MapControllers();
app.MapHealthChecks("/healthz");

app.MapGet("/", () => Results.Redirect("/demo.html"));

await app.RunAsync();
```

### 13.5 Workspace `.env` loading

ASP.NET Core's `IConfiguration` already supports `.env`-style sources via `Microsoft.Extensions.Configuration.EnvironmentVariables`, but that source only reads `Environment.GetEnvironmentVariable(...)` — it does not parse a `.env` file. The workspace convention from `INTEGRATIONS_PLAN.md` §"Cross-cutting DX patterns" §2 requires a single root `.env` shared across all integrations.

`example-app/Scripts/PoliPageWorkspaceEnvFile.cs` provides:

```csharp
internal static class PoliPageWorkspaceEnvFile
{
    public static IConfigurationBuilder AddPoliPageWorkspaceEnvFile(this IConfigurationBuilder builder)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            var settings = new Dictionary<string, string?>();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var idx = trimmed.IndexOf('=');
                if (idx < 0) continue;
                var key = trimmed[..idx].Trim().Replace("POLI_PAGE_", "PoliPage:");
                var value = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
                if (Environment.GetEnvironmentVariable(trimmed[..idx].Trim()) is null)
                    settings[key] = value;
            }
            builder.AddInMemoryCollection(settings);
            return builder;   // first file wins
        }
        return builder;
    }
}
```

Precedence: real environment > shell exports > workspace `.env`. Real shell exports always win (`Environment.GetEnvironmentVariable` check above). Same precedence rules as the bundle/nextjs.

**Do NOT** instruct users to `cp .env .env.local` or introduce a per-app `.env.local`. Workspace `.env` only. This is a hard requirement from Mickael — see CLAUDE.md §10.3.

### 13.6 Running it

```bash
cd example-app
dotnet run                                  # → http://localhost:5093
```

ASP.NET Core's `launchSettings.json` provides the port; the example app uses the default `dotnet new web` profile so no extra setup is needed.

---

## 14. Testing strategy

### 14.1 Tooling

- **Test framework**: **xUnit** + FluentAssertions. Matches `sdk-csharp` (`/Users/mickael/Projects/sdk-csharp/CLAUDE.md` §8).
- **WebApplicationFactory**: `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TEntryPoint>` for end-to-end middleware / endpoint tests. `TEntryPoint` is a marker class declared in the test assembly (no `[InternalsVisibleTo]` games on the example app).
- **Mocks**: a hand-rolled `FakePoliPageClient` test double in `tests/.../Fixtures/FakePoliPageClient.cs`. No `Moq`, no `NSubstitute` — the surface is small enough that a hand-rolled fake is clearer (matches `sdk-csharp`'s "custom `DelegatingHandler`" preference).
- **Lint / format**: `dotnet format` + `.editorconfig` + analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`, `Meziantou.Analyzer`, `Roslynator.Analyzers`). Same set as `sdk-csharp`.

### 14.2 What to test (integration-specific)

- **`AddPoliPageAspNetCore` overloads**: each overload registers `PoliPageClient`, `PoliPageResponseFactory`, `IOptions<PoliPageAspNetCoreOptions>`. Use `services.BuildServiceProvider()` and `GetRequiredService<T>()`. The same instance comes back twice (singleton).
- **`IConfiguration` binding**: `PoliPage:ApiKey`, `PoliPage:MaxRetries`, `PoliPage:RequestTimeout` (TimeSpan), `PoliPage:AspNetCore:ProblemDetailsTypeUri` all bind through the right overload.
- **Validation**: missing `ApiKey` (SDK level), malformed `ProblemDetailsTypeUri` (this package level) raise `OptionsValidationException` at `host.StartAsync()`.
- **`PoliPageResults.Pdf` / `PdfStream` / `Preview` / `DocumentRedirect`**: each writes the correct status, content-type, content-disposition, cache-control, nosniff header. ASCII and non-ASCII filenames both encode via RFC 5987.
- **`PoliPageResponseFactory`**: same behavioural matrix as `PoliPageResults` but returning `IActionResult` types. Header-checking goes through `HttpContext.Response.Headers` after the result executes.
- **`PoliPageExceptionHandlerMiddleware`**:
  - `PoliPageAuthenticationException` → 401, `code=authentication_failed`, `application/problem+json`.
  - `PoliPageRateLimitException` → 429.
  - `PoliPageBadRequestException` → 400.
  - `PoliPageConnectionException` → 502.
  - Generic `PoliPageException` with status → that status.
  - Non-`PoliPageException` → rethrown (NOT caught).
  - Exception thrown after `Response.HasStarted` → rethrown + warning logged (cannot rewrite).
- **`MapPoliPageSmokeTest`**: returns 200 + `application/pdf` when the SDK call succeeds (via `FakePoliPageClient` returning a byte[]). Returns the mapped status when `FakePoliPageClient` throws and the middleware is in the pipeline.
- **`HealthChecksBuilderExtensions.AddPoliPage`**: returns `HealthCheckResult.Healthy` on SDK success, `Unhealthy` on `PoliPageException`.
- **`ContentDispositionHeader.Build`**: ASCII-only filename → quoted basic syntax. Embedded quote → escaped. Non-ASCII → `filename=` ASCII fallback + `filename*=UTF-8''<rfc5987>`. Empty string → throws `ArgumentException`.

### 14.3 What we explicitly do NOT test

- HTTP transport behaviour (`HttpClient`, `HttpClientFactory`).
- Retry policy.
- 4xx / 5xx → exception mapping inside the SDK.
- Idempotency-Key generation.
- Stream chunking correctness.
- API contract drift.

These belong to `sdk-csharp/tests/`. Re-testing them here doubles maintenance. **If you find yourself writing a `WireMock.Net` server, stop — you're doing the SDK's job.**

### 14.4 `WebApplicationFactory` pattern

```csharp
public class PoliPageWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakePoliPageClient Fake { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PoliPageClient>();
            services.AddSingleton<PoliPageClient>(_ => Fake);
        });
    }
}
```

The fake exposes call records (`fake.LastInput`, `fake.PdfBytes = ...`) for assertions. Tests instantiate the factory once per test class (`IClassFixture<PoliPageWebApplicationFactory>`) and reset the fake in `Dispose` between tests.

### 14.5 Real-API integration tests

`tests/PoliPage.AspNetCore.IntegrationTests/` contains one happy-path test:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task SmokeEndpoint_HitsRealApi_ReturnsPdf()
{
    if (Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY") is null)
        return;     // skipped without explicit Skip — keeps CI clean when secret absent

    using var factory = new PoliPageWebApplicationFactory();   // real client this time
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/poli-page/smoke");
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

    var bytes = await response.Content.ReadAsByteArrayAsync();
    bytes.Should().StartWith("%PDF-"u8.ToArray());
}
```

One test. Real round-trip. Confirms the wire works end-to-end. Tagged `[Trait("Category", "Integration")]` so `dotnet test --filter "Category!=Integration"` skips it.

### 14.6 Coverage target

- Unit tests cover 100% of branches in the package (small surface; feasible).
- Integration tests are not coverage-counted; their job is "real round-trip works once".

---

## 15. CI matrix

Single workflow `.github/workflows/ci.yml`:

```yaml
matrix:
  include:
    - { os: ubuntu-latest,  tfm: net8.0 }
    - { os: ubuntu-latest,  tfm: net10.0 }
    - { os: windows-latest, tfm: net8.0 }
    - { os: windows-latest, tfm: net10.0 }
    - { os: macos-latest,   tfm: net10.0 }
steps:
  - actions/checkout@v4 (this repo)
  - actions/checkout@v4 (poli-page/sdk-csharp, path: sdk-csharp)
  - actions/setup-dotnet@v4 with multi-TFM SDK install
  - dotnet pack sdk-csharp -c Release -o sdk-csharp/artifacts/package/release
  - dotnet restore
  - dotnet format --verify-no-changes
  - dotnet build -c Release --no-restore -f ${{ matrix.tfm }}
  - dotnet test -c Release --no-build -f ${{ matrix.tfm }} --filter "Category!=Integration"
  - dotnet pack -c Release --no-build
```

Each step auto-skips with a friendly message when the relevant project or test directory does not yet exist — same convention as `sdk-csharp`. A freshly scaffolded repo is green from day one.

The real-API integration test runs only on `main` after merge, gated on `secrets.POLI_PAGE_DEVELOP_API_KEY` being present.

---

## 16. Versioning & release

- **Semantic versioning** (`MAJOR.MINOR.PATCH`).
- **0.x.y** while the surface is unstable; **1.0.0** when we commit to backwards compatibility.
- Each release ships a tag (`v0.1.0`) and a NuGet push to nuget.org.
- The release workflow runs after CI passes on `main`: `dotnet pack -c Release` then `dotnet nuget push` with `NUGET_API_KEY`.
- `MIGRATION.md` documents every breaking change between minors, even pre-1.0.

---

## 17. Deferred to v0.2+ (do not build in v0.1.0)

- **Multi-client / keyed services** — `services.AddKeyedSingleton<PoliPageClient>(...)` with multiple `ApiKey`s. Wait for user request.
- **`IExceptionFilter` (MVC) variant of the middleware** — the terminal middleware works for both MVC and Minimal APIs. Add the filter if users explicitly ask.
- **`[ProducesPdf]` action filter** — a hypothetical attribute that auto-applies the headers. Defer; explicit is better.
- **Swashbuckle / `Microsoft.AspNetCore.OpenApi` integration** — users add their own `[Produces("application/pdf")]` attributes. Defer.
- **`PoliPage.AspNetCore.OpenTelemetry` companion package** — bridging `OnRetry` / `OnError` into `ActivitySource` and `Meter`. Defer; the README pattern in §11 covers the manual path.
- **`IRequestCultureProvider` / locale plumbing for templates** — the SDK accepts a `Locale` in `RequestOptions`; users set it themselves.
- **Razor view rendering helper** — a `Razor.Render(view, model)` → SDK helper. Defer; users compose this themselves with `ICompositeViewEngine`.
- **`PoliPageHostedService` for background rendering jobs** — schedule rendering on a `BackgroundService`. Defer; not in the integration's scope.
- **`IOptionsMonitor`-driven runtime client rebuild** — picking up a rotated API key from `IConfiguration` reload without a process restart. Requires SDK cooperation (a way to swap the inner `HttpClient`'s default request headers without rebuilding the whole client). Defer to v0.2; document the process-restart-required path in §6.5.
- **AOT analyzer cleanup of edge paths** — `<IsAotCompatible>true</IsAotCompatible>` is set on the project from v0.1.0 and the analyzer must be clean across the runtime path. The `IConfiguration` binding overloads of `AddPoliPageAspNetCore` rely on the `Microsoft.Extensions.Configuration.Binder` source generator (`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`). If a future analyzer release flags new warnings in the binder source-gen output, suppress narrowly with a documented justification rather than disabling the flag wholesale.

**Discipline rule**: when implementing, if a "small addition" feels tempting, check this list first. If it's here, defer. If it's not here, ask before adding.

---

## 18. Resolved decisions

Captured from the spec-review conversation so future agents don't reopen them. Most carry from sister specs with .NET-specific adjustments.

| Decision | Choice | Why |
|---|---|---|
| Verdict (package vs recipe) | **Full NuGet package** | ASP.NET Core has rigorous conventions (IConfiguration, IOptions, IResult, IActionResult, middleware, endpoint routing, health checks) that the bare SDK can't satisfy from its README. The package centralises them once. |
| Package name | **`PoliPage.AspNetCore`** | Matches `Sentry.AspNetCore`, `Serilog.AspNetCore`, `Hangfire.AspNetCore`. Familiar shape for any .NET dev. |
| Namespace | **`PoliPage.AspNetCore`** | Same as package; one namespace for the whole public surface. The SDK's `PoliPage` namespace is separately imported for `PoliPageClient`, `PoliPageException`, etc. |
| Target frameworks | **`net8.0` + `net10.0`** | LTS-only. Matches `sdk-csharp` exactly. No `netstandard2.0` (ASP.NET Core has none) and no preview TFMs. |
| Options pattern | **Two `IOptions<T>` bags: SDK's `PoliPageClientOptions` + this package's `PoliPageAspNetCoreOptions`** | Keeps the SDK's options surface untouched. Lets a future companion package add its own bag without conflict. |
| Configuration binding | **Three `AddPoliPageAspNetCore` overloads (callback / `IConfiguration` / both)** | Same shape Microsoft uses for `AddAuthentication`, `AddDataProtection`. No surprises. |
| Validation | **`ValidateOnStart` for both option bags** | A misconfigured host fails on `dotnet run`, not in production. Matches the SDK. |
| SDK constructor mapping | **Delegate to SDK's `AddPoliPage`; do not duplicate** | The SDK already handles `IHttpClientFactory`, named clients, logger discovery, validation. Calling it from here is the entirety of our DI contribution. |
| Minimal API surface | **Static `PoliPageResults` returning `IResult`** | Matches `Microsoft.AspNetCore.Http.Results.*`. Zero ceremony. |
| MVC surface | **`PoliPageResponseFactory` returning `IActionResult`** | Resolvable from DI; parity with the symfony-bundle's `PoliPageResponseFactory`. |
| Smoke entry point | **Endpoint (`MapPoliPageSmokeTest`)**, not CLI | ASP.NET has no app-attached CLI. An endpoint is the idiomatic surface. Mirrors the laravel CLI's *intent* (one well-known render) through the framework-native primitive. |
| Health check | **Opt-in via `IHealthChecksBuilder.AddPoliPage()`** | Users on `Microsoft.Extensions.Diagnostics.HealthChecks` get one-line wiring; users not on that stack pay nothing. |
| Exception handling primary | **`IExceptionHandler` (.NET 8+) auto-registered via `RegisterExceptionHandler = true`** | The .NET 8+ idiom. Composes with the user's other `IExceptionHandler`s through `app.UseExceptionHandler()` — non-destructive. Falls through (returns `false`) on non-`PoliPageException`. |
| Exception handling fallback | **`PoliPageExceptionHandlerMiddleware` via opt-in `app.UsePoliPageExceptionHandler()`** | For hosts that don't call `app.UseExceptionHandler()`. Same `IProblemDetailsService` integration; same status mapping. |
| ProblemDetails writer | **Delegate to `IProblemDetailsService.TryWriteAsync(...)` so user-registered `CustomizeProblemDetails` callbacks fire on our responses** | Apps that call `services.AddProblemDetails()` for their other errors get one consistent JSON shape across all sources. |
| Exception status mapping | **Pass-through 4xx/5xx; network/timeout → 502** | Matches nestjs spec §10 ("network/timeout → 502 with `code: 'NETWORK_ERROR'`"). |
| Distributed-trace marking | **`Activity.Current?.SetStatus(ActivityStatusCode.Error, ...)` + `polipage.error.code` tag** in both the handler and the middleware | Failed spans surface in OpenTelemetry / Application Insights without users wiring anything extra. |
| Lifecycle hooks bridging | **No auto-bridge to `DiagnosticListener`, `ActivitySource`, or `EventSource`** | Intrusive. The SDK already exposes `OnRetry` / `OnError`; users wire them as they prefer. README shows ILogger + ActivitySource patterns. |
| Test runner | **xUnit + FluentAssertions** | Matches `sdk-csharp`. Deviating to NUnit would surprise every contributor. |
| Mock strategy | **Hand-rolled `FakePoliPageClient`** | Small surface; no `Moq`/`NSubstitute` complexity needed. Matches `sdk-csharp`'s "custom `DelegatingHandler`" preference. |
| End-to-end test host | **`WebApplicationFactory<TEntryPoint>`** | The Microsoft-blessed pattern. `dotnet new webapi` already references it. |
| Demo app surface | **Single-page HTML dashboard at `GET /`, no view engine** | Carries from nestjs §13.2 and symfony-bundle `templates/demo.html`. Brand consistency. |
| `renderToFile` placement | **CLI arg branch in `Program.cs`, not a separate command framework** | `if (args is ["render-to-file", ..])`. Mirrors nestjs's `npx tsx scripts/render-to-file.ts` script — minimal ceremony. |
| `nuget.config` workaround | **Local source pointing at `../sdk-csharp/artifacts/package/release`** | Standard .NET pre-publish pattern. Cleanly removable when the SDK publishes. |
| CI matrix shape | **5 cells (TFM × OS)** | net8/net10 × ubuntu/windows + net10 × macos. Same shape as `Sentry.AspNetCore`. |
| AOT compatibility | **Certified on `net10.0` from v0.1 — `<IsAotCompatible>true</IsAotCompatible>` gated by TFM** | Source-gen JSON, source-gen config binder, source-gen logging. The `IConfiguration` binding overloads of `AddPoliPageAspNetCore` rely on `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`. AOT analyzer is gated to `net10.0` because `net8.0`'s ASP.NET-flavored AOT warnings still include known false-positives the platform team fixed in `net9.0`+; turning the flag on for `net8.0` would block CI on those. Re-evaluate at net8 EOL (Nov 2026). |
| Logging | **`[LoggerMessage]` source-gen throughout** | CA1848 / LOG001 analyzer fails the build on direct `ILogger.LogX(...)` calls. Source-gen also a hard requirement for AOT (no reflection-based formatter calls). |
| OpenAPI metadata on `IResult` types | **Each result implements `IEndpointMetadataProvider`** | `Microsoft.AspNetCore.OpenApi` / Swashbuckle auto-discover content type + status. Users don't need to chain `.Produces("application/pdf", 200)` on every endpoint. |
| Double-registration of `AddPoliPageAspNetCore` | **Marker-gated short-circuit** | Calling the extension twice (or pairing it with a user-side `services.AddPoliPage(...)` upstream) is safe. Prevents the silent double-Configure-callback footgun. |
| `IOptionsMonitor`-driven key rotation | **Out of scope for v0.1 — process restart required** | Documented in §6.5. SDK cooperation needed. Defer to v0.2. |
| Package metadata for NuGet | **`<PackageReadmeFile>README.md</PackageReadmeFile>` + `Microsoft.SourceLink.GitHub`** | NuGet listing renders the README; debugger steps from `.pdb` to GitHub sources. Both are zero-cost-to-consumers polish. |
| Versioning | **SemVer, 0.x.y until stable, 1.0.0 commits to compatibility** | Standard. Matches every sister integration. |

---

## 19. Implementation order (for the agent picking this up)

Recommended slice ordering. Each slice is one or two RED → GREEN → refactor cycles, lands as one PR.

1. **Scaffold** the solution: `dotnet new sln`, `dotnet new classlib -n PoliPage.AspNetCore -f net10.0`, add `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `nuget.config`. CI workflow stays auto-skipping; first push goes green from "everything skipped".
2. **`PoliPageAspNetCoreOptions`** + tests. Pure POCO; one validator test for `ProblemDetailsTypeUri`.
3. **`AddPoliPageAspNetCore` (callback overload)** + tests. Asserts `PoliPageClient`, `PoliPageResponseFactory`, both `IOptions<T>` resolve.
4. **`AddPoliPageAspNetCore` (`IConfiguration` overload)** + tests. Binds from in-memory `IConfiguration`.
5. **`ContentDispositionHeader`** + tests (ASCII / non-ASCII / escaped quotes / empty string).
6. **`PoliPageResults.Pdf` + `PdfResult`** + tests. WebApplicationFactory with a `MapGet` that returns the result; assert response shape.
7. **`PoliPageResults.PdfStream`**, **`Preview`**, **`DocumentRedirect`** + tests. One cycle each.
8. **`PoliPageResponseFactory`** (MVC) + tests. Tests run the result against `HttpContext.Response` via `IActionResult.ExecuteResultAsync`.
9. **`PoliPageExceptionHandlerMiddleware`** + `PoliPageProblemDetailsFactory` + tests. Six status-mapping tests, one rethrow-after-HasStarted test, one non-PoliPageException test.
10. **`MapPoliPageSmokeTest`** + tests. WebApplicationFactory + FakePoliPageClient.
11. **`AddPoliPage` on `IHealthChecksBuilder`** + tests.
12. **`example-app/`**: scaffold, port `wwwroot/demo.html` from the symfony-bundle, wire endpoints, MVC controller, render-to-file CLI branch. Manual smoke: `dotnet run` and click every button.
13. **Integration test project** + real-API test.
14. **README, CHANGELOG, MIGRATION** filled to v0.1.0.
15. **Tag v0.1.0**, push to NuGet (when the SDK has published 1.0.0 — see §12.2).

Stick to the order. Don't pre-build later slices.

---

## 20. Open questions (none blocking v0.1.0)

- **Should we ship a `PoliPage.AspNetCore.OpenTelemetry` companion in v0.1?** Current answer: no — the manual pattern in §11 covers the use case. Revisit if two or more users ask for it.
- **Should `MapPoliPageSmokeTest` require authentication by default?** Current answer: no — let users opt in via `.RequireAuthorization(...)` (idiomatic). The README warns about the operational implication.
- **Razor `View()` rendering helper?** Deferred to v0.2 (§17). Reopens if users compose their own and the pattern keeps repeating in user code.

This document is the source of truth. If a PR's design conflicts with it, the spec gets updated FIRST in the same PR, with reasoning in the description.
