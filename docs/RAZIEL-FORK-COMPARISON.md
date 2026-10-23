# Fork comparison: CdogsWindowsGSM `dev` vs Raziel7893/WindowsGSM `master`

**Prepared:** 2026-10-22
**Compared revisions:** local `dev` at `f34b4b1` and Raziel `master` at `2f8e761`

## Scope and caveats

This report compares committed Git trees, not the current working files. The working tree is dirty, with local project, backup, UI, and localization edits plus untracked documentation, packages, and build output; all are deliberately excluded. In particular, `CdogsWindowsGSM/Functions/BackupConfig.cs` has uncommitted edits, so the backup comparison below describes committed `HEAD` only.

The compared branches share merge base `8293c10` (2023-12-06). Since then, **45 commits are unique to the current local branch and 162 are unique to Raziel's `master`**. The 162-commit count includes contributions from other developers. Local `dev` is also 19 commits ahead of the published `origin/dev` branch (`e778044`), so this report describes the newer local checkout, not only the version currently published on GitHub.

The raw tree diff reports 1,297 changed path entries, including 981 under `packages/`. The forks use different project paths/names, and the large vendored package area inflates the raw file and line totals; those numbers should not be read as a count of product features.

## Executive summary

Raziel's branch has evolved into a substantially different WindowsGSM distribution: it targets .NET 8, adds a per-server scheduled-command/RCON subsystem, extends backups to include multiple save roots, and deepens its Discord/webhook and query integrations. It also carries many more plugin-development examples and numerous reliability fixes.

The current local `dev` has now merged the .NET 10 modernization, so Raziel's .NET 8 project conversion is not a migration to copy. Local `dev` also has features that are absent from the compared Raziel tree, notably scheduled backups, server-port discovery, localization, and the server health summary. The branches are better treated as sources of selectively portable features than as candidates for a blind merge.

## Requested deep dives

These reports expand the Discord improvements and comparison sections 5 and 6 with implementation details, current-local overlap, porting cautions, and validation checklists:

- [Discord improvements](RAZIEL-DISCORD-IMPROVEMENTS.md)
- [Query handling and plugin samples (section 5)](RAZIEL-QUERY-PLUGIN-REPORT.md)
- [UI, process management, and reliability (section 6)](RAZIEL-UI-PROCESS-REPORT.md)

## Detailed feature comparison

### 1. Runtime, project structure, and dependencies

- **Raziel:** `WindowsGSM/WindowsGSM.csproj` targets `net8.0-windows8.0`.
- **Current local `dev`:** `CdogsWindowsGSM/CdogsWindowsGSM.csproj` at committed `HEAD` targets `net10.0-windows10.0.19041`.
- **Implication:** Both have moved beyond the original .NET Framework code, but they are on different runtime generations and have different project layouts. Port application behavior, not Raziel's project file or package versions. Confirm compatibility with the local .NET 10 branch's plugin compiler, updater, and dependency choices before integrating runtime-sensitive code.

The project and package changes also account for much of the large raw diff. Review dependencies by purpose and version rather than attempting to merge the vendored `packages/` trees wholesale.

### 2. Scheduled commands and RCON

Raziel adds `CrontabManager` and `RconClient`. Each server can have one or more CSV files under its configuration `Crontab` directory. Rows use a cron expression plus a type and command; documented actions include:

- `exec` to start a Windows command or executable;
- `ServerConsoleCommand` to send a command to the embedded server console;
- `RconCommand` to send an RCON command; and
- `restart` for scheduled server restarts.

The files support comments and multiple schedules. This is separate from the existing GUI restart schedule. RCON connection values are stored in the per-server configuration. The local committed `dev` has scheduled backups, but no matching general-purpose `CrontabManager` or `RconClient` was found.

**Security/operations:** `exec` is an arbitrary command-execution feature running with the WindowsGSM process's permissions. Raziel's own `Crontab.md` warns that the configuration must be admin-protected because the application runs elevated. Preserve strict access controls and make failures visible if this feature is ported.

References: [Crontab.md](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/Crontab.md), [CrontabManager.cs](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/Functions/CrontabManager.cs), [RconClient.cs](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/Functions/RconClient.cs), [initial CSV scheduler commit](https://github.com/Raziel7893/WindowsGSM/commit/bc8d58a), [RCON commit](https://github.com/Raziel7893/WindowsGSM/commit/36717ea).

### 3. Backup and restore behavior

Raziel's `BackupConfig.cfg` supports a backup archive location, a retention count, and a semicolon-separated `saveslocation` setting containing multiple save directories. The backup and restore flow can therefore include saves outside the server's primary `serverfiles` directory.

The current local `dev` already has a different backup improvement: committed local work adds a scheduled-backup configuration and UI, including per-server run/result state. At committed `HEAD`, however, the backup config does not contain Raziel's `SavesLocations` setting. These features are complementary: the useful port is multiple source directories within the local scheduled-backup flow, not replacement of that flow with Raziel's configuration class.

References: [Raziel BackupConfig.cs](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/Functions/BackupConfig.cs), [multi-save backup commit](https://github.com/Raziel7893/WindowsGSM/commit/1ae12fc), local commit `2642ccf` (`feat: add scheduled backups`).

### 4. Discord bot and webhook changes

Raziel's Discord work extends functionality beyond the existing bot/webhook integration:

- **Public-IP change alerts:** waits briefly after startup, then polls the public IP every two minutes and sends a webhook when it changes. The existing local `GetPublicIP()` lookup is used for a different purpose; the committed local tree does not contain Raziel's `CurrentPublicIp` polling/notification flow.
- **Join-code alerts:** scans server-console output for join-code text and can send it via the server webhook. The feature has a per-server toggle and is tied to the existing webhook alert settings; Raziel's change log notes that an embedded console is needed.
- **Console commands through Discord:** a `SendR` path can issue a console command and attempt to collect the response, also requiring a working embedded console.
- **Bot customization and authorization:** configurable bot name/avatar and a user ID of `0` as a default permission mapping. The target's prefix and slash paths do not consistently enforce channel and per-server restrictions, so do not assume this is channel-scoped.
- **Other bot improvements:** action/slash-command support, case-insensitive command handling, stop-all behavior, and fixes to Discord admin handling are present in the branch history.

The current local `dev` already has Discord integration and basic public-IP lookup; the changes above describe the additional behavior, not the introduction of Discord support from scratch.

References: [ChangesFromBase.md](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/ChangesFromBase.md), [DiscordBot/Commands.cs](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/DiscordBot/Commands.cs), [ServerConsole.cs](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/Functions/ServerConsole.cs), [public-IP webhook commit](https://github.com/Raziel7893/WindowsGSM/commit/caa3908), [latest join-code/process fix](https://github.com/Raziel7893/WindowsGSM/commit/2f8e761cdb288edabb1fa58968154ebcc37047db).

Detailed assessment: [Discord improvements deep dive](RAZIEL-DISCORD-IMPROVEMENTS.md).

### 5. Query handling and game-server plugins

Raziel adds a common query contract for A2S, FiveM, and UT3, a separate EOS query client, persisted query-port configuration, and mapping `0.0.0.0` to localhost for local query logic. The current local branch already has A2S, FiveM, and UT3 query files, but no shared `QueryTemplate` contract or EOS client. Its `ServerPortDiscovery.FindClientPort` discovers the client/main port from process-owned endpoints and explicitly removes the configured query port from candidates; that is a distinct local feature, not a replacement for query-protocol handling.

The branch adds **49 plugin-development sample files** under `WindowsGSM-Plugin-Development/Plugins`. Examples include ARK: Survival Ascended, Arma Reforger, Hytale, HumanitZ, Factorio, Core Keeper, Windrose, RedM, and Project Zomboid 42. These are plugin-development examples, not proof that all listed games are shipped as built-in integrations. Raziel also adds/updates core query and engine code, including EOS query support.

References: [plugin-development samples](https://github.com/Raziel7893/WindowsGSM/tree/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM-Plugin-Development/Plugins), [EOS query commit](https://github.com/Raziel7893/WindowsGSM/commit/45eff1a), [query interface rewrite](https://github.com/Raziel7893/WindowsGSM/commit/ea8309a), [query-port change](https://github.com/Raziel7893/WindowsGSM/commit/06cccb3), [0.0.0.0 query fix](https://github.com/Raziel7893/WindowsGSM/commit/e19cb73).

Detailed assessment: [query and plugin deep dive](RAZIEL-QUERY-PLUGIN-REPORT.md).

### 6. UI, process management, and operational fixes

Notable additions/fixes in Raziel's history include:

- a minimize-on-start option;
- a Minecraft installation path that explicitly requests Java 25 (the helper still defaults to Java 22, and another call site uses that default);
- parallel, awaited stop-all behavior (the local fork already has Stop All, but awaits stops sequentially);
- uptime-display work (the local fork already has a live uptime column, implemented differently);
- RAM detection changes for DDR4/DDR5 systems;
- safer behavior when plugins have no explicit stop code;
- Google Analytics failures no longer taking down the application; and
- a server-console crash fix for processes not owned by the current WindowsGSM process.

Raziel also persists per-server process-window visibility. The latest Raziel commit adds the join-code toggle. These are useful candidates for individual review; they are not a substitute for validating behavior against the local .NET 10 build. The local branch's explicit recovered-process handling should be preserved when evaluating the console fix.

References: [minimize-on-start commit](https://github.com/Raziel7893/WindowsGSM/commit/8337517), [uptime/shutdown/RAM and Discord dependency changes](https://github.com/Raziel7893/WindowsGSM/commit/ab9a2ea), [plugin stop behavior fix](https://github.com/Raziel7893/WindowsGSM/commit/4ac02cf), [Java 25 change](https://github.com/Raziel7893/WindowsGSM/commit/39d1db4), [analytics failure handling](https://github.com/Raziel7893/WindowsGSM/commit/f5e1c8e).

Detailed assessment: [UI, process, and reliability deep dive](RAZIEL-UI-PROCESS-REPORT.md).

### 7. Updater and release behavior

Raziel removes the old updater after identifying a path that could downgrade to the abandoned upstream WindowsGSM. The current local .NET 10 branch retains an updater flow, so this should be treated as a difference in update strategy, not as an automatic deletion to copy. Compare the updater's release source, asset naming, and handoff behavior before making any change.

Reference: [Raziel updater-removal commit](https://github.com/Raziel7893/WindowsGSM/commit/31fbc5c); local [`docs/DOTNET-10-MIGRATION-PLAN.md`](DOTNET-10-MIGRATION-PLAN.md).

## Current-branch features that are not Raziel additions

The committed local `dev` has several improvements of its own:

- .NET 10 modernization (merge `a538899`);
- scheduled backups (`2642ccf`);
- server health summary (`53982d7`);
- process-aware client/main-port discovery that excludes the configured query port (`467a02f`, `3f2a916`, refined in `b6ae6b8`);
- localization infrastructure/resources (`fa75f35` and subsequent translation work); and
- update-availability and server-list UI refinements (`a0de344`, `ba58217`).

The current local branch does not have the general CSV scheduler/RCON client, multi-save `SavesLocations` configuration, join-code toggle, or public-IP change webhook found in Raziel's committed tree. That statement is about `HEAD` only; the uncommitted local changes noted in the scope section may alter the comparison.

## Suggested porting order

1. **Multi-save backups:** add multiple save roots to the current backup/archive code while preserving its scheduled-run state and UI.
2. **Webhook additions:** consider public-IP change and join-code alerts. Test the current webhook settings, embedded-console behavior, and Discord message formatting.
3. **Query improvements and plugin samples:** review individual EOS/query changes and useful plugin templates against the local port-discovery implementation and .NET 10 compiler/runtime.
4. **Crontab/RCON:** treat this as a separate feature. Require admin-only configuration, validate schedule input, report execution failures, and test the privilege boundary before enabling arbitrary `exec` commands.
5. **Do not merge the project/runtime files wholesale:** local `dev` is already on .NET 10, whereas Raziel is on .NET 8. Selectively port compatible behavior and dependencies instead.

## Primary references

- [Raziel repository at compared revision](https://github.com/Raziel7893/WindowsGSM/tree/2f8e761cdb288edabb1fa58968154ebcc37047db)
- [Raziel's summary of fork changes](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/ChangesFromBase.md)
- [Crontab usage and security notes](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/Crontab.md)
- [Raziel project target](https://github.com/Raziel7893/WindowsGSM/blob/2f8e761cdb288edabb1fa58968154ebcc37047db/WindowsGSM/WindowsGSM.csproj)
- Local comparison point: `dev` at `f34b4b1c410394123e63ce71153a512b29d0fa03`; published `origin/dev` at `e778044e7a4a5e20aa0d3d9d6f424c1f3dabbe44`.
- Raziel comparison point: `master` at `2f8e761cdb288edabb1fa58968154ebcc37047db`.

### Detailed subreports

- [Discord improvements](RAZIEL-DISCORD-IMPROVEMENTS.md)
- [Query handling and plugin samples](RAZIEL-QUERY-PLUGIN-REPORT.md)
- [UI, process management, and reliability](RAZIEL-UI-PROCESS-REPORT.md)
