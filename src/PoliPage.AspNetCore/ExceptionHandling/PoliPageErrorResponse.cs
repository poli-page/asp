using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Writes a PoliPageException as the HTTP answer every Poli Page integration gives:
// status = ToPayload().Status, or 500 when the SDK knows none; body = ToPayload() as JSON,
// { "code", "message", "status", "requestId" }; Content-Type application/json.
internal static class PoliPageErrorResponse
{
    public const string ContentType = "application/json; charset=utf-8";

    public static async Task WriteAsync(HttpContext httpContext, PoliPageException exception, CancellationToken cancellationToken)
    {
        var payload = exception.ToPayload();

        // Serialised to a byte array and written to the body stream, not through
        // WriteAsJsonAsync: on net8.0 that path needs PipeWriter.UnflushedBytes, which some
        // response pipe writers (the net8.0 TestServer's among them) do not implement.
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, PoliPageErrorPayloadJsonContext.Default.PoliPageErrorPayload);

        var response = httpContext.Response;
        response.Clear();
        response.StatusCode = payload.Status ?? StatusCodes.Status500InternalServerError;
        response.ContentType = ContentType;
        response.ContentLength = body.Length;
        if (exception is PoliPageRateLimitException { RetryAfter: { } retryAfter })
        {
            var seconds = (long)Math.Ceiling(Math.Max(0, retryAfter.TotalSeconds));
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        var activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, payload.Message);
        activity?.AddTag("polipage.error.code", payload.Code);

        await response.Body.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
