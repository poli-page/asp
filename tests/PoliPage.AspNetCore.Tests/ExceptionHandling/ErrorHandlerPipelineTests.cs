using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Results;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

// The error handler as an app gets it from AddPoliPageAspNetCore: through a real host, with and
// without app.UseExceptionHandler(), and turned off.
public class ErrorHandlerPipelineTests
{
    private const string ThrowPath = "/throw";

    // (environment, app calls UseExceptionHandler): Development adds the developer exception page.
    public static TheoryData<string, bool> Wirings => new()
    {
        { "Production", true },
        { "Production", false },
        { "Development", true },
        { "Development", false },
    };

    // Each SDK error shape and the answer the contract fixes for it (policy.implStatusForSdkCode
    // for network_error and timeout; 500 when the SDK knows no status).
    public static TheoryData<string, PoliPageException, int, int?, string?> Errors => new()
    {
        {
            "api error",
            new PoliPageNotFoundException(PoliPageErrorCode.TemplateNotFound, 404,
                "Template 'does-not-exist' not found in project 'getting-started'.", requestId: "req_404"),
            404, 404, "req_404"
        },
        {
            "network_error",
            new PoliPageNetworkException("Network request failed"),
            503, 503, null
        },
        {
            "timeout",
            new PoliPageException(PoliPageErrorCode.Timeout, 0, "Request timed out after 60000ms"),
            504, 504, null
        },
        {
            "no status",
            new PoliPageException(PoliPageErrorCode.Aborted, 0, "Request was aborted"),
            500, null, null
        },
    };

    [Theory]
    [MemberData(nameof(Wirings))]
    public async Task Handler_is_on_by_default_with_or_without_UseExceptionHandler(string environment, bool useExceptionHandler)
    {
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "Bad key", requestId: "req_x");
        await using var host = await StartThrowingHostAsync(exception, configureAspNet: null, useExceptionHandler, environment);

        var response = await host.Client.GetAsync(ThrowPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        (await ReadJsonAsync(response)).ShouldBePayload(PoliPageErrorCode.InvalidApiKey, "Bad key", 401, "req_x");
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Handler_answers_the_payload_and_its_status(
        string shape, PoliPageException exception, int httpStatus, int? bodyStatus, string? requestId)
    {
        await using var host = await StartThrowingHostAsync(exception, configureAspNet: null, useExceptionHandler: false);

        var response = await host.Client.GetAsync(ThrowPath);

        ((int)response.StatusCode).Should().Be(httpStatus, shape);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        (await ReadJsonAsync(response)).ShouldBePayload(exception.Code, exception.Message, bodyStatus, requestId);
    }

    [Fact]
    public async Task Handler_sets_Retry_After_for_a_rate_limit_error()
    {
        var exception = new PoliPageRateLimitException(
            PoliPageErrorCode.RateLimitExceeded, 429, "Slow down", requestId: "req_rl", retryAfter: TimeSpan.FromSeconds(2.5));
        await using var host = await StartThrowingHostAsync(exception, configureAspNet: null, useExceptionHandler: true);

        var response = await host.Client.GetAsync(ThrowPath);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(3));
        (await ReadJsonAsync(response)).ShouldBePayload(PoliPageErrorCode.RateLimitExceeded, "Slow down", 429, "req_rl");
    }

    private const int ErrorAnsweredEventId = 1002;

    // Every wiring logs the answered error exactly once, at Error for a 5xx answer.
    [Theory]
    [MemberData(nameof(Wirings))]
    public async Task Handler_logs_the_answered_error(string environment, bool useExceptionHandler)
    {
        var exception = new PoliPageException(PoliPageErrorCode.InternalError, 500, "Unexpected server error.", requestId: "req_5xx");
        await using var host = await StartThrowingHostAsync(exception, configureAspNet: null, useExceptionHandler, environment);

        var response = await host.Client.GetAsync(ThrowPath);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var entry = host.Logs.Entries.Should().ContainSingle(e => e.EventId.Id == ErrorAnsweredEventId).Subject;
        entry.LogLevel.Should().Be(LogLevel.Error);
        entry.Message.Should().Be("Poli Page error INTERNAL_ERROR answered with HTTP 500 (request id req_5xx).");
        entry.Exception.Should().BeSameAs(exception);
        host.Logs.Entries.Should().NotContain(e => e.Message.Contains(TestKeys.Sandbox, StringComparison.Ordinal)
            || e.State.Contains(TestKeys.Sandbox, StringComparison.Ordinal));
    }

    // Warning for a 4xx answer; Error when the answer is 5xx, including the 500 for no status.
    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Handler_logs_at_a_level_set_by_the_answered_status(
        string shape, PoliPageException exception, int httpStatus, int? bodyStatus, string? requestId)
    {
        await using var host = await StartThrowingHostAsync(exception, configureAspNet: null, useExceptionHandler: false);

        var response = await host.Client.GetAsync(ThrowPath);

        ((int)response.StatusCode).Should().Be(httpStatus, shape);
        var entry = host.Logs.Entries.Should().ContainSingle(e => e.EventId.Id == ErrorAnsweredEventId).Subject;
        entry.LogLevel.Should().Be(httpStatus >= 500 ? LogLevel.Error : LogLevel.Warning, shape);
        entry.Message.Should().Be(
            $"Poli Page error {exception.Code} answered with HTTP {httpStatus} (request id {requestId ?? "none"}).",
            "{0} (body status {1})", shape, bodyStatus);
    }

    // A PDF result sets Content-Type, Content-Disposition and nosniff before it copies the stream;
    // an SDK error raised by the first read must not leave them on the JSON answer.
    [Theory]
    [MemberData(nameof(Wirings))]
    public async Task Stream_error_after_the_pdf_headers_answers_the_payload_without_them(
        string environment, bool useExceptionHandler)
    {
        var exception = new PoliPageException("DOWNLOAD_FAILED", 502, "Failed to download PDF: HTTP 502", requestId: "req_dl");
        await using var host = await PoliPageTestHost.StartAsync(
            app => app.MapGet(ThrowPath, (RequestDelegate)(ctx =>
                PoliPageResults.PdfStream(new ThrowingStream(exception), "statement.pdf").ExecuteAsync(ctx))),
            configureAspNet: null,
            wireDefaultExceptionHandler: useExceptionHandler,
            environmentName: environment);

        var response = await host.Client.GetAsync(ThrowPath);

        ((int)response.StatusCode).Should().Be(502);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition.Should().BeNull();
        response.Headers.Contains("X-Content-Type-Options").Should().BeFalse();
        (await ReadJsonAsync(response)).ShouldBePayload("DOWNLOAD_FAILED", "Failed to download PDF: HTTP 502", 502, "req_dl");
    }

    // (environment, app calls UseExceptionHandler); without either, TestServer rethrows to the client.
    public static TheoryData<string, bool> OffWirings => new()
    {
        { "Production", true },
        { "Development", true },
        { "Development", false },
    };

    [Theory]
    [MemberData(nameof(OffWirings))]
    public async Task Handler_is_off_when_RegisterExceptionHandler_is_false(string environment, bool useExceptionHandler)
    {
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "Bad key", requestId: "req_x");
        await using var host = await StartThrowingHostAsync(
            exception, configureAspNet: o => o.RegisterExceptionHandler = false, useExceptionHandler, environment);

        var response = await host.Client.GetAsync(ThrowPath);

        // ASP.NET Core's own answer for an unhandled exception: a 500 ProblemDetails (from the
        // exception handler, or in Development from the developer page for a non-HTML client).
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await ReadJsonAsync(response);
        body.TryGetProperty("code", out _).Should().BeFalse();
        body.TryGetProperty("requestId", out _).Should().BeFalse();
        host.Logs.Entries.Should().NotContain(e => e.EventId.Id == ErrorAnsweredEventId);
    }

    [Theory]
    [MemberData(nameof(OffWirings))]
    public async Task Handler_leaves_other_exceptions_to_the_app(string environment, bool useExceptionHandler)
    {
        await using var host = await PoliPageTestHost.StartAsync(
            app => app.MapGet(ThrowPath, (RequestDelegate)(_ => throw new InvalidOperationException("not ours"))),
            wireDefaultExceptionHandler: useExceptionHandler,
            environmentName: environment);

        var response = await host.Client.GetAsync(ThrowPath);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    private static Task<PoliPageTestHost> StartThrowingHostAsync(
        PoliPageException exception,
        Action<PoliPageAspNetCoreOptions>? configureAspNet,
        bool useExceptionHandler,
        string environment = "Production")
        => PoliPageTestHost.StartAsync(
            app => app.MapGet(ThrowPath, (RequestDelegate)(_ => throw exception)),
            configureAspNet,
            wireDefaultExceptionHandler: useExceptionHandler,
            environmentName: environment);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    // A PDF stream whose first read fails, as a presigned download that breaks would.
    private sealed class ThrowingStream(PoliPageException exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw exception;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw exception;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw exception;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
