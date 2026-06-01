namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class ValidateOnStartTests
{
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
            opts => opts.ApiKey = "pp_test_x",
            aspnet => aspnet.ProblemDetailsTypeUri = "not-a-uri");

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        Action act = () => validator.Validate();
        act.Should().Throw<OptionsValidationException>().WithMessage("*ProblemDetailsTypeUri*");
    }
}
