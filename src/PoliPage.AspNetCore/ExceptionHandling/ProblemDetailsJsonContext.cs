using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore.ExceptionHandling;

// System.Text.Json source-gen context for ProblemDetails. Drives the middleware's fallback
// WriteAsJsonAsync path so AOT publishes don't pull in the reflection-based serializer.
// ProblemDetails.Extensions is IDictionary<string, object?> — values that aren't primitives
// fall back to the dynamic path at runtime; in practice we only put strings + ints in there.
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext
{
}
