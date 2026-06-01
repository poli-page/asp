using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PoliPage.AspNetCore.ExceptionHandling;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore;

/// <summary>
/// Extensions for registering Poli Page ASP.NET Core services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Poli Page SDK and ASP.NET Core integration services using callback configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">Callback that populates the SDK's <see cref="PoliPageClientOptions"/>.</param>
    /// <param name="configureAspNetCore">
    /// Optional callback that populates the integration's <see cref="PoliPageAspNetCoreOptions"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddPoliPageAspNetCore(
        this IServiceCollection services,
        Action<PoliPageClientOptions> configureClient,
        Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureClient);

        // Skip the SDK registration if the user already called services.AddPoliPage(...). The
        // symmetric "AddPoliPageAspNetCore then AddPoliPage" case still double-registers — see
        // CLAUDE.md §10.14. The marker check inside AddAspNetCoreServices below guards the
        // double-AddPoliPageAspNetCore case.
        if (!services.Any(d => d.ServiceType == typeof(PoliPageClient)))
            services.AddPoliPage(configureClient);

        return AddAspNetCoreServices(services, configureAspNetCore);
    }

    private static IServiceCollection AddAspNetCoreServices(
        IServiceCollection services,
        Action<PoliPageAspNetCoreOptions>? configure)
    {
        // Marker-gated short-circuit. See spec §7.5 / CLAUDE.md §10.14.
        if (services.Any(d => d.ServiceType == typeof(PoliPageResponseFactory)))
            return services;

        // Snapshot the ASP.NET-side flags eagerly so RegisterExceptionHandler /
        // AddProblemDetailsService are knowable at registration time — they gate the
        // AddExceptionHandler / AddProblemDetails calls below.
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);

        services.AddOptions<PoliPageAspNetCoreOptions>()
            .Configure(o =>
            {
                o.ProblemDetailsTypeUri = aspnet.ProblemDetailsTypeUri;
                o.IncludeRequestIdInProblemDetails = aspnet.IncludeRequestIdInProblemDetails;
                o.DefaultCacheControl = aspnet.DefaultCacheControl;
                o.SetNoSniffHeader = aspnet.SetNoSniffHeader;
                o.RegisterExceptionHandler = aspnet.RegisterExceptionHandler;
                o.AddProblemDetailsService = aspnet.AddProblemDetailsService;
            })
            .Validate(Validators.ValidateProblemDetailsTypeUri,
                "PoliPage.AspNetCore: ProblemDetailsTypeUri must be a well-formed absolute http(s) URI.")
            .ValidateOnStart();

        services.AddSingleton<PoliPageResponseFactory>();
        services.AddSingleton<PoliPageProblemDetailsFactory>();

        if (aspnet.AddProblemDetailsService)
            services.AddProblemDetails();

        if (aspnet.RegisterExceptionHandler)
            services.AddExceptionHandler<PoliPageExceptionHandler>();

        return services;
    }
}
