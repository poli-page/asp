using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Writes a PoliPageException as the HTTP answer every Poli Page integration gives:
// status = ToPayload().Status, or 500 when the SDK knows none; body = ToPayload() as JSON,
// { "code", "message", "status", "requestId" }; Content-Type application/json. Logs it
// (EventId 1002): once handled, ASP.NET Core does not log the exception itself on every path.
internal static class PoliPageErrorResponse
{
    public const string ContentType = "application/json; charset=utf-8";

    public static async Task WriteAsync(
        HttpContext httpContext,
        PoliPageException exception,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var payload = exception.ToPayload();
        var status = payload.Status ?? StatusCodes.Status500InternalServerError;
        LogMessages.PoliPageErrorAnswered(
            logger,
            status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            payload.Code,
            status,
            payload.RequestId ?? "none");

        // Serialised to a byte array and written to the body stream, not through
        // WriteAsJsonAsync: on net8.0 that path needs PipeWriter.UnflushedBytes, which some
        // response pipe writers (the net8.0 TestServer's among them) do not implement.
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, PoliPageErrorPayloadJsonContext.Default.PoliPageErrorPayload);

        var response = httpContext.Response;
        response.Clear();
        response.StatusCode = status;
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
