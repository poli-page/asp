using Microsoft.AspNetCore.Http.Features;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// IHttpResponseFeature replacement whose HasStarted is overridable. DefaultHttpContext's
// stock feature is hard-wired to HasStarted == false, so middleware tests that need the
// "response already on the wire" branch swap this in via httpContext.Features.Set(...).
internal sealed class ResponseFeatureWithHasStarted(bool hasStarted) : IHttpResponseFeature
{
    public int StatusCode { get; set; } = StatusCodes.Status200OK;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted { get; } = hasStarted;

    public void OnStarting(Func<object, Task> callback, object state) { }
    public void OnCompleted(Func<object, Task> callback, object state) { }
}
