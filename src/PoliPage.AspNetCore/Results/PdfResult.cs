using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.Results;

internal sealed class PdfResult(byte[] pdf, string? filename, bool inline)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "application/pdf";
        httpContext.Response.ContentLength = pdf.Length;

        if (filename is not null)
            httpContext.Response.Headers.ContentDisposition =
                ContentDispositionHeader.Build(filename, inline);

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await httpContext.Response.Body.WriteAsync(pdf, httpContext.RequestAborted).ConfigureAwait(false);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(byte[]),
            contentTypes: ["application/pdf"]));
    }
}
