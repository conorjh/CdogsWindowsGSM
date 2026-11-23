using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class GameServerConfigDownloadTests
{
    private string _testRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.ConfigDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_ExistingFileWithoutExplicitReplace_PreservesUserConfig()
    {
        string destinationPath = Path.Combine(_testRoot, "server.cfg");
        File.WriteAllText(destinationPath, "user-owned configuration");
        var missingSource = new Uri(Path.Combine(_testRoot, "missing-template.cfg"));

        GameServerConfigDownloadResult result = await Github.DownloadGameServerConfigAsync(
            missingSource,
            destinationPath,
            replaceValues: null,
            replaceExisting: false);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.ExistingFilePreserved);
        Assert.AreEqual("user-owned configuration", File.ReadAllText(destinationPath));
        Assert.AreEqual(1, Directory.GetFiles(_testRoot).Length);
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_FailedExplicitReplace_PreservesExistingFileAndRemovesStage()
    {
        string destinationPath = Path.Combine(_testRoot, "server.cfg");
        File.WriteAllText(destinationPath, "user-owned configuration");
        var missingSource = new Uri(Path.Combine(_testRoot, "missing-template.cfg"));

        GameServerConfigDownloadResult result = await Github.DownloadGameServerConfigAsync(
            missingSource,
            destinationPath,
            replaceValues: null,
            replaceExisting: true);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.AreEqual("user-owned configuration", File.ReadAllText(destinationPath));
        Assert.AreEqual(1, Directory.GetFiles(_testRoot).Length);
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_DestinationInUse_PreservesExistingFile()
    {
        string sourcePath = Path.Combine(_testRoot, "template.cfg");
        string destinationPath = Path.Combine(_testRoot, "server.cfg");
        File.WriteAllText(sourcePath, "new configuration");
        File.WriteAllText(destinationPath, "user-owned configuration");
        GameServerConfigDownloadResult result;

        using (var destinationLock = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await Github.DownloadGameServerConfigAsync(
                new Uri(sourcePath),
                destinationPath,
                replaceValues: null,
                replaceExisting: true);
        }

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.AreEqual("user-owned configuration", File.ReadAllText(destinationPath));
        Assert.AreEqual(2, Directory.GetFiles(_testRoot).Length);
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_EmptyTemplate_DoesNotPublishOrReplaceFile()
    {
        string sourcePath = Path.Combine(_testRoot, "template.cfg");
        string destinationPath = Path.Combine(_testRoot, "server.cfg");
        File.WriteAllText(sourcePath, string.Empty);
        File.WriteAllText(destinationPath, "user-owned configuration");

        GameServerConfigDownloadResult result = await Github.DownloadGameServerConfigAsync(
            new Uri(sourcePath),
            destinationPath,
            replaceValues: null,
            replaceExisting: true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("user-owned configuration", File.ReadAllText(destinationPath));
        Assert.AreEqual(2, Directory.GetFiles(_testRoot).Length);
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_HtmlErrorPage_DoesNotPublishTemplate()
    {
        string sourcePath = Path.Combine(_testRoot, "template.cfg");
        string destinationPath = Path.Combine(_testRoot, "server.cfg");
        File.WriteAllText(sourcePath, "<!doctype html><html><body>Not found</body></html>");

        GameServerConfigDownloadResult result = await Github.DownloadGameServerConfigAsync(
            new Uri(sourcePath),
            destinationPath,
            replaceValues: null,
            replaceExisting: false);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("The downloaded config template is an HTML error page.", result.ErrorMessage);
        Assert.IsFalse(File.Exists(destinationPath));
        Assert.AreEqual(1, Directory.GetFiles(_testRoot).Length);
    }

    [TestMethod]
    public async Task DownloadGameServerConfigAsync_FirstDownloadAndExplicitReplace_ApplyTemplateValues()
    {
        string sourcePath = Path.Combine(_testRoot, "template.cfg");
        string destinationPath = Path.Combine(_testRoot, "server", "server.cfg");
        File.WriteAllText(sourcePath, "hostname={{hostname}}");
        var replacements = new List<(string, string)> { ("{{hostname}}", "Alpha Server") };

        GameServerConfigDownloadResult firstResult = await Github.DownloadGameServerConfigAsync(
            new Uri(sourcePath),
            destinationPath,
            replacements,
            replaceExisting: false);

        Assert.IsTrue(firstResult.Succeeded);
        Assert.IsFalse(firstResult.ExistingFilePreserved);
        Assert.AreEqual("hostname=Alpha Server", File.ReadAllText(destinationPath));

        File.WriteAllText(destinationPath, "previous configuration");
        GameServerConfigDownloadResult replaceResult = await Github.DownloadGameServerConfigAsync(
            new Uri(sourcePath),
            destinationPath,
            replacements,
            replaceExisting: true);

        Assert.IsTrue(replaceResult.Succeeded);
        Assert.IsFalse(replaceResult.ExistingFilePreserved);
        Assert.AreEqual("hostname=Alpha Server", File.ReadAllText(destinationPath));
    }

    [TestMethod]
    public async Task SourceInstallConfigOnly_ExistingConfig_PreservesOriginalBytes()
    {
        using IDisposable pathScope = ServerPath.UseRootForTesting(_testRoot);
        const string serverId = "source-config-preservation";
        var config = new ServerConfig(serverId)
        {
            ServerGame = GameServer.DODS.FullName,
            ServerName = "Updated Name",
            ServerPort = "27015"
        };
        GameServer.Engine.Source source = GameServer.Data.Class.Get(config.ServerGame, config) as GameServer.Engine.Source;
        Assert.IsNotNull(source);
        string configPath = ServerPath.GetServersServerFiles(serverId, source.Game, "cfg", "server.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        byte[] originalBytes = { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'o', (byte)'s', (byte)'t', (byte)'\n' };
        File.WriteAllBytes(configPath, originalBytes);

        bool succeeded = await source.InstallConfigOnly();

        Assert.IsTrue(succeeded, source.Error);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(configPath));
    }

    [TestMethod]
    public async Task GoldSourceInstallConfigOnly_ExistingConfig_PreservesOriginalBytes()
    {
        using IDisposable pathScope = ServerPath.UseRootForTesting(_testRoot);
        const string serverId = "goldsource-config-preservation";
        var config = new ServerConfig(serverId)
        {
            ServerGame = GameServer.CS.FullName,
            ServerName = "Updated Name"
        };
        GameServer.Engine.GoldSource goldSource = GameServer.Data.Class.Get(config.ServerGame, config) as GameServer.Engine.GoldSource;
        Assert.IsNotNull(goldSource);
        string configPath = ServerPath.GetServersServerFiles(serverId, goldSource.Game, "server.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        byte[] originalBytes = { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'o', (byte)'s', (byte)'t', (byte)'\n' };
        File.WriteAllBytes(configPath, originalBytes);

        bool succeeded = await goldSource.InstallConfigOnly();

        Assert.IsTrue(succeeded, goldSource.Error);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(configPath));
    }
}