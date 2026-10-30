using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using PoliPage.AspNetCore.ExceptionHandling;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

public class PoliPageExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_answers_a_PoliPageException_with_its_payload()
    {
        var httpContext = NewContext();
        var middleware = CreateMiddleware(
            _ => throw new PoliPageNotFoundException(PoliPageErrorCode.NotFound, 404, "missing", requestId: "req_x"));

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        httpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        httpContext.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(httpContext.Response.Body);
        doc.RootElement.ShouldBePayload(PoliPageErrorCode.NotFound, "missing", 404, "req_x");
    }

    [Fact]
    public async Task InvokeAsync_propagates_non_PoliPageException()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("not ours"));
        var httpContext = NewContext();

        Func<Task> act = () => middleware.InvokeAsync(httpContext);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("not ours");
        httpContext.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_rethrows_PoliPageException_when_response_has_started()
    {
        var middleware = CreateMiddleware(
            _ => throw new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x"));

        var httpContext = NewContext();
        httpContext.Features.Set<IHttpResponseFeature>(new ResponseFeatureWithHasStarted(hasStarted: true));

        Func<Task> act = () => middleware.InvokeAsync(httpContext);

        await act.Should().ThrowAsync<PoliPageAuthException>();
    }

    [Fact]
    public async Task InvokeAsync_passes_through_when_no_exception()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var httpContext = NewContext();

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        httpContext.Response.Body.Length.Should().Be(0);
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static PoliPageExceptionHandlerMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, NullLogger<PoliPageExceptionHandlerMiddleware>.Instance);
}
