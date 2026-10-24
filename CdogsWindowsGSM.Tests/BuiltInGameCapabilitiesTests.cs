using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.Localization;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class BuiltInGameCapabilitiesTests
{
    private string _testRoot = string.Empty;
    private IDisposable? _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.BuiltInGameCapabilitiesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _pathScope = ServerPath.UseRootForTesting(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _pathScope?.Dispose();
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void SharedInstallationSupportsBackedUpGamesWithEnabledProviders()
    {
        ServerConfig serverConfig = new ServerConfig("source-server");
        string[] sourceGameKeys =
        {
            GameServer.CSGO.FullName,
            GameServer.CSS.FullName,
            GameServer.DODS.FullName,
            GameServer.GMOD.FullName,
            GameServer.HL2DM.FullName,
            GameServer.INS.FullName,
            GameServer.L4D2.FullName,
            GameServer.NMRIH.FullName,
            GameServer.TF2.FullName,
            GameServer.ZPS.FullName
        };
        string[] goldSourceGameKeys =
        {
            GameServer.CS.FullName,
            GameServer.CSCZ.FullName,
            GameServer.DOD.FullName,
            GameServer.DMC.FullName,
            GameServer.HLOF.FullName,
            GameServer.RCC.FullName,
            GameServer.TFC.FullName
        };

        object apiCapablePlugin = new ApiCapablePlugin(serverConfig);
        Assert.AreEqual(
            WindowsGSMPluginCapability.SharedInstallation,
            PluginCapabilityInspector.Inspect(apiCapablePlugin.GetType()).Capabilities & WindowsGSMPluginCapability.SharedInstallation);

        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.COD1.FullName, apiCapablePlugin));
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(
            GameServer.TFC.FullName,
            new GameServer.Engine.Source(serverConfig)));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.COD1.FullName));
        object cod1Server = GameServer.Data.Class.Get(GameServer.COD1.FullName, serverConfig);
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.COD1.FullName, cod1Server));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(GameServer.COD1.FullName));

        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.RUST.FullName));
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(
            GameServer.RUST.FullName,
            new GameServer.RUST(serverConfig)));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(GameServer.RUST.FullName));

        object bedrockServer = GameServer.Data.Class.Get(GameServer.MCBE.FullName, serverConfig);
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.MCBE.FullName));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.MCBE.FullName, bedrockServer));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(GameServer.MCBE.FullName));

        foreach (string gameKey in sourceGameKeys)
        {
            object sourceServer = GameServer.Data.Class.Get(gameKey, serverConfig);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(gameKey), gameKey);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(gameKey, sourceServer), gameKey);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(gameKey), gameKey);
        }

        foreach (string gameKey in goldSourceGameKeys)
        {
            object goldSourceServer = GameServer.Data.Class.Get(gameKey, serverConfig);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(gameKey), gameKey);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(gameKey, goldSourceServer), gameKey);
            Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(gameKey), gameKey);
        }

        Assert.IsFalse(BuiltInGameCapabilities.SupportsConfigurationBackup(GameServer.SDK2013.FullName));
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.SDK2013.FullName));
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation("GoldSrc Dedicated Server"));
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation("Unknown game"));
    }

    [TestMethod]
    public async Task BedrockSharedProviderResolvesExecutableAndBuildFromSharedBinaryRoot()
    {
        const string buildId = "1.26.52.3";
        ServerConfig serverConfig = new ServerConfig("bedrock-instance")
        {
            ServerGame = GameServer.MCBE.FullName
        };
        string installationId;
        string binaryRoot;
        using (SharedInstallationResolution sharedInstallation = SharedInstallationStore.ResolveOrCreate(
            "bedrock-owner",
            serverConfig.ServerGame,
            "serverBedrockWindows",
            "bedrock_server.exe"))
        {
            installationId = sharedInstallation.InstallationId;
            serverConfig.SharedInstallationId = installationId;
            binaryRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
            Directory.CreateDirectory(binaryRoot);
            File.WriteAllText(Path.Combine(binaryRoot, "bedrock_server.exe"), string.Empty);
            File.WriteAllText(Path.Combine(binaryRoot, "MCBE-version.txt"), buildId);
            SharedInstallationStore.MarkReady(installationId, buildId);
        }

        var bedrockServer = new GameServer.MCBE(serverConfig);

        Assert.IsTrue(bedrockServer.IsInstallValid());
        Assert.AreEqual(buildId, bedrockServer.GetLocalBuild());
        using (FileStream sharedLease = SharedInstallationStore.AcquireReadLeaseForServer(serverConfig))
        {
            Assert.IsNull(await bedrockServer.Update());
            Assert.AreEqual(LocalizationManager.Get("SharedInstallationInUse"), bedrockServer.Error);
        }
    }

    [TestMethod]
    public void BedrockConfigOnlyBackupContainsOnlyInstanceConfigurationFiles()
    {
        ServerConfig serverConfig = new ServerConfig("bedrock-backup")
        {
            ServerGame = GameServer.MCBE.FullName,
            SharedInstallationId = Guid.NewGuid().ToString("N")
        };
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(serverConfig.ServerID) }
        };

        List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots);

        CollectionAssert.AreEqual(
            new[] { "server.properties", "allowlist.json", "permissions.json" },
            plan.Select(path => path.RelativePath).ToArray());
        CollectionAssert.AreEqual(
            new[] { "serverfiles", "serverfiles", "serverfiles" },
            plan.Select(path => path.RootId).ToArray());
        Assert.AreEqual(1, roots.Count);
    }

    [TestMethod]
    public void BedrockServerPropertiesUseAssignedPortsAndPreserveOtherSettings()
    {
        ServerConfig serverConfig = new ServerConfig("bedrock-properties")
        {
            ServerName = "Bedrock Test",
            ServerMaxPlayer = "12",
            ServerPort = "19140",
            ServerMap = "Test World"
        };
        string configPath = ServerPath.GetServersServerFiles(serverConfig.ServerID, "server.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        string originalConfig = string.Join(Environment.NewLine, new[]
        {
            "server-name=Old Name",
            "max-players=10",
            "server-port=19132",
            "server-portv6=19133",
            "level-name=Old World",
            "online-mode=true"
        });
        File.WriteAllText(configPath, originalConfig);

        GameServer.MCBE.ApplyServerPropertiesSettings(configPath, serverConfig);

        string[] updatedLines = File.ReadAllLines(configPath);
        CollectionAssert.Contains(updatedLines, "server-name=Bedrock Test");
        CollectionAssert.Contains(updatedLines, "max-players=12");
        CollectionAssert.Contains(updatedLines, "server-port=19140");
        CollectionAssert.Contains(updatedLines, "server-portv6=19141");
        CollectionAssert.Contains(updatedLines, "level-name=Test World");
        CollectionAssert.Contains(updatedLines, "online-mode=true");

        string updatedConfig = File.ReadAllText(configPath);
        serverConfig.ServerPort = "65535";
        Assert.ThrowsExactly<InvalidDataException>(
            () => GameServer.MCBE.ApplyServerPropertiesSettings(configPath, serverConfig));
        Assert.AreEqual(updatedConfig, File.ReadAllText(configPath));
    }

    [TestMethod]
    public async Task RustSharedOperationsAreRejectedWithoutChangingInstanceFiles()
    {
        string serverId = "rust-shared";
        ServerConfig serverConfig = new ServerConfig(serverId)
        {
            ServerGame = GameServer.RUST.FullName,
            SharedInstallationId = Guid.NewGuid().ToString("N")
        };
        GameServer.RUST rust = new GameServer.RUST(serverConfig);
        string expectedError = LocalizationManager.Get("RustSharedInstallationUnavailable");

        Assert.IsNull(await rust.Start());
        Assert.AreEqual(expectedError, rust.Error);
        Assert.AreEqual(expectedError, rust.Notice);

        Assert.IsNull(await rust.Update());
        Assert.AreEqual(expectedError, rust.Error);

        Assert.IsFalse(await rust.InstallConfigOnly());
        Assert.AreEqual(expectedError, rust.Error);

        Assert.IsNull(await rust.InstallSharedBinaries());
        Assert.AreEqual(expectedError, rust.Error);
        Assert.IsFalse(Directory.Exists(ServerPath.GetServersServerFiles(serverId)));
    }

    [TestMethod]
    public void GoldSourceConfigOnlyBackupIncludesInstanceConfigAndMapCycle()
    {
        string[] goldSourceGameKeys =
        {
            GameServer.CS.FullName,
            GameServer.CSCZ.FullName,
            GameServer.DOD.FullName,
            GameServer.DMC.FullName,
            GameServer.HLOF.FullName,
            GameServer.RCC.FullName,
            GameServer.TFC.FullName
        };

        foreach (string gameKey in goldSourceGameKeys)
        {
            ServerConfig serverConfig = new ServerConfig("goldsrc-private")
            {
                ServerGame = gameKey
            };
            GameServer.Engine.GoldSource goldSource = GameServer.Data.Class.Get(gameKey, serverConfig) as GameServer.Engine.GoldSource;
            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "serverfiles", ServerPath.GetServersServerFiles(serverConfig.ServerID) }
            };

            List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots);

            CollectionAssert.AreEqual(
                new[]
                {
                    Path.Combine(goldSource.Game, "server.cfg"),
                    Path.Combine(goldSource.Game, "mapcycle.txt")
                },
                plan.Select(path => path.RelativePath).ToArray(),
                gameKey);
            CollectionAssert.AreEqual(new[] { "serverfiles", "serverfiles" }, plan.Select(path => path.RootId).ToArray(), gameKey);
        }
    }

    [TestMethod]
    public void SharedGoldSourceConfigOnlyBackupUsesOnlyItsUniqueSharedMapCyclePath()
    {
        string serverId = "goldsrc-shared";
        ServerConfig serverConfig = new ServerConfig(serverId)
        {
            ServerGame = GameServer.CS.FullName
        };
        string installationId;
        string sharedServerFilesRoot;
        using (SharedInstallationResolution sharedInstallation = SharedInstallationStore.ResolveOrCreate(
            "goldsrc-owner",
            serverConfig.ServerGame,
            "90",
            "hlds.exe"))
        {
            installationId = sharedInstallation.InstallationId;
            serverConfig.SharedInstallationId = installationId;
            sharedServerFilesRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
            File.WriteAllText(Path.Combine(sharedServerFilesRoot, "hlds.exe"), "binary");
            SharedInstallationStore.MarkReady(installationId, "build-1");
        }

        GameServer.Engine.GoldSource goldSource = GameServer.Data.Class.Get(serverConfig.ServerGame, serverConfig) as GameServer.Engine.GoldSource;
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(serverId) },
            { "sharedserverfiles", sharedServerFilesRoot }
        };

        List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots);

        CollectionAssert.AreEqual(
            new[]
            {
                new ConfigurationBackupPath("serverfiles", Path.Combine(goldSource.Game, "server.cfg")),
                new ConfigurationBackupPath(
                    "sharedserverfiles",
                    Path.Combine(goldSource.Game, "windowsgsm_goldsrc_mapcycle_goldsrc-shared.txt"))
            }.Select(path => path.RootId + "/" + path.RelativePath).ToArray(),
            plan.Select(path => path.RootId + "/" + path.RelativePath).ToArray());
        Assert.AreEqual(
            Path.Combine(sharedServerFilesRoot, goldSource.Game, "windowsgsm_goldsrc_mapcycle_goldsrc-shared.txt"),
            goldSource.MapCyclePath);
        Assert.Throws<InvalidDataException>(() => BuiltInGameCapabilities.GetConfigurationBackupPlan(
            serverConfig,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "serverfiles", ServerPath.GetServersServerFiles(serverId) }
            }));
    }

    [TestMethod]
    public void SourceBackupPlanContainsOnlyInstanceServerConfigAndMapCycle()
    {
        ServerConfig serverConfig = new ServerConfig("source-server")
        {
            ServerGame = GameServer.CSS.FullName
        };
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(serverConfig.ServerID) }
        };

        List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots);

        CollectionAssert.AreEqual(
            new[]
            {
                new ConfigurationBackupPath("serverfiles", Path.Combine("cstrike", "cfg", "server.cfg")),
                new ConfigurationBackupPath("serverfiles", Path.Combine("cstrike", "mapcycle.txt"))
            }.Select(path => path.RelativePath).ToArray(),
            plan.Select(path => path.RelativePath).ToArray());
        CollectionAssert.AreEqual(
            new[] { "serverfiles", "serverfiles" },
            plan.Select(path => path.RootId).ToArray());
    }

    [TestMethod]
    public void Cod1MeatBotsBackupAddsOnlyItsInstanceModServerConfig()
    {
        ServerConfig serverConfig = new ServerConfig("cod1-server")
        {
            ServerGame = GameServer.COD1.FullName,
            Cod1MeatBots = true
        };
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(serverConfig.ServerID) }
        };

        List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots);

        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine("main", "server.cfg"),
                Path.Combine(GameServer.COD1.ModDirectoryName, "server.cfg")
            },
            plan.Select(path => path.RelativePath).ToArray());
        Assert.AreEqual(1, roots.Count);
    }

    public sealed class ApiCapablePlugin
    {
        public string AppId => "12345";
        public string StartPath => "server.exe";

        public ApiCapablePlugin(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            throw new NotSupportedException();
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }
    }
}
