using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace WindowsGSM.GameServer.Engine
{
    public class GoldSource : ISharedInstallationProvider
    {
        public Functions.ServerConfig serverData;

        public string Error;
        public string Notice;

        public string StartPath = "hlds.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.A2S();

        public virtual string Port { get { return "27015"; } }
        public virtual string QueryPort { get { return "27015"; } }
        public virtual string Defaultmap { get { return string.Empty; } }
        public virtual string Maxplayers { get { return "24"; } }
        public virtual string Additional { get { return string.Empty; } }

        public virtual string Game { get { return string.Empty; } }
        public virtual string AppId { get { return string.Empty; } }
        public string SharedInstallationAppId => "90";
        public string SharedInstallationExecutableName => StartPath;

        public GoldSource(Functions.ServerConfig serverData) => this.serverData = serverData;

        public string MapDirectory => Path.Combine(GetBinaryRoot(), Game, "maps");

        public string MapCyclePath => UsesSharedInstallation
            ? Functions.ServerPath.GetSharedInstallationServerFiles(serverData.SharedInstallationId, Game, GetSharedMapCycleFileName(serverData.ServerID))
            : Functions.ServerPath.GetServersServerFiles(serverData.ServerID, Game, "mapcycle.txt");

        public static void DeleteSharedMapCycleFile(string installationId, string serverId)
        {
            string serverFilesRoot = Functions.ServerPath.GetSharedInstallationServerFiles(installationId);
            if (!Directory.Exists(serverFilesRoot))
            {
                return;
            }

            string fileName = GetSharedMapCycleFileName(serverId);
            foreach (string gameDirectory in Directory.GetDirectories(serverFilesRoot))
            {
                string mapCyclePath = Path.Combine(gameDirectory, fileName);
                if (File.Exists(mapCyclePath))
                {
                    File.Delete(mapCyclePath);
                }
            }
        }

        private static string GetSharedMapCycleFileName(string serverId)
        {
            return $"windowsgsm_goldsrc_mapcycle_{serverId}.txt";
        }

        public List<string> GetAvailableMaps()
        {
            if (!Directory.Exists(MapDirectory))
            {
                return new List<string>();
            }

            return Directory.GetFiles(MapDirectory, "*.bsp", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(map => map, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> GetMapRotation()
        {
            if (!File.Exists(MapCyclePath))
            {
                return new List<string>();
            }

            return File.ReadLines(MapCyclePath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("//") && !line.StartsWith("#"))
                .Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0])
                .ToList();
        }

        public void SaveMapRotation(IEnumerable<string> maps)
        {
            File.WriteAllLines(MapCyclePath, maps);
        }

        public async Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            FileStream sharedLease = null;
            try
            {
                sharedLease = Functions.SharedInstallationStore.AcquireReadLeaseForServer(serverData);
                string binaryRoot = GetBinaryRoot();
                string hldsPath = Path.Combine(binaryRoot, StartPath);
                if (!File.Exists(hldsPath))
                {
                    Error = $"{StartPath} not found ({hldsPath})";
                    sharedLease?.Dispose();
                    return null;
                }

                string instanceRoot = Functions.ServerPath.GetServersServerFiles(serverData.ServerID);
                string workingDirectory = UsesSharedInstallation ? binaryRoot : instanceRoot;
                string configPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, Game, "server.cfg");
                if (!File.Exists(configPath))
                {
                    Notice = $"server.cfg not found ({configPath})";
                    if (UsesSharedInstallation)
                    {
                        Error = Notice;
                        sharedLease?.Dispose();
                        return null;
                    }
                }

                StringBuilder sb = new StringBuilder();
                sb.Append($"-console -game {Game}");
                sb.Append(string.IsNullOrWhiteSpace(serverData.ServerIP) ? string.Empty : $" +ip {serverData.ServerIP}");
                sb.Append(string.IsNullOrWhiteSpace(serverData.ServerPort) ? string.Empty : $" -port {serverData.ServerPort}");
                sb.Append(string.IsNullOrWhiteSpace(serverData.ServerMaxPlayer) ? string.Empty : $" -maxplayers {serverData.ServerMaxPlayer}");
                sb.Append(string.IsNullOrWhiteSpace(serverData.ServerGSLT) ? string.Empty : $" +sv_setsteamaccount {serverData.ServerGSLT}");
                sb.Append(string.IsNullOrWhiteSpace(serverData.ServerParam) ? string.Empty : $" {serverData.ServerParam}");
                if (UsesSharedInstallation)
                {
                    sb.Append($" +servercfgfile \"{configPath}\"");
                }
                List<string> mapRotation = GetMapRotation();
                if (UsesSharedInstallation || mapRotation.Count > 0)
                {
                    sb.Append(UsesSharedInstallation
                        ? $" +mapcyclefile {GetSharedMapCycleFileName(serverData.ServerID)}"
                        : " +mapcyclefile mapcycle.txt");
                }
                string startMap = mapRotation.Count > 0 ? mapRotation[0] : serverData.ServerMap;
                sb.Append(string.IsNullOrWhiteSpace(startMap) ? string.Empty : $" +map {startMap}");
                if (!string.IsNullOrWhiteSpace(serverData.ServerParam) && serverData.ServerParam.Contains("-game "))
                {
                    sb.Replace($" -game {Game}", string.Empty);
                }
                string param = sb.ToString();

                Process p;
                if (!AllowsEmbedConsole)
                {
                    p = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = workingDirectory,
                            FileName = hldsPath,
                            Arguments = param,
                            WindowStyle = ProcessWindowStyle.Minimized,
                            UseShellExecute = false
                        },
                        EnableRaisingEvents = true
                    };
                    p.Start();
                    if (sharedLease != null)
                    {
                        Functions.SharedInstallationStore.ReleaseLeaseWhenProcessExits(p, sharedLease);
                        sharedLease = null;
                    }
                }
                else
                {
                    p = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = workingDirectory,
                            FileName = hldsPath,
                            Arguments = param,
                            WindowStyle = ProcessWindowStyle.Minimized,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        },
                        EnableRaisingEvents = true
                    };
                    var serverConsole = new Functions.ServerConsole(serverData.ServerID);
                    p.OutputDataReceived += serverConsole.AddOutput;
                    p.ErrorDataReceived += serverConsole.AddOutput;
                    p.Start();
                    if (sharedLease != null)
                    {
                        Functions.SharedInstallationStore.ReleaseLeaseWhenProcessExits(p, sharedLease);
                        sharedLease = null;
                    }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                return p;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                sharedLease?.Dispose();
                Error = exception.Message;
                return null;
            }
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "quit");
            });
        }

        public async void CreateServerCFG()
        {
            await InstallConfigOnly();
        }

        public async Task<bool> InstallConfigOnly()
        {
            Error = string.Empty;
            string configPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, Game, "server.cfg");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                if (!await Functions.Github.DownloadGameServerConfig(configPath, serverData.ServerGame))
                {
                    Error = $"Could not download {serverData.ServerGame} server.cfg.";
                    return false;
                }
                if (!File.Exists(MapCyclePath))
                {
                    File.WriteAllText(MapCyclePath, string.Empty);
                }

                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{hostname}}", serverData.ServerName);
                configText = configText.Replace("{{rcon_password}}", serverData.GetRCONPassword());
                File.WriteAllText(configPath, configText);

                string steamAppIdPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, "steam_appid.txt");
                File.WriteAllText(steamAppIdPath, AppId);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is System.Net.WebException)
            {
                Error = $"Failed to create {serverData.ServerGame} config: {exception.Message}";
                return false;
            }
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(serverData.ServerID, Game, "90", true);
            Error = steamCMD.Error;

            return p;
        }

        public async Task<Process> InstallSharedBinaries()
        {
            try
            {
                var steamCMD = new Installer.SteamCMD();
                Process process = await steamCMD.InstallToPath(GetBinaryRoot(), Game, "90", true);
                Error = steamCMD.Error;
                return process;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                Error = exception.Message;
                return null;
            }
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            FileStream sharedLease = null;
            try
            {
                sharedLease = Functions.SharedInstallationStore.AcquireExclusiveLeaseForServer(serverData);
                var (process, error) = await Installer.SteamCMD.UpdateExAtPath(GetBinaryRoot(), serverData.ServerID, "90", validate, modName: Game, custom: custom);
                Error = error;
                if (process != null && sharedLease != null)
                {
                    Functions.SharedInstallationStore.ReleaseLeaseWhenProcessExits(process, sharedLease);
                    sharedLease = null;
                }

                return process;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                Error = exception.Message;
                return null;
            }
            finally
            {
                sharedLease?.Dispose();
            }
        }

        public bool IsInstallValid()
        {
            return File.Exists(Path.Combine(GetBinaryRoot(), StartPath));
        }

        public bool IsImportValid(string path)
        {
            string hldsPath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(hldsPath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuildAtPath(GetBinaryRoot(), AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }

        private bool UsesSharedInstallation => !string.IsNullOrWhiteSpace(serverData.SharedInstallationId);

        private string GetBinaryRoot()
        {
            return Functions.SharedInstallationStore.GetServerBinaryRoot(serverData);
        }
    }
}
