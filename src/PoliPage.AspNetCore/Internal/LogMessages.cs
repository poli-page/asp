using Microsoft.Extensions.Logging;

namespace PoliPage.AspNetCore.Internal;

// All logging in this assembly funnels through [LoggerMessage] source-gen extension methods.
// Analyzer CA1848 is on (TWAE) so direct logger.LogXxx calls fail the build — keeping every
// message defined here gives us a single audit surface for event IDs and a uniform AOT-safe
// formatter path.
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "PoliPageException thrown after response started; cannot rewrite headers.")]
    public static partial void ExceptionAfterResponseStarted(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "MapPoliPageSmokeTest is registered without explicit .RequireAuthorization(...) or .AllowAnonymous(). " +
                  "In production this endpoint will burn API quota on unauthenticated callers — gate it or opt out explicitly.")]
    public static partial void SmokeEndpointUnguarded(ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "PoliPage ApiKey ({KeyPrefix}…) does not match the key format pp_<environment>_<32 lowercase hex>. " +
                  "Starting anyway: the Poli Page API decides whether the key is valid.")]
    public static partial void ApiKeyFormatUnexpected(ILogger logger, string keyPrefix);

    // No key text at all: the environment name itself is outside the grammar.
    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "PoliPage ApiKey does not match the key format pp_<environment>_<32 lowercase hex>. " +
                  "Starting anyway: the Poli Page API decides whether the key is valid.")]
    public static partial void ApiKeyFormatUnexpectedNoKeyText(ILogger logger);
}
