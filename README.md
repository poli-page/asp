# Poli Page for ASP.NET Core

> Render Poli Page documents as ASP.NET Core HTTP responses.

[![CI](https://github.com/poli-page/asp/actions/workflows/ci.yml/badge.svg)](https://github.com/poli-page/asp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PoliPage.AspNetCore.svg)](https://www.nuget.org/packages/PoliPage.AspNetCore)
[![NuGet downloads](https://img.shields.io/nuget/dt/PoliPage.AspNetCore.svg)](https://www.nuget.org/packages/PoliPage.AspNetCore)
[![License](https://img.shields.io/github/license/poli-page/asp)](LICENSE)

## About

This package wraps the Poli Page .NET SDK as an ASP.NET Core-native integration: a one-line DI registration that composes the SDK's `AddPoliPage(...)` with response helpers, an error handler (on by default) that answers SDK exceptions with the SDK's `ToPayload()` JSON body and status, and a `MapPoliPageSmokeTest` endpoint for post-deploy verification. You configure it through `IConfiguration` (`appsettings.json` + environment variables) and return rendered PDFs the same way you return JSON or a view.

**When to use this:**

- You want to return a generated PDF from a Minimal API or MVC controller with correct `Content-Type`, `Content-Disposition`, and cache headers.
- You want the Poli Page client autowired from DI and configured through `appsettings.json`.
- You want SDK exceptions to reach your clients as the same `{ code, message, status, requestId }` JSON every Poli Page integration answers.

**When not to:**

- You need to generate PDFs without a remote API — Poli Page is a hosted service.
- You're outside ASP.NET Core — pick the matching Poli Page package for your stack ([Laravel](https://packagist.org/packages/poli-page/laravel), [Symfony](https://packagist.org/packages/poli-page/symfony-bundle), [NestJS](https://www.npmjs.com/package/@poli-page/nestjs), [Next.js](https://www.npmjs.com/package/@poli-page/nextjs), [Rails](https://rubygems.org/gems/poli_page-rails), [Django](https://pypi.org/project/poli-page-django/), [Rocket](https://crates.io/crates/poli-page-rocket)).
- You're on classic ASP.NET (System.Web, .NET Framework). This package targets ASP.NET Core on `net8.0` / `net10.0`; .NET Framework users keep using the bare [`PoliPage`](https://www.nuget.org/packages/PoliPage) SDK.

## Requirements

- .NET 8.0 (LTS) or .NET 10.0 (LTS)
- ASP.NET Core 8 or 10
- The `PoliPage` SDK 0.11.0 or later (installed with this package): environment API keys, no template versions
- A Poli Page API key from [app.poli.page](https://app.poli.page)

## Install

```bash
dotnet add package PoliPage.AspNetCore
```

Or add the reference directly to your `.csproj`:

```xml
<ItemGroup>
  <!-- Brings in the PoliPage SDK, 0.11.0 or later. -->
  <PackageReference Include="PoliPage.AspNetCore" Version="0.1.0" />
</ItemGroup>
```

Add your API key to `appsettings.json` (or environment variables — see [Configuration](#configuration)):

```json
{
  "PoliPage": {
    "ApiKey": "pp_sandbox_your_key_here"
  }
}
```

Wire the integration in `Program.cs`:

```csharp
using PoliPage.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));
// AddPoliPageAspNetCore also turns on the Poli Page error handler: a PoliPageException is
// answered with its ToPayload() body and status (see "Errors").

var app = builder.Build();

app.UseExceptionHandler();           // for your other exceptions; Poli Page errors don't need it
app.UseStatusCodePages();            // optional but recommended for 4xx without a body

app.MapPoliPageSmokeTest()
   .RequireAuthorization("Operator");// or .AllowAnonymous() — see "Smoke testing"

app.MapControllers();

app.Run();
```

`AddPoliPageAspNetCore` also calls `services.AddProblemDetails()` (turn it off with `AddProblemDetailsService = false`), which a bare `app.UseExceptionHandler()` needs for the app's other exceptions.

## Quick start

### Minimal API

```csharp
using PoliPage;
using PoliPage.AspNetCore;
using PoliPage.AspNetCore.Results;

app.MapGet("/invoices/{id}.pdf", async (
    string id,
    PoliPageClient poliPage,
    CancellationToken cancellationToken) =>
{
    var pdf = await poliPage.Render.PdfAsync(
        new RenderInput
        {
            Project = "invoices",
            Template = "default",
            Data = new { InvoiceId = id },
        },
        cancellationToken: cancellationToken);

    return PoliPageResults.Pdf(pdf, $"invoice-{id}.pdf");
});
```

### MVC controller

```csharp
using Microsoft.AspNetCore.Mvc;
using PoliPage;
using PoliPage.AspNetCore;

public class InvoicesController(
    PoliPageClient poliPage,
    PoliPageResponseFactory responses) : ControllerBase
{
    [HttpGet("invoices/{id}.pdf")]
    public async Task<IActionResult> Show(string id, CancellationToken cancellationToken)
    {
        var pdf = await poliPage.Render.PdfAsync(
            new RenderInput
            {
                Project = "invoices",
                Template = "default",
                Data = new { InvoiceId = id },
            },
            cancellationToken: cancellationToken);

        return responses.Pdf(pdf, $"invoice-{id}.pdf");
    }
}
```

The two surfaces are equivalent: both set `Content-Type: application/pdf`, RFC 5987-encoded `Content-Disposition`, `Cache-Control: no-store, private`, and `X-Content-Type-Options: nosniff`.

### Smoke test the wiring

`app.MapPoliPageSmokeTest()` registers `GET /poli-page/smoke` against the well-known `getting-started/welcome` template — use it once after deployment to confirm credentials and network reachability:

```csharp
app.MapPoliPageSmokeTest().RequireAuthorization("Operator");

// curl -sS https://your-app.example.com/poli-page/smoke -o welcome.pdf
```

Every Poli Page organization gets a `getting-started` project with a `welcome` template, published into its `sandbox` environment, so the endpoint works with a sandbox key as soon as the key is valid. With a key of another environment, publish `getting-started` there first or the API answers `404 PROJECT_NOT_IN_ENVIRONMENT`. The package emits a one-time `Warning`-level log entry at startup if the endpoint is registered without `.RequireAuthorization(...)` **or** `.AllowAnonymous()` — every call burns one render against your quota, so production hosts should gate it.

## Configuration

`AddPoliPageAspNetCore` has three overloads. Pick the one that matches your config source.

```csharp
// 1. Pure code — no IConfiguration source.
builder.Services.AddPoliPageAspNetCore(opts =>
{
    opts.ApiKey = "pp_sandbox_…";
    opts.MaxRetries = 3;
});

// 2. Bind from IConfiguration (the common case).
builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));

// 3. Both — IConfiguration binding, then a code override. Runs before builder.Build(), so
//    it cannot use `app`; delegates such as OnRetry cannot come from appsettings.json.
builder.Services.AddPoliPageAspNetCore(
    builder.Configuration.GetSection("PoliPage"),
    configureClient: opts =>
    {
        opts.MaxRetries = 5; // wins over PoliPage:MaxRetries
        opts.OnRetry = e => Console.WriteLine($"PoliPage retry {e.Attempt}");
    });
```

### Option reference

| Option | Default | Source | Description |
|---|---|---|---|
| `PoliPage:ApiKey` | — (required) | `PoliPageClientOptions` | `pp_{env}_{32 lowercase hex}`; the key selects the environment (see [API keys and environments](#api-keys-and-environments)). Missing or empty fails startup. |
| `PoliPage:BaseUrl` | `https://api.poli.page` | `PoliPageClientOptions` | API endpoint. Override for staging or self-hosted. |
| `PoliPage:RequestTimeout` | `00:01:00` | `PoliPageClientOptions` | Per-request timeout (`TimeSpan` format: `"00:00:30"`). |
| `PoliPage:MaxRetries` | `2` | `PoliPageClientOptions` | Automatic retries on 429 / 5xx. Must be `≥ 0`. |
| `PoliPage:RetryDelay` | `00:00:00.500` | `PoliPageClientOptions` | Base delay between retries (`TimeSpan`). |
| `PoliPage:AspNetCore:RegisterExceptionHandler` | `true` | `PoliPageAspNetCoreOptions` | Turns on the error handler that answers a `PoliPageException` with its `ToPayload()` body (see [Errors](#errors)). `false` turns it off. |
| `PoliPage:AspNetCore:AddProblemDetailsService` | `true` | `PoliPageAspNetCoreOptions` | Calls `services.AddProblemDetails()`, which a bare `app.UseExceptionHandler()` needs. Poli Page errors do not use it. |
| `PoliPage:AspNetCore:DefaultCacheControl` | `no-store, private` | `PoliPageAspNetCoreOptions` | Default `Cache-Control` for response helpers. `null` omits the header. |
| `PoliPage:AspNetCore:SetNoSniffHeader` | `true` | `PoliPageAspNetCoreOptions` | Adds `X-Content-Type-Options: nosniff`. |

Validation runs at host startup via `ValidateOnStart` — a misconfigured app fails on `dotnet run`, not on the first SDK call.

### API keys and environments

An API key is `pp_{env}_{32 lowercase hex}`, where `{env}` names the one environment the key belongs to. Every organization starts with a `sandbox` and a `live` environment; Pro and above can create more (for example `staging`). There are no template versions: each environment holds one published copy of a project, and the key picks the environment.

| Environment                           | Key            | Watermark | Counted |
| ------------------------------------- | -------------- | --------- | ------- |
| `sandbox` (every org)                 | `pp_sandbox_…` | yes       | no      |
| `live` (every org, deletable on Pro+) | `pp_live_…`    | no        | yes     |
| created on Pro+ (e.g. `staging`)      | `pp_staging_…` | no        | yes     |

"Counted" means the render counts toward the organization's monthly PDF quota.

The API is the only authority on keys. At startup the integration only checks that `ApiKey` is present and not empty (startup fails otherwise). A key that does not match `pp_{env}_{32 lowercase hex}` (environment names: lowercase letters, digits and single inner hyphens, starting with a letter, 1 to 20 characters) logs one warning (`EventId` 3001), and the app starts anyway; the API then answers `401 INVALID_API_KEY` (or `401 MISSING_API_KEY` for a value without `pp_`). The warning names the expected format and shows no part of the key, not even the `pp_{env}_` prefix: a mis-pasted secret fragment can look like a valid environment name.

```json
// appsettings.json
{
  "PoliPage": {
    "ApiKey": "pp_sandbox_…",
    "RequestTimeout": "00:00:30",
    "MaxRetries": 3,
    "AspNetCore": {
      "DefaultCacheControl": "no-store, private"
    }
  }
}
```

Environment variables override `appsettings.json` via the colon-separated path convention. On Linux/macOS, replace `:` with `__`:

```
PoliPage__ApiKey=pp_live_…
PoliPage__BaseUrl=https://api-eu.poli.page
```

### Production secrets

The `appsettings.json` block above is illustrative — never commit a live API key to source control. In production, source `PoliPage:ApiKey` from one of:

- **Azure Key Vault** + `Azure.Extensions.AspNetCore.Configuration.Secrets`
- **Azure App Configuration** with feature-flag–style key references
- **AWS Secrets Manager** + `Kralizek.Extensions.Configuration.AWSSecretsManager`
- **HashiCorp Vault** + `VaultSharp.Extensions.Configuration`
- **Kubernetes Secret** mounted as an environment variable

All of these flow through `IConfiguration` automatically — `AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"))` picks up whichever source is registered first.

### Key rotation

`PoliPageClient` is a **singleton** built once at host startup. Rotating `PoliPage:ApiKey` in `appsettings.json` (or in Key Vault behind a sentinel-based reloader) updates `IOptionsMonitor<PoliPageClientOptions>` but does **not** rebuild the running client. Practical rotation paths:

- Blue/green deploy: standard. The new pod boots with the new key; traffic shifts; the old pod drains.
- `kubectl rollout restart deployment/your-api`: forces a pod restart, picking up the new key.

A live-reload story (subscribe to `IOptionsMonitor.OnChange`, swap the inner client behind a thread-safe accessor) is deferred to v0.2 — it requires SDK cooperation that doesn't exist yet.

### Behind a reverse proxy

If your app sits behind nginx, Envoy, an Azure Application Gateway, or an AWS ALB, register the standard ASP.NET Core forwarded-headers middleware first, **before** `app.UseExceptionHandler()`:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});
app.UseExceptionHandler();
```

With forwarded-headers in the right order, `Request.Scheme` and `Request.Host` (and every absolute URL built from them) are the public-facing ones the client actually hit, not the internal pod-level ones.

## API at a glance

| Symbol | Purpose |
|---|---|
| `PoliPage.PoliPageClient` | SDK client singleton. Inject by type. Exposes `Render` and `Documents`. |
| `PoliPage.AspNetCore.Results.PoliPageResults` | Static factory for Minimal API `IResult` values: `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`. |
| `PoliPage.AspNetCore.PoliPageResponseFactory` | DI-resolvable factory for MVC `IActionResult` values. Same four methods. |
| `PoliPage.AspNetCore.ApplicationBuilderExtensions.UsePoliPageExceptionHandler` | Places the error handler's middleware yourself (for example after `RegisterExceptionHandler = false`). Not needed with the default wiring. |
| `PoliPage.AspNetCore.EndpointRouteBuilderExtensions.MapPoliPageSmokeTest` | Registers a `GET /poli-page/smoke` endpoint that renders `getting-started/welcome`. |

Full reference: <https://poli-page.github.io/asp/>.

## Errors

`AddPoliPageAspNetCore` turns on an error handler by default. Every `PoliPageException` that leaves an endpoint, controller or middleware is answered with:

- **Status**: `exception.ToPayload().Status`: the API's status, `503` for `network_error`, `504` for `timeout`, and `500` when the SDK knows no status (for example `aborted`).
- **Body**: exactly `exception.ToPayload()`, as `application/json`. `status` and `requestId` are `null` when unknown.

```json
{
  "code": "TEMPLATE_NOT_FOUND",
  "message": "Template 'does-not-exist' not found in project 'getting-started'.",
  "status": 404,
  "requestId": "req_8f1c2d4e"
}
```

`code` is the API's error code verbatim (or the SDK's own code, such as `network_error`); `requestId` is the API's `x-request-id`, the value to quote to Poli Page support. A `PoliPageRateLimitException` that carries a `RetryAfter` also sets the `Retry-After` header. Every Poli Page framework integration answers with this same body, so a client can read Poli Page errors the same way whatever the backend.

The handler works whether or not the app calls `app.UseExceptionHandler()`. `AddPoliPageAspNetCore` registers it three ways, all answering the same: as an `IExceptionHandler` (which `UseExceptionHandler()` runs), as a developer exception page filter (in Development that page answers before any other middleware), and as a middleware at the start of the pipeline (an `IStartupFilter`). Exceptions that are not `PoliPageException` are left to the rest of your pipeline.

**Turning it off.** Set `RegisterExceptionHandler` to `false`:

```csharp
builder.Services.AddPoliPageAspNetCore(
    builder.Configuration.GetSection("PoliPage"),
    aspnet => aspnet.RegisterExceptionHandler = false);
```

or `"PoliPage": { "AspNetCore": { "RegisterExceptionHandler": false } }` in `appsettings.json`. A `PoliPageException` then reaches your own handling (ASP.NET Core's default is a `500`). To keep the Poli Page answer but choose where it sits in the pipeline, call `app.UsePoliPageExceptionHandler()` at that point.

Each answered error is logged once (`EventId` 1002, category `PoliPage.AspNetCore.ExceptionHandling.*`) with the exception: `Poli Page error {Code} answered with HTTP {Status} (request id {RequestId}).`, at `Error` for a 5xx answer and `Warning` otherwise. The line carries the code, status and request id, never the API key.

If an exception is thrown after the response has started (a stream that fails mid-copy), the status and headers can no longer change: the handler logs a warning (`EventId` 1001) and lets the exception through.

The SDK's exception types and the statuses they usually carry:

| Exception | Status | Typical `code` |
|---|---|---|
| `PoliPageAuthException` | 401 or 403 (the API's status) | `INVALID_API_KEY`, `MISSING_API_KEY`, `FORBIDDEN`, `ORGANIZATION_PAYMENT_GRACE`, `ENVIRONMENT_PROTECTED` |
| `PoliPageNotFoundException` | 404 | `PROJECT_NOT_FOUND`, `PROJECT_NOT_IN_ENVIRONMENT`, `TEMPLATE_NOT_FOUND`, `DOCUMENT_NOT_FOUND` |
| `PoliPageGoneException` | 410 | `DOCUMENT_GONE` |
| `PoliPageValidationException` | 400 or 422 | `VALIDATION_ERROR` |
| `PoliPageRateLimitException` | 429 | `RATE_LIMIT_EXCEEDED`, `QUOTA_EXCEEDED`, `OVERAGE_CAP_EXCEEDED` |
| `PoliPageNetworkException` | 503 | `network_error` |
| `PoliPageDownloadException` | the storage's status; 500 when there was none | `DOWNLOAD_FAILED` |
| `PoliPageException` | the API's status; 504 for `timeout`; 500 when none | any other code |

To answer one error family differently, catch it in the endpoint; everything else still goes to the handler:

```csharp
try
{
    var pdf = await poliPage.Render.PdfAsync(input, cancellationToken: ct);
    return PoliPageResults.Pdf(pdf, "invoice.pdf");
}
catch (PoliPageValidationException ex)
{
    // Validation message useful to surface back to the user.
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["template"] = new[] { ex.Message },
    });
}
catch (PoliPageRateLimitException ex)
{
    var retryAfter = ex.RetryAfter ?? TimeSpan.FromSeconds(5);
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}
```

The explicit `try`/`catch` is only needed when you want a custom response for one error family.

## Example app

A runnable Minimal API + MVC app at [`example-app/`](example-app/) demonstrates every public SDK method. `GET /` redirects to an interactive single-page dashboard at `/demo.html` that exercises render (`/render/pdf`, `/render/stream`), document lifecycle (`POST /documents`, `GET/DELETE /documents/{id}`, `/preview`, `/thumbnails`), and the typed-exception path (`/errors/not-found`: a real render of the `does-not-exist` template that the default error handler answers with `404` and the `ToPayload()` body, code `TEMPLATE_NOT_FOUND`).

```bash
cd example-app
POLI_PAGE_API_KEY=pp_sandbox_… dotnet run -f net10.0
```

Or drop `POLI_PAGE_API_KEY` (and optionally `POLI_PAGE_BASE_URL`) into a `.env` file above the app — the example app loads the nearest one at startup; real shell exports always win.

## Going further

- [`docs/responses.md`](docs/responses.md) — `PoliPageResults` vs `PoliPageResponseFactory`, header reference, RFC 5987 filename encoding.
- [`docs/minimal-apis.md`](docs/minimal-apis.md) — Minimal API patterns, route groups, `IResult` composition.
- [`docs/streaming.md`](docs/streaming.md) — streaming multi-megabyte PDFs without buffering, cancellation propagation.
- [`docs/testing.md`](docs/testing.md) — `WebApplicationFactory<TEntryPoint>`, swapping `PoliPageClient` with a fake via `IServiceCollection.Replace`, xUnit fixtures.

## Compatibility

| Package | .NET | ASP.NET Core |
|---|---|---|
| 0.1.x | 8.0 LTS / 10.0 LTS | 8.0 / 10.0 |

The package follows .NET's own LTS window. Non-LTS runtimes (9, 11) are best-effort but not in the CI matrix.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

**Contract tests.** `tests/PoliPage.AspNetCore.IntegrationTests/Contract` runs the integration cases (`impl.*`) of the [Poli Page API contract](https://github.com/poli-page/api-contract) against the example app, with its SDK pointed at a local mock server. CI runs them on every leg against the pinned contract (`poli-page/api-contract` action, v1.3.0). Locally: `POLI_CONTRACT=<path to contract.json> dotnet test tests/PoliPage.AspNetCore.IntegrationTests --filter "Category!=Integration"`; without `POLI_CONTRACT` they are skipped. `tests/contract-skip.json` lists the cases allowed to fail, each with its reason.

## License

Released under the [MIT License](LICENSE).
