# ServerHost Design Report

Status: proposed; not implemented.
Investigated: 22 October 2026.
Scope: preserve redirected server-console input and output across a graceful WindowsGSM close and relaunch.

## Executive summary

Add a small, hidden, per-user `ServerHost` process whose only job is to keep redirected console streams alive while the WindowsGSM UI is closed. WindowsGSM continues to start and manage game servers using its existing game-specific code. Before a normal UI exit, it duplicates the existing stream handles into the host. The host keeps those handles open, reads output, accepts input commands, and exposes the sessions to a relaunched UI through one local named pipe.

The host is not a Windows service, server launcher, restart manager, or game-specific policy layer. The server's anonymous pipes do not need to be renamed: the host retains duplicated handles to the existing endpoints. The named pipe is only the reconnect channel between WindowsGSM and ServerHost.

This proposal covers a normal, graceful WindowsGSM exit. It does not promise recovery if WindowsGSM is forcibly terminated before handoff, if ServerHost itself exits unexpectedly, or after a Windows reboot.

## Current behavior

- `MainWindow.Server_BeginStart` creates the game-specific server object and calls its `Start()` method. The game-server implementations create and start their own `Process` instances, with per-game command lines and redirection settings.
- Many game-server implementations attach `OutputDataReceived`/`ErrorDataReceived` handlers and call `BeginOutputReadLine`/`BeginErrorReadLine` themselves.
- `ServerCache` persists a process ID, process name, and window handle. On startup, `MainWindow` can use the PID to reattach for status and monitoring, but a new .NET `Process` object does not recover the old redirected stream readers and writers.
- `Window_Closing` currently saves UI settings, disposes the tray icon, and stops the Discord bot; it does not stop game servers.

The missing capability is therefore not process survival. It is keeping the parent-side stdin/stdout/stderr pipe handles alive, and giving the relaunched UI a way to use them.

## Goals and non-goals

### Goals

- Keep servers running when WindowsGSM closes normally.
- Restore redirected stdin/stdout/stderr access when WindowsGSM is relaunched.
- Preserve the existing per-game launch configuration and stop-command semantics.
- Keep a bounded amount of recent console output so the UI can restore its current console view.
- Leave window-based servers on the existing PID/window-control path.

### Non-goals

- Survive a Windows reboot or a ServerHost crash.
- Restart, update, install, schedule, query, or configure servers from ServerHost.
- Replace game-specific stop commands with generic assumptions.
- Add a Windows service, network listener, database, general-purpose job supervisor, or permanent output archive.
- Recover redirected handles for servers that were already running before this design is implemented and were never handed to a host.

## Recommended architecture

```text
WindowsGSM UI -- current-user named pipe --> ServerHost -- duplicated stdio handles --> game server
```

WindowsGSM remains responsible for server policy and game-specific behavior. ServerHost owns only the duplicated pipe endpoints and a small amount of session state. The UI may exit; the host continues holding and draining the streams. When WindowsGSM returns, it reconnects to ServerHost rather than trying to recreate the old anonymous pipes.

### ServerHost responsibilities

For each adopted server, retain:

- WindowsGSM server ID and process identity (PID, process start time, and expected executable name).
- The duplicated stdin writer and any redirected stdout/stderr reader handles.
- The stdout/stderr encoding information captured while the original `Process.StartInfo` is available.
- A bounded output buffer with sequence numbers. Use the existing 150-line console limit for the first version.

The host reads output continuously, accepts a generic `WriteLine(serverId, text)` request, returns output after a supplied sequence number, and releases its session when the process exits. It does not decide whether a command means `stop`, `quit`, or anything else; WindowsGSM supplies the game-specific command text.

### Local protocol

Use one versioned named pipe, restricted to the current Windows user/session. Keep the initial protocol small:

- `Adopt`: register a process and the handle values already duplicated into ServerHost.
- `ListSessions`: report active server IDs and process identities.
- `WriteLine`: send a line to a server's redirected stdin.
- `ReadOutputSince`: return buffered stdout/stderr lines after a sequence number.

Line-delimited JSON is sufficient for this local protocol; no external RPC package or TCP port is needed. Include a protocol version so an incompatible host/UI pair fails explicitly rather than silently losing console control.

## Lifecycle

### Graceful WindowsGSM close

1. Make the close path cancellable while the handoff runs. Start ServerHost if it is not already running, or connect to the existing host.
2. For each live process with redirected streams, capture its process identity, redirection flags, encodings, and current console tail while the original `Process` object is still available.
3. Stop the UI's output readers and duplicate the parent-side pipe handles into ServerHost with `DuplicateHandle`.
4. ServerHost opens readers/writers over the duplicated handles, registers the session, and acknowledges the handoff.
5. Complete the UI close only after the host acknowledges all sessions. Do not stop the servers as part of this transfer.

If the host cannot start or a session cannot be adopted, show the error. Let the user cancel closing and keep WindowsGSM open, or explicitly continue while accepting that console continuity will be lost. Do not report a successful handoff when it failed.

### WindowsGSM relaunch

1. Connect to ServerHost early in startup and request its active sessions.
2. Match each session to the saved server ID and validate the PID, process start time, and expected executable. This avoids treating a reused PID as the old server.
3. Continue using the existing `Process.GetProcessById` and `ServerCache` behavior for process status and non-console management.
4. Restore the recent output from the host's buffer. Route console input and redirected stop-command writes through the host for adopted sessions. Continue using the current window-based fallback for sessions without redirected input.

If no host session exists, keep the existing PID/window reattachment behavior and clearly indicate that redirected console control is unavailable. ServerHost should stay alive while it owns at least one live session and exit after the last adopted process exits.

## Code integration boundary

- Add a small companion executable and a `ServerHostClient` in WindowsGSM. Run the host hidden under the same user token and interactive session; do not run it as a service in Session 0.
- Wire host discovery into the existing startup reattachment path in `MainWindow.xaml.cs` and the handoff into `Window_Closing`.
- Add one shared `ServerConsole` input/output route for host-backed sessions. Update built-in stop handlers that write directly to `Process.StandardInput` to use that route, while retaining their existing game-specific command text and stop fallbacks.
- Leave `ServerCache` responsible for its existing PID/name/window cache. ServerHost's in-memory session registry is the authority for whether redirected streams are still available.
- Package and update the host executable alongside WindowsGSM so both components are released together.

The first version should not move per-game process creation into ServerHost. That would duplicate or relocate the existing game-specific startup logic and is unnecessary for a graceful UI-exit handoff.

## Implementation phases

| Phase | Work | Exit evidence |
|---|---|---|
| 0. Handle-handoff proof | Use a small child process that writes numbered stdout/stderr lines and echoes stdin. Verify handle duplication, output-reader cancellation, host reading, UI exit/relaunch, and post-relaunch input. | No stalled child; output continues while the UI is absent; the relaunched UI receives subsequent output and can send a command. |
| 1. Minimal host | Implement one host process, one named pipe, one session, handle lifetime, output ring buffer, and process-exit cleanup. | The proof-of-concept behavior is available through the host protocol. |
| 2. WindowsGSM integration | Add close-time handoff, startup reconnection, output restoration, and host-backed console writes. Keep window-only servers unchanged. | A redirected server can be closed/reopened in the UI without being restarted; a window-only server retains existing behavior. |
| 3. Multi-server and failure cases | Test multiple adopted sessions, server exit while the UI is closed, failed host startup, stale PID/session validation, and host cleanup. | Errors are visible and sessions/handles are released correctly. |
| 4. Release packaging | Include the host with the WindowsGSM build and updater package. | A clean install/update can start the host without a separately installed runtime or manual setup. |

## Main technical risk and decision gate

The current game-server implementations often call `BeginOutputReadLine` and `BeginErrorReadLine` before returning their `Process`. A .NET `StreamReader` may already have read bytes into a buffer that cannot be moved to another process. Cancelling the reader and duplicating the underlying handle could therefore lose a partial or recent line, or cause two readers to compete during handoff.

Phase 0 is mandatory. It must test the exact .NET reader/handle handoff, not just prove that `DuplicateHandle` works. Seed the host's buffer with the UI's existing console tail and use sequence-numbered output in the test to detect gaps or duplicates.

If the close-time handoff cannot be made reliable, do not ship a fragile workaround. The fallback is to make ServerHost the sole stdout/stderr reader from the time each process starts. Centralize the output-reader hookup and register the process handles with the host as soon as they are available, while leaving per-game launch configuration in its current location. This is a larger integration change, but preserves the same simple host responsibility.

## Acceptance checks

- A test child process continues running after WindowsGSM closes and does not block when it writes output.
- After relaunch, the UI restores the host's recent output and can send stdin commands to the same process.
- Stdout and stderr remain distinguishable or are combined consistently with the current UI behavior; configured encodings are preserved.
- Multiple redirected servers can be handed off and reattached independently.
- A process that exits while the UI is closed is removed from the host, and its handles are released.
- Window-only servers continue to use the current cached-window behavior.
- A host startup/protocol/handoff failure is reported explicitly; WindowsGSM never claims the console is attached when it is not.
- Forced WindowsGSM termination, ServerHost failure, and system reboot remain documented limitations for this first scope.