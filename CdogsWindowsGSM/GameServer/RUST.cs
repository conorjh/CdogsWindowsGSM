using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace WindowsGSM.GameServer
{
    /// <summary>
    ///
    /// Notes:
    /// Both RedirectStandardInput or RedirectStandardOutput cannot use on WindowsGSM.
    /// RedirectStandardOutput is possible but it will break the input, if used both, the server can run successfully but the input become useless again.
    ///
    /// The solution for this is don't use neither RedirectStandardInput nor RedirectStandardOutput.
    /// Just use the traditional method to handle the server.
    ///
    /// </summary>
    class RUST : Engine.ISharedInstallationProvider
    {
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Rust Dedicated Server";
        public string StartPath = "RustDedicated.exe";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "28015";
        public string QueryPort = "28016";
        public string Defaultmap = "Procedural Map";
        public string Maxplayers = "50";
        public string Additional = string.Empty;

        public string AppId = "258550";
        public string SharedInstallationAppId => AppId;
        public string SharedInstallationExecutableName => StartPath;

        public RUST(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG()
        {
            await InstallConfigOnly();
        }

        public async Task<bool> InstallConfigOnly()
        {
            Error = string.Empty;
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.cfg");
            try
            {
                if (UsesSharedInstallation)
                {
                    Functions.SharedInstallationStore.EnsureReady(_serverData.SharedInstallationId);
                }

                Directory.CreateDirectory(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID));
                if (File.Exists(configPath))
                {
                    return true;
                }

                if (!await Functions.Github.DownloadGameServerConfig(configPath, FullName))
                {
                    Error = "Could not download Rust server.cfg.";
                    return false;
                }

                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{rcon_password}}", _serverData.GetRCONPassword());
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                File.WriteAllText(configPath, configText);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Error = $"Failed to create Rust server.cfg: {exception.Message}";
                return false;
            }
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            FileStream sharedLease = null;
            try
            {
                string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.cfg");
                if (!File.Exists(configPath))
                {
                    Error = $"server.cfg not found ({configPath})";
                    Notice = Error;
                    return Task.FromResult<Process>(null);
                }

                if (UsesSharedInstallation)
                {
                    Functions.SharedInstallationStore.EnsureReady(_serverData.SharedInstallationId);
                    sharedLease = Functions.SharedInstallationStore.AcquireReadLease(_serverData.SharedInstallationId);
                }

                string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
                string srcdsPath = Path.Combine(GetBinaryRoot(), StartPath);
                if (!File.Exists(srcdsPath))
                {
                    Error = $"{StartPath} not found ({srcdsPath})";
                    sharedLease?.Dispose();
                    return Task.FromResult<Process>(null);
                }

                string param = "-nographics -batchmode -silent-crashes";
                param += $" {_serverData.ServerParam}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerName) ? string.Empty : $" +server.hostname \"{_serverData.ServerName}\"";
                param += string.IsNullOrWhiteSpace(_serverData.ServerIP) ? string.Empty : $" +server.ip {_serverData.ServerIP}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $" +server.port {_serverData.ServerPort}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerQueryPort) ? string.Empty : $" +server.queryport {_serverData.ServerQueryPort}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerMap) ? string.Empty : $" +server.level \"{_serverData.ServerMap}\"";
                param += string.IsNullOrWhiteSpace(_serverData.ServerMaxPlayer) ? string.Empty : $" +server.maxplayers {_serverData.ServerMaxPlayer}";

                foreach (string line in File.ReadLines(configPath))
                {
                    param += $" {line}";
                }

                Process process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir,
                        FileName = srcdsPath,
                        Arguments = param.TrimEnd()
                    },
                    EnableRaisingEvents = true
                };
                if (!process.Start())
                {
                    Error = "RustDedicated.exe did not start.";
                    sharedLease?.Dispose();
                    return Task.FromResult<Process>(null);
                }

                if (sharedLease != null)
                {
                    Functions.SharedInstallationStore.ReleaseLeaseWhenProcessExits(process, sharedLease);
                    sharedLease = null;
                }

                return Task.FromResult(process);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                sharedLease?.Dispose();
                Error = exception.Message;
                return Task.FromResult<Process>(null);
            }
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "quit");
            });
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId);
            Error = steamCMD.Error;

            return p;
        }

        public async Task<Process> InstallSharedBinaries()
        {
            try
            {
                var steamCMD = new Installer.SteamCMD();
                Process process = await steamCMD.InstallToPath(GetBinaryRoot(), string.Empty, AppId);
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
                string binaryRoot = GetBinaryRoot();
                if (UsesSharedInstallation)
                {
                    Functions.SharedInstallationStore.EnsureReady(_serverData.SharedInstallationId);
                    sharedLease = Functions.SharedInstallationStore.AcquireExclusiveLease(_serverData.SharedInstallationId);
                }

                var (process, error) = await Installer.SteamCMD.UpdateExAtPath(binaryRoot, _serverData.ServerID, AppId, validate, custom: custom);
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
            try
            {
                return File.Exists(Path.Combine(GetBinaryRoot(), StartPath));
            }
            catch (ArgumentException exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        public bool IsImportValid(string path)
        {
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(Path.Combine(path, StartPath));
        }

        public string GetLocalBuild()
        {
            try
            {
                var steamCMD = new Installer.SteamCMD();
                string build = steamCMD.GetLocalBuildAtPath(GetBinaryRoot(), AppId);
                Error = steamCMD.Error;
                return build;
            }
            catch (ArgumentException exception)
            {
                Error = exception.Message;
                return string.Empty;
            }
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }

        private bool UsesSharedInstallation => !string.IsNullOrWhiteSpace(_serverData.SharedInstallationId);

        private string GetBinaryRoot()
        {
            return UsesSharedInstallation
                ? Functions.ServerPath.GetSharedInstallationServerFiles(_serverData.SharedInstallationId)
                : Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
        }
    }
}
