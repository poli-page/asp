# Response helpers

> `PoliPageResults` (Minimal API `IResult`) and `PoliPageResponseFactory` (MVC `IActionResult`) — two surfaces, one set of headers.

## Why

Returning a PDF from an ASP.NET Core endpoint correctly means setting four headers consistently: `Content-Type: application/pdf`, an RFC 5987-encoded `Content-Disposition` (so non-ASCII filenames survive Unicode-hostile browsers), `Cache-Control: no-store, private` (so proxies and SWs don't cache an authenticated render), and `X-Content-Type-Options: nosniff` (so a misclassified upload can't hijack the MIME type). The two helpers do that for you; they also keep the Minimal API and MVC code paths visually identical so you can grep for either.

## Minimal API — `PoliPageResults`

`PoliPage.AspNetCore.PoliPageResults` is a static class with four factories. Each returns an `IResult` you hand back from your endpoint delegate.

```csharp
using PoliPage;
using PoliPage.AspNetCore;

app.MapGet("/invoices/{id}.pdf", async (
    string id,
    PoliPageClient client,
    CancellationToken cancellationToken) =>
{
    var pdf = await client.Render.PdfAsync(
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

| Method | Body | Returns |
|---|---|---|
| `PoliPageResults.Pdf(byte[] pdf, string? filename = null, bool inline = false)` | full bytes | `IResult` writing `application/pdf` + filename + cache + nosniff |
| `PoliPageResults.PdfStream(Stream pdfStream, string? filename = null, bool inline = false)` | streamed | `IResult` copying the stream to the response body |
| `PoliPageResults.Preview(string html)` | HTML string | `IResult` writing `text/html; charset=utf-8` + cache + nosniff |
| `PoliPageResults.DocumentRedirect(string presignedUrl)` | — | `IResult` issuing a 302 to the presigned URL |

`inline: true` swaps `Content-Disposition: attachment` for `Content-Disposition: inline` so the browser renders the PDF in-tab instead of downloading.

## MVC — `PoliPageResponseFactory`

`PoliPage.AspNetCore.PoliPageResponseFactory` is registered as a singleton by `AddPoliPageAspNetCore`. Inject it alongside `PoliPageClient`:

```csharp
using Microsoft.AspNetCore.Mvc;
using PoliPage;
using PoliPage.AspNetCore;

public class InvoicesController(
    PoliPageClient client,
    PoliPageResponseFactory responses) : ControllerBase
{
    [HttpGet("invoices/{id}.pdf")]
    public async Task<IActionResult> Show(string id, CancellationToken cancellationToken)
    {
        var pdf = await client.Render.PdfAsync(
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

| Method | Returns |
|---|---|
| `Pdf(byte[] pdf, string? filename = null, bool inline = false)` | `FileContentResult` |
| `PdfStream(Stream pdf, string? filename = null, bool inline = false)` | `FileStreamResult` |
| `Preview(string html)` | `ContentResult` (`text/html; charset=utf-8`) |
| `DocumentRedirect(string presignedUrl)` | `RedirectResult` |

## Headers each helper sets

| Helper | Content-Type | Content-Disposition | Cache-Control | X-Content-Type-Options |
|---|---|---|---|---|
| `Pdf` / `PdfStream` | `application/pdf` | `attachment; filename="..."; filename*=UTF-8''...` when filename set | `DefaultCacheControl` value | `nosniff` when `SetNoSniffHeader` |
| `Preview` | `text/html; charset=utf-8` | — | `DefaultCacheControl` value | `nosniff` when `SetNoSniffHeader` |
| `DocumentRedirect` | — | — | — | — |

The cache header and nosniff defaults come from `PoliPageAspNetCoreOptions`. Set `DefaultCacheControl = null` to drop the cache header entirely on a per-app basis (e.g., if you front the app with a CDN that decides cache policy).

## Filename encoding

The header is built by ASP.NET Core's own `Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.SetHttpFileName` — the same helper MVC's `FileResult` uses — so both surfaces emit the RFC 6266 dual form: an ASCII `filename` fallback (non-ASCII characters replaced by `_`, quoted and with `\` and `"` escaped as quoted-pairs when needed) plus a UTF-8 percent-encoded `filename*` for modern clients:

```
Content-Disposition: attachment; filename=invoice-INV-42.pdf; filename*=UTF-8''invoice-INV-42.pdf
Content-Disposition: attachment; filename="facture-_t_ 2026.pdf"; filename*=UTF-8''facture-%C3%A9t%C3%A9%202026.pdf
Content-Disposition: attachment; filename="say \"hi\".pdf"; filename*=UTF-8''say%20%22hi%22.pdf
```

Before that, control characters (C0 including TAB/CR/LF, DEL, C1) are stripped from the filename — the framework helper would otherwise keep them as `_` in the fallback and as `%0D%0A`-style escapes in `filename*`. A filename made only of control characters is rejected with an `ArgumentException`, like an empty one. You do not call any of this directly — it runs whenever you pass `filename:` to `PoliPageResults.Pdf` / `PdfStream` or `PoliPageResponseFactory.Pdf` / `PdfStream`.

## OpenAPI metadata is automatic

Every `IResult` returned by `PoliPageResults` also implements `IEndpointMetadataProvider` (.NET 9+). That means `Microsoft.AspNetCore.OpenApi` and Swashbuckle discover the response shape automatically — you don't need to chain `.Produces("application/pdf", 200)` on every endpoint.

```csharp
app.MapGet("/invoices/{id}.pdf", (string id, PoliPageClient c, CancellationToken ct) =>
    PoliPageResults.Pdf(/* … */, $"invoice-{id}.pdf"));
// OpenAPI sees: 200 application/pdf — no manual .Produces() needed.
```

You can still add `.Produces<T>(...)` / `.ProducesProblem(...)` to document additional statuses or refine the schema. The auto-metadata is additive.

For the MVC surface, `[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]` on the action method is the equivalent. ASP.NET Core does not infer this from a `FileContentResult` return type.

## Gotchas

- **A bare `FileContentResult` keeps control characters.** `new FileContentResult(pdf, "application/pdf") { FileDownloadName = name }` gets the same RFC 6266 encoding, but CR/LF/TAB survive as `_` and `%0D%0A`. `PoliPageResponseFactory.Pdf(...)` strips them before handing the name to MVC — prefer it when the filename comes from user or template data.
- **`PoliPageResults.Pdf` buffers the whole byte array** into the response body in one `WriteAsync`. For 10 MB+ documents, prefer `PoliPageResults.PdfStream(client.Render.PdfStreamAsync(...))` — see [streaming.md](streaming.md).
- **`Preview` returns the HTML verbatim.** The SDK already renders the HTML; this helper does not re-render, re-escape, or sanitize. If you accept user-controlled `Data` and the template echoes it raw, that's a template authoring issue, not a helper issue.
- **`DocumentRedirect`** issues a 302 to a presigned S3 URL with a 15-minute TTL. Don't store the URL in a database or pass it to a job queue — fetch a fresh one with `client.Documents.GetAsync(id)` each time.

## Related

- [README → Quick start](../README.md#quick-start) — the canonical Minimal API + MVC pair.
- [streaming.md](streaming.md) — when to prefer `PdfStream` over `Pdf`.
- [minimal-apis.md](minimal-apis.md) — `IResult` composition and route groups.
