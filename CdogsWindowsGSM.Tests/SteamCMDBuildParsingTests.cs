using WindowsGSM.Installer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class SteamCMDBuildParsingTests
{
    [TestMethod]
    public void TryParseRemoteBuildId_NoMatches_ReturnsFalse()
    {
        bool parsed = SteamCMD.TryParseRemoteBuildId("no public build metadata", out string buildId);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, buildId);
    }

    [TestMethod]
    public void TryParseRemoteBuildId_OneMatch_ReturnsItsBuildId()
    {
        bool parsed = SteamCMD.TryParseRemoteBuildId(BuildPublicSection("12345"), out string buildId);

        Assert.IsTrue(parsed);
        Assert.AreEqual("12345", buildId);
    }

    [TestMethod]
    public void TryParseRemoteBuildId_MultipleMatches_ReturnsFirstBuildId()
    {
        string output = BuildPublicSection("12345") + "\r\n" + BuildPublicSection("67890");

        bool parsed = SteamCMD.TryParseRemoteBuildId(output, out string buildId);

        Assert.IsTrue(parsed);
        Assert.AreEqual("12345", buildId);
    }

    private static string BuildPublicSection(string buildId)
    {
        return $"\"public\"\r\n{{\r\n\t\"buildid\"\t\"{buildId}\"\r\n}}";
    }
}