# Minimal API patterns

> Route groups, `IResult` composition, model binding, and authorization for endpoints that render Poli Page documents.

## Why

ASP.NET Core's Minimal APIs are the default for new projects on `net8.0`+ and the natural surface for "render a PDF and return it" endpoints. The patterns below show how to keep handlers small, push validation to model binding, and compose authorization/rate-limiting around a render group without per-endpoint boilerplate.

## A route group for render endpoints

```csharp
using PoliPage;
using PoliPage.AspNetCore;

var render = app.MapGroup("/render")
    .RequireAuthorization("Renderer")
    .WithTags("PoliPage");

render.MapGet("/invoices/{id}.pdf", async (
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

render.MapGet("/statements/{accountId}.pdf", /* … */);
render.MapGet("/contracts/{id}.pdf", /* … */);
```

Every endpoint in the group inherits the `Renderer` policy and the `PoliPage` OpenAPI tag. Authentication, authorization, output caching, rate limiting, and CORS all compose via `MapGroup` — the docs you have for those features apply unchanged.

## Strongly-typed request bodies

The SDK's input types are POCOs, so the `[FromBody]` binder accepts them directly. For the inline-HTML mode:

```csharp
public sealed record RenderRequest(string Html, object? Data);

app.MapPost("/render/custom", async (
    [FromBody] RenderRequest request,
    PoliPageClient client,
    CancellationToken cancellationToken) =>
{
    var pdf = await client.Render.PdfAsync(
        new InlineModeInput
        {
            Template = request.Html,
            Data = request.Data,
        },
        cancellationToken: cancellationToken);

    return PoliPageResults.Pdf(pdf, "custom.pdf");
});
```

## Returning multiple result types

When an endpoint can return either a PDF or a ProblemDetails error, declare a `Results<...>` union so the framework knows which type to expose in OpenAPI:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;

app.MapGet("/render/preview/{templateId}", async Task<Results<ContentHttpResult, NotFound>> (
    string templateId,
    PoliPageClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var preview = await client.Render.PreviewAsync(
            new ProjectModeInput
            {
                Project = "templates",
                Template = templateId,
                Version = "1.0.0",
            },
            cancellationToken: cancellationToken);

        return TypedResults.Content(preview.Html, "text/html; charset=utf-8");
    }
    catch (PoliPageNotFoundException)
    {
        return TypedResults.NotFound();
    }
});
```

`PoliPageResults.Pdf`, `PdfStream`, `Preview`, `DocumentRedirect` all implement `IResult` but are not `TypedResults`-shaped — they're untyped on purpose so the headers contract stays under the helper's control rather than competing with `Microsoft.AspNetCore.Http.HttpResults`. If you need a typed result in a `Results<...>` union, return `TypedResults.File(pdf, "application/pdf", $"invoice-{id}.pdf")` instead and accept that you'll lose the RFC 5987 filename encoding for non-ASCII names.

## OpenAPI annotations

The 200 + `application/pdf` content type is contributed automatically by `PoliPageResults.Pdf`/`PdfStream` via `IEndpointMetadataProvider` (.NET 9+) — `Microsoft.AspNetCore.OpenApi` and Swashbuckle pick it up without you writing `.Produces(...)`:

```csharp
render.MapGet("/invoices/{id}.pdf", /* … */)
    .WithName("DownloadInvoice")
    .WithSummary("Download an invoice as a PDF.")
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status429TooManyRequests);
//  ^ no .Produces(...) for the 200 — auto-discovered.
```

The exception-handler's ProblemDetails responses match `application/problem+json` exactly, so `ProducesProblem(...)` describes them accurately for the spec consumers. The default content type on `ProducesProblem` is `application/problem+json`; ASP.NET Core's `Microsoft.AspNetCore.Http.ProblemDetailsService` writes the same content type by default.

## Rate limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("render", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

app.UseRateLimiter();

render.RequireRateLimiting("render");
```

When a caller exceeds the limit, ASP.NET Core returns 429 before the SDK is ever invoked — the SDK's own retry budget never burns on locally-rejected traffic.

## Output caching for previews

Preview HTML is deterministic for a given `(project, template, version, data)` tuple. If your `data` is small and shareable across users, output caching saves repeat renders:

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("preview-15m", builder => builder
        .Expire(TimeSpan.FromMinutes(15))
        .SetVaryByQuery("v", "data"));
});

app.UseOutputCache();

render.MapGet("/preview/{templateId}", /* … */)
    .CacheOutput("preview-15m");
```

Do **not** apply this to PDFs containing authenticated data — `Cache-Control: no-store, private` is set on PDF responses on purpose, and `UseOutputCache` ignores the response cache header by design.

## Cancellation

Every Minimal API delegate accepts a `CancellationToken` parameter wired to `HttpContext.RequestAborted`. Pass it through to the SDK so client disconnects abort the upstream call:

```csharp
app.MapGet("/render/heavy", async (
    PoliPageClient client,
    CancellationToken cancellationToken) =>
{
    var pdf = await client.Render.PdfAsync(input, cancellationToken: cancellationToken);
    return PoliPageResults.Pdf(pdf, "heavy.pdf");
});
```

When the user closes the browser tab, the token cancels, the SDK aborts the inflight HTTP request, and your server gives up CPU instead of finishing a render no one will read.

## Smoke endpoint composition

`MapPoliPageSmokeTest` is a regular endpoint convention builder, so the same grouping rules apply:

```csharp
app.MapPoliPageSmokeTest("/poli-page/smoke")
    .RequireAuthorization("Operator")
    .WithTags("Operations");
```

The package emits a startup-log warning **once** when the smoke endpoint is registered without either `.RequireAuthorization(...)` or `.AllowAnonymous()` — log level `Warning`, source `PoliPage.AspNetCore.SmokeTest`. The warning catches the common deploy regression where `.RequireAuthorization("Operator")` gets accidentally removed and the endpoint silently becomes anonymous in production.

In production environments, prefer `.RequireAuthorization(...)` over `if (app.Environment.IsDevelopment())` — the latter silently drops the endpoint after a Helm-chart-introduced environment change, which makes incident-response slower. If you genuinely want the endpoint anonymous (a dev sandbox, an isolated demo cluster), opt in explicitly:

```csharp
app.MapPoliPageSmokeTest()
    .AllowAnonymous();   // silences the startup warning; documents intent
```

## Gotchas

- **`Stream pdfStream` from `Render.PdfStreamAsync`** must be disposed. Returning `PoliPageResults.PdfStream(stream, ...)` transfers ownership to the result, which disposes after the body copy completes. Do **not** wrap the call in a `using` — you'll dispose the stream before the response body writer reads it.
- **`TypedResults` and `PoliPageResults` are not interchangeable.** `TypedResults.File(...)` returns a typed `FileContentHttpResult` that ASP.NET serializes via OpenAPI but skips the RFC 5987 filename encoding. `PoliPageResults.Pdf(...)` is untyped but encodes correctly. Pick based on whether your filenames can be non-ASCII.
- **Per-endpoint authorization clobbers group policies if you specify both.** `render.RequireAuthorization("Renderer")` on the group + `.AllowAnonymous()` on a single endpoint disables auth for that endpoint only — useful for a public-by-design preview, dangerous if accidental.

## Related

- [responses.md](responses.md) — header reference and the `PoliPageResponseFactory` MVC alternative.
- [streaming.md](streaming.md) — when to switch from `Pdf` to `PdfStream`.
- [testing.md](testing.md) — `WebApplicationFactory<Program>` and faking `PoliPageClient`.
