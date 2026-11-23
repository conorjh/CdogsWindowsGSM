using System;
using System.IO;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.GameServer
{
    class L4D : Source
    {
        public const string FullName = "Left 4 Dead Dedicated Server";
        public const string LegacyPluginFullName = "Left 4 Dead Dedicated Server [L4D.cs]";

        public override string Game => "left4dead";
        public override string AppId => "222840";
        public override string Port => "27015";
        public override string QueryPort => "27015";
        public override string Defaultmap => "l4d_hospital01_apartment";
        public override string Maxplayers => string.Empty;
        public override string Additional => "-nocrashdialog +clientport {{clientport}}";

        public L4D(ServerConfig serverData) : base(serverData)
        {
            this.serverData = serverData;
        }

        public new async void CreateServerCFG()
        {
            await InstallConfigOnly();
        }

        public override Task<bool> InstallConfigOnly()
        {
            Error = string.Empty;
            string serverParam = serverData.ServerParam;
            bool updateServerParam = false;
            if (!string.IsNullOrWhiteSpace(serverParam) && serverParam.Contains("{{clientport}}"))
            {
                if (!int.TryParse(serverData.ServerPort, out int serverPort) || serverPort <= 10)
                {
                    Error = "The Left 4 Dead server port must be greater than 10 to calculate its client port.";
                    return Task.FromResult(false);
                }

                serverParam = serverParam.Replace(
                    "{{clientport}}",
                    (serverPort - 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
                updateServerParam = true;
            }

            string configPath = ServerPath.GetServersServerFiles(serverData.ServerID, Game, "cfg", "server.cfg");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                if (!File.Exists(configPath))
                {
                    string config = $"hostname \"{EscapeConfigValue(serverData.ServerName)}\"{Environment.NewLine}" +
                        $"rcon_password \"{EscapeConfigValue(serverData.GetRCONPassword())}\"{Environment.NewLine}" +
                        "sv_lan 0" + Environment.NewLine;
                    File.WriteAllText(configPath, config);
                }

                if (!File.Exists(MapCyclePath))
                {
                    File.WriteAllText(MapCyclePath, string.Empty);
                }

                if (updateServerParam)
                {
                    ServerConfig.SetSetting(serverData.ServerID, ServerConfig.SettingName.ServerParam, serverParam);
                    serverData.ServerParam = serverParam;
                }

                return Task.FromResult(true);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                Error = $"Failed to create {FullName} configuration: {exception.Message}";
                return Task.FromResult(false);
            }
        }

        private static string EscapeConfigValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
