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
    [InlineData("pp_sandbox_" + TestKeys.Secret + "\n", "pp_sandbox_")]
    [InlineData("pp_Sandbox_" + TestKeys.Secret, null)]
    [InlineData("pp_" + TestKeys.Env21 + "_" + TestKeys.Secret, null)]
    [InlineData("sk_live_" + TestKeys.Secret, null)]
    public async Task Console_output_never_carries_the_key(string apiKey, string? shownPrefix)
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

        var output = stdout.ToString() + stderr.ToString();
        output.Should().Contain("does not match the key format", "the console logger must have flushed the warning");
        output.Should().NotContain(TestKeys.Secret);
        if (shownPrefix is null)
            output.Should().NotContain(apiKey[..^TestKeys.Secret.Length]);
        else
            output.Should().Contain(shownPrefix + "…");
    }
}
