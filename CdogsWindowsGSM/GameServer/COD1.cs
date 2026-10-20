using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class COD1
    {
        private const string AppId = "2620";
        private const string ModDirectoryName = "CoD1MeatBots_RC2";
        private const string ModArchiveUrl = "https://www.moddb.com/downloads/start/216746/all";
        private const string ModPageUrl = "https://www.moddb.com/games/call-of-duty/downloads/cod1-meatbot-v02";
        private const long ModArchiveSize = 24089986;
        private const string ModArchiveMd5 = "f157792c719b57944fd0c67a2584416f";
        private const string ModServerPackage = "z_mbot_cod_v02_svr.pk3";
        private readonly ServerConfig _serverData;

        public string Error;
        public string Notice;
        public string StartPath => "CoDMP.exe";
        public const string FullName = "Call of Duty 1 Dedicated Server";
        public bool AllowsEmbedConsole => false;
        public bool loginAnonymous => false;
        public int PortIncrements => 1;
        public object QueryMethod => null;
        public string Port => "28960";
        public string QueryPort => "28960";
        public string Defaultmap => "mp_carentan";
        public string Maxplayers => "32";
        public string Additional => string.Empty;

        public COD1(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            string mainDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID, "main");
            Directory.CreateDirectory(mainDirectory);
            WriteDefaultConfigIfMissing(Path.Combine(mainDirectory, "server.cfg"));

            string modDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID, ModDirectoryName);
            if (Directory.Exists(modDirectory))
            {
                WriteDefaultConfigIfMissing(Path.Combine(modDirectory, "server.cfg"));
            }
        }

        private void WriteDefaultConfigIfMissing(string path)
        {
            if (File.Exists(path)) { return; }

            var config = new StringBuilder();
            config.AppendLine($"set sv_hostname \"{EscapeConfigValue(_serverData.ServerName)}\"");
            config.AppendLine("set dedicated 2");
            config.AppendLine($"set net_port {_serverData.ServerPort}");
            config.AppendLine($"set sv_maxclients {_serverData.ServerMaxPlayer}");
            config.AppendLine("set g_gametype \"tdm\"");
            config.AppendLine("set sv_mapRotation \"map mp_carentan; map mp_dawnville\"");
            File.WriteAllText(path, config.ToString());
        }

        public Task<Process> Start()
        {
            if (!IsInstallValid()) { return Task.FromResult<Process>(null); }

            bool meatBots = _serverData.Cod1MeatBots;
            if (meatBots && !ServerConfig.ValidateCod1Settings(
                    _serverData.Cod1AlliesBots,
                    _serverData.Cod1AxisBots,
                    _serverData.Cod1FastDlUrl,
                    _serverData.ServerMaxPlayer,
                    true,
                    out Error))
            {
                return Task.FromResult<Process>(null);
            }

            CreateServerCFG();
            string executablePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            var arguments = new StringBuilder("+set dedicated 2 ");
            if (meatBots)
            {
                arguments.Append($"+set fs_game {ModDirectoryName} +exec server.cfg +exec wgs_cod1_meatbots.cfg ");
            }
            else
            {
                arguments.Append("+exec server.cfg ");
            }

            arguments.Append($"+set net_port {_serverData.ServerPort} ");
            if (!string.IsNullOrWhiteSpace(_serverData.ServerIP))
            {
                arguments.Append($"+set net_ip {_serverData.ServerIP} ");
            }
            arguments.Append($"+set sv_maxclients {_serverData.ServerMaxPlayer} ");
            arguments.Append($"+set sv_hostname \"{EscapeConfigValue(_serverData.ServerName)}\" ");
            if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
            {
                arguments.Append(_serverData.ServerParam.Trim()).Append(' ');
            }
            arguments.Append("+map_rotate");

            var process = new Process
            {
                StartInfo =
                {
                    FileName = executablePath,
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    Arguments = arguments.ToString(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true
                },
                EnableRaisingEvents = true
            };

            try
            {
                if (meatBots) { WriteMeatBotsConfig(); }
                process.Start();
                return Task.FromResult(process);
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                process.Dispose();
                return Task.FromResult<Process>(null);
            }
        }

        public async Task Stop(Process process)
        {
            await Task.Run(() =>
            {
                if (process == null || process.HasExited) { return; }
                try
                {
                    process.StandardInput.WriteLine("quit");
                    process.StandardInput.Flush();
                }
                catch
                {
                    process.CloseMainWindow();
                }
            });
        }

        public async Task<Process> Install()
        {
            var steamCmd = new Installer.SteamCMD();
            Process process = await steamCmd.Install(_serverData.ServerID, string.Empty, AppId, true, loginAnonymous: false);
            Error = steamCmd.Error;
            return process;
        }

        public async Task<Process> InstallFallback()
        {
            string archiveUrl = Environment.GetEnvironmentVariable("WINDOWSGSM_COD1_BASE_ARCHIVE_URL") ?? string.Empty;
            string archiveFormat = Environment.GetEnvironmentVariable("WINDOWSGSM_COD1_BASE_ARCHIVE_FORMAT") ?? string.Empty;
            string archiveLayout = Environment.GetEnvironmentVariable("WINDOWSGSM_COD1_BASE_ARCHIVE_LAYOUT") ?? string.Empty;
            string archiveSha256 = Environment.GetEnvironmentVariable("WINDOWSGSM_COD1_BASE_ARCHIVE_SHA256") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(archiveUrl) || string.IsNullOrWhiteSpace(archiveSha256) || string.IsNullOrWhiteSpace(archiveFormat))
            {
                Error = "No verified HTTPS Call of Duty 1 base archive is configured.";
                return null;
            }

            if (!Uri.TryCreate(archiveUrl, UriKind.Absolute, out Uri archiveUri) || archiveUri.Scheme != Uri.UriSchemeHttps || !IsSha256(archiveSha256))
            {
                Error = "The HTTPS base archive URL or SHA-256 release configuration is invalid.";
                return null;
            }

            string format = archiveFormat.ToLowerInvariant();
            if (format != "zip" && format != "rar")
            {
                Error = "The configured base archive format must be zip or rar.";
                return null;
            }

            string cachePath = ServerPath.GetServersCache(_serverData.ServerID);
            Directory.CreateDirectory(cachePath);
            string downloadPath = Path.Combine(cachePath, "cod1-base-fallback.partial");
            string stagingPath = Path.Combine(cachePath, "cod1-base-staging-" + Guid.NewGuid().ToString("N"));
            string backupPath = Path.Combine(cachePath, "cod1-base-previous-" + Guid.NewGuid().ToString("N"));
            bool previousMoved = false;

            try
            {
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync(archiveUri, downloadPath);
                }

                if (!string.Equals(ComputeSha256(downloadPath), archiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Error = "The Call of Duty 1 base archive SHA-256 does not match the release configuration.";
                    return null;
                }

                Directory.CreateDirectory(stagingPath);
                if (!await FileManagement.ExtractArchive(downloadPath, stagingPath))
                {
                    Error = "The Call of Duty 1 base archive could not be safely extracted.";
                    return null;
                }

                string stagedRoot = ResolveArchiveLayout(stagingPath, archiveLayout);
                string stagingError = string.Empty;
                if (stagedRoot == null || !IsBaseFilesValid(stagedRoot, out stagingError))
                {
                    Error = stagedRoot == null ? "The configured base archive layout is invalid." : stagingError;
                    return null;
                }

                string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
                if (Directory.Exists(serverFilesPath))
                {
                    Directory.Move(serverFilesPath, backupPath);
                    previousMoved = true;
                }

                Directory.Move(stagedRoot, serverFilesPath);
                if (!IsBaseFilesValid(serverFilesPath, out string installedError))
                {
                    Directory.Delete(serverFilesPath, true);
                    if (previousMoved) { Directory.Move(backupPath, serverFilesPath); previousMoved = false; }
                    Error = installedError;
                    return null;
                }

                string archivePath = Path.Combine(cachePath, "cod1-base-fallback." + format);
                if (File.Exists(archivePath)) { File.Delete(archivePath); }
                File.Move(downloadPath, archivePath);
                if (previousMoved)
                {
                    Directory.Delete(backupPath, true);
                    previousMoved = false;
                }
                Error = string.Empty;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
                if (previousMoved && Directory.Exists(serverFilesPath))
                {
                    Directory.Delete(serverFilesPath, true);
                }
                if (previousMoved && Directory.Exists(backupPath) && !Directory.Exists(serverFilesPath))
                {
                    Directory.Move(backupPath, serverFilesPath);
                }
            }
            finally
            {
                if (File.Exists(downloadPath)) { File.Delete(downloadPath); }
                if (Directory.Exists(stagingPath)) { Directory.Delete(stagingPath, true); }
            }
            return null;
        }

        public async Task<bool> InstallMeatBots()
        {
            if (!IsBaseInstallValid()) { return false; }
            if (IsMeatBotsValid(ServerPath.GetServersServerFiles(_serverData.ServerID))) { return true; }

            string cachePath = ServerPath.GetServersCache(_serverData.ServerID);
            Directory.CreateDirectory(cachePath);
            string downloadPath = Path.Combine(cachePath, "cod1-meatbots.partial");
            string stagingPath = Path.Combine(cachePath, "cod1-meatbots-staging-" + Guid.NewGuid().ToString("N"));
            string backupPath = Path.Combine(cachePath, "cod1-meatbots-previous-" + Guid.NewGuid().ToString("N"));
            bool previousMoved = false;

            try
            {
                await DownloadMeatBots(downloadPath);
                var fileInfo = new FileInfo(downloadPath);
                if (fileInfo.Length != ModArchiveSize)
                {
                    Error = $"MeatBots archive size mismatch: expected {ModArchiveSize} bytes, got {fileInfo.Length}.";
                    return false;
                }
                if (!string.Equals(ComputeMd5(downloadPath), ModArchiveMd5, StringComparison.OrdinalIgnoreCase))
                {
                    Error = "MeatBots archive MD5 does not match the published ModDB digest.";
                    return false;
                }
                if (!IsRar4Archive(downloadPath))
                {
                    Error = "The downloaded MeatBots package is not a RAR4 archive.";
                    return false;
                }

                Directory.CreateDirectory(stagingPath);
                if (!await FileManagement.ExtractRar(downloadPath, stagingPath))
                {
                    Error = "MeatBots archive extraction failed. The base game installation was preserved.";
                    return false;
                }

                string[] modDirectories = Directory.GetDirectories(stagingPath, ModDirectoryName, SearchOption.AllDirectories);
                if (modDirectories.Length != 1 || Directory.GetFiles(modDirectories[0], ModServerPackage, SearchOption.AllDirectories).Length == 0)
                {
                    Error = "The verified MeatBots archive does not contain the expected CoD1MeatBots_RC2 server files.";
                    return false;
                }

                string targetPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ModDirectoryName);
                if (Directory.Exists(targetPath))
                {
                    Directory.Move(targetPath, backupPath);
                    previousMoved = true;
                }
                Directory.Move(modDirectories[0], targetPath);
                if (!IsMeatBotsValid(ServerPath.GetServersServerFiles(_serverData.ServerID)))
                {
                    Directory.Delete(targetPath, true);
                    if (previousMoved) { Directory.Move(backupPath, targetPath); previousMoved = false; }
                    Error = "Installed MeatBots files failed validation. The previous mod directory was restored.";
                    return false;
                }
                Error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                Error = $"MeatBots installation failed: {exception.Message}. The base game installation was preserved.";
                string targetPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ModDirectoryName);
                if (previousMoved && Directory.Exists(targetPath)) { Directory.Delete(targetPath, true); }
                if (previousMoved && Directory.Exists(backupPath) && !Directory.Exists(targetPath)) { Directory.Move(backupPath, targetPath); }
                return false;
            }
            finally
            {
                if (File.Exists(downloadPath)) { File.Delete(downloadPath); }
                if (Directory.Exists(stagingPath)) { Directory.Delete(stagingPath, true); }
                if (previousMoved && Directory.Exists(backupPath)) { Directory.Delete(backupPath, true); }
            }
        }

        private static async Task DownloadMeatBots(string destinationPath)
        {
            Exception lastError = null;
            foreach (string url in new[] { ModArchiveUrl, "https://www.moddb.com/downloads/mirror/216746/" })
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36";
                    request.Referer = ModPageUrl;
                    request.Accept = "text/html,application/octet-stream;q=0.9,*/*;q=0.8";
                    request.CookieContainer = new CookieContainer();
                    request.AllowAutoRedirect = true;
                    using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    using (Stream source = response.GetResponseStream())
                    using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await source.CopyToAsync(destination);
                    }
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    if (File.Exists(destinationPath)) { File.Delete(destinationPath); }
                }
            }
            throw new WebException("ModDB rejected both stable file-ID download routes.", lastError);
        }

        private void WriteMeatBotsConfig()
        {
            string configPath = ServerPath.GetServersConfigs(_serverData.ServerID, "cod1", "meatbots.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            var config = new StringBuilder();
            config.AppendLine("set bot_waypointtype 0");
            config.AppendLine("set bot_forceteam 0");
            config.AppendLine($"set cod_mbot_amount1 {_serverData.Cod1AxisBots}");
            config.AppendLine($"set cod_mbot_amount2 {_serverData.Cod1AlliesBots}");
            config.AppendLine("set sv_allowDownload 1");
            config.AppendLine("set sv_wwwDownload 1");
            config.AppendLine($"set sv_wwwBaseURL \"{EscapeConfigValue(_serverData.Cod1FastDlUrl.TrimEnd('/'))}\"");
            File.WriteAllText(configPath, config.ToString());
            string modPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ModDirectoryName);
            File.Copy(configPath, Path.Combine(modPath, "wgs_cod1_meatbots.cfg"), true);
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (process, error) = await Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate, loginAnonymous: false, custom: custom, embedConsole: false);
            Error = error;
            return process;
        }

        public bool IsBaseInstallValid()
        {
            return IsBaseFilesValid(ServerPath.GetServersServerFiles(_serverData.ServerID), out Error);
        }

        public bool IsInstallValid()
        {
            if (!IsBaseInstallValid()) { return false; }
            if (_serverData.Cod1MeatBots && !IsMeatBotsValid(ServerPath.GetServersServerFiles(_serverData.ServerID)))
            {
                Error = "CoD1MeatBots_RC2 server files are missing.";
                return false;
            }
            return true;
        }

        public bool IsImportValid(string path)
        {
            return IsBaseFilesValid(path, out Error);
        }

        private static bool IsBaseFilesValid(string path, out string error)
        {
            string executable = Path.Combine(path, "CoDMP.exe");
            if (!File.Exists(executable))
            {
                error = "CoDMP.exe is missing.";
                return false;
            }
            foreach (string package in new[] { "pak0.pk3", "pak1.pk3", "pak2.pk3" })
            {
                if (!File.Exists(Path.Combine(path, "main", package)))
                {
                    error = $"Required multiplayer data main\\{package} is missing.";
                    return false;
                }
            }
            string version = GetGameVersion(executable);
            if (string.IsNullOrWhiteSpace(version) || !version.StartsWith("1.5", StringComparison.OrdinalIgnoreCase))
            {
                error = $"CoDMP.exe version 1.5 is required; detected '{version}'.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static string GetGameVersion(string executable)
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executable);
            return string.IsNullOrWhiteSpace(versionInfo.ProductVersion) ? versionInfo.FileVersion : versionInfo.ProductVersion;
        }

        private static bool IsMeatBotsValid(string path)
        {
            string modDirectory = Path.Combine(path, ModDirectoryName);
            return Directory.Exists(modDirectory) && Directory.GetFiles(modDirectory, ModServerPackage, SearchOption.AllDirectories).Length > 0;
        }

        public string GetLocalBuild()
        {
            var steamCmd = new Installer.SteamCMD();
            string build = steamCmd.GetLocalBuild(_serverData.ServerID, AppId);
            if (!string.IsNullOrWhiteSpace(build)) { return build; }
            if (IsBaseInstallValid()) { return GetGameVersion(ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath)); }
            Error = steamCmd.Error;
            return string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCmd = new Installer.SteamCMD();
            string build = await steamCmd.GetRemoteBuild(AppId);
            Error = steamCmd.Error;
            return build;
        }

        private static string ResolveArchiveLayout(string stagingPath, string layout)
        {
            string root = Path.GetFullPath(stagingPath);
            if (string.IsNullOrWhiteSpace(layout)) { return root; }
            if (Path.IsPathRooted(layout) || layout.IndexOf(':') >= 0) { return null; }
            string destination = Path.GetFullPath(Path.Combine(root, layout));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || string.Equals(destination, root, StringComparison.OrdinalIgnoreCase) ? destination : null;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) { return false; }
            foreach (char character in value) { if (!Uri.IsHexDigit(character)) { return false; } }
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) { return ToHex(sha256.ComputeHash(stream)); }
        }

        private static string ComputeMd5(string path)
        {
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(path)) { return ToHex(md5.ComputeHash(stream)); }
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) { result.Append(value.ToString("x2")); }
            return result.ToString();
        }

        private static bool IsRar4Archive(string path)
        {
            byte[] signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Length < signature.Length) { return false; }
                for (int i = 0; i < signature.Length; i++) { if (stream.ReadByte() != signature[i]) { return false; } }
            }
            return true;
        }

        private static string EscapeConfigValue(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
