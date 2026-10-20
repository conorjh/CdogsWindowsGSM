using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class ForgeMC : Engine.MinecraftJava
    {
        public const string FullName = "Minecraft: Forge Server";
        private const string Maven = "https://maven.minecraftforge.net/net/minecraftforge/forge";
        protected override string GameName => FullName;
        protected override string BuildVersion(string build) => build.Split('-')[0];

        public ForgeMC(ServerConfig serverData) : base(serverData) { }

        private static string FindBuild(string directory)
        {
            string libraries = Path.Combine(directory, "libraries", "net", "minecraftforge", "forge");
            var modern = Directory.Exists(libraries) ? Directory.GetDirectories(libraries)
                .Where(path => File.Exists(Path.Combine(path, "win_args.txt")))
                .Select(Path.GetFileName) : Enumerable.Empty<string>();
            var legacy = Directory.Exists(directory) ? Directory.GetFiles(directory, "forge-*.jar")
                .Select(path => Regex.Match(Path.GetFileName(path), @"^forge-(\d+(?:\.\d+)+-\d+(?:\.\d+)+)\.jar$"))
                .Where(match => match.Success).Select(match => match.Groups[1].Value) : Enumerable.Empty<string>();
            return modern.Concat(legacy)
                .Where(build => Regex.IsMatch(build, @"^\d+(\.\d+)+-\d+(\.\d+)+$"))
                .OrderByDescending(build => ParseVersion(build.Split('-')[0]))
                .ThenByDescending(build => ParseVersion(build.Split('-')[1])).FirstOrDefault() ?? string.Empty;
        }

        private static string ArgumentsFor(string directory, string build)
        {
            if (string.IsNullOrEmpty(build)) return null;
            string args = Path.Combine("libraries", "net", "minecraftforge", "forge", build, "win_args.txt");
            if (File.Exists(Path.Combine(directory, args)))
            {
                string userArgs = File.Exists(Path.Combine(directory, "user_jvm_args.txt")) ? "@user_jvm_args.txt " : string.Empty;
                return userArgs + "@\"" + args + "\"";
            }
            string jar = "forge-" + build + ".jar";
            return File.Exists(Path.Combine(directory, jar)) ? "-jar \"" + jar + "\"" : null;
        }

        protected override string LaunchArguments => ArgumentsFor(ServerDirectory, GetLocalBuild());
        protected override bool HasServer(string directory) => ArgumentsFor(directory, FindBuild(directory)) != null;

        public override string GetLocalBuild()
        {
            string installed = ReadInstalledBuild();
            return installed.Length > 0 ? installed : FindBuild(ServerDirectory);
        }

        public override async Task<string> GetRemoteBuild()
        {
            try
            {
                using (var client = CreateClient())
                {
                    var promotions = (JObject)JObject.Parse(await client.DownloadStringTaskAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"))["promos"];
                    string local = GetLocalBuild();
                    var latest = promotions.Properties()
                        .Where(item => Regex.IsMatch(item.Name, @"^\d+(\.\d+)+-latest$"))
                        .Where(item => local.Length == 0 || BuildVersion(item.Name) == BuildVersion(local))
                        .OrderByDescending(item => ParseVersion(BuildVersion(item.Name))).FirstOrDefault();
                    if (latest == null) throw new InvalidDataException("No Forge promotion is available for this Minecraft version.");
                    return BuildVersion(latest.Name) + "-" + (string)latest.Value;
                }
            }
            catch (Exception exception)
            {
                Error = "Failed to get the Forge build: " + exception.Message;
                return string.Empty;
            }
        }

        protected override async Task InstallBuild(string build)
        {
            string staging = ServerPath.GetServersCache(ServerData.ServerID, "forge-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string logPath = ServerPath.GetServersCache(ServerData.ServerID, "forge-install.log");
            try
            {
                string installer = Path.Combine(staging, "forge-installer.jar");
                using (var client = CreateClient())
                {
                    string url = $"{Maven}/{build}/forge-{build}-installer.jar";
                    string checksum = await client.DownloadStringTaskAsync(url + ".sha256");
                    await DownloadVerified(client, url, installer, checksum);
                }
                using (var process = CreateJavaProcess("-jar \"" + installer + "\" --installServer", false))
                {
                    process.StartInfo.WorkingDirectory = staging;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.Start();
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    Task exit = Task.Run(() => process.WaitForExit());
                    if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromMinutes(15))) != exit)
                    {
                        process.Kill();
                        await exit;
                        File.WriteAllText(logPath, await output + Environment.NewLine + await errors);
                        throw new TimeoutException("Forge installation exceeded 15 minutes. See " + logPath);
                    }
                    await exit;
                    File.WriteAllText(logPath, await output + Environment.NewLine + await errors);
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException($"Forge installer exited with code {process.ExitCode}. See {logPath}");
                }
                if (ArgumentsFor(staging, build) == null)
                    throw new InvalidDataException("Forge installer did not produce a server or win_args.txt. See " + logPath);

                // Install only after Forge has finished successfully; retain worlds, configuration and older libraries.
                foreach (string source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                {
                    string relative = source.Substring(staging.Length + 1);
                    if (relative == "forge-installer.jar" || relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                    string destination = Path.Combine(ServerDirectory, relative);
                    if (relative == "user_jvm_args.txt" && File.Exists(destination)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, true);
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
    }
}
