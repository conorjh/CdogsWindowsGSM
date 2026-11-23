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
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class Mindustry
    {
        private const string ReleaseFeedUrl = "https://github.com/Anuken/Mindustry/releases.atom";
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
                try
                {
                    javaPath = await Engine.MinecraftJava.EnsureJavaRuntimeAsync(17);
                }
                catch (Exception exception) when (
                    exception is WebException
                    || exception is JsonException
                    || exception is InvalidDataException
                    || exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is CryptographicException
                    || exception is TaskCanceledException
                    || exception is NotSupportedException
                    || exception is ArgumentException)
                {
                    Error = $"Failed to prepare Mindustry's Java 17 runtime: {exception.Message}";
                    return null;
                }
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);

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
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            var serverConsole = new ServerConsole(_serverData.ServerID);
            process.OutputDataReceived += serverConsole.AddOutput;
            process.ErrorDataReceived += serverConsole.AddOutput;

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
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

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
                throw new InvalidDataException("GitHub returned invalid Mindustry release metadata.", exception);
            }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            string releaseTag = string.Empty;
            foreach (XElement entry in releaseFeed.Descendants(atom + "entry"))
            {
                string releaseUrl = (string)entry.Element(atom + "link")?.Attribute("href");
                if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out Uri releaseUri)
                    || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = Path.GetFileName(releaseUri.AbsolutePath);
                string normalizedCandidate = candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? candidate.Substring(1)
                    : candidate;
                if (Regex.IsMatch(normalizedCandidate, @"^\d+(?:\.\d+)+$", RegexOptions.CultureInvariant))
                {
                    releaseTag = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(releaseTag))
            {
                throw new InvalidDataException("GitHub did not publish a stable Mindustry release in its release feed.");
            }

            string assetsUrl = $"https://github.com/Anuken/Mindustry/releases/expanded_assets/{Uri.EscapeDataString(releaseTag)}";
            string assetsHtml = await webClient.DownloadStringTaskAsync(assetsUrl);
            Match assetMatch = Regex.Match(
                assetsHtml,
                @"<li\b[^>]*>[\s\S]*?<a href=""(?<url>/Anuken/Mindustry/releases/download/[^""]+/server-release\.jar)""[\s\S]*?<span[^>]*class=""Truncate-text""[^>]*>sha256:(?<sha256>[a-f0-9]{64})</span>[\s\S]*?</li>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!assetMatch.Success)
            {
                throw new InvalidDataException($"Mindustry release {releaseTag} did not expose a server-release.jar SHA-256 digest.");
            }

            string downloadUrl = "https://github.com" + assetMatch.Groups["url"].Value;
            Uri downloadUri;
            string expectedPath = $"/Anuken/Mindustry/releases/download/{releaseTag}/{ServerJarName}";
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Uri.UnescapeDataString(downloadUri.AbsolutePath), expectedPath, StringComparison.Ordinal))
            {
                throw new InvalidDataException("GitHub returned an invalid Mindustry server JAR URL.");
            }

            string version = releaseTag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? releaseTag.Substring(1)
                : releaseTag;
            return new ReleaseInfo
            {
                DownloadUrl = downloadUri,
                Sha256 = assetMatch.Groups["sha256"].Value,
                Version = version
            };
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