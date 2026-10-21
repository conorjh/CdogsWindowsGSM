using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;
using WindowsGSM.Localization;

namespace WindowsGSM.GameServer
{
    class MCBE
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
            //Download server.properties
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.properties");
            if (await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{server-name}}", _serverData.ServerName);
                configText = configText.Replace("{{max-players}}", Maxplayers);
                string tempPort = _serverData.ServerPort;
                configText = configText.Replace("{{server-port}}", tempPort);
                configText = configText.Replace("{{server-portv6}}", (int.Parse(tempPort)+1).ToString());
                configText = configText.Replace("{{level-name}}", Defaultmap);
                File.WriteAllText(configPath, configText);
            }
        }

        public async Task<Process> Start()
        {
            string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);

            string exePath = Path.Combine(workingDir, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{Path.GetFileName(exePath)} not found ({exePath})";
                return null;
            }

            string serverConfigPath = Path.Combine(workingDir, "server.properties");
            if (!File.Exists(serverConfigPath))
            {
                Error = $"{Path.GetFileName(serverConfigPath)} not found ({serverConfigPath})";
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

            return p;
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p.StartInfo.RedirectStandardInput)
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
            //EULA and Privacy Policy
            MessageBoxResult result = System.Windows.MessageBox.Show(LocalizationManager.Get("MinecraftBedrockEulaPrivacyPrompt"), LocalizationManager.Get("MinecraftEulaTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                Error = LocalizationManager.Get("MinecraftBedrockEulaDeclined");
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
            catch (Exception ex)
            {
                Error = $"Failed to install Minecraft Bedrock server: {ex.Message}";
            }

            return null;
        }

        public async Task<Process> Update()
        {
            try
            {
                using (WebClient webClient = new MCBEWebclient())
                {
                    BedrockServerDownload download = await GetLatestBedrockServerDownload(webClient);

                    string tempPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "__temp");

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

                    string serverFilesPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);

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
                    File.WriteAllText(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "MCBE-version.txt"), download.Version);
                }

                return null;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(Path.Combine(path, StartPath));
        }

        public string GetLocalBuild()
        {
            string versionPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "MCBE-version.txt");
            if (File.Exists(versionPath))
            {
                return File.ReadAllText(versionPath);
            }
            else { 
                Error = $"Fail to get local build"; 
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
