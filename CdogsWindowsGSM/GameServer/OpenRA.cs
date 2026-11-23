using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class OpenRA
    {
        private const string ReleaseFeedUrl = "https://github.com/OpenRA/OpenRA/releases.atom";
        private const string RuntimeDirectoryName = "OpenRA";
        private const string SupportDirectoryName = "OpenRA-data";
        private const string VersionFileName = "openra_version.txt";
        private const string ExecutableRelativePath = @"bin\OpenRA.Server.exe";
        private readonly ServerConfig _serverData;

        private sealed class ReleaseInfo
        {
            public Uri DownloadUrl { get; set; }
            public string Version { get; set; }
        }

        public const string FullName = "OpenRA Dedicated Server";
        public string StartPath = Path.Combine(RuntimeDirectoryName, ExecutableRelativePath);
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "1234";
        public string QueryPort = "1234";
        public string Defaultmap = "Default";
        public string Maxplayers = "8";
        public string Additional = string.Empty;
        public string Error;
        public string Notice;

        public OpenRA(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            int port;
            if (_serverData == null || !int.TryParse(_serverData.ServerPort, out port) || port < 1 || port > 65535)
            {
                Error = "OpenRA server port must be between 1 and 65535.";
                return;
            }

            try
            {
                string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
                Directory.CreateDirectory(Path.Combine(serverFilesPath, SupportDirectoryName));
            }
            catch (IOException exception)
            {
                Error = "Failed to prepare the OpenRA server data directory: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to prepare the OpenRA server data directory: " + exception.Message;
            }
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
            Notice = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__openra_stage_" + Guid.NewGuid().ToString("N"));
            string archivePath = Path.Combine(stagingPath, "openra.zip");
            string extractPath = Path.Combine(stagingPath, "extract");

            try
            {
                Directory.CreateDirectory(extractPath);
                ReleaseInfo release;
                using (WebClient webClient = CreateWebClient())
                {
                    release = await GetLatestRelease(webClient);
                    await webClient.DownloadFileTaskAsync(release.DownloadUrl, archivePath);
                }

                if (!await FileManagement.ExtractArchive(archivePath, extractPath))
                {
                    throw new InvalidDataException("Could not safely extract the OpenRA portable package.");
                }

                string packageRoot = FindPackageRoot(extractPath);
                if (!IsValidPackage(packageRoot))
                {
                    throw new InvalidDataException("The OpenRA portable package is missing its server executable or Red Alert mod.");
                }

                ReplaceDirectory(packageRoot, Path.Combine(serverFilesPath, RuntimeDirectoryName));
                File.WriteAllText(Path.Combine(serverFilesPath, VersionFileName), release.Version, new UTF8Encoding(false));
            }
            catch (WebException exception)
            {
                Error = $"Failed to {action} OpenRA: {exception.Message}";
            }
            catch (JsonException exception)
            {
                Error = $"Failed to {action} OpenRA: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = $"Failed to {action} OpenRA: {exception.Message}";
            }
            catch (IOException exception)
            {
                Error = $"Failed to {action} OpenRA: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to {action} OpenRA: {exception.Message}";
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
                        Error = AppendError(Error, "Could not remove temporary OpenRA files: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary OpenRA files: " + exception.Message);
                    }
                }
            }

            return null;
        }

        public async Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!IsInstallValid())
            {
                return null;
            }

            CreateServerCFG();
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return null;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string engineDirectory = Path.Combine(serverFilesPath, RuntimeDirectoryName);
            string executablePath = GetServerExecutablePath(engineDirectory);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Error = $"OpenRA server executable was not found in ({engineDirectory}).";
                return null;
            }
            StartPath = Path.GetRelativePath(serverFilesPath, executablePath);
            string supportDirectory = Path.Combine(serverFilesPath, SupportDirectoryName);
            string parameters = $"Engine.EngineDir={QuoteCommandLineArgument(engineDirectory)} " +
                $"Engine.SupportDir={QuoteCommandLineArgument(supportDirectory)} Game.Mod=ra " +
                $"Server.Name={QuoteCommandLineArgument(_serverData.ServerName ?? string.Empty)} " +
                $"Server.ListenPort={_serverData.ServerPort}";
            string map = (_serverData.ServerMap ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(map) && !string.Equals(map, Defaultmap, StringComparison.OrdinalIgnoreCase))
            {
                parameters += " Server.Map=" + QuoteCommandLineArgument(map);
            }
            if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
            {
                parameters += " " + _serverData.ServerParam.Trim();
            }

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = engineDirectory,
                    FileName = executablePath,
                    Arguments = parameters,
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
                    Error = "OpenRA did not start a server process.";
                    process.Dispose();
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
                Error = "Failed to start OpenRA: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start OpenRA: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start OpenRA: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start OpenRA: " + exception.Message;
            }

            if (processStarted && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
            process.Dispose();
            return null;
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
                process.StandardInput.WriteLine("quit");
            }
            catch (IOException exception)
            {
                Error = "Failed to send the OpenRA shutdown command: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to send the OpenRA shutdown command: " + exception.Message;
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

        public bool IsInstallValid()
        {
            Error = string.Empty;
            string runtimeDirectory = Path.Combine(ServerPath.GetServersServerFiles(_serverData.ServerID), RuntimeDirectoryName);
            if (IsValidPackage(runtimeDirectory))
            {
                return true;
            }
            Error = $"OpenRA server executable or Red Alert mod was not found in ({runtimeDirectory}).";
            return false;
        }

        public bool IsImportValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Error = $"OpenRA package directory was not found ({path}).";
                return false;
            }

            string packageRoot = ResolvePackageRoot(path);
            bool valid = packageRoot != null && IsValidPackage(packageRoot);
            Error = valid ? string.Empty : $"OpenRA server executable or Red Alert mod was not found in ({path}).";
            return valid;
        }

        public string GetLocalBuild()
        {
            Error = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string versionPath = Path.Combine(serverFilesPath, VersionFileName);
            try
            {
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath).Trim();
                }
                string executablePath = Path.Combine(serverFilesPath, StartPath);
                if (!File.Exists(executablePath))
                {
                    Error = $"OpenRA server executable was not found ({executablePath}).";
                    return string.Empty;
                }
                string version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
                return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
            }
            catch (IOException exception)
            {
                Error = "Failed to read the local OpenRA version: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to read the local OpenRA version: " + exception.Message;
            }
            catch (Win32Exception exception)
            {
                Error = "Failed to read the local OpenRA version: " + exception.Message;
            }
            return string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            Error = string.Empty;
            try
            {
                using (WebClient webClient = CreateWebClient())
                {
                    return (await GetLatestRelease(webClient)).Version;
                }
            }
            catch (WebException exception)
            {
                Error = "Failed to get the latest OpenRA version: " + exception.Message;
            }
            catch (JsonException exception)
            {
                Error = "Failed to get the latest OpenRA version: " + exception.Message;
            }
            catch (InvalidDataException exception)
            {
                Error = "Failed to get the latest OpenRA version: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to get the latest OpenRA version: " + exception.Message;
            }
            return string.Empty;
        }

        private static WebClient CreateWebClient()
        {
            var webClient = new WebClient();
            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            return webClient;
        }

        private static async Task<ReleaseInfo> GetLatestRelease(WebClient webClient)
        {
            XDocument releaseFeed;
            try
            {
                releaseFeed = XDocument.Parse(await webClient.DownloadStringTaskAsync(ReleaseFeedUrl));
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("GitHub returned invalid OpenRA release metadata.", exception);
            }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            string version = releaseFeed.Descendants(atom + "entry")
                .Select(entry => ((string)entry.Element(atom + "title") ?? string.Empty).Trim())
                .FirstOrDefault(tag => Regex.IsMatch(tag, @"^release-\d{8}$", RegexOptions.CultureInvariant));
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException("GitHub did not publish a stable OpenRA release in its release feed.");
            }

            string expandedAssetsUrl = $"https://github.com/OpenRA/OpenRA/releases/expanded_assets/{Uri.EscapeDataString(version)}";
            string assetsHtml = await webClient.DownloadStringTaskAsync(expandedAssetsUrl);
            Match assetMatch = Regex.Match(
                assetsHtml,
                @"<a href=""(?<path>/OpenRA/OpenRA/releases/download/[^""]+/[^""]+-x64-winportable\.zip)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!assetMatch.Success)
            {
                throw new InvalidDataException($"OpenRA release {version} did not provide an x64 Windows portable package.");
            }

            string downloadUrl = "https://github.com" + assetMatch.Groups["path"].Value;
            Uri uri;
            string expectedPrefix = $"/OpenRA/OpenRA/releases/download/{version}/";
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal)
                || !uri.AbsolutePath.EndsWith("-x64-winportable.zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("GitHub returned an invalid OpenRA portable package URL.");
            }

            return new ReleaseInfo { DownloadUrl = uri, Version = version };
        }

        private static string FindPackageRoot(string extractPath)
        {
            string[] executablePaths = Directory.GetFiles(extractPath, "OpenRA.Server.exe", SearchOption.AllDirectories);
            foreach (string executablePath in executablePaths)
            {
                string extractionRoot = Path.GetFullPath(extractPath);
                string extractionRootPrefix = extractionRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                for (DirectoryInfo candidate = new DirectoryInfo(Path.GetDirectoryName(executablePath)); candidate != null; candidate = candidate.Parent)
                {
                    string candidatePath = Path.GetFullPath(candidate.FullName);
                    if (!string.Equals(candidatePath, extractionRoot, StringComparison.OrdinalIgnoreCase)
                        && !candidatePath.StartsWith(extractionRootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (IsValidPackage(candidatePath))
                    {
                        return candidatePath;
                    }

                    if (string.Equals(candidatePath, extractionRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }
            throw new InvalidDataException("The OpenRA package did not contain a server executable and Red Alert mod directory.");
        }

        private static string ResolvePackageRoot(string path)
        {
            if (IsValidPackage(path))
            {
                return path;
            }
            string nestedRuntime = Path.Combine(path, RuntimeDirectoryName);
            return IsValidPackage(nestedRuntime) ? nestedRuntime : null;
        }

        private static bool IsValidPackage(string path)
        {
            return Directory.Exists(path) &&
                (File.Exists(Path.Combine(path, ExecutableRelativePath)) || File.Exists(Path.Combine(path, "OpenRA.Server.exe"))) &&
                Directory.Exists(Path.Combine(path, "mods", "ra"));
        }

        private static string GetServerExecutablePath(string runtimeDirectory)
        {
            string executablePath = Path.Combine(runtimeDirectory, ExecutableRelativePath);
            if (File.Exists(executablePath))
            {
                return executablePath;
            }

            string rootExecutablePath = Path.Combine(runtimeDirectory, "OpenRA.Server.exe");
            return File.Exists(rootExecutablePath) ? rootExecutablePath : string.Empty;
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

        private static string QuoteCommandLineArgument(string value)
        {
            var quoted = new StringBuilder();
            quoted.Append('"');
            int backslashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashCount++;
                }
                else if (character == '"')
                {
                    quoted.Append('\\', backslashCount * 2 + 1);
                    quoted.Append(character);
                    backslashCount = 0;
                }
                else
                {
                    quoted.Append('\\', backslashCount);
                    quoted.Append(character);
                    backslashCount = 0;
                }
            }
            quoted.Append('\\', backslashCount * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static string AppendError(string currentError, string message)
        {
            return string.IsNullOrWhiteSpace(currentError) ? message : currentError + Environment.NewLine + message;
        }
    }
}