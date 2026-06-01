namespace PoliPage.AspNetCore.ExampleApp.Endpoints;

internal static class ErrorEndpoints
{
    public static IEndpointRouteBuilder MapErrorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/errors/bad-version", BadVersionAsync);
        return endpoints;
    }

    // Deliberately throws PoliPageValidationException so the IExceptionHandler maps it to
    // a 400 + application/problem+json response. The demo dashboard pretty-prints the JSON
    // body to show what hosts will see when the SDK surfaces a typed exception.
    private static Task BadVersionAsync(HttpContext _) =>
        throw new PoliPageValidationException(
            PoliPageErrorCode.InvalidVersionFormat,
            400,
            "The version 'banana' does not match the required semver pattern.",
            requestId: "req_demo_invalid_version");
}
