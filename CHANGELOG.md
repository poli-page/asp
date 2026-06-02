# Changelog

All notable changes to `PoliPage.AspNetCore` are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_No unreleased changes._

## [0.1.0] — 2026-06-01

### Added
- `services.AddPoliPageAspNetCore(...)` — three overloads (callback / `IConfiguration` section / `IConfiguration` + callback). Composes the SDK's `AddPoliPage(...)` with this package's services; idempotent under both the symmetric "double `AddPoliPageAspNetCore`" call and the "SDK-first, then `AddPoliPageAspNetCore`" call (see `CLAUDE.md §10.14`).
- `PoliPageResults` — static class returning `IResult` for Minimal APIs: `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`. Sets `Content-Type`, RFC 5987-encoded `Content-Disposition`, `Cache-Control: no-store, private`, and `X-Content-Type-Options: nosniff`. Every result also implements `IEndpointMetadataProvider` so OpenAPI generators (`Microsoft.AspNetCore.OpenApi`, Swashbuckle) auto-discover the status code and content type.
- `PoliPageResponseFactory` — DI-resolvable factory returning `IActionResult` for MVC controllers. Same four methods. Stays thin and delegates to MVC's built-in `FileContentResult`, `FileStreamResult`, `ContentResult`, `RedirectResult`.
- **`IExceptionHandler` primary path** — `AddPoliPageAspNetCore` auto-registers a `PoliPageExceptionHandler` that maps `PoliPageException` to RFC 7807 ProblemDetails via `IProblemDetailsService`. Activates when the host calls `app.UseExceptionHandler()`. Cooperates with `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` so a single response shape covers every error source.
- **`app.UsePoliPageExceptionHandler()`** — fallback middleware for hosts that do not use `app.UseExceptionHandler()`. Same mapping, AOT-safe `WriteAsJsonAsync` fallback when `IProblemDetailsService` is unavailable.
- ProblemDetails mapping for the SDK's actual exception classes: `PoliPageAuthException` (401), `PoliPagePaymentRequiredException` (402), `PoliPageNotFoundException` (404), `PoliPageGoneException` (410), `PoliPageValidationException` (400 or 422 depending on the SDK's `StatusCode`), `PoliPageRateLimitException` (429), `PoliPageNetworkException` (502), `PoliPageDownloadException` (502), generic `PoliPageException` (500). Extensions surface `code`, `poliPageRequestId`, `retryAfterSeconds` (for rate-limit), and `traceId`.
- `app.MapPoliPageSmokeTest("/poli-page/smoke")` — endpoint rendering `getting-started/welcome` for post-deploy verification. Logs a one-time `EventId 2001` warning at startup when the endpoint is registered without `.RequireAuthorization(...)` or `.AllowAnonymous()` — quota protection in production.
- `PoliPageAspNetCoreOptions` — `ProblemDetailsTypeUri`, `IncludeRequestIdInProblemDetails`, `DefaultCacheControl`, `SetNoSniffHeader`, `RegisterExceptionHandler`, `AddProblemDetailsService`. Bindable from `IConfiguration` under `PoliPage:AspNetCore`. The URI is validated as `http(s)://…` at host startup via `ValidateOnStart`.
- Example ASP.NET Core 10 app at `example-app/` covering every public SDK method through Minimal API + MVC endpoints. Interactive single-page dashboard at `/demo.html`. Workspace `.env` loader maps shell-style `POLI_PAGE_*` env vars into the `PoliPage:*` config tree the SDK options bind from.
- CI matrix: `net8.0` / `net10.0` × Ubuntu/Windows + `net10.0` × macOS (5 cells). Each step is gated by `hashFiles(...)` so a freshly-scaffolded repo is green from day one.

### Deferred to v0.2
- `IHealthChecksBuilder.AddPoliPage(...)` — the SDK does not yet expose a cheap `PingAsync` probe, and shipping a probe that renders the full welcome template per poll would burn API quota. Until then, the README documents an `IHttpClientFactory`-based health check probing the smoke endpoint URL.

### Notes
- `PoliPage` (the SDK) is referenced via Central Package Management at `>= 1.0.0 < 2.0.0`. While unpublished, `nuget.config` resolves from `../sdk-csharp/artifacts/package/release/`.
- ASP.NET Core 8 (LTS) and 10 (LTS) only. No `netstandard2.0`, no .NET Framework. .NET Framework consumers use the bare `PoliPage` SDK.
- `<IsAotCompatible>true</IsAotCompatible>` is set on `net10.0`; the ProblemDetails fallback path uses a `System.Text.Json` source-generated `JsonSerializerContext`. All logging routes through `[LoggerMessage]` source-gen.

[Unreleased]: https://github.com/poli-page/asp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/poli-page/asp/releases/tag/v0.1.0
