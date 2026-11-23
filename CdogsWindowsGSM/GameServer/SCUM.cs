using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.GameServer
{
    class SCUM : SteamCMDAgent
    {
        public const string FullName = "SCUM dedicated server";
        public override bool loginAnonymous { get; set; } = true;
        public override string AppId { get; set; } = "3792580";
        public override string StartPath { get; set; } = @"SCUM\Binaries\Win64\SCUMServer.exe";

        private readonly ServerConfig _serverData;

        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 2;
        public object QueryMethod = new A2S();
        public string Port = "10000";
        public string QueryPort = "10000";
        public string Defaultmap = "Dedicated";
        public string Maxplayers = "32";
        public string Additional = "-log";

        public SCUM(ServerConfig serverData) : base(serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
        }

        public async Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!IsInstallValid())
            {
                return null;
            }

            if (!int.TryParse(_serverData.ServerPort, out int port) || port < 1 || port > 65535)
            {
                Error = "SCUM server port must be between 1 and 65535.";
                return null;
            }
            if (!int.TryParse(_serverData.ServerMaxPlayer, out int maxPlayers) || maxPlayers < 1)
            {
                Error = "SCUM max players must be a positive number.";
                return null;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            string serverParameters = string.IsNullOrWhiteSpace(_serverData.ServerParam)
                ? Additional
                : _serverData.ServerParam.Trim();
            string arguments = $"{serverParameters} -port={port} -MaxPlayers={maxPlayers}";

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = serverFilesPath,
                FileName = executablePath,
                Arguments = arguments,
                WindowStyle = AllowsEmbedConsole ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Minimized,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = AllowsEmbedConsole,
                RedirectStandardError = AllowsEmbedConsole
            };
            startInfo.EnvironmentVariables["PATH"] = ProcessEnvironment.PrependPathDirectories(
                serverFilesPath,
                Environment.GetEnvironmentVariable("PATH"),
                @"Engine\Binaries\ThirdParty\PhysX\Win64\VS2015\release",
                @"Engine\Binaries\ThirdParty\NVIDIA\NVaftermath\Win64",
                @"Engine\Binaries\ThirdParty\Ogg\Win64\VS2015",
                @"Engine\Binaries\ThirdParty\Vorbis\Win64\VS2015");

            Process process = null;
            try
            {
                await LegacyDirectXRuntime.EnsureInstalledAsync(serverFilesPath);
                process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                if (AllowsEmbedConsole)
                {
                    var serverConsole = new ServerConsole(_serverData.ServerID);
                    process.OutputDataReceived += serverConsole.AddOutput;
                    process.ErrorDataReceived += serverConsole.AddOutput;
                }

                if (!process.Start())
                {
                    Error = "SCUM did not start a server process.";
            if (process != null)
            {
                process.Dispose();
            }
                    return null;
                }

                if (AllowsEmbedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                return process;
            }
            catch (Win32Exception exception)
            {
                Error = "Failed to start SCUM: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start SCUM: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start SCUM: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start SCUM: " + exception.Message;
            }

            process.Dispose();
            return null;
        }

        public async Task Stop(Process process)
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (process == null || process.HasExited)
            {
                return;
            }

            bool hasUsableWindow = ServerConsole.HasUsableWindow(process, process.MainWindowHandle);
            if (hasUsableWindow)
            {
                try
                {
                    process.CloseMainWindow();
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    return;
                }
            }

            bool exited = hasUsableWindow && await Task.Run(() => process.HasExited || process.WaitForExit(5000));
            if (!exited && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    return;
                }

                await process.WaitForExitAsync();
                Notice = "SCUM did not stop gracefully and was terminated.";
            }
        }

        public new bool IsInstallValid()
        {
            return ValidateInstall(ServerPath.GetServersServerFiles(_serverData.ServerID));
        }

        public new bool IsImportValid(string path)
        {
            return ValidateInstall(path);
        }

        private bool ValidateInstall(string serverFilesPath)
        {
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (File.Exists(executablePath))
            {
                Error = string.Empty;
                return true;
            }

            Error = $"SCUM server executable was not found ({executablePath}).";
            return false;
        }
    }
}
