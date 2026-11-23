using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System;

namespace WindowsGSM.GameServer
{
    /// <summary>
    /// 7 Days to Die does not reliably accept redirected stdin or window input for shutdown.
    /// Use its configured, authenticated Telnet interface instead.
    /// </summary>
    class SDTD
    {
        private static readonly TimeSpan TelnetTimeout = TimeSpan.FromSeconds(20);
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "7 Days to Die Dedicated Server";
        public string StartPath = "7DaysToDieServer.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = null;

        public string Port = "26900";
        public string QueryPort = "26900";
        public string Defaultmap = "Navezgane";
        public string Maxplayers = "8";
        public string Additional = string.Empty;

        public string AppId = "294420";

        public SDTD(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG()
        {
            //Download serverconfig.xml
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "serverconfig.xml");
            if (await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                string telnetPassword = _serverData.GetRCONPassword();
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{hostname}}", _serverData.ServerName);
                configText = configText.Replace("{{rcon_password}}", _serverData.GetRCONPassword());
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                configText = configText.Replace("{{telnetPort}}", (int.Parse(_serverData.ServerPort) - int.Parse(Port) + 8081).ToString());
                configText = configText.Replace("{{telnet_password}}", telnetPassword);
                configText = configText.Replace("{{maxplayers}}", Maxplayers);
                XDocument configuration = XDocument.Parse(configText);
                SetTelnetProperty(configuration, "TelnetEnabled", "true");
                SetTelnetProperty(configuration, "TelnetPort", (int.Parse(_serverData.ServerPort) - int.Parse(Port) + 8081).ToString());
                SetTelnetProperty(configuration, "TelnetPassword", telnetPassword);
                File.WriteAllText(configPath, configuration.ToString(SaveOptions.DisableFormatting));
            }

            //Create steam_appid.txt
            string txtPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "steam_appid.txt");
            File.WriteAllText(txtPath, "251570");
        }

        public async Task<Process> Start()
        {
            string exeName = "7DaysToDieServer.exe";
            string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
            string exePath = Path.Combine(workingDir, exeName);

            if (!File.Exists(exePath))
            {
                Error = $"{exeName} not found ({exePath})";
                return null;
            }

            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "serverconfig.xml");
            if (!File.Exists(configPath))
            {
                Notice = $"serverconfig.xml not found ({configPath})";
            }

            string logFile = @"7DaysToDieServer_Data\output_log_dedi__" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".txt";
            string param = $"-logfile \"{Path.Combine(workingDir, logFile)}\" -quit -batchmode -nographics -configfile=serverconfig.xml -dedicated {_serverData.ServerParam}";

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir,
                        FileName = exePath,
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized
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
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false,
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
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (p.HasExited)
            {
                return;
            }

            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "serverconfig.xml");
            (int port, string password) = ReadTelnetSettings(configPath);

            using var timeout = new CancellationTokenSource(TelnetTimeout);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);

                using NetworkStream stream = client.GetStream();
                await ReadUntilAnyAsync(stream, timeout.Token, "Please enter password:");
                await WriteTelnetLineAsync(stream, password, timeout.Token);

                string authenticationResponse = await ReadUntilAnyAsync(
                    stream,
                    timeout.Token,
                    "Logon successful",
                    "Password incorrect",
                    "Too many failed login attempts");
                if (!authenticationResponse.Contains("Logon successful", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("7 Days to Die rejected the configured Telnet password.");
                }

                await WriteTelnetLineAsync(stream, "shutdown", timeout.Token);
                await WaitForShutdownCommandAsync(p, stream, timeout.Token);
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("7 Days to Die did not complete its Telnet shutdown exchange within 20 seconds.", exception);
            }
        }

        private static (int Port, string Password) ReadTelnetSettings(string configPath)
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("7 Days to Die serverconfig.xml was not found.", configPath);
            }

            XDocument configuration = XDocument.Load(configPath);
            string telnetEnabled = ReadTelnetProperty(configuration, "TelnetEnabled");
            if (!bool.TryParse(telnetEnabled, out bool isEnabled) || !isEnabled)
            {
                throw new InvalidOperationException("7 Days to Die Telnet is disabled or not configured.");
            }

            string telnetPort = ReadTelnetProperty(configuration, "TelnetPort");
            if (!int.TryParse(telnetPort, out int port) || port < 1 || port > 65535)
            {
                throw new InvalidDataException("7 Days to Die TelnetPort is missing or invalid.");
            }

            string password = ReadTelnetProperty(configuration, "TelnetPassword");
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidDataException("7 Days to Die TelnetPassword is missing.");
            }

            return (port, password);
        }

        private static string ReadTelnetProperty(XDocument configuration, string propertyName)
        {
            foreach (XElement property in configuration.Descendants())
            {
                if (property.Name.LocalName == "property" &&
                    string.Equals((string)property.Attribute("name"), propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)property.Attribute("value");
                }
            }

            return null;
        }

        private static void SetTelnetProperty(XDocument configuration, string propertyName, string value)
        {
            foreach (XElement property in configuration.Descendants())
            {
                XAttribute nameAttribute = property.Attribute("name");
                if (string.Equals(property.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase) &&
                    nameAttribute != null &&
                    string.Equals(nameAttribute.Value, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property.SetAttributeValue("value", value);
                    return;
                }
            }

            if (configuration.Root == null)
            {
                throw new InvalidDataException("7 Days to Die serverconfig.xml has no root element.");
            }

            configuration.Root.Add(new XElement(
                "property",
                new XAttribute("name", propertyName),
                new XAttribute("value", value)));
        }

        private static async Task<string> ReadUntilAnyAsync(NetworkStream stream, CancellationToken cancellationToken, params string[] markers)
        {
            var response = new StringBuilder();
            byte[] buffer = new byte[512];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    throw new IOException("7 Days to Die closed its Telnet connection before responding.");
                }

                response.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                string responseText = response.ToString();
                foreach (string marker in markers)
                {
                    if (responseText.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return responseText;
                    }
                }
            }
        }

        private static async Task WaitForShutdownCommandAsync(Process process, NetworkStream stream, CancellationToken cancellationToken)
        {
            var response = new StringBuilder();
            byte[] buffer = new byte[512];

            while (!process.HasExited)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    throw new IOException("7 Days to Die closed its Telnet connection before acknowledging shutdown.");
                }

                response.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                string responseText = response.ToString();
                if (responseText.IndexOf("Shutting server down...", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                if (responseText.IndexOf("*** ERROR:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException("7 Days to Die rejected the Telnet shutdown command.");
                }
            }
        }

        private static async Task WriteTelnetLineAsync(NetworkStream stream, string line, CancellationToken cancellationToken)
        {
            byte[] request = Encoding.UTF8.GetBytes(line + "\r\n");
            await stream.WriteAsync(request, 0, request.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId);
            Error = steamCMD.Error;

            return p;
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate, custom: custom);
            Error = error;
            return p;
        }

        public bool IsInstallValid()
        {
            string exeFile = "7DaysToDieServer.exe";
            string exePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, exeFile);

            return File.Exists(exePath);
        }

        public bool IsImportValid(string path)
        {
            string exeFile = "7DaysToDieServer.exe";
            string exePath = Path.Combine(path, exeFile);

            Error = $"Invalid Path! Fail to find {exeFile}";
            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}
