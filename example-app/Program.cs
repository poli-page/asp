using PoliPage.AspNetCore;
using PoliPage.AspNetCore.ExampleApp.Endpoints;
using PoliPage.AspNetCore.ExampleApp.Scripts;

var builder = WebApplication.CreateBuilder(args);

// Workspace .env file (`/Users/mickael/Projects/.env`) is the single source of truth for
// POLI_PAGE_* secrets across all sister integrations. Real shell exports always win, so the
// loader is a no-op when the var is already set. See CLAUDE.md §10.5.
builder.Configuration.AddPoliPageWorkspaceEnvFile();

// Both the SDK options (ApiKey, BaseUrl, MaxRetries, RequestTimeout) and the integration
// options (ProblemDetailsTypeUri, etc., under "PoliPage:AspNetCore") bind from the same
// "PoliPage" section.
builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));

var app = builder.Build();

// Primary path: the IExceptionHandler registered by AddPoliPageAspNetCore handles
// PoliPageException → ProblemDetails. Hosts that prefer the fallback middleware would
// swap this for `app.UsePoliPageExceptionHandler()` (and disable RegisterExceptionHandler).
app.UseExceptionHandler();

app.MapPoliPageSmokeTest().AllowAnonymous();
app.MapRenderEndpoints();

app.MapGet("/", () => Results.Redirect("/poli-page/smoke"));

await app.RunAsync();
