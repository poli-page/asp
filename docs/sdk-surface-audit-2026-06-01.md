# SDK surface audit — 2026-06-01

> Audit performed as Task 0 of `docs/plan/2026-06-01-implementation.md`. Confirms (or refutes) the three load-bearing SDK assumptions in that plan before any source code lands. No source code in this PR — output is this report and the plan amendments it triggers.
>
> SDK audited: `/Users/mickael/Projects/sdk-csharp/` @ `main` (`d74ec9f` — *fix(api): align endpoint paths with /v1 prefix and adopt the two-step PDF model*).

## 0.1 Exception class names — 🟡 Yellow

The plan's mapping switch (spec §10.2 / plan Task 12.2) names six exception classes with a constructor signature of `(string message, string? errorCode, string? requestId)`. Reality differs on names, on the ctor shape, **and** on the set of exceptions the integration must map.

| Plan expected                            | Actual class on `main`                 | HTTP statuses covered | Notes |
| ---------------------------------------- | -------------------------------------- | --------------------- | ----- |
| `PoliPageAuthenticationException` (401)  | `PoliPageAuthException`                | 401, 403              | Renamed; covers Forbidden too. |
| `PoliPageBadRequestException` (400)      | `PoliPageValidationException`          | 400, 422              | Renamed; covers 422 too. Default error code is `VALIDATION`, default status `422`. |
| `PoliPageNotFoundException` (404)        | `PoliPageNotFoundException`            | 404                   | ✅ unchanged. |
| `PoliPageRateLimitException` (429)       | `PoliPageRateLimitException`           | 429                   | ✅ unchanged. Adds a `RetryAfter` (`TimeSpan?`) property — the integration's ProblemDetails extensions should surface it under `retryAfterSeconds`. |
| `PoliPageConnectionException` (no status)| `PoliPageNetworkException`             | `StatusCode == 0`     | Renamed. Default error code is `NETWORK`. |
| `PoliPageException` (root)               | `PoliPageException`                    | varies                | ✅ unchanged. |
| —                                        | `PoliPageGoneException`                | 410                   | **Extra** — soft-deleted resource. Non-retryable. |
| —                                        | `PoliPagePaymentRequiredException`     | 402                   | **Extra** — account balance owed. |
| —                                        | `PoliPageDownloadException`            | 0 or storage status   | **Extra** — presigned-download failure (S3/CDN). |

Constructor shape is also off. Actual public ctor:

```csharp
public PoliPageException(string code, int statusCode, string message,
                         string? requestId = null, Exception? innerException = null)
```

…not `(string message, string? errorCode, string? requestId)`. The error-code accessor is `Code`, **not** `ErrorCode`. `StatusCode` (`int`) and `RequestId` (`string?`) properties both exist as expected.

**Plan amendment required (before Task 12):**

1. Rewrite the Task 12.2 mapping switch against the actual class set:

   ```csharp
   var (status, errorCode) = ex switch
   {
       PoliPageAuthException             => (StatusCodes.Status401Unauthorized,     ex.Code),
       PoliPagePaymentRequiredException  => (StatusCodes.Status402PaymentRequired,  ex.Code),
       PoliPageNotFoundException         => (StatusCodes.Status404NotFound,         ex.Code),
       PoliPageGoneException             => (StatusCodes.Status410Gone,             ex.Code),
       PoliPageValidationException       => (ex.StatusCode == 400
                                                ? StatusCodes.Status400BadRequest
                                                : StatusCodes.Status422UnprocessableEntity, ex.Code),
       PoliPageRateLimitException        => (StatusCodes.Status429TooManyRequests,  ex.Code),
       PoliPageNetworkException          => (StatusCodes.Status502BadGateway,       ex.Code),
       PoliPageDownloadException         => (StatusCodes.Status502BadGateway,       ex.Code),
       _                                 => (StatusCodes.Status500InternalServerError, ex.Code),
   };
   ```

2. Update Task 12.7's theory rows to use the renamed classes and the actual ctor signature `new PoliPageAuthException(PoliPageErrorCode.Unauthorized, 401, "msg", requestId: "req_1")`.
3. Add ProblemDetails extension surfacing for `PoliPageRateLimitException.RetryAfter` — already a roadmap-item-worthy nicety.
4. In `PoliPageProblemDetailsFactory`, read `ex.Code` (not `ex.ErrorCode`) and append it to `ProblemDetailsTypeUri` as the fragment.

## 0.2 `PoliPageClient` extensibility — 🟡 Yellow, recoverable in-plan

The plan's test strategy assumes `FakePoliPageClient : PoliPageClient` is buildable. Reality:

- `PoliPageClient` is **`public sealed class`** — cannot be subclassed.
- `Render` is **`public sealed class`** with an `internal Render(...)` constructor — cannot be subclassed or directly instantiated by tests.
- `Documents` is **`public sealed class`** with an `internal Documents(...)` constructor — same.
- All render/document methods (`Render.PdfAsync`, `Render.PdfStreamAsync`, `Render.PreviewAsync`, `Render.DocumentAsync`, `Documents.GetAsync`, `Documents.DeleteAsync`, `Documents.PreviewAsync`, `Documents.ThumbnailsAsync`) are `public async Task<…>` (not `virtual`).
- Note: the plan's `Documents.CreateAsync` does not exist — the closest equivalent is `Render.DocumentAsync(...)` which returns a `DocumentDescriptor`. Adjust Task 13.2 fixture wording accordingly.

The plan's option 0.2(1) (SDK PR to unseal `PoliPageClient` + add `virtual`) is technically possible but a heavy ask — it commits the SDK to a public-virtual contract on the render methods, which constrains future refactors and contradicts BCL conventions (`HttpClient`, `JsonSerializer`, etc. are sealed for the same reason). Option 0.2(2) (interface inversion via test-only `IFakeablePoliPage`) introduces a public abstraction this package would have to expose to consumers — also undesirable.

**Recommended recovery (third path, simpler than either plan option):** drop the `FakePoliPageClient` subclass strategy entirely. The vast majority of tests in this package don't need a faked SDK at all:

- **Response helpers** (`PoliPageResults.Pdf`, `PdfStream`, `Preview`, `DocumentRedirect`) take raw inputs (`byte[]`, `Stream`, `string`, `DocumentDescriptor`) — feed them directly in tests, no SDK call involved.
- **Header formatting** (`ContentDispositionHeader.Build`) is a pure function — no SDK call involved.
- **`PoliPageExceptionHandler` and middleware** are driven by throwing `PoliPageException` instances directly into a `DefaultHttpContext` — no SDK call involved.
- **OpenAPI metadata** (`IEndpointMetadataProvider.PopulateMetadata`) is exercised against a `TestEndpointRouteBuilder` — no SDK call involved.

For the small subset of end-to-end tests that exercise `WebApplicationFactory<Program>` + a handler that calls the real SDK, inject a stubbed `HttpMessageHandler` via `PoliPageClientOptions.HttpClient` **and** `PoliPageClientOptions.DownloadHttpClient` (both are already public extension points on the SDK options record). A ~20-line `DelegatingHandler` returning canned descriptor JSON + canned PDF bytes is sufficient. This is **not** a WireMock-style SDK retest (which CLAUDE.md §4 explicitly forbids) — we're driving one known happy-path response so the ASP.NET layer above can be exercised, not asserting on the SDK's retry/timeout/error-classification behaviour.

**Plan amendment required (before Task 13):** rename the `FakePoliPageClient` fixture to `StubPoliPageHttpHandler : DelegatingHandler`, place it under `tests/PoliPage.AspNetCore.Tests/Fixtures/`, and document in Task 13's preamble that it returns a fixed descriptor + PDF body for any incoming request. Spec §14 reads "mocked SDK via `FakePoliPageClient` (~95% of the suite)"; in practice the ratio inverts — ~95% of tests don't touch the SDK at all, and ~5% drive it through the handler stub.

## 0.3 `PoliPageClient.PingAsync` — 🔴 Red

Grepping `/Users/mickael/Projects/sdk-csharp/` for `PingAsync`, `HealthCheck`, `GET /v1/health`, and `/v1/ping` returns zero matches. The SDK exposes no dedicated health probe.

Of the three recovery paths the plan lists:

1. **SDK PR adding `PingAsync`** — preferred long-term; blocks Task 14 until merged + packed.
2. **`Documents.ListAsync(limit: 1)`** — does not exist either. The closest GETs (`Documents.GetAsync`, `Documents.PreviewAsync`, `Documents.ThumbnailsAsync`) all require a known document ID, which is wrong shape for a health probe.
3. **Render `getting-started/welcome`** — available via `Render.PreviewAsync(new ProjectModeInput { ProjectSlug = "getting-started", TemplateSlug = "welcome" })` (returns HTML — lighter than a full PDF). This is what the smoke endpoint (Task 16) does anyway.

**Recommended recovery: defer Task 14 to v0.2.** Open an SDK issue tracking the addition of `PingAsync` (or a documented "cheapest stable GET"), and replace Task 14 in the v0.1 plan with a one-page README pattern showing how a host can implement an `IHealthCheck` using `IHttpClientFactory` to probe the published smoke endpoint URL — same code path as the smoke test (Task 16), zero new code in this package, no contract debt to walk back. Re-introduce `IHealthChecksBuilder.AddPoliPage(...)` in v0.2 once the SDK ships a real probe.

**Plan amendments required:**

- Strike Task 14 from the v0.1 task list.
- Update CLAUDE.md §2 to drop `IHealthChecksBuilder.AddPoliPage(...)` from the v0.1 feature list and add a "does NOT ship in v0.1; deferred to v0.2" line.
- Drop the `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` `<PackageVersion>` and `<PackageReference>` entries from Task 1.3 and Task 2.1 — unused until v0.2.

## 0.4 SDK CI / build green — ✅ Green

- `dotnet build PoliPage.sln -c Release` — 0 warnings, 0 errors across both `net8.0` and `net10.0`. Build time ~4 s.
- `dotnet test PoliPage.sln -c Release --no-build --filter "Category!=Integration"` — **136 / 136 passing** on `net8.0` and **136 / 136 passing** on `net10.0`. Runtime ~2 s per TFM.
- SDK is on `main` at `d74ec9f` and packs cleanly to a local source — confirmed via the dependency on `dotnet pack` working in Task 1.8's CI workflow.

## Verdict — 🟡 Yellow: amend the plan first, then proceed

None of the three load-bearing assumptions held verbatim, but all three are recoverable inside the existing plan without an SDK change. Concretely, before opening the PR for Task 1:

1. **Edit Task 12** (`docs/plan/2026-06-01-implementation.md`) to use the actual exception classes and ctor shape (see 0.1 above). Add ProblemDetails extension wiring for `PoliPageRateLimitException.RetryAfter`. Replace `ex.ErrorCode` with `ex.Code` throughout.
2. **Edit Task 13** to replace the `FakePoliPageClient` subclass with `StubPoliPageHttpHandler : DelegatingHandler`. Clarify that the ~95% / ~5% mocking ratio inverts — most tests need no SDK fake at all.
3. **Strike Task 14 and the health-checks package references** from Tasks 1.3 / 2.1. Defer to v0.2. Update CLAUDE.md §2 to match.
4. **Open SDK issues** in `/Users/mickael/Projects/sdk-csharp/` tracking (a) adding a public `PoliPageClient.PingAsync(CancellationToken)` for health probes, (b) the integration's interest in unsealing `PoliPageClient` *if* test virtualization ever becomes load-bearing.

With those amendments, Task 1 (solution scaffold + CI workflow) can start.
