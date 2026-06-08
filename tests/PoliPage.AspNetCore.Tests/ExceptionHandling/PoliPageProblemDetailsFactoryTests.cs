using Microsoft.AspNetCore.Mvc;
using PoliPage.AspNetCore.ExceptionHandling;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

public class PoliPageProblemDetailsFactoryTests
{
    public static TheoryData<PoliPageException, int, string, string> MappingRows => new()
    {
        { new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "bad key", requestId: "req_1"),
          StatusCodes.Status401Unauthorized, "authentication_failed", "Authentication failed" },
        { new PoliPagePaymentRequiredException(PoliPageErrorCode.PaymentRequired, 402, "owed", requestId: "req_1"),
          StatusCodes.Status402PaymentRequired, "payment_required", "Payment required" },
        { new PoliPageNotFoundException(PoliPageErrorCode.NotFound, 404, "missing", requestId: "req_1"),
          StatusCodes.Status404NotFound, "not_found", "Not found" },
        { new PoliPageGoneException(PoliPageErrorCode.Gone, 410, "gone", requestId: "req_1"),
          StatusCodes.Status410Gone, "gone", "Resource permanently gone" },
        { new PoliPageValidationException(PoliPageErrorCode.ValidationError, 400, "bad input", requestId: "req_1"),
          StatusCodes.Status400BadRequest, "validation_failed", "Validation failed" },
        { new PoliPageValidationException(PoliPageErrorCode.ValidationError, 422, "schema fail", requestId: "req_1"),
          StatusCodes.Status422UnprocessableEntity, "validation_failed", "Validation failed" },
        { new PoliPageRateLimitException(PoliPageErrorCode.QuotaExceeded, 429, "slow down",
                                          requestId: "req_1", retryAfter: TimeSpan.FromSeconds(7)),
          StatusCodes.Status429TooManyRequests, "rate_limited", "Rate limit exceeded" },
        { new PoliPageNetworkException(PoliPageErrorCode.NetworkError, "DNS fail", new HttpRequestException("dns")),
          StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "Upstream unavailable" },
        { new PoliPageDownloadException(PoliPageErrorCode.DownloadFailed, 0, "s3 503", requestId: "req_1"),
          StatusCodes.Status502BadGateway, "download_failed", "Stored document download failed" },
    };

    [Theory]
    [MemberData(nameof(MappingRows))]
    public void Build_maps_exception_to_problem_details(
        PoliPageException exception, int expectedStatus, string expectedCode, string expectedTitle)
    {
        var factory = CreateFactory();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/test";
        httpContext.Request.QueryString = new QueryString("?x=1");

        var problem = factory.Build(httpContext, exception);

        problem.Status.Should().Be(expectedStatus);
        problem.Title.Should().Be(expectedTitle);
        problem.Type.Should().Be($"https://poli.page/errors#{expectedCode}");
        problem.Detail.Should().Be(exception.Message);
        problem.Instance.Should().Be("/test?x=1");
        // `code` extension is the API's verbatim code, not the framework problem-code.
        problem.Extensions["code"].Should().Be(exception.Code);
    }

    [Fact]
    public void Build_uses_504_for_base_exception_with_timeout_code()
    {
        var factory = CreateFactory();
        var exception = new PoliPageException(PoliPageErrorCode.Timeout, statusCode: 0, "deadline");

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Status.Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    [Fact]
    public void Build_includes_request_id_when_option_enabled()
    {
        var factory = CreateFactory();
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x", requestId: "req_xyz");

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Extensions["poliPageRequestId"].Should().Be("req_xyz");
    }

    [Fact]
    public void Build_omits_request_id_when_option_disabled()
    {
        var factory = CreateFactory(opts => opts.IncludeRequestIdInProblemDetails = false);
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x", requestId: "req_xyz");

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Extensions.Should().NotContainKey("poliPageRequestId");
    }

    [Fact]
    public void Build_omits_request_id_when_exception_has_no_request_id()
    {
        var factory = CreateFactory();
        // PoliPageNetworkException constructor omits requestId by design.
        var exception = new PoliPageNetworkException(PoliPageErrorCode.NetworkError, "dns", new HttpRequestException("x"));

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Extensions.Should().NotContainKey("poliPageRequestId");
    }

    [Fact]
    public void Build_includes_retry_after_seconds_when_rate_limit_has_retry_after()
    {
        var factory = CreateFactory();
        var exception = new PoliPageRateLimitException(PoliPageErrorCode.QuotaExceeded, 429, "slow",
            requestId: "req_1", retryAfter: TimeSpan.FromMilliseconds(6500));

        var problem = factory.Build(new DefaultHttpContext(), exception);

        // Ceiling of 6.5s → 7s.
        problem.Extensions["retryAfterSeconds"].Should().Be(7);
    }

    [Fact]
    public void Build_omits_retry_after_when_rate_limit_has_no_retry_after()
    {
        var factory = CreateFactory();
        var exception = new PoliPageRateLimitException(PoliPageErrorCode.QuotaExceeded, 429, "slow");

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Extensions.Should().NotContainKey("retryAfterSeconds");
    }

    [Fact]
    public void Build_falls_through_to_500_for_generic_PoliPageException()
    {
        var factory = CreateFactory();
        var exception = new PoliPageException("generic");

        var problem = factory.Build(new DefaultHttpContext(), exception);

        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Extensions["code"].Should().Be(PoliPageErrorCode.Unknown);
    }

    [Fact]
    public void Build_sets_trace_id_from_http_context()
    {
        var factory = CreateFactory();
        var httpContext = new DefaultHttpContext { TraceIdentifier = "0HMVLOAD123" };
        var exception = new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "x");

        var problem = factory.Build(httpContext, exception);

        problem.Extensions["traceId"].Should().Be("0HMVLOAD123");
    }

    private static PoliPageProblemDetailsFactory CreateFactory(Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        return new PoliPageProblemDetailsFactory(Microsoft.Extensions.Options.Options.Create(aspnet));
    }
}
