using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Catches a PoliPageException thrown further down the pipeline and answers it with the SDK's
// ToPayload() body. AddPoliPageAspNetCore puts it at the start of the pipeline through
// PoliPageExceptionHandlerStartupFilter; app.UsePoliPageExceptionHandler() places it explicitly.
internal sealed class PoliPageExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<PoliPageExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (PoliPageException ex)
        {
            if (httpContext.Response.HasStarted)
            {
                LogMessages.ExceptionAfterResponseStarted(logger, ex);
                throw;
            }

            await PoliPageErrorResponse.WriteAsync(httpContext, ex, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
