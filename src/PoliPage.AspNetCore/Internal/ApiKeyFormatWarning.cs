using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore.Internal;

// Runs once per host start, after ValidateOnStart has rejected a missing or empty key. A key
// that does not match the contract only warns: the API answers 401 for a key it does not know.
internal sealed class ApiKeyFormatWarning(
    IOptions<PoliPageClientOptions> options,
    ILogger<ApiKeyFormatWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var apiKey = options.Value.ApiKey;
        if (!ApiKeyFormat.IsWellFormed(apiKey))
        {
            if (ApiKeyFormat.ShownPrefix(apiKey) is { } prefix)
                LogMessages.ApiKeyFormatUnexpected(logger, prefix);
            else
                LogMessages.ApiKeyFormatUnexpectedNoKeyText(logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
