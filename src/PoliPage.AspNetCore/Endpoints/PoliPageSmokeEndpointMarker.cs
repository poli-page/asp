namespace PoliPage.AspNetCore;

// Marker attached to the smoke endpoint's Metadata so the ApplicationStarted callback
// can locate it across the IEndpointRouteBuilder.DataSources collection.
internal sealed class PoliPageSmokeEndpointMarker
{
}
