using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Internal;
using PoliPage.AspNetCore.Results;

namespace PoliPage.AspNetCore;

/// <summary>
/// Endpoint routing extensions for the Poli Page integration.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a <c>GET</c> endpoint that renders the well-known
    /// <c>getting-started/welcome</c> template via the resolved <see cref="PoliPageClient"/>.
    /// Use it as a post-deploy smoke probe to confirm credentials, network reachability, and
    /// the integration's response pipeline are all working end-to-end.
    /// </summary>
    /// <remarks>
    /// The endpoint logs a startup warning when registered without an explicit authorization
    /// decision (<c>.RequireAuthorization(...)</c> or <c>.AllowAnonymous()</c>). Unauthenticated
    /// callers in production will burn API quota — gate the endpoint or silence the warning
    /// by opting into <c>.AllowAnonymous()</c> deliberately.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">URL pattern. Defaults to <c>/poli-page/smoke</c>.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for further configuration.</returns>
    public static IEndpointConventionBuilder MapPoliPageSmokeTest(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/poli-page/smoke")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Use the RequestDelegate overload (not the typed lambda one) so MapGet doesn't trip
        // the IL2026/IL3050 trim/AOT analyzers — the typed delegate path uses reflection on
        // parameter types, which is not AOT-safe.
        var convention = endpoints.MapGet(pattern, HandleAsync);

        convention.Add(endpointBuilder => endpointBuilder.Metadata.Add(new PoliPageSmokeEndpointMarker()));

        endpoints.ServiceProvider
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted
            .Register(() => WarnIfSmokeEndpointIsUnguarded(endpoints));

        return convention;
    }

    private static async Task HandleAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();

        var pdf = await client.Render.PdfAsync(
            new ProjectModeInput
            {
                Project = "getting-started",
                Template = "welcome",
                Version = "1.0.0",
                Data = new { name = "PoliPage.AspNetCore" },
            },
            cancellationToken: httpContext.RequestAborted).ConfigureAwait(false);

        await PoliPageResults.Pdf(pdf, "welcome.pdf", inline: true)
            .ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private static void WarnIfSmokeEndpointIsUnguarded(IEndpointRouteBuilder endpoints)
    {
        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("PoliPage.AspNetCore.SmokeTest");

        foreach (var endpoint in endpoints.DataSources.SelectMany(s => s.Endpoints))
        {
            if (endpoint.Metadata.GetMetadata<PoliPageSmokeEndpointMarker>() is null) continue;
            if (endpoint.Metadata.GetMetadata<AuthorizeAttribute>() is not null) return;
            if (endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>() is not null) return;
            LogMessages.SmokeEndpointUnguarded(logger);
            return;
        }
    }
}
