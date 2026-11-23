using WindowsGSM.Installer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class SteamCMDCredentialParsingTests
{
    [TestMethod]
    public void TryParseSteamCredentials_ValidQuotedValues_IgnoresCommentsAndWhitespace()
    {
        const string contents = "  // local note = not a credential\r\n steamuser = \"synthetic-user\"\nSteamPass=\"synthetic=pass\"\n";

        bool parsed = SteamCMD.TryParseSteamCredentials(contents, out string username, out string password, out string errorMessage);

        Assert.IsTrue(parsed);
        Assert.AreEqual("synthetic-user", username);
        Assert.AreEqual("synthetic=pass", password);
        Assert.AreEqual(string.Empty, errorMessage);
    }

    [TestMethod]
    public void TryParseSteamCredentials_EmptyOrMissingValues_ReturnsControlledError()
    {
        bool parsed = SteamCMD.TryParseSteamCredentials(
            "// comment only\nsteamUser=\"\"\nsteamPass=\"synthetic-password\"",
            out string username,
            out string password,
            out string errorMessage);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, username);
        Assert.AreEqual(string.Empty, password);
        Assert.IsFalse(string.IsNullOrWhiteSpace(errorMessage));
        Assert.IsFalse(errorMessage.Contains("synthetic-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryParseSteamCredentials_MalformedLines_ReturnsControlledError()
    {
        foreach (string contents in new[] { string.Empty, "/", "steamUser" })
        {
            bool parsed = SteamCMD.TryParseSteamCredentials(contents, out string username, out string password, out string errorMessage);

            Assert.IsFalse(parsed);
            Assert.AreEqual(string.Empty, username);
            Assert.AreEqual(string.Empty, password);
            Assert.IsFalse(string.IsNullOrWhiteSpace(errorMessage));
        }
    }

    [TestMethod]
    public void TryParseSteamCredentials_MalformedQuotes_DoesNotReturnPartialValues()
    {
        const string contents = "steamUser=synthetic-user\nsteamPass=\"synthetic-password\"trailing";

        bool parsed = SteamCMD.TryParseSteamCredentials(contents, out string username, out string password, out string errorMessage);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, username);
        Assert.AreEqual(string.Empty, password);
        Assert.IsFalse(string.IsNullOrWhiteSpace(errorMessage));
        Assert.IsFalse(errorMessage.Contains("synthetic-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryParseSteamCredentials_DuplicateKey_ReturnsControlledErrorWithoutValues()
    {
        const string contents = "steamUser=\"first-user\"\nSTEAMUSER=\"second-user\"\nsteamPass=\"synthetic-password\"";

        bool parsed = SteamCMD.TryParseSteamCredentials(contents, out string username, out string password, out string errorMessage);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, username);
        Assert.AreEqual(string.Empty, password);
        Assert.IsFalse(string.IsNullOrWhiteSpace(errorMessage));
        Assert.IsFalse(errorMessage.Contains("first-user", StringComparison.Ordinal));
        Assert.IsFalse(errorMessage.Contains("second-user", StringComparison.Ordinal));
        Assert.IsFalse(errorMessage.Contains("synthetic-password", StringComparison.Ordinal));
    }
}