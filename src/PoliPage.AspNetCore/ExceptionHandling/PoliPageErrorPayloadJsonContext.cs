using System.Text.Json.Serialization;

namespace PoliPage.AspNetCore.ExceptionHandling;

// Source-generated serializer for the SDK's PoliPageErrorPayload, so the error body needs no
// reflection (AOT and trimming safe). camelCase gives exactly the contract's keys: code, message,
// status, requestId. Null values are written, not dropped: an unknown status or requestId is null.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PoliPageErrorPayload))]
internal sealed partial class PoliPageErrorPayloadJsonContext : JsonSerializerContext
{
}
