using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using PoliPage.AspNetCore.ExceptionHandling;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

public class PoliPageExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_answers_a_PoliPageException_with_its_payload()
    {
        var handler = new PoliPageExceptionHandler(NullLogger<PoliPageExceptionHandler>.Instance);
        var httpContext = NewContext();
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "bad key", requestId: "req_1");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        httpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        (await ReadBodyAsync(httpContext)).ShouldBePayload(PoliPageErrorCode.InvalidApiKey, "bad key", 401, "req_1");
    }

    [Fact]
    public async Task TryHandleAsync_returns_false_for_non_PoliPageException()
    {
        var handler = new PoliPageExceptionHandler(NullLogger<PoliPageExceptionHandler>.Instance);
        var httpContext = NewContext();

        var handled = await handler.TryHandleAsync(
            httpContext, new InvalidOperationException("not ours"), CancellationToken.None);

        handled.Should().BeFalse();
        httpContext.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task TryHandleAsync_returns_false_when_response_has_started()
    {
        var handler = new PoliPageExceptionHandler(NullLogger<PoliPageExceptionHandler>.Instance);
        // DefaultHttpContext's response feature always reports HasStarted == false. Swap in one
        // that reports true to simulate an exception raised after body bytes were sent.
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new ResponseFeatureWithHasStarted(hasStarted: true));

        var handled = await handler.TryHandleAsync(
            httpContext,
            new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x"),
            CancellationToken.None);

        handled.Should().BeFalse();
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return doc.RootElement.Clone();
    }
}
