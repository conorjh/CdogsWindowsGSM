using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class PaperMC : Engine.MinecraftJava
    {
        public const string FullName = "Minecraft: Paper Server";
        private const string Api = "https://fill.papermc.io/v3/projects/paper";
        protected override string GameName => FullName;
        protected override string BuildVersion(string build) => build.Split('/')[0];
        protected override string LaunchArguments => "-jar paper.jar";

        public PaperMC(ServerConfig serverData) : base(serverData) { }

        protected override int RequiredJava(string version, int vanillaMajor)
        {
            var number = ParseVersion(version);
            int paperMajor = number >= new Version(26, 1) ? 25 :
                number >= new Version(1, 20) ? 21 :
                number >= new Version(1, 17) ? 17 :
                number >= new Version(1, 16, 5) ? 16 :
                number >= new Version(1, 12) ? 11 : 8;
            return Math.Max(paperMajor, vanillaMajor);
        }

        protected override bool HasServer(string directory) => File.Exists(Path.Combine(directory, "paper.jar"));

        public override string GetLocalBuild()
        {
            string installed = ReadInstalledBuild();
            if (installed.Length > 0) return installed;
            string history = Path.Combine(ServerDirectory, "version_history.json");
            if (!File.Exists(history)) return string.Empty;
            string current = (string)JObject.Parse(File.ReadAllText(history))["currentVersion"] ?? string.Empty;
            var legacy = Regex.Match(current, @"git-Paper-(\d+) \(MC: ([\d.]+)\)");
            if (legacy.Success) return legacy.Groups[2].Value + "/" + legacy.Groups[1].Value;
            var modern = Regex.Match(current, @"^([\d.]+)-(\d+)-");
            return modern.Success ? modern.Groups[1].Value + "/" + modern.Groups[2].Value : string.Empty;
        }

        public override async Task<string> GetRemoteBuild()
        {
            try
            {
                using (var client = CreateClient())
                {
                    string local = GetLocalBuild();
                    var project = JObject.Parse(await client.DownloadStringTaskAsync(Api));
                    var versions = ((JObject)project["versions"]).Properties()
                        .SelectMany(group => group.Value.Values<string>())
                        .Where(version => Regex.IsMatch(version, @"^\d+(\.\d+)+$"))
                        .OrderByDescending(ParseVersion);
                    foreach (string version in versions)
                    {
                        if (local.Length > 0 && version != BuildVersion(local)) continue;
                        var builds = JArray.Parse(await client.DownloadStringTaskAsync($"{Api}/versions/{version}/builds"));
                        var build = builds.Where(item => (string)item["channel"] == "STABLE")
                            .OrderByDescending(item => (int)item["id"]).FirstOrDefault();
                        if (build != null) return version + "/" + (string)build["id"];
                    }
                    throw new InvalidDataException("No stable Paper build is available for this Minecraft version.");
                }
            }
            catch (Exception exception)
            {
                Error = "Failed to get the Paper build: " + exception.Message;
                return string.Empty;
            }
        }

        protected override async Task InstallBuild(string build)
        {
            string[] parts = build.Split('/');
            string destination = Path.Combine(ServerDirectory, "paper.jar");
            string temporary = destination + ".download";
            try
            {
                using (var client = CreateClient())
                {
                    var metadata = JObject.Parse(await client.DownloadStringTaskAsync($"{Api}/versions/{parts[0]}/builds/{parts[1]}"));
                    var download = metadata["downloads"]?["server:default"];
                    if (download == null) throw new InvalidDataException("Paper did not provide a server download.");
                    await DownloadVerified(client, (string)download["url"], temporary, (string)download["checksums"]?["sha256"]);
                    using (var archive = ZipFile.OpenRead(temporary))
                    {
                        if (archive.GetEntry("META-INF/MANIFEST.MF") == null)
                            throw new InvalidDataException("The Paper download is not a server JAR.");
                    }
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
