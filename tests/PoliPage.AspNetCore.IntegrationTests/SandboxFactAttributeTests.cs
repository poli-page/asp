using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.IntegrationTests;

public class SandboxFactAttributeTests
{
    [Fact]
    public void A_well_formed_sandbox_key_runs_the_live_tests() =>
        SandboxFactAttribute.IsSandboxKey(TestKeys.Sandbox).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pp_sandbox_")]
    [InlineData("pp_sandbox_x")]
    [InlineData(TestKeys.Sandbox + "\n")]
    [InlineData(TestKeys.Sandbox + "0")]
    [InlineData("pp_sandbox_0123456789abcdef0123456789abcde")]
    [InlineData("pp_sandbox_0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("pp_sandbox_ghijklmnopqrstuvwxyzghijklmnopqr")]
    [InlineData(TestKeys.Live)]
    [InlineData(TestKeys.EuProd)]
    public void Any_other_value_skips_the_live_tests(string? key) =>
        SandboxFactAttribute.IsSandboxKey(key).Should().BeFalse();
}
