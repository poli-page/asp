using System.Linq;

namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class AddPoliPageAspNetCoreTests
{
    [Fact]
    public void Registers_PoliPageClient_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

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

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.ProblemDetailsTypeUri.Should().Be("https://poli.page/errors");
        options.IncludeRequestIdInProblemDetails.Should().BeTrue();
        options.DefaultCacheControl.Should().Be("no-store, private");
        options.SetNoSniffHeader.Should().BeTrue();
    }

    [Fact]
    public void Registers_aspnetcore_options_with_overrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_x",
            aspnet =>
            {
                aspnet.ProblemDetailsTypeUri = "https://example.com/errors";
                aspnet.SetNoSniffHeader = false;
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.ProblemDetailsTypeUri.Should().Be("https://example.com/errors");
        options.SetNoSniffHeader.Should().BeFalse();
    }

    [Fact]
    public void Registers_response_factory_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

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

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Count(d => d.ServiceType == typeof(PoliPageResponseFactory)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }

    [Fact]
    public void Sdk_add_then_aspnetcore_short_circuits()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPage(opts => opts.ApiKey = "pp_test_x");
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }

    [Fact]
    public void RegisterExceptionHandler_enabled_registers_handler()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Any(d =>
            d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageExceptionHandler")
            .Should().BeTrue();
    }

    [Fact]
    public void RegisterExceptionHandler_disabled_skips_IExceptionHandler_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_x",
            aspnet => aspnet.RegisterExceptionHandler = false);

        services.Any(d =>
            d.ImplementationType?.FullName ==
                "PoliPage.AspNetCore.ExceptionHandling.PoliPageExceptionHandler")
            .Should().BeFalse();
    }

    [Fact]
    public void AddProblemDetailsService_enabled_registers_problem_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

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
            opts => opts.ApiKey = "pp_test_x",
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
}
