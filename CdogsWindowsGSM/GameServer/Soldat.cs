using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer
{
    class Soldat
    {
        private const string PackageUrl = "https://static.soldat.pl/downloads/soldatserver2.8.2_1.7.1.1.zip";
        private const string PackageSha256 = "6EC945868029587AC16E2AFE42815B04DB7CDDDBD97A1625601989C04A867099";
        private const long PackageSize = 5225652;
        private const string PackageVersion = "2.8.2 (1.7.1.1)";
        private const string VersionFileName = "soldat_version.txt";
        private readonly ServerConfig _serverData;

        public const string FullName = "Soldat Dedicated Server";
        public string StartPath = "soldatserver.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "23073";
        public string QueryPort = "23073";
        public string Defaultmap = "Random map";
        public string Maxplayers = "32";
        public string Additional = string.Empty;
        public string Error;
        public string Notice;

        public Soldat(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public void CreateServerCFG()
        {
            Error = string.Empty;
            if (!int.TryParse(_serverData.ServerPort, out int port) || port < 1 || port > 65535)
            {
                Error = "Soldat server port must be between 1 and 65535.";
                return;
            }
            if (!int.TryParse(_serverData.ServerMaxPlayer, out int maxPlayers) || maxPlayers < 1 || maxPlayers > 32)
            {
                Error = "Soldat max players must be between 1 and 32.";
                return;
            }

            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string configPath = Path.Combine(serverFilesPath, "soldat.ini");
            try
            {
                if (!File.Exists(configPath))
                {
                    Error = $"Soldat configuration was not found ({configPath}).";
                    return;
                }

                string config = File.ReadAllText(configPath);
                var settings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Port", port.ToString()),
                    new KeyValuePair<string, string>("Max_Players", maxPlayers.ToString()),
                    new KeyValuePair<string, string>("Server_Name", EscapeIniValue(_serverData.ServerName))
                };
                config = SetIniSettings(config, "NETWORK", settings);
                File.WriteAllText(configPath, config, new UTF8Encoding(false));
            }
            catch (IOException exception)
            {
                Error = "Failed to write the Soldat configuration: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to write the Soldat configuration: " + exception.Message;
            }
        }

        private static string SetIniSettings(string config, string section, IList<KeyValuePair<string, string>> settings)
        {
            string newline = config.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : config.IndexOf('\r') >= 0 ? "\r" : "\n";
            bool hasTrailingNewline = config.EndsWith("\r\n", StringComparison.Ordinal) ||
                config.EndsWith("\r", StringComparison.Ordinal) || config.EndsWith("\n", StringComparison.Ordinal);
            var lines = string.IsNullOrEmpty(config)
                ? new List<string>()
                : new List<string>(Regex.Split(config, "\r\n|\n|\r"));
            if (hasTrailingNewline && lines.Count > 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            int sectionStart = -1;
            int sectionEnd = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!TryGetSectionName(lines[i], out string sectionName) ||
                    !string.Equals(sectionName, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sectionStart = i;
                sectionEnd = lines.Count;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (TryGetSectionName(lines[j], out sectionName))
                    {
                        sectionEnd = j;
                        break;
                    }
                }
                break;
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }
                lines.Add("[" + section + "]");
                sectionStart = lines.Count - 1;
                sectionEnd = lines.Count;
            }

            foreach (KeyValuePair<string, string> setting in settings)
            {
                bool found = false;
                for (int i = sectionStart + 1; i < sectionEnd; i++)
                {
                    if (!string.Equals(GetSettingName(lines[i]), setting.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    lines[i] = setting.Key + "=" + setting.Value;
                    found = true;
                }
                if (!found)
                {
                    lines.Insert(sectionEnd, setting.Key + "=" + setting.Value);
                    sectionEnd++;
                }
            }

            return string.Join(newline, lines) + newline;
        }

        private static bool TryGetSectionName(string line, out string sectionName)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
            {
                sectionName = null;
                return false;
            }

            sectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return true;
        }

        private static string GetSettingName(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
            {
                return null;
            }

            int separator = trimmed.IndexOf('=');
            return separator < 0 ? null : trimmed.Substring(0, separator).Trim();
        }

        private static string EscapeIniValue(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
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
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesPath,
                    FileName = executablePath,
                    Arguments = _serverData.ServerParam ?? string.Empty,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = AllowsEmbedConsole,
                    RedirectStandardInput = false,
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

            try
            {
                if (!process.Start())
                {
                    Error = "Soldat did not start a server process.";
                    process.Dispose();
                    return Task.FromResult<Process>(null);
                }
                if (AllowsEmbedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                return Task.FromResult(process);
            }
            catch (Win32Exception exception)
            {
                Error = "Failed to start Soldat: " + exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to start Soldat: " + exception.Message;
            }
            catch (IOException exception)
            {
                Error = "Failed to start Soldat: " + exception.Message;
            }
            catch (ArgumentException exception)
            {
                Error = "Failed to start Soldat: " + exception.Message;
            }

            process.Dispose();
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process process)
        {
            Error = string.Empty;
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                process.CloseMainWindow();
            }
            catch (InvalidOperationException exception)
            {
                Error = "Failed to request Soldat shutdown: " + exception.Message;
            }

            await Task.Run(() =>
            {
                if (!process.HasExited && !process.WaitForExit(5000))
                {
                    process.Kill();
                    process.WaitForExit();
                }
            });
        }

        public Task<Process> Install()
        {
            return InstallOrUpdate("install");
        }

        public Task<Process> Update(bool validate = false, string custom = null)
        {
            return InstallOrUpdate("update");
        }

        private async Task<Process> InstallOrUpdate(string action)
        {
            Error = string.Empty;
            Notice = string.Empty;
            string serverFilesPath = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string stagingPath = Path.Combine(serverFilesPath, "__soldat_stage_" + Guid.NewGuid().ToString("N"));
            string archivePath = Path.Combine(stagingPath, "soldat-server.zip");
            string extractPath = Path.Combine(stagingPath, "extract");

            try
            {
                Directory.CreateDirectory(stagingPath);
                Directory.CreateDirectory(extractPath);
                using (var webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
                    await webClient.DownloadFileTaskAsync(new Uri(PackageUrl), archivePath);
                }

                var archiveInfo = new FileInfo(archivePath);
                if (archiveInfo.Length != PackageSize || !VerifySha256(archivePath, PackageSha256))
                {
                    throw new InvalidDataException("The official Soldat server package failed size or SHA-256 verification.");
                }
                if (!await FileManagement.ExtractArchive(archivePath, extractPath))
                {
                    throw new InvalidDataException("The official Soldat server package could not be safely extracted.");
                }

                string packageRoot = FindPackageRoot(extractPath);
                CopyDirectory(packageRoot, serverFilesPath);
                File.WriteAllText(Path.Combine(serverFilesPath, VersionFileName), PackageVersion, new UTF8Encoding(false));
            }
            catch (WebException exception)
            {
                Error = $"Failed to {action} Soldat: {exception.Message}";
            }
            catch (InvalidDataException exception)
            {
                Error = exception.Message;
            }
            catch (IOException exception)
            {
                Error = $"Failed to {action} Soldat: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = $"Failed to {action} Soldat: {exception.Message}";
            }
            catch (CryptographicException exception)
            {
                Error = $"Failed to verify the Soldat package: {exception.Message}";
            }
            finally
            {
                if (Directory.Exists(stagingPath))
                {
                    try
                    {
                        Directory.Delete(stagingPath, true);
                    }
                    catch (IOException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary Soldat files: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Error = AppendError(Error, "Could not remove temporary Soldat files: " + exception.Message);
                    }
                }
            }

            return null;
        }

        private static string FindPackageRoot(string extractPath)
        {
            string[] executablePaths = Directory.GetFiles(extractPath, "soldatserver.exe", SearchOption.AllDirectories);
            if (executablePaths.Length != 1)
            {
                throw new InvalidDataException("The official Soldat package did not contain one expected soldatserver.exe.");
            }

            string packageRoot = Path.GetDirectoryName(executablePaths[0]);
            foreach (string requiredFile in new[] { "soldat.ini", "server.ini", "mapslist.txt" })
            {
                if (!File.Exists(Path.Combine(packageRoot, requiredFile)))
                {
                    throw new InvalidDataException($"The official Soldat package is missing {requiredFile}.");
                }
            }
            return packageRoot;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            foreach (string sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourceFile.Substring(sourceDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationPath = Path.Combine(destinationDirectory, relativePath);
                if (ShouldPreserveServerFile(relativePath) && File.Exists(destinationPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourceFile, destinationPath, true);
            }
        }

        private static bool ShouldPreserveServerFile(string relativePath)
        {
            string normalizedPath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string fileName = Path.GetFileName(normalizedPath);
            return string.Equals(fileName, "soldat.ini", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "server.ini", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "mapslist.txt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "weapons.ini", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "weapons_realistic.ini", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith("maps" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith("scripts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

        private static string AppendError(string currentError, string message)
        {
            return string.IsNullOrWhiteSpace(currentError) ? message : currentError + Environment.NewLine + message;
        }

        public bool IsInstallValid()
        {
            return ValidateInstall(ServerPath.GetServersServerFiles(_serverData.ServerID));
        }

        public bool IsImportValid(string path)
        {
            return ValidateInstall(path);
        }

        private bool ValidateInstall(string serverFilesPath)
        {
            Error = string.Empty;
            string executablePath = Path.Combine(serverFilesPath, StartPath);
            if (!File.Exists(executablePath))
            {
                Error = $"Soldat server executable was not found ({executablePath}).";
                return false;
            }

            foreach (string requiredFile in new[] { "soldat.ini", "server.ini", "mapslist.txt" })
            {
                if (!File.Exists(Path.Combine(serverFilesPath, requiredFile)))
                {
                    Error = $"Soldat server file is missing: {requiredFile}.";
                    return false;
                }
            }

            return true;
        }

        public string GetLocalBuild()
        {
            string versionPath = ServerPath.GetServersServerFiles(_serverData.ServerID, VersionFileName);
            try
            {
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath).Trim();
                }
                return IsInstallValid() ? "unknown" : string.Empty;
            }
            catch (IOException exception)
            {
                Error = "Failed to read the local Soldat version: " + exception.Message;
                return string.Empty;
            }
            catch (UnauthorizedAccessException exception)
            {
                Error = "Failed to read the local Soldat version: " + exception.Message;
                return string.Empty;
            }
        }

        public Task<string> GetRemoteBuild()
        {
            return Task.FromResult(PackageVersion);
        }
    }
}
