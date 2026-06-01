using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore.Results;

// Named HtmlPreviewResult (not PreviewResult) to disambiguate from PoliPage.PreviewResult,
// which the SDK ships as the JSON-shaped return type of Render.PreviewAsync. C#'s name
// resolution prefers enclosing-namespace types over file-level using aliases, so a bare
// "PreviewResult" inside any namespace nested under PoliPage.* resolves to the SDK type.
// This class is internal — consumers see PoliPageResults.Preview(string) instead.
internal sealed class HtmlPreviewResult(string html) : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "text/html; charset=utf-8";

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await httpContext.Response.WriteAsync(html, httpContext.RequestAborted).ConfigureAwait(false);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(string),
            contentTypes: ["text/html; charset=utf-8"]));
    }
}
