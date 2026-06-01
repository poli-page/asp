namespace PoliPage.AspNetCore.Tests.Fixtures;

internal static class ResultTestContext
{
    public static (DefaultHttpContext context, MemoryStream body) Create(
        Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x", configure);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        return (httpContext, body);
    }
}
