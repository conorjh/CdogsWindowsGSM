using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class GTA5
    {
        private const string ServerDownloadPage = "https://docs.fivem.net/docs/server-download/?platform=legacy&os=windows";
        private const string ServerDataArchiveUrl = "https://github.com/citizenfx/cfx-server-data/archive/refs/heads/master.zip";
        private readonly Functions.ServerConfig _serverData;

        private sealed class FiveMArtifact
        {
            public string Build { get; set; }
            public Uri DownloadUrl { get; set; }
        }

        public string Error;
        public string Notice;

        public const string FullName = "Grand Theft Auto V Dedicated Server (FiveM)";
        public string StartPath = @"server\FXServer.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.FIVEM();

        public string Port = "30120";
        public string QueryPort = "30120";
        public string Defaultmap = "fivem-map-skater";
        public string Maxplayers = "32";
        public string Additional = "+exec server.cfg";

        public GTA5(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async Task CreateServerCFG()
        {
            Error = string.Empty;
            Notice = string.Empty;
            string serverDataPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "cfx-server-data-master");
            string stagingPath = Path.Combine(serverDataPath, "__windowsgsm_config_" + Guid.NewGuid().ToString("N"));
            string stagedConfigPath = Path.Combine(stagingPath, "server.cfg");
            string stagedLogoPath = Path.Combine(stagingPath, "myLogo.png");
            string configPath = Path.Combine(serverDataPath, "server.cfg");

            try
            {
                Directory.CreateDirectory(stagingPath);
                if (!await Functions.Github.DownloadGameServerConfig(stagedConfigPath, FullName))
                {
                    Error = "Failed to download the FiveM server.cfg template.";
                    return;
                }

                string maxPlayers = string.IsNullOrWhiteSpace(_serverData.ServerMaxPlayer) ? Maxplayers : _serverData.ServerMaxPlayer;
                string configText = File.ReadAllText(stagedConfigPath);
                configText = configText.Replace("{{hostname}}", EscapeConfigValue(_serverData.ServerName));
                configText = configText.Replace("{{rcon_password}}", EscapeConfigValue(_serverData.GetRCONPassword()));
                configText = configText.Replace("{{ip}}", EscapeConfigValue(_serverData.GetIPAddress()));
                configText = configText.Replace("{{port}}", EscapeConfigValue(_serverData.ServerPort));
                configText = configText.Replace("{{maxplayers}}", EscapeConfigValue(maxPlayers));
                File.WriteAllText(stagedConfigPath, configText, new UTF8Encoding(false));
                ReplaceFile(stagedConfigPath, configPath);

                string logoPath = Path.Combine(serverDataPath, "myLogo.png");
                if (await Functions.Github.DownloadGameServerConfig(stagedLogoPath, FullName))
                {
                    ReplaceFile(stagedLogoPath, logoPath);
                }
                else
                {
                    Notice = "The sample FiveM server logo could not be downloaded.";
                }
            }
            catch (IOException exception)
            {
                Error = "Failed to create the FiveM server configuration: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to create the FiveM server configuration: " + exception.Message;
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
                        Notice = "Could not remove temporary FiveM config files: " + exception.Message;
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Notice = "Could not remove temporary FiveM config files: " + exception.Message;
                    }
                }
            }
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            string fxServerPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"server\FXServer.exe");
            if (!File.Exists(fxServerPath))
            {
                Error = $"FXServer.exe not found ({fxServerPath})";
                return Task.FromResult<Process>(null);
            }

            string citizenPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"server\citizen");
            if (!Directory.Exists(citizenPath))
            {
                Error = $"Directory citizen not found ({citizenPath})";
                return Task.FromResult<Process>(null);
            }

            string serverDataPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "cfx-server-data-master");
            if (!Directory.Exists(serverDataPath))
            {
                Error = $"Directory cfx-server-data-master not found ({serverDataPath})";
                return Task.FromResult<Process>(null);
            }

            string configPath = Path.Combine(serverDataPath, "server.cfg");
            if (!File.Exists(configPath))
            {
                Error = $"server.cfg not found ({configPath})";
                return Task.FromResult<Process>(null);
            }

            string serverParameters = _serverData.ServerParam ?? string.Empty;
            if (!Regex.IsMatch(serverParameters, @"(?:^|\s)\+exec\s+(?:""server\.cfg""|'server\.cfg'|server\.cfg)(?:\s|$)", RegexOptions.IgnoreCase))
            {
                serverParameters = (serverParameters.Trim() + " +exec server.cfg").Trim();
            }

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverDataPath,
                    FileName = fxServerPath,
                    Arguments = $"+set citizen_dir \"{citizenPath}\" {serverParameters}",
                    WindowStyle = ProcessWindowStyle.Minimized,
                    CreateNoWindow = AllowsEmbedConsole,
                    UseShellExecute = false,
                    RedirectStandardInput = AllowsEmbedConsole,
                    RedirectStandardOutput = AllowsEmbedConsole,
                    RedirectStandardError = AllowsEmbedConsole
                },
                EnableRaisingEvents = true
            };

            if (AllowsEmbedConsole)
            {
                var serverConsole = new Functions.ServerConsole(_serverData.ServerID);
                process.OutputDataReceived += serverConsole.AddOutput;
                process.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                if (!process.Start())
                {
                    Error = "FXServer did not start a process.";
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
                Error = "Failed to start FXServer: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start FXServer: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start FXServer: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start FXServer: " + exception.Message;
            }

            process.Dispose();
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process p)
        {
            Error = string.Empty;
            if (p == null || p.HasExited) { return; }

            try
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    p.StandardInput.WriteLine("quit");
                }
                else
                {
                    Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "quit");
                }
            }
            catch (IOException exception)
            {
                Error = "Failed to send the FiveM shutdown command: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to send the FiveM shutdown command: " + exception.Message;
            }

            await Task.Run(() =>
            {
                if (!p.HasExited && !p.WaitForExit(10000))
                {
                    p.Kill();
                    p.WaitForExit();
                }
            });
        }

        public Task<Process> Install()
        {
            return InstallOrUpdate(true);
        }

        public Task<Process> Update()
        {
            return InstallOrUpdate(false);
        }

        public bool IsInstallValid()
        {
            return ValidateInstall(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID));
        }

        public bool IsImportValid(string path)
        {
            return ValidateInstall(path);
        }

        public string GetLocalBuild()
        {
            Error = string.Empty;
            string versionPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "FiveM-version.txt");
            try
            {
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath).Trim();
                }
                string executablePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
                return File.Exists(executablePath) ? "unknown" : string.Empty;
            }
            catch (IOException exception)
            {
                Error = "Failed to read the local FiveM build: " + exception.Message;
                return string.Empty;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to read the local FiveM build: " + exception.Message;
                return string.Empty;
            }
        }

        public async Task<string> GetRemoteBuild()
        {
            Error = string.Empty;
            try
            {
                using (WebClient webClient = CreateWebClient())
                {
                    return (await GetLatestArtifact(webClient)).Build;
                }
            }
            catch (WebException exception)
            {
                Error = "Failed to get the latest FiveM build: " + exception.Message;
            }
            catch (JsonException exception)
            {
                Error = "Failed to get the latest FiveM build: " + exception.Message;
            }
            catch (InvalidDataException exception)
            {
                Error = "Failed to get the latest FiveM build: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to get the latest FiveM build: " + exception.Message;
            }
            return string.Empty;
        }

        private static WebClient CreateWebClient()
        {
            var webClient = new WebClient { Encoding = Encoding.UTF8 };
            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            return webClient;
        }

        private static async Task<FiveMArtifact> GetLatestArtifact(WebClient webClient)
        {
            string html = await webClient.DownloadStringTaskAsync(ServerDownloadPage);
            Match dataMatch = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json"">(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!dataMatch.Success)
            {
                throw new InvalidDataException("The Cfx server download page did not include its build metadata.");
            }

            JObject page = JObject.Parse(dataMatch.Groups[1].Value);
            JObject windowsBuild = page["props"]?["pageProps"]?["legacy"]?["recommended"]?["windows"] as JObject;
            if (windowsBuild == null)
            {
                throw new InvalidDataException("Cfx did not publish a recommended legacy Windows server artifact.");
            }

            string assetName = (string)windowsBuild["displayName"];
            string downloadUrl = (string)windowsBuild["downloadURL"];
            Uri uri;
            if (!string.Equals(assetName, "server.7z", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !string.Equals(uri.Host, "runtime.fivem.net", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Cfx returned an invalid recommended Windows server artifact URL.");
            }

            Match buildMatch = Regex.Match(uri.AbsolutePath,
                @"^/artifacts/fivem/build_server_windows/master/(?<build>\d{4,}-[a-f0-9]{40})/server\.7z$",
                RegexOptions.IgnoreCase);
            if (!buildMatch.Success)
            {
                throw new InvalidDataException("Cfx returned an unrecognized Windows server build path.");
            }

            return new FiveMArtifact { Build = buildMatch.Groups["build"].Value, DownloadUrl = uri };
        }

        private async Task<Process> InstallOrUpdate(bool installing)
        {
            Error = string.Empty;
            Notice = string.Empty;
            string serverFilesPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__fivem_stage_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stagingPath);
                string stagedServerPath = Path.Combine(stagingPath, "server");
                Directory.CreateDirectory(stagedServerPath);

                using (WebClient webClient = CreateWebClient())
                {
                    FiveMArtifact artifact = await GetLatestArtifact(webClient);
                    string artifactPath = Path.Combine(stagingPath, "server.7z");
                    await webClient.DownloadFileTaskAsync(artifact.DownloadUrl, artifactPath);
                    if (!await Functions.FileManagement.ExtractArchive(artifactPath, stagedServerPath))
                    {
                        throw new InvalidDataException("Could not safely extract the FiveM 7-Zip server artifact.");
                    }
                    if (!IsValidRuntime(stagedServerPath))
                    {
                        throw new InvalidDataException("The FiveM artifact did not contain FXServer.exe and its citizen directory.");
                    }

                    if (installing)
                    {
                        string dataArchivePath = Path.Combine(stagingPath, "cfx-server-data.zip");
                        string extractedDataPath = Path.Combine(stagingPath, "data");
                        await webClient.DownloadFileTaskAsync(ServerDataArchiveUrl, dataArchivePath);
                        if (!await Functions.FileManagement.ExtractArchive(dataArchivePath, extractedDataPath))
                        {
                            throw new InvalidDataException("Could not safely extract the Cfx server-data archive.");
                        }
                        string stagedDataPath = Path.Combine(extractedDataPath, "cfx-server-data-master");
                        if (!Directory.Exists(stagedDataPath) || !File.Exists(Path.Combine(stagedDataPath, "server.cfg")))
                        {
                            throw new InvalidDataException("The Cfx server-data archive did not contain server.cfg.");
                        }

                        string serverPath = Path.Combine(serverFilesPath, "server");
                        string serverDataPath = Path.Combine(serverFilesPath, "cfx-server-data-master");
                        if (Directory.Exists(serverPath) || Directory.Exists(serverDataPath))
                        {
                            throw new InvalidDataException("FiveM files already exist. Remove or import the existing server before installing.");
                        }

                        Directory.Move(stagedServerPath, serverPath);
                        try
                        {
                            Directory.Move(stagedDataPath, serverDataPath);
                        }
                        catch (IOException)
                        {
                            if (Directory.Exists(serverPath))
                            {
                                Directory.Delete(serverPath, true);
                            }
                            throw;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            if (Directory.Exists(serverPath))
                            {
                                Directory.Delete(serverPath, true);
                            }
                            throw;
                        }

                        File.WriteAllText(Path.Combine(serverFilesPath, "FiveM-version.txt"), artifact.Build, new UTF8Encoding(false));
                    }
                    else
                    {
                        string serverPath = Path.Combine(serverFilesPath, "server");
                        ReplaceDirectory(stagedServerPath, serverPath);
                        File.WriteAllText(Path.Combine(serverFilesPath, "FiveM-version.txt"), artifact.Build, new UTF8Encoding(false));
                    }
                }
            }
            catch (WebException exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} FiveM: {exception.Message}";
            }
            catch (JsonException exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} FiveM: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} FiveM: {exception.Message}";
            }
            catch (IOException exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} FiveM: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} FiveM: {exception.Message}";
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
                        Error = AppendError(Error, "Could not remove temporary FiveM files: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary FiveM files: " + exception.Message);
                    }
                }
            }

            return null;
        }

        private static bool IsValidRuntime(string serverPath)
        {
            return File.Exists(Path.Combine(serverPath, "FXServer.exe")) &&
                Directory.Exists(Path.Combine(serverPath, "citizen"));
        }

        private bool ValidateInstall(string serverFilesPath)
        {
            Error = string.Empty;
            string serverPath = Path.Combine(serverFilesPath, "server");
            string serverDataPath = Path.Combine(serverFilesPath, "cfx-server-data-master");
            if (!IsValidRuntime(serverPath))
            {
                Error = $"Invalid FiveM installation: FXServer.exe or citizen is missing from ({serverPath}).";
                return false;
            }
            if (!File.Exists(Path.Combine(serverDataPath, "server.cfg")))
            {
                Error = $"Invalid FiveM installation: server.cfg is missing from ({serverDataPath}).";
                return false;
            }
            return true;
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

        private static void ReplaceDirectory(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(destinationPath))
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }

            string backupPath = destinationPath + ".backup-" + Guid.NewGuid().ToString("N");
            Directory.Move(destinationPath, backupPath);
            try
            {
                Directory.Move(sourcePath, destinationPath);
            }
            catch (IOException)
            {
                RestoreDirectory(destinationPath, backupPath);
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                RestoreDirectory(destinationPath, backupPath);
                throw;
            }
            Directory.Delete(backupPath, true);
        }

        private static void RestoreDirectory(string destinationPath, string backupPath)
        {
            if (!Directory.Exists(destinationPath) && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, destinationPath);
            }
        }

        private static string EscapeConfigValue(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static string AppendError(string currentError, string message)
        {
            return string.IsNullOrWhiteSpace(currentError) ? message : currentError + Environment.NewLine + message;
        }
    }
}
