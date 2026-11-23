using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BackupScheduleCollectionTests
{
    private string _testRoot = string.Empty;
    private IDisposable? _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.BackupScheduleCollectionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _pathScope = ServerPath.UseRootForTesting(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _pathScope?.Dispose();
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void SaveAndLoad_RetainsMultipleActionsForOneServer()
    {
        const string serverId = "multiple-scheduled-tasks";
        var config = new BackupScheduleConfig
        {
            Tasks = new List<BackupScheduleTaskConfig>
            {
                new()
                {
                    Id = "backup",
                    Enabled = true,
                    CronExpression = "0 2 * * *",
                    ScheduledAction = BackupScheduleConfig.ScheduledActionBackup
                },
                new()
                {
                    Id = "update",
                    Enabled = true,
                    CronExpression = "30 3 * * 1",
                    ScheduledAction = BackupScheduleConfig.ScheduledActionUpdate
                },
                new()
                {
                    Id = "restart",
                    Enabled = true,
                    CronExpression = "0 4 * * 0",
                    ScheduledAction = BackupScheduleConfig.ScheduledActionRestart
                }
            }
        };

        config.Save(serverId);
        BackupScheduleConfig loaded = BackupScheduleConfig.Load(serverId);

        Assert.HasCount(3, loaded.Tasks);
        Assert.AreEqual(BackupScheduleConfig.ScheduledActionBackup, loaded.Tasks[0].ScheduledAction);
        Assert.AreEqual(BackupScheduleConfig.ScheduledActionUpdate, loaded.Tasks[1].ScheduledAction);
        Assert.AreEqual(BackupScheduleConfig.ScheduledActionRestart, loaded.Tasks[2].ScheduledAction);
        Assert.AreEqual("30 3 * * 1", loaded.Tasks[1].CronExpression);
    }

    [TestMethod]
    public void Load_LegacyScheduleUsesStableTaskIdBeforeItIsSaved()
    {
        const string serverId = "legacy-schedule-id";
        string configPath = ServerPath.GetServersConfigs(serverId, "BackupSchedule.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(
            configPath,
            "{\"SchemaVersion\":1,\"Enabled\":true,\"CronExpression\":\"0 2 * * *\",\"BackupType\":\"FullServer\"}");

        BackupScheduleConfig firstLoad = BackupScheduleConfig.Load(serverId);
        BackupScheduleConfig secondLoad = BackupScheduleConfig.Load(serverId);

        Assert.AreEqual(firstLoad.Tasks[0].Id, secondLoad.Tasks[0].Id);
    }

    [TestMethod]
    public void Calendar_GetPeriodStartUsesCultureAlignedWeeksAndMonthGrid()
    {
        DateTime selectedDate = new DateTime(2026, 11, 18);
        DayOfWeek firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        DateTime weekStart = BackupScheduleCalendar.GetPeriodStart(selectedDate, BackupScheduleCalendarView.Week);
        DateTime monthStart = BackupScheduleCalendar.GetPeriodStart(selectedDate, BackupScheduleCalendarView.Month);

        Assert.AreEqual(firstDayOfWeek, weekStart.DayOfWeek);
        Assert.IsTrue(weekStart <= selectedDate && selectedDate < weekStart.AddDays(7));
        Assert.AreEqual(firstDayOfWeek, monthStart.DayOfWeek);
        Assert.IsTrue(monthStart <= new DateTime(2026, 11, 1));
        Assert.IsTrue(monthStart.AddDays(42) > new DateTime(2026, 11, 30));
        Assert.AreEqual(42, BackupScheduleCalendar.GetPeriodDayCount(BackupScheduleCalendarView.Month));
    }

    [TestMethod]
    public void Calendar_GetOccurrencesIncludesMidnightAndHonorsDisplayLimit()
    {
        var task = new BackupScheduleTaskConfig
        {
            Enabled = true,
            CronExpression = "* * * * *"
        };
        DateTime day = new DateTime(2026, 11, 18);

        IReadOnlyList<DateTime> occurrences = BackupScheduleCalendar.GetOccurrences(task, day, maximumCount: 3);

        Assert.HasCount(3, occurrences);
        Assert.AreEqual(day, occurrences[0]);
        Assert.AreEqual(day.AddMinutes(1), occurrences[1]);
        Assert.AreEqual(day.AddMinutes(2), occurrences[2]);
    }
}
