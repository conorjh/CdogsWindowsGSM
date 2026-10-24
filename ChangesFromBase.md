# Changes from the WindowsGSM base

This document summarizes the product and code changes in CdogsWindowsGSM compared with the original [WindowsGSM project](https://github.com/WindowsGSM/WindowsGSM).

- **Upstream comparison point:** `WindowsGSM/WindowsGSM` `upstream/master` at `3459bb6087b9ccf00d5179fe834aee4e5b3fad56` (26 December 2025).
- **CdogsWindowsGSM snapshot:** local `dev`, based on commit `3b12956`, with the working-tree changes present for preview `2.0.0-preview.8` (24 October 2026).
- **Scope:** functional and user-visible differences in the current checkout. This is not a line-by-line patch; it excludes restored package caches, generated build output, and session files. The original application project was renamed and migrated, so file-level diff counts overstate the amount of behavioral change.

## Fork identity and release flow

- The application is branded and packaged as **CdogsWindowsGSM**, with its own README, release notes, version metadata, and attribution. The original WindowsGSM copyright is retained, and third-party notices are included.
- In-app update checks target releases from `conorjh/CdogsWindowsGSM`, rather than directing users to upstream WindowsGSM releases. The repository includes a Windows build workflow and a companion updater executable.
- The current app/updater version is `2.0.0-preview.8` on the .NET 10 target. This identifies the local preview build; it does not by itself mean a GitHub Release has been published.

## WindowsGSM capabilities retained

The fork continues to use the original WindowsGSM workflows for installing and importing servers, starting/stopping/restarting them, managing server consoles and files, backups and restores, Discord alerts, plugins, CPU priority/affinity, automatic restarts, and game-server updates. The changes below extend or repair those workflows rather than replacing the application with a different management model.

## Game-server integrations

The current built-in registry contains 57 game-server entries; community plugins are separate. The fork adds or substantially extends these built-in integrations:

- **Call of Duty 1** and **Call of Duty 4 with CoD4x**.
- **OpenRCT2**, **OpenTTD**, **Mindustry**, and **OpenRA**.
- **SCUM** and **Soldat**.
- **Minecraft Paper** and **Forge**, alongside fixes to Minecraft Bedrock installation and related server handling.
- **RedM/CFX** support and fixes shared with the existing FiveM integration.

Related game-specific changes include:

- FiveM and RedM use current CFX server artifacts and query support; a valid CFX license key is required.
- OpenRCT2 can download a verified sample park when none is present and can start without custom server parameters. A licensed RollerCoaster Tycoon 2 installation is still required.
- CoD4x installs the upstream CoD4x 21.2 server binary. The CoD4 game installation must be licensed; proprietary game data is not redistributed, and CoD4x is under AGPL-3.0.
- GoldSrc servers receive the configured local IP through `+ip`, useful on hosts with multiple network interfaces.
- Source and GoldSrc map-rotation interfaces support adding and removing maps.
- Insurgency: Sandstorm installation validates the expected server binary before treating installation as successful.
- A2S query handling includes GoldSrc response parsing and Source protocol fixes.

## Server management and monitoring

- **Bulk actions:** update all stopped servers sequentially, in addition to the existing start, stop, and restart actions. Per-server and group operations report individual results rather than masking partial failures.
- **Runtime port verification:** discovers a process-owned client/main port when it can be identified unambiguously and displays whether it matches the configured value. It is not universal query-port discovery. Source and GoldSrc A2S health checks use the effective game/server UDP port; other supported query methods use their configured query endpoints.
- **Server health:** adds separate process and supported-query checks, a compact status in the server list, and explanatory details. Unsupported checks and endpoints that have never returned a valid response remain `Unknown`; only a previously verified endpoint that later fails can produce a query warning. A failed query does not itself restart a server.
- **Scheduled backups:** adds per-server backup schedules, next-run and last-result information, retention handling, and a run-now path. Backups follow the stopped-server safety rule; the schedule runner requires WindowsGSM to be running.
- **Shared game installations:** adds opt-in shared binaries for 10 Source titles, 7 GoldSrc titles, and Call of Duty 1, with per-instance server data and update-impact reporting. This is code-level selection, not live qualification; only TFC has a tester-reported pilot, with its update check still open. Rust, Minecraft Bedrock, and plugins remain gated, and private-to-shared migration is not implemented.
- **Configuration-only backups:** adds opt-in, host-owned `.wgsmbackup` archives for explicit scopes in Rust, 10 Source titles, 7 GoldSrc titles, and Call of Duty 1. The host validates the manifest and target, previews affected paths, records file hashes, snapshots existing configuration when available, and merges only approved paths. Existing full `.zip` backup/restore remains unchanged; full-server remains the default and scheduled type.
- **Server groups:** users can create groups, assign servers, filter the list, and run sequential group actions with progress and per-server results. Group metadata is separate from individual server configuration.
- **Dashboard:** adds a fleet summary, system CPU/memory/disk metrics, available hardware-temperature readings, active/peak player totals, and a player chart. The chart carries player-summary help text for assistive technology without duplicating its game labels in a visible list.
- **Process recovery:** improves reattachment to already-running server processes and restores their console controls. Runtime port observations are tied to the current process and are cleared when it stops or is replaced.
- **Server-list usability:** game names are available from their icons, the main port shows a bold match/mismatch indicator and detected value, and IP, map, player, and uptime columns size to their content.

## Localization and accessibility

- The interface provides a language selector for Windows language, English, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Romanian, Russian, Ukrainian, Simplified Chinese, Japanese, Korean, and Turkish. The selection is saved for the current Windows user and applied after restarting the app; missing translations fall back to English.
- Keyboard focus, tab access, UI Automation names/help text, label associations, and live status announcements have been added across server management, settings, schedules, groups, dialogs, and the dashboard. Health and port indicators remain visual icons/colors while their localized meaning and details are exposed to assistive technology.
- UI layout and contrast were adjusted in affected areas, including the server details/log panel and dashboard. Manual Narrator/NVDA, high-contrast, scaling, and full-workflow acceptance checks remain necessary.

## Runtime, dependencies, and plugin tooling

- The legacy .NET Framework application and plugin-development projects were converted to SDK-style projects targeting .NET 10. The desktop app retains WPF and WinForms and targets Windows; the configured publish target is a self-contained, single-file `win-x64` executable.
- The updater was migrated to a self-contained single-file .NET 10 executable. The app uses the fork's release source and latest-release handoff.
- Plugin source compilation moved from the legacy CodeDom/bundled-compiler setup to the Roslyn API. A content- and compiler-input-aware assembly cache avoids recompiling unchanged plugins while keeping source plugins and their normal diagnostics as the source of truth.
- The plugin-development tool was updated for the SDK-style project and output layout.
- The chart library was moved from the legacy LiveCharts WPF package to LiveChartsCore WPF.
- Discord.Net was upgraded while retaining the existing bot workflows and enabling the intents required by them.
- Windows Firewall integration was changed from the legacy COM path to profile-aware NetSecurity PowerShell rules. Firewall behavior still requires validation on supported Windows profiles before claiming full parity.
- The repository now includes updated build configuration, localization resources, and third-party notices for the changed dependencies and integrations.

## Behavior and support boundaries

- A2S, UT3, and FiveM query support is not a universal health protocol. A game with no supported query method, or a query endpoint with no successful baseline, remains `Unknown`; process health is reported separately.
- Query-port behavior is engine-specific. Stock Source and GoldSrc use the game/server UDP port for A2S; WindowsGSM cannot make an engine bind an arbitrary separate query port merely by changing its setting.
- Scheduled backups run only while the app and machine are available, and do not stop a running game server to create a backup.
- Shared-install selection and config-only scope declarations are not live qualification. The TFC shared-install update/recovery check, other enabled providers, plugin workflows, and clean-machine behavior remain open; private-to-shared migration is not implemented.
- The .NET 10 preview and its single-file publish need runtime qualification on supported systems. Accessibility and hardware-sensor behavior also require the manual checks in the Alpha tester and accessibility checklists.

## Related documentation

- [Built-in game-server audit](docs/BUILTIN-GAME-SERVER-AUDIT.md)
- [Roadmap to Alpha](docs/MULTI-INSTANCE-ALPHA-ROADMAP.md)
- [Alpha tester checklist](docs/ALPHA-TESTER-CHECKLIST.md)
- [Built-in shared-install and config-only report](docs/BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md)
- [Shared-installation guide](docs/SHARED-INSTALLATION-GUIDE.md)
- [Config-only backup guide](docs/CONFIG-ONLY-BACKUP-GUIDE.md)
- [Scheduled backup guide](docs/SCHEDULED-BACKUPS-GUIDE.md)
- [Accessibility test checklist](docs/ACCESSIBILITY-TEST-CHECKLIST.md)
- [Plugin compilation cache](CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-COMPILATION-CACHING-PLAN.md)
