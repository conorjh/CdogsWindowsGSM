using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using WindowsGSM.Functions;

namespace WindowsGSM.Installer
{
    /// <summary>
    /// This script is very old, so it doesn't written in the best practice, but at least it works
    /// </summary>
    public class SteamCMD
    {
        private static readonly string _exeFile = "steamcmd.exe";
        private static readonly Regex _remoteBuildIdRegex = new Regex("\"public\"\r\n.{0,}{\r\n.{0,}\"buildid\".{1,}\"(.*?)\"");
        private static string InstallPath => ServerPath.GetBin("steamcmd");
        private static string UserDataPath => Path.Combine(InstallPath, "userData.txt");
        private string _param;
        public string Error;

        public SteamCMD()
        {
            Directory.CreateDirectory(InstallPath);
        }

        private static async Task<bool> Download()
        {
            Directory.CreateDirectory(InstallPath);
            var exePath = Path.Combine(InstallPath, _exeFile);
            if (File.Exists(exePath)) { return true; }
            
            try
            {
                var zipPath = Path.Combine(InstallPath, "steamcmd.zip");
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", zipPath);
                }

                //Extract steamcmd.zip and delete the zip
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, InstallPath));
                await Task.Run(() => File.Delete(zipPath));

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Old parameter script
        public void SetParameter(string installDir, string modName, string appId, bool validate, bool loginAnonymous = true)
        {
            Error = string.Empty;
            _param = $"+force_install_dir \"{installDir}\"";

            if (loginAnonymous)
            {
                _param += " +login anonymous";
            }
            else
            {
                if (!File.Exists(UserDataPath))
                {
                    CreateUserDataTxtIfNotExist();
                }

                if (!TryLoadSteamCredentials(out string steamUser, out string steamPass, out string credentialError))
                {
                    _param = null;
                    Error = credentialError;
                    return;
                }

                _param += $" +login \"{steamUser}\" \"{steamPass}\"";
            }

            _param += (string.IsNullOrWhiteSpace(modName) ? string.Empty : $" +app_set_config 90 mod {modName}") + $" +app_update {appId}" + (validate ? " validate" : "");
            
            if (appId == "90")
            {
                //Install 4 more times if hlds.exe
                for (int i = 0; i < 4; i++)
                {
                    _param += $" +app_update {appId}" + (validate ? " validate" : string.Empty);
                }
            }

            _param += " +quit";
        }

        public static string GetParameter(string forceInstallDir, string appId, bool validate = true, bool loginAnonymous = true, string modName = null, string custom = null)
        {
            return TryGetParameter(
                forceInstallDir,
                appId,
                validate,
                loginAnonymous,
                modName,
                custom,
                out string parameter,
                out _)
                ? parameter
                : null;
        }

        private static bool TryGetParameter(
            string forceInstallDir,
            string appId,
            bool validate,
            bool loginAnonymous,
            string modName,
            string custom,
            out string parameter,
            out string errorMessage)
        {
            var sb = new StringBuilder();

            // Set up force_install_dir parameter
            sb.Append($"+force_install_dir \"{forceInstallDir}\"");

            // Set up login parameter
            if (loginAnonymous)
            {
                sb.Append(" +login anonymous");
            }
            else
            {
                if (!TryLoadSteamCredentials(out string username, out string password, out errorMessage))
                {
                    parameter = null;
                    return false;
                }

                sb.Append($" +login \"{username}\" \"{password}\"");
            }

            // Set up app_set_config parameter
            sb.Append(!string.IsNullOrWhiteSpace(modName) ? $" +app_set_config {appId} mod \"{modName}\"" : string.Empty);

            // Install 4 more times if hlds.exe (appId = 90)
            for (var i = 0; i < 4; i++)
            {
                // Set up app_update parameter
                sb.Append($" +app_update {appId}");

                // Set up app_update extra parameter
                sb.Append(!string.IsNullOrWhiteSpace(custom) ? $" {custom}" : string.Empty); // custom parameter like -beta latest_experimental

                // Set up app_update validate parameter
                sb.Append(validate ? " validate" : string.Empty);

                if (appId != "90") { break; }
            }

            // Set up quit parameter
            sb.Append(" +quit");

            parameter = sb.ToString();
            errorMessage = string.Empty;
            return true;
        }

        internal static bool TryParseSteamCredentials(
            string contents,
            out string username,
            out string password,
            out string errorMessage)
        {
            username = string.Empty;
            password = string.Empty;
            errorMessage = string.Empty;
            if (contents == null)
            {
                errorMessage = "Steam credential content is missing.";
                return false;
            }

            string parsedUsername = null;
            string parsedPassword = null;
            bool hasUsername = false;
            bool hasPassword = false;
            foreach (string rawLine in contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    errorMessage = "Steam credential entries must use key=\"value\" format.";
                    return false;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                if (string.Equals(key, "steamUser", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasUsername)
                    {
                        errorMessage = "Steam credential file contains duplicate steamUser entries.";
                        return false;
                    }

                    if (!TryParseQuotedCredential(value, out parsedUsername))
                    {
                        errorMessage = "steamUser must be a double-quoted value without embedded quotes.";
                        return false;
                    }

                    hasUsername = true;
                }
                else if (string.Equals(key, "steamPass", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasPassword)
                    {
                        errorMessage = "Steam credential file contains duplicate steamPass entries.";
                        return false;
                    }

                    if (!TryParseQuotedCredential(value, out parsedPassword))
                    {
                        errorMessage = "steamPass must be a double-quoted value without embedded quotes.";
                        return false;
                    }

                    hasPassword = true;
                }
            }

            if (string.IsNullOrWhiteSpace(parsedUsername) || string.IsNullOrWhiteSpace(parsedPassword))
            {
                errorMessage = "Steam credential file must contain nonempty steamUser and steamPass values.";
                return false;
            }

            username = parsedUsername;
            password = parsedPassword;
            return true;
        }

        private static bool TryParseQuotedCredential(string value, out string credential)
        {
            credential = string.Empty;
            if (value.Length < 2 || value[0] != '\"' || value[value.Length - 1] != '\"')
            {
                return false;
            }

            string parsedCredential = value.Substring(1, value.Length - 2);
            if (parsedCredential.Contains('\"'))
            {
                return false;
            }

            credential = parsedCredential;
            return true;
        }

        private static bool TryLoadSteamCredentials(out string username, out string password, out string errorMessage)
        {
            username = string.Empty;
            password = string.Empty;
            if (!File.Exists(UserDataPath))
            {
                errorMessage = "Steam credential file was not found.";
                return false;
            }

            try
            {
                return TryParseSteamCredentials(File.ReadAllText(UserDataPath), out username, out password, out errorMessage);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException)
            {
                username = string.Empty;
                password = string.Empty;
                errorMessage = $"Unable to read Steam credential file: {exception.Message}";
                return false;
            }
        }

        public async Task<Process> Run()
        {
            string installPath = InstallPath;
            string exePath = Path.Combine(installPath, _exeFile);
            if (!File.Exists(exePath))
            {
                //If steamcmd.exe not exists, download steamcmd.exe
                if (!await Download())
                {
                    Error = $"Fail to download {_exeFile}";
                    return null;
                }
            }

            if (_param == null)
            {
                if (string.IsNullOrWhiteSpace(Error))
                {
                    Error = "Steam account is not set";
                }

                return null;
            }

            //Console.WriteLine($"SteamCMD Param: {_param}");

            var firewall = new WindowsFirewall(_exeFile, exePath);
            if (!await firewall.IsRuleExist())
            {
                await firewall.AddRule();
            }

            Process p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = installPath,
                    FileName = exePath,
                    Arguments = _param,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            p.Start();

            return p;
        }

        public async Task<Process> Install(string serverId, string modName, string appId, bool validate = true, bool loginAnonymous = true)
        {
            return await InstallToPath(ServerPath.GetServersServerFiles(serverId), modName, appId, validate, loginAnonymous);
        }

        public async Task<Process> InstallToPath(string installPath, string modName, string appId, bool validate = true, bool loginAnonymous = true)
        {
            Directory.CreateDirectory(Path.Combine(installPath, "steamapps"));

            SetParameter(installPath, modName, appId, validate, loginAnonymous);
            Process p = await Run();
            SendEnterPreventFreeze(p);
            return p;
        }

        // New
        public static async Task<(Process, string)> UpdateEx(string serverId, string appId, bool validate = true, bool loginAnonymous = true, string modName = null, string custom = null, bool embedConsole = true)
        {
            return await UpdateExAtPath(ServerPath.GetServersServerFiles(serverId), serverId, appId, validate, loginAnonymous, modName, custom, embedConsole);
        }

        public static async Task<(Process, string)> UpdateExAtPath(string installPath, string serverId, string appId, bool validate = true, bool loginAnonymous = true, string modName = null, string custom = null, bool embedConsole = true)
        {
            if (!TryGetParameter(installPath, appId, validate, loginAnonymous, modName, custom, out string param, out string parameterError))
            {
                return (null, parameterError);
            }

            string steamCmdPath = InstallPath;
            string exePath = Path.Combine(steamCmdPath, _exeFile);
            if (!File.Exists(exePath) && !await Download())
            {
                return (null, "Unable to download steamcmd");
            }

            // Fix the SteamCMD issue
            Directory.CreateDirectory(Path.Combine(installPath, "steamapps"));

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = steamCmdPath,
                    FileName = exePath,
                    Arguments = param,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            if (embedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(serverId);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return (p, null);
            }

            p.Start();
            return (p, null);
        }

        // Old
        public async Task<bool> Update(string serverId, string modName, string appId, bool validate, bool loginAnonymous = true)
        {
            SetParameter(Functions.ServerPath.GetServersServerFiles(serverId), modName, appId, validate, loginAnonymous);

            Process p = await Run();
            if (p == null)
            {
                return false;
            }

            SendEnterPreventFreeze(p);

            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode != 0)
            {
                Error = $"Exit code: {p.ExitCode.ToString()}";
                return false;
            }

            return true;
        }

        private async void SendEnterPreventFreeze(Process p)
        {
            try
            {
                await Task.Delay(300000);

                // Send enter 3 times per 3 seconds
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(3000);

                    if (p == null || p.HasExited) { break; }
                    p.StandardInput.WriteLine(string.Empty);
                }

                // Wait 5 minutes
                await Task.Delay(300000);

                // Send enter 3 times per 3 seconds
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(3000);

                    if (p == null || p.HasExited) { break; }
                    p.StandardInput.WriteLine(string.Empty);
                }
            }
            catch
            {

            }
        }

        public string GetLocalBuild(string serverId, string appId)
        {
            return GetLocalBuildAtPath(ServerPath.GetServersServerFiles(serverId), appId);
        }

        public string GetLocalBuildAtPath(string installPath, string appId)
        {
            string manifestFile = $"appmanifest_{appId}.acf";
            string manifestPath = Path.Combine(installPath, "steamapps", manifestFile);

            if (!File.Exists(manifestPath))
            {
                Error = $"{manifestFile} is missing.";
                return string.Empty;
            }

            string text;
            try
            {
                text = File.ReadAllText(manifestPath);
            }
            catch (Exception e)
            {
                Error = $"Fail to get local build {e.Message}";
                return string.Empty;
            }

            Regex regex = new Regex("\"buildid\".{1,}\"(.*?)\"");
            var matches = regex.Matches(text);

            if (matches.Count != 1 || matches[0].Groups.Count != 2)
            {
                Error = $"Fail to get local build";
                return string.Empty;
            }

            return matches[0].Groups[1].Value;
        }

        public async Task<string> GetRemoteBuild(string appId)
        {
            string installPath = InstallPath;
            string exePath = Path.Combine(installPath, "steamcmd.exe");
            if (!File.Exists(exePath))
            {
                //If steamcmd.exe not exists, download steamcmd.exe
                if (!await Download())
                {
                    Error = "Fail to download steamcmd.exe";
                    return string.Empty;
                }
            }

            WindowsFirewall firewall = new WindowsFirewall("steamcmd.exe", exePath);
            if (!await firewall.IsRuleExist())
            {
                await firewall.AddRule();
            }

            // Removes appinfo.vdf as a fix for not always getting up to date version info from SteamCMD.
            await Task.Run(() =>
            {
                string vdfPath = Path.Combine(installPath, "appcache", "appinfo.vdf");
                try
                {
                    if (File.Exists(vdfPath))
                    {
                        File.Delete(vdfPath);
                        Debug.WriteLine($"Deleted appinfo.vdf ({vdfPath})");
                    }
                }
                catch
                {
                    Debug.WriteLine($"File to delete appinfo.vdf ({vdfPath})");
                }
            });

            Process p = new Process
            {
                StartInfo =
                {
                    FileName = exePath,
                    //Sometimes it fails to get if appID < 90
                    Arguments = $"+login anonymous +app_info_update 1 +app_info_print {appId} +app_info_print {appId} +app_info_print {appId} +app_info_print {appId} +quit",
                    WindowStyle = ProcessWindowStyle.Minimized,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            };
            p.Start();
            SendEnterPreventFreeze(p);

            string output = await p.StandardOutput.ReadToEndAsync();
            if (!TryParseRemoteBuildId(output, out string buildId))
            {
                Error = $"Fail to get remote build";
                return string.Empty;
            }

            return buildId;
        }

        internal static bool TryParseRemoteBuildId(string output, out string buildId)
        {
            buildId = string.Empty;
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            MatchCollection matches = _remoteBuildIdRegex.Matches(output);
            if (matches.Count == 0 || matches[0].Groups.Count < 2)
            {
                return false;
            }

            string candidate = matches[0].Groups[1].Value;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            buildId = candidate;
            return true;
        }

        public void CreateUserDataTxtIfNotExist()
        {
            if (!File.Exists(UserDataPath))
            {
                File.Create(UserDataPath).Dispose();

                using (TextWriter textwriter = new StreamWriter(UserDataPath))
                {
                    textwriter.WriteLine("// For security and compatibility reasons, WindowsGSM suggests you to create a new steam account.");
                    textwriter.WriteLine("// More info: (https://docs.windowsgsm.com/installer/steamcmd)");
                    textwriter.WriteLine("// ");
                    textwriter.WriteLine("// Username and password - No Steam Guard             (Supported + Auto update supported) (Recommended)");
                    textwriter.WriteLine("// Username and password - Steam Guard via Email      (Supported + Auto update supported)");
                    textwriter.WriteLine("// Username and password - Steam Guard via Smartphone (Supported + Auto update NOT supported)");
                    textwriter.WriteLine("// ");
                    textwriter.WriteLine("steamUser=\"\"");
                    textwriter.WriteLine("steamPass=\"\"");
                }
            }
        }
    }
}
