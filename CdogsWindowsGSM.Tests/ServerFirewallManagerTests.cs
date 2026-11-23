using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class ServerFirewallManagerTests
{
    [TestMethod]
    public void Build_UsesConfiguredPortsInsteadOfAdapterDefaultsOrDetectedPorts()
    {
        var config = new ServerConfig("12")
        {
            ServerGame = GameServer.RUST.FullName,
            ServerPort = "28123",
            ServerQueryPort = "28124"
        };
        object server = GameServer.Data.Class.Get(config.ServerGame, config);

        ServerFirewallPlan plan = ServerFirewallPlanBuilder.Build(config, server);

        ServerFirewallRuleSpecification udpRule = plan.Rules.Single(rule => rule.Protocol == FirewallTransportProtocol.Udp);
        CollectionAssert.AreEqual(new[] { 28123, 28124 }, udpRule.LocalPorts.ToArray());
        Assert.AreEqual("UDP 28123, 28124", plan.PortSummary);
    }

    [TestMethod]
    public void Build_AddsSeparateTcpAndUdpRulesForMinecraftConfiguredPorts()
    {
        var config = new ServerConfig("13")
        {
            ServerGame = GameServer.MC.FullName,
            ServerPort = "25581",
            ServerQueryPort = "25582"
        };
        object server = GameServer.Data.Class.Get(config.ServerGame, config);

        ServerFirewallPlan plan = ServerFirewallPlanBuilder.Build(config, server);

        CollectionAssert.AreEqual(
            new[] { FirewallTransportProtocol.Tcp, FirewallTransportProtocol.Udp },
            plan.Rules.Select(rule => rule.Protocol).OrderBy(protocol => protocol).ToArray());
        CollectionAssert.AreEqual(new[] { 25581 }, plan.Rules.Single(rule => rule.Protocol == FirewallTransportProtocol.Tcp).LocalPorts.ToArray());
        CollectionAssert.AreEqual(new[] { 25582 }, plan.Rules.Single(rule => rule.Protocol == FirewallTransportProtocol.Udp).LocalPorts.ToArray());
    }

    [TestMethod]
    public void Build_AddsConfiguredFiveMHttpQueryPortOverTcpAndGamePortOverUdp()
    {
        var config = new ServerConfig("50")
        {
            ServerGame = GameServer.GTA5.FullName,
            ServerPort = "30125",
            ServerQueryPort = "30125"
        };
        object server = GameServer.Data.Class.Get(config.ServerGame, config);

        ServerFirewallPlan plan = ServerFirewallPlanBuilder.Build(config, server);

        CollectionAssert.AreEqual(new[] { 30125 }, plan.Rules.Single(rule => rule.Protocol == FirewallTransportProtocol.Udp).LocalPorts.ToArray());
        CollectionAssert.AreEqual(new[] { 30125 }, plan.Rules.Single(rule => rule.Protocol == FirewallTransportProtocol.Tcp).LocalPorts.ToArray());
    }

    [TestMethod]
    public void Build_RejectsInvalidConfiguredPortsBeforeChangingFirewallRules()
    {
        var config = new ServerConfig("15")
        {
            ServerGame = GameServer.RUST.FullName,
            ServerPort = "70000",
            ServerQueryPort = "27016"
        };
        object server = GameServer.Data.Class.Get(config.ServerGame, config);

        Assert.ThrowsExactly<System.IO.InvalidDataException>(() => ServerFirewallPlanBuilder.Build(config, server));
    }

    [TestMethod]
    public async Task Synchronize_UpdatesConfiguredPortsAndRemovesObsoleteProtocolRules()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        var config = new ServerConfig("16")
        {
            ServerGame = GameServer.MC.FullName,
            ServerPort = "25565",
            ServerQueryPort = "25565"
        };

        ServerFirewallSyncResult firstResult = await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));
        Assert.IsTrue(firstResult.Succeeded, firstResult.Error);
        Assert.AreEqual(2, store.Rules.Count);
        Assert.AreEqual("25565", store.Rules["CdogsWindowsGSM-Server-16-TCP"].PortSummary);
        Assert.AreEqual("25565", store.Rules["CdogsWindowsGSM-Server-16-UDP"].PortSummary);

        config.ServerGame = GameServer.L4D.FullName;
        config.ServerPort = "27105";
        config.ServerQueryPort = "27106";
        ServerFirewallSyncResult secondResult = await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));

        Assert.IsTrue(secondResult.Succeeded, secondResult.Error);
        Assert.AreEqual(1, store.Rules.Count);
        Assert.AreEqual("27105, 27106", store.Rules["CdogsWindowsGSM-Server-16-UDP"].PortSummary);
        Assert.IsFalse(store.Rules.ContainsKey("CdogsWindowsGSM-Server-16-TCP"));
    }

    [TestMethod]
    public async Task SynchronizeMany_ChunksLargeServerListsIntoBoundedStoreOperations()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        var targets = new List<ServerFirewallTarget>();
        var activeServerIds = new List<string>();
        for (int id = 1; id <= 49; id++)
        {
            string serverId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var config = new ServerConfig(serverId)
            {
                ServerGame = GameServer.RUST.FullName,
                ServerPort = (23000 + id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ServerQueryPort = (24000 + id).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            targets.Add(new ServerFirewallTarget(serverId, config, GameServer.Data.Class.Get(config.ServerGame, config)));
            activeServerIds.Add(serverId);
        }

        IReadOnlyList<ServerFirewallSyncResult> results = await manager.SynchronizeManyAsync(targets, activeServerIds);

        Assert.AreEqual(49, results.Count);
        Assert.IsTrue(results.All(result => result.Succeeded));
        Assert.AreEqual(3, store.SynchronizationCallCount);
        Assert.AreEqual(1, store.OrphanCleanupCallCount);
        Assert.AreEqual(49, store.Rules.Count);

        ServerFirewallPlan[] largestBatch = targets.Take(24)
            .Select(target => ServerFirewallPlanBuilder.Build(target.ServerConfig, target.GameServer))
            .ToArray();
        string script = WindowsFirewallRuleStore.BuildSynchronizeScript(
            largestBatch,
            largestBatch.Select(plan => ServerPath.GetServers(plan.ServerId)).ToArray(),
            activeServerIds,
            removeOrphanedRules: true);
        Assert.IsTrue(script.Length < 32767, "The largest firewall batch must fit in the Windows process command-line limit.");
    }

    [TestMethod]
    public async Task RemoveServerRules_RemovesOnlyTheServerManagedProtocolRules()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        var config = new ServerConfig("17")
        {
            ServerGame = GameServer.MC.FullName,
            ServerPort = "25565",
            ServerQueryPort = "25565"
        };
        await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));

        IReadOnlyList<string> failures = await manager.RemoveServerRulesAsync(config.ServerID);

        Assert.AreEqual(0, failures.Count);
        Assert.AreEqual(0, store.Rules.Count);
    }

    [TestMethod]
    public async Task SetManagedRulesEnabled_TogglesRulesWithoutRemovingThem()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        var config = new ServerConfig("22")
        {
            ServerGame = GameServer.RUST.FullName,
            ServerPort = "28123",
            ServerQueryPort = "28124"
        };
        await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));

        IReadOnlyList<string> disableFailures = await manager.SetManagedRulesEnabledAsync(false);

        Assert.AreEqual(0, disableFailures.Count);
        Assert.AreEqual(1, store.Rules.Count);
        Assert.IsFalse(store.EnabledStates["CdogsWindowsGSM-Server-22-UDP"]);

        IReadOnlyList<string> enableFailures = await manager.SetManagedRulesEnabledAsync(true);

        Assert.AreEqual(0, enableFailures.Count);
        Assert.AreEqual(1, store.Rules.Count);
        Assert.IsTrue(store.EnabledStates["CdogsWindowsGSM-Server-22-UDP"]);
    }

    [TestMethod]
    public async Task Synchronize_PreservesManuallyDisabledRules()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        var config = new ServerConfig("23")
        {
            ServerGame = GameServer.RUST.FullName,
            ServerPort = "28123",
            ServerQueryPort = "28124"
        };
        await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));
        await manager.SetManagedRulesEnabledAsync(false);

        config.ServerPort = "28125";
        ServerFirewallSyncResult result = await manager.SynchronizeAsync(config, GameServer.Data.Class.Get(config.ServerGame, config));

        Assert.IsTrue(result.Succeeded, result.Error);
        Assert.IsFalse(store.EnabledStates["CdogsWindowsGSM-Server-23-UDP"]);
        Assert.AreEqual("28124, 28125", store.Rules["CdogsWindowsGSM-Server-23-UDP"].PortSummary);
    }

    [TestMethod]
    public void BuildSynchronizeScriptUsesPersistentAnyProfileRulesAndOnlyConfiguredPorts()
    {
        var rule = new ServerFirewallRuleSpecification(
            "CdogsWindowsGSM-Server-18-UDP",
            "WindowsGSM - Server #18 - Rust Dedicated Server (UDP)",
            "Managed by WindowsGSM.",
            FirewallTransportProtocol.Udp,
            new[] { 28123 });

        ServerFirewallPlan plan = new ServerFirewallPlan("18", GameServer.RUST.FullName, new[] { rule });
        string script = WindowsFirewallRuleStore.BuildSynchronizeScript(
            new[] { plan },
            new[] { @"C:\WindowsGSM\servers\18" },
            new[] { "18" },
            removeOrphanedRules: true);

        StringAssert.Contains(script, "-PolicyStore PersistentStore");
        StringAssert.Contains(script, "-Profile Any");
        StringAssert.Contains(script, "-Enabled True");
        StringAssert.Contains(script, "-LocalPort $localPortValues");
        StringAssert.Contains(script, "LocalPorts='28123'");
        StringAssert.Contains(script, "New-NetFirewallRule -PolicyStore PersistentStore -Name $item.Name -DisplayName $item.DisplayName");
        StringAssert.Contains(script, "Set-NetFirewallRule -Name $item.Name -PolicyStore PersistentStore -NewDisplayName $item.DisplayName");
        Assert.IsFalse(script.Contains("Set-NetFirewallRule -Name $item.Name -PolicyStore PersistentStore -NewDisplayName $item.DisplayName -Description $item.Description -Direction Inbound -Action Allow -Enabled True", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("Set-NetFirewallRule -InputObject", StringComparison.Ordinal));
        StringAssert.Contains(script, "$activeServerIds = @('18')");
        StringAssert.Contains(script, "$plannedServerIds = @('18')");
        StringAssert.Contains(script, "$expectedRuleNames = @('CdogsWindowsGSM-Server-18-UDP')");
        StringAssert.Contains(script, "GetFileName($program).Equals('java.exe'");
        StringAssert.Contains(script, "-like 'WindowsGSM-*'");
        Assert.IsFalse(script.Contains("Get-NetConnectionProfile", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task BuildSetManagedRulesEnabledScriptTargetsManagedRulesAndHasValidSyntax()
    {
        string enableScript = WindowsFirewallRuleStore.BuildSetManagedRulesEnabledScript(true);
        string disableScript = WindowsFirewallRuleStore.BuildSetManagedRulesEnabledScript(false);

        StringAssert.Contains(enableScript, "-Name 'CdogsWindowsGSM-Server-*'");
        StringAssert.Contains(enableScript, "Set-NetFirewallRule -Name $rule.Name -PolicyStore PersistentStore -Enabled True");
        StringAssert.Contains(disableScript, "Set-NetFirewallRule -Name $rule.Name -PolicyStore PersistentStore -Enabled False");
        Assert.IsFalse(enableScript.Contains("-InputObject", StringComparison.Ordinal));
        Assert.IsFalse(disableScript.Contains("-InputObject", StringComparison.Ordinal));

        await AssertValidWindowsPowerShellScriptAsync(enableScript);
        await AssertValidWindowsPowerShellScriptAsync(disableScript);
    }

    [TestMethod]
    public async Task BuildSynchronizeScriptHasValidWindowsPowerShellSyntax()
    {
        var config = new ServerConfig("21")
        {
            ServerGame = GameServer.MC.FullName,
            ServerPort = "25565",
            ServerQueryPort = "25565"
        };
        ServerFirewallPlan plan = ServerFirewallPlanBuilder.Build(config, GameServer.Data.Class.Get(config.ServerGame, config));
        string script = WindowsFirewallRuleStore.BuildSynchronizeScript(
            new[] { plan },
            new[] { @"C:\WindowsGSM\servers\21" },
            new[] { "21" },
            removeOrphanedRules: true);
        string intermediateBatchScript = WindowsFirewallRuleStore.BuildSynchronizeScript(
            new[] { plan },
            new[] { @"C:\WindowsGSM\servers\21" },
            new[] { "21" },
            removeOrphanedRules: false);

        await AssertValidWindowsPowerShellScriptAsync(script);
        await AssertValidWindowsPowerShellScriptAsync(intermediateBatchScript);
    }

    private static async Task AssertValidWindowsPowerShellScriptAsync(string script)
    {
        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string parserCommand = "$scriptText = [System.Text.Encoding]::Unicode.GetString([System.Convert]::FromBase64String('" + encodedScript + "')); " +
            "$tokens = $null; $errors = $null; [System.Management.Automation.Language.Parser]::ParseInput($scriptText, [ref]$tokens, [ref]$errors) | Out-Null; " +
            "if ($errors.Count -gt 0) { [Console]::Error.Write(($errors | ForEach-Object { $_.Extent.StartLineNumber.ToString() + ':' + $_.Extent.StartColumnNumber.ToString() + ' ' + $_.Message }) -join '; '); exit 1 }; " +
            "[Console]::Out.Write('valid')";
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(parserCommand);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
            throw;
        }

        string output = await outputTask;
        string error = await errorTask;
        int contextStart = Math.Max(0, Math.Min(script.Length - 1, 2822));
        string scriptContext = script.Substring(contextStart, Math.Min(900, script.Length - contextStart));
        Assert.AreEqual(0, process.ExitCode, error + Environment.NewLine + scriptContext);
        Assert.AreEqual("valid", output.Trim());
    }

    [TestMethod]
    public async Task RemoveOrphanedRules_RemovesOnlyRulesForMissingServerIds()
    {
        var store = new InMemoryFirewallRuleStore();
        var manager = new ServerFirewallManager(store);
        store.Rules["CdogsWindowsGSM-Server-19-UDP"] = CreateRule("CdogsWindowsGSM-Server-19-UDP", FirewallTransportProtocol.Udp, 27015);
        store.Rules["CdogsWindowsGSM-Server-20-TCP"] = CreateRule("CdogsWindowsGSM-Server-20-TCP", FirewallTransportProtocol.Tcp, 25565);

        IReadOnlyList<ServerFirewallSyncResult> results = await manager.SynchronizeManyAsync(
            Array.Empty<ServerFirewallTarget>(),
            new[] { "19" });

        Assert.AreEqual(0, results.Count);
        Assert.IsTrue(store.Rules.ContainsKey("CdogsWindowsGSM-Server-19-UDP"));
        Assert.IsFalse(store.Rules.ContainsKey("CdogsWindowsGSM-Server-20-TCP"));
    }

    private static ServerFirewallRuleSpecification CreateRule(string name, FirewallTransportProtocol protocol, int port)
    {
        return new ServerFirewallRuleSpecification(name, name, "Managed by tests.", protocol, new[] { port });
    }

    private sealed class InMemoryFirewallRuleStore : IServerFirewallRuleStore
    {
        public Dictionary<string, ServerFirewallRuleSpecification> Rules { get; } = new Dictionary<string, ServerFirewallRuleSpecification>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> EnabledStates { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public int SynchronizationCallCount { get; private set; }
        public int OrphanCleanupCallCount { get; private set; }

        public Task SynchronizeAsync(
            IReadOnlyList<ServerFirewallPlan> plans,
            IReadOnlyList<string> serverRoots,
            IReadOnlyCollection<string> activeServerIds,
            bool removeOrphanedRules)
        {
            SynchronizationCallCount++;
            foreach (ServerFirewallPlan plan in plans)
            {
                var expectedNames = new HashSet<string>(plan.Rules.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);
                foreach (ServerFirewallRuleSpecification rule in plan.Rules)
                {
                    if (!EnabledStates.ContainsKey(rule.Name))
                    {
                        EnabledStates[rule.Name] = true;
                    }
                    Rules[rule.Name] = rule;
                }

                foreach (string ruleName in ServerFirewallManager.GetServerRuleNames(plan.ServerId))
                {
                    if (!expectedNames.Contains(ruleName))
                    {
                        Rules.Remove(ruleName);
                        EnabledStates.Remove(ruleName);
                    }
                }
            }

            if (removeOrphanedRules)
            {
                OrphanCleanupCallCount++;
                var activeIds = new HashSet<string>(activeServerIds, StringComparer.OrdinalIgnoreCase);
                foreach (string ruleName in Rules.Keys.ToList())
                {
                    if (ServerFirewallPlanBuilder.TryGetServerIdFromRuleName(ruleName, out string serverId) && !activeIds.Contains(serverId))
                    {
                        Rules.Remove(ruleName);
                        EnabledStates.Remove(ruleName);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task RemoveServerRulesAsync(string serverId)
        {
            foreach (string ruleName in ServerFirewallManager.GetServerRuleNames(serverId))
            {
                Rules.Remove(ruleName);
                EnabledStates.Remove(ruleName);
            }
            return Task.CompletedTask;
        }

        public Task SetManagedRulesEnabledAsync(bool enabled)
        {
            foreach (string ruleName in Rules.Keys)
            {
                EnabledStates[ruleName] = enabled;
            }
            return Task.CompletedTask;
        }
    }
}
