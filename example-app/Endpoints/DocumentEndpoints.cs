using PoliPage.AspNetCore.Results;

namespace PoliPage.AspNetCore.ExampleApp.Endpoints;

internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/documents", CreateAsync);
        endpoints.MapGet("/documents/{id}", GetAsync);
        endpoints.MapGet("/documents/{id}/preview", PreviewAsync);
        endpoints.MapGet("/documents/{id}/thumbnails", ThumbnailsAsync);
        endpoints.MapDelete("/documents/{id}", DeleteAsync);

        return endpoints;
    }

    private static async Task CreateAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var descriptor = await client.Render.DocumentAsync(
            new ProjectModeInput
            {
                Project = "getting-started",
                Template = "welcome",
                Version = "1.0.0",
                Data = new { name = "PoliPage.AspNetCore" },
            },
            cancellationToken: httpContext.RequestAborted);

        await TypedResults.Ok(descriptor).ExecuteAsync(httpContext);
    }

    private static async Task GetAsync(HttpContext httpContext, string id)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var descriptor = await client.Documents.GetAsync(id, cancellationToken: httpContext.RequestAborted);
        // 302 to the presigned URL: an iframe navigation follows it without CORS.
        await PoliPageResults.DocumentRedirect(descriptor.PresignedPdfUrl).ExecuteAsync(httpContext);
    }

    private static async Task PreviewAsync(HttpContext httpContext, string id)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var preview = await client.Documents.PreviewAsync(id, cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.Preview(preview.Html).ExecuteAsync(httpContext);
    }

    private static async Task ThumbnailsAsync(HttpContext httpContext, string id)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var thumbnails = await client.Documents.ThumbnailsAsync(
            id,
            new ThumbnailOptions { Width = 240 },
            cancellationToken: httpContext.RequestAborted);
        await TypedResults.Ok(thumbnails).ExecuteAsync(httpContext);
    }

    private static async Task DeleteAsync(HttpContext httpContext, string id)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        await client.Documents.DeleteAsync(id, cancellationToken: httpContext.RequestAborted);
        await TypedResults.NoContent().ExecuteAsync(httpContext);
    }
}
