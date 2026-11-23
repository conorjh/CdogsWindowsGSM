using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NCrontab;
using WindowsGSM.Functions;
using WindowsGSM.Localization;

namespace WindowsGSM
{
    public partial class MainWindow
    {
        private bool TryGetBackupScheduleEditorValues(
            BackupScheduleRow row,
            bool forAllServers,
            bool reportValidationErrors,
            out string scheduledAction,
            out string backupType,
            out string expression)
        {
            scheduledAction = comboBox_ScheduledAction.SelectedValue as string;
            backupType = comboBox_BackupScheduleType.SelectedValue as string;
            expression = string.Empty;

            if (scheduledAction != BackupScheduleConfig.ScheduledActionBackup &&
                scheduledAction != BackupScheduleConfig.ScheduledActionUpdate &&
                scheduledAction != BackupScheduleConfig.ScheduledActionRestart)
            {
                if (reportValidationErrors)
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                }
                return false;
            }

            if (scheduledAction == BackupScheduleConfig.ScheduledActionBackup)
            {
                if (backupType != BackupScheduleConfig.BackupTypeFullServer &&
                    backupType != BackupScheduleConfig.BackupTypeConfiguration)
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }

                if (backupType == BackupScheduleConfig.BackupTypeConfiguration)
                {
                    List<ServerTable> servers = forAllServers ? ServerGroups_GetAllServerRows() : null;
                    bool supported = forAllServers
                        ? servers.Count > 0 && servers.All(server => IsConfigurationBackupSupported(server.Game))
                        : row != null && row.SupportsConfigurationBackup;
                    if (!supported)
                    {
                        if (reportValidationErrors)
                        {
                            SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ConfigurationBackupUnsupported"));
                        }
                        return false;
                    }
                }
            }
            else
            {
                backupType = BackupScheduleConfig.BackupTypeFullServer;
            }

            string frequency = comboBox_BackupScheduleFrequency.SelectedValue as string;
            if (frequency == "Custom")
            {
                if (forAllServers || row == null)
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }

                expression = row.CronExpression;
            }
            else
            {
                if (!DateTime.TryParseExact(
                    textBox_BackupScheduleTime.Text.Trim(),
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime scheduledTime))
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }

                if (frequency == "Daily")
                {
                    expression = string.Format(CultureInfo.InvariantCulture, "{0} {1} * * *", scheduledTime.Minute, scheduledTime.Hour);
                }
                else if (frequency == "Weekly" &&
                    int.TryParse(comboBox_BackupScheduleDay.SelectedValue as string, NumberStyles.None, CultureInfo.InvariantCulture, out int dayOfWeek) &&
                    dayOfWeek >= 0 && dayOfWeek <= 6)
                {
                    expression = string.Format(CultureInfo.InvariantCulture, "{0} {1} * * {2}", scheduledTime.Minute, scheduledTime.Hour, dayOfWeek);
                }
                else
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }
            }

            CrontabSchedule schedule = string.IsNullOrWhiteSpace(expression) ? null : CrontabSchedule.TryParse(expression);
            bool validSchedule = schedule != null && TryGetNextBackupOccurrence(schedule, DateTime.Now, out DateTime _);
            if ((frequency != "Custom" || switch_BackupScheduleEnabled.IsOn) && !validSchedule)
            {
                if (reportValidationErrors)
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                }
                return false;
            }

            return true;
        }
    }
}
