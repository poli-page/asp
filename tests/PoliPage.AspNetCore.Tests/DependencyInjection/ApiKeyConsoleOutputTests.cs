using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.DependencyInjection;

// Redirects the process-wide Console, so nothing else may run alongside it.
[CollectionDefinition(nameof(ApiKeyConsoleOutputTests), DisableParallelization = true)]
public sealed class ApiKeyConsoleOutputSerial;

[Collection(nameof(ApiKeyConsoleOutputTests))]
public class ApiKeyConsoleOutputTests
{
    [Theory]
    // Each row: a malformed key, then a part of it that must never reach the console on top of
    // TestKeys.TextOf (the key, the secret, the pp_{env}_ prefix and the bare {env} segment).
    [InlineData("pp_sandbox_" + TestKeys.Secret + "\n", "pp_sandbox_" + TestKeys.Secret)]
    [InlineData("pp_sandbox_x", "pp_sandbox_x")]
    [InlineData("pp_abc123_0123456789abcdef", "0123456789abcdef")]
    [InlineData("pp_Sandbox_" + TestKeys.Secret, "Sandbox_")]
    [InlineData("pp_" + TestKeys.Env21 + "_" + TestKeys.Secret, TestKeys.Env21)]
    [InlineData("sk_live_" + TestKeys.Secret, "sk_live_")]
    public async Task Console_output_never_carries_the_key(string apiKey, string hidden)
    {
        var output = await ConsoleOutputOfAsync(apiKey);

        output.Should().Contain("does not match the expected format", "the console logger must have flushed the warning");
        foreach (var part in TestKeys.TextOf(apiKey).Append(hidden))
            output.Should().NotContain(part);
    }

    [Theory]
    [InlineData(TestKeys.Sandbox)]
    [InlineData(TestKeys.EuProd)]
    [InlineData("pp_a_" + TestKeys.Secret)]
    public async Task Console_output_never_carries_a_well_formed_key(string apiKey)
    {
        var output = await ConsoleOutputOfAsync(apiKey);

        output.Should().Contain("Hosting stopped", "the console logger must have flushed the host's last line");
        output.Should().NotContain("does not match the expected format");
        foreach (var part in TestKeys.TextOf(apiKey, TestKeys.MinCheckedEnvLength))
            output.Should().NotContain(part);
    }

    private static async Task<string> ConsoleOutputOfAsync(string apiKey)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            await ValidateOnStartTests.StartAndStopAsync(
                new InMemoryLoggerProvider(),
                services => services.AddPoliPageAspNetCore(opts => opts.ApiKey = apiKey),
                configureLogging: logging => logging.AddSimpleConsole());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return stdout.ToString() + stderr.ToString();
    }
}
