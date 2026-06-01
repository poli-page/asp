using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Placeholder so AddPoliPageAspNetCore can call services.AddExceptionHandler<PoliPageExceptionHandler>()
// at the DI layer. TryHandleAsync returns false (let other handlers / the default 500 page take over)
// until Task 12 implements the actual PoliPageException → ProblemDetails mapping.
internal sealed class PoliPageExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}
