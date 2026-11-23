using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Localization;

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
                File.WriteAllText(configPath, $"{SettingName.BackupLocation}=\"{Path.Combine(ServerPath.Get(), "Backups", serverId)}\"{Environment.NewLine}{SettingName.MaximumBackups}=\"{DefaultMaximumBackups}\"");
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
            Process.Start(new ProcessStartInfo(ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg")) { UseShellExecute = true });
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
        public const int CurrentSchemaVersion = 2;
        private const int LegacySchemaVersion = 1;
        public const string DefaultCronExpression = "0 2 * * *";
        public const string ScheduledActionBackup = "Backup";
        public const string ScheduledActionUpdate = "Update";
        public const string ScheduledActionRestart = "Restart";
        public const string BackupTypeFullServer = "FullServer";
        public const string BackupTypeConfiguration = "Configuration";
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
        public const string MessageUpdateCompleted = "UpdateCompleted";
        public const string MessageUpdateFailed = "UpdateFailed";
        public const string MessageRestartCompleted = "RestartCompleted";
        public const string MessageRestartFailed = "RestartFailed";

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<BackupScheduleTaskConfig> Tasks { get; set; } = new List<BackupScheduleTaskConfig>();

        private bool _hasLegacyValues;
        private bool _legacyEnabled;
        private string _legacyCronExpression = DefaultCronExpression;
        private string _legacyScheduledAction = ScheduledActionBackup;
        private string _legacyBackupType = BackupTypeFullServer;

        [JsonIgnore]
        public bool Enabled
        {
            get => Tasks?.FirstOrDefault()?.Enabled ?? _legacyEnabled;
            set { _legacyEnabled = value; _hasLegacyValues = true; }
        }

        [JsonIgnore]
        public string CronExpression
        {
            get => Tasks?.FirstOrDefault()?.CronExpression ?? _legacyCronExpression;
            set { _legacyCronExpression = value; _hasLegacyValues = true; }
        }

        [JsonIgnore]
        public string ScheduledAction
        {
            get => Tasks?.FirstOrDefault()?.ScheduledAction ?? _legacyScheduledAction;
            set { _legacyScheduledAction = value; _hasLegacyValues = true; }
        }

        [JsonIgnore]
        public string BackupType
        {
            get => Tasks?.FirstOrDefault()?.BackupType ?? _legacyBackupType;
            set { _legacyBackupType = value; _hasLegacyValues = true; }
        }

        public static BackupScheduleConfig Load(string serverId)
        {
            string path = GetConfigPath(serverId);
            if (!File.Exists(path))
            {
                return new BackupScheduleConfig();
            }

            JObject document = JObject.Parse(File.ReadAllText(path));
            int? schemaVersion = document["SchemaVersion"]?.Value<int>();
            if (schemaVersion == LegacySchemaVersion)
            {
                LegacyBackupScheduleConfig legacy = document.ToObject<LegacyBackupScheduleConfig>();
                if (legacy == null)
                {
                    throw new InvalidDataException("The backup schedule file contains no configuration.");
                }

                return new BackupScheduleConfig
                {
                    Tasks = new List<BackupScheduleTaskConfig>
                    {
                        new BackupScheduleTaskConfig
                        {
                            Id = "legacy-" + serverId,
                            Enabled = legacy.Enabled,
                            CronExpression = legacy.CronExpression,
                            ScheduledAction = legacy.ScheduledAction,
                            BackupType = legacy.BackupType,
                            LastEvaluatedAt = legacy.LastEvaluatedAt,
                            LastAttemptedOccurrence = legacy.LastAttemptedOccurrence,
                            LastAttemptedAt = legacy.LastAttemptedAt,
                            LastResult = legacy.LastResult,
                            LastResultMessage = legacy.LastResultMessage
                        }
                    }
                };
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported backup schedule schema version: {schemaVersion}.");
            }

            BackupScheduleConfig config = document.ToObject<BackupScheduleConfig>();
            if (config?.Tasks == null)
            {
                throw new InvalidDataException("The backup schedule file contains no task list.");
            }

            var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BackupScheduleTaskConfig task in config.Tasks)
            {
                if (task == null || string.IsNullOrWhiteSpace(task.Id) || !taskIds.Add(task.Id))
                {
                    throw new InvalidDataException("The backup schedule contains a missing or duplicate task ID.");
                }
                if (task.ScheduledAction != ScheduledActionBackup &&
                    task.ScheduledAction != ScheduledActionUpdate &&
                    task.ScheduledAction != ScheduledActionRestart)
                {
                    throw new InvalidDataException($"Unsupported scheduled action: {task.ScheduledAction}.");
                }
                if (task.BackupType != BackupTypeFullServer && task.BackupType != BackupTypeConfiguration)
                {
                    throw new InvalidDataException($"Unsupported backup type in schedule: {task.BackupType}.");
                }
            }

            return config;
        }

        public void Save(string serverId)
        {
            if (Tasks == null)
            {
                throw new InvalidDataException("The backup schedule task list is missing.");
            }

            if (Tasks.Count == 0 && _hasLegacyValues)
            {
                Tasks.Add(new BackupScheduleTaskConfig
                {
                    Enabled = _legacyEnabled,
                    CronExpression = _legacyCronExpression,
                    ScheduledAction = _legacyScheduledAction,
                    BackupType = _legacyBackupType
                });
                _hasLegacyValues = false;
            }

            SchemaVersion = CurrentSchemaVersion;
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

        private sealed class LegacyBackupScheduleConfig
        {
            public bool Enabled { get; set; }
            public string CronExpression { get; set; } = DefaultCronExpression;
            public string ScheduledAction { get; set; } = ScheduledActionBackup;
            public string BackupType { get; set; } = BackupTypeFullServer;
            public DateTime? LastEvaluatedAt { get; set; }
            public DateTime? LastAttemptedOccurrence { get; set; }
            public DateTime? LastAttemptedAt { get; set; }
            public string LastResult { get; set; } = string.Empty;
            public string LastResultMessage { get; set; } = string.Empty;
        }
    }

    public sealed class BackupScheduleTaskConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool Enabled { get; set; }
        public string CronExpression { get; set; } = BackupScheduleConfig.DefaultCronExpression;
        public string ScheduledAction { get; set; } = BackupScheduleConfig.ScheduledActionBackup;
        public string BackupType { get; set; } = BackupScheduleConfig.BackupTypeFullServer;
        public DateTime? LastEvaluatedAt { get; set; }
        public DateTime? LastAttemptedOccurrence { get; set; }
        public DateTime? LastAttemptedAt { get; set; }
        public string LastResult { get; set; } = string.Empty;
        public string LastResultMessage { get; set; } = string.Empty;
    }

    public sealed class BackupScheduleRow
    {
        public string ServerId { get; private set; }
        public string ScheduleId { get; private set; }
        public string ServerName { get; private set; }
        public string Game { get; private set; }
        public string ScheduledAction { get; private set; }
        public string ScheduledTaskDisplay => !string.IsNullOrWhiteSpace(ConfigurationError)
            ? LocalizationManager.Get("ScheduleConfigError")
            : string.IsNullOrWhiteSpace(ScheduleId)
                ? LocalizationManager.Get("ScheduleNoTaskSelected")
                : ScheduledAction switch
                {
                    BackupScheduleConfig.ScheduledActionUpdate => LocalizationManager.Get("ScheduleUpdate"),
                    BackupScheduleConfig.ScheduledActionRestart => LocalizationManager.Get("ScheduleRestart"),
                    _ => string.Format(LocalizationManager.Get("ScheduleBackupTask"), BackupTypeDisplay)
                };
        public string BackupType { get; private set; }
        public string BackupTypeDisplay => BackupType == BackupScheduleConfig.BackupTypeConfiguration
            ? LocalizationManager.Get("BackupTypeConfiguration")
            : LocalizationManager.Get("BackupTypeFullServer");
        public bool SupportsConfigurationBackup { get; private set; }
        public bool Enabled { get; private set; }
        public string CronExpression { get; private set; }
        public string ScheduleDescription { get; private set; }
        public string NextRun { get; private set; }
        public string LastAttempt { get; private set; }
        public string LastResult { get; private set; }
        public string LastResultMessage { get; private set; }
        public string BackupStorage { get; private set; }
        public string ConfigurationError { get; private set; }

        public BackupScheduleRow(
            string serverId,
            string scheduleId,
            string serverName,
            string game,
            string scheduledAction,
            string backupType,
            bool supportsConfigurationBackup,
            bool enabled,
            string cronExpression,
            string scheduleDescription,
            string nextRun,
            string lastAttempt,
            string lastResult,
            string lastResultMessage,
            string configurationError,
            string backupStorage)
        {
            ServerId = serverId;
            ScheduleId = scheduleId;
            ServerName = serverName;
            Game = game;
            ScheduledAction = scheduledAction;
            BackupType = backupType;
            SupportsConfigurationBackup = supportsConfigurationBackup;
            Enabled = enabled;
            CronExpression = cronExpression;
            ScheduleDescription = scheduleDescription;
            NextRun = nextRun;
            LastAttempt = lastAttempt;
            LastResult = lastResult;
            LastResultMessage = lastResultMessage;
            ConfigurationError = configurationError;
            BackupStorage = backupStorage;
        }
    }
}
