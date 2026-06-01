using PoliPage.AspNetCore.Results;

namespace PoliPage.AspNetCore.ExampleApp.Endpoints;

internal static class RenderEndpoints
{
    public static IEndpointRouteBuilder MapRenderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/render/welcome", RenderWelcomeAsync);
        endpoints.MapGet("/render/welcome/inline", RenderWelcomeInlineAsync);
        endpoints.MapGet("/render/welcome/stream", RenderWelcomeStreamAsync);
        endpoints.MapGet("/render/welcome/preview", RenderWelcomePreviewAsync);

        return endpoints;
    }

    private static async Task RenderWelcomeAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var pdf = await client.Render.PdfAsync(
            WelcomeInput("PoliPage.AspNetCore"),
            cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.Pdf(pdf, "welcome.pdf").ExecuteAsync(httpContext);
    }

    private static async Task RenderWelcomeInlineAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var pdf = await client.Render.PdfAsync(
            WelcomeInput("PoliPage.AspNetCore"),
            cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.Pdf(pdf, "welcome.pdf", inline: true).ExecuteAsync(httpContext);
    }

    private static async Task RenderWelcomeStreamAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var stream = await client.Render.PdfStreamAsync(
            WelcomeInput("PoliPage.AspNetCore"),
            cancellationToken: httpContext.RequestAborted);
        await PoliPageResults.PdfStream(stream, "welcome-stream.pdf", inline: true).ExecuteAsync(httpContext);
    }

    private static async Task RenderWelcomePreviewAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        var preview = await client.Render.PreviewAsync(
            WelcomeInput("PoliPage.AspNetCore"),
            cancellationToken: httpContext.RequestAborted);
        var html = string.Concat(preview.Pages);
        await PoliPageResults.Preview(html).ExecuteAsync(httpContext);
    }

    private static ProjectModeInput WelcomeInput(string name) => new()
    {
        Project = "getting-started",
        Template = "welcome",
        Version = "1.0.0",
        Data = new { name },
    };
}
