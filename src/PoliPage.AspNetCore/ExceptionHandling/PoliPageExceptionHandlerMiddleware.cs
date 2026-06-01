using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageExceptionHandlerMiddleware(
    RequestDelegate next,
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
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

            var problem = factory.Build(httpContext, ex);
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = ex,
            }).ConfigureAwait(false);

            if (!written)
            {
                httpContext.Response.ContentType = "application/problem+json";
                await httpContext.Response.WriteAsJsonAsync(
                    problem,
                    ProblemDetailsJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    cancellationToken: httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
    }
}
