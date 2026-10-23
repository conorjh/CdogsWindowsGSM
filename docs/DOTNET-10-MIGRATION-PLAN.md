# .NET 10 and runtime modernization plan

Status: .NET 10 migration is merged into `dev`; stable .NET 10.0.402 solution Release build and app/updater publishes pass. Runtime and behavior qualification remain open.
Plan investigated: 21 October 2026. Implementation status updated: 12 November 2026.

## Recommendation

Treat this as a modernization program with a central .NET 10 migration and independently testable workstreams for Discord, plugin compilation/Roslyn, and Windows Firewall management. Keep the current .NET Framework release available until the new release meets the acceptance gates below.

Target the desktop app at `net10.0-windows10.0.19041`, retaining WPF and WinForms support. This Windows API target selects the supported WPF assets for the replacement chart library. The initial publish target is `win-x64`; preserve a self-contained, single-file application EXE for each architecture that is explicitly supported. Keep GitHub Releases as the update source.

The firewall behavior appears practical to reproduce without COM interop. The initial implementation uses the NetSecurity PowerShell cmdlets; compare it against the current authorized-application behavior on supported Windows profiles before considering that workstream complete.

These workstreams belong in the same repository and release plan. They do not automatically require separate `.csproj` files; add project boundaries only where a reusable plugin contract or service boundary justifies them.

## Product requirements and boundaries

- Preserve existing Discord bot, command, presence, dashboard, authorization and webhook behavior. Do not combine the dependency upgrade with a command-system or webhook rewrite.
- Keep the normal application distribution to a single self-contained EXE. A single-file publish is specific to a runtime identifier (RID), so the supported architecture list must be decided explicitly.
- Migrate the legacy CodeDom provider and bundled Roslyn compiler as part of the move to .NET 10; do not carry the old compiler toolset forward. Preserve plugin source behavior and its current C# language compatibility where practical.
- Continue sourcing application updates from GitHub Releases. The update transport, metadata lookup and replacement helper can change.
- Preserve the current Windows Firewall user-visible intent while evaluating a non-COM implementation. Profile scope and removal behavior must be tested rather than assumed equivalent.
- Preserve existing server files, settings, plugins and logs during installation and updates.
- Keep code changes as small and behavior-preserving as practical. This is a runtime/dependency migration, not feature work: do not add or intentionally change product features, except for updater changes needed to support the agreed GitHub and single-file update flow. Necessary compatibility fixes and test/documentation changes remain in scope; defer unrelated refactoring.
- Keep the current .NET Framework release available while the .NET 10 version is qualified.
- Do not promise that existing compiled plugin binaries will load unchanged until that behavior has been tested. Record an explicit policy for source-plugin and binary-plugin compatibility.

## Implementation checkpoint (21 October 2026)

This is an implementation attempt, not a release candidate.

| Workstream | Implemented on the branch | Verification and remaining work |
| --- | --- | --- |
| App and solution | Replaced the Framework app and plugin-tool projects with SDK-style .NET 10 projects under their original filenames; the active solution contains those projects and the migrated updater. A normal Build now publishes the app and updater as self-contained single-file executables under `artifacts\app` and `artifacts\updater`. The repository selects a stable .NET 10 SDK through `global.json`. | Stable .NET 10.0.402 restore, Release solution build, and app/updater single-file publishes pass. User-reported app startup and plugin smoke checks pass; the stable .NET 10 CI run is still pending. |
| Single-file and Roslyn | The app is self-contained, `win-x64`, and published as one EXE. Managed bundle contents are extracted to the runtime's private temp directory so Roslyn can obtain plugin metadata references. The legacy bootstrap/compiler was replaced with `Microsoft.CodeAnalysis.CSharp`; source plugins remain restricted to C# 7.3. | The publish folder contains only `CdogsWindowsGSM.exe`. A single-file smoke host compiled and loaded the existing skeleton plugin; user reports plugins load and pass basic smoke checks. Broader external-plugin binary compatibility remains unverified. |
| Chart compatibility | Replaced the framework-only LiveCharts v0 package with LiveChartsCore WPF, retaining the player-count series, axis limits/title and right-side legend. | The project builds without the prior `NU1701` fallback warning. Verify the chart's appearance and the Windows API target on supported machines. |
| Discord | Upgraded Discord.Net and explicitly enabled the existing guild/direct-message and message-content intents used by the prefix-command flow. | Build only; login, commands, authorization, presence, dashboard and webhook parity still need a non-production bot/guild. |
| Firewall | Replaced the new app's COM firewall path with profile-aware NetSecurity PowerShell rules and awaited cleanup during server deletion. | No firewall rules were changed on this development machine. Add/check/remove behavior, profile parity, legacy-rule cleanup and user-visible failure handling still need an isolated elevated Windows test. |
| Updates | Retained the GitHub latest-release updater handoff and migrated the updater to a self-contained single-file `win-x64` executable. | The app and updater publish successfully as one EXE each. The workflow uploads Actions artifacts; it does not attach them to a GitHub Release. The release process must still publish both expected assets before end-to-end update testing. |
| Plugin development tool | Updated its repository and app-output path resolution for the SDK-style build layout. | Included in the successful solution build; interactive install/run from Visual Studio remains untested. |
| Version | App and updater metadata use `2.0.0-preview.7` with numeric assembly/file version `2.0.0.0`; the app displays the prerelease tag. | Publish a GitHub release tagged `v2.0.0-preview.7` as a prerelease only after qualification; updater behavior against prerelease assets remains unverified. |

The local solution build reports compiler/analyzer warnings; there are no build errors. `global.json` requires stable .NET 10 SDK feature-band roll-forward, matching the workflow's .NET 10 SDK configuration.

## Baseline evidence (before implementation)

Paths are relative to the repository root. Line numbers are omitted because these files are expected to change during the migration.

| Area | Current implementation | Migration consequence |
| --- | --- | --- |
| Main desktop app | `CdogsWindowsGSM\CdogsWindowsGSM.csproj` is a non-SDK WPF application targeting .NET Framework 4.7.2. The app also uses WinForms APIs, including dialogs, `NotifyIcon` events and `SendKeys`. | Use a Windows-specific target with both WPF and WinForms enabled. Preserve XAML, resources, application identity, manifest and settings when converting the project file. |
| Project and package format | The main app uses manually listed source/resources, `packages.config`, direct DLL `HintPath` references and embedded `ReferencesEx` assemblies. | Convert to SDK-style project/package management, removing duplicate default items and stale framework-only references deliberately. Validate every game-server source file and resource in the build. |
| Discord | The main app references Discord.Net 2.2.0. Bot behavior is implemented in `CdogsWindowsGSM\DiscordBot`; webhook delivery is implemented separately in `CdogsWindowsGSM\Functions\DiscordWebhook.cs`. | Keep the bot and webhook behavior matrix separate from framework retargeting. Coordinate dependency work with `docs\DISCORD-DEPENDENCY-UPGRADE-PLAN.md`, which currently treats the .NET 10 migration as a prerequisite. |
| Plugin compilation | `CdogsWindowsGSM\Functions\PluginManagement.cs` compiles installed C# source through `Microsoft.CodeDom.Providers.DotNetCompilerPlatform`. `Program.cs` extracts a bundled Roslyn ZIP and uses `AssemblyResolve` to load embedded assemblies. | Migrate both the CodeDom provider and bundled Roslyn compiler to a .NET 10-compatible Roslyn API. Preserve plugin source behavior/language compatibility where practical and test it with the published single-file app. |
| Plugin API | Plugin source is compiled against the entry assembly and uses WindowsGSM types. The plugin-development project references the main app. | Decide source and binary plugin compatibility explicitly. A dedicated public plugin API/reference assembly may simplify future plugins, but its required surface must be audited before extraction. |
| Firewall | `CdogsWindowsGSM\WindowsFirewall.cs` uses the `NetFwTypeLib` COM reference and `HNetCfg.FwMgr`. It adds an executable to `CurrentProfile.AuthorizedApplications`; callers check/add rules before starting processes, and server removal calls `RemoveRuleEx`. | Prototype a non-COM program rule and preserve intended profile, add/check/remove and cleanup behavior. `RemoveRuleEx` currently matches paths by substring, so define exact ownership and deletion semantics rather than copying that matching strategy blindly. |
| Elevation | `CdogsWindowsGSM\app.manifest` requests `requireAdministrator`. | Preserve the current elevation/UAC behavior unless product requirements intentionally change. Firewall operation failures still need to be reported clearly. |
| Update flow | `MainWindow.xaml.cs` downloads `CdogsWindowsGSM-Updater.exe` from a GitHub latest-release URL. The updater downloads and replaces `CdogsWindowsGSM.exe`. | Keep GitHub Releases as the source. Design a replacement handoff appropriate for a single-file app; Windows will not permit the running executable to overwrite itself. The helper may be retained, replaced, or made temporary. |
| Updater project and solution | `CdogsWindowsGSM.Updater\CdogsWindowsGSM.Updater.csproj` is SDK-style but targets .NET Framework 4.7.2 and is not listed in `CdogsWindowsGSM.sln`. | Decide whether the existing updater remains, is replaced, or is retired. Include whichever update components are selected in CI and release qualification. |
| CI and tests | `.github\workflows\build.yml` runs MSBuild on `windows-2019` and builds the solution. There is no separate test project in the solution. | Move CI to a supported Windows runner with the .NET 10 SDK. Add targeted automated and Windows integration coverage before claiming behavior parity. |

## Proposed workstreams

### 1. Behavior baseline and test seams

Before changing the runtime, record the existing behavior for:

- Discord login/readiness, prefix commands, admin and per-server authorization, activity/presence updates, dashboard messages, shutdown and webhook notifications.
- Plugin installation, compile diagnostics, loading, metadata/images and representative in-repository or community plugins.
- Firewall rule existence checks, adding rules before server start and cleanup when a server is removed.
- Application startup/elevation, user settings, server data, crash logs and the current GitHub update path.

There is no separate test project in the solution today. Add focused automated tests for logic that can be isolated and Windows-only smoke/integration checks for OS behavior. A small adapter around Discord, plugin compilation and firewall operations can make these workstreams testable without turning the migration into a broad architecture rewrite.

### 2. Firewall replacement without COM

The current code adds executable-specific exceptions to the active profile. This is a suitable scope for a non-COM rule-based replacement; it does not currently configure arbitrary ports or protocols.

Prototype one of:

1. **`netsh advfirewall firewall`**: built into Windows and invokable as a child process. Manage deterministic WindowsGSM-owned rule names, pass executable paths as arguments rather than composing shell commands, and check process exit status/output.
2. **NetSecurity PowerShell cmdlets**: `New-NetFirewallRule`, `Get-NetFirewallRule` and `Remove-NetFirewallRule` provide a structured rule interface without COM interop in the C# application. Validate the availability of the cmdlets on the supported Windows versions.

There is no first-party managed .NET firewall API. Direct Windows Filtering Platform interop is a lower-level alternative, but appears disproportionate for the current per-program exceptions.

Before choosing an implementation, verify:

- Whether to preserve the current-profile scope or intentionally apply rules to Domain, Private and Public profiles. Do not silently broaden the current behavior.
- Whether these are inbound allow rules for the exact executable path, and how a path change/update affects an existing rule.
- Idempotent add/check/remove behavior for SteamCMD, Java-based servers, game-server executables and server deletion.
- Exact ownership and cleanup: only remove rules created by WindowsGSM; do not use broad path substring matching that could remove unrelated rules.
- Elevated and denied-access behavior, with actionable error reporting.

The app already requests administrator elevation in its manifest. Test the replacement on supported Windows versions and network profiles before removing `NetFwTypeLib` from the project.

### 3. Discord dependency and behavior migration

Use `docs\DISCORD-DEPENDENCY-UPGRADE-PLAN.md` as the detailed package/API plan. Keep its .NET 10 prerequisite unless package compatibility is deliberately verified and that plan is revised. The migration work should preserve current message-based commands and authorization; it should not introduce slash commands or change webhook transport.

Capture the current behavior before the package change, then validate it against a test bot and non-production server. In particular, confirm Gateway intents and Developer Portal settings for the existing message-content workflow, reconnect/readiness behavior, activity updates, shutdown and the existing HTTP webhook sender. Avoid changing token or admin configuration as part of this upgrade.

### 4. Plugin and Roslyn modernization

Replace `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` and the bundled legacy Roslyn ZIP/compiler with a current Roslyn API supported by .NET 10, such as `Microsoft.CodeAnalysis.CSharp`. Do not retain the old compiler toolset as a compatibility fallback. Preserve the source-plugin workflow if that remains a product requirement; otherwise define a compiled-plugin contract and provide a migration path.

Keep the current plugin language level (C# 7.3) during the initial migration if it helps preserve existing source compatibility; changing compiler implementation does not require accepting newer plugin syntax. This workstream must also solve the single-file constraint explicitly. The current compiler references `Assembly.GetEntryAssembly().Location`, which cannot be treated as a reliable path to a bundled executable. Define the plugin's public API and provide compiler metadata references through a deliberate contract/reference model. Choose and test plugin dependency resolution and assembly loading rather than assuming the old `AppDomain.AssemblyResolve` behavior is sufficient.

Acceptance testing must compile and load the in-repository skeleton plugin from a published single-file build. Also test representative external plugins and document any required source changes or binary compatibility break. Do not bundle a compiler migration and an unrelated plugin API redesign in the same unreviewed change.

### 5. .NET 10 project and dependency migration

Move the active WPF app to the SDK-style `net10.0-windows10.0.19041` target, with WPF and WinForms enabled. Preserve `App.xaml`, `MainWindow.xaml`, resources, icon, manifest, and settings; resolve duplicate source/resource inclusion and generated `AssemblyInfo` attributes as part of the conversion.

Move `packages.config` and direct DLL references to `PackageReference`. Audit package target support and upgrade where needed, especially the UI dependencies, Discord.Net, archive libraries and compiler packages. Remove framework facade packages and binding redirects only after verifying they are no longer required. Review the old `App.config` `system.web`, CodeDom and startup entries and migrate any actual application settings deliberately.

Migrate the plugin-development utility to the new target and update its output/path assumptions. Add it to the solution build if it remains a supported tool. For the updater project, make a separate keep/replace/retire decision based on the selected update handoff; do not migrate an updater executable that is no longer part of the design merely to make every existing project look modern.

### 6. Single-file publishing and GitHub updates

Publish a self-contained single-file executable for each supported RID. The initial implementation targets `win-x64`. The app uses managed-content extraction to a private temp directory for Roslyn metadata references, so verify temporary-directory access, startup behavior and antivirus handling on a clean Windows machine. The downloaded app itself remains one EXE with no required sidecar files.

Keep GitHub Releases as the authoritative update source. The initial implementation retains the existing helper role: the app downloads `CdogsWindowsGSM-Updater.exe`, then the updater downloads and replaces `CdogsWindowsGSM.exe` from the latest release after the app exits. The current workflow uploads both executables as Actions artifacts but does not create or update a GitHub Release; release publication must supply both expected asset names.

Test the complete cycle: discover an update, download the correct asset, validate it, replace/restart the app, recover from download/replacement failure, and preserve server data/settings/plugins/logs. Do not require a permanent second EXE in the normal app distribution unless that is the selected update design.

## Phased delivery and acceptance gates

| Phase | Work | Exit evidence |
| --- | --- | --- |
| 0. Baseline | Record current .NET Framework build/output and the Discord, plugin, firewall, data and update behavior. Add focused test seams where useful. | Existing release can still be built/run; preservation checklist and compatibility policy are recorded. |
| 1. Early risk spikes | Prototype firewall management without COM; verify single-file WPF feasibility; prove modern Roslyn can compile and load a representative plugin; confirm package compatibility for the planned Discord upgrade. | Each high-risk item has a tested direction or a documented blocker and alternative. |
| 2. Independent workstreams | Implement firewall replacement and Discord behavior isolation; complete the Discord package upgrade after its prerequisite gate; settle plugin source/binary compatibility and compiler design. | Firewall add/check/remove and Discord behavior pass their focused acceptance checks; plugin policy is explicit. |
| 3. Framework/project conversion | Convert the main project and supported tools to SDK-style .NET 10; modernize dependency references/configuration; replace the legacy CodeDom/Roslyn toolchain; include all supported projects in CI. | Clean restore/build succeeds on Windows, all app resources/server modules are present, and the legacy compiler provider/toolset is no longer required. |
| 4. Single-file and update integration | Publish self-contained single-file app per selected RID; implement the chosen GitHub release/update handoff. | A clean Windows machine can install/run the EXE and complete update, restart and recovery tests without losing user data. |
| 5. Release candidate | Run the full behavior matrix against the published artifact; keep the Framework release available during qualification. | Acceptance matrix passes; release notes document architecture and plugin compatibility; rollback path is available. |

The firewall replacement can be prototyped before the target-framework conversion. Discord behavior capture can also start early, while the package upgrade follows the prerequisite in its dedicated plan. The Roslyn/plugin design must be decided before single-file publishing is considered complete.

## Verification matrix

These are release-qualification checks. Build, publish-shape and skeleton-plugin smoke evidence is summarized above; the remaining behavior and Windows integration checks are still pending.

1. **Build and publish:** clean restore/build of all supported projects on a current Windows runner; publish produces the intended single-file EXE for each supported RID.
2. **Single-file runtime:** launch on a clean supported Windows machine without Visual Studio/NuGet caches; verify WPF startup, WinForms interop, resources, native dependencies, manifest and required elevation.
3. **Discord parity:** test bot login/readiness, commands, admin/server grants, message intents, presence, dashboard, clean shutdown/restart, and webhook alert/crash-log delivery.
4. **Plugin parity:** compile/load the skeleton and representative external source plugins from the published EXE; verify diagnostics, plugin API references, dependency resolution and documented compatibility expectations.
5. **Firewall parity:** on supported Windows profiles, verify exact executable rule add/check/remove, server-removal cleanup, profile scope, elevation and clear access-denied/error behavior. Confirm no COM reference is needed by the selected implementation.
6. **Server lifecycle:** test representative SteamCMD, Java and non-Steam server start/stop flows, including the firewall call paths; confirm process handling and user data remain unchanged.
7. **Update and recovery:** source releases from GitHub; test correct RID selection, staged download, replacement while the app is running, restart, interrupted/invalid download, replacement failure and recovery. Verify user data is never overwritten.
8. **Upgrade compatibility:** test existing Framework-era settings, plugin folders, server directories, backups and logs with the .NET 10 release.

## Decisions and remaining policy questions

- The initial RID is `win-x64`. Should additional Windows RIDs be added after separate publish/runtime validation?
- Must existing source plugins continue to compile unchanged, or is a versioned plugin migration acceptable? Is binary plugin compatibility required?
- Does firewall behavior apply only to the current profile or to all relevant profiles? Which exact rules does server removal own?
- The initial update handoff retains the existing helper. Verify that both assets are attached to each intended GitHub Release and test restart/recovery before release.
- Which features of the existing ClickOnce/publish settings are still used, if any?
- Is `WindowsGSM-Plugin-Development` still a supported deliverable, and should it be included in CI/release tooling?

Do not hide these decisions inside the project-file conversion. Record the chosen behavior and test it before releasing .NET 10.

## Deferred scope

- Rewriting the WPF UI or changing the game-server feature set.
- Replacing prefix commands with Discord interactions/slash commands.
- Changing Discord token/admin storage or webhook delivery semantics.
- Rewriting the plugin model beyond what is necessary for a supported compiler/runtime and the agreed compatibility policy.
- Replacing the Windows Firewall feature with a new user-facing port/rule editor.
- Changing elevation behavior or making the app cross-platform.

## Related plans and source files

- `docs\DISCORD-DEPENDENCY-UPGRADE-PLAN.md` - detailed Discord.Net dependency migration; currently assumes .NET 10 is complete first.
- `CdogsWindowsGSM\CdogsWindowsGSM.csproj` - current SDK-style main project and its `PackageReference` dependency graph.
- `CdogsWindowsGSM\Program.cs` and `CdogsWindowsGSM\Functions\PluginManagement.cs` - embedded assembly loading and runtime source compilation.
- `CdogsWindowsGSM\WindowsFirewall.cs` and `CdogsWindowsGSM\app.manifest` - NetSecurity firewall implementation and elevation requirement.
- `CdogsWindowsGSM\MainWindow.xaml.cs` and `CdogsWindowsGSM.Updater\Program.cs` - current GitHub update flow and EXE replacement.
- `CdogsWindowsGSM.sln`, `CdogsWindowsGSM.Updater\CdogsWindowsGSM.Updater.csproj` and `.github\workflows\build.yml` - solution and CI coverage.