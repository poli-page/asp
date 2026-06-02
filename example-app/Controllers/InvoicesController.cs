using Microsoft.AspNetCore.Mvc;

namespace PoliPage.AspNetCore.ExampleApp.Controllers;

[ApiController]
[Route("invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly PoliPageClient _client;
    private readonly PoliPageResponseFactory _factory;

    public InvoicesController(PoliPageClient client, PoliPageResponseFactory factory)
    {
        _client = client;
        _factory = factory;
    }

    // GET /invoices/{id}.pdf
    // MVC twin of the Minimal-API /render/pdf endpoint — demonstrates that the same SDK
    // call paired with PoliPageResponseFactory yields a FileContentResult on the MVC path.
    [HttpGet("{id}.pdf")]
    public async Task<IActionResult> DownloadPdfAsync(string id, CancellationToken cancellationToken)
    {
        var pdf = await _client.Render.PdfAsync(
            new ProjectModeInput
            {
                Project = "getting-started",
                Template = "welcome",
                Version = "1.0.0",
                Data = new { name = $"invoice {id}" },
            },
            cancellationToken: cancellationToken);

        return _factory.Pdf(pdf, filename: $"invoice-{id}.pdf");
    }

    // GET /invoices/{id}/preview
    [HttpGet("{id}/preview")]
    public async Task<IActionResult> PreviewAsync(string id, CancellationToken cancellationToken)
    {
        var preview = await _client.Render.PreviewAsync(
            new ProjectModeInput
            {
                Project = "getting-started",
                Template = "welcome",
                Version = "1.0.0",
                Data = new { name = $"invoice {id}" },
            },
            cancellationToken: cancellationToken);

        return _factory.Preview(preview.Html);
    }
}
