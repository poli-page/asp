# Changelog

All notable changes to `PoliPage.AspNetCore` are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial release scaffolding.

## [0.1.0] — TBD

### Added
- `services.AddPoliPageAspNetCore(...)` — three overloads (callback / `IConfiguration` section / both). Composes the SDK's `AddPoliPage(...)` with this package's services.
- `PoliPageResults` — static class returning `IResult` for Minimal APIs: `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`. Sets `Content-Type`, RFC 5987-encoded `Content-Disposition`, `Cache-Control: no-store, private`, and `X-Content-Type-Options: nosniff`.
- `PoliPageResponseFactory` — DI-resolvable factory returning `IActionResult` for MVC controllers. Same four methods, same header guarantees.
- `app.UsePoliPageExceptionHandler()` — terminal middleware mapping `PoliPageException` (and subclasses) to RFC 7807 `ProblemDetails` JSON. Pass-through status for 4xx/5xx; `PoliPageConnectionException` → 502.
- `app.MapPoliPageSmokeTest("/poli-page/smoke")` — endpoint renderering `getting-started/welcome` for post-deploy verification.
- `IHealthChecksBuilder.AddPoliPage(...)` — health-check probe returning `Healthy` / `Degraded` / `Unhealthy` based on SDK response.
- `PoliPageAspNetCoreOptions` — `ProblemDetailsTypeUri`, `IncludeRequestIdInProblemDetails`, `DefaultCacheControl`, `SetNoSniffHeader`. Bindable from `IConfiguration` under `PoliPage:AspNetCore`.
- Options validation via `ValidateOnStart` — misconfigured hosts fail on `dotnet run`, not at first call.
- Example ASP.NET Core 10 app at `example-app/` covering all 10 SDK demo steps, with an interactive demo dashboard at `GET /` matching the symfony-bundle's aesthetic.
- CI matrix: `net8.0` / `net10.0` × Ubuntu/Windows + `net10.0` × macOS (5 cells).

### Notes
- `PoliPage` (the SDK) is referenced via Central Package Management at `>= 1.0.0 < 2.0.0`. While unpublished, `nuget.config` resolves from `../sdk-csharp/artifacts/package/release/`.
- ASP.NET Core 8 (LTS) and 10 (LTS) only. No `netstandard2.0`, no .NET Framework. .NET Framework consumers use the bare `PoliPage` SDK.
- The integration is structured to be AOT-compatible (System.Text.Json source generation, no `Reflection.Emit`), but the `<IsAotCompatible>` flag is deferred to v0.2.

[Unreleased]: https://github.com/poli-page/asp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/poli-page/asp/releases/tag/v0.1.0
