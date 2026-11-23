using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class MC : Engine.MinecraftJava
    {
        private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        public const string FullName = "Minecraft: Java Edition Server";
        protected override string GameName => FullName;
        protected override string BuildVersion(string build) => build;
        protected override string LaunchArguments => "-jar server.jar";
        protected override bool HasServer(string directory) => File.Exists(Path.Combine(directory, "server.jar"));

        public MC(ServerConfig serverData) : base(serverData)
        {
            Additional = "-Xmx1024M -Xms1024M";
        }

        public override async void CreateServerCFG()
        {
            //Create server.properties
            string configPath = ServerPath.GetServersServerFiles(ServerData.ServerID, "server.properties");
            if (await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{serverPort}}", ServerData.ServerPort);
                configText = configText.Replace("{{maxplayers}}", Maxplayers);
                configText = configText.Replace("{{rconPort}}", (int.Parse(ServerData.ServerPort) + 10).ToString());
                configText = configText.Replace("{{serverIP}}", ServerData.ServerIP);
                configText = configText.Replace("{{defaultmap}}", Defaultmap);
                configText = configText.Replace("{{rcon_password}}", ServerData.GetRCONPassword());
                configText = configText.Replace("{{serverName}}", ServerData.ServerName);
                File.WriteAllText(configPath, configText);
            }
        }

        protected override async Task InstallBuild(string build)
        {
            Directory.CreateDirectory(ServerDirectory);
            using (WebClient webClient = CreateClient())
            {
                JObject manifest = JObject.Parse(await webClient.DownloadStringTaskAsync(VersionManifestUrl));
                JToken version = manifest["versions"]?.FirstOrDefault(
                    candidate => string.Equals((string)candidate["id"], build, StringComparison.Ordinal));
                if (version == null)
                {
                    throw new InvalidDataException($"Minecraft release {build} was not found in Mojang's version manifest.");
                }

                string metadataUrl = (string)version["url"];
                if (string.IsNullOrWhiteSpace(metadataUrl))
                {
                    throw new InvalidDataException($"Mojang's version manifest did not provide metadata for Minecraft {build}.");
                }

                JObject metadata = JObject.Parse(await webClient.DownloadStringTaskAsync(metadataUrl));
                JObject serverDownload = metadata["downloads"]?["server"] as JObject;
                if (serverDownload == null)
                {
                    throw new InvalidDataException($"Mojang did not provide a server JAR for Minecraft {build}.");
                }

                string downloadUrl = (string)serverDownload["url"];
                string expectedSha1 = (string)serverDownload["sha1"];
                if (string.IsNullOrWhiteSpace(downloadUrl)
                    || !Regex.IsMatch(expectedSha1 ?? string.Empty, @"^[0-9a-f]{40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    throw new InvalidDataException($"Mojang's metadata for Minecraft {build} is missing the server JAR URL or SHA-1 checksum.");
                }

                string serverJarPath = Path.Combine(ServerDirectory, "server.jar");
                string stagedPath = serverJarPath + ".download";
                try
                {
                    await webClient.DownloadFileTaskAsync(downloadUrl, stagedPath);
                    using (SHA1 sha1 = SHA1.Create())
                    using (FileStream stream = File.OpenRead(stagedPath))
                    {
                        string actualSha1 = BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", string.Empty);
                        if (!actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException($"Minecraft {build} server JAR failed its SHA-1 checksum.");
                        }
                    }

                    File.Move(stagedPath, serverJarPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(stagedPath)) File.Delete(stagedPath);
                }
            }
        }

        public override string GetLocalBuild()
        {
            string installedBuild = ReadInstalledBuild();
            if (!string.IsNullOrWhiteSpace(installedBuild))
            {
                return installedBuild;
            }

            string logPath = ServerPath.GetServersServerFiles(ServerData.ServerID, "logs", "latest.log");

            if (!File.Exists(logPath))
            {
                Error = "Minecraft latest.log is missing and no installed build metadata is available.";
                return string.Empty;
            }

            try
            {
                using (FileStream fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader streamReader = new StreamReader(fileStream))
                {
                    while (!streamReader.EndOfStream)
                    {
                        string line = streamReader.ReadLine();
                        Match versionMatch = Regex.Match(line, @"Starting minecraft server version (\d+(?:\.\d+)+)");
                        if (versionMatch.Success)
                        {
                            return versionMatch.Groups[1].Value;
                        }
                    }
                }
            }
            catch (IOException exception)
            {
                Error = $"Failed to read Minecraft latest.log: {exception.Message}";
                return string.Empty;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to read Minecraft latest.log: {exception.Message}";
                return string.Empty;
            }

            Error = "Could not determine the installed Minecraft release from build metadata or latest.log.";
            return string.Empty;
        }

        public override async Task<string> GetRemoteBuild()
        {
            try
            {
                using (WebClient webClient = CreateClient())
                {
                    JObject manifest = JObject.Parse(await webClient.DownloadStringTaskAsync(VersionManifestUrl));
                    string release = (string)manifest["latest"]?["release"];
                    if (!string.IsNullOrWhiteSpace(release))
                    {
                        return release;
                    }

                    throw new InvalidDataException("Mojang's version manifest does not contain a latest release.");
                }
            }
            catch (WebException exception)
            {
                Error = $"Failed to download the Minecraft version manifest: {exception.Message}";
            }
            catch (JsonException exception)
            {
                Error = $"Failed to parse the Minecraft version manifest: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = exception.Message;
            }

            return string.Empty;
        }
    }
}
