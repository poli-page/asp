using Microsoft.AspNetCore.Builder;
using PoliPage.AspNetCore.ExceptionHandling;

namespace PoliPage.AspNetCore;

/// <summary>
/// Extensions for wiring Poli Page middleware into <see cref="IApplicationBuilder"/>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds, at this point of the pipeline, the middleware that answers a
    /// <see cref="PoliPageException"/> with <see cref="PoliPageException.ToPayload"/> as
    /// <c>application/json</c> and its status (500 when the payload has none).
    /// <c>AddPoliPageAspNetCore</c> already adds this middleware at the start of the pipeline
    /// unless <see cref="PoliPageAspNetCoreOptions.RegisterExceptionHandler"/> is
    /// <see langword="false"/>; call this only to place it yourself, for example after
    /// turning the default off.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UsePoliPageExceptionHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PoliPageExceptionHandlerMiddleware>();
    }
}
