using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class ConfiguredQueryPortTests
{
    [TestMethod]
    public void AreConfiguredUdpEndpointsConflicting_DetectsSameAddressAndPort()
    {
        Assert.IsTrue(ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting("192.168.30.2", 27015, "192.168.30.2", 27015));
    }

    [TestMethod]
    public void AreConfiguredUdpEndpointsConflicting_DetectsWildcardBind()
    {
        Assert.IsTrue(ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting("0.0.0.0", 27015, "192.168.30.2", 27015));
    }

    [TestMethod]
    public void AreConfiguredUdpEndpointsConflicting_IgnoresDifferentPortsAddressesAndInvalidEndpoints()
    {
        Assert.IsFalse(ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting("192.168.30.2", 27015, "192.168.30.2", 27016));
        Assert.IsFalse(ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting("192.168.30.2", 27015, "192.168.30.3", 27015));
        Assert.IsFalse(ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting("not-an-ip", 27015, "192.168.30.2", 27015));
    }
}
