using Microsoft.AspNetCore.Http;

namespace PoliPage.AspNetCore.Internal;

// The cache and nosniff headers every response helper writes, from the host's
// PoliPageAspNetCoreOptions. Shared by the Minimal API results and the MVC factory's results so
// the two surfaces cannot drift apart.
internal static class PoliPageResponseHeaders
{
    public static void Apply(HttpResponse response, PoliPageAspNetCoreOptions options)
    {
        if (options.DefaultCacheControl is not null)
            response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
