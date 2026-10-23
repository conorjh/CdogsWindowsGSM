# Shared game installs, config-only backups, Plugin API v2, and Alpha roadmap

**Status:** Proposed; the shared-install and config-only-backup features below are not implemented yet.

## Goal and terminology

Deliver these capabilities in dependency order:

1. **Shared game install, multiple server instances:** several servers use the same game binaries from one install folder, while each instance keeps its own configuration and runtime state.
2. **Configuration-only backups:** preserve and restore only the files needed to configure an instance, without copying or replacing shared binaries.
3. **Plugin API v2:** give plugins an explicit way to describe shared-install support and per-instance configuration/backup scope, while retaining a documented v1 compatibility path.
4. **Complete the .NET Framework exit:** prove the host, plugin compiler, dependencies, and supported plugin workflow no longer require .NET Framework.
5. **Alpha readiness:** qualify migration, sharing, backup/restore, plugin, and standalone-publish behavior with a controlled tester group.

Here, “single exe” means a game-server binary installation shared by multiple WindowsGSM instances. It is separate from the WindowsGSM desktop app's existing single-file publish.

## Current baseline and constraints

- The app, updater, and plugin-development project currently target .NET 10. The core retargeting is already done; “fully off .NET Framework” is a dependency, compiler, plugin-compatibility, and clean-machine verification gate, not another project retarget.
- Server configuration and files are currently organized under per-server `servers\<id>` directories. The existing full backup archives that per-server directory; the existing restore deletes and recreates it. That restore path must never be used for a scoped/config-only archive.
- Plugins are currently loaded using the v1 conventions and compiled source path. There is no required v2 contract marker yet. The existing [Plugin API v2 plan](PLUGIN-API-V2-PLAN.md) already proposes a v1 adapter, explicit v2 versioning, typed optional capabilities, plugin-defined backup plans, safe targeted restore, and a .NET-Framework-free compiler baseline.
- Full-server backup and its stopped-server safety behavior must remain the default until each game and plugin has a verified config scope. A missing or invalid scope must not silently produce a smaller “successful” backup.

## Recommended architecture

Separate an installed game from a running/configured server instance:

| Concept | Owns | Must not own |
|---|---|---|
| **Shared installation** | Game identity, binary location, installed version, shared immutable assets, update state, and an install/update lock. | Per-instance ports, server name, config files, logs, saves, or runtime process state. |
| **Server instance** | Stable server ID, reference to a shared installation, unique config root, command-line settings, ports, logs, saves, process/query state, and backup policy. | A private duplicate of binaries when the selected installation is shared. |
| **Backup scope** | A validated set of files and named roots for one instance, plus a versioned manifest and result. | Arbitrary paths, shared executable trees, or ownership of archive/restore mechanics by a plugin. |

Keep existing per-server installs working. Sharing is opt-in and only available for a game whose integration declares and passes the shared-install checks. Many dedicated servers write into their binary directory or update themselves; do not infer shareability from the fact that the launcher is an EXE.

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

Detailed implementation sequence and API recommendation: [Shared game installation sub-plan](SHARED-INSTALLATION-PLAN.md).

Implement the smallest end-to-end pilot:

1. Add a shared-install record and stable reference from each server instance. In shared mode, resolve a compatible ready installation first; install binaries only when none exists, then run config-only setup for the new instance. Keep instance settings and the existing unique server ID in host-owned configuration. See the [shared installation sub-plan](SHARED-INSTALLATION-PLAN.md).
2. Pass the instance's config/data root to the game using its documented mechanism. Preserve distinct ports, logs, saves, process tracking, and query state for every server.
3. Add an explicit per-game/plugin capability for safe sharing. Default to private installs when a game cannot prove that it supports concurrent instances or separate configuration.
4. Serialize install/update operations on a shared install. Report which instances use that installation and what an update will affect. Do not update binaries while an instance is using them unless the game-specific implementation proves that safe.
5. Migrate existing servers by copy-and-verify first. Retain the original files until the new instance starts and its config, ports, logs, saves, and server identity are verified. Provide rollback; never delete the original as part of a failed migration.

**Acceptance checks:** two instances start simultaneously from one shared binary directory; changing one instance's config does not change the other; logs, saves, ports, and server identities remain isolated; updates are serialized and their impact is visible; a failed migration leaves both the original instance and its data usable.

### Phase 2 — Host-owned configuration-only backup and restore

Add an explicit backup type, such as **Full server** and **Configuration**, without changing the existing full-backup format or retention behavior.

- Begin with host-known, verified config paths for the pilot built-ins. Do not guess by extension or directory name. Until an integration provides a validated scope, offer only the existing full-server backup.
- Store a versioned archive manifest with backup type, game/instance identity, creation time, named roots, and relative paths. A config-only archive must exclude shared binaries and other instances' files.
- Let the host create the archive, publish it atomically, apply retention only after success, and report the exact included paths. Keep the existing stopped-server safety rule initially; live config capture is a separate capability requiring consistency evidence.
- Restore to a staging directory first. Validate the manifest and every destination; reject absolute paths, traversal, root escapes, duplicate destinations, unsafe links/reparse points, and mismatched targets before writing anything.
- Apply a config-only restore by merging only the validated config paths into the selected instance. Never delete or replace the shared installation, the complete instance directory, or unrelated saves/logs. Keep legacy full ZIPs on the existing full-restore route.
- Confirm the target instance and affected files, and offer a pre-restore config snapshot where it can be created safely.

**Acceptance checks:** a config-only archive is measurably smaller for a binary-heavy pilot and contains no executable tree; restoring it changes only its declared paths; a full backup still restores through the legacy path; invalid/truncated archives make no changes; scheduled and manual runs use the same scope and result reporting; existing archives remain readable.

### Phase 3 — Plugin API v2 and compatibility adapter

Extend the existing [Plugin API v2 plan](PLUGIN-API-V2-PLAN.md), prioritizing shared installs and scoped backups:

- Declare a numeric plugin API version; absence of a v2 declaration remains v1. A malformed or unsupported v2 declaration must fail visibly, not silently downgrade.
- Add optional typed capabilities for shared-install eligibility and instance/config-path discovery. The plugin must identify its writable state and documented config selection mechanism; host validation remains authoritative.
- Add or retain a typed backup-plan capability that returns declarative named roots and paths. The host validates and archives them; plugins do not write arbitrary archive files or choose unrestricted restore destinations.
- Provide a per-instance context containing stable instance identity and approved config/data roots, rather than requiring plugins to reconstruct paths from the process working directory.
- Keep a v1 adapter for current plugins and preserve their existing installation/start/stop/update behavior. V1 plugins without a backup capability continue to use full-server backup; v1 plugins are never assumed to support shared binaries.
- Update the plugin-development template, loader diagnostics, compilation cache fingerprint, and authoring/migration guide. Add separate tests for v1 source compatibility, v2 version negotiation, capability validation, and cache invalidation.
- Keep other v2 capabilities (including the already-proposed soft-restart provider) separately gated; they must not delay the shared-install/config-backup pilot or change existing restart behavior by default.

**Acceptance checks:** representative v1 plugins continue to load; an unsupported plugin cannot opt into shared installs accidentally; valid v2 providers expose only approved paths; invalid v2 declarations and unsafe backup plans fail with actionable diagnostics; plugin/API changes invalidate stale compiled cache entries.

### Phase 4 — Verify the .NET Framework-free boundary

The solution's primary projects already target .NET 10. Close the remaining boundary by auditing the *active* graph and plugin workflow rather than deleting package-cache files indiscriminately:

1. Inventory every solution/project target, build script, restore graph, publish output, compiler reference, and bundled runtime/tool.
2. Confirm no active `.NETFramework` target, reference-assembly dependency, CodeDom provider, Framework compiler fallback, `mscorlib` plugin reference, or Framework-only runtime asset is required by the host or v2 plugin toolchain. Distinguish stale/unreferenced local package-cache files from actual dependencies.
3. Compile plugins only against the supported .NET 10 host/reference set. Preserve v1 source compatibility where supported; clearly diagnose v1 plugins that require Framework-only APIs and document their migration. Never restore a Framework fallback to make those plugins appear to load.
4. Validate the self-contained single-file app and updater from a clean Windows environment without relying on a separately installed .NET Framework runtime or developer-machine files.

**Exit gate:** clean restore/build/publish succeeds; a dependency and target-framework audit is attached to the release evidence; app and updater launch on the supported Windows baseline; representative v1 and v2 plugin tests pass or have explicit compatibility diagnostics; no active Framework-only path remains.

### Phase 5 — Alpha qualification and limited release

Alpha is appropriate only after the shared-install and config-backup migration is safe enough for testers; a successful compile or a single-file publish is not sufficient.

- Freeze the alpha scope and publish the supported Windows version, games, plugin API versions, backup types, and known limitations.
- Test clean installs and upgrades from the current per-server layout, including interruption/rollback, repeated migration, two instances sharing binaries, one instance being removed, and one shared install being updated.
- Run config-only and full backup/restore tests on representative built-ins and plugins. Include corrupt archives, permission errors, external roots, long paths, interrupted writes, and recovery from a failed restore.
- Test plugin v1/v2 discovery, compilation, cache invalidation, install/update/start/stop, and unsupported capability reporting.
- Test a clean-machine single-file install/update, both app and updater, with no Framework runtime installed. Record Windows versions and any required elevation/firewall behavior.
- Complete manual keyboard/screen-reader, high-contrast/scaling, and accessibility checks. Ensure logs expose migration/backup errors and do not report false success.
- Provide a pre-upgrade backup and recovery instructions, a known-issues list, versioned data/archive formats, and a feedback path. Release to a limited tester group first; do not describe Alpha as stable or promise backward restore support that has not been tested.

**Alpha exit gate:** no known data-loss or cross-instance configuration bug; migration is reversible; full and config-only restores are scoped and verified; the single-file app works on a clean supported machine; plugin compatibility is documented; all blocked tests and limitations are published with the Alpha.

## Dependency order and related plans

| Order | Deliverable | Depends on | Existing plan |
|---|---|---|---|
| 0 | Game/path capability inventory and migration contract | None | [Server-management features](SERVER-MANAGEMENT-FEATURES-PLAN.md) |
| 1 | Shared game installation with isolated server instances | Phase 0 | [Shared installation sub-plan](SHARED-INSTALLATION-PLAN.md) |
| 2 | Host-owned config-only archive and non-destructive restore | Verified instance/config roots | [Plugin API v2](PLUGIN-API-V2-PLAN.md), [server-management features](SERVER-MANAGEMENT-FEATURES-PLAN.md) |
| 3 | Plugin v2 shared-install and backup capabilities, plus v1 adapter | Phases 1–2 design | [Plugin API v2](PLUGIN-API-V2-PLAN.md), [compilation cache](PLUGIN-COMPILATION-CACHING-PLAN.md) |
| 4 | .NET Framework-free audit and compatibility evidence | Phases 1–3 | [.NET 10 migration](DOTNET-10-MIGRATION-PLAN.md) |
| 5 | Limited Alpha release | All exit gates above | [Accessibility checklist](ACCESSIBILITY-TEST-CHECKLIST.md) |

## First implementation slice

Start with **one game and two instances**. Prove that the engine accepts separate config roots while sharing immutable binaries, record every file it writes, and test an update while both instances exist. Then add the host-owned config-only archive/restore for that one game. Use the evidence to settle the instance model and Plugin API v2 capability shape before migrating the rest of the built-ins or changing backup defaults.