using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageProblemDetailsFactory
{
    private readonly PoliPageAspNetCoreOptions _options;

    public PoliPageProblemDetailsFactory(IOptions<PoliPageAspNetCoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public ProblemDetails Build(HttpContext httpContext, PoliPageException exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (status, code, title) = Map(exception);

        var problem = new ProblemDetails
        {
            Type = $"{_options.ProblemDetailsTypeUri}#{code}",
            Title = title,
            Status = status,
            Detail = exception.Message,
            Instance = httpContext.Request.Path + httpContext.Request.QueryString,
        };

        problem.Extensions["code"] = code;

        if (_options.IncludeRequestIdInProblemDetails && exception.RequestId is { } requestId)
            problem.Extensions["poliPageRequestId"] = requestId;

        if (exception is PoliPageRateLimitException { RetryAfter: { } retryAfter })
            problem.Extensions["retryAfterSeconds"] = (int)Math.Ceiling(retryAfter.TotalSeconds);

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
            problem.Extensions["traceId"] = traceId;

        return problem;
    }

    // Status / problem-code / title resolution. Confirmed against
    // /Users/mickael/Projects/sdk-csharp/src/PoliPage/Exceptions/ on 2026-06-01
    // (see docs/sdk-surface-audit-2026-06-01.md §0.1). The exception's wire-level
    // Code is exposed separately under Extensions["code"]; the label below is the
    // public problem-code that surfaces in the Type URI fragment.
    private static (int status, string code, string title) Map(PoliPageException exception)
        => exception switch
        {
            PoliPageAuthException => (StatusCodes.Status401Unauthorized, "authentication_failed", "Authentication failed"),
            PoliPagePaymentRequiredException => (StatusCodes.Status402PaymentRequired, "payment_required", "Payment required"),
            PoliPageNotFoundException => (StatusCodes.Status404NotFound, "not_found", "Not found"),
            PoliPageGoneException => (StatusCodes.Status410Gone, "gone", "Resource permanently gone"),
            PoliPageValidationException ex => (ex.StatusCode == 400
                                                ? StatusCodes.Status400BadRequest
                                                : StatusCodes.Status422UnprocessableEntity,
                                               "validation_failed", "Validation failed"),
            PoliPageRateLimitException => (StatusCodes.Status429TooManyRequests, "rate_limited", "Rate limit exceeded"),
            PoliPageNetworkException => (StatusCodes.Status502BadGateway, "upstream_unavailable", "Upstream unavailable"),
            PoliPageDownloadException => (StatusCodes.Status502BadGateway, "download_failed", "Stored document download failed"),
            _ => (StatusCodes.Status500InternalServerError, "poli_page_error", "Poli Page error"),
        };
}
