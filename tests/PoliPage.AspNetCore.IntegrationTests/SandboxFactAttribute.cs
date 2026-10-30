using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore.IntegrationTests;

// A live-API test that runs only with a sandbox key: sandbox renders are watermarked and not
// counted, while any other environment (live, staging, …) counts toward the organization's PDF
// quota. xUnit 2 has no runtime skip (Assert.Skip is xUnit 3), so Skip is set at discovery and
// the runner reports the test as skipped instead of passing it without a request.
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute()
    {
        if (!IsSandboxKey(Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")))
            Skip = "POLI_PAGE_API_KEY is not a sandbox key (pp_sandbox_ + 32 lowercase hex).";
    }

    // The whole key, ^pp_sandbox_[0-9a-f]{32}\z: the sandbox prefix picks the environment, and the
    // library's grammar (linked in, not copied) checks the rest.
    internal static bool IsSandboxKey(string? key) =>
        key is not null
        && key.StartsWith("pp_sandbox_", StringComparison.Ordinal)
        && ApiKeyFormat.IsWellFormed(key);
}
