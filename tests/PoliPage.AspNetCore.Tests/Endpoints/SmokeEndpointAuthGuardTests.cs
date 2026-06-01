using Microsoft.Extensions.Logging;
using PoliPage.AspNetCore.Tests.Fixtures;

namespace PoliPage.AspNetCore.Tests.Endpoints;

public class SmokeEndpointAuthGuardTests
{
    private const int SmokeEndpointUnguardedEventId = 2001;

    [Fact]
    public async Task Warns_when_smoke_endpoint_registered_without_auth_metadata()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest());

        host.Logs.Entries.Should().Contain(e =>
            e.EventId.Id == SmokeEndpointUnguardedEventId && e.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task Does_not_warn_when_AllowAnonymous_is_chained()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
            app.MapPoliPageSmokeTest().AllowAnonymous());

        host.Logs.Entries.Should().NotContain(e => e.EventId.Id == SmokeEndpointUnguardedEventId);
    }

    [Fact]
    public async Task Does_not_warn_when_RequireAuthorization_is_chained()
    {
        await using var host = await PoliPageTestHost.StartAsync(app =>
        {
            app.MapPoliPageSmokeTest().RequireAuthorization();
        }, configureAspNet: null);

        host.Logs.Entries.Should().NotContain(e => e.EventId.Id == SmokeEndpointUnguardedEventId);
    }
}
