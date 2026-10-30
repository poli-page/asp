using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class ValidateOnStartTests
{
    private const int KeyFormatEventId = 3001;

    private const string KeyFormatWarning =
        "PoliPage ApiKey does not match the expected format pp_<environment>_<32 lowercase hex characters>. " +
        "Starting anyway: the Poli Page API decides whether the key is valid.";

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Host_startup_fails_on_empty_api_key(string apiKey)
    {
        var logs = new InMemoryLoggerProvider();

        var act = () => StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey));

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*ApiKey*required*");
        logs.Entries.Should().NotContain(e => e.EventId.Id == KeyFormatEventId);
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
        ShouldCarryNoneOf(logs, TestKeys.TextOf(apiKey, TestKeys.MinCheckedEnvLength));
    }

    // Each row: a malformed key, then a part of it that must never reach a log line on top of
    // TestKeys.TextOf (the key, the secret, the pp_{env}_ prefix and the bare {env} segment).
    public static TheoryData<string, string> MalformedKeys => new()
    {
        // Acceptance rows.
        { "pp_sandbox_x", "pp_sandbox_x" },
        { "pp_Sandbox_" + TestKeys.Secret, "Sandbox_" },
        { "pp_2fa_" + TestKeys.Secret, "2fa_" },
        { "pp_a--b_" + TestKeys.Secret, "a--b_" },
        { $"pp_{TestKeys.Env21}_{TestKeys.Secret}", TestKeys.Env21 },
        { "sk_live_" + TestKeys.Secret, "sk_live_" },
        // A valid environment name with a bad secret still shows no part of the key.
        { "pp_eu-prod_nothex", "nothex" },
        // A mis-pasted secret fragment can look like a valid environment name.
        { "pp_abc123_" + TestKeys.Secret[..16], TestKeys.Secret[..16] },
        // 20/21 boundary with a bad secret.
        { $"pp_{TestKeys.Env20}_nothex", "nothex" },
        { $"pp_{TestKeys.Env21}_nothex", "nothex" },
        // .NET's $ matches before a final "\n"; the key check must not.
        { TestKeys.Sandbox + "\n", TestKeys.Sandbox },
        { TestKeys.Sandbox + "\r\n", TestKeys.Sandbox },
        // Hex case and length.
        { "pp_sandbox_" + TestKeys.Secret.ToUpperInvariant(), TestKeys.Secret.ToUpperInvariant() },
        { "pp_sandbox_" + TestKeys.Secret[..31], TestKeys.Secret[..31] },
        { "pp_sandbox_" + TestKeys.Secret + "0", TestKeys.Secret },
        { "pp_sandbox_" + new string('g', 32), new string('g', 32) },
        // Environment names outside the grammar.
        { "pp_0123456789abcdef0123_rest", "0123456789abcdef0123_rest" },
        { "pp_2fa_", "2fa_" },
        { "pp_Sandbox_", "Sandbox_" },
        { "pp_a--b_", "a--b_" },
        { "pp_-eu_", "-eu_" },
        { "pp_eu-_", "eu-_" },
        { "pp_eu_prod_" + TestKeys.Secret, "prod_" + TestKeys.Secret },
    };

    [Theory]
    [MemberData(nameof(MalformedKeys))]
    public async Task Malformed_key_starts_with_exactly_one_warning_that_shows_no_part_of_the_key(
        string apiKey, string hidden)
    {
        var logs = new InMemoryLoggerProvider();

        await StartAndStopAsync(logs, services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey));

        var warning = logs.Entries.Should().ContainSingle(e => e.LogLevel >= LogLevel.Warning).Subject;
        warning.LogLevel.Should().Be(LogLevel.Warning);
        warning.EventId.Id.Should().Be(KeyFormatEventId);
        warning.Message.Should().Be(KeyFormatWarning);

        ShouldCarryNoneOf(logs, TestKeys.TextOf(apiKey).Append(hidden));
        foreach (var entry in logs.Entries)
            entry.Exception.Should().BeNull();
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

        logs.Entries.Should().ContainSingle(e => e.EventId.Id == KeyFormatEventId);
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

    // No log entry's formatted message or structured state, and no scope, may carry any part.
    private static void ShouldCarryNoneOf(InMemoryLoggerProvider logs, IEnumerable<string> parts)
    {
        foreach (var part in parts)
        {
            foreach (var entry in logs.Entries)
            {
                entry.Message.Should().NotContain(part);
                entry.State.Should().NotContain(part);
            }

            foreach (var scope in logs.Scopes)
                scope.Should().NotContain(part);
        }
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
