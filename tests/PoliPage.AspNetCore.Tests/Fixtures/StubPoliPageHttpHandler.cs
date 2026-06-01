using System.Net;
using System.Text;
using PoliPage;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// Canned-response handler used by tests that drive the full ASP.NET pipeline (endpoint →
// SDK → response factory). Inspects the request URI to pick a response shape:
//   POST /v1/render        → descriptor JSON with presignedPdfUrl pointing at the stub
//   GET  /storage/…        → the configured PDF bytes
//   GET  /v1/render/preview→ canned preview JSON (paginated HTML pages array)
//
// When NextException is set, every call throws it so the IExceptionHandler / middleware
// can be exercised end-to-end without modelling real API failure modes.
//
// CLAUDE.md §4 discipline reminder: this stub returns ONE canned response per request shape.
// It is NOT a WireMock-style retest of the SDK — it does not exercise retry budgets, timeout
// behaviour, or 4xx→exception mapping. Tests that need those belong on the SDK, not here.
internal sealed class StubPoliPageHttpHandler : DelegatingHandler
{
    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%stub"u8.ToArray();
    public string PreviewHtml { get; set; } = "<h1>preview</h1>";
    public PoliPageException? NextException { get; private set; }

    public void SetException(PoliPageException ex) => NextException = ex;
    public void ClearException() => NextException = null;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (NextException is { } ex)
            throw ex;

        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/v1/render", StringComparison.Ordinal))
        {
            var descriptor = $$"""
                {
                  "documentId": "doc_stub",
                  "organizationId": "org_stub",
                  "projectSlug": "getting-started",
                  "templateSlug": "welcome",
                  "version": "1.0.0",
                  "environment": "test",
                  "format": "pdf",
                  "pageCount": 1,
                  "sizeBytes": {{PdfBytes.Length}},
                  "presignedPdfUrl": "https://stub.invalid/storage/doc_stub.pdf"
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(descriptor, Encoding.UTF8, "application/json"),
            });
        }

        if (path.Contains("/storage/", StringComparison.Ordinal))
        {
            var content = new ByteArrayContent(PdfBytes);
            content.Headers.ContentType = new("application/pdf");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        if (path.EndsWith("/v1/render/preview", StringComparison.Ordinal))
        {
            var escaped = PreviewHtml.Replace("\"", "\\\"", StringComparison.Ordinal);
            var preview = $$"""{"pages":["{{escaped}}"],"pageCount":1}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(preview, Encoding.UTF8, "application/json"),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
