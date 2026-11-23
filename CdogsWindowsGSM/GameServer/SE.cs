using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace WindowsGSM.GameServer
{
    /// <summary>
    /// 
    /// Note:
    /// I personally don't play Space engineers and I have no experience on this game, even on the game server.
    /// If anyone is the specialist or having a experience on Space Engineers server. Feel feel to edit this and pull request in Github.
    /// 
    /// </summary>
    class SE
    {
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Space Engineers Dedicated Server";
        public string StartPath = @"DedicatedServer64\SpaceEngineersDedicated.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 2;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "27016";
        public string QueryPort = "27017";
        public string Defaultmap = "world";
        public string Maxplayers = "4";
        public string Additional = string.Empty;

        public string AppId = "298740";

        public SE(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG()
        {
            Error = string.Empty;
            //Download SpaceEngineersDedicated-WindowsGSM.bat
            string batPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"DedicatedServer64\SpaceEngineersDedicated-WindowsGSM.bat");
            Functions.GameServerConfigDownloadResult batchResult = await Functions.Github.DownloadGameServerConfigAsync(
                batPath,
                _serverData.ServerGame);
            if (!batchResult.Succeeded)
            {
                Error = $"Could not download the Space Engineers startup script: {batchResult.ErrorMessage}";
                return;
            }

            //Download world and extract
            string zipPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"AppData\Roaming\SpaceEngineersDedicated\Saves\world.zip");
            Functions.GameServerConfigDownloadResult worldResult = await Functions.Github.DownloadGameServerConfigAsync(
                zipPath,
                _serverData.ServerGame,
                replaceExisting: true);
            if (!worldResult.Succeeded)
            {
                Error = $"Could not download the Space Engineers world archive: {worldResult.ErrorMessage}";
                return;
            }

            try
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, Path.GetDirectoryName(zipPath)));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidDataException ||
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException ||
                exception is ArgumentException)
            {
                Error = $"Failed to extract the Space Engineers world archive: {exception.Message}";
                return;
            }

            try
            {
                File.Delete(zipPath);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException)
            {
                Error = $"The Space Engineers world was extracted, but its archive could not be removed: {exception.Message}";
                return;
            }

            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"AppData\Roaming\SpaceEngineersDedicated", "SpaceEngineers-Dedicated.cfg");
            Functions.GameServerConfigDownloadResult configResult = await Functions.Github.DownloadGameServerConfigAsync(
                configPath,
                _serverData.ServerGame);
            if (!configResult.Succeeded)
            {
                Error = $"Could not download the Space Engineers server config: {configResult.ErrorMessage}";
                return;
            }

            if (!configResult.ExistingFilePreserved)
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{maxplayers}}", _serverData.ServerMaxPlayer);
                configText = configText.Replace("{{LoadWorld}}", Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"AppData\Roaming\SpaceEngineersDedicated\Saves", Defaultmap));
                configText = configText.Replace("{{ip}}", _serverData.ServerIP);
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                configText = configText.Replace("{{ServerName}}", _serverData.ServerName);
                configText = configText.Replace("{{WorldName}}", Defaultmap);
                File.WriteAllText(configPath, configText);
            }
        }

        public async Task<Process> Start()
        {
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"AppData\Roaming\SpaceEngineersDedicated", "SpaceEngineers-Dedicated.cfg");
            if (!File.Exists(configPath))
            {
                Notice = $"{Path.GetFileName(configPath)} not found ({configPath})";
            }

            string param = (!AllowsEmbedConsole ? "-console" : "-noconsole") + " -ignorelastsession";
            param += string.IsNullOrWhiteSpace(_serverData.ServerIP) ? string.Empty : $" -ip {_serverData.ServerIP}";
            param += string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $" -port {_serverData.ServerPort}";
            param += $" {_serverData.ServerParam}";

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = Path.GetDirectoryName(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath)),
                        FileName = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath),
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };
                p.StartInfo.EnvironmentVariables["USERPROFILE"] = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
                p.Start();
            }
            else
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = Path.GetDirectoryName(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath)),
                        FileName = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath),
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                p.StartInfo.EnvironmentVariables["USERPROFILE"] = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
                var serverConsole = new Functions.ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            
            return p;
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p.StartInfo.RedirectStandardOutput)
                {
                    /*  Base on https://www.spaceengineersgame.com/dedicated-servers.html
                     * 
                     *  C:\WINDOWS\system32 > TASKKILL /pid 26500
                     *  SUCCESS: Sent termination signal to the process with PID 26500.
                     * 
                     *  But the process still exist... Therefore, p.Kill(); is used
                     * 

                    Process taskkill = new Process
                    {
                        StartInfo =
                        {
                            FileName = "TASKKILL",
                            Arguments = $"/PID {p.Id}",
                            Verb = "runas",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    taskkill.Start();
                    */

                    p.Kill();
                }
                else
                {
                    Functions.ServerConsole.SetMainWindow(p.MainWindowHandle);
                    Functions.ServerConsole.SendWaitToMainWindow("^(c)");
                    Functions.ServerConsole.SendWaitToMainWindow("^(c)");
                }
            });
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId);
            Error = steamCMD.Error;

            return p;
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate, custom: custom);
            Error = error;
            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string importPath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {Path.GetFileName(StartPath)}";
            return File.Exists(importPath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}
