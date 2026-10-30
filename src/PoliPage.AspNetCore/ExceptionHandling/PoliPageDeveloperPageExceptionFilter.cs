using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

// In Development, WebApplication puts the developer exception page in front of the app's own
// middleware and behind every IStartupFilter, so it would answer a PoliPageException before
// PoliPageExceptionHandlerMiddleware sees it. This filter is the developer page's extension
// point: it answers a PoliPageException with the same ToPayload() body and passes anything else
// on to the page.
internal sealed class PoliPageDeveloperPageExceptionFilter(ILogger<PoliPageDeveloperPageExceptionFilter> logger)
    : IDeveloperPageExceptionFilter
{
    public async Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(errorContext);
        ArgumentNullException.ThrowIfNull(next);

        if (errorContext.Exception is not PoliPageException poliEx)
        {
            await next(errorContext).ConfigureAwait(false);
            return;
        }

        var httpContext = errorContext.HttpContext;
        if (httpContext.Response.HasStarted)
        {
            LogMessages.ExceptionAfterResponseStarted(logger, poliEx);
            await next(errorContext).ConfigureAwait(false);
            return;
        }

        await PoliPageErrorResponse.WriteAsync(httpContext, poliEx, logger, httpContext.RequestAborted).ConfigureAwait(false);
    }
}
