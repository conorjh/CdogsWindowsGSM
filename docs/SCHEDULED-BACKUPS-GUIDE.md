# Scheduled server tasks

WindowsGSM can run multiple independent backup, update, and restart schedules for each server while the app is open. A schedule is not a Windows service: the computer must be available and WindowsGSM must be running when the scheduled time arrives.

## Calendar views and multiple tasks

Use the **Calendar** and **List** views to review scheduled tasks. The calendar can show a day, week, or month; navigate dates with **Previous**, **Today**, and **Next**. Select a calendar event or a list row to edit it. Use **Add schedule** to create another task for the selected server, and **Remove** to delete the selected task. Existing single-task schedule files are migrated automatically and retain their action and run history.

## Configure a server schedule

1. Open **Schedules**, select the server, and add a schedule if it does not already have one.
2. Turn **Enabled** on.
3. Choose **Daily** or **Weekly**, set a time in `HH:mm` format, and choose a weekday for a weekly schedule. The page displays the time zone; scheduled times are local to that Windows machine.
4. Choose **Backup**, **Update**, or **Restart**. Each task has its own frequency and action, so a server can have backup and update schedules at different times. Backups can be **Full server** or, only when offered for that exact game/plugin, **Configuration only**. New tasks default to disabled full-server backups.
5. Review the backup location and retention count for backup tasks, and check **Next run**, **Last attempt**, **Result**, and **Details** in the list. Valid selections are saved by the editor; after changing the time, leave the field and confirm the status reports that the schedule was saved.

The **Custom** frequency preserves an existing custom schedule unless you change it to Daily or Weekly. The visible editor does not provide a general cron-expression field; use the simple frequencies unless a custom schedule has already been configured by your maintainer.

## Run once and retention

Select a task and use its **Run Now**, **Run Update Now**, or **Restart Now** action for a one-time run. The app asks for confirmation. Backups require a stopped server and are skipped if it is running or busy. An update gracefully stops a running server, updates it, and starts it again; a server that was already stopped remains stopped. A scheduled restart requires a running server; it is skipped if the server is stopped or busy.

Scheduled backups use the same per-server backup location and retention setting shown in the editor. Retention is applied after a backup completes. If the new backup succeeds but old archives cannot be removed, WindowsGSM reports a notice; check the server log and free disk space as needed.

## Safety and missed runs

- Backups never stop a running server. A due backup for a running/busy server is recorded as **Skipped** and is not replayed later. Scheduled updates may stop and restart a running server, and scheduled restarts interrupt player sessions; schedule both during an appropriate maintenance window.
- Keep WindowsGSM open and prevent the machine from sleeping through the scheduled time. If the app was closed or the machine was asleep, the occurrence is recorded as **Missed**; it is not replayed when the app starts again.
- An interrupted task is reported as **Interrupted**. A failed backup or update is reported as **Failed** with details in the schedule view and server log. Investigate the cause rather than treating a previous successful backup or update as current.
- Keep enough free space at the displayed backup location. Full-server backups can include large game and server data; config-only backups include only approved paths and are not full recovery copies.
- Configuration archives (`.wgsmbackup`) are not encrypted. Protect them as carefully as the selected configuration files.

## Choosing a backup type

Full-server backup remains the default and uses the existing `.zip` format. Configuration-only is opt-in and is available only where WindowsGSM has a validated explicit plan. The current built-in plans cover Rust, 10 Source titles, 7 GoldSrc titles, and Call of Duty 1; code eligibility does not mean those providers have all passed live qualification. Plugin plans are also not runtime qualification. See the [config-only backup guide](CONFIG-ONLY-BACKUP-GUIDE.md) and the [built-in capability report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) before selecting that type.

For Alpha testing, use BK-04 in the [tester checklist](ALPHA-TESTER-CHECKLIST.md) and record the candidate, result, and any missed/skipped/error status.