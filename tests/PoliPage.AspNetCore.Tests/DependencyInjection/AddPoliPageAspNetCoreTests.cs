using System.Linq;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class AddPoliPageAspNetCoreTests
{
    [Fact]
    public void Registers_PoliPageClient_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<PoliPageClient>();
        var second = provider.GetRequiredService<PoliPageClient>();
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Registers_aspnetcore_options_with_defaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.RegisterExceptionHandler.Should().BeTrue();
        options.DefaultCacheControl.Should().Be("no-store, private");
        options.SetNoSniffHeader.Should().BeTrue();
    }

    [Fact]
    public void Registers_aspnetcore_options_with_overrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = TestKeys.Sandbox,
            aspnet =>
            {
                aspnet.DefaultCacheControl = "no-cache";
                aspnet.SetNoSniffHeader = false;
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.DefaultCacheControl.Should().Be("no-cache");
        options.SetNoSniffHeader.Should().BeFalse();
    }

    [Fact]
    public void Registers_response_factory_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<PoliPageResponseFactory>();
        var second = provider.GetRequiredService<PoliPageResponseFactory>();
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Double_call_short_circuits_to_a_single_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        services.Count(d => d.ServiceType == typeof(PoliPageResponseFactory)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }

    [Fact]
    public void Sdk_add_then_aspnetcore_short_circuits()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPage(opts => opts.ApiKey = TestKeys.Sandbox);
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }

    [Fact]
    public void RegisterExceptionHandler_enabled_registers_handler_developer_filter_and_startup_filter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        services.Any(d =>
            d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageExceptionHandler")
            .Should().BeTrue();
        services.Any(d =>
            d.ServiceType == typeof(IStartupFilter)
            && d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageExceptionHandlerStartupFilter")
            .Should().BeTrue();
        services.Any(d =>
            d.ServiceType == typeof(Microsoft.AspNetCore.Diagnostics.IDeveloperPageExceptionFilter)
            && d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageDeveloperPageExceptionFilter")
            .Should().BeTrue();
    }

    [Fact]
    public void RegisterExceptionHandler_disabled_skips_handler_developer_filter_and_startup_filter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = TestKeys.Sandbox,
            aspnet => aspnet.RegisterExceptionHandler = false);

        services.Any(d =>
            d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageExceptionHandler")
            .Should().BeFalse();
        services.Any(d => d.ServiceType == typeof(IStartupFilter)).Should().BeFalse();
        services.Any(d => d.ServiceType == typeof(Microsoft.AspNetCore.Diagnostics.IDeveloperPageExceptionFilter))
            .Should().BeFalse();
    }

    [Fact]
    public void AddProblemDetailsService_enabled_registers_problem_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = TestKeys.Sandbox);

        services.Any(d =>
            d.ServiceType.FullName == "Microsoft.AspNetCore.Http.IProblemDetailsService")
            .Should().BeTrue();
    }

    [Fact]
    public void AddProblemDetailsService_disabled_skips_AddProblemDetails()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = TestKeys.Sandbox,
            aspnet =>
            {
                aspnet.AddProblemDetailsService = false;
                // RegisterExceptionHandler stays at default (true) — the test isolates the
                // AddProblemDetailsService flag, but the auto-registered exception handler does
                // not pull AddProblemDetails in via a transitive AddX call.
            });

        services.Any(d =>
            d.ServiceType.FullName == "Microsoft.AspNetCore.Http.IProblemDetailsService")
            .Should().BeFalse();
    }

    [Fact]
    public void Binds_client_options_from_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoliPage:ApiKey"] = TestKeys.Live,
                ["PoliPage:MaxRetries"] = "5",
                ["PoliPage:RequestTimeout"] = "00:00:30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage"));

        using var provider = services.BuildServiceProvider();
        var clientOptions = provider.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
        clientOptions.ApiKey.Should().Be(TestKeys.Live);
        clientOptions.MaxRetries.Should().Be(5);
        clientOptions.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Binds_aspnetcore_options_from_aspnetcore_subsection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoliPage:ApiKey"] = TestKeys.Sandbox,
                ["PoliPage:AspNetCore:RegisterExceptionHandler"] = "false",
                ["PoliPage:AspNetCore:SetNoSniffHeader"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage"));

        using var provider = services.BuildServiceProvider();
        var aspnetOptions = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        aspnetOptions.RegisterExceptionHandler.Should().BeFalse();
        aspnetOptions.SetNoSniffHeader.Should().BeFalse();
    }

    [Fact]
    public void Configuration_plus_callback_overload_applies_callback_after_binding()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoliPage:ApiKey"] = TestKeys.Live,
                ["PoliPage:MaxRetries"] = "5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            configuration.GetSection("PoliPage"),
            opts => opts.MaxRetries = 9);

        using var provider = services.BuildServiceProvider();
        var clientOptions = provider.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
        clientOptions.ApiKey.Should().Be(TestKeys.Live);
        clientOptions.MaxRetries.Should().Be(9);
    }
}
