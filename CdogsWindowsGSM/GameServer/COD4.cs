using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class COD4
    {
        private const string AppId = "7940";
        private const string Cod4xVersion = "21.2";
        private const string Cod4xDownloadUrl = "https://github.com/callofduty4x/CoD4x_Server/releases/download/21.2/cod4x18_dedrun.exe";
        private const string Cod4xSha256 = "15D10D334744FE355E09835D279FEE4ABDF7CAE86C160832297A74F516ACA1CA";
        private const long Cod4xSize = 6790029;
        private const string Cod4xExecutableName = "cod4x18_dedrun.exe";
        private const string VersionFileName = "cod4x_version.txt";
        private readonly ServerConfig _serverData;

        public const string FullName = "Call of Duty 4: Modern Warfare Dedicated Server (CoD4x)";
        public string StartPath = Cod4xExecutableName;
        public bool AllowsEmbedConsole = true;
        public bool loginAnonymous = false;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "28960";
        public string QueryPort = "28960";
        public string Defaultmap = "mp_killhouse";
        public string Maxplayers = "32";
        public string Additional = "+exec server.cfg +map_rotate";
        public string Error;
        public string Notice;

        public COD4(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            int port;
            int maxPlayers;
            if (!int.TryParse(_serverData.ServerPort, out port) || port < 1 || port > 65535)
            {
                Error = "Call of Duty 4 server port must be between 1 and 65535.";
                return;
            }
            if (!int.TryParse(_serverData.ServerMaxPlayer, out maxPlayers) || maxPlayers < 1 || maxPlayers > 64)
            {
                Error = "Call of Duty 4 max players must be between 1 and 64.";
                return;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string configPath = Path.Combine(serverFilesPath, "server.cfg");
            try
            {
                Directory.CreateDirectory(serverFilesPath);
                string config = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                config = SetServerSetting(config, "sv_hostname", QuoteConfigValue(_serverData.ServerName));
                config = SetServerSetting(config, "net_port", port.ToString());
                config = SetServerSetting(config, "sv_maxclients", maxPlayers.ToString());
                config = SetServerSetting(config, "sv_mapRotation", QuoteConfigValue("map " + (string.IsNullOrWhiteSpace(_serverData.ServerMap) ? Defaultmap : _serverData.ServerMap.Trim())));
                config = SetServerSetting(config, "bot_enable", "1");
                if (!string.IsNullOrWhiteSpace(_serverData.ServerGSLT))
                {
                    config = SetServerSetting(config, "sv_authtoken", QuoteConfigValue(_serverData.ServerGSLT.Trim()));
                }
                File.WriteAllText(configPath, config, new UTF8Encoding(false));
            }
            catch (IOException exception)
            {
                Error = "Failed to write the Call of Duty 4 server.cfg: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to write the Call of Duty 4 server.cfg: " + exception.Message;
            }
        }

        private static string SetServerSetting(string config, string name, string value)
        {
            string pattern = @"^[ \t]*(?:seta?[ \t]+)?" + Regex.Escape(name) + @"[ \t]+.*$";
            string line = "set " + name + " " + value;
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (regex.IsMatch(config))
            {
                return regex.Replace(config, match => line);
            }

            string existingConfig = (config ?? string.Empty).TrimEnd();
            return string.IsNullOrEmpty(existingConfig)
                ? line + Environment.NewLine
                : existingConfig + Environment.NewLine + line + Environment.NewLine;
        }

        private static string QuoteConfigValue(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private string BuildServerArguments()
        {
            var arguments = new StringBuilder("+set dedicated 2");
            if (!string.IsNullOrWhiteSpace(_serverData.ServerIP))
            {
                arguments.Append(" +set net_ip ").Append(QuoteConfigValue(_serverData.ServerIP));
            }
            arguments.Append(" +set net_port ").Append(_serverData.ServerPort);
            arguments.Append(" +set sv_maxclients ").Append(_serverData.ServerMaxPlayer);

            string serverParameters = _serverData.ServerParam ?? string.Empty;
            if (!Regex.IsMatch(serverParameters, @"(?:^|\s)\+exec\s+(?:""server\.cfg""|'server\.cfg'|server\.cfg)(?:\s|$)", RegexOptions.IgnoreCase))
            {
                serverParameters = (serverParameters.Trim() + " +exec server.cfg").Trim();
            }
            if (!Regex.IsMatch(serverParameters, @"(?:^|\s)\+map_rotate(?:\s|$)", RegexOptions.IgnoreCase))
            {
                serverParameters = (serverParameters.Trim() + " +map_rotate").Trim();
            }
            if (!string.IsNullOrWhiteSpace(serverParameters))
            {
                arguments.Append(' ').Append(serverParameters);
            }

            return arguments.ToString();
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!IsInstallValid())
            {
                return Task.FromResult<Process>(null);
            }

            CreateServerCFG();
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return Task.FromResult<Process>(null);
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = executablePath,
                    Arguments = BuildServerArguments(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = AllowsEmbedConsole,
                    RedirectStandardInput = AllowsEmbedConsole,
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

            bool processStarted = false;
            try
            {
                processStarted = process.Start();
                if (!processStarted)
                {
                    Error = "CoD4x did not start a server process.";
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
                Error = "Failed to start CoD4x: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start CoD4x: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start CoD4x: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start CoD4x: " + exception.Message;
            }

            if (processStarted && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
            process.Dispose();
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process process)
        {
            Error = string.Empty;
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                if (process.StartInfo.RedirectStandardInput)
                {
                    process.StandardInput.WriteLine("quit");
                    process.StandardInput.Flush();
                }
                else
                {
                    process.CloseMainWindow();
                }
            }
            catch (IOException exception)
            {
                Error = "Failed to send the CoD4x shutdown command: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to send the CoD4x shutdown command: " + exception.Message;
            }

            await Task.Run(() =>
            {
                if (!process.HasExited && !process.WaitForExit(10000))
                {
                    process.Kill();
                    process.WaitForExit();
                }
            });
        }

        public async Task<Process> Install()
        {
            await DownloadAndInstallCoD4x();
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return null;
            }

            var steamCmd = new Installer.SteamCMD();
            Process process = await steamCmd.Install(_serverData.ServerID, string.Empty, AppId, true, loginAnonymous: false);
            Error = steamCmd.Error;
            return process;
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            await DownloadAndInstallCoD4x();
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return null;
            }

            var (process, error) = await Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate, loginAnonymous: false, custom: custom, embedConsole: false);
            Error = error;
            return process;
        }

        private async Task DownloadAndInstallCoD4x()
        {
            Error = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__cod4x_stage_" + Guid.NewGuid().ToString("N"));
            string stagedExecutablePath = Path.Combine(stagingPath, Cod4xExecutableName);
            string executablePath = Path.Combine(serverFilesPath, Cod4xExecutableName);

            try
            {
                Directory.CreateDirectory(stagingPath);
                using (var webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
                    await webClient.DownloadFileTaskAsync(new Uri(Cod4xDownloadUrl), stagedExecutablePath);
                }

                var fileInfo = new FileInfo(stagedExecutablePath);
                if (fileInfo.Length != Cod4xSize || !VerifySha256(stagedExecutablePath, Cod4xSha256))
                {
                    throw new InvalidDataException("The CoD4x 21.2 Windows binary failed size or SHA-256 verification.");
                }

                ReplaceFile(stagedExecutablePath, executablePath);
                File.WriteAllText(Path.Combine(serverFilesPath, VersionFileName), Cod4xVersion, new UTF8Encoding(false));
            }
            catch (WebException exception)
            {
                Error = "Failed to download CoD4x: " + exception.Message;
            }
            catch (InvalidDataException exception)
            {
                Error = exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to install CoD4x: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to install CoD4x: " + exception.Message;
            }
            catch (CryptographicException exception)
            {
                Error = "Failed to verify CoD4x: " + exception.Message;
            }
            finally
            {
                if (Directory.Exists(stagingPath))
                {
                    try
                    {
                        Directory.Delete(stagingPath, true);
                    }
                    catch (IOException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary CoD4x files: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary CoD4x files: " + exception.Message);
                    }
                }
            }
        }

        private static bool VerifySha256(string filePath, string expectedSha256)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                string actualSha256 = BitConverter.ToString(hash).Replace("-", string.Empty);
                return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(sourcePath, destinationPath, null);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }

        private static string AppendError(string currentError, string message)
        {
            return string.IsNullOrWhiteSpace(currentError) ? message : currentError + Environment.NewLine + message;
        }

        public bool IsInstallValid()
        {
            return ValidateInstall(ServerPath.GetServersServerFiles(_serverData.ServerID));
        }

        public bool IsImportValid(string path)
        {
            return ValidateInstall(path);
        }

        private bool ValidateInstall(string serverFilesPath)
        {
            Error = string.Empty;
            string executablePath = Path.Combine(serverFilesPath, Cod4xExecutableName);
            if (!File.Exists(executablePath))
            {
                Error = $"CoD4x server executable is missing ({executablePath}).";
                return false;
            }

            string mainPath = Path.Combine(serverFilesPath, "main");
            if (!Directory.Exists(mainPath) || Directory.GetFiles(mainPath, "*.iwd", SearchOption.TopDirectoryOnly).Length == 0)
            {
                Error = "Licensed Call of Duty 4 game data is missing from the main directory. Install AppID 7940 with an account that owns the game.";
                return false;
            }

            string zonePath = Path.Combine(serverFilesPath, "zone");
            if (!Directory.Exists(zonePath) || Directory.GetFiles(zonePath, "*.ff", SearchOption.AllDirectories).Length == 0)
            {
                Error = "Licensed Call of Duty 4 zone data is missing. Install AppID 7940 with an account that owns the game.";
                return false;
            }

            return true;
        }

        public string GetLocalBuild()
        {
            string versionPath = ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName);
            try
            {
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath).Trim();
                }
                return IsInstallValid() ? "unknown" : string.Empty;
            }
            catch (IOException exception)
            {
                Error = "Failed to read the local CoD4x version: " + exception.Message;
                return string.Empty;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to read the local CoD4x version: " + exception.Message;
                return string.Empty;
            }
        }

        public Task<string> GetRemoteBuild()
        {
            return Task.FromResult(Cod4xVersion);
        }
    }
}
