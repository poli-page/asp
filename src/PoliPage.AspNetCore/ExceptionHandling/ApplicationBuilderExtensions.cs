using Microsoft.AspNetCore.Builder;
using PoliPage.AspNetCore.ExceptionHandling;

namespace PoliPage.AspNetCore;

/// <summary>
/// Extensions for wiring Poli Page middleware into <see cref="IApplicationBuilder"/>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds a fallback middleware that maps <see cref="PoliPageException"/> to RFC 7807
    /// ProblemDetails for hosts that do not call <c>app.UseExceptionHandler()</c>. .NET 8+
    /// hosts using <c>UseExceptionHandler()</c> already get the primary
    /// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> path and do NOT need
    /// this call — see CLAUDE.md §10.11 for the "don't enable both" rule.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UsePoliPageExceptionHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PoliPageExceptionHandlerMiddleware>();
    }
}
