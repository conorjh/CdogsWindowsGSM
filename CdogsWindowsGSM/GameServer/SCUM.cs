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

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!IsInstallValid())
            {
                return Task.FromResult<Process>(null);
            }

            if (!int.TryParse(_serverData.ServerPort, out int port) || port < 1 || port > 65535)
            {
                Error = "SCUM server port must be between 1 and 65535.";
                return Task.FromResult<Process>(null);
            }
            if (!int.TryParse(_serverData.ServerMaxPlayer, out int maxPlayers) || maxPlayers < 1)
            {
                Error = "SCUM max players must be a positive number.";
                return Task.FromResult<Process>(null);
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            string serverParameters = string.IsNullOrWhiteSpace(_serverData.ServerParam)
                ? Additional
                : _serverData.ServerParam.Trim();
            string arguments = $"{serverParameters} -port={port} -MaxPlayers={maxPlayers}";

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = executablePath,
                    Arguments = arguments,
                    WindowStyle = AllowsEmbedConsole ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Minimized,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = AllowsEmbedConsole,
                    RedirectStandardError = AllowsEmbedConsole
                },
                EnableRaisingEvents = true
            };

            if (AllowsEmbedConsole)
            {
                var serverConsole = new ServerConsole(_serverData.ServerID);
                process.OutputDataReceived += serverConsole.AddOutput;
                process.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                if (!process.Start())
                {
                    Error = "SCUM did not start a server process.";
                    process.Dispose();
                    return Task.FromResult<Process>(null);
                }

                if (AllowsEmbedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                return Task.FromResult(process);
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
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process process)
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (process == null || process.HasExited)
            {
                return;
            }

            IntPtr windowHandle = process.MainWindowHandle;
            if (windowHandle != IntPtr.Zero)
            {
                ServerConsole.SetMainWindow(windowHandle);
                ServerConsole.SendWaitToMainWindow("^c");
            }
            else
            {
                process.CloseMainWindow();
            }

            bool exited = await Task.Run(() => process.HasExited || process.WaitForExit(5000));
            if (!exited)
            {
                process.Kill();
                await Task.Run(() => process.WaitForExit());
                Notice = "SCUM did not stop after Ctrl+C and was terminated.";
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
