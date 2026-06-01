using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.Tests.Options;

public class PoliPageAspNetCoreOptionsTests
{
    [Fact]
    public void Defaults_match_documented_values()
    {
        var options = new PoliPageAspNetCoreOptions();

        options.ProblemDetailsTypeUri.Should().Be("https://poli.page/errors");
        options.IncludeRequestIdInProblemDetails.Should().BeTrue();
        options.DefaultCacheControl.Should().Be("no-store, private");
        options.SetNoSniffHeader.Should().BeTrue();
        options.RegisterExceptionHandler.Should().BeTrue();
        options.AddProblemDetailsService.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/errors/poli")]
    public void Validator_accepts_well_formed_absolute_uris(string uri)
    {
        var options = new PoliPageAspNetCoreOptions { ProblemDetailsTypeUri = uri };
        Validators.ValidateProblemDetailsTypeUri(options).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/relative")]
    public void Validator_rejects_malformed_uris(string uri)
    {
        var options = new PoliPageAspNetCoreOptions { ProblemDetailsTypeUri = uri };
        Validators.ValidateProblemDetailsTypeUri(options).Should().BeFalse();
    }
}
