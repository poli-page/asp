using System.Text.Json;

namespace PoliPage.AspNetCore.Tests.ExceptionHandling;

// The error body every Poli Page integration answers: exactly the SDK's ToPayload() keys.
internal static class ErrorPayloadAssertions
{
    public static void ShouldBePayload(
        this JsonElement body,
        string code,
        string message,
        int? status,
        string? requestId)
    {
        body.ValueKind.Should().Be(JsonValueKind.Object);
        body.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["code", "message", "status", "requestId"], o => o.WithStrictOrdering());

        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("message").GetString().Should().Be(message);

        var statusElement = body.GetProperty("status");
        if (status is { } s)
            statusElement.GetInt32().Should().Be(s);
        else
            statusElement.ValueKind.Should().Be(JsonValueKind.Null);

        var requestIdElement = body.GetProperty("requestId");
        if (requestId is null)
            requestIdElement.ValueKind.Should().Be(JsonValueKind.Null);
        else
            requestIdElement.GetString().Should().Be(requestId);
    }
}
