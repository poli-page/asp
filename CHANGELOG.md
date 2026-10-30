# Changelog

All notable changes to `PoliPage.AspNetCore` are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed (breaking)
- Requires `PoliPage` 0.11.0 (or later), the SDK's first published release with environment API keys: an API key is `pp_{env}_{32 lowercase hex}` and selects the environment (`pp_sandbox_…`, `pp_live_…`, or one created on Pro+ such as `pp_staging_…`); the old test-key prefix is gone. There are no template versions: inputs are `RenderInput` with no `Version`.
- The error handler answers a `PoliPageException` with the SDK's `ToPayload()` instead of RFC 7807 ProblemDetails: status `ToPayload().Status` (the API's status, `503` for `network_error`, `504` for `timeout`), or `500` when the SDK knows none; body exactly `{ "code", "message", "status", "requestId" }` as `application/json`, with `null` for an unknown status or request id. A rate-limit error also sets `Retry-After`. The same body as every other Poli Page framework integration.
- The error handler is on by default whether or not the app calls `app.UseExceptionHandler()`: `AddPoliPageAspNetCore` registers it as an `IExceptionHandler`, as a developer exception page filter, and as a middleware at the start of the pipeline. `PoliPageAspNetCoreOptions.RegisterExceptionHandler = false` (`PoliPage:AspNetCore:RegisterExceptionHandler`) turns it off; `app.UsePoliPageExceptionHandler()` still places the middleware explicitly.
- Removed `PoliPageAspNetCoreOptions.ProblemDetailsTypeUri` and `IncludeRequestIdInProblemDetails`, which only shaped the ProblemDetails answer.
- `MapPoliPageSmokeTest` renders `getting-started/welcome` without a version. The project is published into every organization's `sandbox` environment, so the smoke endpoint works out of the box with a sandbox key.
- Payment grace is `403 ORGANIZATION_PAYMENT_GRACE`, a `PoliPageAuthException` (the SDK dropped `PoliPagePaymentRequiredException`); `PoliPageGoneException` (410) carries `DOCUMENT_GONE`.
- `Microsoft.Extensions.Configuration.Binder` 10.0.9 → 10.0.13, matching the `Microsoft.Extensions.*` floor `PoliPage` 0.11.0 brings in (NU1605 otherwise).
- Example app: `GET /errors/not-found` answers through the default error handler: `404` with the `ToPayload()` body (code `TEMPLATE_NOT_FOUND`). The example app now targets `net8.0` and `net10.0`, like the library (`dotnet run -f net10.0`).

### Added
- API contract tests: the integration cases (`impl.*`) of the Poli Page API contract run against the example app in CI, on every OS and target framework, from the pinned `poli-page/api-contract` action (v1.3.0). `tests/contract-skip.json` lists the cases allowed to fail (none).
- Dependabot: GitHub Actions daily (the API contract in its own group, without the cooldown) and NuGet weekly.
- Startup warning when `ApiKey` does not match `pp_{env}_{32 lowercase hex}` (`EventId` 3001). The host still starts and the API stays the authority on keys; the warning names the expected format and shows no part of the key, not even the `pp_{env}_` prefix. A missing or empty key still fails startup.

### Removed
- Example app: `GET /render/preview` and `GET /invoices/{id}/preview` (the SDK dropped `Render.PreviewAsync`), with their dashboard button and README rows. `GET /documents/{id}/preview` (`Documents.PreviewAsync`) stays.
- Example app: `GET /errors/bad-version`, replaced by `GET /errors/not-found`, a real render of `getting-started` / `does-not-exist` with `{}`. The dashboard shows the error's `code`, `message`, `status` and `requestId`.

### Fixed
- `PoliPageResults.Pdf` / `PdfStream`: the `Content-Disposition` filename now has `\` escaped as well as `"` (a trailing backslash used to escape the closing quote), and control characters (CR/LF, TAB, DEL, C1) are stripped instead of surviving as `_` / `%0D%0A`. A filename made only of control characters throws `ArgumentException`, like an empty one.
- `PoliPageResponseFactory.Pdf` / `PdfStream`: control characters are stripped from `FileDownloadName`.
- Build: `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.303 (its `Microsoft.Build.Tasks.Git` 8.0.0 is flagged by GHSA-23fw-v26w-5fgq, which failed restore under `TreatWarningsAsErrors`).

### Changed
- `PoliPageResults.Pdf` / `PdfStream` now build `Content-Disposition` with ASP.NET Core's `ContentDispositionHeaderValue.SetHttpFileName` (the helper MVC's `FileResult` uses), so the Minimal API and MVC surfaces emit the same header: always the dual `filename=…; filename*=UTF-8''…` form, with the ASCII `filename` left unquoted when it is a plain token (`attachment; filename=invoice.pdf; filename*=UTF-8''invoice.pdf`).

## [0.1.0] — 2026-06-01

### Added
- `services.AddPoliPageAspNetCore(...)` — three overloads (callback / `IConfiguration` section / `IConfiguration` + callback). Composes the SDK's `AddPoliPage(...)` with this package's services; idempotent under both the symmetric "double `AddPoliPageAspNetCore`" call and the "SDK-first, then `AddPoliPageAspNetCore`" call.
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
