using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using WindowsGSM.Localization;

namespace WindowsGSM.GameServer
{
    /// <summary>
    ///
    /// Notes:
    /// Both RedirectStandardInput or RedirectStandardOutput cannot use on WindowsGSM.
    /// RedirectStandardOutput is possible but it will break the input, if used both, the server can run successfully but the input become useless again.
    ///
    /// The solution for this is don't use neither RedirectStandardInput nor RedirectStandardOutput.
    /// Just use the traditional method to handle the server.
    ///
    /// </summary>
    class RUST : Engine.ISharedInstallationProvider
    {
        private const string WebRconPasswordCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Rust Dedicated Server";
        public string StartPath = "RustDedicated.exe";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "28015";
        public string QueryPort = "28016";
        public string Defaultmap = "Procedural Map";
        public string Maxplayers = "50";
        public string Additional = string.Empty;

        public string AppId = "258550";
        public string SharedInstallationAppId => AppId;
        public string SharedInstallationExecutableName => StartPath;

        public RUST(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG()
        {
            await InstallConfigOnly();
        }

        public async Task<bool> InstallConfigOnly()
        {
            Error = string.Empty;
            if (UsesSharedInstallation)
            {
                Error = LocalizationManager.Get("RustSharedInstallationUnavailable");
                return false;
            }

            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.cfg");
            try
            {
                Directory.CreateDirectory(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID));
                if (File.Exists(configPath))
                {
                    return true;
                }

                if (!await Functions.Github.DownloadGameServerConfig(configPath, FullName))
                {
                    Error = "Could not download Rust server.cfg.";
                    return false;
                }

                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{rcon_password}}", GenerateWebRconPassword());
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                File.WriteAllText(configPath, configText);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Error = $"Failed to create Rust server.cfg: {exception.Message}";
                return false;
            }
        }

        public Task<Process> Start()
        {
            Error = string.Empty;
            Notice = string.Empty;
            if (UsesSharedInstallation)
            {
                Error = LocalizationManager.Get("RustSharedInstallationUnavailable");
                Notice = Error;
                return Task.FromResult<Process>(null);
            }

            try
            {
                string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.cfg");
                if (!File.Exists(configPath))
                {
                    Error = $"server.cfg not found ({configPath})";
                    Notice = Error;
                    return Task.FromResult<Process>(null);
                }

                string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
                string srcdsPath = Path.Combine(GetBinaryRoot(), StartPath);
                if (!File.Exists(srcdsPath))
                {
                    Error = $"{StartPath} not found ({srcdsPath})";
                    return Task.FromResult<Process>(null);
                }

                string param = "-nographics -batchmode -silent-crashes";
                param += $" {_serverData.ServerParam}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerName) ? string.Empty : $" +server.hostname \"{_serverData.ServerName}\"";
                param += string.IsNullOrWhiteSpace(_serverData.ServerIP) ? string.Empty : $" +server.ip {_serverData.ServerIP}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $" +server.port {_serverData.ServerPort}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerQueryPort) ? string.Empty : $" +server.queryport {_serverData.ServerQueryPort}";
                param += string.IsNullOrWhiteSpace(_serverData.ServerMap) ? string.Empty : $" +server.level \"{_serverData.ServerMap}\"";
                param += string.IsNullOrWhiteSpace(_serverData.ServerMaxPlayer) ? string.Empty : $" +server.maxplayers {_serverData.ServerMaxPlayer}";

                foreach (string line in File.ReadLines(configPath))
                {
                    param += $" {line}";
                }

                Process process = Functions.ServerConsole.StartInNewConsole(new ProcessStartInfo
                {
                    WorkingDirectory = workingDir,
                    FileName = srcdsPath,
                    Arguments = param.TrimEnd(),
                    UseShellExecute = false
                });
                if (process.HasExited)
                {
                    Error = "RustDedicated.exe did not start.";
                    process.Dispose();
                    return Task.FromResult<Process>(null);
                }

                return Task.FromResult(process);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                Error = exception.Message;
                return Task.FromResult<Process>(null);
            }
        }

        public async Task Stop(Process p)
        {
            Notice = string.Empty;
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (p.HasExited)
            {
                return;
            }

            int port;
            string password;
            try
            {
                string configPath;
                if (_serverData != null)
                {
                    configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server.cfg");
                }
                else
                {
                    string workingDirectory = Functions.ServerConsole.GetWorkingDirectory(p);
                    if (string.IsNullOrWhiteSpace(workingDirectory))
                    {
                        Notice = "Rust server settings were unavailable for WebRCON shutdown; using the server console instead.";
                        await StopThroughConsoleAsync(p);
                        return;
                    }

                    configPath = Path.Combine(workingDirectory, "server.cfg");
                }

                (port, password) = ReadWebRconSettings(configPath);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidDataException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is ArgumentException)
            {
                Notice = $"Rust WebRCON settings could not be read ({exception.Message}); using the server console instead.";
                await StopThroughConsoleAsync(p);
                return;
            }

            if (!IsWebRconPasswordUrlSafe(password))
            {
                Notice = "Rust WebRCON password uses characters outside the URL-safe alphabet; stopping through the server console instead.";
                await StopThroughConsoleAsync(p);
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            string stopStage = "WebRCON connection";
            try
            {
                var endpoint = new Uri($"ws://127.0.0.1:{port}/{Uri.EscapeDataString(password)}");
                ClientWebSocket connectedSocket = await ConnectWebRconAsync(p, port, endpoint, timeout.Token);
                if (connectedSocket == null)
                {
                    return;
                }

                using ClientWebSocket socket = connectedSocket;

                stopStage = "WebRCON command delivery";
                byte[] command = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Identifier = 1,
                    Message = "global.quit",
                    Name = "WindowsGSM"
                });
                await socket.SendAsync(new ArraySegment<byte>(command), WebSocketMessageType.Text, true, timeout.Token).WaitAsync(timeout.Token);

                stopStage = "WebRCON command response";
                string response;
                try
                {
                    response = await ReadWebRconResponseAsync(socket, timeout.Token);
                }
                catch (WebSocketException) when (p.HasExited)
                {
                    return;
                }

                if (p.HasExited)
                {
                    return;
                }

                if (response == null)
                {
                    throw new IOException("Rust closed the WebRCON connection before acknowledging shutdown.");
                }

                stopStage = "Rust process exit";
                await p.WaitForExitAsync().WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (p.HasExited)
                {
                    return;
                }

                Notice = $"Rust did not complete the {stopStage} stage of WebRCON shutdown within 25 seconds; trying the server console.";
                await StopThroughConsoleAsync(p);
            }
            catch (WebSocketException exception) when (!p.HasExited)
            {
                Notice = $"Rust WebRCON shutdown failed ({exception.Message}); trying the server console.";
                await StopThroughConsoleAsync(p);
            }
            catch (IOException exception) when (!p.HasExited)
            {
                Notice = $"Rust WebRCON shutdown failed ({exception.Message}); trying the server console.";
                await StopThroughConsoleAsync(p);
            }
            catch (InvalidOperationException exception) when (!p.HasExited)
            {
                Notice = $"Rust rejected the WebRCON shutdown ({exception.Message}); trying the server console.";
                await StopThroughConsoleAsync(p);
            }
        }

        internal static string GenerateWebRconPassword()
        {
            char[] password = new char[12];
            for (int i = 0; i < password.Length; i++)
            {
                password[i] = WebRconPasswordCharacters[RandomNumberGenerator.GetInt32(WebRconPasswordCharacters.Length)];
            }

            return new string(password);
        }

        internal static bool IsWebRconPasswordUrlSafe(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            foreach (char character in password)
            {
                if (WebRconPasswordCharacters.IndexOf(character) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task StopThroughConsoleAsync(Process process)
        {
            if (process.HasExited)
            {
                return;
            }

            try
            {
                Functions.ServerConsole.SendCommandToProcess(process, "quit");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Win32Exception exception) when (!process.HasExited)
            {
                Notice = AppendNotice(Notice, $"Rust console shutdown failed: {exception.Message}");
            }
            catch (InvalidOperationException exception) when (!process.HasExited)
            {
                Notice = AppendNotice(Notice, $"Rust console shutdown failed: {exception.Message}");
            }
            catch (IOException exception) when (!process.HasExited)
            {
                Notice = AppendNotice(Notice, $"Rust console shutdown failed: {exception.Message}");
            }
            catch (TimeoutException) when (!process.HasExited)
            {
                Notice = AppendNotice(Notice, "Rust did not exit after the console shutdown command; WindowsGSM will terminate it after the stop timeout.");
            }
        }

        private static string AppendNotice(string existingNotice, string message)
        {
            return string.IsNullOrWhiteSpace(existingNotice) ? message : existingNotice + " " + message;
        }

        private static (int Port, string Password) ReadWebRconSettings(string configPath)
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Rust server.cfg was not found.", configPath);
            }

            string webRcon = ReadRconSetting(configPath, "rcon.web");
            if (webRcon != "1" && (!bool.TryParse(webRcon, out bool enabled) || !enabled))
            {
                throw new InvalidOperationException("Rust WebRCON is disabled or not configured.");
            }

            string portValue = ReadRconSetting(configPath, "rcon.port");
            if (!int.TryParse(portValue, out int port) || port < 1 || port > 65535)
            {
                throw new InvalidDataException("Rust rcon.port is missing or invalid.");
            }

            string password = ReadRconSetting(configPath, "rcon.password");
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidDataException("Rust rcon.password is missing.");
            }

            return (port, password);
        }

        private static string ReadRconSetting(string configPath, string settingName)
        {
            foreach (string line in File.ReadLines(configPath))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && string.Equals(parts[0], "+" + settingName, StringComparison.OrdinalIgnoreCase))
                {
                    return parts[1].Trim().Trim('"');
                }
            }

            return null;
        }

        private static async Task<ClientWebSocket> ConnectWebRconAsync(Process process, int port, Uri endpoint, CancellationToken cancellationToken)
        {
            while (!process.HasExited)
            {
                if (!Functions.ServerPortDiscovery.IsTcpPortBoundToProcess(process, port))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    continue;
                }

                var socket = new ClientWebSocket();
                try
                {
                    await socket.ConnectAsync(endpoint, cancellationToken).WaitAsync(cancellationToken);
                    return socket;
                }
                catch (WebSocketException exception) when (IsTransientWebRconConnectionFailure(exception) && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                }
                finally
                {
                    if (socket.State != WebSocketState.Open)
                    {
                        socket.Dispose();
                    }
                }
            }

            return null;
        }

        private static bool IsTransientWebRconConnectionFailure(WebSocketException exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException &&
                    (socketException.SocketErrorCode == SocketError.ConnectionRefused ||
                     socketException.SocketErrorCode == SocketError.ConnectionReset ||
                     socketException.SocketErrorCode == SocketError.ConnectionAborted))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<string> ReadWebRconResponseAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).WaitAsync(cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                using JsonDocument response = JsonDocument.Parse(message.ToArray());
                JsonElement root = response.RootElement;
                if (!root.TryGetProperty("Identifier", out JsonElement identifier) ||
                    !identifier.TryGetInt32(out int responseIdentifier) ||
                    responseIdentifier != 1)
                {
                    continue;
                }

                string responseMessage = root.TryGetProperty("Message", out JsonElement messageValue)
                    ? messageValue.ToString()
                    : string.Empty;
                string responseType = root.TryGetProperty("Type", out JsonElement typeValue)
                    ? typeValue.ToString()
                    : string.Empty;
                if (string.Equals(responseType, "Error", StringComparison.OrdinalIgnoreCase) ||
                    responseMessage.IndexOf("unknown command", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException("Rust rejected the WebRCON shutdown command.");
                }

                return responseMessage;
            }
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId);
            Error = steamCMD.Error;

            return p;
        }

        public Task<Process> InstallSharedBinaries()
        {
            Error = LocalizationManager.Get("RustSharedInstallationUnavailable");
            return Task.FromResult<Process>(null);
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            if (UsesSharedInstallation)
            {
                Error = LocalizationManager.Get("RustSharedInstallationUnavailable");
                return null;
            }

            try
            {
                string binaryRoot = GetBinaryRoot();

                var (process, error) = await Installer.SteamCMD.UpdateExAtPath(binaryRoot, _serverData.ServerID, AppId, validate, custom: custom);
                Error = error;
                return process;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is Win32Exception)
            {
                Error = exception.Message;
                return null;
            }
        }

        public bool IsInstallValid()
        {
            try
            {
                return File.Exists(Path.Combine(GetBinaryRoot(), StartPath));
            }
            catch (ArgumentException exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        public bool IsImportValid(string path)
        {
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(Path.Combine(path, StartPath));
        }

        public string GetLocalBuild()
        {
            try
            {
                var steamCMD = new Installer.SteamCMD();
                string build = steamCMD.GetLocalBuildAtPath(GetBinaryRoot(), AppId);
                Error = steamCMD.Error;
                return build;
            }
            catch (ArgumentException exception)
            {
                Error = exception.Message;
                return string.Empty;
            }
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }

        private bool UsesSharedInstallation => !string.IsNullOrWhiteSpace(_serverData.SharedInstallationId);

        private string GetBinaryRoot()
        {
            return UsesSharedInstallation
                ? Functions.SharedInstallationStore.GetServerBinaryRoot(_serverData.SharedInstallationId)
                : Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
        }
    }
}
