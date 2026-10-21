using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;
using WindowsGSM.Localization;

namespace WindowsGSM.GameServer.Engine
{
    abstract class MinecraftJava
    {
        protected readonly ServerConfig ServerData;
        protected string ServerDirectory => ServerPath.GetServersServerFiles(ServerData.ServerID);
        protected abstract string GameName { get; }
        protected abstract string BuildVersion(string build);
        protected abstract string LaunchArguments { get; }
        protected abstract bool HasServer(string directory);
        protected virtual int RequiredJava(string version, int vanillaMajor) => vanillaMajor;
        protected abstract Task InstallBuild(string build);
        public abstract string GetLocalBuild();
        public abstract Task<string> GetRemoteBuild();

        public string Error;
        public string Notice;
        public string StartPath = string.Empty;
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.UT3();
        public string Port = "25565";
        public string QueryPort = "25565";
        public string Defaultmap = "world";
        public string Maxplayers = "20";
        public string Additional = "-Xms1G -Xmx2G";

        protected MinecraftJava(ServerConfig serverData) => ServerData = serverData;

        protected WebClient CreateClient()
        {
            var client = new WebClient { Encoding = Encoding.UTF8 };
            client.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM/1.0 (https://github.com/WindowsGSM/WindowsGSM)";
            return client;
        }

        protected static Version ParseVersion(string version)
        {
            return Version.Parse(version.Contains(".") ? version : version + ".0");
        }

        protected string ReadInstalledBuild()
        {
            string path = Path.Combine(ServerDirectory, ".windowsgsm-build");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }

        public void CreateServerCFG()
        {
            var config = new StringBuilder();
            config.AppendLine($"motd={EscapeProperty(ServerData.ServerName)}");
            config.AppendLine($"server-ip={ServerData.ServerIP}");
            config.AppendLine($"server-port={ServerData.ServerPort}");
            config.AppendLine($"max-players={ServerData.ServerMaxPlayer}");
            config.AppendLine($"level-name={EscapeProperty(ServerData.ServerMap)}");
            config.AppendLine("enable-query=true");
            config.AppendLine($"query.port={ServerData.ServerQueryPort}");
            config.AppendLine("enable-rcon=true");
            config.AppendLine($"rcon.port={int.Parse(ServerData.ServerPort) + 10}");
            config.AppendLine($"rcon.password={EscapeProperty(ServerData.GetRCONPassword())}");
            File.WriteAllText(Path.Combine(ServerDirectory, "server.properties"), config.ToString(), Encoding.ASCII);
        }

        private static string EscapeProperty(string value)
        {
            var result = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (character == '\\') result.Append("\\\\");
                else if (character < 32 || character > 126) result.Append("\\u" + ((int)character).ToString("x4"));
                else result.Append(character);
            }
            return result.ToString();
        }

        public async Task<Process> Install()
        {
            Error = null;
            if (!await UI.CreateYesNoPromptV1(LocalizationManager.Get("MinecraftEulaTitle"), LocalizationManager.Get("MinecraftJavaEulaPrompt"), LocalizationManager.Get("MinecraftAgree"), LocalizationManager.Get("MinecraftDecline")))
            {
                Error = LocalizationManager.Get("MinecraftEulaDeclined");
                return null;
            }
            return await InstallOrUpdate(true);
        }

        public Task<Process> Update() => InstallOrUpdate(false);

        private async Task<Process> InstallOrUpdate(bool installing)
        {
            Error = null;
            try
            {
                string build = await GetRemoteBuild();
                if (string.IsNullOrWhiteSpace(build))
                    throw new InvalidOperationException(Error ?? "No compatible server build is available.");

                await EnsureJava(BuildVersion(build));
                await InstallBuild(build);
                if (!HasServer(ServerDirectory))
                    throw new InvalidDataException("The installer did not produce a runnable server.");

                File.WriteAllText(Path.Combine(ServerDirectory, ".windowsgsm-build"), build);
                if (installing)
                    File.WriteAllText(Path.Combine(ServerDirectory, "eula.txt"), "# https://www.minecraft.net/eula\neula=true\n");
            }
            catch (Exception exception)
            {
                Error = $"Failed to {(installing ? "install" : "update")} {GameName}: {exception.Message}";
            }
            return null;
        }

        protected string JavaPath;

        private async Task EnsureJava(string minecraftVersion)
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new PlatformNotSupportedException("These Minecraft servers require 64-bit Windows.");

            using (var client = CreateClient())
            {
                string metadataPath = ServerPath.GetServersCache(ServerData.ServerID, "minecraft-" + minecraftVersion + ".json");
                JObject metadata;
                if (File.Exists(metadataPath)) metadata = JObject.Parse(File.ReadAllText(metadataPath));
                else
                {
                    var manifest = JObject.Parse(await client.DownloadStringTaskAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
                    var version = manifest["versions"].FirstOrDefault(item => (string)item["id"] == minecraftVersion);
                    if (version == null)
                        throw new InvalidDataException($"Minecraft version {minecraftVersion} is not in Mojang's version manifest.");
                    metadata = JObject.Parse(await client.DownloadStringTaskAsync((string)version["url"]));
                    Directory.CreateDirectory(Path.GetDirectoryName(metadataPath));
                    File.WriteAllText(metadataPath, metadata.ToString());
                }
                int major = RequiredJava(minecraftVersion, (int?)metadata["javaVersion"]?["majorVersion"] ?? 8);
                string runtime = ServerPath.GetServersCache(ServerData.ServerID, "java-" + major);
                JavaPath = FindRuntime(runtime);
                if (JavaPath != null) return;

                var assets = JArray.Parse(await client.DownloadStringTaskAsync($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows"));
                var package = assets.FirstOrDefault()?["binary"]?["package"];
                if (package == null)
                    throw new InvalidDataException($"No Windows x64 Java {major} runtime is available from Adoptium.");

                string staging = runtime + "-" + Guid.NewGuid().ToString("N");
                string archive = staging + ".zip";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(runtime));
                    await DownloadVerified(client, (string)package["link"], archive, (string)package["checksum"]);
                    ZipFile.ExtractToDirectory(archive, staging);
                    if (FindRuntime(staging) == null)
                        throw new InvalidDataException("The Java archive does not contain bin\\java.exe.");
                    if (Directory.Exists(runtime))
                        throw new InvalidDataException($"Incomplete Java runtime at {runtime}. Remove that cache directory and retry.");
                    Directory.Move(staging, runtime);
                    JavaPath = FindRuntime(runtime);
                }
                finally
                {
                    if (File.Exists(archive)) File.Delete(archive);
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
            }
        }

        private static string FindRuntime(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.EnumerateFiles(directory, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase));
        }

        protected static async Task DownloadVerified(WebClient client, string url, string destination, string sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256))
                throw new InvalidDataException("The download metadata is missing its SHA-256 checksum.");
            await client.DownloadFileTaskAsync(url, destination);
            using (var stream = File.OpenRead(destination))
            using (var hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!actual.Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Download checksum mismatch: " + Path.GetFileName(destination));
            }
        }

        protected Process CreateJavaProcess(string arguments, bool embed)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = JavaPath,
                    WorkingDirectory = ServerDirectory,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = embed,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    RedirectStandardInput = embed,
                    RedirectStandardOutput = embed,
                    RedirectStandardError = embed
                },
                EnableRaisingEvents = true
            };
            if (embed)
            {
                var console = new ServerConsole(ServerData.ServerID);
                process.OutputDataReceived += console.AddOutput;
                process.ErrorDataReceived += console.AddOutput;
            }
            return process;
        }

        public async Task<Process> Start()
        {
            Error = null;
            try
            {
                if (!HasServer(ServerDirectory))
                    throw new FileNotFoundException("No runnable Minecraft server was found.");
                string build = GetLocalBuild();
                if (string.IsNullOrWhiteSpace(build))
                    throw new InvalidDataException("Cannot determine the installed Minecraft version.");
                await EnsureJava(BuildVersion(build));
                var firewall = new WindowsFirewall("java.exe", JavaPath);
                if (!await firewall.IsRuleExist()) await firewall.AddRule();
                var process = CreateJavaProcess($"{ServerData.ServerParam} {LaunchArguments} nogui", AllowsEmbedConsole);
                process.Start();
                if (AllowsEmbedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                return process;
            }
            catch (Exception exception)
            {
                Error = $"Failed to start {GameName}: {exception.Message}";
                return null;
            }
        }

        public Task Stop(Process process)
        {
            if (process.StartInfo.RedirectStandardInput) process.StandardInput.WriteLine("stop");
            else ServerConsole.SendMessageToMainWindow(process.MainWindowHandle, "stop");
            return Task.CompletedTask;
        }

        public bool IsInstallValid() => string.IsNullOrEmpty(Error) && HasServer(ServerDirectory);

        public bool IsImportValid(string path)
        {
            bool valid = HasServer(path);
            Error = valid ? null : "No runnable Minecraft server was found in " + path;
            return valid;
        }
    }
}
