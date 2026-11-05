using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore;

// MVC's FileContentResult plus the PoliPageAspNetCoreOptions headers; see PoliPageMvcHeaders.
internal sealed class PoliPageFileContentResult(
    byte[] fileContents,
    PoliPageAspNetCoreOptions options,
    string? inlineDisposition)
    : FileContentResult(fileContents, "application/pdf")
{
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PoliPageMvcHeaders.Apply(context, options, inlineDisposition);
        return base.ExecuteResultAsync(context);
    }
}
