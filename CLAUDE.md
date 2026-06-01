# CLAUDE.md

> Instructions for Claude Code agents working in `poli-page/asp`.

## 1. Repo at a glance

| Field        | Value |
| ------------ | ----- |
| Repository   | `poli-page/asp` |
| Type         | Framework integration (ASP.NET Core package) |
| Language     | C# (latest; `net8.0` uses C# 12, `net10.0` uses C# 14) |
| .NET         | `net8.0` (LTS) + `net10.0` (LTS) |
| ASP.NET Core | 8.x and 10.x |
| Registry     | NuGet — `PoliPage.AspNetCore` |
| Depends on   | `PoliPage` (NuGet, `>= 1.0.0 < 2.0.0`); framework reference `Microsoft.AspNetCore.App` |
| Roadmap slot | P5.1 |

**Source-of-truth docs (read first):**
- `docs/spec/aspnet-core-specification.md` — full design spec for v0.1.0
- `docs/plan/2026-06-01-implementation.md` — implementation plan
- `/Users/mickael/Projects/INTEGRATIONS_PLAN.md` — cross-repo umbrella note, esp. §"Cross-cutting DX patterns"
- `/Users/mickael/Projects/sdk-csharp/` — the SDK this package wraps. Compare against `src/PoliPage/DependencyInjection/ServiceCollectionExtensions.cs` and `src/PoliPage/PoliPageClientOptions.cs` whenever you touch DI or options.
- `/Users/mickael/Projects/symfony-bundle/` — sister integration. `PoliPageResponseFactory` shape, RFC 5987 filename algorithm, and the example-app demo aesthetic copy across.
- `/Users/mickael/Projects/nestjs/` — sister integration. Module-options validation philosophy, exception-mapping status table, hooks-bridging stance copy across.

## 2. The package's job

This package is a **thin wrapper** around the official Poli Page .NET SDK (`PoliPage`, source at `/Users/mickael/Projects/sdk-csharp/`). It provides:

- `services.AddPoliPageAspNetCore(...)` — three overloads (callback / `IConfiguration` / both). Composes the SDK's `AddPoliPage(...)` with this package's services.
- `PoliPageResults` — static class returning `IResult` for Minimal APIs: `Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`.
- `PoliPageResponseFactory` — DI-resolvable class returning `IActionResult` for MVC controllers. Same four methods.
- `app.UsePoliPageExceptionHandler()` — terminal middleware mapping `PoliPageException` to RFC 7807 ProblemDetails JSON.
- `app.MapPoliPageSmokeTest("/poli-page/smoke")` — endpoint that renders `getting-started/welcome` for post-deploy verification.

**This package does NOT** reimplement HTTP transport, retries, error classification, idempotency keys, stream chunking, `IHttpClientFactory` wiring, options validation for SDK-owned options, or anything else the SDK already does. Bug in those areas? Fix it in `sdk-csharp`, not here.

**This package does NOT ship in v0.1**: a Swashbuckle / `Microsoft.AspNetCore.OpenApi` integration (defer to v0.2), an `IExceptionFilter` (MVC) variant of the middleware (the terminal middleware works for both MVC and Minimal APIs), Razor view-rendering helpers, a Polly resilience handler (the SDK already retries — stacking Polly causes double-retries), `PoliPage.AspNetCore.OpenTelemetry` (defer; the README shows manual `ActivitySource` wiring), or **`IHealthChecksBuilder.AddPoliPage(...)`** (deferred to v0.2 — the SDK has no `PingAsync` yet, see `docs/sdk-surface-audit-2026-06-01.md` §0.3; until then, the README shows an `IHttpClientFactory`-based probe against the smoke endpoint as a host-side workaround).

## 3. Working language

- **Code, comments, file names, commit messages, PR descriptions, repository documentation**: English.
- **Day-to-day conversation with Xavier/Mickael**: French, tutoiement.
- **Conversation in this Claude Code session**: French is fine for the chat; artifacts stay English.

## 4. TDD is mandatory

RED → GREEN → refactor for every change. Tests live in `tests/PoliPage.AspNetCore.Tests/` (mocked SDK via `FakePoliPageClient`, ~95% of the suite) and `tests/PoliPage.AspNetCore.IntegrationTests/` (one happy-path test against `api-develop.poli.page`, gated on `POLI_PAGE_API_KEY`).

### What to test (integration-specific!)

- **`AddPoliPageAspNetCore` overloads**: each registers `PoliPageClient` as a singleton resolvable by type, the `PoliPageResponseFactory` singleton, and both `IOptions<T>` bags. Same instance returned twice.
- **`IConfiguration` binding**: `PoliPage:ApiKey`, `PoliPage:MaxRetries`, `PoliPage:RequestTimeout` (`TimeSpan` string), `PoliPage:AspNetCore:ProblemDetailsTypeUri` all bind through the right overload.
- **Validation**: missing `ApiKey` (SDK level), malformed `ProblemDetailsTypeUri` (this package level) raise `OptionsValidationException` at `host.StartAsync()`.
- **`PoliPageResults` / `PoliPageResponseFactory`**: every method sets the right headers (`Content-Type`, RFC 5987 `Content-Disposition`, `Cache-Control: no-store, private` when default, `X-Content-Type-Options: nosniff` when default). ASCII and non-ASCII filenames both encode correctly.
- **`PoliPageExceptionHandlerMiddleware`**: status mapping for every exception family (see §10 of the spec for the table). `PoliPageException` thrown after `Response.HasStarted` is rethrown + warning logged. Non-`PoliPageException` bubbles unchanged.
- **`MapPoliPageSmokeTest`**: returns 200 + `application/pdf` when the (faked) SDK succeeds; throws → middleware → ProblemDetails when the SDK throws.
- **`HealthChecksBuilderExtensions.AddPoliPage`**: `Healthy` on success, `Unhealthy` on `PoliPageException`, `Degraded` on `PoliPageRateLimitException`.
- **`ContentDispositionHeader.Build`**: ASCII / non-ASCII / embedded quote / empty string cases.

### What NOT to test (the SDK already does)

- HTTP transport behaviour (`HttpClient`, `HttpClientFactory`, socket lifetime).
- Retry policy (backoff, max attempts, `Retry-After`, never-retry-4xx).
- 4xx / 5xx → `PoliPage*Exception` mapping inside the SDK.
- Idempotency-key generation.
- Stream chunking correctness.
- API contract drift — the SDK's contract tests own that.

Re-testing these here doubles maintenance. **If you find yourself writing a `WireMock.Net` server, stop — you're doing the SDK's job.**

## 5. Robustness over shortcuts

Mickael's hard rule (validated across symfony-bundle, laravel, nestjs sessions): **no hacks to make a test pass or a corner case go away**. Fix root causes. If a workaround is genuinely required (framework bug, SDK quirk), document it inline with a `// Why:` comment naming the constraint.

Concretely: do not disable analyzers per-file with `#pragma warning disable` to silence them, do not catch `Exception` to swallow a test failure, do not widen `PoliPageExceptionHandlerMiddleware`'s catch from `PoliPageException` to `Exception`, do not add `[Fact(Skip = "flaky")]` instead of fixing the root cause.

## 6. Code conventions

- **`<Nullable>enable</Nullable>`** + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props`. Pin in `Directory.Packages.props`.
- **Analyzers**: `Microsoft.CodeAnalysis.NetAnalyzers`, `Meziantou.Analyzer`, `Roslynator.Analyzers`. Same set as `sdk-csharp`. Pin versions via Central Package Management.
- **`dotnet format`** + `.editorconfig` enforced in CI.
- **`Async` suffix on every async method.** Standard BCL convention.
- **XML doc comments (`///`) on every `public` symbol.** They feed IntelliSense and the auto-generated reference at `poli-page.github.io/asp`. Do NOT use `[CanBeNull]`-style attributes — use BCL `?` annotation.
- **No commented-out code, no `TODO` without a linked issue, no `Console.WriteLine` / `Debug.WriteLine` debug prints in committed code.**
- **Default to no comments.** Add one only when the *why* is non-obvious. Comments restating *what* the code does are noise.
- **`internal sealed class`** for `IResult` implementations, middleware, and `IExceptionHandler`. The public API is `PoliPageResults.*` static methods, `services.AddPoliPageAspNetCore(...)`, `app.UsePoliPageExceptionHandler()` (fallback), and `app.MapPoliPageSmokeTest()` — implementation classes are not part of the contract.
- **No usings polluting consumers**: every public extension lives in a namespace the user has already imported (`Microsoft.AspNetCore.Builder` for `Use*` / `Map*`, `Microsoft.Extensions.DependencyInjection` for `Add*`). Match Microsoft's own conventions.
- **All logging via `[LoggerMessage]` source-gen.** Analyzer rule `CA1848` (and `LOG001` in the Roslynator set) is on; direct `logger.LogWarning(...)` calls fail the build. Define every log message as a `partial void` extension method in `Internal/LogMessages.cs` with a stable `EventId`. The source generator also elides the reflection-based formatter path that would otherwise break AOT.
- **`IResult` implementations also implement `IEndpointMetadataProvider`.** Without it, `Microsoft.AspNetCore.OpenApi` and Swashbuckle can't auto-discover that `PoliPageResults.Pdf(...)` produces `200 application/pdf`. The static `PopulateMetadata(MethodInfo, EndpointBuilder)` method goes on every concrete result class — see spec §8.1.

## 7. Commits and PRs

- **Conventional Commits**: `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`.
- **One concern per PR**, reviewable in under 30 minutes.
- PR description: what changed, why, how it was tested.
- CI must be green before merge.

## 8. CI

Workflow: `.github/workflows/ci.yml`. Matrix: `net8.0` / `net10.0` × Ubuntu/Windows + `net10.0` × macOS (5 cells). Each step auto-skips when the relevant project or test directory does not yet exist, so a freshly scaffolded repo is green from day one. Don't change that behaviour.

Local mirror:
```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --filter "Category!=Integration"
dotnet pack -c Release --no-build
```

When working in this repo:
- After adding `src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj`, the restore + build steps light up.
- After adding `.editorconfig`, the format step lights up.
- After adding the first test, the test step lights up.

## 9. Unpublished-SDK note

The .NET SDK is **not yet on NuGet**. We use `nuget.config` + a local source pointing at `../sdk-csharp/artifacts/package/release/`. See `docs/spec/aspnet-core-specification.md` §12 for the full workaround. Same intent as the symfony-bundle's Composer Merge Plugin and the nestjs npm-workspaces trick — local-only, removable in one PR when the SDK ships.

When the SDK eventually publishes:
1. `git rm nuget.config`
2. Bump `<PackageVersion Include="PoliPage" Version="1.0.0" />` in `Directory.Packages.props`.
3. `dotnet restore` resolves from nuget.org.
4. Tag v0.1.0 of `PoliPage.AspNetCore`.

**Package source code is untouched** by this transition — only the dev environment is.

## 10. Known gotchas (battle-tested — don't relearn the hard way)

These surfaced in sister integrations or are ASP.NET-specific. Recorded so future agents do not burn a session rediscovering them.

### 10.1 Don't stack Polly on the named `HttpClient`

The SDK's `AddPoliPage` registers two named `HttpClient`s (`"PoliPage"`, `"PoliPage.Download"`) and implements retries **inside the client** — not via a `DelegatingHandler`. Adding `.AddStandardResilienceHandler()` or `.AddPolicyHandler(...)` to the named client **stacks a second retry budget on top of the SDK's**, doubling the effective retry count and breaking `Retry-After` semantics. The README and `docs/spec` §1 say so explicitly. If a user reports "retries seem to take forever", check whether they've layered Polly.

### 10.2 `PoliPageExceptionHandlerMiddleware` only catches `PoliPageException`

The middleware is **narrow on purpose**. Any other throw bubbles up to ASP.NET Core's `UseExceptionHandler` / developer exception page. This matches the nestjs spec §10 and symfony-bundle's "exceptions propagate" choice. **Do NOT** widen to `catch (Exception)` — generic exception swallowing destroys observability.

### 10.3 `Response.HasStarted` is a real concern in streaming endpoints

When a Minimal API handler returns `PoliPageResults.PdfStream(stream, ...)` and the SDK throws *mid-stream* (e.g., a `PoliPageConnectionException` during the body copy), the response has already begun streaming and the middleware **cannot rewrite headers**. The middleware checks `httpContext.Response.HasStarted` and rethrows + logs a warning when true. **Do NOT** try to be clever and "buffer the stream until success". The whole point of `PdfStream` is to avoid buffering. Document this in `docs/streaming.md`.

### 10.4 `WebApplicationFactory` requires a public `Program` class

For tests using `WebApplicationFactory<Program>` to compile, the example app's `Program.cs` (top-level statements) must end with `public partial class Program { }` — see `docs/testing.md`. The pattern is documented in Microsoft's own testing guidance; don't replace it with `[InternalsVisibleTo]` hacks.

### 10.5 Single root `.env`, no per-app `.env.local`

The example app's `Program.cs` reads the workspace root `.env` (`/Users/mickael/Projects/.env`) at startup and pushes values into `IConfiguration` only when not already set via real environment variables. Real shell exports always win. The hand-rolled parser lives at `example-app/Scripts/PoliPageWorkspaceEnvFile.cs` and is the same convention used in symfony-bundle / nestjs.

**Do NOT** introduce a per-app `.env.local` or instruct users to `cp .env .env.local`. This was an explicit hard requirement from Mickael during the symfony-bundle session and reaffirmed in every sister integration. See `INTEGRATIONS_PLAN.md` §"Cross-cutting DX patterns" §2.

### 10.6 The interactive demo UI is mandatory, not optional

`GET /` in the example app redirects to `/demo.html` — a single-page dashboard with one button per SDK feature, inline `<iframe>` previews, JSON pretty-print, and a document-lifecycle state machine in client JS. Aesthetic copied from `/Users/mickael/Projects/symfony-bundle/example-app/templates/demo.html` (white surface, indigo `#4f5d99`, Manrope + IBM Plex Sans + JetBrains Mono). It's served as a static file from `wwwroot/` — no Razor, no Razor Pages, no Blazor. See `INTEGRATIONS_PLAN.md` §"Cross-cutting DX patterns" §1 for the bar.

### 10.7 xUnit, not NUnit / MSTest

`sdk-csharp` is on xUnit + FluentAssertions, and the broader .NET test ecosystem assumes one of the two. **Do NOT** swap to NUnit "because the test team prefers it" — that's a deviation Mickael hasn't approved. See spec §14.1.

### 10.8 Central Package Management (CPM) is mandatory

All package versions are pinned in `Directory.Packages.props`. Do not add a `Version="..."` attribute on individual `<PackageReference>` elements — that disables CPM for the entire project and triggers analyzer warnings. Pin in the central file; reference by id only in `.csproj` files. Same convention as `sdk-csharp`.

### 10.9 `IResult` implementations live in `internal sealed class`es

`PoliPageResults.Pdf(...)` is the public API. The returned object (`PdfResult`) is an internal implementation detail. Do NOT expose it as a public type — that locks us into an API contract on the result class shape, which constrains future refactors (e.g., switching to `Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult` under the hood when AOT becomes a v0.2 priority).

### 10.10 No `Newtonsoft.Json` anywhere

`System.Text.Json` everywhere. The ProblemDetails serialization uses a source-generated `JsonSerializerContext` and the project carries `<IsAotCompatible>true</IsAotCompatible>` from v0.1 — adding `Newtonsoft.Json` breaks the AOT path and adds a 1MB transitive dep for no reason.

### 10.11 `IExceptionHandler` is the primary path; `UsePoliPageExceptionHandler()` is the fallback

`AddPoliPageAspNetCore` auto-registers `PoliPageExceptionHandler` as `IExceptionHandler` (toggle: `PoliPageAspNetCoreOptions.RegisterExceptionHandler`, default `true`). Hosts wire it via the standard `app.UseExceptionHandler()` — the same call they'd make for any other registered handler. **Do NOT call both** `app.UseExceptionHandler()` **and** `app.UsePoliPageExceptionHandler()` — whichever runs first wins, and the order subtly changes the `IProblemDetailsService` cooperation path. The middleware exists only for hosts that don't use `UseExceptionHandler()` at all. See spec §10.3.

### 10.12 Forwarded headers and `ProblemDetails.Instance`

`PoliPageProblemDetailsFactory` sets `ProblemDetails.Instance` from `httpContext.Request.Path + QueryString`. Behind a reverse proxy that doesn't strip its own path prefix (e.g., a misconfigured `X-Forwarded-Prefix` header), the path is the **internal** path, not the public one. If the host uses `app.UseForwardedHeaders(...)` upstream of `app.UseExceptionHandler()`, the path will already be rewritten by the time our handler runs — no special handling needed. **Verify ordering** in any host running behind nginx/Envoy/ALB and add an integration test that asserts `Instance` is the public path.

### 10.13 `IOptionsMonitor<PoliPageClientOptions>` does NOT rebuild the client on reload

The SDK snapshots options at singleton construction. `appsettings.json` reload, Azure App Configuration sentinel triggers, or Key Vault rotation all update `IOptionsMonitor<PoliPageClientOptions>` but the running `PoliPageClient` keeps the old `ApiKey` until the process restarts. **Do NOT** ship a "feature" that subscribes to `IOptionsMonitor.OnChange` and tries to rebuild the singleton — the SDK is not currently safe for that without cooperation, and dropping the inner `HttpClient` mid-request would crash inflight calls. Document the restart-required path in `docs/spec/aspnet-core-specification.md` §6.5 and call it a v0.2 item if users push back.

### 10.14 Double-registration is guarded — don't break the guard

`AddPoliPageAspNetCore` short-circuits if `PoliPageResponseFactory` is already registered. This protects against two patterns: (a) the same call made twice by composed `AddX()` extensions, and (b) a user who reads the SDK README first and writes `services.AddPoliPage(...)` themselves before calling `services.AddPoliPageAspNetCore(...)`. **The guard does NOT protect the reverse order** — calling `services.AddPoliPage(...)` *after* `services.AddPoliPageAspNetCore(...)` double-registers the SDK client (last-wins on resolve, but the `Configure(...)` callbacks both run). Document this in the README's "Configuration" section. Do not try to "fix" it by removing the marker check — the marker prevents the symmetric case and is correct.

### 10.15 `IEndpointMetadataProvider` is required on every `IResult` we ship

Without `IEndpointMetadataProvider.PopulateMetadata`, OpenAPI generators can't infer that `PoliPageResults.Pdf(...)` returns `200 application/pdf`. Adding `.Produces(...)` to every endpoint manually is the workaround users *had* to do before .NET 9 introduced the metadata provider hook. We ship it — every concrete `IResult` class (`PdfResult`, `PdfStreamResult`, `PreviewResult`, `DocumentRedirectResult`) has a static `PopulateMetadata` method. **Adding a new result class without this method is a regression**; the analyzer doesn't catch it, but the integration test in `tests/.../Results/OpenApiMetadataTests.cs` does — keep that test in place.

## 11. When stuck

- Re-read `docs/spec/aspnet-core-specification.md` first; most "open questions" are answered there or in §18 "Resolved decisions".
- Compare with the SDK reference at `/Users/mickael/Projects/sdk-csharp/`.
- Compare with `/Users/mickael/Projects/nestjs/` (same DI shape, different language) and `/Users/mickael/Projects/symfony-bundle/` (same response-factory shape, different language).
- Compare patterns with `Sentry.AspNetCore`, `Serilog.AspNetCore`, `Hangfire.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer` (the package's industry benchmarks).
- Ask Mickael early. A two-line message is faster than a half-day rebuilding the wrong thing.
- If a CI failure looks unrelated to your change, check `main` first before assuming you caused it.
