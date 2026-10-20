using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class RCT2
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/OpenRCT2/OpenRCT2/releases/latest";
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
        public string Defaultmap = "DefaultWorld";
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
            string response = await webClient.DownloadStringTaskAsync(LatestReleaseApi);
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

        private async Task<string> DownloadAndInstallLatestRelease()
        {
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__openrct2_stage_" + Guid.NewGuid().ToString("N"));
            string downloadPath = Path.Combine(stagingPath, "download");
            string extractPath = Path.Combine(stagingPath, "extract");
            OpenRCT2Release release = null;

            Directory.CreateDirectory(stagingPath);
            try
            {
                Directory.CreateDirectory(downloadPath);
                Directory.CreateDirectory(extractPath);

                using (WebClient webClient = CreateWebClient())
                {
                    release = await GetLatestRelease(webClient);
                    string archivePath = Path.Combine(downloadPath, release.FileName);
                    await webClient.DownloadFileTaskAsync(release.DownloadUrl, archivePath);
                    await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractPath));
                }

                string packageRoot = FindPackageRoot(extractPath);
                await Task.Run(() => CopyDirectory(packageRoot, serverFilesPath));
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
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (!File.Exists(executablePath))
            {
                Error = $"{StartPath} was not found ({executablePath}).";
                return null;
            }

            if (string.IsNullOrWhiteSpace(_serverData.ServerParam))
            {
                Error = "OpenRCT2 server parameters are required, for example: host \"scenario.sv6\" --headless.";
                return null;
            }

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = executablePath,
                    Arguments = _serverData.ServerParam,
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
            catch (Exception ex)
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

        public async Task Stop(Process process)
        {
            if (process == null) { return; }

            await Task.Run(() =>
            {
                if (process.HasExited) { return; }

                if (process.StartInfo.RedirectStandardInput)
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
            catch (Exception ex)
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
            catch (Exception ex)
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
            catch (Exception ex)
            {
                Error = $"Failed to get the latest OpenRCT2 version: {ex.Message}";
                return string.Empty;
            }
        }
    }
}