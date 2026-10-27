namespace PoliPage.AspNetCore.Tests.Fixtures;

// The one place test API keys are spelled out. Well-formed keys follow the SDK contract
// pp_{env}_{32 lowercase hex}; none of them is a real credential.
internal static class TestKeys
{
    public const string Secret = "0123456789abcdef0123456789abcdef";

    public const string Sandbox = "pp_sandbox_" + Secret;
    public const string Live = "pp_live_" + Secret;
    public const string EuProd = "pp_eu-prod_" + Secret;

    // 20 characters: the longest environment name the contract allows.
    public const string Env20 = "abcdefghij0123456789";

    // 21 characters: one past the limit.
    public const string Env21 = Env20 + "x";
}
