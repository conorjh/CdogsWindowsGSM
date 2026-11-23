using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Functions
{
    internal enum FirewallTransportProtocol
    {
        Tcp,
        Udp
    }

    internal sealed class ServerFirewallRuleSpecification
    {
        public string Name { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public FirewallTransportProtocol Protocol { get; }
        public IReadOnlyList<int> LocalPorts { get; }

        public ServerFirewallRuleSpecification(
            string name,
            string displayName,
            string description,
            FirewallTransportProtocol protocol,
            IReadOnlyList<int> localPorts)
        {
            Name = name;
            DisplayName = displayName;
            Description = description;
            Protocol = protocol;
            LocalPorts = localPorts;
        }

        public string PortSummary => string.Join(", ", LocalPorts);
    }

    internal sealed class ServerFirewallPlan
    {
        public string ServerId { get; }
        public string GameName { get; }
        public IReadOnlyList<ServerFirewallRuleSpecification> Rules { get; }

        public ServerFirewallPlan(string serverId, string gameName, IReadOnlyList<ServerFirewallRuleSpecification> rules)
        {
            ServerId = serverId;
            GameName = gameName;
            Rules = rules;
        }

        public string PortSummary => string.Join("; ", Rules.Select(rule =>
            rule.Protocol.ToString().ToUpperInvariant() + " " + rule.PortSummary));
    }

    internal sealed class ServerFirewallSyncResult
    {
        public string ServerId { get; }
        public string GameName { get; }
        public string PortSummary { get; }
        public bool Succeeded { get; }
        public string Error { get; }

        public ServerFirewallSyncResult(string serverId, string gameName, string portSummary, bool succeeded, string error)
        {
            ServerId = serverId;
            GameName = gameName;
            PortSummary = portSummary;
            Succeeded = succeeded;
            Error = error;
        }
    }

    internal sealed class ServerFirewallTarget
    {
        public string ServerId { get; }
        public ServerConfig ServerConfig { get; }
        public object GameServer { get; }
        public string GameName { get; }
        public string Error { get; }

        public ServerFirewallTarget(string serverId, ServerConfig serverConfig, object gameServer, string error = null)
        {
            ServerId = serverId;
            ServerConfig = serverConfig;
            GameServer = gameServer;
            GameName = serverConfig?.ServerGame ?? string.Empty;
            Error = error ?? string.Empty;
        }
    }

    internal interface IServerFirewallRuleStore
    {
        Task SynchronizeAsync(
            IReadOnlyList<ServerFirewallPlan> plans,
            IReadOnlyList<string> serverRoots,
            IReadOnlyCollection<string> activeServerIds,
            bool removeOrphanedRules);
        Task RemoveServerRulesAsync(string serverId);
        Task SetManagedRulesEnabledAsync(bool enabled);
    }

    internal static class ServerFirewallPlanBuilder
    {
        private const string RulePrefix = "CdogsWindowsGSM-Server-";
        private static readonly Regex ValidServerId = new Regex(@"\A[0-9]{1,3}\z", RegexOptions.CultureInvariant);

        public static ServerFirewallPlan Build(ServerConfig serverConfig, object gameServer)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }
            if (string.IsNullOrWhiteSpace(serverConfig.ServerID) || !ValidServerId.IsMatch(serverConfig.ServerID))
            {
                throw new InvalidDataException("The server ID cannot be used to name its Windows Firewall rule.");
            }
            if (string.IsNullOrWhiteSpace(serverConfig.ServerGame))
            {
                throw new InvalidDataException("The configured game is missing; Windows Firewall ports cannot be determined.");
            }
            if (gameServer == null)
            {
                throw new InvalidDataException("The configured game could not be loaded; Windows Firewall ports cannot be determined.");
            }

            var portsByProtocol = new Dictionary<FirewallTransportProtocol, SortedSet<int>>
            {
                { FirewallTransportProtocol.Tcp, new SortedSet<int>() },
                { FirewallTransportProtocol.Udp, new SortedSet<int>() }
            };
            AddConfiguredPort(portsByProtocol, serverConfig.ServerPort, GetGameProtocol(gameServer), "server");
            if (!string.IsNullOrWhiteSpace(serverConfig.ServerQueryPort))
            {
                AddConfiguredPort(portsByProtocol, serverConfig.ServerQueryPort, GetQueryProtocol(gameServer), "query");
            }

            string rulePrefix = RulePrefix + serverConfig.ServerID + "-";
            var rules = new List<ServerFirewallRuleSpecification>();
            foreach (KeyValuePair<FirewallTransportProtocol, SortedSet<int>> group in portsByProtocol)
            {
                if (group.Value.Count == 0)
                {
                    continue;
                }

                string protocol = group.Key.ToString().ToUpperInvariant();
                string ports = string.Join(",", group.Value);
                rules.Add(new ServerFirewallRuleSpecification(
                    rulePrefix + protocol,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "WindowsGSM - Server #{0} - {1} ({2})",
                        serverConfig.ServerID,
                        serverConfig.ServerGame,
                        protocol),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Managed by WindowsGSM. Configured ports: {0} {1}.",
                        protocol,
                        ports),
                    group.Key,
                    group.Value.ToArray()));
            }

            return new ServerFirewallPlan(serverConfig.ServerID, serverConfig.ServerGame, rules);
        }

        public static bool TryGetServerIdFromRuleName(string ruleName, out string serverId)
        {
            serverId = string.Empty;
            if (string.IsNullOrWhiteSpace(ruleName) || !ruleName.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = ruleName.Substring(RulePrefix.Length);
            int separator = suffix.LastIndexOf('-');
            if (separator <= 0 || !ValidServerId.IsMatch(suffix.Substring(0, separator)))
            {
                return false;
            }

            string protocol = suffix.Substring(separator + 1);
            if (!string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            serverId = suffix.Substring(0, separator);
            return true;
        }

        private static FirewallTransportProtocol GetGameProtocol(object gameServer)
        {
            if (gameServer is MinecraftJava)
            {
                return FirewallTransportProtocol.Tcp;
            }

            return FirewallTransportProtocol.Udp;
        }

        private static FirewallTransportProtocol GetQueryProtocol(object gameServer)
        {
            object queryMethod = GetQueryMethod(gameServer);
            return queryMethod is GameServer.Query.FIVEM
                ? FirewallTransportProtocol.Tcp
                : FirewallTransportProtocol.Udp;
        }

        private static object GetQueryMethod(object gameServer)
        {
            var field = gameServer.GetType().GetField("QueryMethod");
            if (field != null)
            {
                return field.GetValue(gameServer);
            }

            var property = gameServer.GetType().GetProperty("QueryMethod");
            return property?.GetValue(gameServer);
        }

        private static void AddConfiguredPort(
            IDictionary<FirewallTransportProtocol, SortedSet<int>> portsByProtocol,
            string configuredPort,
            FirewallTransportProtocol protocol,
            string portDescription)
        {
            if (!int.TryParse(configuredPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ||
                port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
            {
                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture,
                    "The configured {0} port '{1}' is not between 1 and 65535.",
                    portDescription,
                    configuredPort ?? string.Empty));
            }

            portsByProtocol[protocol].Add(port);
        }
    }

    internal sealed class ServerFirewallManager
    {
        private const int MaximumPlansPerPowerShellCommand = 24;
        private readonly IServerFirewallRuleStore _ruleStore;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);

        public ServerFirewallManager()
            : this(new WindowsFirewallRuleStore())
        {
        }

        internal ServerFirewallManager(IServerFirewallRuleStore ruleStore)
        {
            _ruleStore = ruleStore ?? throw new ArgumentNullException(nameof(ruleStore));
        }

        public async Task<ServerFirewallSyncResult> SynchronizeAsync(
            ServerConfig serverConfig,
            object gameServer,
            IReadOnlyCollection<string> activeServerIds = null)
        {
            string serverId = serverConfig?.ServerID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverId) || !Regex.IsMatch(serverId, @"\A[0-9]{1,3}\z", RegexOptions.CultureInvariant))
            {
                return new ServerFirewallSyncResult(
                    serverId,
                    serverConfig?.ServerGame ?? string.Empty,
                    string.Empty,
                    false,
                    "A numeric server ID is required to synchronize Windows Firewall rules.");
            }

            IReadOnlyList<ServerFirewallSyncResult> results = await SynchronizeManyAsync(
                new[] { new ServerFirewallTarget(serverId, serverConfig, gameServer) },
                activeServerIds ?? new[] { serverId });
            return results[0];
        }

        public async Task<IReadOnlyList<ServerFirewallSyncResult>> SynchronizeManyAsync(
            IReadOnlyList<ServerFirewallTarget> targets,
            IReadOnlyCollection<string> activeServerIds)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            var results = new ServerFirewallSyncResult[targets.Count];
            var plans = new List<ServerFirewallPlan>();
            var planResultIndexes = new List<int>();
            var invalidServerRoots = new List<string>();
            for (int index = 0; index < targets.Count; index++)
            {
                ServerFirewallTarget target = targets[index];
                if (target == null)
                {
                    results[index] = new ServerFirewallSyncResult(string.Empty, string.Empty, string.Empty, false, "The firewall target is missing.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(target.Error))
                {
                    results[index] = new ServerFirewallSyncResult(
                        target.ServerId,
                        target.GameName,
                        string.Empty,
                        false,
                        target.Error);
                    if (!string.IsNullOrWhiteSpace(target.ServerId) &&
                        int.TryParse(target.ServerId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    {
                        invalidServerRoots.Add(ServerPath.GetServers(target.ServerId));
                    }
                    continue;
                }

                try
                {
                    ServerFirewallPlan plan = ServerFirewallPlanBuilder.Build(target.ServerConfig, target.GameServer);
                    plans.Add(plan);
                    planResultIndexes.Add(index);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
                {
                    results[index] = new ServerFirewallSyncResult(
                        target.ServerId,
                        target.GameName,
                        string.Empty,
                        false,
                        exception.Message);
                    if (!string.IsNullOrWhiteSpace(target.ServerId) &&
                        int.TryParse(target.ServerId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    {
                        invalidServerRoots.Add(ServerPath.GetServers(target.ServerId));
                    }
                }
            }

            IReadOnlyCollection<string> activeIds = activeServerIds ?? targets
                .Where(target => target != null && !string.IsNullOrWhiteSpace(target.ServerId))
                .Select(target => target.ServerId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var cleanupFailures = new List<string>();
            await _operationGate.WaitAsync();
            try
            {
                bool orphanCleanupCompleted = false;
                for (int offset = 0; offset < plans.Count; offset += MaximumPlansPerPowerShellCommand)
                {
                    int count = Math.Min(MaximumPlansPerPowerShellCommand, plans.Count - offset);
                    List<ServerFirewallPlan> planBatch = plans.Skip(offset).Take(count).ToList();
                    bool isLastBatch = offset + count == plans.Count;
                    var roots = planBatch.Select(plan => ServerPath.GetServers(plan.ServerId)).ToList();

                    try
                    {
                        await _ruleStore.SynchronizeAsync(
                            planBatch,
                            roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            activeIds,
                            removeOrphanedRules: isLastBatch);
                        orphanCleanupCompleted = isLastBatch;
                        for (int batchIndex = 0; batchIndex < count; batchIndex++)
                        {
                            ServerFirewallPlan plan = planBatch[batchIndex];
                            results[planResultIndexes[offset + batchIndex]] = new ServerFirewallSyncResult(
                                plan.ServerId,
                                plan.GameName,
                                plan.PortSummary,
                                true,
                                string.Empty);
                        }
                    }
                    catch (Exception exception) when (IsFirewallOperationException(exception))
                    {
                        for (int batchIndex = 0; batchIndex < count; batchIndex++)
                        {
                            ServerFirewallPlan plan = planBatch[batchIndex];
                            results[planResultIndexes[offset + batchIndex]] = new ServerFirewallSyncResult(
                                plan.ServerId,
                                plan.GameName,
                                plan.PortSummary,
                                false,
                                exception.Message);
                        }
                    }
                }

                for (int offset = 0; offset < invalidServerRoots.Count; offset += MaximumPlansPerPowerShellCommand)
                {
                    string[] rootBatch = invalidServerRoots
                        .Skip(offset)
                        .Take(MaximumPlansPerPowerShellCommand)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    try
                    {
                        await _ruleStore.SynchronizeAsync(
                            Array.Empty<ServerFirewallPlan>(),
                            rootBatch,
                            activeIds,
                            removeOrphanedRules: false);
                    }
                    catch (Exception exception) when (IsFirewallOperationException(exception))
                    {
                        cleanupFailures.Add("Legacy application rule cleanup failed: " + exception.Message);
                    }
                }

                if (!orphanCleanupCompleted)
                {
                    try
                    {
                        await _ruleStore.SynchronizeAsync(
                            Array.Empty<ServerFirewallPlan>(),
                            Array.Empty<string>(),
                            activeIds,
                            removeOrphanedRules: true);
                    }
                    catch (Exception exception) when (IsFirewallOperationException(exception))
                    {
                        cleanupFailures.Add("Orphaned firewall rule cleanup failed: " + exception.Message);
                    }
                }

                if (cleanupFailures.Count > 0)
                {
                    int errorIndex = Array.FindIndex(results, result => result != null);
                    if (errorIndex >= 0)
                    {
                        ServerFirewallSyncResult result = results[errorIndex];
                        results[errorIndex] = new ServerFirewallSyncResult(
                            result.ServerId,
                            result.GameName,
                            result.PortSummary,
                            false,
                            string.Join(Environment.NewLine, new[] { result.Error }.Concat(cleanupFailures).Where(message => !string.IsNullOrWhiteSpace(message))));
                    }
                    else
                    {
                        return new[]
                        {
                            new ServerFirewallSyncResult(string.Empty, string.Empty, string.Empty, false, string.Join(Environment.NewLine, cleanupFailures))
                        };
                    }
                }
            }
            finally
            {
                _operationGate.Release();
            }

            return results;
        }

        public async Task<IReadOnlyList<string>> RemoveServerRulesAsync(string serverId)
        {
            await _operationGate.WaitAsync();
            try
            {
                await _ruleStore.RemoveServerRulesAsync(serverId);
                return Array.Empty<string>();
            }
            catch (Exception exception) when (IsFirewallOperationException(exception))
            {
                return new[] { exception.Message };
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<IReadOnlyList<string>> SetManagedRulesEnabledAsync(bool enabled)
        {
            await _operationGate.WaitAsync();
            try
            {
                await _ruleStore.SetManagedRulesEnabledAsync(enabled);
                return Array.Empty<string>();
            }
            catch (Exception exception) when (IsFirewallOperationException(exception))
            {
                return new[] { exception.Message };
            }
            finally
            {
                _operationGate.Release();
            }
        }

        internal static IReadOnlyList<string> GetServerRuleNames(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId) || !Regex.IsMatch(serverId, @"\A[0-9]{1,3}\z", RegexOptions.CultureInvariant))
            {
                throw new ArgumentException("A numeric server ID is required.", nameof(serverId));
            }

            string prefix = "CdogsWindowsGSM-Server-" + serverId + "-";
            return new[] { prefix + "TCP", prefix + "UDP" };
        }

        private static bool IsFirewallOperationException(Exception exception)
        {
            return exception is ArgumentException || exception is IOException || exception is InvalidDataException ||
                exception is InvalidOperationException || exception is NotSupportedException || exception is OperationCanceledException ||
                exception is TimeoutException || exception is UnauthorizedAccessException || exception is SecurityException ||
                exception is System.ComponentModel.Win32Exception;
        }

    }

    internal sealed class WindowsFirewallRuleStore : IServerFirewallRuleStore
    {
        private const string RulePrefix = "CdogsWindowsGSM-Server-*";
        private const int CommandTimeoutSeconds = 30;
        private static readonly string PowerShellPath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

        public async Task SynchronizeAsync(
            IReadOnlyList<ServerFirewallPlan> plans,
            IReadOnlyList<string> serverRoots,
            IReadOnlyCollection<string> activeServerIds,
            bool removeOrphanedRules)
        {
            string script = BuildSynchronizeScript(plans, serverRoots, activeServerIds, removeOrphanedRules);
            string output = await RunPowerShellAsync(script);
            if (!string.Equals(output, "True", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows Firewall did not confirm the server-rule synchronization.");
            }
        }

        public async Task RemoveServerRulesAsync(string serverId)
        {
            IReadOnlyList<string> names = ServerFirewallManager.GetServerRuleNames(serverId);
            string script = "$ErrorActionPreference = 'Stop'; $names = @(" + string.Join(",", names.Select(PowerShellLiteral)) + "); " +
                "foreach ($name in $names) { Remove-NetFirewallRule -PolicyStore PersistentStore -Name $name -Confirm:$false -ErrorAction SilentlyContinue | Out-Null }; " +
                "[Console]::Out.Write('True');";
            string output = await RunPowerShellAsync(script);
            if (!string.Equals(output, "True", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows Firewall did not confirm the server-rule removal.");
            }
        }

        public async Task SetManagedRulesEnabledAsync(bool enabled)
        {
            string script = BuildSetManagedRulesEnabledScript(enabled);
            string output = await RunPowerShellAsync(script);
            if (!string.Equals(output, "True", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows Firewall did not confirm the managed server-rule state change.");
            }
        }

        internal static string BuildSetManagedRulesEnabledScript(bool enabled)
        {
            string enabledValue = enabled ? "True" : "False";
            return "$ErrorActionPreference = 'Stop'; $rules = @(Get-NetFirewallRule -PolicyStore PersistentStore -Name " +
                PowerShellLiteral(RulePrefix) + " -ErrorAction SilentlyContinue); " +
                "foreach ($rule in $rules) { Set-NetFirewallRule -Name $rule.Name -PolicyStore PersistentStore -Enabled " +
                enabledValue + " -ErrorAction Stop | Out-Null }; [Console]::Out.Write('True');";
        }

        internal static string BuildSynchronizeScript(
            IReadOnlyList<ServerFirewallPlan> plans,
            IReadOnlyList<string> serverRoots,
            IReadOnlyCollection<string> activeServerIds,
            bool removeOrphanedRules)
        {
            if (plans == null)
            {
                throw new ArgumentNullException(nameof(plans));
            }
            if (serverRoots == null)
            {
                throw new ArgumentNullException(nameof(serverRoots));
            }
            if (activeServerIds == null)
            {
                throw new ArgumentNullException(nameof(activeServerIds));
            }

            var rules = plans.SelectMany(plan => plan.Rules).ToList();
            var script = new StringBuilder("$ErrorActionPreference = 'Stop'; $rules = @(");
            script.Append(string.Join(",", rules.Select(rule =>
                "[pscustomobject]@{ Name=" + PowerShellLiteral(rule.Name) +
                "; DisplayName=" + PowerShellLiteral(rule.DisplayName) +
                "; Description=" + PowerShellLiteral(rule.Description) +
                "; Protocol=" + PowerShellLiteral(rule.Protocol.ToString().ToUpperInvariant()) +
                "; LocalPorts=" + PowerShellLiteral(string.Join(",", rule.LocalPorts)) + " }")));
            script.Append("); foreach ($item in $rules) { $localPortValues = [string[]]($item.LocalPorts -split ','); ");
            script.Append("$existing = @(Get-NetFirewallRule -PolicyStore PersistentStore -Name $item.Name -ErrorAction SilentlyContinue); ");
            script.Append("if ($existing.Count -gt 1) { Remove-NetFirewallRule -PolicyStore PersistentStore -Name $item.Name -Confirm:$false -ErrorAction Stop | Out-Null; $existing = @() }; ");
            script.Append("if ($existing.Count -eq 0) { ");
            script.Append("New-NetFirewallRule -PolicyStore PersistentStore -Name $item.Name -DisplayName $item.DisplayName -Description $item.Description -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol $item.Protocol -LocalPort $localPortValues -ErrorAction Stop | Out-Null ");
            script.Append("} else { ");
            script.Append("Set-NetFirewallRule -Name $item.Name -PolicyStore PersistentStore -NewDisplayName $item.DisplayName -Description $item.Description -Direction Inbound -Action Allow -Profile Any -ErrorAction Stop | Out-Null; ");
            script.Append("$existing[0] | Get-NetFirewallPortFilter -ErrorAction Stop | Set-NetFirewallPortFilter -Protocol $item.Protocol -LocalPort $localPortValues -ErrorAction Stop | Out-Null ");
            script.Append("} }; ");

            script.Append("$serverRoots = @(");
            script.Append(string.Join(",", serverRoots.Select(root => PowerShellLiteral(Path.GetFullPath(root)))));
            script.Append("); $filters = @(Get-NetFirewallApplicationFilter -PolicyStore PersistentStore -All -ErrorAction Stop); ");
            script.Append("foreach ($filter in $filters) { $program = [string]$filter.Program; if ([string]::IsNullOrWhiteSpace($program)) { continue }; ");
            script.Append("$isLegacyJavaRule = [IO.Path]::GetFileName($program).Equals('java.exe', [StringComparison]::OrdinalIgnoreCase); ");
            script.Append("$matchedServerRoot = $false; foreach ($root in $serverRoots) { ");
            script.Append("$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar; ");
            script.Append("if ($program.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $program.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $matchedServerRoot = $true; break } }; ");
            script.Append("if ($matchedServerRoot -or $isLegacyJavaRule) { foreach ($legacyRule in @(Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $filter -PolicyStore PersistentStore -ErrorAction Stop)) { ");
            script.Append("if ([string]$legacyRule.Name -like 'WindowsGSM-*') { Remove-NetFirewallRule -Name $legacyRule.Name -PolicyStore PersistentStore -Confirm:$false -ErrorAction Stop | Out-Null } } } }; ");

            script.Append("$activeServerIds = @(");
            if (removeOrphanedRules)
            {
                script.Append(string.Join(",", activeServerIds.Select(PowerShellLiteral)));
            }
            script.Append("); $plannedServerIds = @(");
            script.Append(string.Join(",", plans.Select(plan => PowerShellLiteral(plan.ServerId)).Distinct(StringComparer.OrdinalIgnoreCase)));
            script.Append("); $expectedRuleNames = @(");
            script.Append(string.Join(",", rules.Select(rule => PowerShellLiteral(rule.Name)).Distinct(StringComparer.OrdinalIgnoreCase)));
            script.Append("); $managedRules = @(Get-NetFirewallRule -PolicyStore PersistentStore -Name ");
            script.Append(PowerShellLiteral(RulePrefix));
            script.Append(" -ErrorAction SilentlyContinue); foreach ($managedRule in $managedRules) { ");
            script.Append("if ($managedRule.Name -match '^CdogsWindowsGSM-Server-([0-9]{1,3})-(TCP|UDP)$') { $managedServerId = [string]$Matches[1]; ");
            string orphanCondition = removeOrphanedRules ? "$activeServerIds -notcontains $managedServerId" : "$false";
            script.Append("if ((" + orphanCondition + ") -or ($plannedServerIds -contains $managedServerId -and $expectedRuleNames -notcontains $managedRule.Name)) { ");
            script.Append("Remove-NetFirewallRule -Name $managedRule.Name -PolicyStore PersistentStore -Confirm:$false -ErrorAction Stop | Out-Null } } }; ");
            script.Append("[Console]::Out.Write('True');");
            return script.ToString();
        }

        private static async Task<string> RunPowerShellAsync(string script)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PowerShellPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Windows PowerShell did not start.");
                }

                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CommandTimeoutSeconds)))
                {
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        await process.WaitForExitAsync();
                        throw new TimeoutException("The Windows Firewall operation timed out.");
                    }
                }

                string output = await standardOutput;
                string error = await standardError;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Windows Firewall PowerShell command failed with exit code {0}. {1} {2}",
                        process.ExitCode,
                        error,
                        output).Trim());
                }

                return output.Trim();
            }
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }
    }
}
