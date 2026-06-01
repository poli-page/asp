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

// Adding MVC services so the InvoicesController demo (PoliPageResponseFactory) is reachable.
builder.Services.AddControllers();

var app = builder.Build();

// Primary path: the IExceptionHandler registered by AddPoliPageAspNetCore handles
// PoliPageException → ProblemDetails. Hosts that prefer the fallback middleware would
// swap this for `app.UsePoliPageExceptionHandler()` (and disable RegisterExceptionHandler).
app.UseExceptionHandler();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPoliPageSmokeTest().AllowAnonymous();
app.MapRenderEndpoints();
app.MapDocumentEndpoints();
app.MapErrorEndpoints();
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/demo.html"));

await app.RunAsync();

// Marker class so tests can reference the example-app's host as
// WebApplicationFactory<Program>. The C# compiler synthesizes an "internal partial class
// Program" for the file's top-level statements; adding "public partial" here promotes
// the visibility without altering the entry-point semantics. CLAUDE.md §10.4.
public partial class Program { }
