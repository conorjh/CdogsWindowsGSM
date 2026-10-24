using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Security;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;
using WindowsGSM.Localization;

namespace WindowsGSM.GameServer
{
    class MCBE : Engine.ISharedInstallationProvider
    {
        internal class MCBEWebclient : WebClient
        {
            public MCBEWebclient()
            {
                Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                HttpWebRequest req = (HttpWebRequest)base.GetWebRequest(address);
                // WWW server only responds to Compression
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                // Also needs a Accept header.
                req.Accept = "*/*";
                return req;
            }
        }

        private sealed class BedrockServerDownload
        {
            public Uri Url { get; set; }
            public string FileName { get; set; }
            public string Version { get; set; }
        }

        private const string BedrockDownloadLinksUrl = "https://net-secondary.web.minecraft-services.net/api/v1.0/download/links";
        private const string BedrockWindowsDownloadType = "serverBedrockWindows";
        private static readonly string[] BedrockRuntimeDirectories =
        {
            "behavior_packs",
            "definitions",
            "resource_packs",
            "structures"
        };
        internal static readonly string[] BedrockInstanceConfigurationFiles =
        {
            "allowlist.json",
            "permissions.json"
        };
        private static readonly Regex BedrockArchiveNameRegex = new Regex(
            @"^bedrock-server-(?<version>\d+(?:\.\d+)*)\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Minecraft: Bedrock Edition Server";
        public string StartPath = "bedrock_server.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 2;
        public dynamic QueryMethod = null;

        public string Port = "19132";
        public string QueryPort = "19132";
        public string Defaultmap = "Bedrock level";
        public string Maxplayers = "10";
        public string Additional = string.Empty;
        public string SharedInstallationAppId => BedrockWindowsDownloadType;
        public string SharedInstallationExecutableName => StartPath;

        private bool UsesSharedInstallation => !string.IsNullOrWhiteSpace(_serverData.SharedInstallationId);

        private string GetBinaryRoot()
        {
            return Functions.SharedInstallationStore.GetServerBinaryRoot(_serverData);
        }

        private string GetInstanceRoot()
        {
            return Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
        }

        public MCBE(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        private static async Task<BedrockServerDownload> GetLatestBedrockServerDownload(WebClient webClient)
        {
            string response = await webClient.DownloadStringTaskAsync(BedrockDownloadLinksUrl);
            JObject downloadData = JObject.Parse(response);
            JArray links = downloadData["result"]?["links"] as JArray;
            if (links == null)
            {
                throw new InvalidDataException("The official Minecraft download API did not return a links list.");
            }

            foreach (JToken link in links)
            {
                if (!string.Equals((string)link["downloadType"], BedrockWindowsDownloadType, StringComparison.Ordinal))
                {
                    continue;
                }

                string url = (string)link["downloadUrl"];
                Uri downloadUri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out downloadUri) ||
                    !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !downloadUri.IsDefaultPort ||
                    (!string.Equals(downloadUri.Host, "www.minecraft.net", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(downloadUri.Host, "minecraft.azureedge.net", StringComparison.OrdinalIgnoreCase)) ||
                    downloadUri.AbsolutePath.IndexOf("/bin-win/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException("The official Minecraft download API returned an invalid Windows Bedrock server URL.");
                }

                Match archiveName = BedrockArchiveNameRegex.Match(Path.GetFileName(downloadUri.AbsolutePath));
                if (!archiveName.Success)
                {
                    throw new InvalidDataException("The official Minecraft download API returned an unexpected Windows Bedrock server archive name.");
                }

                return new BedrockServerDownload
                {
                    Url = downloadUri,
                    FileName = Path.GetFileName(downloadUri.AbsolutePath),
                    Version = archiveName.Groups["version"].Value
                };
            }

            throw new InvalidDataException("The official Minecraft download API did not include a Windows Bedrock server download.");
        }

        public async void CreateServerCFG()
        {
            try
            {
                await EnsureServerConfigAsync(overwrite: true);
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to create Minecraft Bedrock server configuration: {exception.Message}";
            }
        }

        private async Task<bool> EnsureServerConfigAsync(bool overwrite)
        {
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.properties");
            if (!overwrite && File.Exists(configPath))
            {
                return true;
            }

            Directory.CreateDirectory(GetInstanceRoot());
            if (!await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                Error = "Could not download the Minecraft Bedrock server.properties template.";
                return false;
            }

            ApplyServerPropertiesSettings(configPath, _serverData);
            return true;
        }

        internal bool UpdateServerPropertiesFromWindowsGsmSettings()
        {
            Error = string.Empty;
            try
            {
                ApplyServerPropertiesSettings(
                    Path.Combine(GetInstanceRoot(), "server.properties"),
                    _serverData);
                return true;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to update Minecraft Bedrock server configuration: {exception.Message}";
                return false;
            }
        }

        internal static void ApplyServerPropertiesSettings(string configPath, Functions.ServerConfig serverData)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("A Minecraft Bedrock server.properties path is required.", nameof(configPath));
            }
            if (serverData == null)
            {
                throw new ArgumentNullException(nameof(serverData));
            }
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Minecraft Bedrock server.properties was not found.", configPath);
            }

            int serverPort = ParseServerPort(serverData.ServerPort);

            var managedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "server-name", serverData.ServerName ?? string.Empty },
                { "max-players", string.IsNullOrWhiteSpace(serverData.ServerMaxPlayer) ? "10" : serverData.ServerMaxPlayer },
                { "server-port", serverPort.ToString() },
                { "server-portv6", (serverPort + 1).ToString() },
                { "level-name", string.IsNullOrWhiteSpace(serverData.ServerMap) ? "Bedrock level" : serverData.ServerMap }
            };
            var lines = new List<string>(File.ReadAllLines(configPath));
            var updatedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                string propertyName = line.Substring(0, separatorIndex).Trim();
                if (managedProperties.TryGetValue(propertyName, out string value))
                {
                    lines[lineIndex] = line.Substring(0, separatorIndex + 1) + value;
                    updatedProperties.Add(propertyName);
                }
            }

            foreach (KeyValuePair<string, string> property in managedProperties)
            {
                if (!updatedProperties.Contains(property.Key))
                {
                    lines.Add(property.Key + "=" + property.Value);
                }
            }

            File.WriteAllLines(configPath, lines);
        }

        internal static int ParseServerPort(string value)
        {
            if (!int.TryParse(value, out int serverPort) || serverPort < 1 || serverPort >= 65535)
            {
                throw new InvalidDataException("Minecraft Bedrock's IPv4 port must be between 1 and 65534 so its IPv6 port remains valid.");
            }

            return serverPort;
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            FileStream sharedLease = null;
            try
            {
                sharedLease = Functions.SharedInstallationStore.AcquireReadLeaseForServer(_serverData);
                string workingDir = GetInstanceRoot();
                string binaryRoot = GetBinaryRoot();

                string exePath = Path.Combine(binaryRoot, StartPath);
                if (!File.Exists(exePath))
                {
                    Error = $"{Path.GetFileName(exePath)} not found ({exePath})";
                    sharedLease?.Dispose();
                    return Task.FromResult<Process>(null);
                }

                if (UsesSharedInstallation)
                {
                    string buildId = GetLocalBuild();
                    if (string.IsNullOrWhiteSpace(buildId))
                    {
                        sharedLease?.Dispose();
                        return Task.FromResult<Process>(null);
                    }

                    EnsureSharedInstanceRuntimeData(binaryRoot, workingDir, buildId);
                }

                string serverConfigPath = Path.Combine(workingDir, "server.properties");
                if (!File.Exists(serverConfigPath))
                {
                    Error = $"{Path.GetFileName(serverConfigPath)} not found ({serverConfigPath})";
                    sharedLease?.Dispose();
                    return Task.FromResult<Process>(null);
                }

                Process p;
                if (!AllowsEmbedConsole)
                {
                    p = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = workingDir,
                            FileName = exePath,
                            Arguments = _serverData.ServerParam,
                            WindowStyle = ProcessWindowStyle.Minimized,
                        },
                        EnableRaisingEvents = true
                    };
                    p.Start();
                }
                else
                {
                    p = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = workingDir,
                            FileName = exePath,
                            Arguments = _serverData.ServerParam,
                            WindowStyle = ProcessWindowStyle.Minimized,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        },
                        EnableRaisingEvents = true
                    };
                    var serverConsole = new Functions.ServerConsole(_serverData.ServerID);
                    p.OutputDataReceived += serverConsole.AddOutput;
                    p.ErrorDataReceived += serverConsole.AddOutput;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                if (sharedLease != null)
                {
                    Functions.SharedInstallationStore.ReleaseLeaseWhenProcessExits(p, sharedLease);
                    sharedLease = null;
                }

                return Task.FromResult(p);
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
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
                if (Functions.ServerConsole.IsStandardInputRedirected(p))
                {
                    p.StandardInput.WriteLine("stop");
                }
                else
                {
                    Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "stop");
                }
            });
        }

        public async Task<Process> Install()
        {
            Error = string.Empty;
            if (!ConfirmEulaAndPrivacy())
            {
                return null;
            }

            try
            {
                using (WebClient webClient = new MCBEWebclient())
                {
                    BedrockServerDownload download = await GetLatestBedrockServerDownload(webClient);

                    //Download zip and extract then delete zip
                    string serverFilesPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
                    string zipPath = Path.Combine(serverFilesPath, download.FileName);
                    try
                    {
                        await webClient.DownloadFileTaskAsync(download.Url, zipPath);
                        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, serverFilesPath));
                    }
                    finally
                    {
                        if (File.Exists(zipPath))
                        {
                            File.Delete(zipPath);
                        }
                    }

                    //Create MCBE-version.txt and write the version
                    File.WriteAllText(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "MCBE-version.txt"), download.Version);
                }
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to install Minecraft Bedrock server: {exception.Message}";
            }

            return null;
        }

        public async Task<Process> InstallSharedBinaries()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!UsesSharedInstallation)
            {
                Error = "A shared installation ID is required to install Minecraft Bedrock binaries.";
                return null;
            }

            if (!ConfirmEulaAndPrivacy())
            {
                return null;
            }

            string stagingRoot = null;
            try
            {
                string binaryRoot = GetBinaryRoot();
                string binaryParent = Path.GetDirectoryName(binaryRoot);
                Directory.CreateDirectory(binaryParent);
                stagingRoot = Path.Combine(binaryParent, "__mcbe_install_" + Guid.NewGuid().ToString("N"));

                using (WebClient webClient = new MCBEWebclient())
                {
                    BedrockServerDownload download = await GetLatestBedrockServerDownload(webClient);
                    await CreateStagedPackageAsync(webClient, download, stagingRoot);
                    PublishInitialSharedInstallation(binaryRoot, stagingRoot);
                }

                Error = string.Empty;
                return null;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to install Minecraft Bedrock server: {exception.Message}";
                string cleanupError = TryDeleteDirectory(stagingRoot);
                if (!string.IsNullOrWhiteSpace(cleanupError))
                {
                    Error += Environment.NewLine + $"Could not remove the incomplete Bedrock installation staging directory: {cleanupError}";
                }

                return null;
            }
        }

        public async Task<bool> InstallConfigOnly()
        {
            Error = string.Empty;
            try
            {
                string binaryRoot = GetBinaryRoot();
                if (!File.Exists(Path.Combine(binaryRoot, StartPath)))
                {
                    throw new InvalidDataException($"{StartPath} not found ({binaryRoot}).");
                }

                string buildId = GetLocalBuild();
                if (string.IsNullOrWhiteSpace(buildId))
                {
                    throw new InvalidDataException("Could not read the Minecraft Bedrock installation build.");
                }

                string instanceRoot = GetInstanceRoot();
                Directory.CreateDirectory(instanceRoot);
                if (UsesSharedInstallation)
                {
                    CopyInstanceConfigurationFiles(binaryRoot, instanceRoot);
                    EnsureSharedInstanceRuntimeData(binaryRoot, instanceRoot, buildId);
                }

                if (!await EnsureServerConfigAsync(overwrite: true))
                {
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to prepare Minecraft Bedrock instance configuration: {exception.Message}";
                return false;
            }
        }

        private bool ConfirmEulaAndPrivacy()
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(
                LocalizationManager.Get("MinecraftBedrockEulaPrivacyPrompt"),
                LocalizationManager.Get("MinecraftEulaTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                return true;
            }

            Error = LocalizationManager.Get("MinecraftBedrockEulaDeclined");
            return false;
        }

        private static async Task CreateStagedPackageAsync(
            WebClient webClient,
            BedrockServerDownload download,
            string stagingRoot)
        {
            Directory.CreateDirectory(stagingRoot);
            string zipPath = Path.Combine(stagingRoot, download.FileName);
            try
            {
                await webClient.DownloadFileTaskAsync(download.Url, zipPath);
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingRoot));
            }
            finally
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }

            if (!File.Exists(Path.Combine(stagingRoot, "bedrock_server.exe")))
            {
                throw new InvalidDataException("The Minecraft Bedrock archive did not contain bedrock_server.exe at its root.");
            }

            File.WriteAllText(Path.Combine(stagingRoot, "MCBE-version.txt"), download.Version);
        }

        private static void PublishInitialSharedInstallation(string binaryRoot, string stagingRoot)
        {
            string parentDirectory = Path.GetDirectoryName(binaryRoot);
            Directory.CreateDirectory(parentDirectory);
            if (Directory.Exists(binaryRoot))
            {
                if (Directory.GetFileSystemEntries(binaryRoot).Length > 0)
                {
                    throw new InvalidDataException("The new Minecraft Bedrock shared installation directory is not empty.");
                }

                Directory.Delete(binaryRoot);
            }

            Directory.Move(stagingRoot, binaryRoot);
        }

        private static void CopyInstanceConfigurationFiles(string binaryRoot, string instanceRoot)
        {
            foreach (string fileName in BedrockInstanceConfigurationFiles)
            {
                string sourcePath = Path.Combine(binaryRoot, fileName);
                string destinationPath = Path.Combine(instanceRoot, fileName);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                if (!File.Exists(sourcePath))
                {
                    throw new InvalidDataException($"The Minecraft Bedrock shared installation is missing {fileName}.");
                }

                File.Copy(sourcePath, destinationPath);
            }
        }

        internal static void EnsureSharedInstanceRuntimeData(string binaryRoot, string instanceRoot, string buildId)
        {
            if (string.IsNullOrWhiteSpace(binaryRoot))
            {
                throw new ArgumentException("A Bedrock binary root is required.", nameof(binaryRoot));
            }
            if (string.IsNullOrWhiteSpace(instanceRoot))
            {
                throw new ArgumentException("A Bedrock instance root is required.", nameof(instanceRoot));
            }
            if (string.IsNullOrWhiteSpace(buildId))
            {
                throw new ArgumentException("A Bedrock build ID is required.", nameof(buildId));
            }

            Directory.CreateDirectory(instanceRoot);
            string versionPath = Path.Combine(instanceRoot, "MCBE-version.txt");
            bool buildMatches = File.Exists(versionPath) &&
                string.Equals(File.ReadAllText(versionPath).Trim(), buildId, StringComparison.Ordinal);
            bool directoriesMatch = true;
            foreach (string directoryName in BedrockRuntimeDirectories)
            {
                bool sharedDirectoryExists = Directory.Exists(Path.Combine(binaryRoot, directoryName));
                bool instanceDirectoryExists = Directory.Exists(Path.Combine(instanceRoot, directoryName));
                if (sharedDirectoryExists != instanceDirectoryExists)
                {
                    directoriesMatch = false;
                    break;
                }
            }

            if (buildMatches && directoriesMatch)
            {
                return;
            }

            foreach (string directoryName in BedrockRuntimeDirectories)
            {
                string sourcePath = Path.Combine(binaryRoot, directoryName);
                string destinationPath = Path.Combine(instanceRoot, directoryName);
                if (!Directory.Exists(sourcePath))
                {
                    if (Directory.Exists(destinationPath))
                    {
                        Directory.Delete(destinationPath, recursive: true);
                    }

                    continue;
                }

                ReplaceRuntimeDirectory(sourcePath, destinationPath);
            }

            File.WriteAllText(versionPath, buildId);
        }

        private static void ReplaceRuntimeDirectory(string sourcePath, string destinationPath)
        {
            string parentDirectory = Path.GetDirectoryName(destinationPath);
            string uniqueSuffix = Guid.NewGuid().ToString("N");
            string stagingPath = Path.Combine(parentDirectory, ".mcbe-runtime-stage-" + uniqueSuffix);
            string backupPath = Path.Combine(parentDirectory, ".mcbe-runtime-backup-" + uniqueSuffix);
            CopyDirectory(sourcePath, stagingPath);

            bool destinationMoved = false;
            try
            {
                if (Directory.Exists(destinationPath))
                {
                    Directory.Move(destinationPath, backupPath);
                    destinationMoved = true;
                }

                Directory.Move(stagingPath, destinationPath);
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                string rollbackError = string.Empty;
                if (Directory.Exists(destinationPath))
                {
                    rollbackError = TryDeleteDirectory(destinationPath);
                }
                if (destinationMoved && Directory.Exists(backupPath))
                {
                    try
                    {
                        Directory.Move(backupPath, destinationPath);
                    }
                    catch (Exception rollbackException) when (IsExpectedOperationException(rollbackException))
                    {
                        rollbackError = string.IsNullOrWhiteSpace(rollbackError)
                            ? rollbackException.Message
                            : rollbackError + Environment.NewLine + rollbackException.Message;
                    }
                }

                if (!string.IsNullOrWhiteSpace(rollbackError))
                {
                    throw new IOException(
                        $"Failed to update Bedrock runtime data at '{destinationPath}'. Rollback also failed; the backup is retained at '{backupPath}'. {rollbackError}",
                        exception);
                }

                throw;
            }
            finally
            {
                string stagingCleanupError = TryDeleteDirectory(stagingPath);
                if (!string.IsNullOrWhiteSpace(stagingCleanupError))
                {
                    throw new IOException($"Could not remove Bedrock runtime staging directory '{stagingPath}': {stagingCleanupError}.");
                }
            }

            string backupCleanupError = TryDeleteDirectory(backupPath);
            if (!string.IsNullOrWhiteSpace(backupCleanupError))
            {
                throw new IOException($"Bedrock runtime data was updated, but the previous directory remains at '{backupPath}': {backupCleanupError}.");
            }
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            foreach (string filePath in Directory.GetFiles(sourcePath, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)), overwrite: true);
            }
            foreach (string directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.TopDirectoryOnly))
            {
                CopyDirectory(directoryPath, Path.Combine(destinationPath, Path.GetFileName(directoryPath)));
            }
        }

        private static string TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                return string.Empty;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                return exception.Message;
            }
        }

        private static bool IsExpectedOperationException(Exception exception)
        {
            return exception is ArgumentException ||
                exception is IOException ||
                exception is InvalidDataException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is Win32Exception ||
                exception is WebException ||
                exception is NotSupportedException ||
                exception is FormatException ||
                exception is OverflowException ||
                exception is TaskCanceledException ||
                exception is SecurityException;
        }

        public async Task<Process> Update()
        {
            Error = string.Empty;
            FileStream sharedLease = null;
            try
            {
                sharedLease = Functions.SharedInstallationStore.AcquireExclusiveLeaseForServer(_serverData);
                using (WebClient webClient = new MCBEWebclient())
                {
                    BedrockServerDownload download = await GetLatestBedrockServerDownload(webClient);

                    string serverFilesPath = GetBinaryRoot();
                    string tempPath = Path.Combine(serverFilesPath, "__temp");

                    //Delete old __temp folder
                    if (Directory.Exists(tempPath))
                    {
                        await Task.Run(() => Directory.Delete(tempPath, true));
                    }

                    Directory.CreateDirectory(tempPath);

                    //Download zip and extract then delete zip - install to __temp folder
                    string zipPath = Path.Combine(tempPath, download.FileName);
                    await webClient.DownloadFileTaskAsync(download.Url, zipPath);
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempPath));
                    await Task.Run(() => File.Delete(zipPath));

                    //Delete old folder and files
                    //List of sub folders that may or maynot exist in the various zip files
                    List<string> serverSubFolder = new List<string>() { "behavior_packs", "definitions", "resource_packs", "structures" };

                    await Task.Run(() =>
                    {
                        foreach (var folder in serverSubFolder)
                        {
                            if (Directory.Exists(Path.Combine(serverFilesPath, folder)))
                            {
                                Directory.Delete(Path.Combine(serverFilesPath, folder),true);
                            }
                        }
                        File.Delete(Path.Combine(serverFilesPath, "bedrock_server.exe"));
                        File.Delete(Path.Combine(serverFilesPath, "bedrock_server.pdb"));
                        File.Delete(Path.Combine(serverFilesPath, "release-notes.txt"));
                    });

                    //Move folder and files
                    await Task.Run(() =>
                    {
                        foreach (var folder in serverSubFolder)
                        {
                            if (Directory.Exists(Path.Combine(serverFilesPath, "__temp", folder)))
                            {
                                Directory.Move(Path.Combine(serverFilesPath, "__temp", folder), Path.Combine(serverFilesPath, folder));
                            }
                        }
                        File.Move(Path.Combine(serverFilesPath, "__temp", "bedrock_server.exe"), Path.Combine(serverFilesPath, "bedrock_server.exe"));
                        File.Move(Path.Combine(serverFilesPath, "__temp", "bedrock_server.pdb"), Path.Combine(serverFilesPath, "bedrock_server.pdb"));
                        File.Move(Path.Combine(serverFilesPath, "__temp", "release-notes.txt"), Path.Combine(serverFilesPath, "release-notes.txt"));
                    });

                    //Delete __temp folder
                    await Task.Run(() => Directory.Delete(tempPath, true));

                    //Create MCBE-version.txt and write the version
                    File.WriteAllText(Path.Combine(serverFilesPath, "MCBE-version.txt"), download.Version);
                    if (UsesSharedInstallation)
                    {
                        Functions.SharedInstallationStore.MarkReady(_serverData.SharedInstallationId, download.Version);
                    }
                }

                return null;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
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
                string executablePath = Path.Combine(GetBinaryRoot(), StartPath);
                if (File.Exists(executablePath))
                {
                    Error = string.Empty;
                    return true;
                }

                Error = $"{Path.GetFileName(executablePath)} not found ({executablePath})";
                return false;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
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
                string versionPath = Path.Combine(GetBinaryRoot(), "MCBE-version.txt");
                if (File.Exists(versionPath))
                {
                    Error = string.Empty;
                    return File.ReadAllText(versionPath);
                }

                Error = "Fail to get local build";
                return string.Empty;
            }
            catch (Exception exception) when (IsExpectedOperationException(exception))
            {
                Error = $"Failed to get the local Minecraft Bedrock server build: {exception.Message}";
                return string.Empty; 
            }
        }

        public async Task<string> GetRemoteBuild()
        {
            try
            {
                using (WebClient webClient = new MCBEWebclient())
                {
                    BedrockServerDownload download = await GetLatestBedrockServerDownload(webClient);
                    return download.Version;
                }
            }
            catch (Exception ex)
            {
                Error = $"Failed to get the latest Minecraft Bedrock server build: {ex.Message}";
                return string.Empty;
            }
        }
    }
}
