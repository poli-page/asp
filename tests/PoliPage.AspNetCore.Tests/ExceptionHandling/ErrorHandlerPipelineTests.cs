using System.Text.Json;
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

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Handler_is_off_when_RegisterExceptionHandler_is_false(string environment)
    {
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "Bad key", requestId: "req_x");
        await using var host = await StartThrowingHostAsync(
            exception, configureAspNet: o => o.RegisterExceptionHandler = false, useExceptionHandler: true, environment);

        var response = await host.Client.GetAsync(ThrowPath);

        // ASP.NET Core's own answer for an unhandled exception: a 500 ProblemDetails (from the
        // exception handler, or in Development from the developer page for a non-HTML client).
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await ReadJsonAsync(response);
        body.TryGetProperty("code", out _).Should().BeFalse();
        body.TryGetProperty("requestId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handler_leaves_other_exceptions_to_the_app()
    {
        await using var host = await PoliPageTestHost.StartAsync(
            app => app.MapGet(ThrowPath, (RequestDelegate)(_ => throw new InvalidOperationException("not ours"))));

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
}
