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
            GameServer.L4D.FullName,
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

        ServerConfig luantiConfig = new ServerConfig("luanti-server")
        {
            ServerGame = GameServer.Luanti.FullName,
            ServerMap = "world"
        };
        object luantiServer = GameServer.Data.Class.Get(GameServer.Luanti.FullName, luantiConfig);
        Assert.IsInstanceOfType<GameServer.Luanti>(luantiServer);
        Assert.IsFalse(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.Luanti.FullName, luantiServer));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(GameServer.Luanti.FullName));
        Assert.IsFalse(GlobalServerList.IsSupportedGame(GameServer.Luanti.FullName));
        Assert.IsFalse(string.IsNullOrWhiteSpace(GameServer.Data.Icon.ResourceManager.GetString(GameServer.Luanti.FullName)));
        List<ConfigurationBackupPath> luantiBackupPlan = BuiltInGameCapabilities.GetConfigurationBackupPlan(
            luantiConfig,
            new Dictionary<string, string> { ["serverfiles"] = Path.Combine(_testRoot, "luanti-serverfiles") });
        CollectionAssert.AreEquivalent(
            new[] { "minetest.conf", Path.Combine("worlds", "world", "world.mt") },
            luantiBackupPlan.Select(path => path.RelativePath).ToArray());

        ServerConfig l4dConfig = new ServerConfig("left4dead-server")
        {
            ServerGame = GameServer.L4D.FullName
        };
        object l4dServer = GameServer.Data.Class.Get(GameServer.L4D.FullName, l4dConfig);
        Assert.IsInstanceOfType<GameServer.L4D>(l4dServer);
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(GameServer.L4D.FullName, l4dServer));
        Assert.AreEqual("left4dead", ((GameServer.L4D)l4dServer).Game);
        Assert.AreEqual("222840", ((GameServer.L4D)l4dServer).AppId);

        ServerConfig legacyL4DConfig = new ServerConfig("left4dead-legacy-server")
        {
            ServerGame = GameServer.L4D.LegacyPluginFullName
        };
        object legacyL4DServer = GameServer.Data.Class.Get(legacyL4DConfig.ServerGame, legacyL4DConfig);
        Assert.IsInstanceOfType<GameServer.L4D>(legacyL4DServer);
        Assert.IsTrue(BuiltInGameCapabilities.SupportsSharedInstallation(legacyL4DConfig.ServerGame, legacyL4DServer));
        Assert.IsTrue(BuiltInGameCapabilities.SupportsConfigurationBackup(legacyL4DConfig.ServerGame));

        List<ConfigurationBackupPath> l4dBackupPlan = BuiltInGameCapabilities.GetConfigurationBackupPlan(
            l4dConfig,
            new Dictionary<string, string> { ["serverfiles"] = Path.Combine(_testRoot, "l4d-serverfiles") });
        CollectionAssert.AreEquivalent(
            new[] { Path.Combine("left4dead", "cfg", "server.cfg"), Path.Combine("left4dead", "mapcycle.txt") },
            l4dBackupPlan.Select(path => path.RelativePath).ToArray());

        List<ConfigurationBackupPath> legacyL4DBackupPlan = BuiltInGameCapabilities.GetConfigurationBackupPlan(
            legacyL4DConfig,
            new Dictionary<string, string> { ["serverfiles"] = Path.Combine(_testRoot, "l4d-legacy-serverfiles") });
        CollectionAssert.AreEquivalent(
            l4dBackupPlan.Select(path => path.RelativePath).ToArray(),
            legacyL4DBackupPlan.Select(path => path.RelativePath).ToArray());
        string? left4DeadIcon = GameServer.Data.Icon.ResourceManager.GetString(GameServer.L4D.FullName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(left4DeadIcon));
        Assert.AreEqual(left4DeadIcon, GameServer.Data.Icon.ResourceManager.GetString(GameServer.L4D.LegacyPluginFullName));
    }

    [TestMethod]
    public async Task Left4DeadConfigOnlyCreatesItsInstanceConfigAndMapCycle()
    {
        var config = new ServerConfig("left4dead-config")
        {
            ServerGame = GameServer.L4D.FullName,
            ServerName = "L4D test server",
            ServerPort = "27015",
            ServerParam = "-nocrashdialog +clientport {{clientport}}"
        };
        var server = new GameServer.L4D(config);
        GameServer.Engine.ISharedInstallationProvider sharedProvider = server;

        Assert.IsTrue(await sharedProvider.InstallConfigOnly(), server.Error);
        Assert.AreEqual("-nocrashdialog +clientport 27005", config.ServerParam);
        server.CreateServerCFG();

        string configPath = ServerPath.GetServersServerFiles(config.ServerID, "left4dead", "cfg", "server.cfg");
        Assert.IsTrue(File.Exists(configPath));
        string serverConfig = File.ReadAllText(configPath);
        StringAssert.Contains(serverConfig, "hostname \"L4D test server\"");
        StringAssert.Contains(serverConfig, "rcon_password \"");
        StringAssert.Contains(serverConfig, "sv_lan 0");
        Assert.IsTrue(File.Exists(ServerPath.GetServersServerFiles(config.ServerID, "left4dead", "mapcycle.txt")));
        Assert.AreEqual("-nocrashdialog +clientport 27005", config.ServerParam);
    }

    [TestMethod]
    public async Task Left4DeadConfigOnlyRejectsPortsThatCannotProduceAClientPort()
    {
        var config = new ServerConfig("left4dead-invalid-port")
        {
            ServerGame = GameServer.L4D.FullName,
            ServerPort = "10",
            ServerParam = "+clientport {{clientport}}"
        };
        var server = new GameServer.L4D(config);
        GameServer.Engine.ISharedInstallationProvider sharedProvider = server;

        Assert.IsFalse(await sharedProvider.InstallConfigOnly());
        StringAssert.Contains(server.Error, "greater than 10");
        Assert.IsFalse(Directory.Exists(ServerPath.GetServersServerFiles(config.ServerID, "left4dead")));
    }

    [TestMethod]
    public void BuiltInFeatureRowsListAllGamesAndTheirV2Capabilities()
    {
        IReadOnlyList<BuiltInGameFeatureRow> rows = BuiltInGameCapabilities.GetFeatureRows();

        Assert.AreEqual(59, rows.Count);
        Assert.AreEqual(59, rows.Select(row => row.Game).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(20, rows.Count(row => row.SupportsSharedInstallation));
        Assert.AreEqual(22, rows.Count(row => row.SupportsConfigurationBackup));
        Assert.IsTrue(rows.All(row => row.SupportsProcessHealthCheck));
        Assert.IsTrue(rows.Any(row => row.Game == GameServer.RUST.FullName && row.SupportsQueryHealthCheck));
        Assert.IsFalse(rows.Single(row => row.Game == GameServer.Mindustry.FullName).SupportsQueryHealthCheck);
        Assert.IsFalse(rows.Single(row => row.Game == GameServer.MC.FullName).SupportsGlobalServerListCheck);
        BuiltInGameFeatureRow luanti = rows.Single(row => row.Game == GameServer.Luanti.FullName);
        Assert.IsFalse(luanti.SupportsSharedInstallation);
        Assert.IsTrue(luanti.SupportsConfigurationBackup);
        Assert.IsTrue(luanti.SupportsProcessHealthCheck);
        Assert.IsFalse(luanti.SupportsQueryHealthCheck);
        Assert.IsFalse(luanti.SupportsGlobalServerListCheck);
        Assert.AreEqual("\u2717", luanti.SharedInstallationIndicator);
        Assert.AreEqual("\u2713", luanti.ConfigurationBackupIndicator);
        Assert.AreEqual("\u2713", luanti.ProcessHealthIndicator);
        Assert.AreEqual("\u2717", luanti.QueryHealthIndicator);
        Assert.AreEqual(LocalizationManager.Get("FeatureNotSupported"), luanti.QueryHealth);
        BuiltInGameFeatureRow left4Dead = rows.Single(row => row.Game == GameServer.L4D.FullName);
        Assert.IsTrue(left4Dead.SupportsSharedInstallation);
        Assert.IsTrue(left4Dead.SupportsConfigurationBackup);
        Assert.IsTrue(left4Dead.SupportsQueryHealthCheck);
        Assert.IsTrue(left4Dead.SupportsGlobalServerListCheck);
        BuiltInGameFeatureRow rust = rows.Single(row => row.Game == GameServer.RUST.FullName);
        Assert.IsFalse(rust.SupportsSharedInstallation);
        Assert.IsTrue(rust.SupportsConfigurationBackup);
        Assert.AreEqual(LocalizationManager.Get("FeatureSupported"), rust.ProcessHealth);
        Assert.AreEqual(LocalizationManager.Get("FeatureSupported"), rust.QueryHealth);
        Assert.AreEqual(
            LocalizationManager.Get(GlobalServerList.IsSupportedGame(GameServer.RUST.FullName) ? "FeatureSupported" : "FeatureNotSupported"),
            rust.GlobalServerList);
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
    public async Task BedrockStart_RefreshesConfiguredPortsBeforeLaunching()
    {
        ServerConfig serverConfig = new ServerConfig("bedrock-start-port")
        {
            ServerGame = GameServer.MCBE.FullName,
            ServerName = "Bedrock Test",
            ServerMaxPlayer = "12",
            ServerPort = "19140",
            ServerMap = "Test World",
            ServerParam = "/c exit"
        };
        string instanceRoot = ServerPath.GetServersServerFiles(serverConfig.ServerID);
        Directory.CreateDirectory(instanceRoot);
        File.WriteAllLines(Path.Combine(instanceRoot, "server.properties"), new[]
        {
            "server-name=Bedrock Test",
            "max-players=12",
            "server-port=19132",
            "server-portv6=19133",
            "level-name=Test World",
            "online-mode=true"
        });

        var bedrockServer = new GameServer.MCBE(serverConfig)
        {
            StartPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            AllowsEmbedConsole = false
        };
        Process process = null;
        try
        {
            process = await bedrockServer.Start();

            Assert.IsNotNull(process, bedrockServer.Error);
            string[] properties = File.ReadAllLines(Path.Combine(instanceRoot, "server.properties"));
            CollectionAssert.Contains(properties, "server-port=19140");
            CollectionAssert.Contains(properties, "server-portv6=19141");
            CollectionAssert.Contains(properties, "online-mode=true");
        }
        finally
        {
            if (process != null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.Dispose();
            }
        }
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
    public void GoldSourceBuildIdentityUsesDedicatedServerManifestForEveryMod()
    {
        (string GameKey, string ClientAppId)[] goldSourceGames =
        {
            (GameServer.CS.FullName, "10"),
            (GameServer.CSCZ.FullName, "80"),
            (GameServer.DOD.FullName, "30"),
            (GameServer.DMC.FullName, "40"),
            (GameServer.HLOF.FullName, "50"),
            (GameServer.RCC.FullName, "60"),
            (GameServer.TFC.FullName, "20")
        };

        foreach ((string gameKey, string clientAppId) in goldSourceGames)
        {
            string serverId = "goldsrc-build-" + clientAppId;
            var config = new ServerConfig(serverId) { ServerGame = gameKey };
            GameServer.Engine.GoldSource server = GameServer.Data.Class.Get(gameKey, config) as GameServer.Engine.GoldSource;
            Assert.IsNotNull(server, gameKey);
            Assert.AreEqual(clientAppId, server.AppId, gameKey);
            Assert.AreEqual("90", server.SharedInstallationAppId, gameKey);

            string steamAppsDirectory = ServerPath.GetServersServerFiles(serverId, "steamapps");
            Directory.CreateDirectory(steamAppsDirectory);
            File.WriteAllText(
                Path.Combine(steamAppsDirectory, "appmanifest_90.acf"),
                "\"AppState\"\r\n{\r\n\t\"appid\"\t\"90\"\r\n\t\"buildid\"\t\"123456789\"\r\n}");
            File.WriteAllText(
                Path.Combine(steamAppsDirectory, $"appmanifest_{clientAppId}.acf"),
                $"\"AppState\"\r\n{{\r\n\t\"appid\"\t\"{clientAppId}\"\r\n\t\"buildid\"\t\"987654321\"\r\n}}");

            Assert.AreEqual("123456789", server.GetLocalBuild(), gameKey);
        }
    }

    [TestMethod]
    public async Task GoldSourceSharedConfigOnlyCreatesInstanceConfigsAndUniqueMapCyclesForEveryMod()
    {
        string[] goldSourceGames =
        {
            GameServer.CS.FullName,
            GameServer.CSCZ.FullName,
            GameServer.DOD.FullName,
            GameServer.DMC.FullName,
            GameServer.HLOF.FullName,
            GameServer.RCC.FullName,
            GameServer.TFC.FullName
        };

        foreach (string gameKey in goldSourceGames)
        {
            string ownerId = "goldsrc-config-owner-" + Guid.NewGuid().ToString("N");
            string clientId = "goldsrc-config-client-" + Guid.NewGuid().ToString("N");
            string installationId;
            string sharedBinaryRoot;
            using (SharedInstallationResolution installation = SharedInstallationStore.ResolveOrCreate(
                ownerId,
                gameKey,
                "90",
                "hlds.exe"))
            {
                installationId = installation.InstallationId;
                sharedBinaryRoot = SharedInstallationStore.GetServerBinaryRoot(installationId);
                File.WriteAllText(Path.Combine(sharedBinaryRoot, "hlds.exe"), "test-binary");
                SharedInstallationStore.MarkReady(installationId, "test-build");
            }

            ServerConfig ownerConfig = CreateSharedGoldSourceConfig(ownerId, gameKey, installationId);
            ServerConfig clientConfig = CreateSharedGoldSourceConfig(clientId, gameKey, installationId);
            GameServer.Engine.GoldSource owner = GameServer.Data.Class.Get(gameKey, ownerConfig) as GameServer.Engine.GoldSource;
            GameServer.Engine.GoldSource client = GameServer.Data.Class.Get(gameKey, clientConfig) as GameServer.Engine.GoldSource;
            Assert.IsNotNull(owner, gameKey);
            Assert.IsNotNull(client, gameKey);

            string ownerConfigPath = ServerPath.GetServersServerFiles(ownerId, owner.Game, "server.cfg");
            string clientConfigPath = ServerPath.GetServersServerFiles(clientId, client.Game, "server.cfg");
            string ownerConfigContents = "hostname=owner configuration";
            string clientConfigContents = "hostname=client configuration";
            Directory.CreateDirectory(Path.GetDirectoryName(ownerConfigPath));
            Directory.CreateDirectory(Path.GetDirectoryName(clientConfigPath));
            File.WriteAllText(ownerConfigPath, ownerConfigContents);
            File.WriteAllText(clientConfigPath, clientConfigContents);

            Assert.IsTrue(await owner.InstallConfigOnly(), owner.Error);
            Assert.IsTrue(await client.InstallConfigOnly(), client.Error);

            Assert.AreEqual(ownerConfigContents, File.ReadAllText(ownerConfigPath), gameKey);
            Assert.AreEqual(clientConfigContents, File.ReadAllText(clientConfigPath), gameKey);
            Assert.IsTrue(File.Exists(owner.MapCyclePath), gameKey);
            Assert.IsTrue(File.Exists(client.MapCyclePath), gameKey);
            Assert.AreNotEqual(Path.GetFullPath(owner.MapCyclePath), Path.GetFullPath(client.MapCyclePath), gameKey);

            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "serverfiles", ServerPath.GetServersServerFiles(clientId) },
                { "sharedserverfiles", sharedBinaryRoot }
            };
            List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(clientConfig, roots);
            CollectionAssert.AreEqual(
                new[]
                {
                    "serverfiles/" + Path.Combine(client.Game, "server.cfg").Replace('\\', '/'),
                    "sharedserverfiles/" + Path.Combine(client.Game, GameServer.Engine.GoldSource.GetSharedMapCycleFileName(clientId)).Replace('\\', '/')
                },
                plan.Select(path => path.RootId + "/" + path.RelativePath.Replace('\\', '/')).ToArray(),
                gameKey);
        }
    }

    private static ServerConfig CreateSharedGoldSourceConfig(string serverId, string gameKey, string installationId)
    {
        return new ServerConfig(serverId)
        {
            ServerGame = gameKey,
            ServerName = serverId,
            ServerPort = "27015",
            SharedInstallationId = installationId
        };
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
