using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using PoliPage.AspNetCore.ExceptionHandling;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

public class PoliPageExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_catches_PoliPageException_and_writes_problem_via_service()
    {
        var recorder = new RecordingProblemDetailsService();
        var httpContext = NewContext();
        var middleware = CreateMiddleware(recorder,
            _ => throw new PoliPageNotFoundException(PoliPageErrorCode.NotFound, 404, "missing", requestId: "req_x"));

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        recorder.Captured.Should().NotBeNull();
        recorder.Captured!.Extensions["code"].Should().Be(PoliPageErrorCode.NotFound);
        recorder.Captured.Extensions["poliPageRequestId"].Should().Be("req_x");
    }

    [Fact]
    public async Task InvokeAsync_propagates_non_PoliPageException()
    {
        var recorder = new RecordingProblemDetailsService();
        var middleware = CreateMiddleware(recorder, _ => throw new InvalidOperationException("not ours"));
        var httpContext = NewContext();

        Func<Task> act = () => middleware.InvokeAsync(httpContext);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("not ours");
        recorder.Captured.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_rethrows_PoliPageException_when_response_has_started()
    {
        var recorder = new RecordingProblemDetailsService();
        var middleware = CreateMiddleware(recorder,
            _ => throw new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x"));

        var httpContext = NewContext();
        httpContext.Features.Set<IHttpResponseFeature>(new ResponseFeatureWithHasStarted(hasStarted: true));

        Func<Task> act = () => middleware.InvokeAsync(httpContext);

        await act.Should().ThrowAsync<PoliPageAuthException>();
        recorder.Captured.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_falls_back_to_source_gen_write_when_problem_service_returns_false()
    {
        var recorder = new RecordingProblemDetailsService { ReturnValue = false };
        var httpContext = NewContext();
        var middleware = CreateMiddleware(recorder,
            _ => throw new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "denied", requestId: "req_x"));

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("application/problem+json");
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        httpContext.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(httpContext.Response.Body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Authentication failed");
    }

    [Fact]
    public async Task InvokeAsync_passes_through_when_no_exception()
    {
        var recorder = new RecordingProblemDetailsService();
        var middleware = CreateMiddleware(recorder, _ => Task.CompletedTask);
        var httpContext = NewContext();

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        recorder.Captured.Should().BeNull();
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static PoliPageExceptionHandlerMiddleware CreateMiddleware(
        RecordingProblemDetailsService recorder,
        RequestDelegate next,
        Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        var factory = new PoliPageProblemDetailsFactory(Microsoft.Extensions.Options.Options.Create(aspnet));
        return new PoliPageExceptionHandlerMiddleware(
            next, factory, recorder, NullLogger<PoliPageExceptionHandlerMiddleware>.Instance);
    }
}
