using System;
using System.IO;
using System.Linq;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class SharedInstallationStoreTests
{
    private string _installationId = string.Empty;
    private string _appId = string.Empty;
    private string _testRoot;
    private IDisposable _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.SharedInstallationStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _pathScope = ServerPath.UseRootForTesting(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_installationId))
            {
                SharedInstallationStore.DeleteIfUnreferenced(_installationId);
            }
        }
        finally
        {
            _pathScope?.Dispose();
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ReadyInstallationsAreReusedAndReadLeasesBlockUpdates()
    {
        string gameKey = "WindowsGSM.Tests." + Guid.NewGuid().ToString("N");
        _appId = "test-" + Guid.NewGuid().ToString("N");
        string executableName = "server.exe";

        using (SharedInstallationResolution created = SharedInstallationStore.ResolveOrCreate("1", gameKey, _appId, executableName))
        {
            _installationId = created.InstallationId;
            Assert.IsTrue(created.Created);

            string serverFilesRoot = ServerPath.GetServersServerFiles("1");
            File.WriteAllText(Path.Combine(serverFilesRoot, executableName), "test binary marker");
            SharedInstallationStore.MarkReady(created.InstallationId, "test-build");
            Assert.IsFalse(Directory.Exists(ServerPath.GetSharedInstallationRoot(created.InstallationId)));
            Assert.IsTrue(SharedInstallationStore.IsServerFilesRootOwner("1", created.InstallationId));
        }

        using (SharedInstallationResolution reused = SharedInstallationStore.ResolveOrCreate("2", gameKey, _appId, executableName))
        {
            Assert.IsFalse(reused.Created);
            Assert.AreEqual(_installationId, reused.InstallationId);
            Assert.AreEqual(ServerPath.GetServersServerFiles("1"), SharedInstallationStore.GetServerBinaryRoot(reused.InstallationId));
        }

        using (SharedInstallationStore.AcquireReadLease(_installationId))
        {
            Assert.Throws<InvalidOperationException>(() => SharedInstallationStore.AcquireExclusiveLease(_installationId));
        }

        using (SharedInstallationStore.AcquireExclusiveLease(_installationId))
        {
            SharedInstallationRecord record = SharedInstallationStore.GetInstallations()
                .Single(item => item.Id == _installationId);
            Assert.AreEqual("test-build", record.BuildId);
        }

        Assert.IsTrue(SharedInstallationStore.DeleteIfUnreferenced(_installationId));
        Assert.IsFalse(Directory.Exists(ServerPath.GetServersServerFiles("1")));
        _installationId = string.Empty;
    }

    [TestMethod]
    public void SharedBinaryRootRemainsUntilTheLastInstanceReferenceIsRemoved()
    {
        string gameKey = "WindowsGSM.Tests." + Guid.NewGuid().ToString("N");
        string appId = "test-" + Guid.NewGuid().ToString("N");
        string installationId;
        string binaryRoot;

        using (SharedInstallationResolution created = SharedInstallationStore.ResolveOrCreate("1", gameKey, appId, "server.exe"))
        {
            installationId = created.InstallationId;
            binaryRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
            File.WriteAllText(Path.Combine(binaryRoot, "server.exe"), "shared binary");
            SharedInstallationStore.MarkReady(installationId, "test-build");
        }

        WriteSharedReference("1", installationId);
        WriteSharedReference("2", installationId);

        Assert.IsFalse(SharedInstallationStore.DeleteIfUnreferenced(installationId));
        File.Delete(ServerPath.GetServersConfigs("1", "WindowsGSM.cfg"));
        Assert.IsFalse(SharedInstallationStore.DeleteIfUnreferenced(installationId));
        Assert.IsTrue(File.Exists(Path.Combine(binaryRoot, "server.exe")));

        File.Delete(ServerPath.GetServersConfigs("2", "WindowsGSM.cfg"));
        Assert.IsTrue(SharedInstallationStore.DeleteIfUnreferenced(installationId));
        Assert.IsFalse(Directory.Exists(binaryRoot));
    }

    [TestMethod]
    public void ServerIdAllocationSkipsAnIdReservedBySharedBinaries()
    {
        using (SharedInstallationResolution created = SharedInstallationStore.ResolveOrCreate(
            "1",
            "WindowsGSM.Tests." + Guid.NewGuid().ToString("N"),
            "test-" + Guid.NewGuid().ToString("N"),
            "server.exe"))
        {
            _installationId = created.InstallationId;

            ServerConfig nextServer = new ServerConfig(string.Empty);

            Assert.AreEqual("2", nextServer.ServerID);
        }
    }

    [TestMethod]
    public void FailedUnreferencedInstallationIsRemovedFromRegistryAndOwnerRoot()
    {
        string installationId;
        string binaryRoot;
        using (SharedInstallationResolution created = SharedInstallationStore.ResolveOrCreate(
            "1",
            "WindowsGSM.Tests." + Guid.NewGuid().ToString("N"),
            "test-" + Guid.NewGuid().ToString("N"),
            "server.exe"))
        {
            installationId = created.InstallationId;
            _installationId = installationId;
            binaryRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
            File.WriteAllText(Path.Combine(binaryRoot, "partial-install.marker"), "incomplete");
        }

        Assert.IsTrue(SharedInstallationStore.DeleteIfUnreferenced(installationId));
        Assert.IsFalse(SharedInstallationStore.GetInstallations().Any(item => item.Id == installationId));
        Assert.IsFalse(Directory.Exists(binaryRoot));
        _installationId = string.Empty;
    }

    [TestMethod]
    public void LegacySharedInstallationWithoutOwnerKeepsItsInstallationFolder()
    {
        string installationId = Guid.NewGuid().ToString("N");
        string gameKey = "WindowsGSM.Tests." + Guid.NewGuid().ToString("N");
        string appId = "test-" + Guid.NewGuid().ToString("N");
        string legacyServerFilesRoot = ServerPath.GetSharedInstallationServerFiles(installationId);
        Directory.CreateDirectory(legacyServerFilesRoot);
        File.WriteAllText(Path.Combine(legacyServerFilesRoot, "server.exe"), "legacy binary");
        Directory.CreateDirectory(ServerPath.Get("configs"));
        File.WriteAllText(
            ServerPath.GetSharedInstallationsRegistry(),
            "{\"SchemaVersion\":1,\"Installations\":[{" +
                "\"Id\":\"" + installationId + "\",\"GameKey\":\"" + gameKey + "\",\"AppId\":\"" + appId + "\"," +
                "\"CapabilityVersion\":1,\"BuildId\":\"legacy-build\",\"State\":\"Ready\",\"CreatedUtc\":\"2025-01-01T00:00:00Z\"}]} ");

        using (SharedInstallationResolution reused = SharedInstallationStore.ResolveOrCreate("1", gameKey, appId, "server.exe"))
        {
            Assert.IsFalse(reused.Created);
            Assert.AreEqual(installationId, reused.InstallationId);
            Assert.AreEqual(legacyServerFilesRoot, SharedInstallationStore.GetServerBinaryRoot(installationId));
            Assert.IsFalse(SharedInstallationStore.IsServerFilesRootOwner("1", installationId));
        }
    }

    private static void WriteSharedReference(string serverId, string installationId)
    {
        Directory.CreateDirectory(ServerPath.GetServersConfigs(serverId));
        File.WriteAllText(
            ServerPath.GetServersConfigs(serverId, "WindowsGSM.cfg"),
            ServerConfig.SettingName.SharedInstallationId + "=\"" + installationId + "\"");
    }
}