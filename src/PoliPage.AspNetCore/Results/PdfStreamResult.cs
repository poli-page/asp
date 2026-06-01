using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.Results;

internal sealed class PdfStreamResult(Stream pdfStream, string? filename, bool inline)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Take ownership: the SDK's Render.PdfStreamAsync returns a stream that owns the HTTP
        // response message — disposing it here releases the socket back to the pool. Buffering
        // the whole PDF first would defeat the point of using PdfStream over Pdf.
        await using var stream = pdfStream.ConfigureAwait(false);

        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "application/pdf";

        if (filename is not null)
            httpContext.Response.Headers.ContentDisposition =
                ContentDispositionHeader.Build(filename, inline);

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await pdfStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted).ConfigureAwait(false);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(Stream),
            contentTypes: ["application/pdf"]));
    }
}
