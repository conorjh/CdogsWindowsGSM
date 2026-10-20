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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class Mindustry
    {
        private const string ReleaseApi = "https://api.github.com/repos/Anuken/Mindustry/releases/latest";
        private const string ServerJarName = "server-release.jar";
        private const string VersionFileName = "mindustry_version.txt";
        private readonly ServerConfig _serverData;

        private sealed class ReleaseInfo
        {
            public Uri DownloadUrl { get; set; }
            public string Sha256 { get; set; }
            public string Version { get; set; }
        }

        public const string FullName = "Mindustry Dedicated Server";
        public string StartPath = string.Empty;
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "6567";
        public string QueryPort = "6567";
        public string Defaultmap = "Random map";
        public string Maxplayers = "10";
        public string Additional = string.Empty;
        public string Error;
        public string Notice;

        public Mindustry(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            int port;
            int maxPlayers;
            if (_serverData == null || !int.TryParse(_serverData.ServerPort, out port) || port < 1 || port > 65535)
            {
                Error = "Mindustry server port must be between 1 and 65535.";
                return;
            }
            if (!int.TryParse(_serverData.ServerMaxPlayer, out maxPlayers) || maxPlayers < 1 || maxPlayers > 255)
            {
                Error = "Mindustry max players must be between 1 and 255.";
                return;
            }

            try
            {
                Directory.CreateDirectory(ServerPath.GetServersServerFiles(_serverData.ServerID));
            }
            catch (IOException exception)
            {
                Error = "Failed to prepare the Mindustry server directory: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to prepare the Mindustry server directory: " + exception.Message;
            }
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

            string javaPath = FindJavaExecutable();
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                Error = "Mindustry requires Java 17 or newer. Set JAVA_HOME or add a compatible java.exe to PATH.";
                return null;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            var firewall = new WindowsFirewall("java.exe", javaPath);
            if (!await firewall.IsRuleExist() && !await firewall.AddRule())
            {
                Notice = "Could not add Java to the Windows Firewall; verify the server port is allowed.";
            }

            string arguments = $"-jar \"{ServerJarName}\" {_serverData.ServerParam}".Trim();
            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = javaPath,
                    Arguments = arguments,
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
                    Error = "Mindustry did not start a server process.";
                    process.Dispose();
                    return null;
                }
                if (AllowsEmbedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                int port = int.Parse(_serverData.ServerPort);
                int maxPlayers = int.Parse(_serverData.ServerMaxPlayer);
                process.StandardInput.WriteLine("config name " + QuoteConsoleValue(_serverData.ServerName));
                process.StandardInput.WriteLine("config port " + port);
                process.StandardInput.WriteLine("playerlimit " + maxPlayers);
                string map = (_serverData.ServerMap ?? string.Empty).Trim();
                process.StandardInput.WriteLine(string.IsNullOrWhiteSpace(map) || string.Equals(map, Defaultmap, StringComparison.OrdinalIgnoreCase)
                    ? "host"
                    : "host " + QuoteConsoleValue(map));
                return process;
            }
            catch (Win32Exception exception)
            {
                Error = "Failed to start Mindustry: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start Mindustry: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start Mindustry: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start Mindustry: " + exception.Message;
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
                process.StandardInput.WriteLine("exit");
            }
            catch (IOException exception)
            {
                Error = "Failed to send the Mindustry shutdown command: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to send the Mindustry shutdown command: " + exception.Message;
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
            Notice = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__mindustry_stage_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stagingPath);
                ReleaseInfo release;
                using (WebClient webClient = CreateWebClient())
                {
                    release = await GetLatestRelease(webClient);
                    string stagedJarPath = Path.Combine(stagingPath, ServerJarName);
                    await webClient.DownloadFileTaskAsync(release.DownloadUrl, stagedJarPath);
                    if (!VerifySha256(stagedJarPath, release.Sha256))
                    {
                        throw new InvalidDataException("The Mindustry server JAR failed GitHub's published SHA-256 check.");
                    }
                    ReplaceFile(stagedJarPath, Path.Combine(serverFilesPath, ServerJarName));
                }

                File.WriteAllText(Path.Combine(serverFilesPath, VersionFileName), release.Version, new UTF8Encoding(false));
            }
            catch (WebException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
            }
            catch (JsonException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
            }
            catch (IOException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
            }
            catch (CryptographicException exception)
            {
                Error = $"Failed to {action} Mindustry: {exception.Message}";
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
                        Error = AppendError(Error, "Could not remove temporary Mindustry files: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary Mindustry files: " + exception.Message);
                    }
                }
            }

            return null;
        }

        public bool IsInstallValid()
        {
            Error = string.Empty;
            string jarPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ServerJarName);
            if (File.Exists(jarPath))
            {
                return true;
            }
            Error = $"Mindustry server JAR was not found ({jarPath}).";
            return false;
        }

        public bool IsImportValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Error = $"Mindustry server directory was not found ({path}).";
                return false;
            }

            string jarPath = Path.Combine(path, ServerJarName);
            bool valid = File.Exists(jarPath);
            Error = valid ? string.Empty : $"Mindustry server JAR was not found ({jarPath}).";
            return valid;
        }

        public string GetLocalBuild()
        {
            Error = string.Empty;
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
                Error = "Failed to read the local Mindustry version: " + exception.Message;
                return string.Empty;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to read the local Mindustry version: " + exception.Message;
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
                    return (await GetLatestRelease(webClient)).Version;
                }
            }
            catch (WebException exception)
            {
                Error = "Failed to get the latest Mindustry version: " + exception.Message;
            }
            catch (JsonException exception)
            {
                Error = "Failed to get the latest Mindustry version: " + exception.Message;
            }
            catch (InvalidDataException exception)
            {
                Error = "Failed to get the latest Mindustry version: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to get the latest Mindustry version: " + exception.Message;
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
            string normalizedVersion = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
            if (!Regex.IsMatch(normalizedVersion, @"^\d+(?:\.\d+)+$"))
            {
                throw new InvalidDataException("GitHub returned an invalid Mindustry release tag.");
            }

            JArray assets = release["assets"] as JArray;
            JToken serverAsset = null;
            if (assets != null)
            {
                foreach (JToken asset in assets)
                {
                    if (string.Equals((string)asset["name"], ServerJarName, StringComparison.Ordinal))
                    {
                        serverAsset = asset;
                        break;
                    }
                }
            }
            if (serverAsset == null)
            {
                throw new InvalidDataException("The latest Mindustry release did not include server-release.jar.");
            }

            string digest = (string)serverAsset["digest"];
            string downloadUrl = (string)serverAsset["browser_download_url"];
            Uri uri;
            if (!Regex.IsMatch(digest ?? string.Empty, @"^sha256:[a-f0-9]{64}$", RegexOptions.IgnoreCase) ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), $"/Anuken/Mindustry/releases/download/{version}/{ServerJarName}", StringComparison.Ordinal))
            {
                throw new InvalidDataException("GitHub returned invalid Mindustry download or checksum metadata.");
            }

            return new ReleaseInfo { DownloadUrl = uri, Sha256 = digest.Substring("sha256:".Length), Version = version };
        }

        private static bool VerifySha256(string filePath, string expectedHash)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                string actualHash = BitConverter.ToString(hash).Replace("-", string.Empty);
                return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string FindJavaExecutable()
        {
            var candidates = new List<string>();
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            AddJavaCandidates(candidates, uniquePaths, javaHome);

            string pathEnvironment = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathEnvironment))
            {
                foreach (string path in pathEnvironment.Split(Path.PathSeparator))
                {
                    AddJavaCandidates(candidates, uniquePaths, path);
                }
            }

            foreach (string candidate in candidates)
            {
                if (GetJavaMajorVersion(candidate) >= 17)
                {
                    return candidate;
                }
            }
            return string.Empty;
        }

        private static void AddJavaCandidates(ICollection<string> candidates, ISet<string> uniquePaths, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { return; }
            path = path.Trim().Trim('"');
            string[] possiblePaths = Directory.Exists(path)
                ? new[] { Path.Combine(path, "bin", "java.exe"), Path.Combine(path, "java.exe") }
                : new[] { path };
            foreach (string candidate in possiblePaths)
            {
                if (File.Exists(candidate) && uniquePaths.Add(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        private static int GetJavaMajorVersion(string javaPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null || !process.WaitForExit(5000))
                    {
                        if (process != null && !process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit();
                        }
                        return 0;
                    }

                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    Match match = Regex.Match(output, @"(?<version>\d+(?:\.\d+)*)");
                    if (!match.Success)
                    {
                        return 0;
                    }

                    string[] versionParts = match.Groups["version"].Value.Split('.');
                    int major;
                    if (!int.TryParse(versionParts[0], out major))
                    {
                        return 0;
                    }
                    if (major == 1 && versionParts.Length > 1 && int.TryParse(versionParts[1], out int legacyMajor))
                    {
                        return legacyMajor;
                    }
                    return major;
                }
            }
            catch (Win32Exception)
            {
                return 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
            catch (IOException)
            {
                return 0;
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

        private static string QuoteConsoleValue(string value)
        {
            value = (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
            return "\"" + value + "\"";
        }

        private static string AppendError(string currentError, string message)
        {
            return string.IsNullOrWhiteSpace(currentError) ? message : currentError + Environment.NewLine + message;
        }
    }
}