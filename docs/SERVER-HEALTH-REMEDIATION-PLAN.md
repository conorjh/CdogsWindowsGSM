# Server health warning remediation plan

Status: Source/GoldSrc port mismatch confirmed for 10 servers in the follow-up live probe; code fix awaits runtime verification on Sherri. ID 15 remains unresolved.
Investigated: 22 October 2026.
Scope: the WindowsGSM server-list screenshot supplied with this request.

## Incident snapshot

The screenshot shows 10 running rows with a yellow health warning, six running rows with a healthy check, and one stopped row. All rows show `192.168.30.2` and the same display name, so use server IDs to distinguish them. The stopped server (ID 32) is not part of this incident.

| Server ID | PID | Displayed main port | Screenshot health |
|---:|---:|---:|---|
| 8 | 15596 | 27519 | Warning |
| 9 | 18332 | 27027 | Warning |
| 12 | 4548 | 27030 | Warning |
| 13 | 18812 | 27032 | Warning |
| 14 | 13360 | 27415 | Warning |
| 15 | 17604 | 27213 | Warning |
| 17 | 11716 | 27046 | Warning |
| 18 | 18124 | 27042 | Warning |
| 19 | 14492 | 27044 | Warning |
| 27 | 12424 | 16261 | Warning |

Healthy comparison rows in the screenshot are IDs 7, 10, 16, 21, 28, and 29. Compare their game/query configuration with the warning rows before attributing the issue to the shared host or LAN.

## What the warning does and does not establish

WindowsGSM's overall health combines a process check with a supported game-query check. In the current implementation, the process check reports Healthy, Unhealthy, or Unknown; it does not report Warning. Therefore these yellow Warning rows point to the Query check, although the process result still needs to be recorded separately. A query warning does not prove that the server process has stopped. Select each row and read its health details and the corresponding server log before taking action.

The query check must resolve the endpoint per engine. Source and GoldSrc A2S queries use the detected main UDP port (or configured server port when discovery is unavailable); their stock launchers do not expose a separate query-only port. Other supported query methods use their configured query endpoint. Polling is about every five seconds. An endpoint that has never returned a valid query response remains `Unknown` with failure detail. After a successful baseline, one or two consecutive failures remain `Unknown`; three or more failures or a query check older than 20 seconds are warnings. A game type without a supported query is `Unknown`, not a query failure. Process health is reported separately.

For the follow-up live screenshot on Sherri, A2S responses identified the expected servers on the main ports for IDs 3, 6, 8, 9, 12, 13, 14, 17, 18, and 19; their configured `main port + 1` query ports did not respond. ID 15 did not respond on either tested port and remains unresolved. A green main-port indicator alone still does not prove that the game-query protocol responds; test the protocol at the engine's actual endpoint.

## Remediation sequence

### 1. Capture evidence without disturbing the fleet

For each of the ten IDs above, record:

- Game/mod, server ID, PID, process start time, and the configured IP.
- Displayed main port and configured query port, including the query protocol expected by that game.
- The per-check health result (Process and Query), the full explanation/error, last query attempt, last successful query, and the time of observation.
- Relevant server log messages around startup and the failed query.

Use the row's health tooltip/selected-server health details and preserve a fresh screenshot or written results. Do not bulk restart servers or change health thresholds while collecting this baseline.

**Exit evidence:** each Query warning is classified as a query configuration/protocol/network failure, stale sample, or unexpected health result; the Process result is recorded separately.

### 2. Confirm process state and server readiness

Match each PID to the expected game-server executable and confirm it has not exited or been replaced since WindowsGSM started it. Review the game's startup log for fatal errors, bind failures, or a query-service startup error. Where practical, verify that a client can join the server using its intended address and main port.

If the Process check is unhealthy or the PID is no longer the expected process, investigate that lifecycle problem separately rather than treating the yellow warning as proof of a stopped process. If Process is healthy and Query is warning, leave the process running and continue with the query endpoint checks.

**Exit evidence:** process state is confirmed independently of query response; any process failure has a specific log or lifecycle cause.

### 3. Validate each game-query endpoint

For every row whose Query check is warning:

1. Compare the configured `QueryPort` with the game's actual configuration, launch arguments, and startup log. Confirm the address is the intended interface (`192.168.30.2` in the screenshot), and that the game is listening on the required query port and protocol.
2. Check for per-server port collisions or a query port accidentally copied from another server. Group results by game/query implementation and compare warning servers with healthy IDs on the same host.
3. Test with a game-aware query client or the game's supported protocol from the WindowsGSM host. If the service is intended to be publicly reachable, separately test from an external client using the public address and expected NAT/forwarding path.
4. Review only the relevant host/network firewall and router rules. A generic TCP connection test does not establish that a UDP game query works; use a protocol-appropriate test.

Do not open broad port ranges to the internet. Add or change only the required protocol/port after confirming the server's expected configuration.

**Exit evidence:** each endpoint either returns a valid game-query response or has a specific, reproducible configuration/network failure.

### 4. Apply the narrowest confirmed fix

Correct only the cause found for the affected server(s), for example a wrong query port, bind address, protocol-specific firewall/forwarding rule, port collision, or game-specific query configuration. Capture the before/after values. Restart only a server that needs a configuration change, and do so deliberately after preserving its logs and current state.

If the endpoint is confirmed healthy but WindowsGSM still reports a warning, reproduce it and inspect the relevant query implementation, exception/timeout, and health timestamps. Make a targeted code fix with regression coverage rather than masking the warning or treating every query failure as a server crash.

### 5. Verify recovery and watch for recurrence

After the fix, observe each affected server for at least 60 seconds (about twelve polling intervals). For a supported query, require both Process and Query to report healthy, with a recent successful query and no repeated failures. Confirm that a client can join through the intended route when external availability is part of the server's requirement. Recheck the six healthy comparison rows and confirm the stopped server remains stopped.

If a server's query is genuinely unsupported, `Unknown` is the expected state; do not count that as a successful query or force it to green. If warnings recur despite a successful protocol-level test, retain the full error, timestamps, game/query type, and configuration for a focused product fix.

## Completion criteria

- Every warning ID has a recorded cause and a corresponding resolution or a reproducible follow-up case.
- A supported query endpoint returns valid responses and WindowsGSM shows fresh Process and Query health for at least the observation window.
- Healthy comparison servers remain healthy; ID 32 remains stopped.
- No unrelated server was restarted, and no broad firewall rule, disabled check, or warning-threshold change was used to make the indicators look healthy.

## Implementation references

- `CdogsWindowsGSM\Functions\ServerTable.cs` — process/query health rules and the 20-second stale-query threshold.
- `CdogsWindowsGSM\MainWindow.xaml.cs` — query polling, query inputs, and per-server health refresh.
- `CdogsWindowsGSM\MainWindow.xaml` — separate main-port and query-port columns.
- [Server management features plan](SERVER-MANAGEMENT-FEATURES-PLAN.md#3-modular-server-health) — intended health-check behavior and existing acceptance checks.