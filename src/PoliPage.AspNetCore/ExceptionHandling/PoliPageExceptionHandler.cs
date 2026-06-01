using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageExceptionHandler(
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
    ILogger<PoliPageExceptionHandler> logger) : IExceptionHandler
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

        var problem = factory.Build(httpContext, poliEx);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, poliEx.Message);
        if (problem.Extensions.TryGetValue("code", out var code) && code is not null)
            Activity.Current?.AddTag("polipage.error.code", code);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        }).ConfigureAwait(false);
    }
}
