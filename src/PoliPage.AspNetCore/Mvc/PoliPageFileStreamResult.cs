using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore;

// MVC's FileStreamResult plus the PoliPageAspNetCoreOptions headers; see PoliPageMvcHeaders.
internal sealed class PoliPageFileStreamResult(
    Stream fileStream,
    PoliPageAspNetCoreOptions options,
    string? inlineDisposition)
    : FileStreamResult(fileStream, "application/pdf")
{
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PoliPageMvcHeaders.Apply(context, options, inlineDisposition);
        return base.ExecuteResultAsync(context);
    }
}
