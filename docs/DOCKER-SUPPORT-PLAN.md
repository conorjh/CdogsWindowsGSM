# Docker container support: feasibility and implementation plan

Status: proposed; no runtime implementation included.
Investigated: 20 October 2026.

## Recommendation

**Feasible, with a bounded first release.** Keep CdogsWindowsGSM as a Windows GUI and add Docker as a second server runtime alongside native Windows processes. Manage containers provided by existing image maintainers; do not try to put the WPF application itself into a Linux container.

Start with **local Docker Desktop, Linux containers, x86-64, one container per server**, and a curated Minecraft Java profile using `itzg/minecraft-server`. This is a candidate to validate, not an already certified integration. Minecraft gives us persistent worlds, a known image configuration contract, game commands and an existing query integration to exercise. Add a UDP-based game as the next compatibility gate before claiming broad game-server support.

This is a medium-sized architectural feature, not a small plugin. A sensible planning allowance is **4-7 engineer-weeks for an experimental release**, contingent on the proof of concept below. Docker is optional; existing installations must work without it.

### What support would mean

- Install by pulling an image and creating a managed container.
- Start, stop, restart, view logs, reconnect after the GUI restarts, and query players through published ports.
- Preserve game data across container replacement, with tested backup/restore and image-update workflows.
- Offer game-specific settings through curated profiles; offer basic custom-image lifecycle management later.
- Reuse the existing server list, scheduling and Discord actions through the same runtime-aware action path.

It would **not** mean that every image works automatically. Images differ in environment variables, data directories, port protocols, stop behavior, update policy and command interfaces. An image packages software; it is not a standard game-server management API.

## Evidence from this repository

Paths below are relative to the repository root. Symbols are included because line numbers will move.

| Area | Current implementation | Docker consequence |
| --- | --- | --- |
| Application platform | `CdogsWindowsGSM\CdogsWindowsGSM.csproj`: WPF, .NET Framework 4.7.2, AnyCPU with Prefer32Bit disabled | No framework migration is required for a CLI-based Docker integration. The GUI stays Windows-only. |
| Game/plugin selection | `GameServer\Data\Class.cs`, `Class.Get`; `Functions\PluginManagement.cs`, `GetPluginClass` | Preserve the existing dynamic native/plugin contract behind an adapter rather than changing every game and third-party plugin. |
| Lifecycle | `MainWindow.xaml.cs`, `ServerMetadata`, `Server_BeginStart`, `Server_BeginStop`, `GameServer_Start/Stop/Restart` | A server is currently a `Process`; startup sets Windows priority/affinity and window handles, and stop eventually kills that process. These cannot manage a container directly. |
| Recovery and identity | `MainWindow` constructor; `Functions\ServerCache.cs`; `LoadServerTable`; `Functions\ServerTable.cs`, `Uptime` | Recovery, display and uptime use cached Windows PIDs/process names. Containers require daemon-scoped container IDs and inspected start times instead. |
| Background work | `StartAutoUpdateCheck`, `StartRestartCrontabCheck`, `StartSendHeartBeat`, `StartQuery`, `OnGameServerExited` | These loops/events depend on `Process.HasExited` or `Process.Exited`. They must follow runtime state, not the lifetime of a Docker CLI child. |
| Console | `Functions\ServerConsole.cs`, `Input`; `MainWindow.SendCommand` | Native stdin/Win32 window messages do not provide a general container command channel. Logs and commands must be separate capabilities. |
| Install/import/update | `Button_Install_Click`, `Button_Import_Click`, `Server_BeginUpdate`, `GameServer_Update` | Current import copies a Windows server directory; update expects native game build methods and sometimes an installer process. Container create/pull/recreate needs a separate implementation and explicit operation results. |
| Storage and backup | `Functions\ServerConfig.cs`; `GameServer_Backup`, `GameServer_RestoreBackup`, `GameServer_Delete` | Backup zips the whole server directory and restore deletes/replaces it. This does not capture Docker volumes or container metadata safely by itself. |
| Networking | `WindowsFirewall.cs`; `ServerConfig.GetAvailablePort`; `StartQuery` | Firewall rules authorize Windows executables, and port allocation considers the primary listed game port. Docker needs complete TCP/UDP publication mappings and host-side queries. |
| Remote actions | `DiscordBot\Commands.cs`; `MainWindow` public `*ServerById` methods | Discord must call the same runtime-aware actions and report unsupported operations or failures, not bypass capability checks. |
| FiveM example | `GameServer\GTA5.cs`, `Start`, `Install`, `IsImportValid` | This integration explicitly downloads Windows artifacts and launches `server\FXServer.exe`. A Linux FiveM image would need its own tested profile, not reuse of that executable path. |

In this table, paths under `GameServer` and `Functions` are inside `CdogsWindowsGSM`.

**Why not just return `docker.exe` from a plugin?** `docker start` can exit successfully while the container keeps running. A logs/attach process can fail while the game remains healthy, and killing that process does not reliably stop the container. Treating either as the game process would corrupt status, crash recovery, schedules, uptime and stop behavior.

## Host feasibility and boundaries

| Host/workload | Assessment |
| --- | --- |
| Supported Windows desktop + Docker Desktop in Linux-container mode | Recommended initial target. WSL 2 is the baseline backend; verify Docker's current OS, virtualization and memory prerequisites during setup. |
| Windows Server hosting Linux images | Not covered by the first release. Docker Desktop is not supported on Windows Server. A separately managed Linux VM/host with Docker Engine is a viable future route, but requires remote storage/network/backup design. |
| Windows-container images | Possible later, but not interchangeable with Linux images. Engine mode, base-image/host compatibility and image-specific behavior need separate qualification. Reject these in the Linux MVP. |
| Existing Windows-only game executables | Docker does not make them Linux-compatible. Require a native Linux server image or an explicitly supported compatibility-layer image; do not promise universal Wine support. |
| ARM images/hosts | Defer. Validate image OS/architecture and reject unsupported combinations rather than rely on emulation. |
| Dockerfile or Compose bundles | Defer building images and multi-container stacks. A Compose deployment with a database is not equivalent to one game-server container. |

Docker Desktop has separate licensing terms, including paid subscription requirements for certain commercial uses. Link users to the current terms; do not bundle Docker, install it silently or change host virtualization/engine settings automatically. Account for VM memory/disk overhead and nested-virtualization restrictions on hosted Windows machines.

**Validation limitation:** the Docker CLI was not available on PATH in the investigation environment. No image was pulled, server started, performance benchmark run or Docker/host configuration changed. This assessment is based on source inspection and the documentation linked below. Working-runtime claims are deliberately deferred to the proof of concept.

## Proposed design

### 1. A small runtime boundary, preserving native compatibility

Introduce an internal `IServerRuntime` with a native adapter and a Docker implementation. Keep game identity/profile separate from execution mode: the same game can be native or containerized without changing the names stored by existing plugins.

The boundary should cover start/stop, inspect/reconnect, lifecycle notifications, log subscription and optional game-command delivery. Install/update/backup are runtime-aware operations on the same coordinator, not pretend `Process` results. Return typed results containing success/failure, actionable error details and runtime identity.

Expose capabilities for command input, updates, backup/restore, query, native window display, process priority/affinity and resource metrics. Disable unsupported controls with an explanation, and enforce the same checks for Discord and automation. Do not add a general orchestration framework or rewrite game implementations.

All existing configuration files default to `Native` when no runtime setting exists. Existing `Process`, Win32 console handling and plugin calls remain in the native adapter. The Docker path must never enter Windows process cleanup, window manipulation or PID-based recovery.

### 2. Use the installed Docker CLI initially

Use one internal Docker client wrapper that launches the resolved `docker.exe` directly with `UseShellExecute=false`. This avoids a new client-library compatibility decision for .NET Framework 4.7.2 and uses Docker's existing context/credential mechanisms. An Engine API client can replace it later if measured streaming/process overhead warrants that change.

Requirements:

- Select and persist an explicit local Docker context; inspect its endpoint and require an approved local named-pipe endpoint for the MVP. Do not silently follow the user's changing default context or inherited `DOCKER_HOST`/`DOCKER_CONTEXT` settings. Never run `docker context use` globally.
- Probe client/server reachability, engine OS/architecture and required command support. Distinguish missing CLI, unavailable daemon, access denied, unsupported engine and missing image.
- Use argument construction tested for Windows command-line quoting **and Docker's own argument grammar**. Framework 4.7.2 lacks the newer `ProcessStartInfo.ArgumentList`; do not assume that API exists. Reject unrepresentable mount paths rather than concatenate unsafe arguments.
- Do not invoke `cmd.exe`, PowerShell or arbitrary shell fragments. Render settings from typed fields; never accept a pasted `docker run` command as executable input.
- Consume stdout/stderr concurrently, parse JSON where available, check exit codes, and apply operation-specific timeouts/cancellation. Long image pulls need progress and a different timeout from inspect calls.
- Cancellation kills only the app-owned CLI child if necessary, then reconciles Docker state: canceling a client request does not prove the daemon canceled the operation.
- Bound log history and dispose/cancel streams on server removal, reconnect and GUI exit. A log-stream failure is not a game-server crash.

Do not introduce an unauthenticated TCP Docker socket or mount the Docker control socket into game containers. Docker access is highly privileged; only trusted administrators should configure it.

### 3. Persist desired configuration separately from observed identity

Add a runtime discriminator to `WindowsGSM.cfg`. Store a versioned structured container definition in `servers\<id>\configs\container.json`; the existing flat game-settings format is unsuitable for port/mount/environment lists. Keep runtime cache separate from this desired definition.

Minimum definition:

- Profile ID/version, image repository and selected tag/digest; game version when separately controlled by the image.
- Explicit context/endpoint identity, a persistent installation UUID and server UUID. Label containers with these ownership identities; the numeric server ID/name alone is unsafe because it can be reused or copied in a backup.
- TCP/UDP mappings containing host bind address, host port, container port and protocol; optional separate query/admin endpoints.
- Profile-approved environment fields, writable data mount(s), stop timeout, readiness check and optional CPU/memory limits.
- References to protected credentials, not secrets embedded in displayable definitions or logs. Use Windows-protected secret storage; explain that Docker administrators can still inspect secrets supplied to containers.

Cache the full container ID and observed image ID/digest, start time and state. Inspect and verify ownership before every destructive operation. On moving/cloning/restoring a server, create or explicitly rebind ownership rather than reconnect to another installation's live container. A missing container must not be confused with an unreachable daemon.

For the first profile, bind-mount only the game-data directory beneath `servers\<id>\serverfiles` to the image's documented data destination. Do not mount the parent server folder: it contains management configuration/cache. Use `--mount` with an existing validated source and validate Docker-specific delimiters as well as path traversal/reparse-point escapes.

Windows bind mounts are convenient for the existing file UI but may be slower or incompatible with Linux permissions, symlinks or case-sensitive layouts. Benchmark and test the chosen image. If it requires Linux-native storage, stop and revise the MVP to named volumes plus explicit export/restore; do not silently switch storage while retaining a misleading ZIP backup button.

### 4. Lifecycle, automation and recovery

- Create managed containers separately from starting them; use detached execution, retain stopped containers and avoid `--rm`.
- Treat Docker inspect as authoritative. Distinguish running from ready/healthy, exited, paused, restarting, missing and unavailable. Preserve last-known information when the daemon disappears; show an unavailable/unknown state, not a crash.
- Use filtered Docker events plus periodic inspect reconciliation. Events are not a durable history (Docker retains only a limited event backlog); reconcile after every stream reconnect.
- Start succeeds only after inspecting the intended container. Keep it in Starting until the profile's readiness condition succeeds or a bounded startup timeout produces a clear failure. Images without readiness support must say "running; readiness unknown" rather than imply playability.
- Stop uses the profile's supported save/shutdown method where needed, followed by `docker stop` with a suitable timeout. Report forced termination and inspect the final state; never kill a CLI process as a substitute for stopping the container.
- Serialize operations per server. Suppress crash handling during intentional stop/update/delete, reject conflicting actions, and avoid duplicate schedules/watchers after reconnect.
- For the experimental release, keep WindowsGSM as the **single restart-policy owner**, with Docker restart policy `no`. Existing AutoRestart behavior is coordinated through runtime state with bounded retry/backoff. Do not combine it with Docker automatic restarts. If daemon-owned restart is added later, it must be an explicit mutually exclusive mode.
- Closing the GUI leaves already running containers running, consistent with native servers remaining outside the GUI lifetime. With restart policy `no`, crash restart and scheduled tasks require the GUI to be running; state this clearly. AutoStart means start on WindowsGSM launch, not guaranteed pre-login service operation.
- On reopening, reconnect to owned containers before applying AutoStart. Never start a duplicate container because a PID cache or event stream is missing.

### 5. Networking, logs and game commands

Publish every required game/query/admin port with its explicit protocol. Do not rely on image `EXPOSE` metadata, host networking or container-internal IPs. On Docker Desktop, clients and the GUI query the Windows host's published endpoint. Inside the container, games generally bind to their container interface/all interfaces, not the Windows host's LAN IP.

Use complete mappings for collision checks against native servers and other container servers; Docker's bind result is the final authority because other host processes may also occupy a port. Allocate port groups together and report the exact conflicting address/protocol/port. Keep public and query endpoints distinct when necessary.

Do not apply the existing executable firewall rule to `docker.exe` or a nonexistent Linux executable. Explain Docker Desktop backend/firewall behavior, allow only explicitly scoped inbound rules with consent, and remove only rules created by this installation. Router forwarding remains an administrator responsibility. Publish game ports to a selected interface; keep RCON/admin access unpublished or loopback-only by default. Test from a second LAN machine, not just localhost.

Stream bounded, timestamped container stdout/stderr into the existing console; reconnect without unbounded duplication. Some images log only to files: profiles must declare whether live logs are available.

Game commands are an optional profile capability, using a documented RCON connection or fixed image helper executed as argument tokens. `docker exec` is not automatically a connection to the game's stdin. Do not expose a generic container shell through the game-command field or Discord. Keep native window/priority/affinity controls disabled for containers; Docker CPU/memory limits have different semantics and need separately labelled controls.

### 6. Updates, backup, restore and removal

**Updates:** explicitly pull the configured release/tag, record its resolved digest and recreate the container with the same mappings/data. Starting a stopped server must not implicitly pull a newer image. Record the previous image and definition, keep them until the replacement passes readiness, and preserve data on failure. Pull/registry failures leave the old container usable.

An image digest does not necessarily pin the game: many server images download/update the game at startup. The Minecraft profile must pin its game version separately and expose any image-managed update behavior. Disable generic build polling/auto-update until the profile implements meaningful version comparison. Do not auto-upgrade worlds to a new Minecraft version.

Before changing a running deployment, stop and take a tested data backup. A new server can migrate saves incompatibly; reverting the image alone is not a safe world rollback. Recovery must offer the matching pre-update data backup and warn about lost changes. Keep the previous digest locally until acceptance; do not prune shared images automatically.

**Backup:** confirm Docker is reachable and the owned container is stopped, then archive all declared persistent data plus a portable definition and version manifest. Exclude PID/container caches and protected secrets; explain credential re-entry when restoring elsewhere. For the Windows-bind-mount MVP, a ZIP is acceptable only for profiles verified not to require Linux metadata preservation. Arbitrary volumes or external paths must block backup with an explanation until a proper exporter exists.

**Restore:** validate/stage the archive, stop/verify the owned container, restore the declared data safely, then recreate from the saved definition/digest with a new validated runtime identity. Do not reuse the existing delete-the-entire-directory restore routine while Docker still references its mounts. Failure must leave recoverable data, not a success-shaped partial restore.

**Remove:** show a separate confirmation for container removal and permanent data deletion. Default to retaining data; remove only resources bearing this installation/server's ownership identity. Never run system prune, remove unrelated containers or delete shared/external volumes. GUI/daemon failures must be recoverable on the next reconciliation.

## Phased delivery and acceptance gates

Estimates are working days for one engineer familiar with the code; phases are sequential and exclude maintainer review delays and unforeseen host/image compatibility work.

| Phase | Work | Exit evidence | Estimate |
| --- | --- | --- | --- |
| 0. Host/image spike | On a supported Windows test host, validate a chosen `itzg/minecraft-server` digest with explicit EULA acceptance, pinned game version, Windows data mount, published port, logs, command delivery and graceful stop. Record exact host/Docker/image versions. | Connect a real client; save/restart/recreate and reload the same world; restore a backup; verify file permissions and acceptable performance. Choose storage before proceeding. | 2-3 days |
| 1. Native-preserving boundary | Introduce runtime selection, native adapter, capabilities and typed action results; route startup/reconnect, manual/bulk/Discord actions and background loops through it. Keep existing plugins unchanged. | Native install/import/start/stop/restart/update/backup/restore and GUI recovery still work; existing plugin compiles/loads without edits. | 4-6 days |
| 2. Docker runtime | Implement CLI wrapper, preflight, validated definitions, ownership, pull/create/start/stop/inspect, event reconciliation and bounded logs. | Manage two isolated containers; reconnect without duplicates; distinguish CLI/log failure from game exit and daemon outage. | 5-8 days |
| 3. Experimental UI/profile | Add optional Docker setup, execution-mode selection, first profile, port/mount/environment editor, capability-aware controls and diagnostics. | User can install/manage the pilot without raw commands; EULA is explicit; GUI and Discord share validation/errors; no Docker required for native use. | 4-6 days |
| 4. Data safety and release | Finish update/recreate/recovery, backup/restore/delete confirmations, restart/schedule parity, fault tests and user documentation. Qualify a UDP profile before broad support claims. | Pass the matrix below; publish exact supported host/image combinations and feature limitations. | 5-8 days |

Total: 20-31 working days, approximately 4-7 weeks. Phase 0 is a go/no-go gate, not a promise to ship arbitrary-image support. Stop or revise scope if filesystem compatibility, performance or the native boundary cannot meet the acceptance checks.

### Verification matrix

These are future implementation tests, not results from this investigation. Add focused automated tests for pure validation/state/coordinator logic and run real-engine acceptance tests on a dedicated host.

1. **Backward compatibility:** load unmodified legacy configs; exercise representative SteamCMD, Java and FiveM native servers plus an unchanged third-party plugin. Verify native console, firewall, priority/affinity and backup behavior are preserved.
2. **Lifecycle accuracy:** a successful short-lived CLI process does not imply server exit; a killed log client does not trigger AutoRestart. Start/stop/restart, intentional stop versus crash/OOM, paused containers, duplicate clicks and simultaneous GUI/Discord actions converge to one correct state.
3. **Recovery timing:** with a healthy reachable local engine, reflect externally initiated start/stop within 5 seconds, report engine loss within 15 seconds, and reconcile within 15 seconds of connectivity returning. Make these implementation targets measurable, not blocking UI waits.
4. **Logs/readiness:** display new log lines within 2 seconds under normal local conditions; retain bounded history during a 30-minute high-output run. Profile startup deadlines must accommodate first-run downloads and clearly distinguish timeout, unhealthy and running-but-not-ready.
5. **Networking:** run two instances with different published ports, test both TCP and UDP from a second machine, and verify game/query/admin mapping. Reject conflicts involving native servers, secondary ports or external processes; test chosen host interfaces and firewall profiles.
6. **Persistence:** write identifiable world/config data, recreate/update, close/reopen the GUI and reboot the test host; verify expected data and ownership. Test spaces, Unicode and unsupported delimiter characters in Windows paths, permissions, disk exhaustion and unavailable mounts.
7. **Update failure:** registry unavailable, missing tag, canceled pull, replacement crash/readiness failure and image-managed game updates. Verify old deployment remains recoverable and rollback uses a matching data snapshot where needed. Start offline with cached image/data without an implicit pull.
8. **Backup/restore:** restore to a fresh server identity; verify known file hashes and world load, correct image/game versions, secret re-entry and no accidental live-container attachment. Reject unsupported volumes, unsafe archives and partial extraction with explicit errors.
9. **Ownership/deletion:** name collision, stale ID, reused numeric server ID, copied app directory and manually removed/replaced container must never mutate another server. Removal without data-deletion consent retains worlds. No unrelated Docker resources are changed.
10. **Policy/automation:** AutoStart, AutoRestart, scheduled restart, Update/Backup on Start, bulk actions and Discord observe capabilities and intentional-stop guards. Verify no double restart owner or restart storm after daemon recovery; document behavior while the GUI is closed.
11. **Input/privacy:** test quoting/argument injection, invalid port/environment/mount fields, remote context rejection and redaction of secrets from logs/UI/Discord/backups. Missing Docker must not affect native server startup.
12. **Performance:** compare the pilot with the same native game version/world and matched heap/CPU limits, after warm-up, using the same scripted workload for 30 minutes. Target no more than 10% regression in p95 tick time and no gameplay disconnects; record host/VM/container memory, disk I/O and startup time. If Windows bind mounts fail this target, revisit storage instead of hiding the result.

## Deferred scope

- Adopt/import existing arbitrary containers (start with discovery/read-only inspection later; do not take ownership implicitly).
- Custom images beyond validated structured fields, Compose stacks, image builds, databases and Kubernetes/Swarm.
- Remote Engine over SSH/TLS, including Windows Server + Linux VM deployments; daemon-host paths cannot be treated as GUI-local paths.
- Named-volume backup/export, Linux-metadata-preserving archives and external-storage management.
- Windows-container profiles, ARM/emulation, GPU and privileged/device-dependent images.
- Generic mod installers/file editors inside arbitrary image layouts, automatic migration from native Windows servers and full container resource dashboards.

## Sources and remaining decisions

Documentation consulted on 20 October 2026; compatibility requirements and third-party image contracts must be rechecked when implementing.

- [Docker Desktop Windows requirements, Windows Server exclusion and licensing](https://docs.docker.com/desktop/setup/install/windows-install/).
- [Docker Desktop WSL 2 backend and Windows CLI usage](https://docs.docker.com/desktop/features/wsl/).
- [Docker Desktop networking, published ports and firewall visibility](https://docs.docker.com/desktop/features/networking/).
- [Docker contexts and endpoint selection](https://docs.docker.com/engine/manage-resources/contexts/).
- [Bind mounts, Windows Desktop sharing and daemon-host path constraints](https://docs.docker.com/engine/storage/bind-mounts/).
- [Stop signals and stop timeouts](https://docs.docker.com/reference/cli/docker/container/stop/).
- [Docker events and limited replay history](https://docs.docker.com/reference/cli/docker/system/events/).
- [Docker restart-policy semantics and avoiding competing process managers](https://docs.docker.com/engine/containers/start-containers-automatically/).
- [`itzg/minecraft-server` configuration, EULA, persistent data and startup download behavior](https://docker-minecraft-server.readthedocs.io/en/latest/).

Phase 0 must settle the exact supported Docker version range, image digest/game version, command/readiness mechanisms and Windows-storage performance. Select the second UDP-game image based on maintainer quality, distribution rights, data layout and graceful shutdown behavior. These are explicit qualification tasks, not assumptions that every bundled server image is trustworthy or compatible.
