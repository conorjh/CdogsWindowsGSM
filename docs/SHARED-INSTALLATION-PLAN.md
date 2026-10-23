# Shared game installation implementation sub-plan

**Status:** Shared-install providers are wired for built-in Rust, Source-derived, GoldSrc-derived, and COD1 integrations, and the app project builds successfully. A read-only Shared Installations navigation view is included. Source/GoldSrc/COD1 runtime behavior, multi-instance concurrency, update-lock, backup/restore, and failure recovery still require qualification. Plugins remain private-only.

**Parent plan:** [Shared instances and Alpha roadmap](MULTI-INSTANCE-ALPHA-ROADMAP.md)

**Related plans:** [Plugin API v2](PLUGIN-API-V2-PLAN.md), [.NET 10 migration](DOTNET-10-MIGRATION-PLAN.md)

## Goal and product boundary

Allow multiple WindowsGSM server instances to use one compatible game-binary installation while retaining independent server identities, configuration, ports, logs, saves, and runtime state. Sharing is opt-in per installation and per game integration; private installs remain the default.

Keep the Server List opaque to this implementation. It continues to show one ordinary row per server instance, with the existing actions and columns. Do not add a shared-install badge, shared path, or grouped row there. The Install Game Server flyout is the opt-in point: show a **Use shared installation** checkbox only when the selected built-in game has the host shared-install capability. A separate read-only **Shared Installations** navigation page provides a simple way to inspect registered installs and their linked servers. Important operations that affect multiple instances, such as updating or removing shared binaries, must still explain their impact when the user performs them.

This feature concerns sharing game-server files, not the WindowsGSM application's single-file executable. Do not assume that a game is shareable just because SteamCMD or another installer can place its files in a common directory. A game is eligible only when its concurrent-run behavior and instance-specific writable/configuration paths have been proven.

## Current implementation constraints

| Surface | Current behavior | Consequence for shared installs |
|---|---|---|
| Server configuration | `ServerConfig` persists a separate `WindowsGSM.cfg` per server under `servers\<id>\configs`; an optional `sharedinstallationid` now identifies a host-managed install. | Missing metadata continues to mean a private legacy install. IDs are validated and resolve through the host registry, never to a path stored in the server config. |
| Game-file root | `ServerPath.GetServersServerFiles(serverId)` still resolves to `servers\<id>\serverfiles`; shared binaries for supported games resolve explicitly to `installations\<guid>\serverfiles`. | Keep the legacy resolver stable for private installs. Shared paths are derived from the installation ID and do not redirect other games. |
| Install UI/flow | `Button_Install_Click` shows **Use shared installation** for built-in providers. The private per-server directory is prepared separately; shared binaries are installed once or reused, then the provider's awaited config-only operation prepares that server's instance files. | Never run private-install cleanup against a shared root. The server-to-install link is saved only after binary validation and per-instance config preparation succeed. |
| Built-in integrations | Rust, Source, GoldSrc, and COD1 resolve shared binary roots for install/update/validation/build operations and retain server-specific configuration paths. Shared roots use the versioned host registry and read/exclusive file leases. | Engine paths are implemented but Source/GoldSrc launch selectors and all new-engine concurrency behavior still require runtime qualification. The existing Source add-on manager is disabled for shared servers because it only targets private paths. |
| Plugin v1 | Plugins are loaded and invoked dynamically using the existing parameterless `Install()` convention. | Keep that signature and dispatch unchanged. A v1 plugin cannot opt into sharing by accident. |
| Runtime/compiler | The active app and plugin-development projects target .NET 10. Plugin API v2 still needs an explicit capability contract and compiler/reference qualification. | Build the host implementation on .NET 10 now; expose it to plugins only through the planned, typed v2 capability. Do not add a .NET Framework compiler fallback. |

### Shared Installations navigation view

- The page is separate from the Server List and does not expose binary paths or provide mutation/cleanup controls.
- It reads the host registry and displays installation ID, game, Steam AppID, state, build, creation time, and names/IDs of linked server instances.
- A **Refresh** action reloads the registry snapshot and linked server configs. Registry errors are shown to the user; the page never edits registry state.

### Rust prototype snapshot

- A version-1 registry in `configs\SharedInstallations.json` records the Rust game/AppID, capability version, build ID when available, and `Installing`/`Ready`/`Failed` state. Installation roots are derived from validated GUIDs beneath `installations\`; arbitrary paths are not persisted.
- The install flyout exposes sharing for built-in Rust, Source-derived, GoldSrc-derived, and COD1 providers. The first instance installs binaries into the registered shared root; later instances reuse the single ready root for that game/AppID. Each server retains its instance configuration and working directory under its own `servers\<id>` directory.
- A new root remains exclusively leased until the install result is recorded. If WindowsGSM stops before recording completion, a later attempt treats the unlocked `Installing` record as failed and creates a new root; it retains the abandoned root rather than deleting potentially active files.
- Rust start holds a shared read lease for the process lifetime. WindowsGSM reacquires the lease when it reattaches to a running Rust process after restart. An update needs an exclusive lease, so it fails with an actionable error while any linked Rust instance is running. A start also fails while an update owns the root.
- Deleting a server continues to delete only its instance directory. Shared binaries are retained even when no server currently references them. Server backups contain the instance config/runtime files, not the shared binaries; restore therefore requires the referenced registry entry and binaries to still exist. Automatic cleanup, backup metadata, and reinstallation-on-restore are not implemented.
- Build success is not runtime qualification. Verify two simultaneous Rust instances, distinct saves/configs/logs, update blocking, attach after restart, deletion of one linked server, and failure/retry behavior before treating the pilot as production-qualified.

## Recommended API and path design

### Do not change the legacy game-level `Install()` contract

Do **not** add a required overload to the existing game/plugin `Install()` method. A second dynamically discovered method can create ambiguity, and changing the v1 contract would put existing source plugins at risk. The parameterless method also does not express whether the host is creating binaries, attaching a new server to existing binaries, or preparing instance-specific files.

Add a typed host-side install request and operation boundary for the eventual Plugin API v2 contract. The Rust prototype currently uses explicit Rust-only host methods (`InstallSharedBinaries`, awaited `InstallConfigOnly`) and explicit SteamCMD root methods; the context/provider API below remains future design work:

- `ServerInstallRequest` carries an explicit action: `CreatePrivate`, `CreateShared`, or `AttachShared`.
- It contains the stable server-instance ID/configuration and separately approved `InstallationRoot`, `InstanceRoot`, and, when supported, `ConfigRoot`/`DataRoot`. The host derives and canonicalizes these paths from IDs; a persisted plugin-supplied arbitrary path is not authoritative.
- A typed result reports success, cancellation, and actionable failure details. No failed or unsupported shared operation silently falls back to a new private install.
- Built-in integrations adopt an internal typed install handler for this request. Plugin support is an optional v2 capability (for example, `ISharedInstallationProvider`) discovered by the existing versioned plugin loader. Keep all names illustrative until the v2 contract phase freezes them.

The action has precise semantics:

| Action | Binary behavior | Instance behavior |
|---|---|---|
| `CreatePrivate` | Preserve today's per-server install target and behavior. | Initialize the ordinary private server instance. This remains the default and the v1 plugin path. |
| `CreateShared` | Create a new host-managed installation and install/validate binaries there once, only when no compatible ready shared installation is selected. | Run config-only setup for the first independent server instance and link it to the new installation only after successful setup. |
| `AttachShared` | Validate and reference an existing compatible, ready installation; do not reinstall or update it implicitly. | Run config-only setup for the new instance and verify it can run concurrently with existing references. |

Creating or attaching is a game-specific operation. If an integration cannot identify writable files and isolate them per instance, it must not advertise the capability. Do not emulate isolation with hard links, symlinks, directory junctions, or copies of unknown state as a shortcut.

### Shared-binary resolution and `InstallConfigOnly`

When the user checks **Use shared installation**, resolve a compatible, host-managed installation for that game before downloading anything. The Rust prototype currently supports one ready installation per built-in game/AppID and reports duplicate ready records instead of presenting a selector:

1. If exactly one compatible installation is ready, use its registered binary root.
2. If several compatible installations are ready, let the user select one in the Install Game Server flyout. The selector may also offer **Create new shared installation**.
3. If none exists, allocate a new shared root and install the binaries there. Mark it ready only after install and validation succeed.
4. If a matching install is still in progress, unavailable, or fails validation, report that state. Do not start a duplicate install or silently fall back to a private server directory.

Compatibility is determined from the built-in game/plugin identity and declared install capability, plus the install's validated state/build metadata. Do not infer a shareable install by scanning arbitrary private `serverfiles` folders. Existing private installs remain private unless a separately designed, explicit migration/adoption flow is used.

The shared-capable install contract needs two distinct operations:

- `InstallSharedBinaries(SharedInstallationContext)` runs once when no compatible shared root exists. It places and validates the game binaries in that root; it must not leave server-specific mutable configuration there.
- `InstallConfigOnly(ServerInstanceContext)` runs for **every** server instance using shared binaries, including the first instance that created them. It assumes the binary root is already ready and creates only that instance's non-conflicting configuration/data in its approved instance roots. It must not download, repair, replace, or update shared executables or shared files.

This is the intended `InstallConfigOnly()` operation; its typed context and async signature should be finalized with Plugin API v2. A game whose per-instance setup overwrites a common file in the binary root, or cannot direct the game to instance-specific configuration, is not eligible. Do not call the legacy parameterless `Install()` for an attach operation: it may reinstall or overwrite the shared files. Private installs continue through their existing `Install()` path.

### Host-owned installation identity and storage

- Keep shared installation metadata in a dedicated host-owned registry with an explicit schema version. Give each installation a stable opaque ID and a canonical root under a dedicated `installations\<id>` directory (for example, its game files under `installations\<id>\serverfiles`). Do not use a server ID as the shared installation ID.
- Record the game/plugin identity, capability/API version, installed build/branch where known, readiness/update state, and installation root. Publish transitions atomically so an incomplete install cannot be selected for attach.
- Store the optional installation mode and installation ID in each server's own `WindowsGSM.cfg`. Derive the shared path from the host registry; do not persist arbitrary paths in every server config. For legacy configs, absent fields continue to select the existing private root.
- Determine whether an installation is still referenced from server configs (or validate any cached reference count against them) before cleanup. A stale registry count must never authorize deleting binaries still used by a server.
- Keep server settings, secrets, ports, schedules, logs, saves, process/query state, and all game-specific instance files under their instance-owned locations. The supported game capability must declare how the game receives these paths and prove its working-directory behavior.

For the SteamCMD pilot, add a root-oriented install/update path in the low-level SteamCMD helper that accepts an explicit canonical install directory. Retain the existing server-ID methods as adapters for private legacy callers. Update build-manifest lookup and validation to read the selected installation root too. This low-level overload is acceptable because it is an explicit filesystem target; it does not replace or overload the dynamic game/plugin `Install()` contract.

### Capability discovery and Install Game Server UI

Represent support as an explicit game capability, not a name-based list of games or an assumption based only on the game engine. Built-ins can declare capability through a host-owned descriptor/registry. A plugin may advertise it only by implementing the optional v2 capability; an unmarked v1 plugin remains private-only.

Install-flyout behavior:

1. On game selection, query the capability and reset the checkbox and any shared-install selection. If unsupported, unknown, or unable to isolate instance state, collapse the checkbox and keep the current private path.
2. For a supported game, show an unchecked **Use shared installation** checkbox. When checked, resolve a compatible existing root first: reuse the only match, offer a selector if there are multiple, or create a new shared root if there are none. When multiple roots exist, include an explicit **Create new shared installation** choice; show useful game/build labels, not raw disk paths.
3. If a new root is required, install/validate its binaries once with `InstallSharedBinaries`. Then run `InstallConfigOnly` for the new server instance. If reusing an existing root, skip binary installation and run only `InstallConfigOnly`. Neither path may implicitly update an existing shared installation.
4. Persist the server-to-installation link only after binary validation (when newly created) and config-only instance preparation succeed. On failure or cancellation, do not leave a ready-looking dangling reference. Clean up only newly created, unreferenced artifacts; never delete a reused installation.
5. Localize the checkbox, selector, progress, and errors using the existing localization resources. Keyboard and UI Automation must expose the checkbox and any conditional selector correctly.

## Lifecycle, backup, and migration rules

Shared ownership affects more than installation. For every supported integration, route these operations through the selected installation/instance context:

- **Start and validation:** launch the shared executable while passing the right per-instance settings/config; use an instance-safe working directory. If the game requires all servers to share mutable working-directory files, it is not eligible.
- **Update/repair:** lock by shared installation, not by server ID. Report how many instances use it. Do not update while any referencing server is running unless the game's capability has explicitly proven that behavior safe. Update binaries once; do not issue duplicate updates for each linked instance.
- **Other binary operations:** local-build detection, addon installation, and any helper that reads or writes game files must use the same explicit root. Instance-only operations must not write into shared binaries.
- **Delete:** deleting a server removes its instance configuration/data. Keep the shared root while any server config references it; when the last linked server is deleted, remove the shared root and registry entry after acquiring an exclusive lease. Warn about the last-reference cleanup in the delete confirmation and report cleanup failures while retaining the registry entry in a non-ready state.
- **Backups:** preserve the existing private-server backup behavior. A shared instance's archive must not duplicate shared binaries or use the destructive full-directory restore against the shared root. Record the referenced installation ID/build in the archive metadata and require a compatible installation on restore or provide an explicit reinstallation path. Keep config-only backup scope on the host-owned, validated path described in the parent roadmap and Plugin API v2 plan.
- **Existing servers:** leave existing private installs in place and private by default. Bulk conversion or deduplication is not part of the MVP. Any later migration must copy and verify while stopped, retain the source until a successful start and config/data check, and support rollback without deleting original files.

## Candidate engine implementation sketches

The initial implementation scope is the user-nominated **Rust, Source, GoldSrc, and Call of Duty 1** built-in integrations. The shared provider is inherited by the built-in Source and GoldSrc subclasses; the code wiring is not proof of safe concurrent runtime behavior. Verify each representative game/mod and AppID below before treating its shared mode as qualified. In particular, the shared config selectors and map-cycle paths for Source and GoldSrc have not yet been verified against live servers.

| Candidate | One-time shared binary operation | `InstallConfigOnly` work | Runtime path proof required |
|---|---|---|---|
| **Rust (`RUST`)** | Implemented as a SteamCMD AppID `258550` install into the registered shared root; attach skips SteamCMD. | `InstallConfigOnly` is awaited and writes the instance's `server.cfg` with that instance's RCON secret/port. | Rust resolves `RustDedicated.exe` from the shared root and uses the per-server working directory. Verify concurrent processes and separate config/log/save/world data in the manual qualification run. |
| **Source (`Engine.Source`)** | Run the selected built-in game's SteamCMD AppID once into the shared root. | The awaited config-only operation writes `Game\cfg\server.cfg` and the map-cycle file beneath that server's instance root. The existing add-on manager is disabled in shared mode until it can target and lock shared binaries correctly. | `Source.Start` resolves `srcds.exe` and maps from the shared root and passes an absolute `-game` path plus per-instance `+servercfgfile` and `+mapcyclefile` paths. Verify those selectors and distinct runtime state against representative live Source servers; do not treat acceptance of the arguments as proof that the files were loaded. |
| **GoldSrc (`Engine.GoldSource`)** | Run SteamCMD AppID `90` with the selected mod (`Game`) once into the shared root. | The awaited config-only operation writes that instance's mod `server.cfg` and `steam_appid.txt` beneath its instance root, plus a uniquely named map-cycle file in the shared mod directory. | `GoldSource.Start` resolves `hlds.exe` and maps from the shared root, starts HLDS with that directory as its working directory, and keeps `-game` as the relative mod name (for example, `cstrike`) to use the same base-directory behavior as a private install. It passes the map-cycle filename relative to the mod directory because GoldSrc rejects absolute `mapcyclefile` paths. An absolute `-game` path or an instance-only working directory can break GoldSrc's search for fallback `valve` assets such as `gfx.wad`. Config and map-cycle selection remain per instance; deleting a server removes its map-cycle file unless that final reference deletes the shared root. |
| **Call of Duty 1 (`COD1`)** | Run SteamCMD AppID `2620` once into the shared root. If SteamCMD fails, the configured HTTPS fallback installs into that same shared root. MeatBots is installed there under an exclusive lease. | Create that instance's `main\server.cfg` and optional mod config in instance-owned locations; the MeatBots server config is copied only to that instance's mod home. | `COD1.Start` uses the shared `CoDMP.exe`, explicit `fs_basepath` and per-instance `fs_homepath`, and instance `server.cfg`. Verify stock COD1 first, then MeatBots and its concurrent instance behavior. |

For Source and GoldSrc, the candidate config selectors above are a test requirement, not an assumption that an absolute config path is accepted. Record the exact launch argument and observe the server loading each instance's distinctive config. If the server reads the shared default config instead, mark that concrete game unsupported until an isolated mechanism is proven.

The shared install-completion path awaits `InstallConfigOnly` for every supported provider before writing `WindowsGSM.cfg` or reporting success. Private Source and GoldSrc installs retain their legacy `CreateServerCFG()` behavior; plugin API v2 still needs a typed result before plugins can participate.

### Tester run for each candidate

1. Start with no registered shared installation for the concrete game. Create the first server with sharing enabled. Verify that WindowsGSM creates the shared binary root once, validates it, then runs `InstallConfigOnly` for that server.
2. Create a second server of the same game with sharing enabled. Verify that it resolves the same installation ID/root, does not launch SteamCMD or the game binary installer again, and runs only `InstallConfigOnly`.
3. Give the two instances visibly different names, ports, map/mapcycle, and secrets. Start both at once; verify each advertises the expected values and writes config/log/save state to its own instance locations.
4. Change one instance's config and restart only that server. Verify the other instance's config and behavior are unchanged.
5. Compare a recorded inventory/hash of representative immutable binaries before and after the second `InstallConfigOnly` call. Ignore documented per-instance/runtime files; no executable or shared binary may be replaced by config-only setup.
6. Delete one server and confirm the other still starts from the same root. Delete the final linked server and confirm its shared root and registry entry are removed. Test shared update/repair only after the instance isolation case passes.

Suggested implementation order is **Rust first**, then one concrete Source game, one concrete GoldSrc mod, and stock COD1 before its optional MeatBots case. This is an order for building evidence, not a permanent priority or a guarantee that every listed game will qualify.

## Delivery phases

| Phase | Work | Exit evidence |
|---|---|---|
| 0. Capability audit and pilot choice | Inventory binary roots, per-instance config/data paths, writable files, working directory, concurrent-run behavior, update/repair, add-ons, and backup/restore for the nominated Rust, Source, GoldSrc, and COD1 candidates. Select one built-in pilot only after its isolation is demonstrated. | A reviewed support matrix names exact roots and known limits; unsupported/unknown cases remain private-only. |
| 1. Data model and explicit roots | Add versioned shared-install metadata and optional per-server references. Add explicit root resolution while preserving private defaults and old config loading. | Old server configs load unchanged; paths for private/shared instances resolve deterministically; malformed IDs/metadata fail visibly and never fall back to another root. |
| 2. Typed host operation and pilot integration | Add the typed install request/result and internal capability handling. Adapt the Rust pilot and required SteamCMD install/update/manifest calls to explicit roots. Implement `InstallSharedBinaries` and `InstallConfigOnly` as separate operations before adding the other nominated game types. | A private install is behaviorally unchanged; the first shared instance installs binaries once and receives isolated config; attaching another instance calls only config-only setup and leaves shared binaries unchanged. |
| 3. Install-flyout resolution and attach flow | Add capability-driven checkbox, compatible-install resolution, and conditional selector when several roots exist; persist the link only after verified success. Keep the Server List unchanged. | A single ready installation is reused automatically; no match creates one; multiple matches require a clear choice; unsupported and v1 plugin games do not show the control. |
| 4. Runtime lifecycle and ownership | Update start, validation, update/repair, build detection, addons, deletion, locking, and shared-install impact messaging to use explicit roots. | Concurrent instances start with isolated config/data; updates are single, serialized operations; deleting one instance cannot affect another or its binaries. |
| 5. Plugin API v2 integration | Add the optional v2 shared-install capability exposing binary installation and `InstallConfigOnly` with typed contexts/results after the v2 marker, adapter, and compiler reference model are settled. Update the plugin template and cache fingerprint as required. Built-in games can implement the equivalent host capability directly. | Representative v1 plugins still load and remain private-only; a valid v2 provider can opt in; only explicitly supported built-in games or v2 plugins can share; invalid/unknown declarations fail visibly. |
| 6. Qualification and gradual support | Run the full matrix for Rust, representative Source/GoldSrc games, and COD1 (including MeatBots separately). Decide separately whether to add an explicit private-to-shared migration tool. | Two-instance, lifecycle, backup/restore, failure/rollback, UI accessibility, and clean .NET 10 publish gates pass before treating these engines as qualified. |
| 7. Shared Installations visibility | Implemented as a read-only navigation page over the host registry with a refresh action and linked-server details. | Users can inspect registered IDs, game/AppID, state/build, creation time, and linked instances without changing the Server List or registry. |

## Acceptance checks

- Existing private server installs, old `WindowsGSM.cfg` files, v1 plugin behavior, and normal Server List presentation remain unchanged.
- A supported install flyout displays the opt-in checkbox; an unsupported or unverified game and every v1-only plugin do not display it. Changing the selected game cannot retain a stale checked state or installation selection.
- With sharing selected, one compatible ready install is reused; no compatible install causes a single binary install; multiple matches require selection. A new shared root receives binaries once, then `InstallConfigOnly` provisions the first instance; later instances run `InstallConfigOnly` without changing the shared binary tree.
- The Shared Installations page refreshes to show each registry entry and its linked server instances, and reports registry read errors without changing registry data.
- The Rust, selected Source, selected GoldSrc, and COD1 test cases each demonstrate distinct per-instance configuration and runtime state; Source/GoldSrc config and map-cycle selection is verified from observed server behavior, not inferred from a successful install.
- Two instances use the same binary root and run concurrently with distinct ports, config, logs, saves, and server IDs. Changing one instance's configuration does not alter the other.
- Attaching a server reuses a ready compatible install without an implicit download/update. A failed attach does not change or delete the existing shared install.
- Start, update, validation, addon operations, build detection, backup, and restore all resolve the intended explicit root. A shared update is serialized and its impact on every linked instance is made clear.
- Removing one of several linked servers keeps the shared root. Removing the final reference deletes the shared root and registry entry; a locked or inaccessible root is retained safely and the failure is reported.
- Invalid, missing, stale, or mismatched shared-install metadata produces an actionable error, not a private-install fallback or a plausible success.
- Shared-mode backups do not duplicate binaries, and restore cannot delete/replace a shared root. Existing private full backups remain readable/restorable.
- The .NET 10 solution and self-contained publish build with no .NET Framework compiler/runtime dependency. Plugin compilation/cache tests cover the new v2 capability reference.

## Explicitly out of scope for the first slice

- Enabling sharing for every game or every plugin in one release.
- Sharing from v1 plugins. Only explicitly capable WindowsGSM built-in game integrations and v2 plugins may opt in; built-ins must still pass the game-specific support audit.
- Automatic conversion/deduplication of existing private installations.
- User-selected arbitrary shared-install paths, symlink/hardlink tricks, or a background deduplication service.
- Exposing shared-install state, paths, badges, or extra columns in the Server List; the dedicated read-only navigation view is the visibility surface for this first slice.
- Implicit update/repair during attach, updates to running installations without game-specific evidence, or background cleanup of unreferenced binaries outside an explicit server deletion.
- Making v1 plugins shared-install aware or introducing .NET Framework compatibility assemblies to compile them.