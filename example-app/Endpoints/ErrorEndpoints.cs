namespace PoliPage.AspNetCore.ExampleApp.Endpoints;

internal static class ErrorEndpoints
{
    public static IEndpointRouteBuilder MapErrorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/errors/not-found", NotFoundAsync);
        return endpoints;
    }

    // A real render of a template that does not exist: the API answers 404 TEMPLATE_NOT_FOUND,
    // the SDK throws PoliPageNotFoundException, and the error handler AddPoliPageAspNetCore
    // turns on answers 404 with its ToPayload() body: { code, message, status, requestId }.
    // No try/catch here: the default handler does it.
    private static async Task NotFoundAsync(HttpContext httpContext)
    {
        var client = httpContext.RequestServices.GetRequiredService<PoliPageClient>();
        await client.Render.PdfAsync(
            new RenderInput
            {
                Project = "getting-started",
                Template = "does-not-exist",
                Data = new { },
            },
            cancellationToken: httpContext.RequestAborted);

        await TypedResults.Problem(
                detail: "Expected 404 TEMPLATE_NOT_FOUND, but the render succeeded.",
                statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(httpContext);
    }
}
