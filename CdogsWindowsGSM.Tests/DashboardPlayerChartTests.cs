using System.Collections.Generic;
using WindowsGSM;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class DashboardPlayerChartTests
{
    [TestMethod]
    public void PlayerCountsIncludeOnlyRunningGamesWithPlayersAndAggregateByGame()
    {
        var rows = new List<ServerTable>
        {
            new() { Game = "Counter-Strike: Source", StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "2/16" },
            new() { Game = "Counter-Strike: Source", StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "3/16" },
            new() { Game = "Rust", StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "4/20" },
            new() { Game = "Mindustry", StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "0/20" },
            new() { Game = "Rust", StatusCode = MainWindow.ServerStatus.Stopped, Maxplayers = "5/20" },
            new() { Game = "Invalid players", StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "unknown" },
            new() { Game = string.Empty, StatusCode = MainWindow.ServerStatus.Started, Maxplayers = "5/20" }
        };

        List<(string Game, int Players)> counts = MainWindow.GetDashboardPlayerCounts(rows);

        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual("Counter-Strike: Source", counts[0].Game);
        Assert.AreEqual(5, counts[0].Players);
        Assert.AreEqual("Rust", counts[1].Game);
        Assert.AreEqual(4, counts[1].Players);
    }
}
