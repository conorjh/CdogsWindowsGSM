using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WindowsGSM.Functions
{
    class BackupConfig
    {
        private const int DefaultMaximumBackups = 3;

        static class SettingName
        {
            public const string BackupLocation = "backuplocation";
            public const string MaximumBackups = "maximumbackups";
        }

        private readonly string _serverId;
        public string BackupLocation;
        public int MaximumBackups = DefaultMaximumBackups;

        public BackupConfig(string serverId)
        {
            _serverId = serverId;
            string configPath = ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg");
            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, $"{SettingName.BackupLocation}=\"{Path.Combine(MainWindow.WGSM_PATH, "Backups", serverId)}\"{Environment.NewLine}{SettingName.MaximumBackups}=\"{DefaultMaximumBackups}\"");
            }

            LoadConfig();
        }

        public static string GetConfiguredBackupLocation(string serverId)
        {
            string configPath = ServerPath.GetServersConfigs(serverId, "BackupConfig.cfg");
            if (!File.Exists(configPath)) { return null; }

            foreach (string line in File.ReadLines(configPath))
            {
                string[] keyvalue = line.Split(new[] { '=' }, 2);
                if (keyvalue.Length == 2 && keyvalue[0] == SettingName.BackupLocation)
                {
                    return keyvalue[1].Trim('"');
                }
            }

            return null;
        }

        public void Open()
        {
            Process.Start(ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg"));
        }

        private void LoadConfig()
        {
            string configPath = ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg");
            foreach (string line in File.ReadLines(configPath))
            {
                string[] keyvalue = line.Split(new[] { '=' }, 2);
                if (keyvalue.Length == 2)
                {
                    keyvalue[1] = keyvalue[1].Trim('\"');
                    switch (keyvalue[0])
                    {
                        case SettingName.BackupLocation: BackupLocation = keyvalue[1]; break;
                        case SettingName.MaximumBackups: MaximumBackups = int.TryParse(keyvalue[1], out int max) ? ((max <= 0) ? 1 : max) : DefaultMaximumBackups; break;
                    }
                }
            }
        }
    }

    public sealed class BackupScheduleConfig
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultCronExpression = "0 2 * * *";
        public const string ResultRunning = "Running";
        public const string ResultCompleted = "Completed";
        public const string ResultSkipped = "Skipped";
        public const string ResultFailed = "Failed";
        public const string ResultMissed = "Missed";
        public const string ResultInterrupted = "Interrupted";
        public const string MessageServerBusy = "ServerBusy";
        public const string MessageMissed = "MissedWhileUnavailable";
        public const string MessageInterrupted = "Interrupted";
        public const string MessageCompleted = "BackupCompleted";
        public const string MessageFailed = "BackupFailed";

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public bool Enabled { get; set; }
        public string CronExpression { get; set; } = DefaultCronExpression;
        public DateTime? LastEvaluatedAt { get; set; }
        public DateTime? LastAttemptedOccurrence { get; set; }
        public DateTime? LastAttemptedAt { get; set; }
        public string LastResult { get; set; } = string.Empty;
        public string LastResultMessage { get; set; } = string.Empty;

        public static BackupScheduleConfig Load(string serverId)
        {
            string path = GetConfigPath(serverId);
            if (!File.Exists(path))
            {
                return new BackupScheduleConfig();
            }

            BackupScheduleConfig config = JsonConvert.DeserializeObject<BackupScheduleConfig>(File.ReadAllText(path));
            if (config == null)
            {
                throw new InvalidDataException("The backup schedule file contains no configuration.");
            }

            if (config.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported backup schedule schema version: {config.SchemaVersion}.");
            }

            return config;
        }

        public void Save(string serverId)
        {
            string path = GetConfigPath(serverId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string GetConfigPath(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }

            return ServerPath.GetServersConfigs(serverId, "BackupSchedule.json");
        }
    }

    public sealed class BackupScheduleRow
    {
        public string ServerId { get; private set; }
        public string ServerName { get; private set; }
        public string Game { get; private set; }
        public bool Enabled { get; private set; }
        public string CronExpression { get; private set; }
        public string NextRun { get; private set; }
        public string LastAttempt { get; private set; }
        public string LastResult { get; private set; }
        public string LastResultMessage { get; private set; }
        public string BackupStorage { get; private set; }
        public string ConfigurationError { get; private set; }

        public BackupScheduleRow(
            string serverId,
            string serverName,
            string game,
            bool enabled,
            string cronExpression,
            string nextRun,
            string lastAttempt,
            string lastResult,
            string lastResultMessage,
            string configurationError,
            string backupStorage)
        {
            ServerId = serverId;
            ServerName = serverName;
            Game = game;
            Enabled = enabled;
            CronExpression = cronExpression;
            NextRun = nextRun;
            LastAttempt = lastAttempt;
            LastResult = lastResult;
            LastResultMessage = lastResultMessage;
            ConfigurationError = configurationError;
            BackupStorage = backupStorage;
        }
    }
}
