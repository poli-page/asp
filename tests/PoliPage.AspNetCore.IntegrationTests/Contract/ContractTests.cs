using Xunit.Abstractions;

namespace PoliPage.AspNetCore.IntegrationTests.Contract;

// Conformance with the Poli Page API contract (https://github.com/poli-page/api-contract).
//
// CI copies the pinned contract.json with the poli-page/api-contract action and sets
// POLI_CONTRACT to its path. Every `kind: "impl"` case runs against the example app, loaded
// in-process by WebApplicationFactory, with its SDK pointed at a local HTTP server that answers
// the case's responses in order and records each request (ImplCaseRunner). The SDK keeps its
// default retry settings.
//
// tests/contract-skip.json maps the case ids allowed to fail to the reason. A listed case that
// passes, or a listed id the contract does not have as an impl case, fails the run.
//
// Locally: `POLI_CONTRACT=/path/to/contract.json dotnet test`. Without it the suite is skipped,
// except when CI is set, where a missing contract is an error.

/// <summary>Skips the contract suite locally when no contract is given.</summary>
public sealed class ContractFactAttribute : FactAttribute
{
    public ContractFactAttribute()
    {
        if (ContractFile.LocalSkipReason is { } reason)
            Skip = reason;
    }
}

/// <summary>Skips the contract suite locally when no contract is given.</summary>
public sealed class ContractTheoryAttribute : TheoryAttribute
{
    public ContractTheoryAttribute()
    {
        if (ContractFile.LocalSkipReason is { } reason)
            Skip = reason;
    }
}

public sealed class ContractTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The row of a contract that cannot be run, so the failure shows.</summary>
    private const string Unavailable = "(contract unavailable)";

    /// <summary>One row per impl case id, in contract order.</summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var contract = ContractFile.Current;
            var data = new TheoryData<string>();
            if (contract.UnavailableReason is not null || contract.ImplIds.Count == 0)
            {
                data.Add(Unavailable);
                return data;
            }

            foreach (var id in contract.ImplIds)
                data.Add(id);
            return data;
        }
    }

    [ContractFact]
    public void Contract_and_skip_list_are_valid()
    {
        var problems = ContractFile.Current.Problems;
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [ContractTheory]
    [MemberData(nameof(Cases))]
    public async Task Case(string id)
    {
        var contract = ContractFile.Current;
        if (contract.UnavailableReason is { } reason)
            Assert.Fail(reason);
        Assert.True(contract.ImplCases.TryGetValue(id, out var testCase), $"the contract has no impl case {id}");

        var problems = await ImplCaseRunner.RunAsync(factory, testCase);
        var title = testCase["title"]?.GetValue<string>() ?? id;
        if (!contract.SkipList.TryGetValue(id, out var skipReason))
        {
            Assert.True(problems.Count == 0, $"{title}\n{string.Join("\n", problems)}");
            return;
        }

        Assert.True(problems.Count > 0, $"{id} passes now: remove it from {ContractFile.SkipFileName}");
        output.WriteLine($"expected to fail ({skipReason}):\n{string.Join("\n", problems)}");
    }
}
