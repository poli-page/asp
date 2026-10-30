namespace PoliPage.AspNetCore.Tests.Options;

public class PoliPageAspNetCoreOptionsTests
{
    [Fact]
    public void Defaults_match_documented_values()
    {
        var options = new PoliPageAspNetCoreOptions();

        options.DefaultCacheControl.Should().Be("no-store, private");
        options.SetNoSniffHeader.Should().BeTrue();
        options.RegisterExceptionHandler.Should().BeTrue();
        options.AddProblemDetailsService.Should().BeTrue();
    }
}
