# Plugin API v2 Compatibility and Inferred Capabilities Plan

Status: The initial plugin-facing V2 contract is finalized for method-inferred config-only backups and shared installation. Capability discovery, typed contexts/results, host-owned config-only archive/restore integration, and C# 7.3 examples are implemented. Clean/published .NET 10 verification and live provider qualification remain open. The general host create/attach request model and typed built-in handler are tracked separately in [the shared-installation plan](../../docs/SHARED-INSTALLATION-PLAN.md); they do not change plugin method signatures. Soft restart is deferred and is not part of the initial V2 contract.
Investigated: 23 October 2026.

## Goal

Freeze the initial optional plugin capabilities for config-only backups and shared installation while continuing to load existing v1 plugins with their current behavior. Infer capabilities from exact public method signatures; do not require an API-version attribute, marker interface, or capability interface.

The first release should preserve the current plugin installation layout, v1 runtime behavior, normal Restart behavior, full-server backups, and existing backup archives. Full-server backups are host-managed copies of the whole per-server directory and do not call plugin backup methods. Config-only backups are a separate host-managed path; a plugin may supply only the per-instance file list.

## Current implementation and constraints

Paths below are relative to the repository root.

| Area | Current behavior | Design consequence |
|---|---|---|
| Discovery | `CdogsWindowsGSM\Functions\PluginManagement.cs` scans `plugins\<PluginName>.cs\<PluginName>.cs` and expects `WindowsGSM.Plugins.<PluginName>`. | Keep discovery and type naming unchanged for v1 and preferably v2. |
| Plugin shape | The loader compiles source and constructs the plugin type through a constructor that accepts `ServerConfig` (metadata discovery passes `null`; per-server instances receive the server config). It reads the public `Plugin` metadata and dispatches game operations dynamically. The development template exposes methods such as `Start`, `Stop`, `Install`, and `Update`, plus game settings. | Existing plugins do not implement an explicit versioned interface. Do not add required members to the v1 shape. |
| Compiler | Runtime compilation pins C# 7.3, references .NET 10 runtime trusted-platform assemblies plus the application assembly, and the app project pins `Microsoft.CodeAnalysis.CSharp` 5.9.0. | Keep this baseline fixed for the initial V2 contract. Clean-machine and published-app verification remain required; no .NET Framework fallback is allowed. |
| Restart | `MainWindow.GameServer_Restart` clears process state, invokes the plugin's `Stop`, waits, then calls the normal start path. | Preserve Restart as-is. Soft restart is deferred; it is not an initial V2 capability and must not change existing restart behavior. |
| Full-server backup | `GameServer_Backup` archives the whole `servers\<id>` directory to ZIP, then applies retention after success. The existing scheduler uses the same host-owned path and requires a stopped server. | Keep this as the default. It does not query plugin capabilities or invoke plugin methods. |
| Config-only backup | The host validates a declared list of per-instance files and creates a `.wgsmbackup` archive. | The plugin describes paths only; the host reads, archives, validates, restores, and applies retention. |
| Restore | `GameServer_RestoreBackup` deletes the existing server directory before extracting a full-server archive. | A config-only archive must use a staged merge/targeted-restore path; it must never go through the destructive full-server restore path. Full restore of a shared-root owner is blocked while linked servers still use its `serverfiles`. |
| Cache | Plugin compilation has a content-addressed cache whose fingerprint includes compiler references and the application assembly. | Verify v2 contract changes invalidate cached compilation correctly; keep plugin API behavior separate from cache format unless the cache inputs actually change. |

## Host implementation snapshot

- A plugin with no optional typed methods remains v1 and keeps the existing constructor, metadata, and dynamic lifecycle convention. No plugin-authored version marker or marker interface is required. The host infers the capability level from exact public method signatures and reports partial or malformed method sets visibly.
- A public `GetConfigOnlyBackupPlan(PluginInstanceContext)` method infers config-only backup support and returns a declarative `PluginBackupPlan`. The host maps named roots, validates path containment, duplicate destinations, and plan size, and owns archive creation, restore, and retention. The currently approved plugin root is only the per-instance `serverfiles` root; external roots are not exposed yet. GoldSrc's built-in plan is host-owned and separately allows only its server-specific mapcycle file beneath the shared binary root.
- Shared-install support is inferred only when both typed methods are present: `Task<Process> InstallSharedBinaries(SharedInstallationContext)` and `Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext)`. The binary method follows the regular install contract and returns the installer process for host monitoring; the config-only method reports its terminal result explicitly. New shared binary roots are stored in the first server's `servers\\<id>\\serverfiles`; linked instances keep their own `serverfiles` directories for per-instance files. The host derives install identity from `SharedInstallationAppId`/`SharedInstallationExecutableName`, falling back to existing `AppId`/`StartPath` members. Typed method presence does not establish concurrent-instance safety or live qualification. New built-in sharing is offered for Rust, supported Source titles, CoD1, and GoldSrc; SDK2013 remains gated. GoldSrc config-only backup is a separate built-in capability and includes only per-instance configuration paths.
- The in-repository v1 skeleton and method-inferred capability examples compile against the host references with the supported C# 7.3 plugin language level. The from-scratch authoring path is documented in the [tutorial](PLUGIN-FROM-SCRATCH-TUTORIAL.md), and migration from an existing plugin is covered by the [V1-to-V2 guide](PLUGIN-V1-TO-V2-GUIDE.md). This is compile evidence, not live game-server qualification.
- Plugin compiler references still come from the .NET 10 runtime trusted-platform-assembly list plus the host assembly. The local project/package audit found no active Framework compiler/reference package; clean-machine plugin and published-app qualification remain required. No Framework fallback is permitted.

### Config-only backup file-list method

Keep the existing plugin constructor, `Plugin` metadata, and lifecycle members. A public method with the exact `GetConfigOnlyBackupPlan(PluginInstanceContext)` signature is sufficient for the host to infer config-only backup support; the method returns file paths and does not perform file or archive I/O. The complete source template is `CdogsWindowsGSM-Plugin-Development\Examples\SkeletonV2.cs`; the abbreviated shape is:

```csharp
public class Example
{
	public Example(ServerConfig serverConfig) { }

	public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
	{
		return new PluginBackupPlan(new[]
		{
			new ConfigurationBackupPath("serverfiles", "server.cfg")
		});
	}
}
```

Use only root IDs supplied in `context.ApprovedRoots`; return relative file paths, not absolute destinations. The host rejects unknown roots, traversal, duplicates, empty plans, and oversized plans. The plugin does not read files into an archive or restore them; the host does both using this same validated path list. Full-server backups remain independent of the plugin method. A shared-install plugin must also provide `GetConfigOnlyBackupPlan`; a backup plan alone does not grant shared-install eligibility. Sharing still requires its own typed method pair and live concurrency/write-set evidence.

## Compiler baseline and evidence criteria

The initial V2 toolchain uses Roslyn 5.9.0, C# 7.3 plugin syntax, the .NET 10 host references, and the WindowsGSM application assembly. The compiler/runtime path must not depend on .NET Framework or preserve a Framework compiler/reference-assembly fallback.

- Compile plugins for the host's supported .NET runtime using only the matching .NET 10 reference/runtime assemblies, supported Windows platform references, and the WindowsGSM plugin API. If the runtime trusted-platform-assembly list remains the source, verify that every reference belongs to the supported .NET 10 runtime; prefer the matching target reference pack when that gives a more accurate compile-time surface.
- Audit the whole solution and published dependency graph for .NET Framework target frameworks, reference-assembly packages, `mscorlib` references, CodeDom providers, and bundled legacy compiler artifacts. Remove active dependencies; do not mistake a multi-target NuGet package's unused Framework asset for an active runtime dependency without checking the resolved graph.
- Keep the initial C# 7.3 syntax level fixed. Any language-version increase is a separate future decision and must not silently widen the host compile surface.
- Define v1 compatibility as existing plugin source compiling against the v1 API surface and preserving expected host-visible behavior. Do **not** require identical emitted DLL bytes, MVIDs, IL, metadata ordering, or compiler diagnostics.
- Plugins that depend on .NET Framework-only APIs or libraries are not guaranteed to compile under the new runtime. Surface Roslyn diagnostics clearly and document the expected modernization path; do not fall back to Framework assemblies to make those sources compile.

The contract remains source-compatible for v1 plugins that use supported APIs: they do not need to add a v2 marker or change source solely to be recognized as v1. Recompilation under the updated Roslyn is expected; byte-for-byte output matching is not.

## Frozen initial architecture: preserve v1 and infer optional capabilities

This is the frozen plugin-facing contract for the initial V2 release. It covers config-only backups and shared installation without replacing the V1 plugin shape, adding a marker/interface, or changing the plugin loading layout. Soft restart and other future capabilities require separate design and do not block this contract.

### Frozen plugin surface

| Plugin shape | Required public surface | Inferred behavior |
|---|---|---|
| V1 | No optional typed capability methods. | Existing lifecycle behavior; private installation and host-managed full-server backups. |
| Config-only backup | `PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)` | Config-only backup; the host owns file access, archive creation, and restore. |
| Shared installation | `GetConfigOnlyBackupPlan(PluginInstanceContext context)`, `Task<Process> InstallSharedBinaries(SharedInstallationContext context)`, and `Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)`, plus public string fields or properties named SharedInstallationAppId or AppId and SharedInstallationExecutableName or StartPath. | Shared binaries are installed once in the first server's `serverfiles`; each server gets its own config/setup. The current host policy requires the config-only plan for shared installation. |

Only exact public instance signatures opt into a capability. No version attribute, marker interface, or new base class is used. A partial shared-install pair, a recognized method with the wrong return type, or shared-install methods without the backup plan fails visibly rather than advertising shared support. A plugin with `GetConfigOnlyBackupPlan` alone does not receive shared-install support.

Keep existing v1 lifecycle methods unchanged. Infer each optional capability from a narrowly defined method signature, then adapt it to one host-side runtime abstraction:

- **V1 plugin:** when no optional typed capability methods are present, load through the existing conventions. Missing optional behavior remains unsupported; all existing operations keep their current dispatch and results.
- **Inferred capability:** validate the exact method signatures, contexts, return types, and required install identity members, then expose the matching capability to the host.
- **Partial/malformed method set:** report a specific plugin load error. Do not silently treat an incomplete shared-install pair as a usable provider.

The method names are `GetConfigOnlyBackupPlan`, `InstallSharedBinaries`, and the typed `InstallConfigOnly`; the host-side contexts/results live in the current application assembly. Keep existing public v1 types and their names in place. A separate API assembly remains a possible later design, not part of this migration.

The adapter should expose capability availability, the shared-binary installer `Process`, and typed config-only operation results. Do not use broad exception swallowing or treat a missing method, failed command, or invalid backup selection as success.

### Capability inference and errors

- No optional typed methods means v1. Existing v1 plugins do not need to change source just to continue loading.
- A `GetConfigOnlyBackupPlan(PluginInstanceContext)` method infers config-only backup support. Shared-install support requires both typed install methods and a usable app ID/executable identity.
- If only one shared-install method is present, or a method with the supported context has the wrong return type, reject the plugin with an actionable diagnostic.
- Keep the required filename/type/constructor contract stable unless a later migration explicitly changes it.
- Do not require v1 plugins to add a v2 marker or change source solely for version detection. Verify that representative v1 source plugins compile and preserve expected operations under the updated Roslyn and .NET 10 reference set; do not compare emitted assembly bytes.
- If a v1 source uses a .NET Framework-only API/reference, report the compiler diagnostic and document the required source migration rather than reintroducing a Framework dependency.
- Record the inferred capability level and capabilities in plugin diagnostics so support reports can distinguish unchanged v1 plugins from plugins using optional methods.

## Deferred future capability: soft restart

Soft restart is explicitly outside the initial V2 contract. The following remains a future design proposal, not an implemented plugin capability or an Alpha exit gate. If revisited, it could request the game's own restart behavior, commonly by sending a documented command such as `restart` to the redirected standard-input stream. A provider may use a game-specific management channel when stdin is unavailable; the host must not assume every server redirects a console.

### Contract responsibilities

- The plugin/provider owns the game-specific command and its transport. It must not implement soft restart by calling the ordinary Stop/Start sequence.
- The host owns preconditions, operation serialization, UI/status updates, timeout handling, process tracking, and reporting.
- Return a typed result that distinguishes unsupported, rejected, command accepted, verified complete, and failed. Include a diagnostic for failure. Do not report completion merely because a command write succeeded.
- Allow cancellation/timeouts and document whether the server is expected to keep the same process, exit and be relaunched by its own wrapper, or be replaced through a host-observed process change.
- Start with one or more game types whose command semantics can be verified. Do not infer that the literal `restart` command is universal.

### Host lifecycle and UX

- Add a distinct **Soft Restart** action only when the selected plugin advertises the capability. Keep the existing **Restart** action as stop-then-start.
- Do not automatically switch auto-restart, scheduled restart, bulk restart, or existing Discord restart commands to soft restart. Add explicit soft-restart choices to those surfaces only in a later, separately validated step.
- Serialize soft restart against start, stop, restart, update, backup, and restore for the same server.
- Coordinate expected process exits with `OnGameServerExited` and auto-restart logic so a normal soft restart is not misclassified as a crash or followed by a second restart. Refresh process/PID, detected port, query, and UI state when the process changes.
- If the plugin cannot verify readiness, report the narrower fact (for example, “restart command sent; completion not confirmed”), not “Restarted.” If unsupported or rejected, do not fall back to hard restart without explicit user action.

### Acceptance checks

- A v1 plugin without the capability still loads and uses the existing Stop/Start restart behavior.
- A v2 provider sends only its documented native command; no host Stop/Start call is made.
- Unsupported, rejected, timed-out, and accepted-but-unverified outcomes are distinguishable in logs and UI.
- A process that exits or is replaced during soft restart does not trigger duplicate auto-restart, lose the new process handle, or leave stale port/query state.
- Existing manual, bulk, scheduled, auto-restart, and Discord restart behavior remains unchanged unless a user explicitly chooses the new action.

## Initial capability: plugin-declared config-only file list

Let a plugin list the per-instance configuration files to preserve without including server binaries. The host creates and manages the archive; a plugin does not read/write archive files and does not implement the regular full-server backup.

### Recommended initial model

Infer config-only backup support from a method that returns a validated, declarative file list for a server instance:

- Default mode: **Full server**. The host copies the whole per-server folder and does not call plugin methods.
- Optional mode: **Config-only**. The plugin lists explicit configuration files; the host reads them into `.wgsmbackup` and restores only those validated destinations.
- The current approved plugin root is per-instance `serverfiles`; additional roots and directory selections require separate host review.
- Allow plugin logic to calculate the file list from its `ServerConfig`, but keep file reads/writes, archive creation, destination, temporary-file publication, retention, and restore under host control.
- Keep per-server user choices and retention in host-owned configuration. Plugin defaults may suggest a scope; they must not silently override a user's persisted choice.
- A future, separately reviewed hook may let plugins prepare a consistent snapshot or provide generated content. Do not let a plugin replace the archive/restore pipeline in the first cut.

This covers both static file lists and a method that calculates the list from instance context, without handing plugins ownership of backup integrity or retention.

### Archive and restore compatibility

- Add a versioned archive manifest containing backup format version, plugin/game identity, server identity, scope, named roots, and archived relative paths.
- Keep full-server ZIPs without a manifest recognizable and restore them through the existing host-owned full-server path.
- Give config-only archives a distinct extension that the legacy `*.zip` restore picker does not list (`.wgsmbackup`). The host recognizes and validates them; an older host must not offer one to its destructive full-directory restore flow.
- Restore a config-only archive by staging and validating it, then merging only its declared files into the existing server tree. Never delete the entire server directory for a config-only restore. Provide a clear confirmation and show the affected paths before applying it.
- Reject absolute paths, `..` traversal, root escapes, duplicate/colliding destinations, and unsafe links/reparse points. Validate all entries before writing any files. Fail explicitly if required paths cannot be read or safely mapped.
- Preserve current atomic publication and retention rules: publish only a complete archive and prune old archives only after success. Label the two modes **Full server** and **Config-only**.
- Keep the current stopped-server requirement initially. A live, consistent backup must be an explicit separate capability with server-specific evidence, not an accidental consequence of v2.

### Acceptance checks

- A v1 plugin, or any plugin without the inferred `GetConfigOnlyBackupPlan(PluginInstanceContext)` method, still uses the host's full-server backup.
- A config-only file list includes only the requested configuration files and demonstrably excludes unrelated binaries, saves, and logs.
- The resulting archive is smaller for a representative install where large files are excluded; record input size, archive size, and selected path list rather than setting one universal reduction percentage.
- A config-only restore preserves unrelated server files and modifies only validated manifest destinations. A legacy full-server backup still restores through the legacy-compatible path.
- Invalid, missing, inaccessible, or unsafe paths fail with an actionable error and do not publish a partial archive or apply retention.
- Manual and scheduled backups share the same validated plan and result reporting; running-server behavior remains the current safe skip/fail behavior.

## Related shared-install work and future extensions

Shared installation is part of the initial V2 contract and is tracked in the [shared installation sub-plan](../../docs/SHARED-INSTALLATION-PLAN.md). A plugin's shared-install capability requires `GetConfigOnlyBackupPlan`, the typed method pair, and the required install identity members. The pair separates one-time shared-binary installation from `InstallConfigOnly`, which provisions per-instance configuration after the binary root is ready. `InstallSharedBinaries` returns `Task<Process>` like the regular install operation; only `InstallConfigOnly` returns an explicit operation result. New shared binaries live in the first server's `serverfiles` directory, so a full backup of that owner includes the shared binaries; full restore is blocked while other servers still use them. This backup-plan requirement is current host policy, not a technical dependency of shared binaries. V1 plugins remain private-only. Detected methods are not runtime proof: do not treat a provider as Alpha-qualified until its shared write set, concurrent-instance behavior, update impact, and rollback have been verified. The built-in selection gate includes Rust, supported Source titles, CoD1, and GoldSrc; SDK2013 remains excluded, and live Alpha qualification is still pending.

For future additions beyond the frozen initial V2 contract, consider these separately:

1. **Multi-root save/config backup:** include external config or save directories as named roots. This is directly relevant to config-only backups and is also present as a `SavesLocations` idea in the Raziel fork comparison.
2. **Structured console/RCON capability:** provide typed, explicit server commands and results for games with supported console/RCON transports. Avoid a general scheduled `exec` or arbitrary shell API; the fork comparison notes the elevated-privilege risk of such command execution.
3. **Readiness/health provider:** let plugins contribute a bounded server-ready/health result, which could improve restart verification and the existing health summary without assuming every game has the same query protocol.
4. **Plugin configuration schema:** allow a plugin to describe validated settings (for example, a config-only backup path or restart command) so users do not need to edit plugin source. Keep secrets masked and settings host-owned.
5. **Lifecycle notifications:** typed before/after install, update, start, stop, backup, and restore hooks. Defer until operation locking, cancellation, timeout, and failure semantics are defined; hooks can otherwise cause reentrancy or block critical work.

Avoid expanding v2 into a general scheduler or arbitrary command runner. The Raziel fork comparison documents cron actions including `exec`, console commands, RCON, and restart; reuse the useful server-console and multi-save concepts, not its unrestricted execution surface.

## Implementation and qualification status

| Area | Current state | Remaining evidence or work |
|---|---|---|
| Plugin-facing V2 contract | Finalized for config-only backups and shared installation. | Future capabilities require separate design; they do not add requirements to V1 plugins. |
| Method inference and compatibility | Implemented with exact typed signatures, V1 preservation, diagnostics, and examples. | Verify representative V1 source behavior and the .NET 10-only compile/publish path on a clean machine. |
| Config-only backup and restore | Plugin file-list method and host-owned archive/targeted-restore path are implemented. | Complete live backup/restore qualification for each provider; keep full-server backup independent. |
| Shared-install plugin integration | Typed plugin contexts/results and method-inferred host integration are implemented. | The general host create/attach request model and typed built-in handler remain tracked in the [shared-installation plan](../../docs/SHARED-INSTALLATION-PLAN.md); live concurrent-instance/update/recovery qualification remains open. |
| Author documentation | The from-scratch tutorial and V1 migration guidance are in the plugin-development project. | Keep steps aligned with the solution, helper project, and runtime compiler. |
| Soft restart | Deferred; not in the initial V2 contract. | Revisit as a separate capability proposal without changing existing restart behavior by default. |

## Risks and decisions to revisit

- **Plugins are trusted code.** Versioning and path validation do not sandbox dynamically compiled C# plugins; they run with the application's privileges. Do not claim otherwise.
- **Restore is a data-safety boundary.** Config-only restore needs a separate non-destructive implementation because the existing full-server restore deletes the complete server directory.
- **Native restart is not uniform.** A command may only be accepted, may stop the server, or may depend on an external wrapper. Capability support must be per plugin and evidence-based.
- **V1 behavior has no explicit marker.** No supported optional methods means v1. A malformed method or partial shared-install pair must fail visibly rather than silently advertising a capability.
- **Compiler boundary:** Roslyn 5.9.0, C# 7.3 plugin syntax, and .NET 10 host references are implemented. Clean-machine and published-app evidence remains open; a .NET Framework fallback is out of scope.
- **API assembly:** keep API types in the existing application assembly for the initial contract. A separate API assembly is a future change and must not move existing V1 types as part of this release.
- **Backup size is workload-dependent.** Measure representative server trees and report selected paths/bytes; do not promise a fixed compression or savings ratio.
- **Cache behavior:** a regression test verifies that changing the host/API reference hash produces a new plugin cache key and misses the prior entry; retain this guarantee as the API evolves.

## Decisions and remaining questions

Decided for the initial V2 contract:

1. Infer optional capabilities from exact public method signatures; no API marker or marker interface is needed. Plugins without those methods remain v1.
2. Keep the API types in the existing application assembly and preserve the current v1 constructor, metadata, and dynamic lifecycle convention.
3. Keep plugin syntax at C# 7.3 and compile against the .NET 10 host references; do not add a .NET Framework fallback.
4. Use a declarative config-only file list. The host currently approves only the per-instance `serverfiles` root; external roots and generated snapshot content remain deferred. Full-server backup remains a separate host-owned whole-directory copy.
5. Shared installation requires the config-only plan, both typed install methods, and install identity members. Requiring the plan is host policy; it does not mean sharing and backup are inherently the same capability.
6. Defer soft restart. It is not part of this initial V2 contract.

Implementation and evidence still open:

1. The host-level shared create/attach request model and typed built-in handler remain open in the shared-installation plan; they do not change the plugin method contract.
2. Which plugin/game providers have enough live evidence to be enabled for shared installs or config-only backups in Alpha? A capability declaration alone is not qualification.
3. Clean published-app and clean-machine evidence for the .NET 10-only compiler/runtime path.

## References considered

- [Plugin compilation caching](PLUGIN-COMPILATION-CACHING-PLAN.md) — runtime compilation, assembly references, cache invalidation, and unchanged plugin loading behavior.
- `docs/DOTNET-10-MIGRATION-PLAN.md` — replacement of the legacy compiler path with Roslyn supported by the .NET 10 host.
- `docs/SERVER-MANAGEMENT-FEATURES-PLAN.md` — scheduled backups, stopped-server safety, atomic publication, retention, and visible result semantics.
- `docs/BUILTIN-GAME-SERVER-AUDIT.md` — current stop-then-start restart and application-level full-directory backup/restore behavior.
- `docs/RAZIEL-FORK-COMPARISON.md` — Raziel fork's scheduled console/RCON/restart commands, multi-save backup roots, and warning about arbitrary `exec`.
- `README.md` — existing third-party plugin ecosystem.
- Upstream WindowsGSM: <https://github.com/WindowsGSM/WindowsGSM>.
- Raziel WindowsGSM fork: <https://github.com/Raziel7893/WindowsGSM>.
