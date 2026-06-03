using PoliPage.AspNetCore.Results;

namespace PoliPage.AspNetCore.ExampleApp.Endpoints;

internal static class RenderEndpoints
{
    public static IEndpointRouteBuilder MapRenderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/render/pdf", RenderPdfAsync);
        endpoints.MapGet("/render/stream", RenderStreamAsync);
        endpoints.MapGet("/render/preview", RenderPreviewAsync);
        endpoints.MapPost("/render/file", RenderFileAsync);

        return endpoints;
    }

    private static async Task RenderPdfAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var pdf = await client.Render.PdfAsync(WelcomeInput(), cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.Pdf(pdf, "welcome.pdf", inline: true).ExecuteAsync(httpContext);
    }

    private static async Task RenderStreamAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var stream = await client.Render.PdfStreamAsync(WelcomeInput(), cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.PdfStream(stream, "welcome-stream.pdf", inline: true).ExecuteAsync(httpContext);
    }

    private static async Task RenderPreviewAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var preview = await client.Render.PreviewAsync(WelcomeInput(), cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.Preview(preview.Html).ExecuteAsync(httpContext);
    }

    private static async Task<IResult> RenderFileAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var environment = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        var path = Path.Combine(environment.ContentRootPath, "output", "welcome.pdf");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await client.RenderToFileAsync(WelcomeInput(), path, cancellationToken: httpContext.RequestAborted);

        var sizeBytes = new FileInfo(path).Length;
        return TypedResults.Ok(new RenderFileResponse(path, sizeBytes));
    }

    private sealed record RenderFileResponse(string Path, long SizeBytes);

    private static ProjectModeInput WelcomeInput() => new()
    {
        Project = "getting-started",
        Template = "welcome",
        Version = "1.0.0",
        Data = new { name = "PoliPage.AspNetCore" },
    };
}
