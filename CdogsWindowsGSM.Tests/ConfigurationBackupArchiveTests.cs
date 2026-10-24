using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using WindowsGSM.Functions;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class ConfigurationBackupArchiveTests
{
    private string _testRoot = string.Empty;
    private IDisposable? _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _pathScope = ServerPath.UseRootForTesting(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _pathScope?.Dispose();
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [TestMethod]
    public void CreateAndRestore_ChangesOnlyTheApprovedConfigurationFile()
    {
        string instanceRoot = CreateInstanceRoot("source");
        string configPath = Path.Combine(instanceRoot, "server.cfg");
        string worldPath = Path.Combine(instanceRoot, "world.db");
        File.WriteAllText(configPath, "server.hostname=original");
        File.WriteAllText(worldPath, "world state");
        string archivePath = Path.Combine(_testRoot, "rust.wgsmbackup");

        ConfigurationBackupResult created = ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            AllowedPaths());

        CollectionAssert.AreEqual(
            new[] { "serverfiles/server.cfg" },
            created.IncludedPaths.Select(path => path.RootId + "/" + path.RelativePath).ToArray());

        File.WriteAllText(configPath, "server.hostname=changed");
        File.WriteAllText(worldPath, "updated world state");

        List<string> restoredPaths = ConfigurationBackupArchive.Restore(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            AllowedPaths());

        Assert.AreEqual("server.hostname=original", File.ReadAllText(configPath));
        Assert.AreEqual("updated world state", File.ReadAllText(worldPath));
        CollectionAssert.AreEqual(new[] { "serverfiles/server.cfg" }, restoredPaths);
    }

    [TestMethod]
    public void SharedGoldSourceRestoreChangesOnlyInstanceConfigAndUniqueMapCycle()
    {
        string serverId = "goldsrc-server";
        string installationId = CreateSharedInstallation("goldsrc-owner", GameServer.CS.FullName);
        ServerConfig serverConfig = new ServerConfig(serverId)
        {
            ServerGame = GameServer.CS.FullName,
            SharedInstallationId = installationId
        };
        GameServer.Engine.GoldSource goldSource = GameServer.Data.Class.Get(serverConfig.ServerGame, serverConfig) as GameServer.Engine.GoldSource;
        string instanceRoot = Path.Combine(_testRoot, "servers", serverId, "serverfiles");
        string sharedServerFilesRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
        string configPath = Path.Combine(instanceRoot, goldSource.Game, "server.cfg");
        string mapCycleRelativePath = Path.Combine(
            goldSource.Game,
            "windowsgsm_goldsrc_mapcycle_goldsrc-server.txt");
        string mapCyclePath = Path.Combine(sharedServerFilesRoot, mapCycleRelativePath);
        string executablePath = Path.Combine(sharedServerFilesRoot, "hlds.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        Directory.CreateDirectory(Path.GetDirectoryName(mapCyclePath));
        File.WriteAllText(configPath, "hostname=original");
        File.WriteAllText(mapCyclePath, "de_dust2");
        File.WriteAllText(executablePath, "binary-original");
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", instanceRoot },
            { "sharedserverfiles", sharedServerFilesRoot }
        };
        List<ConfigurationBackupPath> paths = ConfigurationBackupArchive.ValidatePlan(
            roots,
            BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots));
        string archivePath = Path.Combine(_testRoot, "goldsrc.wgsmbackup");

        ConfigurationBackupResult created = ConfigurationBackupArchive.Create(
            archivePath,
            serverId,
            serverConfig.ServerGame,
            installationId,
            "build-1",
            roots,
            paths);

        CollectionAssert.AreEqual(
            new[]
            {
                "serverfiles/" + goldSource.Game + "/server.cfg",
                "sharedserverfiles/" + goldSource.Game + "/windowsgsm_goldsrc_mapcycle_goldsrc-server.txt"
            },
            created.IncludedPaths.Select(path => path.RootId + "/" + path.RelativePath).ToArray());

        File.WriteAllText(configPath, "hostname=current");
        File.WriteAllText(mapCyclePath, "de_inferno");
        File.WriteAllText(executablePath, "binary-updated");

        ConfigurationBackupArchive.Restore(
            archivePath,
            serverId,
            serverConfig.ServerGame,
            installationId,
            "build-1",
            roots,
            paths);

        Assert.AreEqual("hostname=original", File.ReadAllText(configPath));
        Assert.AreEqual("de_dust2", File.ReadAllText(mapCyclePath));
        Assert.AreEqual("binary-updated", File.ReadAllText(executablePath));
    }

    [TestMethod]
    public void HasExistingFiles_DetectsNestedSharedGoldSourceConfigForSnapshot()
    {
        string serverId = "goldsrc-snapshot";
        string installationId = CreateSharedInstallation("snapshot-owner", GameServer.DOD.FullName);
        ServerConfig serverConfig = new ServerConfig(serverId)
        {
            ServerGame = GameServer.DOD.FullName,
            SharedInstallationId = installationId
        };
        string instanceRoot = Path.Combine(_testRoot, "servers", serverId, "serverfiles");
        string sharedServerFilesRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
        Directory.CreateDirectory(instanceRoot);
        Directory.CreateDirectory(sharedServerFilesRoot);
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", instanceRoot },
            { "sharedserverfiles", sharedServerFilesRoot }
        };
        List<ConfigurationBackupPath> paths = ConfigurationBackupArchive.ValidatePlan(
            roots,
            BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots));

        Assert.IsFalse(ConfigurationBackupArchive.HasExistingFiles(roots, paths));

        string configPath = Path.Combine(instanceRoot, "dod", "server.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "hostname=before restore");

        Assert.IsTrue(ConfigurationBackupArchive.HasExistingFiles(roots, paths));

        string mapCyclePath = Path.Combine(
            sharedServerFilesRoot,
            "dod",
            "windowsgsm_goldsrc_mapcycle_goldsrc-snapshot.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mapCyclePath)!);
        File.WriteAllText(mapCyclePath, "dod_Anzio");
        string snapshotPath = Path.Combine(_testRoot, "pre-restore.wgsmbackup");
        ConfigurationBackupResult snapshot = ConfigurationBackupArchive.Create(
            snapshotPath,
            serverId,
            serverConfig.ServerGame,
            installationId,
            "build-1",
            roots,
            paths);

        CollectionAssert.AreEqual(
            new[]
            {
                "serverfiles/dod/server.cfg",
                "sharedserverfiles/dod/windowsgsm_goldsrc_mapcycle_goldsrc-snapshot.txt"
            },
            snapshot.IncludedPaths.Select(path => path.RootId + "/" + path.RelativePath).ToArray());
    }

    private string CreateSharedInstallation(string ownerServerId, string gameKey)
    {
        using (SharedInstallationResolution installation = SharedInstallationStore.ResolveOrCreate(
            ownerServerId,
            gameKey,
            "90",
            "hlds.exe"))
        {
            string serverFilesRoot = SharedInstallationStore.GetServerBinaryRoot(installation.InstallationId);
            File.WriteAllText(Path.Combine(serverFilesRoot, "hlds.exe"), "binary");
            SharedInstallationStore.MarkReady(installation.InstallationId, "build-1");
            return installation.InstallationId;
        }
    }

    [TestMethod]
    public void RestoreRejectsDifferentServerIdentityWithoutChangingFiles()
    {
        string instanceRoot = CreateInstanceRoot("identity");
        string configPath = Path.Combine(instanceRoot, "server.cfg");
        File.WriteAllText(configPath, "server.hostname=original");
        string archivePath = Path.Combine(_testRoot, "identity.wgsmbackup");
        CreateArchive(archivePath, instanceRoot);
        File.WriteAllText(configPath, "server.hostname=current");

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.Restore(
            archivePath,
            "2",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            AllowedPaths()));

        Assert.AreEqual("server.hostname=current", File.ReadAllText(configPath));
    }

    [TestMethod]
    public void RestoreRejectsCorruptArchiveDataBeforeChangingFiles()
    {
        string instanceRoot = CreateInstanceRoot("corrupt");
        string configPath = Path.Combine(instanceRoot, "server.cfg");
        string adminsPath = Path.Combine(instanceRoot, "admins.cfg");
        File.WriteAllText(configPath, "server.hostname=original");
        File.WriteAllText(adminsPath, "ownerid=123456");
        string archivePath = Path.Combine(_testRoot, "corrupt.wgsmbackup");
        var paths = new List<ConfigurationBackupPath>
        {
            new ConfigurationBackupPath("serverfiles", "server.cfg"),
            new ConfigurationBackupPath("serverfiles", "admins.cfg")
        };
        ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            paths);
        File.WriteAllText(configPath, "server.hostname=current");
        File.WriteAllText(adminsPath, "ownerid=987654");

        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("files/serverfiles/admins.cfg").Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("files/serverfiles/admins.cfg");
            using (StreamWriter writer = new StreamWriter(replacement.Open()))
            {
                writer.Write("ownerid=654321");
            }
        }

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.Restore(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            paths));

        Assert.AreEqual("server.hostname=current", File.ReadAllText(configPath));
        Assert.AreEqual("ownerid=987654", File.ReadAllText(adminsPath));
    }

    [TestMethod]
    public void RestoreCreatesMissingInstanceConfigurationRoot()
    {
        string sourceRoot = CreateInstanceRoot("missing-root-source");
        File.WriteAllText(Path.Combine(sourceRoot, "server.cfg"), "server.hostname=restored");
        string archivePath = Path.Combine(_testRoot, "missing-root.wgsmbackup");
        ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(sourceRoot),
            AllowedPaths());

        string targetParent = Path.Combine(_testRoot, "missing-root-target");
        Directory.CreateDirectory(targetParent);
        string targetRoot = Path.Combine(targetParent, "serverfiles");

        ConfigurationBackupArchive.Restore(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(targetRoot),
            AllowedPaths());

        Assert.AreEqual("server.hostname=restored", File.ReadAllText(Path.Combine(targetRoot, "server.cfg")));
    }

    [TestMethod]
    public void CreateRejectsTraversalPathWithoutPublishingAnArchive()
    {
        string instanceRoot = CreateInstanceRoot("traversal");
        File.WriteAllText(Path.Combine(instanceRoot, "server.cfg"), "server.hostname=safe");
        string archivePath = Path.Combine(_testRoot, "traversal.wgsmbackup");
        var paths = new[] { new ConfigurationBackupPath("serverfiles", "../server.cfg") };

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            paths));

        Assert.IsFalse(File.Exists(archivePath));
    }

    [TestMethod]
    public void RestoreRejectsTraversalInArchiveManifestWithoutChangingFiles()
    {
        string instanceRoot = CreateInstanceRoot("manifest-traversal");
        string configPath = Path.Combine(instanceRoot, "server.cfg");
        File.WriteAllText(configPath, "server.hostname=current");
        string archivePath = Path.Combine(_testRoot, "manifest-traversal.wgsmbackup");
        var manifest = new ConfigurationBackupManifest
        {
            ServerId = "1",
            GameKey = "Rust Dedicated Server",
            CreatedUtc = DateTime.UtcNow,
            Files = new List<ConfigurationBackupManifestEntry>
            {
                new ConfigurationBackupManifestEntry
                {
                    RootId = "serverfiles",
                    RelativePath = "../server.cfg",
                    Length = 0,
                    Sha256 = new string('0', 64)
                }
            }
        };

        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        using (StreamWriter writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
        {
            writer.Write(JsonSerializer.Serialize(manifest));
        }

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.GetRestorePreview(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            AllowedPaths()));

        Assert.AreEqual("server.hostname=current", File.ReadAllText(configPath));
    }

    [TestMethod]
    public void RestoreRejectsDifferentSharedInstallationBuildWithoutChangingFiles()
    {
        string instanceRoot = CreateInstanceRoot("installation-build");
        string configPath = Path.Combine(instanceRoot, "server.cfg");
        File.WriteAllText(configPath, "server.hostname=original");
        string installationId = Guid.NewGuid().ToString("N");
        string archivePath = Path.Combine(_testRoot, "installation-build.wgsmbackup");
        ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            installationId,
            "build-a",
            Roots(instanceRoot),
            AllowedPaths());
        File.WriteAllText(configPath, "server.hostname=current");

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.Restore(
            archivePath,
            "1",
            "Rust Dedicated Server",
            installationId,
            "build-b",
            Roots(instanceRoot),
            AllowedPaths()));

        Assert.AreEqual("server.hostname=current", File.ReadAllText(configPath));
    }

    private string CreateInstanceRoot(string name)
    {
        string path = Path.Combine(_testRoot, name, "serverfiles");
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string> Roots(string instanceRoot)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", instanceRoot }
        };
    }

    private static List<ConfigurationBackupPath> AllowedPaths()
    {
        return new List<ConfigurationBackupPath>
        {
            new ConfigurationBackupPath("serverfiles", "server.cfg")
        };
    }

    private static void CreateArchive(string archivePath, string instanceRoot)
    {
        File.WriteAllText(Path.Combine(instanceRoot, "server.cfg"), "server.hostname=original");
        ConfigurationBackupArchive.Create(
            archivePath,
            "1",
            "Rust Dedicated Server",
            string.Empty,
            string.Empty,
            Roots(instanceRoot),
            AllowedPaths());
    }
}
