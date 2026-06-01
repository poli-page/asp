using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoliPage.AspNetCore.ExceptionHandling;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

public class PoliPageExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_handles_PoliPageException_and_delegates_to_problem_details_service()
    {
        var (handler, recorder) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new PoliPageAuthException(PoliPageErrorCode.Unauthorized, 401, "bad key", requestId: "req_1");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        recorder.Captured.Should().NotBeNull();
        recorder.Captured!.Status.Should().Be(StatusCodes.Status401Unauthorized);
        recorder.Captured.Extensions["code"].Should().Be("authentication_failed");
    }

    [Fact]
    public async Task TryHandleAsync_returns_false_for_non_PoliPageException()
    {
        var (handler, recorder) = CreateHandler();
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext, new InvalidOperationException("not ours"), CancellationToken.None);

        handled.Should().BeFalse();
        recorder.Captured.Should().BeNull();
    }

    [Fact]
    public async Task TryHandleAsync_returns_false_when_response_has_started()
    {
        var (handler, recorder) = CreateHandler();
        // DefaultHttpContext.Response.Body is Stream.Null with HasStarted always false. Substitute
        // a Response feature whose HasStarted is true to simulate the mid-stream throw case
        // (PoliPageException raised after body bytes have hit the wire — CLAUDE.md §10.3).
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new ResponseFeatureWithHasStarted(hasStarted: true));

        var handled = await handler.TryHandleAsync(
            httpContext,
            new PoliPageAuthException(PoliPageErrorCode.Unauthorized, 401, "x"),
            CancellationToken.None);

        handled.Should().BeFalse();
        recorder.Captured.Should().BeNull();
    }

    private static (PoliPageExceptionHandler handler, RecordingProblemDetailsService recorder) CreateHandler(
        Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        var factory = new PoliPageProblemDetailsFactory(Microsoft.Extensions.Options.Options.Create(aspnet));
        var recorder = new RecordingProblemDetailsService();
        var logger = NullLogger<PoliPageExceptionHandler>.Instance;
        return (new PoliPageExceptionHandler(factory, recorder, logger), recorder);
    }
}
