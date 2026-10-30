using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

// The IExceptionHandler that app.UseExceptionHandler() consults. It answers every
// PoliPageException with the SDK's ToPayload() body and leaves other exceptions to the next
// handler.
internal sealed class PoliPageExceptionHandler(ILogger<PoliPageExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not PoliPageException poliEx)
            return false;

        if (httpContext.Response.HasStarted)
        {
            LogMessages.ExceptionAfterResponseStarted(logger, poliEx);
            return false;
        }

        await PoliPageErrorResponse.WriteAsync(httpContext, poliEx, logger, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
