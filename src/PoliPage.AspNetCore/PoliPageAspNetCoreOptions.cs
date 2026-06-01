namespace PoliPage.AspNetCore;

/// <summary>
/// ASP.NET Core-specific options for the Poli Page integration. Augments the SDK's
/// <see cref="PoliPageClientOptions"/> with knobs that only matter inside an HTTP request pipeline.
/// </summary>
public sealed class PoliPageAspNetCoreOptions
{
    /// <summary>
    /// The <c>type</c> URI returned in ProblemDetails responses written by the Poli Page
    /// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> and the fallback
    /// <c>UsePoliPageExceptionHandler</c> middleware. The exception's wire-level
    /// <see cref="PoliPageException.Code"/> is appended as a fragment.
    /// Defaults to <c>https://poli.page/errors</c>.
    /// </summary>
    public string ProblemDetailsTypeUri { get; set; } = "https://poli.page/errors";

    /// <summary>
    /// Whether to include the SDK-provided <see cref="PoliPageException.RequestId"/> in the
    /// ProblemDetails extensions under <c>poliPageRequestId</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeRequestIdInProblemDetails { get; set; } = true;

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
    /// Whether <c>AddPoliPageAspNetCore</c> auto-registers the
    /// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> implementation that
    /// maps <see cref="PoliPageException"/> to ProblemDetails. Set to <see langword="false"/>
    /// when the host uses the fallback <c>UsePoliPageExceptionHandler</c> middleware instead
    /// (see CLAUDE.md §10.11 for why you should not enable both).
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool RegisterExceptionHandler { get; set; } = true;

    /// <summary>
    /// Whether <c>AddPoliPageAspNetCore</c> calls <c>services.AddProblemDetails()</c> so the
    /// Poli Page handler can delegate to <c>IProblemDetailsService</c> (which respects any
    /// host-side <c>CustomizeProblemDetails</c> callbacks). Defaults to <see langword="true"/>.
    /// </summary>
    public bool AddProblemDetailsService { get; set; } = true;
}
