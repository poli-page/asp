using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.IntegrationTests.Contract;

/// <summary>
/// Runs one <c>kind: "impl"</c> case against the example app: a <see cref="ContractMockServer"/>
/// answers the case's <c>responses</c>, the app's SDK points at it through the app's normal
/// configuration (<c>PoliPage:BaseUrl</c>, <c>PoliPage:ApiKey</c>), the case's <c>request</c>
/// goes through the test client, and the app's answer and the SDK's requests are compared with
/// <c>expect</c>. Returns what differs; empty when the case passes.
/// </summary>
internal static partial class ImplCaseRunner
{
    /// <summary>The case's own budget: the SDK's default retries take a few seconds at most.</summary>
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> CaseFields = new(StringComparer.Ordinal)
        { "id", "title", "kind", "request", "responses", "expect" };

    private static readonly HashSet<string> RequestFields = new(StringComparer.Ordinal) { "method", "path" };

    private static readonly HashSet<string> AnswerFields = new(StringComparer.Ordinal)
        { "status", "headers", "json", "text", "base64" };

    private static readonly HashSet<string> ExpectFields = new(StringComparer.Ordinal)
        { "status", "location", "contentType", "json", "requests" };

    private static readonly HashSet<string> ExpectedRequestFields = new(StringComparer.Ordinal)
        { "method", "path", "auth", "idempotencyKey" };

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseUuidV4();

    public static async Task<IReadOnlyList<string>> RunAsync(WebApplicationFactory<Program> factory, JsonObject testCase)
    {
        var problems = new List<string>();
        CheckFields(testCase, CaseFields, "case", problems);

        await using var server = new ContractMockServer();
        var resolved = Placeholders.Substitute(testCase, server.Origin) as JsonObject
            ?? throw new InvalidDataException("the case is not an object");

        var responses = resolved["responses"] as JsonArray ?? throw new InvalidDataException("the case has no responses");
        server.Steps = [.. responses.Select((r, i) => BuildStep(r as JsonObject, i, problems))];

        var request = resolved["request"] as JsonObject ?? throw new InvalidDataException("the case has no request");
        CheckFields(request, RequestFields, "request", problems);
        var method = request["method"]!.GetValue<string>();
        var path = request["path"]!.GetValue<string>();

        var expect = resolved["expect"] as JsonObject ?? throw new InvalidDataException("the case has no expect");
        CheckFields(expect, ExpectFields, "expect", problems);

        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The app's normal configuration: the SDK goes to the mock with a sandbox key.
                ["PoliPage:ApiKey"] = TestKeys.Sandbox,
                ["PoliPage:BaseUrl"] = server.Origin,
            })));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.Timeout = Timeout.InfiniteTimeSpan;

        using var timeout = new CancellationTokenSource(CaseTimeout);
        using var message = new HttpRequestMessage(new HttpMethod(method), path);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return [$"{method} {path} did not answer within {CaseTimeout}"];
        }

        using (response)
        {
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            CompareAnswer(expect, response, body, problems);
        }

        problems.AddRange(server.Problems);
        if (expect["requests"] is JsonArray wantRequests)
            CompareRequests(wantRequests, server.Requests, problems);

        return problems;
    }

    private static MockStep BuildStep(JsonObject? step, int index, List<string> problems)
    {
        if (step is null)
            throw new InvalidDataException($"responses[{index}] is not an object");

        if (step["fault"] is { } fault)
        {
            if (step.Count != 1)
                problems.Add($"responses[{index}]: a fault has no status, headers or body");
            var name = fault.GetValue<string>();
            if (name is not ("drop" or "hang"))
                throw new InvalidDataException($"responses[{index}]: unknown fault \"{name}\"");
            return new MockStep(0, [], [], name);
        }

        CheckFields(step, AnswerFields, $"responses[{index}]", problems);
        var status = step["status"]?.GetValue<int>() ?? throw new InvalidDataException($"responses[{index}]: no status");
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in step["headers"] as JsonObject ?? [])
            headers.Add(new(name, value!.GetValue<string>()));

        byte[] body = [];
        if (step.ContainsKey("json"))
        {
            body = Encoding.UTF8.GetBytes(step["json"]?.ToJsonString() ?? "null");
            if (!headers.Exists(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)))
                headers.Add(new("Content-Type", "application/json"));
        }
        else if (step["text"] is { } text)
        {
            body = Encoding.UTF8.GetBytes(text.GetValue<string>());
        }
        else if (step["base64"] is { } base64)
        {
            body = Convert.FromBase64String(base64.GetValue<string>());
        }

        return new MockStep(status, headers, body, null);
    }

    private static void CompareAnswer(JsonObject expect, HttpResponseMessage response, byte[] body, List<string> problems)
    {
        if (expect["status"] is { } status && (int)response.StatusCode != status.GetValue<int>())
        {
            problems.Add(
                $"status: expected {status.GetValue<int>()}, got {(int)response.StatusCode} ({Preview(body)})");
        }

        if (expect["location"] is { } location)
        {
            var got = response.Headers.Location?.OriginalString;
            if (!string.Equals(got, location.GetValue<string>(), StringComparison.Ordinal))
                problems.Add($"location: expected {location.GetValue<string>()}, got {got ?? "(none)"}");
        }

        if (expect["contentType"] is { } contentType)
        {
            // The media type only: parameters such as charset are ignored.
            var got = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(got, contentType.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                problems.Add($"contentType: expected {contentType.GetValue<string>()}, got {got ?? "(none)"}");
        }

        if (expect.ContainsKey("json"))
        {
            JsonNode? got;
            try
            {
                got = JsonNode.Parse(body);
            }
            catch (JsonException ex)
            {
                problems.Add($"json: the body is not JSON ({ex.Message}): {Preview(body)}");
                return;
            }

            var differences = new List<string>();
            JsonDiff("json", expect["json"], got, differences);
            if (differences.Count > 0)
            {
                problems.Add(
                    $"json: expected {expect["json"]?.ToJsonString() ?? "null"}, got {got?.ToJsonString() ?? "null"}");
                problems.AddRange(differences.Select(d => "  " + d));
            }
        }
    }

    private static void CompareRequests(JsonArray want, IReadOnlyList<RecordedRequest> got, List<string> problems)
    {
        if (want.Count != got.Count)
        {
            problems.Add(
                $"requests: expected {want.Count}, got {got.Count}: "
                + string.Join(", ", got.Select(r => $"{r.Method} {r.Target}")));
        }

        // One call sends the same Idempotency-Key on every attempt: requests to the same route
        // are attempts of one call in an impl case.
        var keysByCall = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(want.Count, got.Count); i++)
        {
            var expected = want[i] as JsonObject ?? throw new InvalidDataException($"expect.requests[{i}] is not an object");
            CheckFields(expected, ExpectedRequestFields, $"expect.requests[{i}]", problems);
            var actual = got[i];
            var label = $"requests[{i}]";

            var method = expected["method"]?.GetValue<string>();
            if (method is not null && !string.Equals(method, actual.Method, StringComparison.Ordinal))
                problems.Add($"{label}.method: expected {method}, got {actual.Method}");

            var path = expected["path"]?.GetValue<string>();
            if (path is not null && !string.Equals(path, actual.Target, StringComparison.Ordinal))
                problems.Add($"{label}.path: expected {path}, got {actual.Target}");

            var authorization = actual.Header("Authorization");
            switch (expected["auth"]?.GetValue<string>())
            {
                case null:
                    break;
                case "bearer":
                    if (authorization is not [var value] || value != $"Bearer {TestKeys.Sandbox}")
                        problems.Add($"{label}.auth: expected Authorization: Bearer <the test key>, got {Describe(authorization)}");
                    break;
                case "none":
                    if (authorization is not null)
                        problems.Add($"{label}.auth: expected no Authorization header, got one");
                    break;
                case var other:
                    problems.Add($"{label}.auth: unknown value \"{other}\"");
                    break;
            }

            var idempotencyKey = actual.Header("Idempotency-Key");
            switch (expected["idempotencyKey"]?.GetValue<string>())
            {
                case null:
                    break;
                case "stable":
                    if (idempotencyKey is not [var key] || !LowercaseUuidV4().IsMatch(key))
                    {
                        problems.Add($"{label}.idempotencyKey: expected one lowercase UUID v4, got {Describe(idempotencyKey)}");
                        break;
                    }

                    var call = $"{actual.Method} {actual.Target}";
                    if (keysByCall.TryGetValue(call, out var first) && first != key)
                        problems.Add($"{label}.idempotencyKey: {key} differs from the call's first attempt ({first})");
                    keysByCall.TryAdd(call, key);
                    break;
                case "absent":
                    if (idempotencyKey is not null)
                        problems.Add($"{label}.idempotencyKey: expected none, got {Describe(idempotencyKey)}");
                    break;
                case var other:
                    problems.Add($"{label}.idempotencyKey: unknown value \"{other}\"");
                    break;
            }
        }
    }

    // Deep equality with exact key sets; numbers compare by value.
    private static void JsonDiff(string path, JsonNode? want, JsonNode? got, List<string> differences)
    {
        switch (want)
        {
            case null:
                if (got is not null)
                    differences.Add($"{path}: expected null, got {got.ToJsonString()}");
                return;
            case JsonObject wantObject:
                if (got is not JsonObject gotObject)
                {
                    differences.Add($"{path}: expected an object, got {got?.ToJsonString() ?? "null"}");
                    return;
                }

                foreach (var key in wantObject.Select(p => p.Key).Where(k => !gotObject.ContainsKey(k)))
                    differences.Add($"{path}.{key}: missing");
                foreach (var key in gotObject.Select(p => p.Key).Where(k => !wantObject.ContainsKey(k)))
                    differences.Add($"{path}.{key}: not expected");
                foreach (var (key, value) in wantObject)
                {
                    if (gotObject.TryGetPropertyValue(key, out var gotValue))
                        JsonDiff($"{path}.{key}", value, gotValue, differences);
                }

                return;
            case JsonArray wantArray:
                if (got is not JsonArray gotArray || gotArray.Count != wantArray.Count)
                {
                    differences.Add($"{path}: expected {wantArray.ToJsonString()}, got {got?.ToJsonString() ?? "null"}");
                    return;
                }

                for (var i = 0; i < wantArray.Count; i++)
                    JsonDiff($"{path}[{i}]", wantArray[i], gotArray[i], differences);
                return;
            default:
                if (!ScalarEquals(want.AsValue(), got))
                    differences.Add($"{path}: expected {want.ToJsonString()}, got {got?.ToJsonString() ?? "null"}");
                return;
        }
    }

    private static bool ScalarEquals(JsonValue want, JsonNode? got)
    {
        if (got is not JsonValue gotValue)
            return false;
        var kind = want.GetValueKind();
        if (kind != gotValue.GetValueKind())
            return false;
        return kind switch
        {
            JsonValueKind.Number => ParseNumber(want) == ParseNumber(gotValue),
            JsonValueKind.String => string.Equals(want.GetValue<string>(), gotValue.GetValue<string>(), StringComparison.Ordinal),
            _ => true, // true, false: the kind is the value
        };
    }

    private static decimal ParseNumber(JsonValue value)
        => decimal.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static void CheckFields(JsonObject node, HashSet<string> known, string where, List<string> problems)
    {
        foreach (var key in node.Select(p => p.Key).Where(k => !known.Contains(k)))
            problems.Add($"{where}.{key}: this harness does not handle this field yet: update it");
    }

    private static string Describe(IReadOnlyList<string>? values)
        => values is null ? "(none)" : string.Join(", ", values);

    private static string Preview(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        return text.Length <= 300 ? text : string.Concat(text.AsSpan(0, 300), "…");
    }
}
