using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageProblemDetailsFactory
{
    private readonly PoliPageAspNetCoreOptions _options;

    public PoliPageProblemDetailsFactory(IOptions<PoliPageAspNetCoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    // Build(HttpContext, PoliPageException) lands in Task 12 with the actual SDK exception mapping.
    internal PoliPageAspNetCoreOptions Options => _options;
}
