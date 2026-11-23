using System.IO;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GoldSourceMapPreflightTests
{
    [TestMethod]
    public async Task Start_FailsWithContentErrorWhenConfiguredMapIsMissing()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.GoldSourceMapPreflightTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            using IDisposable pathScope = ServerPath.UseRootForTesting(testRoot);
            const string serverId = "opposing-force-missing-map";
            string serverFiles = ServerPath.GetServersServerFiles(serverId);
            Directory.CreateDirectory(serverFiles);
            File.WriteAllText(Path.Combine(serverFiles, "hlds.exe"), string.Empty);

            var serverConfig = new ServerConfig(serverId)
            {
                ServerMap = "op4_bootcamp"
            };
            var gameServer = new OpposingForceGameServer(serverConfig);

            Assert.IsNull(await gameServer.Start());
            StringAssert.Contains(gameServer.Error, "op4_bootcamp");
            StringAssert.Contains(gameServer.Error, "AppID 50");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private sealed class OpposingForceGameServer : GoldSource
    {
        public OpposingForceGameServer(ServerConfig serverConfig) : base(serverConfig) { }
        public override string Game => "gearbox";
        public override string AppId => "50";
    }
}
