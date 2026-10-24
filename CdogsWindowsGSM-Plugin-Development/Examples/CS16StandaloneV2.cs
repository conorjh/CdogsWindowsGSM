using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    public class CS16StandaloneV2
    {
        private const string SteamCmdAppId = "90";
        private const string GameAppId = "10";
        private readonly ServerConfig _serverData;

        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.CS16StandaloneV2",
            author = "Your name",
            description = "Standalone Counter-Strike 1.6 plugin tutorial",
            version = "1.0",
            url = "",
            color = "#ffffff"
        };

        public string Error;
        public string Notice;
        public string StartPath = "hlds.exe";
        public string FullName = "Counter-Strike 1.6 (standalone plugin tutorial)";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 1;
        public object QueryMethod = new A2S();
        public string Game = "cstrike";
        public string AppId = GameAppId;
        public string Port = "27015";
        public string QueryPort = "27015";
        public string Defaultmap = "de_dust2";
        public string Maxplayers = "24";
        public string Additional = string.Empty;

        public CS16StandaloneV2(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            try
            {
                string instanceRoot = ServerPath.GetServersServerFiles(_serverData.ServerID);
                string gameRoot = Path.Combine(instanceRoot, Game);
                Directory.CreateDirectory(gameRoot);
                string configPath = Path.Combine(gameRoot, "server.cfg");
                if (!File.Exists(configPath))
                {
                    string hostname = string.IsNullOrWhiteSpace(_serverData.ServerName)
                        ? "Counter-Strike 1.6 tutorial server"
                        : _serverData.ServerName;
                    hostname = hostname.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\"", string.Empty);
                    File.WriteAllText(configPath,
                        "hostname \"" + hostname + "\"" + Environment.NewLine +
                        "sv_lan 0" + Environment.NewLine +
                        "mp_timelimit 30" + Environment.NewLine);
                }

                string mapCyclePath = Path.Combine(gameRoot, "mapcycle.txt");
                if (!File.Exists(mapCyclePath))
                {
                    File.WriteAllText(mapCyclePath, Defaultmap + Environment.NewLine);
                }

                File.WriteAllText(Path.Combine(instanceRoot, "steam_appid.txt"), GameAppId + Environment.NewLine);
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = "Could not create the instance configuration: " + exception.Message;
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
                new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "server.cfg")),
                new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "mapcycle.txt"))
            });
        }

        public async Task<Process> Install()
        {
            Error = string.Empty;
            try
            {
                var steamCmd = new WindowsGSM.Installer.SteamCMD();
                Process process = await steamCmd.Install(_serverData.ServerID, Game, SteamCmdAppId, true);
                Error = process == null
                    ? (string.IsNullOrWhiteSpace(steamCmd.Error) ? "SteamCMD did not start the installation." : steamCmd.Error)
                    : string.Empty;
                return process;
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = "Could not start the SteamCMD installation: " + exception.Message;
                return null;
            }
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Process process = null;
            try
            {
                string instanceRoot = ServerPath.GetServersServerFiles(_serverData.ServerID);
                string executablePath = Path.Combine(instanceRoot, StartPath);
                if (!File.Exists(executablePath))
                {
                    Error = StartPath + " was not found (" + executablePath + ").";
                    return Task.FromResult<Process>(null);
                }

                var arguments = new StringBuilder("-console -game ");
                arguments.Append(Game);
                AppendOption(arguments, "-ip", _serverData.ServerIP);
                AppendOption(arguments, "-port", _serverData.ServerPort);
                AppendOption(arguments, "-maxplayers", _serverData.ServerMaxPlayer);
                AppendOption(arguments, "+sv_setsteamaccount", _serverData.ServerGSLT);
                string map = string.IsNullOrWhiteSpace(_serverData.ServerMap) ? Defaultmap : _serverData.ServerMap;
                AppendOption(arguments, "+map", map);
                if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
                {
                    arguments.Append(' ').Append(_serverData.ServerParam);
                }

                process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = instanceRoot,
                        FileName = executablePath,
                        Arguments = arguments.ToString(),
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };

                if (!process.Start())
                {
                    Error = "The game server process did not start.";
                    process.Dispose();
                    return Task.FromResult<Process>(null);
                }

                return Task.FromResult(process);
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = "Could not start the game server: " + exception.Message;
                process?.Dispose();
                return Task.FromResult<Process>(null);
            }
        }

        public Task Stop(Process process)
        {
            return Task.Run(() =>
            {
                if (process != null && !process.HasExited)
                {
                    ServerConsole.SendMessageToMainWindow(process.MainWindowHandle, "quit");
                }
            });
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            Error = string.Empty;
            try
            {
                var result = await WindowsGSM.Installer.SteamCMD.UpdateEx(
                    _serverData.ServerID,
                    SteamCmdAppId,
                    validate,
                    modName: Game,
                    custom: custom);

                Error = result.Item2 ?? string.Empty;
                if (result.Item1 == null && string.IsNullOrWhiteSpace(Error))
                {
                    Error = "SteamCMD did not start the update.";
                }

                return result.Item1;
            }
            catch (Exception exception) when (IsExpectedOperationFailure(exception))
            {
                Error = "Could not start the SteamCMD update: " + exception.Message;
                return null;
            }
        }

        public bool IsInstallValid()
        {
            return File.Exists(ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Error = "Select a folder that contains " + StartPath + ".";
                return false;
            }

            bool isValid = File.Exists(Path.Combine(path, StartPath));
            Error = isValid ? string.Empty : "Could not find " + StartPath + " in " + path + ".";
            return isValid;
        }

        public string GetLocalBuild()
        {
            var steamCmd = new WindowsGSM.Installer.SteamCMD();
            string build = steamCmd.GetLocalBuild(_serverData.ServerID, SteamCmdAppId);
            Error = steamCmd.Error ?? string.Empty;
            return build;
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCmd = new WindowsGSM.Installer.SteamCMD();
            string build = await steamCmd.GetRemoteBuild(SteamCmdAppId);
            Error = steamCmd.Error ?? string.Empty;
            return build;
        }

        private static void AppendOption(StringBuilder arguments, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                arguments.Append(' ').Append(name).Append(' ').Append(value);
            }
        }

        private static bool IsExpectedOperationFailure(Exception exception)
        {
            return exception is ArgumentException ||
                exception is IOException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is UnauthorizedAccessException ||
                exception is Win32Exception ||
                exception is System.Net.WebException;
        }
    }
}
