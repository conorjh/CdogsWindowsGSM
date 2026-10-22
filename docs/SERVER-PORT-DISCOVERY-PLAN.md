# Runtime Server Port Discovery Plan

Status: runtime client/main-port discovery is implemented. Query-port discovery is intentionally out of scope; server queries continue using the configured query port.
Investigated: 21 October 2026.
Scope: runtime discovery of the client/main port from TCP/UDP endpoints owned by each server process tree.

## Scope decision

Only the configured client/main port may be checked against live process endpoints. Prefer the configured client port when the server process owns it; otherwise exclude the configured query port when it differs from the configured client port, and report a client port only when one unambiguous candidate remains. Unknown or ambiguous results leave the configured value unchanged.

The configured query port remains a normal server setting used by query polling. WindowsGSM does not probe protocols to discover or replace it, and no query-port mismatch indicator is shown.

## Superseded query-port proposal (historical; not implemented)

Keep the configured port as configuration, and track a verified query endpoint as runtime state. Discover candidate sockets owned by the server process (and its child processes), then confirm a candidate through the server's query protocol. Do not infer the actual port from a successful response on the configured port alone: that endpoint may belong to the unrelated server that caused the conflict.

Show the configured value in the **Query Port** column with a tick when it is verified, or a cross and the detected query port in parentheses when it differs. Unknown and ambiguous values retain the configured display without a status mark. A tooltip explains the configured and detected values; no setting is persisted or overwritten.

The first implementation uses shared A2S, UT3, and FiveM query providers. Types without one of these providers need an explicit protocol/provider before they can be marked as detected; do not apply one assumed port-increment rule to every game.

## Historical query-port design and implications

Paths below are relative to the repository root.

| Area | Original proposal | Historical implication |
|---|---|---|
| Persisted settings | `CdogsWindowsGSM\Functions\ServerConfig.cs`, `ServerPort` and `ServerQueryPort` | These are the requested/configured ports. Do not overwrite them with a runtime observation. |
| Port allocation | `ServerConfig.SetData` and `GetAvailablePort` choose a configured port using each server's `PortIncrements` value. The plugin-development template describes this as the amount to skip after installation. | This is install-time allocation, not a verified description of a running server's conflict behavior. Do not use it as the live-port scan range without per-engine evidence. |
| Server list | `CdogsWindowsGSM\MainWindow.xaml.cs`, `LoadServerTable`, creates each row from the saved config. `CdogsWindowsGSM\MainWindow.xaml` has separate **Port** and **Query Port** columns. | Keep the configured query port separate from the verified runtime endpoint and show the verification in the **Query Port** column. |
| Runtime state | `MainWindow.ServerMetadata` already holds each server's `Process`; start, stop, restart and exit paths manage that state. `LoadServerTable` clears and rebuilds rows. | Store the detected port with the current process in metadata so a row rebuild does not lose it. Clear it when that process stops or is replaced. |
| Query loop | `MainWindow.StartQuery` polls every five seconds. | A2S, UT3, and FiveM providers first resolve a process-owned query endpoint; plugins without the provider continue to use their configured query port. |
| Query implementations | `A2S.cs`, `UT3.cs`, and `FIVEM.cs` implement the optional query-port provider. A2S EDF port-bit handling and GoldSrc response parsing are corrected. | A protocol response is accepted only from a process-owned candidate; query protocol support is not universal. |
| Plugin contract | Game server instances are created dynamically; the plugin template allows `QueryMethod = null`. | `IQueryPortDiscoveryProvider` is optional; existing plugins retain their configured-port behavior. |

Game and query ports can differ by design (for example, ARK SE is configured with 7777/27015 and Rust with 28015/28016). The superseded proposal would have reported only the verified **query endpoint** in the Query Port column; it did not claim to detect or change the primary game port.

## Historical query-port design (superseded)

### 1. Separate configured and observed values

- Keep `ServerConfig.ServerQueryPort` and `ServerTable.QueryPort` as the configured value. Existing allocation, validation, and notifications continue using that meaning.
- Store a nullable runtime query-port observation with the current server process/PID. Unknown or ambiguous must remain unknown, not turn into a guessed value.
- Do not persist the observed port in `WindowsGSM.cfg`. On application restart, display the configured value until a live process is reattached and its port is revalidated.

### 2. Add shared endpoint discovery with optional per-game providers

Use the Windows IP Helper TCP/UDP owner tables and the process parent/child tree to identify candidate sockets. Confirm candidates with a query-provider response, reject endpoints shared with an unrelated process, and leave the result unknown when multiple candidates respond. The configured port is only an ordering hint, not proof of ownership.

For A2S, UT3, and FiveM, accept a response only after confirming the candidate endpoint is owned by this server process tree. If ownership and the query protocol cannot identify one endpoint reliably, leave the observation unset and retain the configured display.

Represent query protocol/transport differences with the optional `IQueryPortDiscoveryProvider` capability. Plugins may implement it to opt into discovery; existing plugins without it keep working.

### 3. Integrate with server lifecycle

- Begin discovery after a successful process launch, allowing time for the server to bind. Use bounded startup retries; after a verified result, avoid a high-frequency scan. Recheck only as needed or when the observation is missing.
- Tie the observation to the process instance so a stale value cannot survive a crash or restart.
- Clear it on failed start, stop, crash/exit, and before starting a replacement process.
- When `LoadServerTable` recreates rows, copy the current runtime observation from metadata. Update/refresh the row when discovery changes its value.

## Historical protocol coverage proposal (superseded)

The 57 entries fall into these implementation groups. A shared query provider handles common protocols; no-query types still need explicit protocol/profile work.

| Detection family | Built-in types | Planned handling |
|---|---|---|
| GoldSource / A2S | CS, CSCZ, DMC, DOD, HLOF, RCC, TFC | Uses the shared A2S UDP provider and process-owned endpoints. Live duplicate-port verification still needs a manual server conflict test. |
| Source / A2S | CSGO, CSS, DODS, GMOD, HL2DM, INS, L4D2, NMRIH, SDK2013, TF2, ZPS | Uses the shared A2S UDP provider to identify the query endpoint; it does not infer the primary game port from the query port. |
| Other A2S types | ARKSE, BT, BW, CE, DAYZ, INSS, MORDHAU, OLOW, PS, RUST, SCUM, SE, SQ, UNT | Uses the shared A2S UDP provider when the configured query protocol is A2S. |
| UT3 query | MC, PaperMC, ForgeMC, MCPE | Uses the shared UT3 UDP provider to confirm the query endpoint. |
| FiveM query | GTA5, RedM | Uses the `dynamic.json` response on process-owned TCP listeners to confirm the query endpoint. |
| No current query method | AVORION, COD1, COD4, ECO, EGS, HEAT, MCBE, Mindustry, ONSET, OpenRA, OpenTTD, RCT2, ROK, ROR2, SDTD, Soldat, SW, TF, VTS | Remains on configured-port display until a protocol-specific provider can distinguish the query endpoint. No socket is guessed. |
| Plugins | Existing and future plugins | Plugins can implement `IQueryPortDiscoveryProvider`; existing plugins retain configured-port behavior. |

The existing audit, `docs\BUILTIN-GAME-SERVER-AUDIT.md`, has the full built-in inventory and current query-method assignments. The types listed here are grouped by their current implementation, not by an assumption that every game changes ports in the same way.

## Historical query-port UI proposal (superseded)

- Keep the configured value in the existing **Query Port** column. Append a tick when the verified endpoint matches; show a cross and the detected query port in parentheses when it differs.
- When stopped, unsupported, or not yet verified, show only the configured value. A tooltip distinguishes configured and detected values; sorting uses the detected endpoint when available.
- Preserve the existing configured-port semantics for configuration, allocation, validation, and notifications. Any decision to use the observed port in Discord messages or other surfaces should be a separate, intentional change.

## Historical phases

| Phase | Work | Exit evidence |
|---|---|---|
| 0. Feasibility spike | Use process-owned UDP/TCP tables and the process tree to identify query endpoints. | Superseded; protocol probing is not part of the requested feature. |
| 1. Runtime model and UI | Add nullable process-scoped state, lifecycle clearing, row-rebuild persistence, and the requested Query Port display. | Superseded; only client/main-port display is retained. |
| 2. Query providers | Confirm process-owned endpoints with A2S, UT3, and FiveM query responses. | Removed; configured query ports are used as-is. |
| 3. Remaining types and plugins | Add protocol providers for no-query types as evidence supports them; document unsupported/ambiguous cases. | Removed from scope; no query-port discovery provider contract is used. |

## Client-port verification plan

- Verify candidate resolution for configured client ports, process-child endpoints, TCP/UDP listeners, unrelated process owners, configured query-port exclusion, and ambiguous candidates.
- Verify the client-port observation is process-scoped, survives a grid refresh, does not modify `WindowsGSM.cfg`, and clears after stop, crash, and restart.
- Confirm the Query Port column displays only the configured value and query polling continues to use that value without probing or rewriting it.
- Manually test representative servers that bind multiple sockets and servers that fall back to another client port after a conflict; ambiguous results must keep the configured display.

No test project or test source files were found in the solution scan. Add a focused Windows-targeted test project/harness if these checks are automated; do not treat a successful build alone as proof of live client-port detection.

## Decisions before broad rollout

- Does the Windows socket-owner API cover all supported Windows versions and process cases without requiring new privileges beyond the app's current elevation?
- Which server types expose a stable, distinguishable client socket when they open multiple UDP/TCP endpoints?
- Should the runtime client-port observation be used only in the UI, or later in Discord notifications and other server links as well?
- What is the acceptable behavior for a running server whose client port cannot be identified reliably? Recommended default: keep the configured value and do not claim detection.
