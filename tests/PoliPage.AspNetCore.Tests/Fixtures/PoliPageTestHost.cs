using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// In-process WebApplication backed by Microsoft.AspNetCore.TestHost. Replaces the SDK's
// PoliPageClient singleton with one whose HttpClients are backed by StubPoliPageHttpHandler —
// the SDK's registration overwrites options.HttpClient unconditionally via "opts with",
// so PostConfigure<PoliPageClientOptions> alone is not enough (see Task 13 commit body).
internal sealed class PoliPageTestHost : IAsyncDisposable
{
    public WebApplication App { get; }
    public StubPoliPageHttpHandler Stub { get; }
    public InMemoryLoggerProvider Logs { get; }
    public HttpClient Client { get; }

    private readonly HttpClient _stubApiClient;
    private readonly HttpClient _stubDownloadClient;

    private PoliPageTestHost(
        WebApplication app,
        StubPoliPageHttpHandler stub,
        InMemoryLoggerProvider logs,
        HttpClient stubApiClient,
        HttpClient stubDownloadClient)
    {
        App = app;
        Stub = stub;
        Logs = logs;
        _stubApiClient = stubApiClient;
        _stubDownloadClient = stubDownloadClient;
        Client = app.GetTestClient();
    }

    public static async Task<PoliPageTestHost> StartAsync(
        Action<WebApplication> configureApp,
        Action<PoliPageAspNetCoreOptions>? configureAspNet = null,
        bool useFallbackMiddleware = false,
        bool wireDefaultExceptionHandler = true)
    {
        ArgumentNullException.ThrowIfNull(configureApp);

        var stub = new StubPoliPageHttpHandler();
        var logs = new InMemoryLoggerProvider();

        // disposeHandler:false so the WebApplication doesn't dispose the singleton stub when
        // it disposes its HttpClient instances on shutdown.
        var stubApiClient = new HttpClient(stub, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.stub.invalid"),
        };
        var stubDownloadClient = new HttpClient(stub, disposeHandler: false);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_factory",
            configureAspNet);

        // Replace the SDK's PoliPageClient registration. The SDK's singleton factory does
        // "opts with { HttpClient = factory.CreateClient(...) }" which discards anything set
        // via PostConfigure<PoliPageClientOptions>. Re-registering at this layer lets us
        // honour the stub-backed HttpClient instances.
        builder.Services.RemoveAll<PoliPageClient>();
        builder.Services.AddSingleton<PoliPageClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
            return new PoliPageClient(opts with
            {
                HttpClient = stubApiClient,
                DownloadHttpClient = stubDownloadClient,
            });
        });

        var app = builder.Build();

#if NET8_0
        // Why: Microsoft.AspNetCore.TestHost on net8.0 ships ResponseBodyPipeWriter without
        // an UnflushedBytes override, and the framework's WriteAsJsonAsync (used by
        // IProblemDetailsService.TryWriteAsync) requires it. Wrap Response.Body in a
        // MemoryStream so the framework synthesises a StreamPipeWriter — which does
        // implement UnflushedBytes — over the buffer. Fixed in net9+ TestHost; see
        // dotnet/runtime#108075.
        app.Use(static async (ctx, next) =>
        {
            var original = ctx.Response.Body;
            using var buffer = new MemoryStream();
            ctx.Response.Body = buffer;
            try
            {
                await next().ConfigureAwait(false);
                buffer.Position = 0;
                await buffer.CopyToAsync(original).ConfigureAwait(false);
            }
            finally
            {
                ctx.Response.Body = original;
            }
        });
#endif

        if (wireDefaultExceptionHandler)
        {
            if (useFallbackMiddleware)
                app.UsePoliPageExceptionHandler();
            else
                app.UseExceptionHandler();
        }

        configureApp(app);

        await app.StartAsync().ConfigureAwait(false);
        return new PoliPageTestHost(app, stub, logs, stubApiClient, stubDownloadClient);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _stubApiClient.Dispose();
        _stubDownloadClient.Dispose();
        await App.StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
    }
}
