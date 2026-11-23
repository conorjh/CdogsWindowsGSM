using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsGSM.GameServer
{
    class MCPE
    {
        private const string BuildInfoUrl = "https://github.com/pmmp/PocketMine-MP/releases/latest/download/build_info.json";
        private const string BuildInfoFileName = "PocketMine-MP-build.json";
        private static readonly Regex SerializedBuildNumberPattern = new Regex(
            @"s:5:""build"";(?:i:(\d+);|s:\d+:""(\d+)"";)",
            RegexOptions.CultureInvariant);

        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Minecraft: Pocket Edition Server (PocketMine-MP)";
        public string StartPath = @"bin\php\php.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.UT3();

        public string Port = "19132";
        public string QueryPort = "19132";
        public string Defaultmap = "world";
        public string Maxplayers = "20";
        public string Additional = string.Empty;

        public MCPE(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG()
        {
            //Download server.properties
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.properties");
            if (await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{hostname}}", _serverData.ServerName);
                configText = configText.Replace("{{rcon_password}}", _serverData.GetRCONPassword());
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                configText = configText.Replace("{{maxplayers}}", Maxplayers);
                File.WriteAllText(configPath, configText);
            }
        }

        public async Task<Process> Start()
        {
            string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);

            string phpPath = Path.Combine(workingDir, @"bin\php\php.exe");
            if (!File.Exists(phpPath))
            {
                Error = $"php.exe not found ({phpPath})";
                return null;
            }

            string PMMPPath = Path.Combine(workingDir, "PocketMine-MP.phar");
            if (!File.Exists(PMMPPath))
            {
                Error = $"PocketMine-MP.phar not found ({PMMPPath})";
                return null;
            }

            string serverConfigPath = Path.Combine(workingDir, "server.properties");
            if (!File.Exists(serverConfigPath))
            {
                Error = $"server.properties not found ({serverConfigPath})";
                return null;
            }

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir,
                        FileName = phpPath,
                        Arguments = $@"-c bin\php PocketMine-MP.phar {_serverData.ServerParam}".TrimEnd(),
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
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
                        FileName = phpPath,
                        Arguments = $@"-c bin\php PocketMine-MP.phar {_serverData.ServerParam}".TrimEnd(),
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

            return p;
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
            string serverFilesPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
            string phpZipPath = Path.Combine(serverFilesPath, "PHP-Windows-x64-PM5.zip");
            string buildInfoPath = Path.Combine(serverFilesPath, BuildInfoFileName);
            try
            {
                using (WebClient webClient = new WebClient())
                using (JsonDocument buildInfoDocument = await DownloadBuildInfoAsync(webClient))
                {
                    JsonElement buildInfo = buildInfoDocument.RootElement;
                    string buildInfoJson = buildInfo.GetRawText();
                    string phpDownloadUrl = GetPhpRuntimeDownloadUrl(buildInfo);
                    string pocketMineDownloadUrl = GetPocketMineDownloadUrl(buildInfo);

                    await webClient.DownloadFileTaskAsync(phpDownloadUrl, phpZipPath);
                    await Task.Run(() => ZipFile.ExtractToDirectory(phpZipPath, serverFilesPath));
                    File.Delete(phpZipPath);
                    await webClient.DownloadFileTaskAsync(
                        pocketMineDownloadUrl,
                        Path.Combine(serverFilesPath, "PocketMine-MP.phar"));
                    File.WriteAllText(buildInfoPath, buildInfoJson);
                }
            }
            catch (WebException ex)
            {
                Error = $"Failed to download PocketMine-MP installation files: {ex.Message}";
                return null;
            }
            catch (JsonException ex)
            {
                Error = $"Failed to parse PocketMine-MP build metadata: {ex.Message}";
                return null;
            }
            catch (IOException ex)
            {
                Error = $"Failed to install PocketMine-MP files: {ex.Message}";
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to write PocketMine-MP files: {ex.Message}";
                return null;
            }

            return null;
        }

        public async Task<Process> Update()
        {
            string pocketMinePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "PocketMine-MP.phar");
            string stagedPocketMinePath = pocketMinePath + ".download";
            string buildInfoPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, BuildInfoFileName);
            string stagedBuildInfoPath = buildInfoPath + ".download";
            try
            {
                string buildInfoJson;
                using (WebClient webClient = new WebClient())
                using (JsonDocument buildInfoDocument = await DownloadBuildInfoAsync(webClient))
                {
                    JsonElement buildInfo = buildInfoDocument.RootElement;
                    string pocketMineDownloadUrl = GetPocketMineDownloadUrl(buildInfo);
                    buildInfoJson = buildInfo.GetRawText();
                    await webClient.DownloadFileTaskAsync(pocketMineDownloadUrl, stagedPocketMinePath);
                }

                File.WriteAllText(stagedBuildInfoPath, buildInfoJson);
                File.Move(stagedPocketMinePath, pocketMinePath, overwrite: true);
                File.Move(stagedBuildInfoPath, buildInfoPath, overwrite: true);
            }
            catch (WebException ex)
            {
                Error = $"Failed to download PocketMine-MP update: {ex.Message}";
                return null;
            }
            catch (JsonException ex)
            {
                Error = $"Failed to parse PocketMine-MP build metadata: {ex.Message}";
                return null;
            }
            catch (IOException ex)
            {
                Error = $"Failed to update PocketMine-MP files: {ex.Message}";
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to write PocketMine-MP files: {ex.Message}";
                return null;
            }

            return null;
        }

        public bool IsInstallValid()
        {
            string PHPPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"bin\php\php.exe");
            string PMMPPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "PocketMine-MP.phar");
            return File.Exists(PHPPath) && File.Exists(PMMPPath);
        }

        public bool IsImportValid(string path)
        {
            string PMMPFile = "PocketMine-MP.phar";
            string PMMPPath = Path.Combine(path, PMMPFile);

            Error = $"Invalid Path! Fail to find {PMMPFile}";
            return File.Exists(PMMPPath);
        }

        public string GetLocalBuild()
        {
            string PMMPFile = "PocketMine-MP.phar";
            string PMMPPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, PMMPFile);

            if (!File.Exists(PMMPPath))
            {
                Error = $"{PMMPFile} is missing.";
                return string.Empty;
            }

            string buildInfoPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, BuildInfoFileName);
            if (File.Exists(buildInfoPath))
            {
                try
                {
                    using JsonDocument buildInfoDocument = JsonDocument.Parse(File.ReadAllText(buildInfoPath));
                    return GetBuildNumber(buildInfoDocument.RootElement);
                }
                catch (JsonException ex)
                {
                    Error = $"Failed to parse installed PocketMine-MP build metadata: {ex.Message}";
                    return string.Empty;
                }
                catch (IOException ex)
                {
                    Error = $"Failed to read installed PocketMine-MP build metadata: {ex.Message}";
                    return string.Empty;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Error = $"Failed to read installed PocketMine-MP build metadata: {ex.Message}";
                    return string.Empty;
                }
            }

            try
            {
                string pharContents = Encoding.ASCII.GetString(File.ReadAllBytes(PMMPPath));
                Match serializedBuildNumber = SerializedBuildNumberPattern.Match(pharContents);
                if (serializedBuildNumber.Success)
                {
                    return serializedBuildNumber.Groups[1].Success
                        ? serializedBuildNumber.Groups[1].Value
                        : serializedBuildNumber.Groups[2].Value;
                }

                using (StreamReader sr = File.OpenText(PMMPPath))
                {
                    string s = string.Empty;
                    while ((s = sr.ReadLine()) != null)
                    {
                        if (s.Contains("const BUILD_NUMBER"))
                        {
                            Regex regex = new Regex("\\d+");
                            return regex.Match(s).Value;
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                Error = $"Failed to read local PocketMine-MP build: {ex.Message}";
                return string.Empty;
            }
            catch (UnauthorizedAccessException ex)
            {
                Error = $"Failed to read local PocketMine-MP build: {ex.Message}";
                return string.Empty;
            }

            Error = "PocketMine-MP build metadata is unavailable; update the server to refresh it.";
            return string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            try
            {
                using (WebClient webClient = new WebClient())
                using (JsonDocument buildInfoDocument = await DownloadBuildInfoAsync(webClient))
                {
                    return GetBuildNumber(buildInfoDocument.RootElement);
                }
            }
            catch (WebException ex)
            {
                Error = $"Failed to download PocketMine-MP build metadata: {ex.Message}";
            }
            catch (JsonException ex)
            {
                Error = $"Failed to parse PocketMine-MP build metadata: {ex.Message}";
            }
            catch (IOException ex)
            {
                Error = $"Failed to read PocketMine-MP build metadata: {ex.Message}";
            }

            return string.Empty;
        }

        private static async Task<JsonDocument> DownloadBuildInfoAsync(WebClient webClient)
        {
            string buildInfoJson = await webClient.DownloadStringTaskAsync(BuildInfoUrl);
            return JsonDocument.Parse(buildInfoJson);
        }

        private static string GetBuildNumber(JsonElement buildInfo)
        {
            if (!buildInfo.TryGetProperty("build", out JsonElement buildNumber)
                || (buildNumber.ValueKind != JsonValueKind.Number && buildNumber.ValueKind != JsonValueKind.String)
                || string.IsNullOrWhiteSpace(buildNumber.ToString()))
            {
                throw new InvalidDataException("PocketMine-MP build metadata does not contain a valid build number.");
            }

            return buildNumber.ToString();
        }

        private static string GetPocketMineDownloadUrl(JsonElement buildInfo)
        {
            string downloadUrl = GetRequiredBuildInfoString(buildInfo, "download_url");
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith("/pmmp/PocketMine-MP/releases/download/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("PocketMine-MP build metadata contains an invalid download URL.");
            }

            return uri.AbsoluteUri;
        }

        private static string GetPhpRuntimeDownloadUrl(JsonElement buildInfo)
        {
            string phpVersion = GetRequiredBuildInfoString(buildInfo, "php_version");
            if (!Regex.IsMatch(phpVersion, @"^\d+\.\d+$", RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException("PocketMine-MP build metadata contains an invalid PHP version.");
            }

            string phpReleaseUrl = GetRequiredBuildInfoString(buildInfo, "php_download_url");
            Match releaseTagMatch = Regex.Match(
                phpReleaseUrl,
                @"^https://github\.com/pmmp/PHP-Binaries/releases/tag/([^/?#]+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!releaseTagMatch.Success)
            {
                throw new InvalidDataException("PocketMine-MP build metadata contains an invalid PHP release URL.");
            }

            string releaseTag = Uri.UnescapeDataString(releaseTagMatch.Groups[1].Value);
            return $"https://github.com/pmmp/PHP-Binaries/releases/download/{Uri.EscapeDataString(releaseTag)}/PHP-{phpVersion}-Windows-x64-PM5.zip";
        }

        private static string GetRequiredBuildInfoString(JsonElement buildInfo, string propertyName)
        {
            if (!buildInfo.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException($"PocketMine-MP build metadata does not contain a valid '{propertyName}' value.");
            }

            return value.GetString();
        }
    }
}
