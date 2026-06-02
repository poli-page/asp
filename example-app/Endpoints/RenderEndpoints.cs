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

    private static ProjectModeInput WelcomeInput() => new()
    {
        Project = "getting-started",
        Template = "welcome",
        Version = "1.0.0",
        Data = new { name = "PoliPage.AspNetCore" },
    };
}
