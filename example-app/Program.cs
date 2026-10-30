using PoliPage.AspNetCore;
using PoliPage.AspNetCore.ExampleApp.Endpoints;
using PoliPage.AspNetCore.ExampleApp.Scripts;

var builder = WebApplication.CreateBuilder(args);

// POLI_PAGE_API_KEY and POLI_PAGE_BASE_URL come from the environment, or from the nearest .env
// file walking up from the working directory. Real environment variables always win.
builder.Configuration.AddPoliPageWorkspaceEnvFile();

// Both the SDK options (ApiKey, BaseUrl, MaxRetries, RequestTimeout) and the integration
// options (under "PoliPage:AspNetCore") bind from the same "PoliPage" section.
builder.Services.AddPoliPageAspNetCore(builder.Configuration.GetSection("PoliPage"));

// Adding MVC services so the InvoicesController demo (PoliPageResponseFactory) is reachable.
builder.Services.AddControllers();

var app = builder.Build();

// AddPoliPageAspNetCore turns on the Poli Page error handler: a PoliPageException is answered
// with its ToPayload() body ({ code, message, status, requestId }) whether or not the app calls
// UseExceptionHandler(). This call handles every other unhandled exception as a ProblemDetails.
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
// the visibility without altering the entry-point semantics.
public partial class Program { }
