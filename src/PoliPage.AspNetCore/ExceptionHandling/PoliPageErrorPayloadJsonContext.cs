using System.Text.Json.Serialization;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Source-generated serializer for the SDK's PoliPageErrorPayload, so the error body needs no
// reflection (AOT and trimming safe). The keys are the SDK's own (its [JsonPropertyName]s: code,
// message, status, requestId), so no naming policy is set here. Null values are written, not
// dropped: an unknown status or requestId is null.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PoliPageErrorPayload))]
internal sealed partial class PoliPageErrorPayloadJsonContext : JsonSerializerContext
{
}
