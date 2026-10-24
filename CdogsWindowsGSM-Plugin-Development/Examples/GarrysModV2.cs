using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Plugins
{
    public class GarrysModV2 : Source
    {
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.GarrysModV2",
            author = "Your name",
            description = "Garry's Mod V2 plugin tutorial",
            version = "1.0",
            url = "",
            color = "#ffffff"
        };

        public GarrysModV2(ServerConfig serverData) : base(serverData)
        {
        }

        public string FullName { get { return "Garry's Mod Dedicated Server (V2 tutorial)"; } }

        public override string Game { get { return "garrysmod"; } }
        public override string AppId { get { return "4020"; } }
        public override string Defaultmap { get { return "gm_construct"; } }
        public override string Additional { get { return "-tickrate 66 +gamemode sandbox"; } }

        public new void CreateServerCFG()
        {
            Error = string.Empty;
            try
            {
                WriteInstanceFiles(ServerPath.GetServersServerFiles(serverData.ServerID));
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = GetFailureMessage("Could not create the instance configuration", exception);
            }
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!context.ApprovedRoots.ContainsKey("serverfiles"))
            {
                throw new InvalidOperationException("The host did not approve the serverfiles backup root.");
            }

            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "cfg", "server.cfg")),
                new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "mapcycle.txt"))
            });
        }

        public async Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Error = string.Empty;
            try
            {
                var steamCmd = new WindowsGSM.Installer.SteamCMD();
                Process process = await steamCmd.InstallToPath(context.InstallationRoot, string.Empty, context.AppId, true);
                if (process == null)
                {
                    Error = string.IsNullOrWhiteSpace(steamCmd.Error)
                        ? "SteamCMD did not start the shared installation."
                        : steamCmd.Error;
                    return null;
                }

                return process;
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = GetFailureMessage("Failed to start the shared installation", exception);
                return null;
            }
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Error = string.Empty;
            try
            {
                WriteInstanceFiles(context.InstanceRoot);
                return Task.FromResult(SharedInstallationOperationResult.Succeeded());
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = GetFailureMessage("Failed to create the instance configuration", exception);
                return Task.FromResult(SharedInstallationOperationResult.Failed(Error));
            }
        }

        private void WriteInstanceFiles(string instanceRoot)
        {
            string gameRoot = Path.Combine(instanceRoot, Game);
            string configDirectory = Path.Combine(gameRoot, "cfg");
            Directory.CreateDirectory(configDirectory);

            string serverName = serverData == null || string.IsNullOrWhiteSpace(serverData.ServerName)
                ? "Garry's Mod V2 tutorial"
                : serverData.ServerName;
            serverName = serverName.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\"", string.Empty);

            string configPath = Path.Combine(configDirectory, "server.cfg");
            File.WriteAllText(configPath,
                "hostname \"" + serverName + "\"" + Environment.NewLine +
                "sv_lan 0" + Environment.NewLine +
                "mp_timelimit 30" + Environment.NewLine);

            string mapCyclePath = Path.Combine(gameRoot, "mapcycle.txt");
            File.WriteAllText(mapCyclePath, Defaultmap + Environment.NewLine);
        }

        private static bool IsExpectedOperationFailure(Exception exception)
        {
            return exception is ArgumentException ||
                exception is IOException ||
                exception is InvalidDataException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is Win32Exception ||
                exception is System.Net.WebException;
        }

        private static string GetFailureMessage(string operation, Exception exception)
        {
            string reason = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
            return operation + ": " + reason;
        }
    }
}