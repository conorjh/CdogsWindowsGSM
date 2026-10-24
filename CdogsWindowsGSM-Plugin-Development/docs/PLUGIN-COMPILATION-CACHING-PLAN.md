# Plugin Compilation Cache Plan

Status: implemented; the stable .NET 10 Release build and cache smoke checks pass for hits, corruption recovery, concurrent publication, and plugin source invalidation. Compilation-duration measurements remain outstanding.
Investigated: 22 October 2026.

## Goal and recommendation

Avoid recompiling unchanged installed plugins on every application start. Keep each plugin's `.cs` source as the source of truth, and persist only its compiled assembly as a rebuildable cache. Reuse a cached assembly only when the plugin source and every effective compiler input still match; otherwise compile the current source and publish a new cache entry.

The cache must not change the plugin API, plugin discovery rules, metadata or image loading, or compile-error reporting. A cache miss or cache failure must never prevent a plugin that compiles successfully from loading.

## Current implementation

Paths below are relative to the repository root.

| Area | Current behavior | Design implication |
|---|---|---|
| Plugin layout | `CdogsWindowsGSM\Functions\PluginManagement.cs` scans `WGSM_PATH\plugins` for directories ending in `.cs`, then expects the source file with the same name inside that directory (for example, `plugins\Example.cs\Example.cs`). | Keep the source layout and discovery logic unchanged. Store cache files outside `plugins` so they cannot be mistaken for installed plugins. |
| Compilation | `PluginManagement.LoadPlugin` uses Roslyn `CSharpCompilation` with C# 7.3 parsing, Release optimization, and references from the runtime's trusted-platform-assembly list plus the application assembly. | Cache the emitted assembly; do not cache a `PluginMetadata` object or a plugin instance. |
| Startup | `MainWindow` calls `LoadPlugins(shouldAwait: false)` during startup. A cache miss still compiles synchronously, matching the existing startup behavior; a hit skips Roslyn compilation. | Keep miss behavior unchanged and let cache failures fall back to source compilation. |
| Import and refresh | Plugin import and **Refresh** call the same loader. Unchanged source can reuse its verified assembly; plugin objects, metadata and images are still loaded again. | Keep startup, import and refresh on the same fingerprint/cache path. |
| Load results | The loader resolves `WindowsGSM.Plugins.<source-file-name>` and then reads plugin metadata and image files. Compile/load failures are written to the existing plugin log. | On a hit, retain the existing type lookup, plugin construction, metadata, image, and failure behavior. Assets are read at load time and need not invalidate the compiled code. |

## Implemented design

### 1. Cache location and identity

- Store entries below an application-level `WGSM_PATH\cache\plugins\v1` directory, created on demand. Do not put generated assemblies in plugin source folders or the per-server `servers\<id>\cache` directories.
- Use a hash of the plugin's stable source-file name for its cache directory, and a SHA-256 fingerprint for each compiled version. This avoids using arbitrary plugin names as cache paths and permits safe, content-addressed entries.
- Include a cache-format version in the fingerprint. A future change to the cache format or loading contract can then invalidate old entries without changing or deleting plugin sources.
- Cache only compiled output and its small manifest. Treat both as disposable generated data, never as the authoritative plugin or user configuration.

### 2. Fingerprint every compilation input

Build a compilation-environment fingerprint once per plugin-load operation, then combine it with each plugin's inputs. Include:

- The exact plugin source bytes and file name/expected type name. Read one source snapshot and use that same snapshot both to calculate the key and to compile, so edits during loading cannot associate a binary with the wrong key.
- The Roslyn compiler assemblies and module identities, target framework/runtime and process architecture, C# language version, compilation options, and the cache-format version.
- The resolved identities and content hashes of all references used to compile, including the entry assembly and trusted runtime/framework references.

Do not use file modification time or length as the cache key; those can remain unchanged when source or a referenced assembly is replaced. Keep the list of references and compiler options in one shared definition used by both fingerprinting and compilation, so they cannot drift apart.

### 3. Cache hit

- Require a manifest whose format version and compilation fingerprint match. Verify the cached assembly's SHA-256 against the manifest before loading it; a missing, partial, or mismatched pair is a cache miss, not a plugin success.
- Load the verified assembly from bytes rather than by path, avoiding a lock on the cache file. Resolve the same expected plugin type and continue through the existing metadata and image-loading path.
- If a cache artifact cannot be loaded or does not contain the expected type, invalidate that entry and retry once by compiling the current source. Do not repeatedly retry a bad entry or silently treat it as loaded.

### 4. Cache miss and publication

- Compile the source snapshot with the current Roslyn references, options, diagnostics, and expected type contract. Emit to memory, then write the assembly bytes to a uniquely named same-volume temporary cache file.
- Only publish after compilation succeeds and the expected type is present. Under a per-fingerprint lock, publish the assembly from a same-volume temporary file, then atomically publish the manifest last as the commit marker. Readers accept only a complete pair with matching hashes; an absent manifest or incomplete temporary output is a miss.
- Do not cache compiler errors. Preserve the current per-plugin error-log behavior so fixing the source causes a fresh compile on the next load.
- If the cache is unreadable or unwritable, report the cache problem through the existing Plugins application log and continue with source compilation. If compilation succeeds but cache publication fails, load that successful result for the current run; do not turn a cache write failure into a plugin failure.
- Use unique temporary names so concurrent starts/refreshes cannot consume a partial assembly. If another load has already published a valid entry for the same fingerprint, discard the temporary output and use that entry; never let concurrent writers pair one assembly with another writer's manifest.

### 5. Refresh, cleanup, and compatibility

- An unchanged plugin source and compilation environment produce a hit on startup, import, and refresh. Any source-byte change—including a same-size edit with a preserved timestamp—or compiler/reference change produces a new fingerprint.
- Reload plugin metadata and image files on every normal load. The compiled-assembly cache must not become a cache of plugin instances or suppress the existing refresh behavior.
- Prune obsolete fingerprints and abandoned temporary files conservatively and best-effort. Cleanup must not block startup or delete an entry while it is being read; loss of any cache entry must simply cause a recompile.
- Keep the stable `.NET 10` Windows target and existing plugin trust model. Do not depend on unloading individual plugin assemblies during refresh; load cached bytes in the default application load context and reuse an already-loaded matching assembly within the process.
- If cache hits solve compilation cost but startup is still slow on a miss, handle moving startup compilation off the UI thread as a separate measured improvement. It is not required to establish persistent caching.

## Delivery phases

| Phase | Work | Exit evidence |
|---|---|---|
| 1. Cache key | Define the shared compiler-input configuration, environment fingerprint, source fingerprint, and versioned cache/manifest format. | The key changes when source or compiler inputs change and remains stable for unchanged inputs. |
| 2. Persistent cache | Add the application-level cache store at `WGSM_PATH\cache\plugins\v1`, manifest/hash verification, per-fingerprint locking, atomic publication, cache-hit loading, and source-compile fallback. | A successful first load writes a valid entry; an unchanged load can use the verified assembly bytes without Roslyn compilation. |
| 3. Integration and hardening | Route startup, import, and refresh through the same cache path; log cache diagnostics, prune old entries under locks, and keep cache artifacts disposable. | Cache failures, races, source edits, and compile errors do not produce stale or success-shaped plugin results. |

## Acceptance checks

- With the same installed plugin set, the first successful load compiles each plugin and writes a cache entry; a subsequent unchanged load reuses the verified output and performs **zero Roslyn compilations** for those plugins.
- Changing source bytes invalidates the entry even if file size and timestamp are preserved. Renaming the source, changing compiler options/provider, updating the application or a referenced assembly, or changing the target runtime also invalidates it.
- Corrupting, deleting, or making a cache entry inaccessible causes a logged cache miss/fallback and a normal source compile; a successfully compiled plugin still loads.
- Compiler errors are not cached; after correcting the source, the next load compiles and can succeed. A corrupt cached assembly or missing expected type is not reported as a successful plugin load.
- Importing or editing a plugin and pressing **Refresh** loads the new source and current image files. A subsequent unchanged refresh reuses the compiled output.
- Concurrent first loads never observe a partially written entry. Cache files are not locked after loading, and cleanup failures do not interrupt plugin loading.
- Compare plugin-load duration and compile counts before and after caching. Report compilation savings separately from remaining plugin initialization and UI work.

## Risks and scope boundaries

- A read-only application directory prevents persistent cache writes. The application must log this explicitly and continue compiling from source; repeated compilation in that environment is preferable to failing plugin loading.
- Fingerprinting compiler references costs file reads and hashing. The environment fingerprint is computed once per process and reused for each plugin; measure its cost against the compilation time saved.
- This change does not redesign plugin APIs, sandbox plugin code, change which plugins are trusted, or promise to remove all startup work. It eliminates repeated compilation for unchanged plugins; plugin assemblies still need to be loaded and initialized.
