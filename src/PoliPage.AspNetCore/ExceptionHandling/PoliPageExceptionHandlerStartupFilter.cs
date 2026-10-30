using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Puts PoliPageExceptionHandlerMiddleware at the start of the request pipeline, so a
// PoliPageException gets the ToPayload() answer even when the app never calls
// app.UseExceptionHandler(). When it does, the PoliPageExceptionHandler registered with it
// answers first and the exception never reaches this middleware.
internal sealed class PoliPageExceptionHandlerStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return app =>
        {
            app.UseMiddleware<PoliPageExceptionHandlerMiddleware>();
            next(app);
        };
    }
}
