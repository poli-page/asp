using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// Captures the ProblemDetails passed to TryWriteAsync so tests can assert on the object the
// handler / middleware constructed without going through System.Text.Json serialization. The
// fallback path (the middleware's WriteAsJsonAsync branch) needs the "returns false" mode —
// set ReturnValue = false to exercise it.
internal sealed class RecordingProblemDetailsService : IProblemDetailsService
{
    public ProblemDetails? Captured { get; private set; }
    public HttpContext? CapturedContext { get; private set; }
    public bool ReturnValue { get; set; } = true;

    public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Captured = context.ProblemDetails;
        CapturedContext = context.HttpContext;
        return ValueTask.FromResult(ReturnValue);
    }

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Captured = context.ProblemDetails;
        CapturedContext = context.HttpContext;
        return ValueTask.CompletedTask;
    }
}
