using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoliPage.AspNetCore.IntegrationTests.Contract;

/// <summary>
/// The pinned API contract (<c>contract.json</c> of poli-page/api-contract), read from the path
/// in <c>POLI_CONTRACT</c>, and the repository's skip list (<c>tests/contract-skip.json</c>).
/// Only the <c>kind: "impl"</c> cases are for this library; the <c>sdk</c> cases are the SDK's.
/// </summary>
internal sealed class ContractFile
{
    /// <summary>The contract.json layout this harness reads.</summary>
    public const int FormatVersion = 1;

    public const string SkipFileName = "contract-skip.json";

    private static readonly Lazy<ContractFile> Shared = new(Load);

    private ContractFile(
        string? unavailableReason,
        IReadOnlyList<string> problems,
        IReadOnlyDictionary<string, JsonObject> implCases,
        IReadOnlyList<string> implIds,
        IReadOnlyDictionary<string, string> skipList,
        JsonObject? policy)
    {
        UnavailableReason = unavailableReason;
        Problems = problems;
        ImplCases = implCases;
        ImplIds = implIds;
        SkipList = skipList;
        Policy = policy;
    }

    public static ContractFile Current => Shared.Value;

    /// <summary>Set when the contract cannot be run: why.</summary>
    public string? UnavailableReason { get; }

    /// <summary>What is wrong with the contract file or the skip list.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Every <c>kind: "impl"</c> case, by id.</summary>
    public IReadOnlyDictionary<string, JsonObject> ImplCases { get; }

    /// <summary>The impl case ids, in contract order.</summary>
    public IReadOnlyList<string> ImplIds { get; }

    public IReadOnlyDictionary<string, string> SkipList { get; }

    public JsonObject? Policy { get; }

    /// <summary>
    /// Local runs without <c>POLI_CONTRACT</c> skip the suite with this reason; in CI (<c>CI</c>
    /// set) a missing contract fails it instead.
    /// </summary>
    public static string? LocalSkipReason
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLI_CONTRACT"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            ? "API contract tests skipped: set POLI_CONTRACT to a contract.json path"
            : null;

    private static ContractFile Load()
    {
        var path = Environment.GetEnvironmentVariable("POLI_CONTRACT");
        if (string.IsNullOrEmpty(path))
        {
            return Unavailable(
                "POLI_CONTRACT is not set: in CI the \"API contract\" step must provide contract.json "
                + "(poli-page/api-contract action, output \"path\")");
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("not a JSON object");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable($"cannot read the contract at {path}: {ex.Message}");
        }

        if (root["formatVersion"] is not JsonValue version || !version.TryGetValue<int>(out var format))
            return Unavailable($"{path} is not a contract.json: it has no formatVersion");
        if (format != FormatVersion)
        {
            return Unavailable(
                $"contract.json formatVersion {format} is not supported (this harness reads format {FormatVersion}): "
                + "update tests/PoliPage.AspNetCore.IntegrationTests/Contract");
        }

        Dictionary<string, string> skipList;
        try
        {
            skipList = LoadSkipList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            return Unavailable($"{SkipFileName}: {ex.Message}");
        }

        return FromContract(root, skipList);
    }

    private static ContractFile FromContract(JsonObject root, Dictionary<string, string> skipList)
    {
        var problems = new List<string>();
        var implCases = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var implIds = new List<string>();
        var cases = root["cases"] as JsonArray ?? [];
        for (var i = 0; i < cases.Count; i++)
        {
            var item = cases[i] as JsonObject;
            var id = item?["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var s) ? s : null;
            var kind = item?["kind"] is JsonValue kindValue && kindValue.TryGetValue<string>(out var k) ? k : null;
            switch (kind)
            {
                case "sdk":
                    // The SDK's own cases: its repository runs them.
                    break;
                case "impl" when id is not null:
                    if (implCases.TryAdd(id, item!))
                        implIds.Add(id);
                    else
                        problems.Add($"case id {id} appears twice");
                    break;
                default:
                    problems.Add($"cases[{i}] ({id}) has kind \"{kind}\": this harness knows only \"sdk\" and \"impl\"");
                    break;
            }
        }

        if (implIds.Count == 0)
            problems.Add("the contract has no \"impl\" case");

        foreach (var id in skipList.Keys.Where(id => !implCases.ContainsKey(id)))
            problems.Add($"{SkipFileName} lists \"{id}\", which is not an impl case of the contract: remove it");

        return new ContractFile(null, problems, implCases, implIds, skipList, root["policy"] as JsonObject);
    }

    private static Dictionary<string, string> LoadSkipList()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SkipFileName);
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("must be an object of case id to reason");
        var skip = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, reason) in root)
        {
            var text = reason is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"\"{id}\" needs a reason");
            skip[id] = text;
        }

        return skip;
    }

    private static ContractFile Unavailable(string reason)
        => new(
            reason,
            [reason],
            new Dictionary<string, JsonObject>(StringComparer.Ordinal),
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            null);
}
