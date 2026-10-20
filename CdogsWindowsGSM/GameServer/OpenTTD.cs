using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class OpenTTD
    {
        private const string OpenTTDReleaseApi = "https://api.github.com/repos/OpenTTD/OpenTTD/releases/latest";
        private const string OpenGfxReleaseApi = "https://api.github.com/repos/OpenTTD/OpenGFX/releases/latest";
        private const string CdnHost = "cdn.openttd.org";
        private const string ConfigFileName = "openttd.cfg";
        private const string BaseSetDirectoryName = "baseset";
        private const string SaveDirectoryName = "save";
        private const string OpenTTDVersionFileName = "openttd_version.txt";
        private const string OpenGfxVersionFileName = "opengfx_version.txt";
        private const string ExecutableName = "openttd.exe";
        private readonly ServerConfig _serverData;

        private sealed class ReleaseInfo
        {
            public Uri DownloadUrl { get; set; }
            public string FileName { get; set; }
            public string Version { get; set; }
        }

        public const string FullName = "OpenTTD Dedicated Server";
        public string StartPath = ExecutableName;
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "3979";
        public string QueryPort = "3978";
        public string Defaultmap = "Random map";
        public string Maxplayers = "10";
        public string Additional = string.Empty;
        public string Error;
        public string Notice;

        public OpenTTD(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            int port;
            int maxClients;
            if (_serverData == null || !int.TryParse(_serverData.ServerPort, out port) || port < 1 || port > 65535)
            {
                Error = "OpenTTD server port must be between 1 and 65535.";
                return;
            }

            if (!int.TryParse(_serverData.ServerMaxPlayer, out maxClients) || maxClients < 1 || maxClients > 255)
            {
                Error = "OpenTTD max players must be between 1 and 255.";
                return;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string configPath = Path.Combine(serverFilesPath, ConfigFileName);
            try
            {
                Directory.CreateDirectory(serverFilesPath);
                Directory.CreateDirectory(Path.Combine(serverFilesPath, SaveDirectoryName));
                string config = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                var settings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("server_name", QuoteConfigValue(_serverData.ServerName)),
                    new KeyValuePair<string, string>("server_port", port.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("max_clients", maxClients.ToString(CultureInfo.InvariantCulture))
                };
                File.WriteAllText(configPath, SetNetworkSettings(config, settings), new UTF8Encoding(false));
            }
            catch (IOException ex)
            {
                Error = $"Failed to write OpenTTD config: {ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to write OpenTTD config: {ex.Message}";
            }
        }

        private static string QuoteConfigValue(string value)
        {
            value = (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
            return "\"" + value + "\"";
        }

        private static string SetNetworkSettings(string config, IList<KeyValuePair<string, string>> settings)
        {
            string newline = config.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : config.IndexOf('\r') >= 0 ? "\r" : "\n";
            bool hasTrailingNewline = config.EndsWith("\r\n", StringComparison.Ordinal) ||
                config.EndsWith("\r", StringComparison.Ordinal) || config.EndsWith("\n", StringComparison.Ordinal);
            var lines = string.IsNullOrEmpty(config)
                ? new List<string>()
                : new List<string>(Regex.Split(config, "\r\n|\n|\r"));
            if (hasTrailingNewline && lines.Count > 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            int networkStart = -1;
            int networkEnd = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string sectionName;
                if (!TryGetSectionName(lines[i], out sectionName) ||
                    !string.Equals(sectionName, "network", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                networkStart = i;
                networkEnd = lines.Count;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (TryGetSectionName(lines[j], out sectionName))
                    {
                        networkEnd = j;
                        break;
                    }
                }
            }

            if (networkStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }
                lines.Add("[network]");
                networkStart = lines.Count - 1;
                networkEnd = lines.Count;
            }

            foreach (KeyValuePair<string, string> setting in settings)
            {
                bool found = false;
                for (int i = networkStart + 1; i < networkEnd; i++)
                {
                    if (!string.Equals(GetSettingName(lines[i]), setting.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    lines[i] = setting.Key + " = " + setting.Value;
                    found = true;
                }
                if (!found)
                {
                    lines.Insert(networkEnd, setting.Key + " = " + setting.Value);
                    networkEnd++;
                }
            }
            return string.Join(newline, lines) + newline;
        }

        private static bool TryGetSectionName(string line, out string sectionName)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
            {
                sectionName = null;
                return false;
            }
            sectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return true;
        }

        private static string GetSettingName(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
            {
                return null;
            }
            int separator = trimmed.IndexOf('=');
            return separator < 0 ? null : trimmed.Substring(0, separator).Trim();
        }

        private static WebClient CreateWebClient()
        {
            var webClient = new WebClient();
            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            return webClient;
        }

        private static async Task<ReleaseInfo> GetLatestRelease(
            WebClient webClient, string apiUrl, string releaseDirectory, string archivePrefix, string archiveSuffix, string productName)
        {
            JObject release = JObject.Parse(await webClient.DownloadStringTaskAsync(apiUrl));
            string tag = ((string)release["tag_name"] ?? string.Empty).Trim();
            string version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
            Version parsedVersion;
            if (!Version.TryParse(version, out parsedVersion) || parsedVersion.Major < 1)
            {
                throw new InvalidDataException($"GitHub returned an invalid {productName} release tag.");
            }

            string fileName = archivePrefix + version + archiveSuffix;
            string url = $"https://{CdnHost}/{releaseDirectory}/{version}/{fileName}";
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || !string.Equals(uri.Host, CdnHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Could not build the official {productName} download URL.");
            }
            return new ReleaseInfo { DownloadUrl = uri, FileName = fileName, Version = version };
        }

        private async Task DownloadAndInstallLatestRelease()
        {
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            Directory.CreateDirectory(serverFilesPath);
            string stagingPath = Path.Combine(serverFilesPath, "__openttd_stage_" + Guid.NewGuid().ToString("N"));
            string downloadPath = Path.Combine(stagingPath, "download");
            string openttdExtractPath = Path.Combine(stagingPath, "openttd");
            string opengfxExtractPath = Path.Combine(stagingPath, "opengfx");
            try
            {
                Directory.CreateDirectory(downloadPath);
                Directory.CreateDirectory(openttdExtractPath);
                Directory.CreateDirectory(opengfxExtractPath);
                ReleaseInfo openttdRelease;
                ReleaseInfo opengfxRelease;
                string openttdArchivePath;
                string opengfxArchivePath;
                using (WebClient webClient = CreateWebClient())
                {
                    openttdRelease = await GetLatestRelease(webClient, OpenTTDReleaseApi, "openttd-releases", "openttd-", "-windows-win64.zip", "OpenTTD");
                    opengfxRelease = await GetLatestRelease(webClient, OpenGfxReleaseApi, "opengfx-releases", "opengfx-", "-all.zip", "OpenGFX");
                    openttdArchivePath = Path.Combine(downloadPath, openttdRelease.FileName);
                    opengfxArchivePath = Path.Combine(downloadPath, opengfxRelease.FileName);
                    await webClient.DownloadFileTaskAsync(openttdRelease.DownloadUrl, openttdArchivePath);
                    await webClient.DownloadFileTaskAsync(opengfxRelease.DownloadUrl, opengfxArchivePath);
                }

                await Task.Run(() => ZipFile.ExtractToDirectory(openttdArchivePath, openttdExtractPath));
                await Task.Run(() => ZipFile.ExtractToDirectory(opengfxArchivePath, opengfxExtractPath));
                string packageRoot = FindPackageRoot(openttdExtractPath);
                string opengfxTarPath = FindOpenGfxArchive(opengfxExtractPath, opengfxRelease.Version);
                if (!Directory.Exists(Path.Combine(packageRoot, BaseSetDirectoryName)))
                {
                    throw new InvalidDataException("The OpenTTD archive did not contain its base set directory.");
                }

                await Task.Run(() => CopyDirectory(packageRoot, serverFilesPath));
                string baseSetPath = Path.Combine(serverFilesPath, BaseSetDirectoryName);
                Directory.CreateDirectory(baseSetPath);
                File.Copy(opengfxTarPath, Path.Combine(baseSetPath, Path.GetFileName(opengfxTarPath)), true);
                File.WriteAllText(Path.Combine(serverFilesPath, OpenTTDVersionFileName), openttdRelease.Version, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(serverFilesPath, OpenGfxVersionFileName), opengfxRelease.Version, new UTF8Encoding(false));
            }
            finally
            {
                if (Directory.Exists(stagingPath))
                {
                    Directory.Delete(stagingPath, true);
                }
            }
        }

        private static string FindPackageRoot(string extractPath)
        {
            string[] executablePaths = Directory.GetFiles(extractPath, ExecutableName, SearchOption.AllDirectories);
            if (executablePaths.Length == 0)
            {
                throw new InvalidDataException("The OpenTTD archive did not contain openttd.exe.");
            }
            return Path.GetDirectoryName(executablePaths[0]);
        }

        private static string FindOpenGfxArchive(string extractPath, string version)
        {
            string expectedName = "opengfx-" + version + ".tar";
            string[] archives = Directory.GetFiles(extractPath, expectedName, SearchOption.AllDirectories);
            if (archives.Length == 0)
            {
                throw new InvalidDataException($"The OpenGFX archive did not contain {expectedName}.");
            }
            return archives[0];
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            foreach (string sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourceFile.Substring(sourceDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (ShouldPreserveServerFile(relativePath))
                {
                    continue;
                }
                string destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourceFile, destinationPath, true);
            }
        }

        private static bool ShouldPreserveServerFile(string relativePath)
        {
            string normalizedPath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return string.Equals(normalizedPath, ConfigFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, OpenTTDVersionFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, OpenGfxVersionFileName, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(SaveDirectoryName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
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
            string serverParameters = _serverData.ServerParam ?? string.Empty;
            if (!Regex.IsMatch(serverParameters, @"(?:^|\s)-g(?:\s|$)"))
            {
                serverParameters = string.IsNullOrWhiteSpace(serverParameters) ? "-g" : serverParameters.Trim() + " -g";
            }
            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = Path.Combine(serverFilesPath, StartPath),
                    Arguments = $"-D -c \"{ConfigFileName}\" {serverParameters}",
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false,
                    CreateNoWindow = AllowsEmbedConsole,
                    RedirectStandardInput = true,
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
                    Error = "OpenTTD did not start a server process.";
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
            catch (Win32Exception ex)
            {
                Error = $"Failed to start OpenTTD: {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                Error = $"Failed to start OpenTTD: {ex.Message}";
            }
            catch (ArgumentException ex)
            {
                Error = $"Failed to start OpenTTD: {ex.Message}";
            }
            catch (IOException ex)
            {
                Error = $"Failed to start OpenTTD: {ex.Message}";
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
            if (process == null || process.HasExited)
            {
                return;
            }
            try
            {
                process.StandardInput.WriteLine("quit");
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex);
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
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

        public Task<Process> Install()
        {
            return InstallOrUpdate("install");
        }

        public Task<Process> Update()
        {
            return InstallOrUpdate("update");
        }

        private async Task<Process> InstallOrUpdate(string action)
        {
            Error = string.Empty;
            try
            {
                await DownloadAndInstallLatestRelease();
            }
            catch (WebException ex)
            {
                Error = $"Failed to {action} OpenTTD: {ex.Message}";
            }
            catch (JsonException ex)
            {
                Error = $"Failed to {action} OpenTTD: {ex.Message}";
            }
            catch (InvalidDataException ex)
            {
                Error = $"Failed to {action} OpenTTD: {ex.Message}";
            }
            catch (IOException ex)
            {
                Error = $"Failed to {action} OpenTTD: {ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to {action} OpenTTD: {ex.Message}";
            }
            return null;
        }

        public bool IsInstallValid()
        {
            Error = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (!File.Exists(executablePath))
            {
                Error = $"{StartPath} was not found ({executablePath}).";
                return false;
            }
            try
            {
                if (HasOpenGfx(serverFilesPath))
                {
                    return true;
                }
                Error = "OpenGFX base graphics are missing. Update this server to download them.";
                return false;
            }
            catch (IOException ex)
            {
                Error = $"Could not check OpenGFX base graphics: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Could not check OpenGFX base graphics: {ex.Message}";
                return false;
            }
        }

        private static bool HasOpenGfx(string serverFilesPath)
        {
            string baseSetPath = Path.Combine(serverFilesPath, BaseSetDirectoryName);
            if (!Directory.Exists(baseSetPath))
            {
                return false;
            }
            if (Directory.GetFiles(baseSetPath, "opengfx-*.tar", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return true;
            }
            foreach (string metadataPath in Directory.GetFiles(baseSetPath, "opengfx-*.obg", SearchOption.TopDirectoryOnly))
            {
                if (File.Exists(Path.ChangeExtension(metadataPath, ".grf")))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsImportValid(string path)
        {
            Error = string.Empty;
            string executablePath = Path.Combine(path, StartPath);
            if (File.Exists(executablePath) && HasOpenGfx(path))
            {
                return true;
            }
            Error = $"Invalid OpenTTD import; {StartPath} or OpenGFX base graphics are missing from ({path}).";
            return false;
        }

        public string GetLocalBuild()
        {
            Error = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (!File.Exists(executablePath))
            {
                Error = $"{StartPath} was not found ({executablePath}).";
                return string.Empty;
            }
            try
            {
                string openttdVersion = ReadVersionFile(Path.Combine(serverFilesPath, OpenTTDVersionFileName));
                if (string.IsNullOrWhiteSpace(openttdVersion))
                {
                    openttdVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
                }
                if (string.IsNullOrWhiteSpace(openttdVersion))
                {
                    openttdVersion = "unknown";
                }
                string opengfxVersion = ReadVersionFile(Path.Combine(serverFilesPath, OpenGfxVersionFileName));
                if (string.IsNullOrWhiteSpace(opengfxVersion))
                {
                    opengfxVersion = GetInstalledOpenGfxVersion(serverFilesPath) ?? "unknown";
                }
                return $"{openttdVersion} / OpenGFX {opengfxVersion}";
            }
            catch (IOException ex)
            {
                Error = $"Failed to read the local OpenTTD build: {ex.Message}";
                return string.Empty;
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to read the local OpenTTD build: {ex.Message}";
                return string.Empty;
            }
            catch (Win32Exception ex)
            {
                Error = $"Failed to read the local OpenTTD build: {ex.Message}";
                return string.Empty;
            }
        }

        private static string ReadVersionFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string version = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }

        private static string GetInstalledOpenGfxVersion(string serverFilesPath)
        {
            string baseSetPath = Path.Combine(serverFilesPath, BaseSetDirectoryName);
            if (!Directory.Exists(baseSetPath))
            {
                return null;
            }
            string latestVersion = null;
            Version latestParsedVersion = null;
            foreach (string filePath in Directory.GetFiles(baseSetPath, "opengfx-*.*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                const string prefix = "opengfx-";
                if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string version = fileName.Substring(prefix.Length);
                Version parsedVersion;
                if (Version.TryParse(version, out parsedVersion) &&
                    (latestParsedVersion == null || parsedVersion.CompareTo(latestParsedVersion) > 0))
                {
                    latestVersion = version;
                    latestParsedVersion = parsedVersion;
                }
            }
            return latestVersion;
        }

        public async Task<string> GetRemoteBuild()
        {
            Error = string.Empty;
            try
            {
                using (WebClient webClient = CreateWebClient())
                {
                    ReleaseInfo openttdRelease = await GetLatestRelease(webClient, OpenTTDReleaseApi, "openttd-releases", "openttd-", "-windows-win64.zip", "OpenTTD");
                    ReleaseInfo opengfxRelease = await GetLatestRelease(webClient, OpenGfxReleaseApi, "opengfx-releases", "opengfx-", "-all.zip", "OpenGFX");
                    return $"{openttdRelease.Version} / OpenGFX {opengfxRelease.Version}";
                }
            }
            catch (WebException ex)
            {
                Error = $"Failed to get the latest OpenTTD build: {ex.Message}";
            }
            catch (JsonException ex)
            {
                Error = $"Failed to get the latest OpenTTD build: {ex.Message}";
            }
            catch (InvalidDataException ex)
            {
                Error = $"Failed to get the latest OpenTTD build: {ex.Message}";
            }
            catch (IOException ex)
            {
                Error = $"Failed to get the latest OpenTTD build: {ex.Message}";
            }
            return string.Empty;
        }
    }
}