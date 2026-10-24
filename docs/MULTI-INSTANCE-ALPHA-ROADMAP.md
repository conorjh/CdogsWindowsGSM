# Roadmap to Alpha: Shared installs, config-only backups, and Plugin API v2

**Status:** In progress. Host-side shared-install plumbing, the Rust config-only backup slice, and Bedrock shared-install/config-only support are in code. Shared-install selection is enabled for the 10 Source titles, 7 GoldSrc titles, Call of Duty 1, and Minecraft Bedrock; this allowlist expansion is not live qualification. The tester has reported the TFC checks complete except update, but that report has not been independently reproduced. Rust remains gated after a live startup failure exposed an unresolved bundle-root/per-instance-data conflict. Bedrock is enabled but unqualified, and all plugins remain private-only until their exact providers have runtime evidence. No provider has passed all Alpha gates.

## Goal and terminology

Deliver these capabilities in dependency order:

1. **Shared game install, multiple server instances:** several servers use the same game binaries from one install folder, while each instance keeps its own configuration and runtime state.
2. **Config-only backups:** preserve and restore only the declared per-instance configuration files, without copying or replacing shared binaries.
3. **Plugin API v2:** infer optional shared-install and config-only backup capabilities from plugin methods, while retaining a documented v1 compatibility path.
4. **Complete the .NET Framework exit:** prove the host, plugin compiler, dependencies, and supported plugin workflow no longer require .NET Framework.
5. **Alpha readiness:** qualify migration, sharing, backup/restore, plugin, and standalone-publish behavior with a controlled tester group.

Here, “single exe” means a game-server binary installation shared by multiple WindowsGSM instances. It is separate from the WindowsGSM desktop app's existing single-file publish.

## Current baseline and constraints

- The app, updater, and plugin-development project currently target .NET 10. The core retargeting is already done; “fully off .NET Framework” is a dependency, compiler, plugin-compatibility, and clean-machine verification gate, not another project retarget.
- Server configuration and files are currently organized under per-server `servers\<id>` directories. Full-server backups remain host-managed copies of that whole per-server directory; plugins do not participate in their creation or restore. The existing full restore deletes and recreates the directory, so it must never be used for a config-only archive.
- Plugins retain the v1 constructor and dynamic lifecycle conventions. The host infers optional API v2 capabilities from exact public method signatures; plugins do not need a version attribute or marker interface. Typed contexts/results are implemented, but method presence is not live qualification.
- Full-server backup and its stopped-server safety behavior must remain the default until each game and plugin has a verified config scope. A missing or invalid scope must not silently produce a smaller “successful” backup.

## Implementation snapshot

- **Phase 0:** source-level provider paths and current enablement are now recorded below. The reviewed support matrix, complete writable-file inventories, and live runtime evidence are still outstanding.
- **Phase 1:** the host-side shared-install store, per-server references, built-in provider paths, update-impact reporting, and install/update locking are present. New built-in selection is enabled for Source titles, CoD1, other GoldSrc titles, and Minecraft Bedrock. Rust, SDK2013, and plugins remain gated. Concurrent isolation, update, rollback, and migration acceptance are not qualified; private-to-shared migration is not implemented.
- **Phase 2:** Rust `serverfiles/server.cfg` uses a separate `.wgsmbackup` config-only archive, staged/hash-checked merge restore, rollback, and a pre-restore snapshot when the current config exists. Manual and scheduled flows use the host-owned scope; an inferred plugin file-list method is also wired through host validation, currently with only the per-instance `serverfiles` root approved. Scheduled backups remain full-server by default. The tester reports the TFC shared-install SH checks completed except update; that partial report has not been independently reproduced. No plugin has live qualification.
- **Phase 3:** method-based capability inference, typed config-only backup/shared-install contexts and results, host validation, host-owned archive/restore integration, C# 7.3 examples, the authoring/migration guide, and host-assembly cache invalidation coverage are implemented and have targeted tests. Live plugin qualification remains outstanding.
- **Phase 4:** the restored project graph is .NET 10-only and the plugin compiler uses Roslyn; the active dependency snapshot below is a source/restore audit, not clean-machine or publish qualification.
- **Phase 5:** Alpha remains blocked on the live, recovery, migration, accessibility, and clean-machine exit gates.

**Prior verification snapshot (24 October 2026, before the allowlist expansion):** The app Debug build succeeded and all 34 tests passed after narrowing shared-install eligibility to the TFC pilot. `git diff --check` passed. These are build/test checks only, not live server or clean-machine qualification. The earlier 23 October 2026 Release and single-file publish checks remain historical results.

### Source-level provider matrix (not runtime qualification)

The full built-in inventory, explicit config-only scopes, current selection allowlist, and per-family qualification steps are recorded in the [built-in game shared-install/config-only report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md). The report distinguishes code enablement from live runtime qualification.

| Integration | Instance paths/mechanism represented in source | New shared-install choice | Evidence still required |
|---|---|---|---|
| Rust | `servers\\<id>\\serverfiles\\server.cfg`; shared binaries previously launched with a per-server working directory. | Gated by the host after the live process failed to find `Bundles\\Bundles` in that instance directory. Private Rust and config-only backups remain available. | Design and verify a supported split between the shared asset root and per-instance writable data before re-enabling; then test two instances, saves/config/logs, update blocking, recovery, and backup/restore. |
| Minecraft Bedrock | Shared binaries are launched with an instance working directory; `server.properties`, `allowlist.json`, and `permissions.json` are the config-only scope. Selected pack/definition directories are prepared per instance. | Shared install and config-only backup are enabled, but not live-qualified. | Verify world/config isolation and which copied directories are mutable; test concurrent start, update serialization, failure recovery, and backup/restore. |
| Source-derived | `Game\\cfg\\server.cfg` and map-cycle file under the instance root; launch arguments select them while binaries/maps come from the shared root. | Selection is enabled for all ten Source titles with config-only plans; none has broad live qualification. | Verify config/map-cycle selection and all writable runtime paths for each exact game; prove concurrent run and update/rollback behavior. |
| GoldSrc-derived | Per-instance mod config and `steam_appid.txt`; per-server map-cycle filename is placed in the shared mod directory. | Selection is enabled for all seven GoldSrc titles with config-only plans. TFC is the only one with a tester-reported run, and its update is open. | Verify TFC update and recovery; for every other title, prove HLDS search behavior, shared-root map-cycle write/restore, complete runtime write set, concurrency, update, and rollback. |
| Call of Duty 1 | Per-instance `main\\server.cfg`/optional mod config; shared `fs_basepath` and per-instance `fs_homepath`. | Selection is enabled, but stock and MeatBots runtime behavior are not qualified. | Verify stock server and each mod's writes, saves/logs/config isolation, concurrent run, update, and rollback. |
| Plugins | V1 remains private-only. The host may infer API-v2 method capabilities, but no plugin has runtime qualification. | All plugin providers remain private-only; method presence and capability diagnostics do not enable shared installation. | Provider-specific live concurrency, writable-state, update, and recovery evidence must precede any explicit provider allowlisting. |

No private-to-shared migration/adoption flow exists. Keep existing private installs private; do not copy a whole `serverfiles` tree or describe migration as accepted until a reversible, failure-tested workflow is implemented.

## Recommended architecture

Separate an installed game from a running/configured server instance:

| Concept | Owns | Must not own |
|---|---|---|
| **Shared installation** | Game identity, binary location, installed version, shared immutable assets, update state, and an install/update lock. | Per-instance ports, server name, config files, logs, saves, or runtime process state. |
| **Server instance** | Stable server ID, reference to a shared installation, unique config root, command-line settings, ports, logs, saves, process/query state, and backup policy. | A private duplicate of binaries when the selected installation is shared. |
| **Full-server backup** | A host-managed copy of the complete per-server directory. | Plugin participation or copying the separate shared binary installation. |
| **Config-only backup** | A host-managed archive/restore of validated files selected for one instance. | Arbitrary paths, shared executable trees, or ownership of archive/restore mechanics by a plugin. |

Keep existing per-server installs working. Sharing is opt-in and only available for a game enabled by the host and proven to pass the shared-install checks. Many dedicated servers write into their binary directory or update themselves; do not infer shareability from the fact that the launcher is an EXE.

Prefer a versioned shared installation directory (for example, game/version/install ID) that instances reference. This prevents an update from leaving some instances on a partially replaced binary set. If mutable in-place updates are required, serialize them and make it explicit that all instances referencing the installation are affected. Do not copy a running server's config, logs, saves, or generated files into the shared install.

## Delivery phases

### Phase 0 — Inventory and contract decisions

Before moving files, record for each pilot game:

- executable, binary/install root, and whether startup or runtime mutates files there;
- config files and how the server selects them (command-line option, environment, working directory, or fixed path);
- per-instance logs, saves, generated data, ports, and external data roots;
- install, update, start, stop, backup, and restore behavior;
- whether two instances can run concurrently from the same install, and any shared locks or writable state.

Choose two or three pilot integrations with demonstrably separate config paths. Include at least one built-in integration and one plugin only after its author can test it. Define a versioned server-instance/shared-install model, config migration rules, and the policy for updates to an installation in use. Do not begin with a broad move of every existing `serverfiles` directory.

**Exit gate:** a reviewed capability matrix names supported pilot games, unsupported cases, each game's exact instance config path, and the update/rollback behavior. The data model has explicit schema versions and a non-destructive migration design.

### Phase 1 — Shared installation and isolated instances

For the current built-in capability inventory, see the [shared-install/config-only report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md); for user behavior and Alpha limits, see the [shared-installation guide](SHARED-INSTALLATION-GUIDE.md).

Implement the smallest end-to-end pilot:

1. Add a shared-install record and stable reference from each server instance. In shared mode, resolve a compatible ready installation first; install binaries only when none exists, then run config-only setup for the new instance. Keep instance settings and the existing unique server ID in host-owned configuration. See the [shared-install/config-only report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) for current provider paths.
2. Pass the instance's config/data root to the game using its documented mechanism. Preserve distinct ports, logs, saves, process tracking, and query state for every server.
3. Add an explicit per-game/plugin capability for safe sharing. Default to private installs when a game cannot prove that it supports concurrent instances or separate configuration.
4. Serialize install/update operations on a shared install. Report which instances use that installation and what an update will affect. Do not update binaries while an instance is using them unless the game-specific implementation proves that safe.
5. **Migration is not implemented.** Before offering it, design a stopped-server, copy-and-verify flow that retains the original until the new instance starts and its config, ports, logs, saves, and identity are verified. Provide rollback; never delete the original as part of a failed migration.

**Acceptance checks:** two instances start simultaneously from one shared binary directory; changing one instance's config does not change the other; logs, saves, ports, and server identities remain isolated; updates are serialized and their impact is visible; a failed migration leaves both the original instance and its data usable.

### Phase 2 — Host-owned config-only backup and restore

Add an explicit backup type, **Full server** or **Config-only**, without changing the existing full-server format or retention behavior.

Host-known built-in config-only backup scopes cover Rust's `serverfiles/server.cfg`, supported Source titles' per-instance `<game>/cfg/server.cfg` and `<game>/mapcycle.txt`, GoldSrc's mod `server.cfg` and mapcycle, and CoD1's `main/server.cfg` (plus the MeatBots mod's editable `server.cfg` when enabled). Config-only backups are opt-in and use `.wgsmbackup`; the host reads and restores the declared files. For shared GoldSrc, the mapcycle is a server-specific file under the shared installation; the plan maps only that exact path through a `sharedserverfiles` root, leaving binaries outside the archive. The plugin method only supplies the file list; it does not create or extract archives. Full-server backups remain host-managed copies of the entire per-server directory and never call plugin backup methods. `SkeletonV2.cs` demonstrates the generic file-list method; Arma 3 and Enshrouded remain researched candidates whose source examples have not yet been added. Existing `.zip` full backups and their restore route remain unchanged; SDK2013 and v1 plugins remain full-server-backup-only.

- Keep the built-in path list explicit; do not guess by extension or directory name. CoD1's generated MeatBots settings are omitted because the host regenerates them from server settings. A plugin's exact `GetConfigOnlyBackupPlan(PluginInstanceContext)` method is inferred and its file list is validated by the host; until a game/provider has a verified scope, offer only the existing full-server backup.
- Store a versioned archive manifest with backup type, game/instance identity, creation time, named roots, and relative paths. A config-only archive must exclude shared binaries and other instances' files.
- Let the host read the listed files, create the archive, publish it atomically, apply retention only after success, and report the exact included paths. The current Rust and inferred plugin flows use host validation; plugins currently receive only the approved per-instance `serverfiles` root. Keep the existing stopped-server safety rule initially; live config capture is a separate capability requiring consistency evidence.
- Restore to a staging directory first. Validate the manifest and every destination; reject absolute paths, traversal, root escapes, duplicate destinations, unsafe links/reparse points, and mismatched targets before writing anything.
- Apply a config-only restore by merging only the validated config paths into the selected instance. Never delete or replace the shared installation, the complete instance directory, or unrelated saves/logs. Keep legacy full ZIPs on the existing full-restore route.
- Confirm the target instance and affected files, and offer a pre-restore config snapshot where it can be created safely.

**Acceptance checks:** a config-only archive is measurably smaller for a binary-heavy pilot and contains no executable tree; restoring it changes only its declared paths; a full-server backup still copies/restores the whole per-server directory through the legacy host path; invalid/truncated config-only archives make no changes; scheduled and manual runs use the same scope and result reporting; existing archives remain readable.

### Phase 3 — Plugin API v2 and compatibility adapter

Extend the existing [Plugin API v2 plan](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-API-V2-PLAN.md), prioritizing shared installs and config-only backups:

- Implemented method-based capability inference: plugins without optional typed methods remain v1; no attribute or marker interface is required. The host rejects partial/malformed typed method sets visibly. Targeted tests cover the v1 skeleton and source compatibility at C# 7.3.
- A public `GetConfigOnlyBackupPlan(PluginInstanceContext)` method infers config-only backup support. Shared-install support requires that method, both typed shared-install methods, and the required identity members. These methods describe capabilities but do not establish live runtime qualification.
- Implemented declarative config-only file lists and `PluginInstanceContext`. The host owns archive/restore; only the per-instance `serverfiles` root is approved for plugins today. Full-server backups remain outside the plugin interface.
- Preserve the v1 constructor and dynamic operation behavior. Typed shared-install contexts/results are implemented; live provider qualification remains open. V1 plugins without the inferred config-only backup method continue to use full-server backup and are never assumed to support shared binaries.
- The compiler cache fingerprints Roslyn, the .NET runtime, the host assembly, and active metadata references. Loader diagnostics report inferred capabilities and cache/load errors. The v1 skeleton remains unchanged; `SkeletonV2.cs` demonstrates the method-inferred config-only backup shape. Arma 3 and Enshrouded remain researched plugin-example candidates; source examples have not yet been added or runtime-qualified. A regression test changes the host-reference hash and verifies the unchanged plugin source receives a cache miss.
- The initial plugin-facing V2 contract for config-only backups and shared installation is frozen. The [from-scratch no-base-class tutorial](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-FROM-SCRATCH-TUTORIAL.md) and [V1 migration guide](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-V1-TO-V2-GUIDE.md) live with the plugin-development project. Defer soft restart and other optional capabilities so they do not delay this pilot or change existing restart behavior by default.

**Acceptance checks:** representative v1 plugins continue to load unchanged; plugins without the complete shared-install capability set (config-only backup plan, exact typed shared-install methods, and required identity members) remain private-only; inferred config-only file lists expose only approved paths; partial method sets and unsafe file lists fail with actionable diagnostics; plugin/API changes demonstrably invalidate stale compiled cache entries.

### Phase 4 — Verify the .NET Framework-free boundary

The solution's primary projects already target .NET 10. Close the remaining boundary by auditing the *active* graph and plugin workflow rather than deleting package-cache files indiscriminately. A local restore/source audit on 23 October 2026 found:

- The app, updater, plugin-development tool, and test project target `net10.0-windows*`; no active project reference to `Microsoft.NETFramework.ReferenceAssemblies` or `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` was found.
- The app uses `Microsoft.CodeAnalysis.CSharp` 5.9.0 and C# 7.3 plugin parsing. The app's active graph contains `System.Management` 10.0.13; the plugin-development and test graphs also resolve its transitive `System.CodeDom` 10.0.13 asset from `lib/net10.0`. No `Microsoft.CodeDom.Providers.DotNetCompilerPlatform`, `CSharpCodeProvider`, or `CodeDomProvider` use is active.
- Plugin references are built from the runtime trusted-platform-assembly list plus the app assembly. The cache fingerprint includes the content hashes of those references, including the host contract assembly; a regression test changes the host-reference hash and verifies that unchanged plugin source misses the prior cache entry.
- No Framework reference-assembly package appears in the restored solution package graph. Unreferenced files under a local package cache are not active dependencies and must not be deleted as audit cleanup.

This audit does not prove published-app behavior or clean-machine compatibility. Finish the remaining gate by:

1. Inventory every solution/project target, build script, restore graph, publish output, compiler reference, and bundled runtime/tool.
2. Confirm no active `.NETFramework` target, reference-assembly dependency, CodeDom provider, Framework compiler fallback, `mscorlib` plugin reference, or Framework-only runtime asset is required by the host or v2 plugin toolchain. Distinguish stale/unreferenced local package-cache files from actual dependencies.
3. Compile plugins only against the supported .NET 10 host/reference set. Preserve v1 source compatibility where supported; clearly diagnose v1 plugins that require Framework-only APIs and document their migration. Never restore a Framework fallback to make those plugins appear to load.
4. Validate the self-contained single-file app and updater from a clean Windows environment without relying on a separately installed .NET Framework runtime or developer-machine files.

**Exit gate:** clean restore/build/publish succeeds; a dependency and target-framework audit is attached to the release evidence; app and updater launch on the supported Windows baseline; representative v1 and v2 plugin tests pass or have explicit compatibility diagnostics; no active Framework-only path remains.

### Phase 5 — Alpha qualification and limited release

Alpha is appropriate only after shared-install and config-only backup behavior is safe enough for testers; a successful compile or a single-file publish is not sufficient.

- Freeze the alpha scope and publish the supported Windows version, games, plugin API versions, backup types, and known limitations.
- Test clean installs and upgrades from the current per-server layout, including interruption/rollback, repeated migration, two instances sharing binaries, one instance being removed, and one shared install being updated.
- Run config-only and full backup/restore tests on representative built-ins and plugins. Include corrupt archives, permission errors, external roots, long paths, interrupted writes, and recovery from a failed restore.
- Test plugin v1/v2 discovery, compilation, cache invalidation, install/update/start/stop, and unsupported capability reporting.
- Test a clean-machine single-file install/update, both app and updater, with no Framework runtime installed. Record Windows versions and any required elevation/firewall behavior.
- Complete manual keyboard/screen-reader, high-contrast/scaling, and accessibility checks. Ensure logs expose migration/backup errors and do not report false success.
- Complete and review the documentation package in the [Alpha documentation deliverables](#alpha-documentation-deliverables) section against the exact candidate build. Keep current behavior, gated capabilities, known limitations, and test evidence distinct.
- Provide a pre-upgrade backup and recovery instructions, a known-issues list, versioned data/archive formats, and a feedback path. Release to a limited tester group first; do not describe Alpha as stable or promise backward restore support that has not been tested.

**Alpha exit gate:** no known data-loss or cross-instance configuration bug; migration is reversible; full-server and config-only backup/restore behavior is verified; config-only restore changes only its validated destinations; the single-file app works on a clean supported machine; plugin compatibility is documented; every example plugin has companion documentation; the README and `ChangesFromBase.md` match the candidate's supported-feature matrix; and all blocked tests and limitations are published with the Alpha.

## Alpha documentation deliverables

Drafts for the documentation package are present in the current workspace. Before a limited Alpha release, review every guide against the exact candidate build and refresh its evidence/status. Documentation must distinguish implemented or selection-enabled behavior from live qualification; implementation plans, method detection, and successful compilation are not proof of runtime qualification.

| Deliverable | Target | Acceptance criteria |
|---|---|---|
| V1-to-V2 plugin migration guide | Existing `CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-V1-TO-V2-GUIDE.md`; companion Garry's Mod sample note added. | Final candidate review must confirm the V1 baseline, optional V2 signatures, capability rules, and migration examples match the current host contract; distinguish compile evidence from live qualification. |
| From-scratch plugin authoring guide | Existing `CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-FROM-SCRATCH-TUTORIAL.md`; companion CS 1.6 sample note added. | Final candidate review must confirm the no-engine-base workflow, V1 lifecycle, optional V2 features, backup/shared-install explanations, and game examples match the current API and tooling. |
| Documentation for every example plugin | Five companion guides are present under `CdogsWindowsGSM-Plugin-Development/docs/examples/` for `Skeleton.cs`, `SkeletonV2.cs`, `Skeleton-SteamCMDAgent.cs`, `GarrysModV2.cs`, and `CS16StandaloneV2.cs`. | Explain each sample's purpose, source/build/load location, implemented lifecycle and capabilities, setup, limitations, and verification level. Keep the guides synchronized with their source examples. |
| Up-to-date README | `README.md` updated with Alpha links, feature guides, plugin guides, and example notes. | Review the game/capability matrix, Alpha status, backup behavior, plugin guide links, and known limitations against the release candidate; do not present gated or unqualified features as supported. |
| Up-to-date fork changes document | `ChangesFromBase.md` updated for current shared-install, backup, schedule, and qualification boundaries. | Recheck the fork-specific feature list and support boundaries against the release candidate, including plugin tooling. |
| Shared-installation feature guide | `docs/SHARED-INSTALLATION-GUIDE.md` added; `docs/BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md` is the provider inventory. | Explain eligibility, creating/selecting and using a shared install, per-instance data isolation, update impact, backup interaction, recovery limits, and the current pilot/gates. State clearly that private-to-shared migration is not implemented and TFC update qualification is outstanding. |
| Config-only backup feature guide | `docs/CONFIG-ONLY-BACKUP-GUIDE.md` added. | Explain how config-only differs from full-server backup, supported scopes, `.wgsmbackup` contents, host validation and targeted restore, pre-restore snapshots, secret exposure, and current provider limitations. |
| Scheduled backup feature guide | `docs/SCHEDULED-BACKUPS-GUIDE.md` added. | Explain schedule setup, next-run/last-result status, run-now and retention behavior, stopped-server safety, the requirement that WindowsGSM and the machine be available, and the full-server default versus explicitly supported config-only scopes. |

**Documentation exit check:** all listed files are present and local file links in the touched docs resolve. Before release, refresh the candidate-specific support status, review feature behavior against that build, and ensure example guides do not imply live game qualification from compilation alone.

## Dependency order and related plans

| Order | Deliverable | Depends on | Existing plan |
|---|---|---|---|
| 0 | Game/path capability inventory and migration contract | None | [Built-in game audit](BUILTIN-GAME-SERVER-AUDIT.md), [shared-install/config-only report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) |
| 1 | Shared game installation with isolated server instances | Phase 0 | [Shared-installation guide](SHARED-INSTALLATION-GUIDE.md), [built-in report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) |
| 2 | Host-owned config-only archive and non-destructive restore | Verified instance/config roots | [Config-only backup guide](CONFIG-ONLY-BACKUP-GUIDE.md), [Plugin API v2](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-API-V2-PLAN.md) |
| 3 | Plugin v2 shared-install and backup capabilities, plus v1 adapter | Phases 1–2 design | [Plugin API v2](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-API-V2-PLAN.md), [compilation cache](../CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-COMPILATION-CACHING-PLAN.md) |
| 4 | .NET Framework-free audit and compatibility evidence | Phases 1–3 | [Fork changes and runtime summary](../ChangesFromBase.md) |
| 5 | Alpha documentation package | Verified candidate behavior and Phase 0–4 status | [Alpha documentation deliverables](#alpha-documentation-deliverables) |
| 6 | Limited Alpha release | All exit gates above, including the documentation package | [Accessibility checklist](ACCESSIBILITY-TEST-CHECKLIST.md) |

## First implementation slice

Continue the existing TFC pilot by completing its outstanding shared-update check on a disposable installation; the tester reports the other SH checks complete, but the report has not been independently reproduced. Source, CoD1, and other GoldSrc titles are selectable for shared installs but must not be described as live-qualified until their exact runtime behavior is proven. Plugins remain private-only. Rust config-only archive/restore remains implemented, but Rust shared binaries are gated until a supported asset-root/per-instance-data design is verified.
