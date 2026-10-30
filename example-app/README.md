# `poli-page/asp` example app

Minimal ASP.NET Core host (net8.0 and net10.0) demonstrating every public method of the Poli Page .NET SDK through the integration. Each Minimal API route maps 1:1 to a step in the SDK's canonical demo, and a parallel MVC controller mirrors the response shape through `PoliPageResponseFactory`.

## Setup

```bash
cd example-app
# POLI_PAGE_API_KEY (a pp_sandbox_… key) and the optional POLI_PAGE_BASE_URL are sourced
# from the nearest .env walking up from here, via Scripts/PoliPageWorkspaceEnvFile — no
# per-app .env needed. Real shell exports always win, so you can override inline:
POLI_PAGE_API_KEY=pp_sandbox_… dotnet run -f net10.0

# Or, with a .env in this directory or any parent holding POLI_PAGE_API_KEY:
dotnet run -f net10.0
```

The host listens on `http://localhost:5044` (per `Properties/launchSettings.json`).

## Interactive demo (recommended)

Open **<http://localhost:5044/>** — it redirects to a single-page dashboard at `/demo.html` with one button per SDK feature:

- **§01 Render** — two buttons that render the welcome PDF / streamed PDF inline.
- **§02 Documents** — store a document, capture the ID, then the Get / Preview / Thumbnails / Delete buttons unlock. "Delete" clears state and re-locks them.
- **§04 Error handling** — renders the `getting-started` project's `does-not-exist` template; the API answers `404 TEMPLATE_NOT_FOUND`, the SDK throws `PoliPageNotFoundException`, and the integration's error handler answers `404` with the error's `ToPayload()` body. The panel shows its `code`, `message`, `status` and `requestId`.

PDFs render in an embedded viewer. JSON responses pretty-print. Errors flip the result panel red. Built with vanilla HTML/CSS/JS — no build step, no Razor.

If you'd rather drive the routes manually (curl, scripts, integration tests), the JSON / PDF endpoints below are unchanged and still respond.

## Routes (Minimal API)

| SDK demo step | URL | What it does |
|---|---|---|
| 1. `Render.PdfAsync` | `GET /render/pdf` | Returns the welcome PDF as `application/pdf` (inline). |
| 2. `Render.PdfStreamAsync` | `GET /render/stream` | Same PDF but streamed via `PoliPageResults.PdfStream`. |
| 5. `Render.DocumentAsync` | `POST /documents` | Stores the document, returns descriptor JSON. |
| 6. `Documents.GetAsync` | `GET /documents/{id}` | 302 to the presigned PDF URL via `PoliPageResults.DocumentRedirect`. |
| 7. `Documents.ThumbnailsAsync` | `GET /documents/{id}/thumbnails` | Page thumbnails as JSON. |
| 8. `Documents.PreviewAsync` | `GET /documents/{id}/preview` | Stored document's HTML preview. |
| 9. `Documents.DeleteAsync` | `DELETE /documents/{id}` | Soft-delete, `204 No Content`. |
| 10. Error handling | `GET /errors/not-found` | Renders `getting-started` / `does-not-exist` with `{}`: the API's `404 TEMPLATE_NOT_FOUND` becomes a `404 application/json` answer from the integration's default error handler, with the `ToPayload()` body `{ "code", "message", "status", "requestId" }`. |
| (smoke probe) | `GET /poli-page/smoke` | The integration's `MapPoliPageSmokeTest()` — renders `getting-started/welcome`. |

## Routes (MVC twin — `PoliPageResponseFactory`)

The same render call paired with the MVC factory yields a `FileContentResult` instead of an `IResult`:

| URL | What it does |
|---|---|
| `GET /invoices/{id}.pdf` | Renders `getting-started/welcome` with `Data = { name = "invoice {id}" }` and returns it via `PoliPageResponseFactory.Pdf`. |

## Quick smoke (no browser)

```bash
curl -o welcome.pdf http://localhost:5044/render/pdf
open welcome.pdf
```

If you see a styled welcome PDF, the integration + SDK + your API key all work end-to-end.

## Architecture notes

- **`Scripts/PoliPageWorkspaceEnvFile.cs`** is invoked by `Program.cs` before `AddPoliPageAspNetCore(...)`. It walks up from the working directory looking for `.env`, parses `KEY=VALUE` lines, and maps the known `POLI_PAGE_*` shell-style names into the hierarchical `PoliPage:*` config keys the SDK + integration options bind from. Real shell exports always win.
- **`Program.cs`** relies on the error handler `AddPoliPageAspNetCore` turns on by default: every `PoliPageException` is answered with its `ToPayload()` body and status. `app.UseExceptionHandler()` is there for the app's other exceptions; the Poli Page answer does not depend on it.
- **`wwwroot/demo.html`** is served verbatim by `app.UseStaticFiles()`. No Razor, no Razor Pages, no Blazor. Ported from the Symfony bundle's example-app dashboard (`poli-page/symfony`) with URLs and SDK-method labels adjusted to the .NET surface.
- **`Endpoints/{Render,Document,Error}Endpoints.cs`** group the Minimal API routes. Each handler resolves `PoliPageClient` from `HttpContext.RequestServices` (rather than the typed-parameter Minimal API binder) so the host stays AOT-friendly under `IsAotCompatible=true`.
- **`Controllers/InvoicesController.cs`** demonstrates `PoliPageResponseFactory` via the MVC convention. Wired through `builder.Services.AddControllers()` + `app.MapControllers()`.
