using Microsoft.AspNetCore.Mvc;
using PoliPage.AspNetCore.Internal;

namespace PoliPage.AspNetCore;

// MVC's file and content results, plus the headers the Minimal API results write: Cache-Control
// and X-Content-Type-Options from PoliPageAspNetCoreOptions, and an inline Content-Disposition
// (MVC's FileResult only writes `attachment`, from FileDownloadName). The headers are set before
// MVC's executor runs; it does not touch them.
internal static class PoliPageMvcHeaders
{
    public static void Apply(ActionContext context, PoliPageAspNetCoreOptions options, string? inlineDisposition)
    {
        var response = context.HttpContext.Response;
        if (inlineDisposition is not null)
            response.Headers.ContentDisposition = inlineDisposition;

        PoliPageResponseHeaders.Apply(response, options);
    }
}
