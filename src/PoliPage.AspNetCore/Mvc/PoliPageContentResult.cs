using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore;

// MVC's ContentResult plus the PoliPageAspNetCoreOptions headers; see PoliPageMvcHeaders.
internal sealed class PoliPageContentResult(PoliPageAspNetCoreOptions options) : ContentResult
{
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PoliPageMvcHeaders.Apply(context, options, inlineDisposition: null);
        return base.ExecuteResultAsync(context);
    }
}
