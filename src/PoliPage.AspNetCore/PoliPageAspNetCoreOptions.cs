namespace PoliPage.AspNetCore;

/// <summary>
/// ASP.NET Core-specific options for the Poli Page integration. Augments the SDK's
/// <see cref="PoliPageClientOptions"/> with knobs that only matter inside an HTTP request pipeline.
/// </summary>
public sealed class PoliPageAspNetCoreOptions
{
    /// <summary>
    /// Default <c>Cache-Control</c> header value applied by <c>PoliPageResults</c> and
    /// <c>PoliPageResponseFactory</c>. Defaults to <c>no-store, private</c>.
    /// Set to <see langword="null"/> to omit the header.
    /// </summary>
    public string? DefaultCacheControl { get; set; } = "no-store, private";

    /// <summary>
    /// Whether response helpers add <c>X-Content-Type-Options: nosniff</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool SetNoSniffHeader { get; set; } = true;

    /// <summary>
    /// Whether <c>AddPoliPageAspNetCore</c> turns on the Poli Page error handler, which answers a
    /// <see cref="PoliPageException"/> with the status of <see cref="PoliPageException.ToPayload"/>
    /// (500 when it has none) and that payload as an <c>application/json</c> body:
    /// <c>{ "code", "message", "status", "requestId" }</c>, and logs it (EventId 1002). It is
    /// registered three ways that answer the same: an
    /// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> for
    /// <c>app.UseExceptionHandler()</c>, a developer exception page filter (Development), and a
    /// middleware at the start of the pipeline, so it works whether or not the app calls
    /// <c>UseExceptionHandler()</c>. Set to <see langword="false"/> to turn all three off and handle
    /// the exception yourself. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RegisterExceptionHandler { get; set; } = true;

    /// <summary>
    /// Whether <c>AddPoliPageAspNetCore</c> calls <c>services.AddProblemDetails()</c>, which
    /// <c>app.UseExceptionHandler()</c> needs when it is called with no path or handler.
    /// Poli Page errors never go through it; it shapes the app's other unhandled exceptions.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AddProblemDetailsService { get; set; } = true;
}
