using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NCrontab;
using WindowsGSM.Functions;
using WindowsGSM.Localization;

namespace WindowsGSM
{
    public partial class MainWindow
    {
        private readonly IHardwareTemperatureSource _dashboardTemperatureSource = new LibreHardwareTemperatureSource();
        private readonly CancellationTokenSource _dashboardRefreshCancellation = new CancellationTokenSource();
        private string _lastDashboardTemperatureError;
        private string _dashboardAttentionServerId;
        private bool _dashboardAttentionOpensSchedules;

        private async void DashboardContent_Loaded(object sender, RoutedEventArgs e)
        {
            hMenu_Dashboard.Loaded -= DashboardContent_Loaded;
            CancellationToken cancellationToken = _dashboardRefreshCancellation.Token;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<DashboardServerSnapshot> servers = ServerGroups_GetAllServerRows()
                        .Select(server => new DashboardServerSnapshot(server))
                        .ToList();
                    DashboardExtendedSnapshot snapshot = await Task.Run(() => BuildDashboardExtendedSnapshot(servers));
                    cancellationToken.ThrowIfCancellationRequested();
                    UpdateDashboardExtendedSnapshot(snapshot);
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private DashboardExtendedSnapshot BuildDashboardExtendedSnapshot(IReadOnlyList<DashboardServerSnapshot> servers)
        {
            HardwareTemperatureSample temperature = _dashboardTemperatureSource.Sample();
            var storageLocations = new List<StorageLocation>
            {
                new StorageLocation(LocalizationManager.Get("DashboardStorageLocationApp"), WGSM_PATH)
            };

            string serverFilesLabel = LocalizationManager.Get("DashboardStorageLocationServers");
            string backupsLabel = LocalizationManager.Get("DashboardStorageLocationBackups");
            foreach (DashboardServerSnapshot server in servers)
            {
                storageLocations.Add(new StorageLocation(serverFilesLabel, ServerPath.GetServersServerFiles(server.ID)));
                try
                {
                    string backupPath = BackupConfig.GetConfiguredBackupLocation(server.ID);
                    if (string.IsNullOrWhiteSpace(backupPath))
                    {
                        backupPath = Path.Combine(WGSM_PATH, ServerPath.FolderName.Backups, server.ID);
                    }
                    storageLocations.Add(new StorageLocation(backupsLabel, backupPath));
                }
                catch (Exception exception) when (IsExpectedBackupFileException(exception))
                {
                    storageLocations.Add(new StorageLocation(backupsLabel, string.Empty, exception.Message));
                }
            }

            List<StorageHeadroom> storage = SystemMetrics.GetStorageHeadroom(storageLocations);
            int runningCount = servers.Count(server => server.StatusCode == ServerStatus.Started);
            int stoppedCount = servers.Count(server => server.StatusCode == ServerStatus.Stopped);
            int healthWarningCount = servers.Count(server =>
                server.HealthStatus == ServerHealthStatus.Warning || server.HealthStatus == ServerHealthStatus.Unhealthy);

            DateTime now = DateTime.Now;
            DateTime? nextBackupAt = null;
            string nextBackupServerId = null;
            string nextBackupServerName = null;
            DateTime? backupAttentionAt = null;
            string backupAttentionServerId = null;
            string backupAttentionServerName = null;
            string backupAttentionResult = null;
            string backupAttentionMessage = null;

            foreach (DashboardServerSnapshot server in servers)
            {
                BackupScheduleConfig scheduleConfig;
                try
                {
                    scheduleConfig = BackupScheduleConfig.Load(server.ID);
                }
                catch (Exception exception) when (IsExpectedDashboardScheduleException(exception))
                {
                    backupAttentionAt = now;
                    backupAttentionServerId = server.ID;
                    backupAttentionServerName = server.Name;
                    backupAttentionResult = "ConfigurationError";
                    backupAttentionMessage = exception.Message;
                    continue;
                }

                if (scheduleConfig.Enabled)
                {
                    CrontabSchedule schedule = CrontabSchedule.TryParse(scheduleConfig.CronExpression);
                    if (schedule == null)
                    {
                        backupAttentionAt = now;
                        backupAttentionServerId = server.ID;
                        backupAttentionServerName = server.Name;
                        backupAttentionResult = "Invalid";
                        backupAttentionMessage = LocalizationManager.Get("ScheduleInvalidMessage");
                    }
                    else if (TryGetNextBackupOccurrence(schedule, now, out DateTime occurrence) &&
                        (!nextBackupAt.HasValue || occurrence < nextBackupAt.Value))
                    {
                        nextBackupAt = occurrence;
                        nextBackupServerId = server.ID;
                        nextBackupServerName = server.Name;
                    }
                }

                if (IsDashboardBackupAttention(scheduleConfig.LastResult))
                {
                    DateTime attemptAt = scheduleConfig.LastAttemptedAt ?? scheduleConfig.LastEvaluatedAt ?? DateTime.MinValue;
                    if (!backupAttentionAt.HasValue || attemptAt >= backupAttentionAt.Value)
                    {
                        backupAttentionAt = attemptAt;
                        backupAttentionServerId = server.ID;
                        backupAttentionServerName = server.Name;
                        backupAttentionResult = scheduleConfig.LastResult;
                        backupAttentionMessage = scheduleConfig.LastResultMessage;
                    }
                }
            }

            DashboardServerSnapshot healthAttention = servers.FirstOrDefault(server =>
                server.HealthStatus == ServerHealthStatus.Warning || server.HealthStatus == ServerHealthStatus.Unhealthy);
            string actionServerId = backupAttentionServerId ?? healthAttention?.ID ?? nextBackupServerId;
            bool actionOpensSchedules = backupAttentionServerId != null || healthAttention == null;

            return new DashboardExtendedSnapshot(
                temperature,
                storage,
                runningCount,
                stoppedCount,
                healthWarningCount,
                nextBackupAt,
                nextBackupServerName,
                backupAttentionServerId,
                backupAttentionServerName,
                backupAttentionResult,
                backupAttentionMessage,
                actionServerId,
                actionOpensSchedules);
        }

        private void UpdateDashboardExtendedSnapshot(DashboardExtendedSnapshot snapshot)
        {
            string temperatureError = snapshot.Temperature.Error;
            string unavailableTemperature = string.IsNullOrWhiteSpace(temperatureError)
                ? LocalizationManager.Get("DashboardTemperatureUnavailable")
                : string.Format(LocalizationManager.Get("DashboardTemperatureError"), temperatureError);

            dashboard_cpu_temperature.Text = snapshot.Temperature.Cpu == null
                ? unavailableTemperature
                : FormatDashboardTemperature(snapshot.Temperature.Cpu);
            dashboard_gpu_temperature.Text = snapshot.Temperature.Gpus.Count == 0
                ? unavailableTemperature
                : string.Join(Environment.NewLine, snapshot.Temperature.Gpus.Select(FormatDashboardTemperature));
            dashboard_temperature_sample_time.Text = snapshot.Temperature.SampledAt.HasValue
                ? string.Format(
                    LocalizationManager.Get("DashboardTemperatureSampledAt"),
                    snapshot.Temperature.SampledAt.Value.ToString("g", CultureInfo.CurrentCulture))
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(temperatureError) && temperatureError != _lastDashboardTemperatureError)
            {
                Log("System", $"[NOTICE] Hardware temperature readings are unavailable: {temperatureError}");
            }
            _lastDashboardTemperatureError = temperatureError;

            bool lowStorage = snapshot.Storage.Any(volume => volume.IsLow);
            bool storageUnavailable = snapshot.Storage.Any(volume => !string.IsNullOrWhiteSpace(volume.Error));
            var storageLines = snapshot.Storage.Select(volume =>
            {
                string volumeName = string.IsNullOrWhiteSpace(volume.Volume) ? "-" : volume.Volume.TrimEnd('\\');
                if (!string.IsNullOrWhiteSpace(volume.Error) || !volume.FreeBytes.HasValue || !volume.FreePercent.HasValue)
                {
                    string error = volume.Error ?? LocalizationManager.Get("DashboardStorageNoReading");
                    return string.Format(LocalizationManager.Get("DashboardStorageUnavailable"), volumeName, volume.Description, error);
                }

                double freeGigabytes = volume.FreeBytes.Value / (1024.0 * 1024.0 * 1024.0);
                string line = string.Format(
                    LocalizationManager.Get("DashboardStorageVolumeLine"),
                    volumeName,
                    volume.Description,
                    freeGigabytes,
                    volume.FreePercent.Value);
                return volume.IsLow ? $"{LocalizationManager.Get("DashboardStorageLow")}: {line}" : line;
            }).ToList();

            dashboard_storage_headroom.Text = storageLines.Count == 0
                ? LocalizationManager.Get("DashboardStorageNoVolumes")
                : string.Join(Environment.NewLine, storageLines);
            dashboard_storage_headroom.Foreground = lowStorage || storageUnavailable ? Brushes.DarkOrange : Brushes.Gray;

            var fleetLines = new List<string>
            {
                string.Format(
                    LocalizationManager.Get("DashboardFleetCounts"),
                    snapshot.RunningCount,
                    snapshot.StoppedCount,
                    snapshot.HealthWarningCount),
                snapshot.NextBackupAt.HasValue
                    ? string.Format(
                        LocalizationManager.Get("DashboardFleetNextBackup"),
                        snapshot.NextBackupServerName,
                        snapshot.NextBackupAt.Value.ToString("g", CultureInfo.CurrentCulture))
                    : LocalizationManager.Get("DashboardFleetNoScheduledBackup"),
                GetDashboardBackupAttentionText(snapshot)
            };

            dashboard_fleet_summary.Text = string.Join(Environment.NewLine, fleetLines);
            button_dashboard_fleet_summary.ToolTip = LocalizationManager.Get("DashboardFleetAttentionTooltip");
            button_dashboard_fleet_summary.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.ActionServerId);
            _dashboardAttentionServerId = snapshot.ActionServerId;
            _dashboardAttentionOpensSchedules = snapshot.ActionOpensSchedules;
        }

        private static string FormatDashboardTemperature(TemperatureReading reading)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} ({1}): {2:0.0}\u00B0C",
                reading.DeviceName,
                reading.SensorName,
                reading.Celsius);
        }

        private static string GetDashboardBackupAttentionText(DashboardExtendedSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.BackupAttentionServerId))
            {
                return LocalizationManager.Get("DashboardFleetNoBackupAttention");
            }

            string result;
            if (snapshot.BackupAttentionResult == "ConfigurationError")
            {
                result = $"{LocalizationManager.Get("ScheduleConfigError")}: {snapshot.BackupAttentionMessage}";
            }
            else if (snapshot.BackupAttentionResult == "Invalid")
            {
                result = $"{LocalizationManager.Get("ScheduleInvalid")}: {snapshot.BackupAttentionMessage}";
            }
            else
            {
                string resultName = GetBackupScheduleResult(snapshot.BackupAttentionResult);
                string resultMessage = GetBackupScheduleMessage(snapshot.BackupAttentionMessage);
                result = string.IsNullOrWhiteSpace(resultMessage) ? resultName : $"{resultName}: {resultMessage}";
            }

            return string.Format(LocalizationManager.Get("DashboardFleetBackupAttention"), snapshot.BackupAttentionServerName, result);
        }

        private static bool IsDashboardBackupAttention(string result)
        {
            return result == BackupScheduleConfig.ResultFailed ||
                result == BackupScheduleConfig.ResultSkipped ||
                result == BackupScheduleConfig.ResultMissed ||
                result == BackupScheduleConfig.ResultInterrupted;
        }

        private static bool IsExpectedDashboardScheduleException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is InvalidDataException ||
                exception is Newtonsoft.Json.JsonException;
        }

        private void Dashboard_FleetSummary_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_dashboardAttentionServerId)) { return; }

            if (_dashboardAttentionOpensSchedules)
            {
                HamburgerMenuControl.SelectedIndex = 2;
                RefreshBackupSchedules(_dashboardAttentionServerId);
                BackupScheduleGrid.SelectedItem = BackupScheduleGrid.Items.Cast<BackupScheduleRow>()
                    .FirstOrDefault(row => row.ServerId == _dashboardAttentionServerId);
                return;
            }

            HamburgerMenuControl.SelectedIndex = 0;
            comboBox_ServerGroupFilter.SelectedValue = AllServerGroupsFilterId;
            ServerTable server = ServerGrid.Items.Cast<ServerTable>()
                .FirstOrDefault(item => item.ID == _dashboardAttentionServerId);
            if (server != null)
            {
                ServerGrid.SelectedItem = server;
                ServerGrid.ScrollIntoView(server);
            }
        }

        private void DashboardWindow_Closed(object sender, EventArgs e)
        {
            _dashboardRefreshCancellation.Cancel();
            _dashboardTemperatureSource.Dispose();
        }

        private sealed class DashboardServerSnapshot
        {
            public string ID { get; private set; }
            public string Name { get; private set; }
            public ServerStatus StatusCode { get; private set; }
            public ServerHealthStatus HealthStatus { get; private set; }

            public DashboardServerSnapshot(ServerTable server)
            {
                ID = server.ID;
                Name = server.Name;
                StatusCode = server.StatusCode;
                HealthStatus = server.HealthStatus;
            }
        }

        private sealed class DashboardExtendedSnapshot
        {
            public HardwareTemperatureSample Temperature { get; private set; }
            public List<StorageHeadroom> Storage { get; private set; }
            public int RunningCount { get; private set; }
            public int StoppedCount { get; private set; }
            public int HealthWarningCount { get; private set; }
            public DateTime? NextBackupAt { get; private set; }
            public string NextBackupServerName { get; private set; }
            public string BackupAttentionServerId { get; private set; }
            public string BackupAttentionServerName { get; private set; }
            public string BackupAttentionResult { get; private set; }
            public string BackupAttentionMessage { get; private set; }
            public string ActionServerId { get; private set; }
            public bool ActionOpensSchedules { get; private set; }

            public DashboardExtendedSnapshot(
                HardwareTemperatureSample temperature,
                List<StorageHeadroom> storage,
                int runningCount,
                int stoppedCount,
                int healthWarningCount,
                DateTime? nextBackupAt,
                string nextBackupServerName,
                string backupAttentionServerId,
                string backupAttentionServerName,
                string backupAttentionResult,
                string backupAttentionMessage,
                string actionServerId,
                bool actionOpensSchedules)
            {
                Temperature = temperature;
                Storage = storage;
                RunningCount = runningCount;
                StoppedCount = stoppedCount;
                HealthWarningCount = healthWarningCount;
                NextBackupAt = nextBackupAt;
                NextBackupServerName = nextBackupServerName;
                BackupAttentionServerId = backupAttentionServerId;
                BackupAttentionServerName = backupAttentionServerName;
                BackupAttentionResult = backupAttentionResult;
                BackupAttentionMessage = backupAttentionMessage;
                ActionServerId = actionServerId;
                ActionOpensSchedules = actionOpensSchedules;
            }
        }
    }
}
