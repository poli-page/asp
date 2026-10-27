using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class ValidateOnStartTests
{
    private const int KeyFormatWithPrefixEventId = 3001;
    private const int KeyFormatEventId = 3002;

    [Fact]
    public void ValidateOnStart_throws_on_missing_api_key()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = string.Empty);

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        Action act = () => validator.Validate();
        act.Should().Throw<OptionsValidationException>().WithMessage("*ApiKey*required*");
    }

    [Fact]
    public void ValidateOnStart_throws_on_bad_problem_details_uri()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = TestKeys.Sandbox,
            aspnet => aspnet.ProblemDetailsTypeUri = "not-a-uri");

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        Action act = () => validator.Validate();
        act.Should().Throw<OptionsValidationException>().WithMessage("*ProblemDetailsTypeUri*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Host_startup_fails_on_empty_api_key(string apiKey)
    {
        var logs = new InMemoryLoggerProvider();

        var act = () => StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey));

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*ApiKey*required*");
        logs.Entries.Should().NotContain(e => e.EventId.Id == KeyFormatWithPrefixEventId || e.EventId.Id == KeyFormatEventId);
    }

    [Fact]
    public async Task Host_startup_fails_when_configuration_has_no_api_key()
    {
        var logs = new InMemoryLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PoliPage:MaxRetries"] = "1" })
            .Build();

        var act = () => StartAndStopAsync(
            logs, services => services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage")));

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*ApiKey*required*");
    }

    public static TheoryData<string> WellFormedKeys => new()
    {
        TestKeys.Sandbox,
        TestKeys.Live,
        TestKeys.EuProd,
        // Grammar edges: one letter, letters then digits, several inner hyphens.
        "pp_a_" + TestKeys.Secret,
        "pp_staging2_" + TestKeys.Secret,
        "pp_a1-b2-c3_" + TestKeys.Secret,
        // 20-character environment name: the longest one allowed.
        $"pp_{TestKeys.Env20}_{TestKeys.Secret}",
    };

    [Theory]
    [MemberData(nameof(WellFormedKeys))]
    public async Task Well_formed_key_starts_without_warning(string apiKey)
    {
        var logs = new InMemoryLoggerProvider();

        await StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey));

        logs.Entries.Should().NotContain(e => e.LogLevel >= LogLevel.Warning);
    }

    // Each row: a malformed key, then the pp_{env}_ prefix the warning may show (null: no key
    // text at all), then the part of the key that must never reach a log line.
    public static TheoryData<string, string?, string> MalformedKeys => new()
    {
        // Acceptance rows.
        { "pp_sandbox_x", "pp_sandbox_", "pp_sandbox_x" },
        { "pp_Sandbox_" + TestKeys.Secret, null, "Sandbox_" },
        { "pp_2fa_" + TestKeys.Secret, null, "2fa_" },
        { "pp_a--b_" + TestKeys.Secret, null, "a--b_" },
        { $"pp_{TestKeys.Env21}_{TestKeys.Secret}", null, TestKeys.Env21 },
        { "sk_live_" + TestKeys.Secret, null, "sk_live_" },
        // 20/21 boundary with a bad secret: 20 characters still shows the prefix, 21 does not.
        { $"pp_{TestKeys.Env20}_nothex", $"pp_{TestKeys.Env20}_", "nothex" },
        { $"pp_{TestKeys.Env21}_nothex", null, TestKeys.Env21 },
        // .NET's $ matches before a final "\n"; the key check must not.
        { TestKeys.Sandbox + "\n", "pp_sandbox_", TestKeys.Secret },
        { TestKeys.Sandbox + "\r\n", "pp_sandbox_", TestKeys.Secret },
        // Hex case and length.
        { "pp_sandbox_" + TestKeys.Secret.ToUpperInvariant(), "pp_sandbox_", TestKeys.Secret.ToUpperInvariant() },
        { "pp_sandbox_" + TestKeys.Secret[..31], "pp_sandbox_", TestKeys.Secret[..31] },
        { "pp_sandbox_" + TestKeys.Secret + "0", "pp_sandbox_", TestKeys.Secret },
        { "pp_sandbox_" + new string('g', 32), "pp_sandbox_", new string('g', 32) },
        // Environment names outside the grammar: no key text at all.
        { "pp_0123456789abcdef0123_rest", null, "0123456789abcdef0123_rest" },
        { "pp_2fa_", null, "2fa_" },
        { "pp_Sandbox_", null, "Sandbox_" },
        { "pp_a--b_", null, "a--b_" },
        { "pp_-eu_", null, "-eu_" },
        { "pp_eu-_", null, "eu-_" },
        { "pp_eu_prod_" + TestKeys.Secret, "pp_eu_", "prod_" + TestKeys.Secret },
    };

    [Theory]
    [MemberData(nameof(MalformedKeys))]
    public async Task Malformed_key_starts_with_exactly_one_warning_and_no_secret(
        string apiKey, string? shownPrefix, string hidden)
    {
        var logs = new InMemoryLoggerProvider();

        await StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey));

        var warning = logs.Entries.Should().ContainSingle(e => e.LogLevel >= LogLevel.Warning).Subject;
        warning.LogLevel.Should().Be(LogLevel.Warning);
        if (shownPrefix is null)
        {
            warning.EventId.Id.Should().Be(KeyFormatEventId);
            warning.Message.Should().Be(
                "PoliPage ApiKey does not match the key format pp_<environment>_<32 lowercase hex>. " +
                "Starting anyway: the Poli Page API decides whether the key is valid.");
        }
        else
        {
            warning.EventId.Id.Should().Be(KeyFormatWithPrefixEventId);
            warning.Message.Should().Be(
                $"PoliPage ApiKey ({shownPrefix}…) does not match the key format pp_<environment>_<32 lowercase hex>. " +
                "Starting anyway: the Poli Page API decides whether the key is valid.");
        }

        foreach (var entry in logs.Entries)
        {
            entry.Message.Should().NotContain(hidden).And.NotContain(TestKeys.Secret);
            entry.State.Should().NotContain(hidden).And.NotContain(TestKeys.Secret);
            entry.Exception.Should().BeNull();
        }
    }

    [Fact]
    public async Task Key_warning_is_logged_once_per_host_even_when_options_are_resolved_again()
    {
        var logs = new InMemoryLoggerProvider();

        await StartAndStopAsync(
            logs,
            services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_sandbox_x"),
            host =>
            {
                using var scope = host.Services.CreateScope();
                _ = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<PoliPageClientOptions>>().Value;
                _ = host.Services.GetRequiredService<IOptionsMonitor<PoliPageClientOptions>>().CurrentValue;
                _ = host.Services.GetRequiredService<PoliPageClient>();
            });

        logs.Entries.Should().ContainSingle(e => e.LogLevel >= LogLevel.Warning);
    }

    [Fact]
    public async Task Key_warning_applies_when_the_sdk_was_registered_first()
    {
        var logs = new InMemoryLoggerProvider();

        await StartAndStopAsync(logs, services =>
        {
            services.AddPoliPage(opts => opts.ApiKey = "pp_sandbox_x");
            services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_sandbox_x");
        });

        logs.Entries.Should().ContainSingle(e => e.EventId.Id == KeyFormatWithPrefixEventId);
    }

    [Fact]
    public async Task Key_warning_reads_the_key_bound_from_configuration()
    {
        var logs = new InMemoryLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PoliPage:ApiKey"] = "pp_2fa_" + TestKeys.Secret })
            .Build();

        await StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage")));

        logs.Entries.Should().ContainSingle(e => e.EventId.Id == KeyFormatEventId);
    }

    internal static async Task StartAndStopAsync(
        InMemoryLoggerProvider logs,
        Action<IServiceCollection> configureServices,
        Action<IHost>? whileRunning = null,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        configureLogging?.Invoke(builder.Logging);
        configureServices(builder.Services);

        using var host = builder.Build();
        await host.StartAsync();
        whileRunning?.Invoke(host);
        await host.StopAsync();
    }
}
