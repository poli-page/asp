# Poli Page for ASP.NET Core

> Render Poli Page documents as ASP.NET Core HTTP responses.

[![CI](https://github.com/poli-page/asp/actions/workflows/ci.yml/badge.svg)](https://github.com/poli-page/asp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PoliPage.AspNetCore.svg)](https://www.nuget.org/packages/PoliPage.AspNetCore)
[![NuGet downloads](https://img.shields.io/nuget/dt/PoliPage.AspNetCore.svg)](https://www.nuget.org/packages/PoliPage.AspNetCore)
[![License](https://img.shields.io/github/license/poli-page/asp)](LICENSE)

## About

This package wraps the Poli Page .NET SDK as an ASP.NET Core-native integration: a one-line DI registration that composes the SDK's `AddPoliPage(...)` with response helpers, terminal exception middleware that maps SDK exceptions to RFC 7807 `ProblemDetails`, a `MapPoliPageSmokeTest` endpoint for post-deploy verification, and a health-check probe for `Microsoft.Extensions.Diagnostics.HealthChecks`. You configure it through `IConfiguration` (`appsettings.json` + environment variables) and return rendered PDFs the same way you return JSON or a view.

**When to use this:**

- You want to return a generated PDF from a Minimal API or MVC controller with correct `Content-Type`, `Content-Disposition`, and cache headers.
- You want the Poli Page client autowired from DI and configured through `appsettings.json`.
- You want SDK exceptions to surface as ProblemDetails JSON consistent with the rest of your error contract.
- You want a one-line `IHealthChecksBuilder.AddPoliPage()` probe.

**When not to:**

- You need to generate PDFs without a remote API — Poli Page is a hosted service.
- You're outside ASP.NET Core — pick the matching Poli Page package for your stack ([Laravel](https://packagist.org/packages/poli-page/laravel), [Symfony](https://packagist.org/packages/poli-page/symfony-bundle), [NestJS](https://www.npmjs.com/package/@poli-page/nestjs), [Next.js](https://www.npmjs.com/package/@poli-page/nextjs), [Rails](https://rubygems.org/gems/poli_page-rails), [Django](https://pypi.org/project/poli-page-django/), [Rocket](https://crates.io/crates/poli-page-rocket)).
- You're on classic ASP.NET (System.Web, .NET Framework). This package targets ASP.NET Core on `net8.0` / `net10.0`; .NET Framework users keep using the bare [`PoliPage`](https://www.nuget.org/packages/PoliPage) SDK.

## Requirements

- .NET 8.0 (LTS) or .NET 10.0 (LTS)
- ASP.NET Core 8 or 10
- A Poli Page API key from [app.poli.page](https://app.poli.page)

## Install

```bash
dotnet add package PoliPage.AspNetCore
```

Or add the reference directly to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="PoliPage.AspNetCore" Version="0.1.0" />
</ItemGroup>
```

Add your API key to `appsettings.json` (or environment variables — see [Configuration](#configuration)):

```json
{
  "PoliPage": {
    "ApiKey": "pp_test_your_key_here"
  }
}
```

Wire the integration in `Program.cs`:

```csharp
using PoliPage.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));
// AddPoliPageAspNetCore auto-registers AddProblemDetails() and an IExceptionHandler
// (PoliPageExceptionHandler) that maps PoliPageException to ProblemDetails JSON.

var app = builder.Build();

app.UseExceptionHandler();           // runs every registered IExceptionHandler — including ours
app.UseStatusCodePages();            // optional but recommended for 4xx without a body

app.MapPoliPageSmokeTest()
   .RequireAuthorization("Operator");// or .AllowAnonymous() — see "Smoke testing"

app.MapControllers();

app.Run();
```

**Do not** call `app.UsePoliPageExceptionHandler()` in addition to `app.UseExceptionHandler()` — the former is a fallback for hosts that don't use the latter; running both makes ordering subtle and breaks `IProblemDetailsService` cooperation. Pick one.

## Quick start

### Minimal API

```csharp
using PoliPage;
using PoliPage.AspNetCore;

app.MapGet("/invoices/{id}.pdf", async (
    string id,
    PoliPageClient poliPage,
    CancellationToken cancellationToken) =>
{
    var pdf = await poliPage.Render.PdfAsync(
        new ProjectModeInput
        {
            Project = "invoices",
            Template = "default",
            Version = "1.0.0",
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
            new ProjectModeInput
            {
                Project = "invoices",
                Template = "default",
                Version = "1.0.0",
                Data = new { InvoiceId = id },
            },
            cancellationToken: cancellationToken);

        return responses.Pdf(pdf, $"invoice-{id}.pdf");
    }
}
```

The two surfaces are equivalent: both set `Content-Type: application/pdf`, RFC 5987-encoded `Content-Disposition`, `Cache-Control: no-store, private`, and `X-Content-Type-Options: nosniff`.

## Smoke testing

`MapPoliPageSmokeTest` registers an endpoint that renders the well-known `getting-started/welcome` template and returns the PDF inline. Use it once after deployment to verify wiring.

```csharp
app.MapPoliPageSmokeTest("/poli-page/smoke")
   .RequireAuthorization("Operator");
```

```bash
curl -sS https://your-app.example.com/poli-page/smoke -o welcome.pdf
```

Every Poli Page org comes pre-provisioned with `getting-started/welcome`, so the endpoint works as soon as the API key is valid.

> **Production warning**: every `GET /poli-page/smoke` call burns one render against your API quota. If you register the endpoint without `.RequireAuthorization(...)` **and** without `.AllowAnonymous()`, the package emits a one-time startup warning at the `Warning` log level. In production, gate the endpoint behind auth or wrap it in an `app.Environment.IsDevelopment()` check.

## Configuration

`AddPoliPageAspNetCore` has three overloads. Pick the one that matches your config source.

```csharp
// 1. Pure code — no IConfiguration source.
builder.Services.AddPoliPageAspNetCore(opts =>
{
    opts.ApiKey = "pp_test_x";
    opts.MaxRetries = 3;
});

// 2. Bind from IConfiguration (the common case).
builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));

// 3. Both — IConfiguration binding, then a code override.
builder.Services.AddPoliPageAspNetCore(
    builder.Configuration.GetSection("PoliPage"),
    configureClient: opts =>
    {
        opts.OnRetry = e => app.Logger.LogWarning("PoliPage retry {Attempt}", e.Attempt);
    });
```

### Option reference

| Option | Default | Source | Description |
|---|---|---|---|
| `PoliPage:ApiKey` | — (required) | `PoliPageClientOptions` | Must start with `pp_test_` or `pp_live_`. |
| `PoliPage:BaseUrl` | `https://api.poli.page` | `PoliPageClientOptions` | API endpoint. Override for staging or self-hosted. |
| `PoliPage:RequestTimeout` | `00:01:00` | `PoliPageClientOptions` | Per-request timeout (`TimeSpan` format: `"00:00:30"`). |
| `PoliPage:MaxRetries` | `2` | `PoliPageClientOptions` | Automatic retries on 429 / 5xx. Must be `≥ 0`. |
| `PoliPage:RetryDelay` | `00:00:00.500` | `PoliPageClientOptions` | Base delay between retries (`TimeSpan`). |
| `PoliPage:AspNetCore:ProblemDetailsTypeUri` | `https://poli.page/errors` | `PoliPageAspNetCoreOptions` | Root URI for ProblemDetails `type`; the error code is appended as a fragment. |
| `PoliPage:AspNetCore:IncludeRequestIdInProblemDetails` | `true` | `PoliPageAspNetCoreOptions` | Adds `poliPageRequestId` to ProblemDetails extensions. |
| `PoliPage:AspNetCore:DefaultCacheControl` | `no-store, private` | `PoliPageAspNetCoreOptions` | Default `Cache-Control` for response helpers. `null` omits the header. |
| `PoliPage:AspNetCore:SetNoSniffHeader` | `true` | `PoliPageAspNetCoreOptions` | Adds `X-Content-Type-Options: nosniff`. |

Validation runs at host startup via `ValidateOnStart` — a misconfigured app fails on `dotnet run`, not on the first SDK call.

```json
// appsettings.json
{
  "PoliPage": {
    "ApiKey": "pp_test_x",
    "RequestTimeout": "00:00:30",
    "MaxRetries": 3,
    "AspNetCore": {
      "ProblemDetailsTypeUri": "https://errors.acme.example/poli-page"
    }
  }
}
```

Environment variables override `appsettings.json` via the colon-separated path convention. On Linux/macOS, replace `:` with `__`:

```
PoliPage__ApiKey=pp_live_x
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
- Per-request override: pass a fresh `ApiKey` in `RequestOptions` on the SDK call site. Works without a restart but only for code paths you control.

A live-reload story (subscribe to `IOptionsMonitor.OnChange`, swap the inner client behind a thread-safe accessor) is deferred to v0.2 — it requires SDK cooperation that doesn't exist yet.

### Behind a reverse proxy

If your app sits behind nginx, Envoy, an Azure Application Gateway, or an AWS ALB, register the standard ASP.NET Core forwarded-headers middleware **before** `app.UseExceptionHandler()`:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});
app.UseExceptionHandler();
```

The `Instance` field in our ProblemDetails responses uses `Request.Path` + `QueryString`. With forwarded-headers in the right order, that path is the public-facing URL the client actually hit, not the internal pod-level path.

## API at a glance

| Symbol | Purpose |
|---|---|
| `PoliPage.PoliPageClient` | SDK client singleton. Inject by type. Exposes `Render` and `Documents`. |
| `PoliPage.AspNetCore.PoliPageResults` | Static factory for Minimal API `IResult` values: `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`. |
| `PoliPage.AspNetCore.PoliPageResponseFactory` | DI-resolvable factory for MVC `IActionResult` values. Same four methods. |
| `PoliPage.AspNetCore.PoliPageExceptionHandlerMiddleware` | Terminal middleware mapping `PoliPageException` to ProblemDetails JSON. Registered via `app.UsePoliPageExceptionHandler()`. |
| `PoliPage.AspNetCore.EndpointRouteBuilderExtensions.MapPoliPageSmokeTest` | Registers a `GET /poli-page/smoke` endpoint that renders `getting-started/welcome`. |
| `PoliPage.AspNetCore.HealthChecksBuilderExtensions.AddPoliPage` | `IHealthChecksBuilder` extension for `Microsoft.Extensions.Diagnostics.HealthChecks`. |

Full reference: <https://poli-page.github.io/asp/>.

## Errors

The SDK throws six families of exceptions. They all extend `PoliPage.PoliPageException`. With the default wiring (`AddPoliPageAspNetCore(...)` + `app.UseExceptionHandler()`), they map to `ProblemDetails` JSON via `IProblemDetailsService` — meaning any `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callback you register also runs on our responses, so the JSON shape stays consistent across **all** error sources in your app (validation 422, framework 404, `PoliPageException`, …):

| Exception | Status | `code` |
|---|---|---|
| `PoliPageAuthenticationException` | 401 | `authentication_failed` |
| `PoliPageBadRequestException` | 400 | `bad_request` |
| `PoliPageNotFoundException` | 404 | `not_found` |
| `PoliPageRateLimitException` | 429 | `rate_limited` |
| `PoliPageConnectionException` | 502 | `upstream_unavailable` |
| `PoliPageException` (catch-all) | from `StatusCode` or 502 | `poli_page_error` |

```csharp
try
{
    var pdf = await poliPage.Render.PdfAsync(input, cancellationToken: ct);
    return PoliPageResults.Pdf(pdf, "invoice.pdf");
}
catch (PoliPageBadRequestException ex)
{
    // Validation message useful to surface back to the user.
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["template"] = new[] { ex.Message },
    });
}
catch (PoliPageRateLimitException)
{
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}
```

You can also let the middleware handle everything — the explicit `try`/`catch` is only needed when you want a custom response for one error family.

## Health checks

```csharp
builder.Services.AddHealthChecks()
    .AddPoliPage(name: "poli-page", tags: new[] { "ready" });

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
});
```

The probe issues a cheap GET against the SDK; `Healthy` on 2xx, `Unhealthy` on `PoliPageException`, `Degraded` on `PoliPageRateLimitException` (rate-limited but reachable).

## Example app

A runnable Minimal API + MVC app at [`example-app/`](example-app/) demonstrates every public SDK method through endpoints, an interactive single-page dashboard at `GET /`, and a `dotnet run -- render-to-file` CLI branch.

```bash
cd example-app
dotnet run
```

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

## License

Released under the [MIT License](LICENSE).
