using System.IO;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class GlobalServerListTests
{
    [TestMethod]
    public void ContainsServer_MatchesConfiguredAddressAndPortExactly()
    {
        const string response = "{\"response\":{\"servers\":[{\"addr\":\"203.0.113.42:27015\"},{\"addr\":\"203.0.113.42:27016\"}]}}";

        Assert.IsTrue(GlobalServerList.ContainsServer(response, "203.0.113.42", 27015));
        Assert.IsFalse(GlobalServerList.ContainsServer(response, "203.0.113.42", 27017));
        Assert.IsFalse(GlobalServerList.ContainsServer(response, "203.0.113.43", 27015));
    }

    [TestMethod]
    public void ContainsServer_RejectsMalformedServerListResponse()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => GlobalServerList.ContainsServer("{}", "203.0.113.42", 27015));
    }

}
