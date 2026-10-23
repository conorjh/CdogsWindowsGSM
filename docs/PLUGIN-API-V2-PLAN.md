# Plugin API v2 Compatibility and Capabilities Plan

Status: Proposed; intended for iteration before implementation.
Investigated: 22 October 2026.

## Goal

Introduce a versioned plugin contract that adds opt-in server-native soft restarts and plugin-defined backup scope, while continuing to load existing v1 plugins with their current behavior. Add other capabilities only behind explicit, discoverable interfaces; do not make every plugin implement new methods.

The first release should preserve the current plugin installation layout, the v1 runtime contract, normal Restart behavior, full-server backups, and existing backup archives. A v2 capability must never silently fall back to a different, success-shaped operation.

## Current implementation and constraints

Paths below are relative to the repository root.

| Area | Current behavior | Design consequence |
|---|---|---|
| Discovery | `CdogsWindowsGSM\Functions\PluginManagement.cs` scans `plugins\<PluginName>.cs\<PluginName>.cs` and expects `WindowsGSM.Plugins.<PluginName>`. | Keep discovery and type naming unchanged for v1 and preferably v2. |
| Plugin shape | The loader compiles source and constructs the plugin type through a constructor that accepts `ServerConfig` (metadata discovery passes `null`; per-server instances receive the server config). It reads the public `Plugin` metadata and dispatches game operations dynamically. The development template exposes methods such as `Start`, `Stop`, `Install`, and `Update`, plus game settings. | Existing plugins do not implement an explicit versioned interface. Do not add required members to the v1 shape. |
| Compiler | Runtime compilation currently pins C# 7.3, references runtime trusted-platform assemblies plus the application assembly, and the app project pins `Microsoft.CodeAnalysis.CSharp` 5.9.0. The development project references the application project. | Updating Roslyn and replacing/auditing the compiler reference set are required parts of the .NET Framework-free v2 work, not optional follow-ups. |
| Restart | `MainWindow.GameServer_Restart` clears process state, invokes the plugin's `Stop`, waits, then calls the normal start path. The built-in game-server audit confirms this is the current restart behavior. | Preserve Restart as-is. Soft Restart must be a separate opt-in operation, not a replacement or an implicit fallback. |
| Backup | `GameServer_Backup` archives the whole `servers\<id>` directory to ZIP, then applies retention after success. The existing scheduler uses the same backup path and requires a stopped server. | Keep full-server backup as the v1/default behavior. New scope selection must integrate with retention and scheduled backups. |
| Restore | `GameServer_RestoreBackup` deletes the existing server directory before extracting the archive. | A config-only archive must use a new, staged merge/targeted-restore path; it must never go through the destructive full-restore path. |
| Cache | Plugin compilation has a content-addressed cache whose fingerprint includes compiler references and the application assembly. | Verify v2 contract changes invalidate cached compilation correctly; keep plugin API behavior separate from cache format unless the cache inputs actually change. |

## Required .NET Framework-free compiler baseline

The v2 migration must remove .NET Framework as a compiler/runtime dependency. Upgrade the Roslyn package/toolchain and its reference-loading path as required for the .NET 10 host; do not preserve a Framework compiler or reference-assembly fallback.

- Compile plugins for the host's supported .NET runtime using only the matching .NET 10 reference/runtime assemblies, supported Windows platform references, and the WindowsGSM plugin API. If the runtime trusted-platform-assembly list remains the source, verify that every reference belongs to the supported .NET 10 runtime; prefer the matching target reference pack when that gives a more accurate compile-time surface.
- Audit the whole solution and published dependency graph for .NET Framework target frameworks, reference-assembly packages, `mscorlib` references, CodeDom providers, and bundled legacy compiler artifacts. Remove active dependencies; do not mistake a multi-target NuGet package's unused Framework asset for an active runtime dependency without checking the resolved graph.
- Keep the C# language-version decision separate from the Roslyn/runtime-reference update. Recommendation: initially retain the current v1 syntax level unless the v2 API needs newer syntax, then consider a language-version increase independently.
- Define v1 compatibility as existing plugin source compiling against the v1 API surface and preserving expected host-visible behavior. Do **not** require identical emitted DLL bytes, MVIDs, IL, metadata ordering, or compiler diagnostics.
- Plugins that depend on .NET Framework-only APIs or libraries are not guaranteed to compile under the new runtime. Surface Roslyn diagnostics clearly and document the expected modernization path; do not fall back to Framework assemblies to make those sources compile.

The contract remains source-compatible for v1 plugins that use supported APIs: they do not need to add a v2 marker or change source solely to be recognized as v1. Recompilation under the updated Roslyn is expected; byte-for-byte output matching is not.

## Recommendation: additive versioning plus optional capabilities

Use an explicit v2 contract marker and small capability interfaces rather than treating every new method as mandatory. Internally, adapt loaded plugins to one host-side runtime abstraction:

- **Unmarked v1 plugin:** load through the existing conventions and a `V1PluginAdapter`. Missing optional behavior remains unsupported; all existing operations keep their current dispatch and results.
- **Marked v2 plugin:** validate its declared API version, load its typed contract, and discover the capabilities it implements.
- **Invalid or unsupported declaration:** report a specific plugin load error. Do not silently downgrade a plugin that declares v2 but fails v2 validation.

Draft names such as `IWindowsGSMPluginV2`, `ISoftRestartProvider`, and `IBackupPlanProvider` are illustrative, not final API naming. The exact shape should be settled in Phase 0. Keep existing public v1 types and their names in place. Prefer adding the first v2 interfaces to the current application assembly; defer extracting a separate API assembly until its type-identity and compatibility costs are understood.

The adapter should expose capability availability and typed operation results. Do not use broad exception swallowing or treat a missing method, failed command, or invalid backup selection as success.

### Version recognition and errors

- Absence of the v2 marker means v1. This is necessary because current third-party plugins cannot declare a version retroactively.
- A v2 marker must carry or imply an exact supported API version. Unknown future versions are rejected with an actionable diagnostic.
- Keep the required filename/type/constructor contract stable unless a later migration explicitly changes it.
- Do not require v1 plugins to add a v2 marker or change source solely for version detection. Verify that representative v1 source plugins compile and preserve expected operations under the updated Roslyn and .NET 10 reference set; do not compare emitted assembly bytes.
- If a v1 source uses a .NET Framework-only API/reference, report the compiler diagnostic and document the required source migration rather than reintroducing a Framework dependency.
- Record the detected API version and capabilities in plugin diagnostics so support reports can distinguish v1 fallback from v2 support.

## Capability 1: soft restart

Add an optional v2 capability that requests the game's own restart behavior, commonly by sending a documented command such as `restart` to the redirected standard-input stream. A provider may use a game-specific management channel when stdin is unavailable; the host must not assume every server redirects a console.

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

## Capability 2: plugin-defined backup scope

Let a plugin describe the files needed to preserve its configuration and saves, so a small backup need not contain server binaries and caches. The host should continue to create and manage the archive; a plugin should not write arbitrary ZIP files into the backup store.

### Recommended initial model

Add a v2 backup-plan provider that returns a validated, declarative manifest for a server instance:

- Default mode: **Full server**, preserving today's behavior.
- Optional mode: **Selected paths**, including explicit files/directories and, if needed, exclusions rooted at approved locations.
- Support multiple named source roots so games whose saves/configuration live outside `serverfiles` can participate. Store a root identifier in the archive manifest so relative paths cannot collide.
- Allow plugin logic to calculate the plan from `ServerConfig`, but keep archive creation, destination, temporary-file publication, retention, and restore under host control.
- Keep per-server user choices and retention in host-owned configuration. Plugin defaults may suggest a scope; they must not silently override a user's persisted choice.
- A future, separately reviewed hook may let plugins prepare a consistent snapshot or provide generated content. Do not let a plugin replace the archive/restore pipeline in the first cut.

This covers both static plugin-declared file lists and dynamic plugin logic that returns a list of required files, without handing plugins ownership of backup integrity or retention.

### Archive and restore compatibility

- Add a versioned archive manifest containing backup format version, plugin/game identity, server identity, scope, named roots, and archived relative paths.
- Keep legacy ZIPs without a manifest recognizable as full-server backups and restore them through the existing legacy path.
- Give scoped archives a distinct extension that the legacy `*.zip` restore picker does not list (for example, a ZIP payload with a `.wgsmbackup` extension). The new host can recognize and validate them; an older host must not accidentally offer a scoped archive to its destructive full-directory restore flow.
- Restore a scoped archive by staging and validating it, then merging only its declared files into the existing server tree. Never delete the entire server directory for a scoped restore. Provide a clear confirmation and show the destination/affected paths before applying it.
- Reject absolute paths, `..` traversal, root escapes, duplicate/colliding destinations, and unsafe links/reparse points. Validate all entries before writing any files. Fail explicitly if required paths cannot be read or safely mapped.
- Preserve current atomic publication and retention rules: publish only a complete archive and prune old archives only after success. Make full and scoped backup types clear in the backup list.
- Keep the current stopped-server requirement initially. A live, consistent backup must be an explicit separate capability with server-specific evidence, not an accidental consequence of v2.

### Acceptance checks

- A v1 plugin, or a v2 plugin without a backup provider, still produces the existing full-server archive.
- A selected-path plan includes the requested configuration/save files and demonstrably excludes large unrelated binaries/cache content.
- The resulting archive is smaller for a representative install where large files are excluded; record input size, archive size, and selected path list rather than setting one universal reduction percentage.
- A scoped restore preserves unrelated server files and modifies only validated manifest destinations. A legacy full backup still restores through the legacy-compatible path.
- Invalid, missing, inaccessible, or unsafe paths fail with an actionable error and do not publish a partial archive or apply retention.
- Manual and scheduled backups share the same validated plan and result reporting; running-server behavior remains the current safe skip/fail behavior.

## Additional features worth considering

Prioritize these only after the v2 foundation is stable:

Shared game installation is tracked in the [shared installation sub-plan](SHARED-INSTALLATION-PLAN.md). Treat it as a later optional v2 capability: v1 plugins remain private-only, and sharing must use explicit installation and instance roots rather than adding required members to the v1 `Install()` convention. A capable provider separates one-time shared-binary installation from `InstallConfigOnly`, which provisions only non-conflicting per-instance configuration after the binary root is ready. Built-in game integrations may implement the equivalent host capability directly, but only after game-specific support is verified.

1. **Multi-root save/config backup:** include external config or save directories as named roots. This is directly relevant to config-only backups and is also present as a `SavesLocations` idea in the Raziel fork comparison.
2. **Structured console/RCON capability:** provide typed, explicit server commands and results for games with supported console/RCON transports. Avoid a general scheduled `exec` or arbitrary shell API; the fork comparison notes the elevated-privilege risk of such command execution.
3. **Readiness/health provider:** let plugins contribute a bounded server-ready/health result, which could improve restart verification and the existing health summary without assuming every game has the same query protocol.
4. **Plugin configuration schema:** allow a plugin to describe validated settings (for example, a config-only backup path or restart command) so users do not need to edit plugin source. Keep secrets masked and settings host-owned.
5. **Lifecycle notifications:** typed before/after install, update, start, stop, backup, and restore hooks. Defer until operation locking, cancellation, timeout, and failure semantics are defined; hooks can otherwise cause reentrancy or block critical work.

Avoid expanding v2 into a general scheduler or arbitrary command runner. The Raziel fork comparison documents cron actions including `exec`, console commands, RCON, and restart; reuse the useful server-console and multi-save concepts, not its unrestricted execution surface.

## Delivery phases

| Phase | Work | Exit evidence |
|---|---|---|
| 0. Contract and compiler baseline | Freeze the v1 surface, choose v2 marker/version negotiation, decide API assembly placement, define operation results, select pilot servers, and choose the supported Roslyn release plus .NET 10 reference source. Audit all projects and resolved dependencies for active .NET Framework compiler/runtime references. | A contract/compatibility table identifies representative v1 sources and expected behavior; the compiler plan has no Framework fallback and no unverified dependency assumptions. |
| 1. Roslyn upgrade, loader, and compatibility adapter | Upgrade the Roslyn package/toolchain, replace or verify compiler references as .NET 10-only, add v2 contract metadata, typed capability discovery, a v1 adapter, diagnostics, and developer examples. Re-fingerprint cached assemblies when compiler/reference inputs change. | Representative v1 plugin sources compile and their expected operations work; output byte identity is not required. Valid v2 loads; malformed/unknown v2 fails visibly and does not downgrade. The resolved compile references contain no active .NET Framework assemblies. |
| 2. Scoped backup and restore | Add provider/model, per-server configuration, archive manifest, safe path validation, atomic archive creation, targeted restore, and retention integration. Preserve legacy full archive support. | Full and scoped backup/restore acceptance checks pass, including preservation of files outside the scope and malicious/invalid path rejection. |
| 3. Soft restart lifecycle | Implement one verified provider, host operation coordination, status/readiness reporting, and a separate UI action. Keep all existing restart callers unchanged. | No Stop/Start is invoked for the soft action; process exit/replacement, timeout, unsupported, and failure cases are reported correctly. |
| 4. Broader integrations and documentation | Consider explicit Discord/schedule choices and additional providers only after the first integration is stable. Publish the v1/v2 authoring and migration guide. | All surfaces clearly label hard vs soft restart and full vs scoped backup; v1 behavior remains covered by regression checks. |

## Risks and decisions to revisit

- **Plugins are trusted code.** Versioning and path validation do not sandbox dynamically compiled C# plugins; they run with the application's privileges. Do not claim otherwise.
- **Restore is a data-safety boundary.** Scoped restore needs a separate non-destructive implementation because the existing restore deletes the complete server directory.
- **Native restart is not uniform.** A command may only be accepted, may stop the server, or may depend on an external wrapper. Capability support must be per plugin and evidence-based.
- **V1 behavior has no explicit marker.** Detection must default to v1 only when no v2 marker is present; invalid declared v2 must not silently become v1.
- **Compiler modernization is required.** Update Roslyn and the reference set to the .NET 10 host; keeping a .NET Framework fallback is out of scope. The current C# 7.3 syntax level can be retained temporarily, but it is a separate decision from the required compiler/reference update.
- **Assembly/versioning choice:** keeping interfaces in the host assembly is the smallest change, while a dedicated API assembly could provide a cleaner long-term contract. Do not move existing v1 types as part of this change.
- **Backup size is workload-dependent.** Measure representative server trees and report selected paths/bytes; do not promise a fixed compression or savings ratio.
- **Cache behavior:** verify that a host/API reference change invalidates the compiled plugin cache; a cache hit must not preserve a stale contract implementation.

## Decisions for the next iteration

1. Should v2 be identified by a marker interface, a plugin attribute, or both? Recommendation: one explicit marker/descriptor with a numeric API version, plus separate optional capability interfaces.
2. Should the first scoped-backup release allow roots outside the server directory? Recommendation: support named roots only through an explicit, canonicalized allow-list returned by the plugin and confirmed by the host.
3. Is a returned list of files/directories sufficient initially, or is generated snapshot content required on day one? Recommendation: start with declarative paths and defer arbitrary content streams/hooks.
4. What qualifies as successful soft restart for the first pilot server: command accepted, process replaced, or readiness/query recovered? Recommendation: report these states separately and never equate command acceptance with verified readiness.
5. Should Soft Restart be available only in the UI first, or also as a separate Discord/schedule operation? Recommendation: UI first; keep existing non-UI restart semantics unchanged.
6. Should the plugin source language level remain C# 7.3 for the first release? Recommendation: keep it initially to isolate language changes from the mandatory Roslyn/.NET 10 reference migration; raise it separately if v2 needs newer syntax.
7. Should plugin API types remain in the application assembly for the first release? Recommendation: yes, while retaining all current v1 types and adding tests for the runtime compiler/cache boundary.

## References considered

- `docs/PLUGIN-COMPILATION-CACHING-PLAN.md` — runtime compilation, assembly references, cache invalidation, and unchanged plugin loading behavior.
- `docs/DOTNET-10-MIGRATION-PLAN.md` — replacement of the legacy compiler path with Roslyn supported by the .NET 10 host.
- `docs/SERVER-MANAGEMENT-FEATURES-PLAN.md` — scheduled backups, stopped-server safety, atomic publication, retention, and visible result semantics.
- `docs/BUILTIN-GAME-SERVER-AUDIT.md` — current stop-then-start restart and application-level full-directory backup/restore behavior.
- `docs/RAZIEL-FORK-COMPARISON.md` — Raziel fork's scheduled console/RCON/restart commands, multi-save backup roots, and warning about arbitrary `exec`.
- `README.md` — existing third-party plugin ecosystem.
- Upstream WindowsGSM: <https://github.com/WindowsGSM/WindowsGSM>.
- Raziel WindowsGSM fork: <https://github.com/Raziel7893/WindowsGSM>.
