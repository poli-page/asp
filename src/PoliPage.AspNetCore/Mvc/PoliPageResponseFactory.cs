using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore;

/// <summary>
/// DI-resolvable factory that constructs <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/>
/// instances for MVC controllers wrapping Poli Page render output. Holds a snapshot of
/// <see cref="PoliPageAspNetCoreOptions"/> so per-call header decisions (Cache-Control, nosniff)
/// stay consistent with the options the host configured at startup.
/// </summary>
public sealed class PoliPageResponseFactory
{
    private readonly PoliPageAspNetCoreOptions _options;

    /// <summary>
    /// Initialises a new <see cref="PoliPageResponseFactory"/> with the supplied options.
    /// </summary>
    /// <param name="options">The ASP.NET Core integration options snapshot.</param>
    public PoliPageResponseFactory(IOptions<PoliPageAspNetCoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    // Real Pdf / PdfStream / Preview / DocumentRedirect methods land in Task 11.
    internal PoliPageAspNetCoreOptions Options => _options;
}
