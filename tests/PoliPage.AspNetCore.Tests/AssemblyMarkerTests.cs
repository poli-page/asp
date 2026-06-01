namespace PoliPage.AspNetCore.Tests;

public class AssemblyMarkerTests
{
    [Fact]
    public void Assembly_loads_and_namespaces_resolve()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.Should().Be("PoliPage.AspNetCore");
    }
}
