# Migration guide

This file documents breaking changes between releases of `PoliPage.AspNetCore`. Future migrations land here so consumers always know where to look.

## 0.13.0: first release

`0.13.0` is the first release on NuGet. It shares its major.minor with the Poli Page API and requires the `PoliPage` SDK 0.13 (`[0.13.0,0.14.0)`); a 0.11.0 was prepared and never published. New users need no migration. The notes below are for apps built against the unpublished `0.1.0` pre-release (from a local package source).

**SDK 0.13.** The package now requires `PoliPage` `[0.13.0,0.14.0)`. API keys are `pp_{env}_{32 lowercase hex}` (`pp_sandbox_…`, `pp_live_…`) and select the environment; `RenderInput` has no `Version`. A 2xx whose body is not JSON is `invalid_response` (answered `502`), a `RenderToFileAsync` that cannot write its file is `io_error` (answered `500`, `"status": null`), and `RequestTimeout` bounds the PDF download. See the SDK's migration guide for the SDK side.

**Error answers.** A `PoliPageException` is now answered with the SDK's `ToPayload()` as `application/json`, not RFC 7807 ProblemDetails:

| Before (ProblemDetails) | Now |
|---|---|
| `application/problem+json` | `application/json` |
| `code` extension | `code` |
| `detail` | `message` |
| `status` | `status` (`null` in the body when the SDK knows none; the HTTP status is then 500) |
| `poliPageRequestId` extension | `requestId` (`null` when unknown) |
| `retryAfterSeconds` extension | the `Retry-After` header |
| `type`, `title`, `instance`, `traceId` | gone |

Clients that read the ProblemDetails fields must read the new ones. `PoliPageAspNetCoreOptions.ProblemDetailsTypeUri` and `IncludeRequestIdInProblemDetails` are removed: delete them from code (it no longer compiles) and from `appsettings.json` (ignored there). `CustomizeProblemDetails` callbacks no longer run on Poli Page errors.

**On by default.** The handler now answers even when the app does not call `app.UseExceptionHandler()`, and in Development before the developer exception page. To handle `PoliPageException` yourself, set `PoliPage:AspNetCore:RegisterExceptionHandler` to `false`.

**Package source.** If you resolved the pre-release from a local `nuget.config` source, remove that `<add key="…"/>` line (or delete `nuget.config` if it had no other entries) and run `dotnet restore`: `PoliPage.AspNetCore` 0.13.0 now comes from `nuget.org`.
