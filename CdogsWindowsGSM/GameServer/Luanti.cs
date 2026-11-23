using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class Luanti
    {
        private const string ReleaseFeedUrl = "https://github.com/luanti-org/luanti/releases.atom";
        private const string MinetestGameArchiveUrl = "https://github.com/luanti-org/minetest_game/archive/refs/heads/master.zip";
        private const string EngineDirectoryName = "luanti";
        private const string ExecutableRelativePath = @"bin\luanti.exe";
        internal const string ConfigFileName = "minetest.conf";
        private const string VersionFileName = "luanti_version.txt";
        private const string GamesDirectoryName = "games";
        private const string WorldsDirectoryName = "worlds";
        private const string DefaultWorldName = "world";
        private const string DefaultGameDirectoryName = "minetest_game";
        private const string DefaultGameId = "minetest";
        private const int StopTimeoutMilliseconds = 10000;
        private readonly ServerConfig _serverData;

        private sealed class ReleaseInfo
        {
            public string Version { get; set; }
            public Uri DownloadUrl { get; set; }
            public string FileName { get; set; }
        }

        public const string FullName = "Luanti Dedicated Server";
        public string StartPath => GetStartPath();
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "30000";
        public string QueryPort = "30000";
        public string Defaultmap = DefaultWorldName;
        public string Maxplayers = "10";
        public string Additional = string.Empty;
        public string Error;
        public string Notice;

        public Luanti(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        private string GetStartPath()
        {
            if (_serverData == null || string.IsNullOrWhiteSpace(_serverData.ServerID))
            {
                return Path.Combine(EngineDirectoryName, ExecutableRelativePath);
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string engineRoot = FindInstalledEngineRoot(serverFilesPath) ?? GetEngineRootForInstall(serverFilesPath);
            return Path.GetRelativePath(serverFilesPath, Path.Combine(engineRoot, ExecutableRelativePath));
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            if (_serverData == null)
            {
                Error = "Could not create Luanti configuration because the server configuration is unavailable.";
                return;
            }

            if (!TryValidateServerSettings(_serverData, out int port, out int maxPlayers, out string validationError))
            {
                Error = validationError;
                return;
            }

            string configPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ConfigFileName);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                string config = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                config = SetConfigValues(config, new[]
                {
                    new KeyValuePair<string, string>("server_name", SanitizeConfigValue(_serverData.ServerName)),
                    new KeyValuePair<string, string>("port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("max_users", maxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture))
                });
                if (!HasConfigValue(config, "server_announce"))
                {
                    config = SetConfigValues(config, new[]
                    {
                        new KeyValuePair<string, string>("server_announce", "false")
                    });
                }

                File.WriteAllText(configPath, config, new UTF8Encoding(false));
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                Error = $"Failed to create Luanti configuration: {exception.Message}";
            }
        }

        public Task<Process> Install()
        {
            return InstallOrUpdate("install");
        }

        public Task<Process> Update()
        {
            return InstallOrUpdate("update");
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (!IsInstallValid())
            {
                return Task.FromResult<Process>(null);
            }

            CreateServerCFG();
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return Task.FromResult<Process>(null);
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string engineRoot = FindInstalledEngineRoot(serverFilesPath);
            string executablePath = Path.Combine(engineRoot, ExecutableRelativePath);
            string worldName;
            try
            {
                worldName = ResolveWorldName(_serverData.ServerMap);
            }
            catch (InvalidDataException exception)
            {
                Error = exception.Message;
                return Task.FromResult<Process>(null);
            }

            string worldPath = ServerPath.GetServersServerFiles(_serverData.ServerID, WorldsDirectoryName, worldName);
            string worldMetadataPath = Path.Combine(worldPath, "world.mt");
            string gameId;
            try
            {
                Directory.CreateDirectory(worldPath);
                gameId = ReadWorldGameId(worldMetadataPath);
                if (gameId == null)
                {
                    gameId = GetDefaultGameId(engineRoot);
                    if (gameId == null)
                    {
                        Error = "Could not select a Luanti game for this world. Keep one game installed or set a gameid in world.mt.";
                        return Task.FromResult<Process>(null);
                    }

                    WriteWorldGameId(worldMetadataPath, gameId);
                }

                if (!HasInstalledGame(engineRoot, gameId))
                {
                    Error = $"The Luanti world requires game '{gameId}', which is not installed under ({Path.Combine(engineRoot, GamesDirectoryName)}).";
                    return Task.FromResult<Process>(null);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                Error = $"Failed to prepare the Luanti world: {exception.Message}";
                return Task.FromResult<Process>(null);
            }

            string configPath = ServerPath.GetServersServerFiles(_serverData.ServerID, ConfigFileName);
            string arguments = $"--server --config {QuoteCommandLineArgument(configPath)} --world {QuoteCommandLineArgument(worldPath)}";
            if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
            {
                arguments += " " + _serverData.ServerParam.Trim();
            }

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = Path.Combine(engineRoot, "bin"),
                    FileName = executablePath,
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
                    Error = "Luanti did not start a server process.";
                    process.Dispose();
                    return Task.FromResult<Process>(null);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return Task.FromResult(process);
            }
            catch (Win32Exception exception)
            {
                Error = $"Failed to start Luanti: {exception.Message}";
            }
            catch (InvalidOperationException exception)
            {
                Error = $"Failed to start Luanti: {exception.Message}";
            }
            catch (ArgumentException exception)
            {
                Error = $"Failed to start Luanti: {exception.Message}";
            }
            catch (IOException exception)
            {
                Error = $"Failed to start Luanti: {exception.Message}";
            }

            if (processStarted)
            {
                StopFailedStart(process);
            }
            process.Dispose();
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                process.StandardInput.WriteLine("shutdown");
            }
            catch (IOException exception)
            {
                Notice = $"Could not send Luanti's graceful shutdown command: {exception.Message}";
            }
            catch (InvalidOperationException exception)
            {
                Notice = $"Could not send Luanti's graceful shutdown command: {exception.Message}";
            }

            bool exited = await Task.Run(() => process.WaitForExit(StopTimeoutMilliseconds));
            if (!exited && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await Task.Run(() => process.WaitForExit());
                Notice = AppendMessage(Notice, "Luanti did not stop gracefully; its process tree was terminated.");
            }
        }

        public bool IsInstallValid()
        {
            Error = string.Empty;
            string serverFilesPath = GetServerFilesPath();
            if (string.IsNullOrWhiteSpace(serverFilesPath))
            {
                Error = "The Luanti server files path is unavailable.";
                return false;
            }

            string engineRoot = FindInstalledEngineRoot(serverFilesPath);
            if (engineRoot == null)
            {
                Error = $"{ExecutableRelativePath} was not found in the Luanti server files.";
                return false;
            }

            try
            {
                if (HasInstalledGame(engineRoot))
                {
                    return true;
                }
                Error = $"No Luanti game was found under ({Path.Combine(engineRoot, GamesDirectoryName)}). Install a game package before starting this server.";
            }
            catch (IOException exception)
            {
                Error = $"Could not check the installed Luanti games: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Could not check the installed Luanti games: {exception.Message}";
            }

            return false;
        }

        public bool IsImportValid(string path)
        {
            Error = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Error = $"The Luanti import directory was not found ({path}).";
                return false;
            }

            string engineRoot = FindInstalledEngineRoot(path);
            if (engineRoot == null)
            {
                Error = $"{ExecutableRelativePath} was not found in the Luanti import directory ({path}).";
                return false;
            }

            try
            {
                if (HasInstalledGame(engineRoot))
                {
                    return true;
                }
                Error = $"The Luanti import does not contain a game package under ({Path.Combine(engineRoot, GamesDirectoryName)}).";
            }
            catch (IOException exception)
            {
                Error = $"Could not check the imported Luanti games: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Could not check the imported Luanti games: {exception.Message}";
            }

            return false;
        }

        public string GetLocalBuild()
        {
            Error = string.Empty;
            string serverFilesPath = GetServerFilesPath();
            string engineRoot = serverFilesPath == null ? null : FindInstalledEngineRoot(serverFilesPath);
            if (engineRoot == null)
            {
                Error = "The Luanti executable was not found.";
                return string.Empty;
            }

            string versionPath = ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName);
            try
            {
                if (File.Exists(versionPath))
                {
                    string savedVersion = File.ReadAllText(versionPath).Trim();
                    if (!string.IsNullOrWhiteSpace(savedVersion))
                    {
                        return savedVersion;
                    }
                }

                string productVersion = FileVersionInfo.GetVersionInfo(Path.Combine(engineRoot, ExecutableRelativePath)).ProductVersion;
                return string.IsNullOrWhiteSpace(productVersion) ? "unknown" : productVersion;
            }
            catch (IOException exception)
            {
                Error = $"Failed to read the local Luanti build: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to read the local Luanti build: {exception.Message}";
            }
            catch (Win32Exception exception)
            {
                Error = $"Failed to read the local Luanti build: {exception.Message}";
            }

            return string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            Error = string.Empty;
            try
            {
                using (WebClient webClient = CreateWebClient())
                {
                    ReleaseInfo release = await GetLatestRelease(webClient);
                    return release.Version;
                }
            }
            catch (WebException exception)
            {
                Error = $"Failed to get the latest Luanti build: {exception.Message}";
            }
            catch (XmlException exception)
            {
                Error = $"Failed to parse Luanti release metadata: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = $"Failed to get the latest Luanti build: {exception.Message}";
            }

            return string.Empty;
        }

        internal static string ParseLatestStableReleaseVersion(string atomXml)
        {
            XDocument releaseFeed;
            try
            {
                releaseFeed = XDocument.Parse(atomXml);
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("GitHub returned invalid Luanti release metadata.", exception);
            }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            foreach (XElement entry in releaseFeed.Descendants(atom + "entry"))
            {
                string title = ((string)entry.Element(atom + "title") ?? string.Empty).Trim();
                Match match = Regex.Match(title, @"^Luanti\s+(\d+\.\d+\.\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success && Version.TryParse(match.Groups[1].Value, out _))
                {
                    return match.Groups[1].Value;
                }
            }

            throw new InvalidDataException("GitHub did not publish a stable Luanti release in its release feed.");
        }

        internal static string ResolveWorldName(string value)
        {
            string worldName = string.IsNullOrWhiteSpace(value) ? DefaultWorldName : value.Trim();
            if (worldName == "." || worldName == ".." ||
                Path.IsPathRooted(worldName) ||
                worldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                worldName.Contains(Path.DirectorySeparatorChar) ||
                worldName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new InvalidDataException("The Luanti world name must be a single valid folder name.");
            }

            return worldName;
        }

        internal static void ReplaceEngineDirectory(
            string packageRoot,
            string destinationPath,
            string stagingPath,
            bool preserveRootInstanceData)
        {
            string backupPath = Path.Combine(stagingPath, "previous-install");
            string preservedDataPath = Path.Combine(stagingPath, "preserved-data");
            string[] preservedDirectories = preserveRootInstanceData
                ? new[] { GamesDirectoryName, WorldsDirectoryName, "mods", "textures" }
                : new[] { GamesDirectoryName, "mods", "textures" };
            string[] preservedFiles = preserveRootInstanceData
                ? new[] { ConfigFileName, VersionFileName }
                : Array.Empty<string>();
            bool previousInstallMoved = false;
            bool packageInstalled = false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                if (Directory.Exists(destinationPath))
                {
                    Directory.Move(destinationPath, backupPath);
                    previousInstallMoved = true;
                    Directory.CreateDirectory(preservedDataPath);
                    MovePreservedData(backupPath, preservedDataPath, preservedDirectories, preservedFiles);
                }

                Directory.Move(packageRoot, destinationPath);
                packageInstalled = true;
                if (previousInstallMoved)
                {
                    MovePreservedData(preservedDataPath, destinationPath, preservedDirectories, preservedFiles);
                }
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                try
                {
                    if (packageInstalled && Directory.Exists(destinationPath))
                    {
                        if (previousInstallMoved)
                        {
                            MovePreservedData(destinationPath, preservedDataPath, preservedDirectories, preservedFiles);
                        }
                        Directory.Delete(destinationPath, recursive: true);
                    }

                    if (previousInstallMoved && Directory.Exists(backupPath))
                    {
                        MovePreservedData(preservedDataPath, backupPath, preservedDirectories, preservedFiles);
                        Directory.Move(backupPath, destinationPath);
                    }
                }
                catch (Exception rollbackException) when (IsExpectedFileSystemException(rollbackException))
                {
                    throw new IOException(
                        $"Luanti installation failed and the previous files could not be fully restored. Recovery files remain at ({backupPath}).",
                        new AggregateException(exception, rollbackException));
                }

                throw;
            }

            if (previousInstallMoved && Directory.Exists(backupPath))
            {
                try
                {
                    Directory.Delete(backupPath, recursive: true);
                }
                catch (IOException exception)
                {
                    throw new IOException($"Luanti was installed, but previous engine files remain at ({backupPath}).", exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new IOException($"Luanti was installed, but previous engine files remain at ({backupPath}).", exception);
                }
            }
        }

        private static void MovePreservedData(
            string sourceRoot,
            string destinationRoot,
            IEnumerable<string> directories,
            IEnumerable<string> files)
        {
            foreach (string directoryName in directories)
            {
                string source = Path.Combine(sourceRoot, directoryName);
                if (!Directory.Exists(source))
                {
                    continue;
                }

                string destination = Path.Combine(destinationRoot, directoryName);
                if (Directory.Exists(destination))
                {
                    if (Directory.GetFileSystemEntries(destination).Length != 0)
                    {
                        throw new IOException($"Luanti package data conflicts with preserved directory ({destination}).");
                    }
                    Directory.Delete(destination);
                }

                Directory.Move(source, destination);
            }

            foreach (string fileName in files)
            {
                string source = Path.Combine(sourceRoot, fileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                string destination = Path.Combine(destinationRoot, fileName);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(source, destination);
            }
        }

        private async Task<Process> InstallOrUpdate(string action)
        {
            Error = string.Empty;
            Notice = string.Empty;
            string serverFilesPath = GetServerFilesPath();
            if (string.IsNullOrWhiteSpace(serverFilesPath))
            {
                Error = "The Luanti server files path is unavailable.";
                return null;
            }

            string serverDirectory = ServerPath.GetServers(_serverData.ServerID);
            string stagingPath = Path.Combine(serverDirectory, "__luanti_stage_" + Guid.NewGuid().ToString("N"));
            string downloadPath = Path.Combine(stagingPath, "download");
            string engineExtractPath = Path.Combine(stagingPath, "engine");
            string gameExtractPath = Path.Combine(stagingPath, "game");
            bool preserveStagingPath = false;

            try
            {
                Directory.CreateDirectory(serverDirectory);
                Directory.CreateDirectory(downloadPath);
                Directory.CreateDirectory(engineExtractPath);
                ReleaseInfo release;
                string engineArchivePath;
                string gameArchivePath = Path.Combine(downloadPath, "minetest_game.zip");
                string existingEngineRoot = FindInstalledEngineRoot(serverFilesPath);
                if (existingEngineRoot == null)
                {
                    existingEngineRoot = Directory.Exists(Path.Combine(serverFilesPath, GamesDirectoryName))
                        ? serverFilesPath
                        : Path.Combine(serverFilesPath, EngineDirectoryName);
                }
                bool needsDefaultGame = !HasInstalledGame(existingEngineRoot);

                using (WebClient webClient = CreateWebClient())
                {
                    release = await GetLatestRelease(webClient);
                    engineArchivePath = Path.Combine(downloadPath, release.FileName);
                    await webClient.DownloadFileTaskAsync(release.DownloadUrl, engineArchivePath);
                    if (needsDefaultGame)
                    {
                        await webClient.DownloadFileTaskAsync(new Uri(MinetestGameArchiveUrl), gameArchivePath);
                    }
                }

                if (!await FileManagement.ExtractArchive(engineArchivePath, engineExtractPath))
                {
                    throw new InvalidDataException("The official Luanti archive could not be safely extracted.");
                }

                string packageRoot = FindEnginePackageRoot(engineExtractPath);
                if (!IsValidEnginePackage(packageRoot))
                {
                    throw new InvalidDataException("The official Luanti archive is missing bin\\luanti.exe or its builtin data.");
                }

                string gamePackageRoot = null;
                if (needsDefaultGame)
                {
                    Directory.CreateDirectory(gameExtractPath);
                    if (!await FileManagement.ExtractArchive(gameArchivePath, gameExtractPath))
                    {
                        throw new InvalidDataException("The official Minetest Game archive could not be safely extracted.");
                    }

                    gamePackageRoot = FindDefaultGamePackageRoot(gameExtractPath);
                    if (gamePackageRoot == null)
                    {
                        throw new InvalidDataException("The Minetest Game archive is missing game.conf or its mods directory.");
                    }
                }

                bool preserveRootInstanceData = string.Equals(existingEngineRoot, serverFilesPath, StringComparison.OrdinalIgnoreCase);
                ReplaceEngineDirectory(packageRoot, existingEngineRoot, stagingPath, preserveRootInstanceData);
                string gameTargetPath = Path.Combine(existingEngineRoot, GamesDirectoryName, DefaultGameDirectoryName);
                if (gamePackageRoot != null && !HasInstalledGame(existingEngineRoot))
                {
                    if (Directory.Exists(gameTargetPath))
                    {
                        throw new InvalidDataException($"A Luanti game directory already exists but is incomplete ({gameTargetPath}). Remove it or install a valid game package.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(gameTargetPath));
                    Directory.Move(gamePackageRoot, gameTargetPath);
                }

                if (!HasInstalledGame(existingEngineRoot))
                {
                    throw new InvalidDataException("Luanti was installed without a valid game package.");
                }

                File.WriteAllText(
                    ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName),
                    release.Version,
                    new UTF8Encoding(false));
            }
            catch (WebException exception)
            {
                Error = $"Failed to {action} Luanti: {exception.Message}";
            }
            catch (XmlException exception)
            {
                Error = $"Failed to {action} Luanti release metadata: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = $"Failed to {action} Luanti: {exception.Message}";
            }
            catch (IOException exception)
            {
                Error = $"Failed to {action} Luanti: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to {action} Luanti: {exception.Message}";
            }
            catch (ArgumentException exception)
            {
                Error = $"Failed to {action} Luanti: {exception.Message}";
            }
            finally
            {
                string previousInstallPath = Path.Combine(stagingPath, "previous-install");
                if (Directory.Exists(previousInstallPath))
                {
                    preserveStagingPath = true;
                    Error = AppendMessage(Error, $"Recovery files remain at ({previousInstallPath}).");
                }

                if (!preserveStagingPath && Directory.Exists(stagingPath))
                {
                    try
                    {
                        Directory.Delete(stagingPath, recursive: true);
                    }
                    catch (IOException exception)
                    {
                        Notice = AppendMessage(Notice, $"Could not remove temporary Luanti installer files: {exception.Message}");
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Notice = AppendMessage(Notice, $"Could not remove temporary Luanti installer files: {exception.Message}");
                    }
                }
            }

            return null;
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
                throw new InvalidDataException("GitHub returned invalid Luanti release metadata.", exception);
            }

            string version = ParseLatestStableReleaseVersion(releaseFeed.ToString(SaveOptions.DisableFormatting));
            string fileName = $"luanti-{version}-win64.zip";
            string url = $"https://github.com/luanti-org/luanti/releases/download/{version}/{fileName}";
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri downloadUrl) ||
                !string.Equals(downloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !downloadUrl.IsDefaultPort ||
                !string.Equals(downloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Could not build the official Luanti download URL.");
            }

            return new ReleaseInfo { Version = version, FileName = fileName, DownloadUrl = downloadUrl };
        }

        private static WebClient CreateWebClient()
        {
            var webClient = new WebClient();
            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
            return webClient;
        }

        private string GetServerFilesPath()
        {
            return _serverData == null || string.IsNullOrWhiteSpace(_serverData.ServerID)
                ? null
                : ServerPath.GetServersServerFiles(_serverData.ServerID);
        }

        private static string FindInstalledEngineRoot(string serverFilesPath)
        {
            if (string.IsNullOrWhiteSpace(serverFilesPath))
            {
                return null;
            }

            string managedEngineRoot = Path.Combine(serverFilesPath, EngineDirectoryName);
            if (IsValidEnginePackage(managedEngineRoot))
            {
                return managedEngineRoot;
            }
            return IsValidEnginePackage(serverFilesPath) ? serverFilesPath : null;
        }

        private static string GetEngineRootForInstall(string serverFilesPath)
        {
            string installedEngineRoot = FindInstalledEngineRoot(serverFilesPath);
            if (installedEngineRoot != null)
            {
                return installedEngineRoot;
            }

            return Directory.Exists(Path.Combine(serverFilesPath, GamesDirectoryName))
                ? serverFilesPath
                : Path.Combine(serverFilesPath, EngineDirectoryName);
        }

        private static string FindEnginePackageRoot(string extractionRoot)
        {
            string[] executables = Directory.GetFiles(extractionRoot, Path.GetFileName(ExecutableRelativePath), SearchOption.AllDirectories);
            string executablePath = executables.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidDataException("The Luanti archive did not contain bin\\luanti.exe.");
            }

            return Directory.GetParent(Path.GetDirectoryName(executablePath)).FullName;
        }

        private static bool IsValidEnginePackage(string engineRoot)
        {
            return !string.IsNullOrWhiteSpace(engineRoot) &&
                File.Exists(Path.Combine(engineRoot, ExecutableRelativePath)) &&
                File.Exists(Path.Combine(engineRoot, "builtin", "init.lua"));
        }

        private static string FindDefaultGamePackageRoot(string extractionRoot)
        {
            return Directory.GetFiles(extractionRoot, "game.conf", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(path => Directory.Exists(Path.Combine(path, "mods")));
        }

        private static bool HasInstalledGame(string engineRoot)
        {
            string gamesPath = Path.Combine(engineRoot ?? string.Empty, GamesDirectoryName);
            return Directory.Exists(gamesPath) && Directory.GetDirectories(gamesPath)
                .Any(gameDirectory => File.Exists(Path.Combine(gameDirectory, "game.conf")));
        }

        private static bool HasInstalledGame(string engineRoot, string gameId)
        {
            string gameDirectoryName = string.Equals(gameId, DefaultGameId, StringComparison.OrdinalIgnoreCase)
                ? DefaultGameDirectoryName
                : gameId;
            return IsValidGameId(gameDirectoryName) &&
                File.Exists(Path.Combine(engineRoot, GamesDirectoryName, gameDirectoryName, "game.conf"));
        }

        private static string GetDefaultGameId(string engineRoot)
        {
            string gamesPath = Path.Combine(engineRoot, GamesDirectoryName);
            if (!Directory.Exists(gamesPath))
            {
                return null;
            }

            string[] gameDirectories = Directory.GetDirectories(gamesPath)
                .Where(gameDirectory => File.Exists(Path.Combine(gameDirectory, "game.conf")))
                .ToArray();
            if (gameDirectories.Length == 0)
            {
                return null;
            }
            if (File.Exists(Path.Combine(gamesPath, DefaultGameDirectoryName, "game.conf")))
            {
                return DefaultGameId;
            }
            return gameDirectories.Length == 1 ? Path.GetFileName(gameDirectories[0]) : null;
        }

        private static string ReadWorldGameId(string worldMetadataPath)
        {
            if (!File.Exists(worldMetadataPath))
            {
                return null;
            }

            foreach (string line in File.ReadLines(worldMetadataPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = trimmed.IndexOf('=');
                if (separator >= 0 && string.Equals(trimmed.Substring(0, separator).Trim(), "gameid", StringComparison.OrdinalIgnoreCase))
                {
                    string gameId = trimmed.Substring(separator + 1).Trim();
                    if (IsValidGameId(gameId))
                    {
                        return gameId;
                    }
                    throw new InvalidDataException("The Luanti world's gameid is invalid.");
                }
            }

            return null;
        }

        private static void WriteWorldGameId(string worldMetadataPath, string gameId)
        {
            if (!File.Exists(worldMetadataPath))
            {
                File.WriteAllText(worldMetadataPath, "gameid = " + gameId + Environment.NewLine, new UTF8Encoding(false));
                return;
            }

            string currentContents = File.ReadAllText(worldMetadataPath);
            string newline = currentContents.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            bool hasTrailingNewline = currentContents.EndsWith("\r\n", StringComparison.Ordinal) ||
                currentContents.EndsWith("\r", StringComparison.Ordinal) ||
                currentContents.EndsWith("\n", StringComparison.Ordinal);
            File.AppendAllText(
                worldMetadataPath,
                (hasTrailingNewline ? string.Empty : newline) + "gameid = " + gameId + newline,
                new UTF8Encoding(false));
        }

        private static bool IsValidGameId(string gameId)
        {
            return !string.IsNullOrWhiteSpace(gameId) &&
                gameId != "." &&
                gameId != ".." &&
                !Path.IsPathRooted(gameId) &&
                gameId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                !gameId.Contains(Path.DirectorySeparatorChar) &&
                !gameId.Contains(Path.AltDirectorySeparatorChar);
        }

        private static bool TryValidateServerSettings(ServerConfig serverConfig, out int port, out int maxPlayers, out string error)
        {
            error = string.Empty;
            if (!int.TryParse(serverConfig.ServerPort, out port) || port < 1 || port > 65535)
            {
                maxPlayers = 0;
                error = "Luanti server port must be between 1 and 65535.";
                return false;
            }

            if (!int.TryParse(serverConfig.ServerMaxPlayer, out maxPlayers) || maxPlayers < 1 || maxPlayers > 65535)
            {
                error = "Luanti max players must be between 1 and 65535.";
                return false;
            }

            return true;
        }

        private static string SetConfigValues(string config, IEnumerable<KeyValuePair<string, string>> settings)
        {
            string newline = config.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : config.IndexOf('\r') >= 0 ? "\r" : "\n";
            var lines = string.IsNullOrEmpty(config)
                ? new List<string>()
                : new List<string>(Regex.Split(config, "\r\n|\n|\r"));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0 &&
                (config.EndsWith("\r\n", StringComparison.Ordinal) || config.EndsWith("\r", StringComparison.Ordinal) || config.EndsWith("\n", StringComparison.Ordinal)))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            foreach (KeyValuePair<string, string> setting in settings)
            {
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!TryGetSettingName(lines[i], out string settingName) ||
                        !string.Equals(settingName, setting.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lines[i] = setting.Key + " = " + setting.Value;
                    found = true;
                }
                if (!found)
                {
                    lines.Add(setting.Key + " = " + setting.Value);
                }
            }

            return string.Join(newline, lines) + newline;
        }

        private static bool HasConfigValue(string config, string settingName)
        {
            return Regex.Split(config ?? string.Empty, "\r\n|\n|\r")
                .Any(line => TryGetSettingName(line, out string key) &&
                    string.Equals(key, settingName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetSettingName(string line, out string settingName)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                settingName = null;
                return false;
            }

            int separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                settingName = null;
                return false;
            }

            settingName = trimmed.Substring(0, separator).Trim();
            return settingName.Length > 0;
        }

        private static string SanitizeConfigValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("#", " ")
                .Replace("\"", " ")
                .Trim();
        }

        private static string QuoteCommandLineArgument(string value)
        {
            var quoted = new StringBuilder();
            quoted.Append('"');
            int backslashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashCount++;
                }
                else if (character == '"')
                {
                    quoted.Append('\\', backslashCount * 2 + 1);
                    quoted.Append(character);
                    backslashCount = 0;
                }
                else
                {
                    quoted.Append('\\', backslashCount);
                    quoted.Append(character);
                    backslashCount = 0;
                }
            }
            quoted.Append('\\', backslashCount * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static string AppendMessage(string current, string message)
        {
            return string.IsNullOrWhiteSpace(current) ? message : current + " " + message;
        }

        private static bool IsExpectedFileSystemException(Exception exception)
        {
            return exception is ArgumentException ||
                exception is IOException ||
                exception is UnauthorizedAccessException;
        }

        private void StopFailedStart(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            catch (Win32Exception exception)
            {
                Error = AppendMessage(Error, $"Could not stop the failed Luanti process: {exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                Error = AppendMessage(Error, $"Could not stop the failed Luanti process: {exception.Message}");
            }
        }
    }
}
