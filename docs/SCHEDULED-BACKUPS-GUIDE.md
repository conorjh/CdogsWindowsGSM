# Scheduled backups

WindowsGSM can run a per-server backup schedule while the app is open. A schedule is not a Windows service: the computer must be available and WindowsGSM must be running when the scheduled time arrives.

## Configure a server schedule

1. Open **Scheduled Backups** from the app navigation and select a server in the schedule list.
2. Turn **Enabled** on.
3. Choose **Daily** or **Weekly**, set a time in `HH:mm` format, and choose a weekday for a weekly schedule. The page displays the time zone; scheduled times are local to that Windows machine.
4. Choose **Full server** or, only when offered for that exact game/plugin, **Configuration only**. New schedules default to disabled and full-server backup.
5. Review the displayed backup location and retention count, and check **Next run**, **Last attempt**, **Result**, and **Details** in the list. Valid selections are saved by the editor; after changing the time, leave the field and confirm the status reports that the schedule was saved.

The **Custom** frequency preserves an existing custom schedule unless you change it to Daily or Weekly. The visible editor does not provide a general cron-expression field; use the simple frequencies unless a custom schedule has already been configured by your maintainer.

## Run once and retention

Select a server and use **Run Now** to request a one-time backup using the currently selected backup type. The app asks for confirmation. The server must be stopped; if it is running or busy, the attempt is skipped rather than stopping the server.

Scheduled backups use the same per-server backup location and retention setting shown in the editor. Retention is applied after a backup completes. If the new backup succeeds but old archives cannot be removed, WindowsGSM reports a notice; check the server log and free disk space as needed.

## Safety and missed runs

- Stop the server before its scheduled time. WindowsGSM does not stop a running server to make a backup. A due run for a running/busy server is recorded as **Skipped** and is not replayed later.
- Keep WindowsGSM open and prevent the machine from sleeping through the scheduled time. If the app was closed or the machine was asleep, the occurrence is recorded as **Missed**; it is not replayed when the app starts again.
- An interrupted backup is reported as **Interrupted**. A failed backup is reported as **Failed** with details in the schedule view and server log. Investigate the cause rather than treating a previous successful backup as current.
- Keep enough free space at the displayed backup location. Full-server backups can include large game and server data; config-only backups include only approved paths and are not full recovery copies.
- Configuration archives (`.wgsmbackup`) are not encrypted. Protect them as carefully as the selected configuration files.

## Choosing a backup type

Full-server backup remains the default and uses the existing `.zip` format. Configuration-only is opt-in and is available only where WindowsGSM has a validated explicit plan. The current built-in plans cover Rust, 10 Source titles, 7 GoldSrc titles, and Call of Duty 1; code eligibility does not mean those providers have all passed live qualification. Plugin plans are also not runtime qualification. See the [config-only backup guide](CONFIG-ONLY-BACKUP-GUIDE.md) and the [built-in capability report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) before selecting that type.

For Alpha testing, use BK-04 in the [tester checklist](ALPHA-TESTER-CHECKLIST.md) and record the candidate, result, and any missed/skipped/error status.