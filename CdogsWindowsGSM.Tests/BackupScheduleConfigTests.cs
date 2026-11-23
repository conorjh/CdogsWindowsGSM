using System;
using System.IO;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BackupScheduleConfigTests
{
    private string _testRoot = string.Empty;
    private IDisposable? _pathScope;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.BackupScheduleConfigTests", Guid.NewGuid().ToString("N"));
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
    public void Load_LegacyBackupScheduleDefaultsToBackupAction()
    {
        const string serverId = "legacy-backup-schedule";
        string configPath = ServerPath.GetServersConfigs(serverId, "BackupSchedule.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "{\"SchemaVersion\":1,\"Enabled\":true,\"CronExpression\":\"0 2 * * *\",\"BackupType\":\"FullServer\"}");

        BackupScheduleConfig config = BackupScheduleConfig.Load(serverId);

        Assert.AreEqual(BackupScheduleConfig.ScheduledActionBackup, config.ScheduledAction);
        Assert.AreEqual(BackupScheduleConfig.BackupTypeFullServer, config.BackupType);
    }

    [TestMethod]
    public void SaveAndLoad_UpdateActionRoundTrips()
    {
        const string serverId = "update-schedule";
        var config = new BackupScheduleConfig
        {
            Enabled = true,
            CronExpression = "30 4 * * 1",
            ScheduledAction = BackupScheduleConfig.ScheduledActionUpdate
        };

        config.Save(serverId);
        BackupScheduleConfig loaded = BackupScheduleConfig.Load(serverId);

        Assert.AreEqual(BackupScheduleConfig.ScheduledActionUpdate, loaded.ScheduledAction);
        Assert.AreEqual("30 4 * * 1", loaded.CronExpression);
        Assert.AreEqual(BackupScheduleConfig.BackupTypeFullServer, loaded.BackupType);
        Assert.IsTrue(loaded.Enabled);
    }
}
