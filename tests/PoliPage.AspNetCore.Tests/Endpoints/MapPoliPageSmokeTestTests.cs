using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Endpoints;

public class MapPoliPageSmokeTestTests
{
    [Fact]
    public async Task Smoke_endpoint_returns_pdf_inline()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest().AllowAnonymous());
        host.Stub.PdfBytes = "%PDF-1.7\n%smoke"u8.ToArray();

        var response = await host.Client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(host.Stub.PdfBytes);
    }

    [Fact]
    public async Task Smoke_endpoint_maps_PoliPageException_via_exception_handler()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest().AllowAnonymous());
        host.Stub.SetException(
            new PoliPageAuthException(PoliPageErrorCode.InvalidApiKey, 401, "Bad key", requestId: "req_x"));

        var response = await host.Client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("authentication_failed");
        doc.RootElement.GetProperty("poliPageRequestId").GetString().Should().Be("req_x");
    }

    [Fact]
    public async Task Smoke_endpoint_maps_PoliPageException_via_fallback_middleware()
    {
        await using var host = await PoliPageTestHost.StartAsync(
            configureApp: app => app.MapPoliPageSmokeTest().AllowAnonymous(),
            configureAspNet: opts =>
            {
                opts.RegisterExceptionHandler = false;
                opts.AddProblemDetailsService = true;
            },
            useFallbackMiddleware: true);
        host.Stub.SetException(
            new PoliPageValidationException(PoliPageErrorCode.ValidationError, 422, "Bad input", requestId: "req_y"));

        var response = await host.Client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Smoke_endpoint_default_pattern_is_poli_page_smoke()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest().AllowAnonymous());

        var notFound = await host.Client.GetAsync("/something-else");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var smoke = await host.Client.GetAsync("/poli-page/smoke");
        smoke.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Smoke_endpoint_honours_custom_pattern()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest("/internal/poli-page/probe").AllowAnonymous());

        var response = await host.Client.GetAsync("/internal/poli-page/probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
    }
}
