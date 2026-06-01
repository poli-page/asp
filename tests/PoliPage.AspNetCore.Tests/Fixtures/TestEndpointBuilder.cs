using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PoliPage.AspNetCore.Tests.Fixtures;

// Minimal EndpointBuilder subclass for unit-testing IEndpointMetadataProvider.PopulateMetadata
// implementations. The real EndpointBuilder is abstract; the public API only needs Metadata to
// be writable and Build() to exist. Build() is never called by PopulateMetadata, so it throws.
internal sealed class TestEndpointBuilder : EndpointBuilder
{
    public override Endpoint Build()
        => throw new NotSupportedException("TestEndpointBuilder.Build is not implemented; use Metadata only.");
}
