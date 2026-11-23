using System;
using System.IO;
using WindowsGSM.Functions;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class LuantiTests
{
    private string _testRoot = string.Empty;
    private IDisposable? _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.LuantiTests", Guid.NewGuid().ToString("N"));
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
    public void CreateServerCFG_UpdatesManagedSettingsAndPreservesCustomOptions()
    {
        var config = new ServerConfig("luanti-config")
        {
            ServerGame = GameServer.Luanti.FullName,
            ServerName = "Luanti test server",
            ServerPort = "30012",
            ServerMaxPlayer = "18",
            ServerMap = "world"
        };
        var server = new GameServer.Luanti(config);
        string configPath = ServerPath.GetServersServerFiles(config.ServerID, GameServer.Luanti.ConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        File.WriteAllText(configPath, "# keep this comment\nserver_password = keep-me\nmax_users = 5\nserver_announce = true\n");

        server.CreateServerCFG();

        Assert.IsTrue(string.IsNullOrEmpty(server.Error), server.Error);
        string generatedConfig = File.ReadAllText(configPath);
        StringAssert.Contains(generatedConfig, "# keep this comment");
        StringAssert.Contains(generatedConfig, "server_password = keep-me");
        StringAssert.Contains(generatedConfig, "server_name = Luanti test server");
        StringAssert.Contains(generatedConfig, "port = 30012");
        StringAssert.Contains(generatedConfig, "max_users = 18");
        StringAssert.Contains(generatedConfig, "server_announce = true");
    }

    [TestMethod]
    public void CreateServerCFG_RejectsInvalidPortWithoutWritingConfig()
    {
        var config = new ServerConfig("luanti-invalid-port")
        {
            ServerGame = GameServer.Luanti.FullName,
            ServerName = "Invalid port",
            ServerPort = "65536",
            ServerMaxPlayer = "10"
        };
        var server = new GameServer.Luanti(config);
        string configPath = ServerPath.GetServersServerFiles(config.ServerID, GameServer.Luanti.ConfigFileName);

        server.CreateServerCFG();

        Assert.IsFalse(string.IsNullOrWhiteSpace(server.Error));
        Assert.IsFalse(File.Exists(configPath));
    }

    [TestMethod]
    public void IsImportValid_RequiresLuantiExecutableAndAtLeastOneGame()
    {
        string importRoot = Path.Combine(_testRoot, "import");
        string executablePath = Path.Combine(importRoot, "bin", "luanti.exe");
        string gamePath = Path.Combine(importRoot, "games", "minetest_game");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
        Directory.CreateDirectory(Path.Combine(importRoot, "builtin"));
        Directory.CreateDirectory(Path.Combine(gamePath, "mods"));
        File.WriteAllText(executablePath, "test executable");
        File.WriteAllText(Path.Combine(importRoot, "builtin", "init.lua"), "-- test");
        File.WriteAllText(Path.Combine(gamePath, "game.conf"), "title = Minetest Game");
        var server = new GameServer.Luanti(new ServerConfig("luanti-import") { ServerGame = GameServer.Luanti.FullName });

        Assert.IsTrue(server.IsImportValid(importRoot), server.Error);

        File.Delete(Path.Combine(gamePath, "game.conf"));
        Assert.IsFalse(server.IsImportValid(importRoot));
        StringAssert.Contains(server.Error, "does not contain a game package");
    }

    [TestMethod]
    public void IsInstallValid_AcceptsAnEngineWithAnInstalledGame()
    {
        var config = new ServerConfig("luanti-valid-install") { ServerGame = GameServer.Luanti.FullName };
        string serverFiles = ServerPath.GetServersServerFiles(config.ServerID);
        string engineRoot = Path.Combine(serverFiles, "luanti");
        string gameRoot = Path.Combine(engineRoot, "games", "minetest_game");
        Directory.CreateDirectory(Path.Combine(engineRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(engineRoot, "builtin"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
        File.WriteAllText(Path.Combine(engineRoot, "bin", "luanti.exe"), "test executable");
        File.WriteAllText(Path.Combine(engineRoot, "builtin", "init.lua"), "-- test");
        File.WriteAllText(Path.Combine(gameRoot, "game.conf"), "title = Minetest Game");
        var server = new GameServer.Luanti(config);

        Assert.IsTrue(server.IsInstallValid(), server.Error);
    }

    [TestMethod]
    public void ReplaceEngineDirectory_PreservesWorldsGamesAndConfiguration()
    {
        string destination = Path.Combine(_testRoot, "serverfiles");
        string staging = Path.Combine(_testRoot, "staging");
        string packageRoot = Path.Combine(staging, "extracted", "luanti-package");
        string gamePath = Path.Combine(destination, "games", "minetest_game");
        string worldPath = Path.Combine(destination, "worlds", "world");
        Directory.CreateDirectory(Path.Combine(destination, "bin"));
        Directory.CreateDirectory(Path.Combine(gamePath, "mods"));
        Directory.CreateDirectory(worldPath);
        Directory.CreateDirectory(Path.Combine(packageRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "builtin"));
        File.WriteAllText(Path.Combine(destination, "bin", "old.exe"), "old binary");
        File.WriteAllText(Path.Combine(gamePath, "game.conf"), "title = Minetest Game");
        File.WriteAllText(Path.Combine(worldPath, "world.mt"), "gameid = minetest");
        File.WriteAllText(Path.Combine(destination, "minetest.conf"), "server_name = Existing server");
        File.WriteAllText(Path.Combine(destination, "luanti_version.txt"), "5.17.0");
        File.WriteAllText(Path.Combine(packageRoot, "bin", "luanti.exe"), "new binary");
        File.WriteAllText(Path.Combine(packageRoot, "builtin", "init.lua"), "-- built-in data");

        GameServer.Luanti.ReplaceEngineDirectory(packageRoot, destination, staging, preserveRootInstanceData: true);

        Assert.AreEqual("new binary", File.ReadAllText(Path.Combine(destination, "bin", "luanti.exe")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, "bin", "old.exe")));
        Assert.IsTrue(File.Exists(Path.Combine(gamePath, "game.conf")));
        Assert.AreEqual("gameid = minetest", File.ReadAllText(Path.Combine(worldPath, "world.mt")));
        Assert.AreEqual("server_name = Existing server", File.ReadAllText(Path.Combine(destination, "minetest.conf")));
        Assert.AreEqual("5.17.0", File.ReadAllText(Path.Combine(destination, "luanti_version.txt")));
    }

    [TestMethod]
    public void ReleaseFeedSelectsLatestStableLuantiVersion()
    {
        const string releaseFeed = "<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><title>Luanti 5.19.0-rc1</title></entry><entry><title>Luanti 5.18.0</title></entry><entry><title>Luanti 5.17.0</title></entry></feed>";

        Assert.AreEqual("5.18.0", GameServer.Luanti.ParseLatestStableReleaseVersion(releaseFeed));
    }

    [TestMethod]
    public void WorldNameRejectsPathsAndTraversal()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => GameServer.Luanti.ResolveWorldName("..\\other"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameServer.Luanti.ResolveWorldName("C:\\outside"));
    }
}
