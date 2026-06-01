# Streaming large PDFs

> Stream a multi-megabyte PDF as an `IResult` / `FileStreamResult` without buffering the full document in process memory.

## Why

`PoliPageClient.Render.PdfAsync` returns the PDF as `byte[]` — fine for a one-page invoice, wasteful for a 50-page report or a 10 MB statement. `Render.PdfStreamAsync` returns a `Stream` that reads from the upstream presigned URL on demand. `PoliPageResults.PdfStream(...)` (Minimal API) and `PoliPageResponseFactory.PdfStream(...)` (MVC) adapt that stream to the response body with the correct PDF headers, so bytes flow controller → client without sitting in memory.

## How — Minimal API

```csharp
using PoliPage;
using PoliPage.AspNetCore;

app.MapGet("/statements/{accountId}.pdf", async (
    string accountId,
    PoliPageClient client,
    CancellationToken cancellationToken) =>
{
    var stream = await client.Render.PdfStreamAsync(
        new ProjectModeInput
        {
            Project = "statements",
            Template = "monthly",
            Version = "2.3.0",
            Data = new { AccountId = accountId },
        },
        cancellationToken: cancellationToken);

    return PoliPageResults.PdfStream(stream, $"statement-{accountId}.pdf");
});
```

`PoliPageResults.PdfStream`:

- Sets `Content-Type: application/pdf`.
- Sets `Content-Disposition` (RFC 5987-encoded for non-ASCII filenames) when `filename` is provided.
- Sets `Cache-Control` from `PoliPageAspNetCoreOptions.DefaultCacheControl` (`no-store, private` by default).
- Sets `X-Content-Type-Options: nosniff` when `SetNoSniffHeader` is `true`.
- Copies the stream to `HttpContext.Response.Body` via `Stream.CopyToAsync(..., HttpContext.RequestAborted)` — so client disconnects abort the upstream read mid-copy.
- Disposes the source stream after the copy completes (success or failure).

## How — MVC

```csharp
using Microsoft.AspNetCore.Mvc;
using PoliPage;
using PoliPage.AspNetCore;

public class StatementsController(
    PoliPageClient client,
    PoliPageResponseFactory responses) : ControllerBase
{
    [HttpGet("statements/{accountId}.pdf")]
    public async Task<IActionResult> Show(string accountId, CancellationToken cancellationToken)
    {
        var stream = await client.Render.PdfStreamAsync(
            new ProjectModeInput
            {
                Project = "statements",
                Template = "monthly",
                Version = "2.3.0",
                Data = new { AccountId = accountId },
            },
            cancellationToken: cancellationToken);

        return responses.PdfStream(stream, $"statement-{accountId}.pdf");
    }
}
```

`PoliPageResponseFactory.PdfStream` returns a `FileStreamResult` — ASP.NET Core's MVC layer handles the body copy and stream disposal. The factory writes the `Content-Disposition` directly to `HttpContext.Response.Headers` (to keep the RFC 5987 encoding) before returning the `FileStreamResult`.

## Inline rendering

Pass `inline: true` to set `Content-Disposition: inline; filename=...` so the browser renders the PDF in-tab instead of triggering a download:

```csharp
return PoliPageResults.PdfStream(stream, $"statement-{accountId}.pdf", inline: true);
```

## Gotchas

- **Response compression breaks streaming.** PDFs are already compressed; do not enable `app.UseResponseCompression()` for `application/pdf`. If you do, ASP.NET Core's `ResponseCompressionMiddleware` will buffer the entire stream before emitting the first byte, defeating the point. Either exclude `application/pdf` from `ResponseCompressionOptions.MimeTypes` or scope `UseResponseCompression` to a route group that excludes render endpoints.
- **HTTP/2 + buffered hosting** (e.g., default IIS in-process). Kestrel streams correctly out of the box. IIS in-process hosting buffers responses up to a threshold (`responseBufferingFlag` in `web.config`); disable it for the render endpoint or host Kestrel directly behind a reverse proxy.
- **Mid-stream failures cannot become ProblemDetails.** If the SDK throws after `Response.HasStarted` (e.g., a `PoliPageConnectionException` while copying the body), `PoliPageExceptionHandlerMiddleware` cannot rewrite the headers — it rethrows + logs a warning. The browser sees a truncated PDF. There is no way around this without buffering, which is what `PdfStream` exists to avoid. Mitigate at the client (browsers retry on visible truncation) or accept the trade-off.
- **The presigned URL has a 15-minute TTL.** For very large documents over slow client connections, the upstream connection can close mid-stream. Cap aggressive per-request timeouts via `PoliPage:RequestTimeout` rather than letting the SDK's default ride for an hour.
- **`Content-Length` is omitted when the upstream stream's size is unknown** (which is the common case — chunked transfer from S3). Browsers show an indeterminate progress bar. That is correct behaviour; do not guess a size.
- **Do not `using var stream = await client.Render.PdfStreamAsync(...)`.** The result owns the stream and disposes it after the body copy. Wrapping in `using` disposes before the copy, producing `ObjectDisposedException`.
- **`MapPoliPageSmokeTest` is not the right surface to validate streaming.** It uses `Render.PdfAsync` (buffered) for predictable, small payloads — that's intentional. Use `curl --no-buffer https://your-app/statements/.../pdf` to exercise the stream path.

## Verifying with curl

```bash
curl -sS --no-buffer -o statement.pdf -w '%{time_starttransfer}s to first byte\n' \
    https://your-app/statements/ACCT-42.pdf
```

For a 10 MB PDF, expect time-to-first-byte to be ~SDK render latency, not ~render latency + transfer time. If TTFB scales with file size, streaming is being buffered somewhere — work back through the middleware chain.

## Related

- [responses.md](responses.md) — when to use the buffered `Pdf` instead of `PdfStream`.
- [minimal-apis.md](minimal-apis.md) — composing streaming endpoints into route groups with rate limiting and auth.
- [testing.md](testing.md) — `WebApplicationFactory` assertions on `Response.Body` content.
