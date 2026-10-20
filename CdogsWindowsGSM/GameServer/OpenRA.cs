using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class OpenRA
    {
        private const string ReleaseApi = "https://api.github.com/repos/OpenRA/OpenRA/releases/latest";
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
            string executablePath = Path.Combine(engineDirectory, ExecutableRelativePath);
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

            var firewall = new WindowsFirewall("OpenRA.Server.exe", executablePath);
            if (!await firewall.IsRuleExist() && !await firewall.AddRule())
            {
                Notice = "Could not add OpenRA to the Windows Firewall; verify the server port is allowed.";
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
            webClient.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return webClient;
        }

        private static async Task<ReleaseInfo> GetLatestRelease(WebClient webClient)
        {
            JObject release = JObject.Parse(await webClient.DownloadStringTaskAsync(ReleaseApi));
            string version = ((string)release["tag_name"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("GitHub returned an invalid OpenRA release tag.");
            }

            JArray assets = release["assets"] as JArray;
            JToken packageAsset = null;
            if (assets != null)
            {
                foreach (JToken asset in assets)
                {
                    string name = (string)asset["name"];
                    if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("OpenRA-", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith("-x64-winportable.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        packageAsset = asset;
                        break;
                    }
                }
            }
            if (packageAsset == null)
            {
                throw new InvalidDataException("The latest OpenRA release did not include the x64 Windows portable package.");
            }

            string fileName = (string)packageAsset["name"];
            string downloadUrl = (string)packageAsset["browser_download_url"];
            Uri uri;
            string expectedPath = $"/OpenRA/OpenRA/releases/download/{version}/{fileName}";
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), expectedPath, StringComparison.Ordinal))
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
                string binDirectory = Path.GetDirectoryName(executablePath);
                if (string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase))
                {
                    return Directory.GetParent(binDirectory).FullName;
                }
            }
            throw new InvalidDataException("The OpenRA package did not contain bin\\OpenRA.Server.exe.");
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
                File.Exists(Path.Combine(path, ExecutableRelativePath)) &&
                Directory.Exists(Path.Combine(path, "mods", "ra"));
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