# Changes from the WindowsGSM base

This document summarizes the product and code changes in CdogsWindowsGSM compared with the original [WindowsGSM project](https://github.com/WindowsGSM/WindowsGSM).

- **Upstream comparison point:** `WindowsGSM/WindowsGSM` `upstream/master` at `3459bb6087b9ccf00d5179fe834aee4e5b3fad56` (26 December 2025).
- **CdogsWindowsGSM snapshot:** `2.0.0-beta1` on .NET 10, published as the GitHub prerelease tag `v2.0.0-beta1`.
- **Scope:** functional and user-visible differences in the current checkout. This is not a line-by-line patch; it excludes restored package caches, generated build output, and session files. The original application project was renamed and migrated, so file-level diff counts overstate the amount of behavioral change.

## Fork identity and release flow

- The application is branded and packaged as **CdogsWindowsGSM**, with its own README, release notes, version metadata, and attribution. The original WindowsGSM copyright is retained, and third-party notices are included.
- In-app update checks target the newest published release, including prereleases, from `conorjh/CdogsWindowsGSM`. The app downloads the updater for the selected release tag, and the updater selects the app executable from that release feed.
- The app and updater report version `2.0.0-beta1`; both executable assets are attached to the `v2.0.0-beta1` prerelease.

## WindowsGSM capabilities retained

The fork continues to use the original WindowsGSM workflows for installing and importing servers, starting/stopping/restarting them, managing server consoles and files, backups and restores, Discord alerts, plugins, CPU priority/affinity, automatic restarts, and game-server updates. The changes below extend or repair those workflows rather than replacing the application with a different management model.

## Game-server integrations

The current built-in registry contains 59 game-server entries; community plugins are separate. The fork adds or substantially extends these built-in integrations:

- **Call of Duty 1** and **Call of Duty 4 with CoD4x**.
- **OpenRCT2**, **OpenTTD**, **Mindustry**, and **OpenRA**.
- **SCUM** and **Soldat**.
- **Minecraft Paper** and **Forge**, alongside fixes to Minecraft Bedrock installation and related server handling.
- **Left 4 Dead**, integrated as a Source-based built-in while retaining the legacy `[L4D.cs]` server identity.
- **Luanti**, installed from the official [Luanti engine](https://github.com/luanti-org/luanti) release with a separately downloaded [Minetest Game](https://github.com/luanti-org/minetest_game) starter package. Per-instance configuration backups are supported; query, Steam-list, and shared-install support remain disabled.
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
- **Shared game installations:** adds opt-in shared binaries for 11 Source titles, 7 GoldSrc titles, and Call of Duty 1, with per-instance server data and update-impact reporting. This is code-level selection, not live qualification; only TFC has a tester-reported pilot, with its update check still open. Rust, Minecraft Bedrock, and plugins remain gated, and private-to-shared migration is not implemented.
- **Configuration-only backups:** adds opt-in, host-owned `.wgsmbackup` archives for explicit scopes in Rust, Luanti, 11 Source titles, 7 GoldSrc titles, and Call of Duty 1. The host validates the manifest and target, previews affected paths, records file hashes, snapshots existing configuration when available, and merges only approved paths. Existing full `.zip` backup/restore remains unchanged; full-server remains the default and scheduled type.
- **Server groups:** users can create groups, assign servers, filter the list, and run sequential group actions with progress and per-server results. Group metadata is separate from individual server configuration.
- **Windows Defender Firewall:** the Firewall tab maintains saved-config port rules per server in the persistent policy store for all profiles, updates them after config edits, and removes them when the server is deleted. It does not import detected runtime ports; Windows represents mixed TCP/UDP needs as protocol-specific rule objects under the server's one managed entry. See [the firewall guide](docs/WINDOWS-FIREWALL.md).
- **Dashboard:** adds a fleet summary, system CPU/memory/disk metrics, available hardware-temperature readings, active/peak player totals, and a player chart. The chart omits zero-player game types, avoids an unbounded legend, and shows the hovered game's count; accessible help retains a text summary.
- **Process recovery:** improves reattachment to already-running server processes and restores their console controls. Runtime port observations are tied to the current process and are cleared when it stops or is replaced.
- **Server-list usability:** game names are available from their icons, the main port shows a bold match/mismatch indicator and detected value, and IP, map, player, and uptime columns size to their content.
- **Startup and capability accessibility:** the branded splash reports the current startup task and server being processed. Built-in V2 capability states use check/cross marks with localized automation names, help text, and tooltips.

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
- Firewall port rules use Windows NetSecurity PowerShell with the persistent local policy store, and are created even when Windows Defender Firewall is currently disabled. Runtime detection is deliberately excluded from rule synchronization.
- The repository now includes updated build configuration, localization resources, and third-party notices for the changed dependencies and integrations.

## Behavior and support boundaries

- A2S, UT3, and FiveM query support is not a universal health protocol. A game with no supported query method, or a query endpoint with no successful baseline, remains `Unknown`; process health is reported separately.
- Query-port behavior is engine-specific. Stock Source and GoldSrc use the game/server UDP port for A2S; WindowsGSM cannot make an engine bind an arbitrary separate query port merely by changing its setting.
- Scheduled backups run only while the app and machine are available, and do not stop a running game server to create a backup.
- Shared-install selection and config-only scope declarations are not live qualification. The TFC shared-install update/recovery check, other enabled providers, plugin workflows, and clean-machine behavior remain open; private-to-shared migration is not implemented.
- The beta1 .NET 10 build and single-file publish need runtime qualification on supported systems. Accessibility and hardware-sensor behavior also require the manual checks in the Alpha tester and accessibility checklists.

## Related documentation

- [Game integration Alpha matrix](docs/GAME-INTEGRATION-ALPHA-MATRIX.md)
- [Roadmap to Alpha](docs/MULTI-INSTANCE-ALPHA-ROADMAP.md)
- [Alpha tester checklist](docs/ALPHA-TESTER-CHECKLIST.md)
- [Config-only backup guide](docs/CONFIG-ONLY-BACKUP-GUIDE.md)
- [Shared-installation guide](docs/SHARED-INSTALLATION-GUIDE.md)
- [Scheduled backup guide](docs/SCHEDULED-BACKUPS-GUIDE.md)
- [Accessibility test checklist](docs/ACCESSIBILITY-TEST-CHECKLIST.md)
- [Plugin V1-to-V2 guide](CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-V1-TO-V2-GUIDE.md)
