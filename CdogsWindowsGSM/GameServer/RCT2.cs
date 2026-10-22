using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class RCT2
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/OpenRCT2/OpenRCT2/releases/latest";
        private const string LatestReleasePage = "https://github.com/OpenRCT2/OpenRCT2/releases/latest";
        private const string DefaultParkDownloadUrl = "https://raw.githubusercontent.com/Kickbut101/WindowsGSM.RCT2/031a235bf1cd02eb07194b23415583a2b7665cf8/New_Scenarios/CrazyCastle.sv6";
        private const string DefaultParkFileName = "CrazyCastle.sv6";
        private const string DefaultParkSha256 = "68E62B96936F40C1DC826AD094E981B1283768403192A07D327918EFBEB4AD0E";
        private const long DefaultParkSize = 321925;
        private const string VersionFileName = "rct2_version.txt";
        private const string WindowsPortableArchiveSuffix = "-windows-portable-x64.zip";
        private readonly ServerConfig _serverData;

        private sealed class OpenRCT2Release
        {
            public Uri DownloadUrl { get; set; }
            public string FileName { get; set; }
            public string Version { get; set; }
        }

        public const string FullName = "RCT2 Dedicated Server";
        public string StartPath = "openrct2.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;

        public string Port = "11753";
        public string QueryPort = "11753";
        public string Defaultmap = DefaultParkFileName;
        public string Maxplayers = "8";
        public string Additional = string.Empty;

        public string Error;
        public string Notice;

        public RCT2(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
        }

        private static WebClient CreateWebClient()
        {
            var webClient = new WebClient();
            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            return webClient;
        }

        private static async Task<OpenRCT2Release> GetLatestRelease(WebClient webClient)
        {
            string response;
            try
            {
                response = await webClient.DownloadStringTaskAsync(LatestReleaseApi);
            }
            catch (WebException exception)
            {
                var httpResponse = exception.Response as HttpWebResponse;
                if (httpResponse == null || ((int)httpResponse.StatusCode != 403 && (int)httpResponse.StatusCode != 429))
                {
                    throw;
                }

                httpResponse.Close();
                return await GetLatestReleaseFromRedirect();
            }

            JObject release = JObject.Parse(response);
            string tag = (string)release["tag_name"];
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidDataException("GitHub did not return the latest OpenRCT2 release tag.");
            }

            JArray assets = release["assets"] as JArray;
            if (assets == null)
            {
                throw new InvalidDataException("GitHub did not return OpenRCT2 release assets.");
            }

            foreach (JToken asset in assets)
            {
                string assetName = (string)asset["name"];
                if (string.IsNullOrWhiteSpace(assetName) || !assetName.EndsWith(WindowsPortableArchiveSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string downloadUrl = (string)asset["browser_download_url"];
                Uri uri;
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !uri.IsDefaultPort ||
                    !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                    !uri.AbsolutePath.StartsWith("/OpenRCT2/OpenRCT2/releases/download/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("GitHub returned an invalid OpenRCT2 Windows download URL.");
                }

                string fileName = Path.GetFileName(uri.AbsolutePath);
                if (!string.Equals(fileName, assetName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The OpenRCT2 release archive name did not match its download URL.");
                }

                return new OpenRCT2Release
                {
                    DownloadUrl = uri,
                    FileName = fileName,
                    Version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag
                };
            }

            throw new InvalidDataException("The latest OpenRCT2 release did not include a Windows x64 portable archive.");
        }

        private static async Task<OpenRCT2Release> GetLatestReleaseFromRedirect()
        {
            var request = (HttpWebRequest)WebRequest.Create(LatestReleasePage);
            request.UserAgent = "CdogsWindowsGSM";
            request.AllowAutoRedirect = true;

            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            {
                Uri responseUri = response.ResponseUri;
                const string tagPathPrefix = "/OpenRCT2/OpenRCT2/releases/tag/";
                if (responseUri == null ||
                    !string.Equals(responseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(responseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                    !responseUri.AbsolutePath.StartsWith(tagPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("GitHub did not redirect to the latest OpenRCT2 release tag.");
                }

                string tag = Uri.UnescapeDataString(responseUri.AbsolutePath.Substring(tagPathPrefix.Length));
                if (!IsValidReleaseTag(tag))
                {
                    throw new InvalidDataException("GitHub returned an invalid OpenRCT2 release tag.");
                }

                string fileName = "OpenRCT2-" + tag + WindowsPortableArchiveSuffix;
                var downloadUrl = new Uri("https://github.com/OpenRCT2/OpenRCT2/releases/download/" +
                    Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(fileName));
                return new OpenRCT2Release
                {
                    DownloadUrl = downloadUrl,
                    FileName = fileName,
                    Version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag
                };
            }
        }

        private static bool IsValidReleaseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) { return false; }
            foreach (char character in tag)
            {
                if (!char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')
                {
                    return false;
                }
            }
            return true;
        }

        private async Task<string> DownloadAndInstallLatestRelease()
        {
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__openrct2_stage_" + Guid.NewGuid().ToString("N"));
            string downloadPath = Path.Combine(stagingPath, "download");
            string extractPath = Path.Combine(stagingPath, "extract");
            OpenRCT2Release release = null;

            Directory.CreateDirectory(stagingPath);
            bool installDefaultPark = false;
            try
            {
                installDefaultPark = !HasSavedPark(serverFilesPath);
                Directory.CreateDirectory(downloadPath);
                Directory.CreateDirectory(extractPath);

                using (WebClient webClient = CreateWebClient())
                {
                    release = await GetLatestRelease(webClient);
                    string archivePath = Path.Combine(downloadPath, release.FileName);
                    await webClient.DownloadFileTaskAsync(release.DownloadUrl, archivePath);
                    await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractPath));
                }

                string stagedDefaultParkPath = Path.Combine(downloadPath, DefaultParkFileName);
                if (installDefaultPark)
                {
                    await DownloadAndValidateDefaultPark(stagedDefaultParkPath);
                }

                string packageRoot = FindPackageRoot(extractPath);
                await Task.Run(() => CopyDirectory(packageRoot, serverFilesPath));
                if (installDefaultPark && !File.Exists(Path.Combine(serverFilesPath, DefaultParkFileName)))
                {
                    File.Move(stagedDefaultParkPath, Path.Combine(serverFilesPath, DefaultParkFileName));
                }
            }
            finally
            {
                if (Directory.Exists(stagingPath))
                {
                    Directory.Delete(stagingPath, true);
                }
            }

            if (release == null)
            {
                throw new InvalidDataException("The latest OpenRCT2 release could not be resolved.");
            }

            File.WriteAllText(ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName), release.Version);
            return release.Version;
        }

        private static bool HasSavedPark(string serverFilesPath)
        {
            return Directory.GetFiles(serverFilesPath, "*.sv6", SearchOption.AllDirectories).Length > 0;
        }

        private static async Task DownloadAndValidateDefaultPark(string destinationPath)
        {
            using (WebClient webClient = CreateWebClient())
            {
                await webClient.DownloadFileTaskAsync(new Uri(DefaultParkDownloadUrl), destinationPath);
            }

            var fileInfo = new FileInfo(destinationPath);
            if (fileInfo.Length != DefaultParkSize || !VerifySha256(destinationPath, DefaultParkSha256))
            {
                throw new InvalidDataException("The default CrazyCastle RCT2 park failed size or SHA-256 verification.");
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

        private static string FindPackageRoot(string extractPath)
        {
            foreach (string executablePath in Directory.GetFiles(extractPath, "openrct2.exe", SearchOption.AllDirectories))
            {
                return Path.GetDirectoryName(executablePath);
            }

            throw new InvalidDataException("The OpenRCT2 archive did not contain openrct2.exe.");
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            foreach (string sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourceFile.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourceFile, destinationPath, true);
            }
        }

        public async Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (!File.Exists(executablePath))
            {
                Error = $"{StartPath} was not found ({executablePath}).";
                return null;
            }

            if (!int.TryParse(_serverData.ServerPort, out int port) || port < 1 || port > 65535)
            {
                Error = "OpenRCT2 server port must be between 1 and 65535.";
                return null;
            }

            string serverParameters = _serverData.ServerParam;
            if (string.IsNullOrWhiteSpace(serverParameters))
            {
                string parkPath;
                try
                {
                    parkPath = FindDefaultParkPath(serverFilesPath);
                }
                catch (IOException exception)
                {
                    Error = "Failed to locate an OpenRCT2 saved park: " + exception.Message;
                    return null;
                }
                catch (UnauthorizedAccessException exception)
                {
                    Error = "Failed to locate an OpenRCT2 saved park: " + exception.Message;
                    return null;
                }
                catch (ArgumentException exception)
                {
                    Error = "Failed to locate an OpenRCT2 saved park: " + exception.Message;
                    return null;
                }

                if (string.IsNullOrWhiteSpace(parkPath))
                {
                    Error = "No saved RCT2 park (.sv6) was found. Import or copy a licensed park into the server files; custom server parameters are not required.";
                    return null;
                }

                serverParameters = $"host {QuoteArgument(parkPath)} --headless --port {port}";
            }

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = executablePath,
                    Arguments = serverParameters,
                    WindowStyle = ProcessWindowStyle.Minimized,
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
                    Error = "OpenRCT2 did not start a server process.";
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
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Error = $"Failed to start OpenRCT2: {ex.Message}";
                if (processStarted && !process.HasExited)
                {
                    process.Kill();
                }
                process.Dispose();
                return null;
            }
        }

        private string FindDefaultParkPath(string serverFilesPath)
        {
            string configuredMap = _serverData.ServerMap;
            if (!string.IsNullOrWhiteSpace(configuredMap))
            {
                string candidatePath = Path.IsPathRooted(configuredMap)
                    ? configuredMap
                    : Path.Combine(serverFilesPath, configuredMap);
                if (string.IsNullOrEmpty(Path.GetExtension(candidatePath)))
                {
                    candidatePath += ".sv6";
                }
                if (string.Equals(Path.GetExtension(candidatePath), ".sv6", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            string[] parkPaths = Directory.GetFiles(serverFilesPath, "*.sv6", SearchOption.AllDirectories);
            Array.Sort(parkPaths, StringComparer.OrdinalIgnoreCase);
            return parkPaths.Length > 0 ? parkPaths[0] : null;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        public async Task Stop(Process process)
        {
            if (process == null) { return; }

            await Task.Run(() =>
            {
                if (process.HasExited) { return; }

                if (Functions.ServerConsole.IsStandardInputRedirected(process))
                {
                    process.StandardInput.WriteLine("exit");
                }
                else
                {
                    process.CloseMainWindow();
                }

                if (!process.HasExited && !process.WaitForExit(10000))
                {
                    process.Kill();
                    process.WaitForExit();
                }
            });
        }

        public async Task<Process> Install()
        {
            Error = string.Empty;
            try
            {
                await DownloadAndInstallLatestRelease();
            }
            catch (Exception ex) when (ex is WebException || ex is InvalidDataException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
                ex is CryptographicException || ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                Error = $"Failed to install OpenRCT2: {ex.Message}";
            }

            return null;
        }

        public async Task<Process> Update()
        {
            Error = string.Empty;
            try
            {
                await DownloadAndInstallLatestRelease();
            }
            catch (Exception ex) when (ex is WebException || ex is InvalidDataException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
                ex is CryptographicException || ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                Error = $"Failed to update OpenRCT2: {ex.Message}";
            }

            return null;
        }

        public bool IsInstallValid()
        {
            string executablePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (File.Exists(executablePath)) { return true; }

            Error = $"{StartPath} was not found ({executablePath}).";
            return false;
        }

        public bool IsImportValid(string path)
        {
            string executablePath = Path.Combine(path, StartPath);
            if (File.Exists(executablePath)) { return true; }

            Error = $"Invalid import path; {StartPath} was not found ({executablePath}).";
            return false;
        }

        public string GetLocalBuild()
        {
            string versionPath = ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName);
            if (File.Exists(versionPath))
            {
                return File.ReadAllText(versionPath).Trim();
            }

            string executablePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (File.Exists(executablePath))
            {
                string productVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
                if (!string.IsNullOrWhiteSpace(productVersion)) { return productVersion; }
            }

            Error = $"Could not determine the local OpenRCT2 version; {VersionFileName} is missing.";
            return string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            Error = string.Empty;
            try
            {
                using (WebClient webClient = CreateWebClient())
                {
                    OpenRCT2Release release = await GetLatestRelease(webClient);
                    return release.Version;
                }
            }
            catch (Exception ex) when (ex is WebException || ex is InvalidDataException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
                ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                Error = $"Failed to get the latest OpenRCT2 version: {ex.Message}";
                return string.Empty;
            }
        }
    }
}