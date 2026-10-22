# Server management features: schedules, health, groups, and dashboard

Status: phases 1–5 are implemented and build-verified. Dashboard hardware and group-interaction runtime checks remain.
Investigated: 21 October 2026.

## Goal and recommendation

Add four related improvements while keeping the existing Windows desktop workflow:

1. Add a **Schedules** section beside Server List, Discord, and Dashboard. Use it to review and configure per-server scheduled backups.
2. Add a small, modular server-health summary, starting with checks the app can report reliably.
3. Add user-defined server groups and safe, observable group actions.
4. Extend the Dashboard with hardware temperatures and actionable host/server summaries, without turning it into a heavy historical analytics system.

Implement the scheduling foundation first, then scheduled backups; build health checks, dashboard additions, and groups as separate, bounded features. Leave the existing automatic-update behavior unchanged. Reuse the existing server lifecycle, backup, query, Discord-alert, and configuration paths where possible instead of creating parallel implementations.

## Current implementation

Paths below are relative to the repository root.

| Area | Current behavior | Design implication |
|---|---|---|
| Main navigation | `CdogsWindowsGSM\MainWindow.xaml` has Server List (`hMenu_Home`), Schedules, Discord Bot, and Dashboard views. | Keep Schedules distinct from Discord and system metrics. |
| Dashboard metrics | `SystemMetrics` collects host CPU, RAM, and the disk containing the app; the Dashboard shows those values, installed/running server counts, active/peak players, and a player chart. No temperature or per-server CPU/RAM measurements were found. | Add temperature through a separate optional sensor provider; improve free-space and server-health visibility without replacing the existing dashboard. |
| Automatic updates | `MainWindow.xaml.cs` checks for a remote build every 30 minutes for a running server with `AutoUpdate` enabled. If it finds a newer version, it stops, updates, and restarts that server. | Preserve this existing behavior and setting; a new scheduled-update feature is not part of this plan. |
| Scheduled restarts | Per-server `RestartCrontab` and `CrontabFormat` settings use the existing NCrontab dependency. | Leave restart behavior and its current settings untouched in the first release; this plan does not require a restart-scheduler redesign. |
| Backups | Manual and scheduled backups require a stopped server. `BackupConfig` stores the backup location and maximum count (default three); archives are published from a temporary file before retention is applied. | Reuse the current backup/restore paths. Do not silently stop a running server to make a backup. |
| Server health | The app reports process and supported-query health in the server list, with per-check explanations and check times on selection. Unsupported queries are shown as unknown. | Keep checks bounded and reuse existing process/query state; unsupported checks must remain unknown, not failures. |
| Server groups | No user-defined server-group model or group assignment was identified in the current UI/configuration. | Add group metadata without changing a server's identity or its per-server settings. |

## 1. Schedules section

Add a navigation item named **Schedules** alongside the existing Server List, Discord, and Dashboard sections. The first version should make scheduled work understandable at a glance, with a list of jobs showing:

- Server name and game.
- Job type: backup.
- Enabled state and schedule in local time.
- Next planned run.
- Last attempt, result, and a concise reason when skipped or failed.
- A per-server edit/enable action and a run-now action where it is safe.

Keep schedules per server in the existing server configuration area (or a clearly versioned schedule file there), with one validated model used by the view and scheduler. Do not use the UI list as the source of truth. Validate schedules before saving and show invalid input instead of silently disabling a job.

Use the existing NCrontab dependency for recurring schedules unless implementation reveals a concrete limitation. Display the user's local time zone and define missed-run behavior: if the app is closed or the PC is asleep at the scheduled time, record/show the missed run and wait for the next occurrence; do not surprise users with a delayed backup at app startup. Scheduling runs only while WindowsGSM is open and the machine is awake; do not imply that it is a Windows service.

Use one application-level scheduler rather than starting an independent timer loop per server. It should prevent duplicate execution for the same backup occurrence, avoid running a backup concurrently with another operation on that server, and marshal server/UI actions through the existing application lifecycle paths. Persist the last attempted occurrence and result so a UI refresh or app restart cannot run the same occurrence twice.

## 2. Scheduled backups

### Behavior

- Allow a recurring backup schedule per server, with enable/disable, next-run display, last-result display, and a manual **Run now** action.
- Initially create scheduled backups only when the server is stopped, matching the current manual-backup safety rule. If it is running when a backup becomes due, skip that occurrence, record why, and show the next run. Do not stop or restart a server without an explicit future opt-in design.
- Write the archive to a temporary/in-progress name and publish it as a completed backup only after the existing backup operation finishes successfully.
- On failure, report the error in the schedule result and server log, remove only the incomplete archive, and preserve existing backups. Apply retention only after a successful backup operation.
- Respect each server's configured backup directory and maximum-backup count. Expose the current location and retention count in the schedule editor without changing existing backup/restore formats.
- Optionally use the existing Discord webhook alert path for completion/failure, respecting the server's alert settings and avoiding repeated alerts for the same result.

### Acceptance checks

- A due job for a stopped server creates a backup using the existing backup operation and records its successful completion.
- A due job for a running server is skipped with a clear reason; it does not stop the server or create a success-shaped result.
- An invalid schedule is rejected with actionable validation feedback.
- A failed backup operation does not replace existing backups or trigger retention deletion.
- Retention keeps the configured number of backups; restore continues to recognize existing backup filenames.
- Disabled, missed, duplicate, and overlapping jobs have deterministic visible outcomes.

## 3. Modular server health

Keep the first version deliberately small. Use a common result shape—check name, status (`Healthy`, `Warning`, `Unhealthy`, or `Unknown`), short explanation, and last-checked time—and a small optional check/provider interface. Avoid a general rules engine or large diagnostic framework.

Start with:

1. **Process check:** report whether the server process is running or has exited, using the process state already tracked by the app.
2. **Query check:** for server types with an existing supported query method, report whether a recent query succeeded. Unsupported or unconfigured query types show **Unknown** rather than unhealthy.

Show one compact overall status in the server list, with per-check details available on selection or expansion. Keep checks bounded and rate-limited; do not add a second query loop where existing query results can be reused. A check failure must not itself trigger a restart. Discord notifications can be added for health-state transitions using existing alert settings, with deduplication so a persistent outage does not spam.

The optional provider boundary lets additional game types or plugins contribute a check later without changing the common presentation. Initially ship only checks that can be implemented from current process/query data.

### Acceptance checks

- Process start, stop, crash, and restart update the health result without showing stale state from a previous process.
- Supported query success/failure is reflected; unsupported query types remain Unknown and do not generate false alarms.
- A transient timeout does not crash the UI, create a restart loop, or generate repeated identical alerts.
- Health checks do not block the UI or interfere with normal query polling and server actions.

## 4. Server groups

Add user-defined named groups and a group selector/filter on the Server List. Users should be able to create, rename, and delete groups; assign or remove servers; and apply existing actions to the selected group.

- Persist group IDs/names and membership separately from game-specific server settings. Store stable server IDs, not row positions or display names.
- Deleting a group removes only the group and its memberships; it never deletes server files or server configuration.
- Reuse the existing per-server start, stop, restart, and update actions. Execute group actions sequentially, show progress and a per-server result, and report partial failure accurately.
- Confirm disruptive group actions such as stop, restart, and update. Do not claim the group action succeeded when one or more servers failed.
- Keep the existing global bulk actions and individual server actions working. Do not include group-based schedules in this first version.

### Acceptance checks

- Group membership survives application restart and remains correct if a server is renamed or the grid is reordered.
- Removing a server or deleting a group does not affect other servers or their files.
- Filters show the correct membership and can return to the full server list.
- Group operations run sequentially, respect each server's existing status guards, and show an accurate result for every member, including partial failure.
- Existing global and per-server actions continue to behave as before.

## 5. Dashboard improvements

Keep the current system metrics and player chart, but make the Dashboard more useful for spotting host or server problems. Prefer a compact summary with drill-through to the relevant server or Schedules view over adding another large data-heavy page.

### CPU and GPU temperatures

- Show CPU package temperature and GPU temperature for detected devices, with a clear sensor/device label and sample time. Where hardware exposes multiple useful readings, prefer a concise primary reading rather than flooding the dashboard with sensor names.
- Windows does not expose consistent CPU/GPU temperature readings through one universal interface. First verify a maintained hardware-sensor library and its supported hardware, licensing, target framework, and publish behavior. Hide provider-specific details behind a small optional temperature-source interface so the rest of the Dashboard does not depend on one vendor or library.
- Do not treat generic WMI/ACPI thermal-zone values as CPU/GPU readings without verifying the sensor mapping. If a reading is unsupported, missing, stale, or failed, display **Unavailable** (with a short reason where useful), never `0 °C` or a plausible-looking fallback.
- Poll sensors off the UI thread at a modest interval, reuse one sampler, and show the last successful sample time. Do not add automatic throttling, shutdown, or restart behavior based on temperature.
- Avoid one universal hard-coded warning temperature for all CPUs/GPUs. If thresholds are added, make them explicit/configurable or use a trustworthy provider's status; temperature display alone should not mark a server unhealthy.

### Other useful Dashboard additions

1. **Useful storage headroom:** show free space in GB and percent for the application drive and the distinct drives containing server files or backup locations. The current disk card reports the drive containing the app, which may not be where large servers/backups live. Make a low-space warning visible, and avoid counting the same drive more than once.
2. **Fleet attention summary:** summarize running/stopped servers and servers with health warnings, plus the next scheduled backup and latest backup result that need attention. Reuse the health and schedule data models rather than starting duplicate polling. Make a summary actionable by taking the user to the corresponding server/filter or Schedules entry.
3. **Per-server resource snapshot:** consider showing the highest CPU/RAM-consuming tracked server processes. Label values as process usage, distinct from host totals, and include them only when the process can be attributed reliably. If a game uses child processes that cannot be attributed, do not imply the number covers the whole server.

Keep this first dashboard extension focused on current values and actionable status, not long-term history. Retain the existing player chart and active/peak count; broader player analytics remain deferred.

### Acceptance checks

- Supported temperature sensors display the correct CPU/GPU label, current value, and recent sample time; unsupported and failing sensors show an unavailable state, not zero.
- Sensor sampling does not block the UI, create an excessive polling loop, or break startup/publishing on machines without supported sensors.
- Storage headroom refers to the actual application/server/backup volumes, reports free space in readable units, and de-duplicates shared drives.
- Fleet status and schedule/backup summaries agree with the underlying server and job state; selecting a summary navigates to the relevant detail.
- Per-server CPU/RAM values are shown only for reliably tracked processes and are not presented as host totals.
- Existing CPU/RAM/disk cards, player totals, and player chart continue to refresh as before.

## Delivery order

| Phase | Work | Exit evidence | Status |
|---|---|---|
| 1. Scheduling shell | Add the Schedules navigation/view, validated schedule model, next/last-run display, and one app-level scheduler. Keep it inert until a job is enabled. | Existing server navigation/actions still work; schedule validation, disabled jobs, local-time display, and missed-run behavior are clear. | Implemented; app project build succeeds. |
| 2. Scheduled backups | Add per-server backup schedules and retention behavior using existing backup paths. | Backup acceptance checks pass, including failed/incomplete archive creation and running-server skip cases. | Implemented; app project build succeeds. Runtime acceptance checks remain. |
| 3. Health summary | Add the common health result model and process/query checks, then add optional transition alerts if the result is useful without noise. | Supported and unsupported checks are clearly distinguished; lifecycle and no-spam checks pass. | Implemented; app project build succeeds. Runtime lifecycle checks remain. |
| 4. Dashboard additions | Validate a temperature provider, then add temperatures, storage headroom, and health/schedule summaries. Add per-server resource figures only if process attribution is reliable. | Sensor absence/failure is handled safely; dashboard values match their source and do not block existing refreshes. | Implemented; app project build succeeds. Hardware-dependent runtime checks remain. |
| 5. Server groups | Add group persistence, Server List filtering, and sequential group actions with progress/results. | Group and existing single/global actions pass the group acceptance checks. | Implemented; app project build succeeds. Runtime interaction checks remain. |

## Explicitly deferred

- Update rollback or staging. This is intentionally out of scope for this plan.
- Scheduled updates; the existing automatic-update behavior remains unchanged.
- Post-creation archive integrity/CRC verification for backups.
- Player history and richer player analytics. These remain out of scope; the existing player chart/count are retained.
- Long-term temperature, system-resource, and per-server resource history. The first Dashboard update shows current values/status only.
- Replacing or redesigning the existing scheduled-restart feature.
- A Windows service or background agent that runs schedules when the UI is closed.
- Automatic server shutdown for backups, live consistent backups, or group-based schedules.
- A generalized health rules engine, automatic remediation, or restart-on-health-failure.

These deferrals keep the first implementation focused on transparent backup scheduling, basic health visibility, actionable current dashboard data, and useful group actions.
