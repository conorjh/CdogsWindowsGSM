# Candidate game server feasibility and V1/V2 plan

**Status:** Research and conditional implementation plan complete; adapters remain gated by the technical spikes below.

**Reviewed:** 31 October 2026.

## Recommendation

Do not add either title to WindowsGSM's built-in dedicated-server catalog yet. Run a short, licensed-install feasibility spike for each game. Age of Empires II: DE is the stronger candidate because community tooling can launch a separate `BattleServer` executable from a Steam/Xbox game install; verify what that process actually hosts before promising headless matches. A process that merely opens the normal game client is not a dedicated server and must not be reported as one.

If the goal is to automate launching an interactive host client, that could be explored separately as an explicitly interactive launcher feature. It would require a logged-in desktop and manual in-game lobby setup, and would not provide the normal unattended server lifecycle. It should not be disguised as a V1 dedicated-server adapter.

## What V1 and V2 mean in this repository

- **V1:** Private-install server operations from the game integration matrix: install/update or import, install/import validation, configuration where supported, build lookup where available, Start/Stop, and backup/restore. Query and map-cycle support are game-specific, not prerequisites to invent when the game has no protocol for them.
- **V2:** Shared binary installation with isolated per-instance configuration, plus only the backup/capability work supported by the actual game file layout. The matrix separately tracks built-in configuration-backup operations; the plugin API calls its optional typed backup/shared-install capabilities V2.
- A V1 or V2 adapter must manage a real server process. A lobby service, matchmaking helper, or command-line launcher for a player's game client does not meet that requirement by itself.

## Feasibility summary

| Game | Dedicated/headless evidence | Proposed decision |
|---|---|---|
| Command & Conquer: Generals (consider Zero Hour as a separate variant) | The public community guide describes installing and launching the game, then signing into C&C Online; it does not document a dedicated server executable or a headless host interface. GameRanger/C&C Online should be treated as lobby or launch tooling unless a server binary and its supported operation are demonstrated. | **No-go as a built-in dedicated server for now.** Revisit only if a supported server binary or documented headless mode is found. |
| Age of Empires II: Definitive Edition | Microsoft's match-creation instructions use the in-game Host Game flow. A Microsoft support article mentions “Host as Dedicated Server,” but a Steam discussion about that wording records that the reference was for Age of Empires: Definitive Edition, not AoE II: DE. Separately, the community Age Server project has a Battle-Server-Manager that launches a `BattleServer` executable and can locate one in Steam/Xbox installs. Its launcher also starts the game client, so this is evidence for a headless battle-server component, not proof that the game match can be created or run without a client. | **Conditional GO for investigating the BattleServer component; no-go for claiming fully headless matches until tested.** |

## Per-game plan

### Command & Conquer: Generals

**V1 feasibility gate**

1. Treat original Generals and Zero Hour as separate variants unless their executable, configuration, and multiplayer behavior are proven identical.
2. Identify an officially supported server executable or documented headless launch mode for the exact supported game edition. Confirm whether it is distributed separately and whether its use is licensed for server hosting.
3. On a clean, licensed install, verify unattended startup, an observable ready state, a client joining a hosted match, and a reliable stop that leaves no child processes behind. Record the required ports and whether any online account or community service is mandatory.
4. If the only successful path launches the normal game UI, stop the dedicated-server plan. A command-line option that launches or joins the client is evidence of client automation, not headless server support.

**If the gate passes:** implement a private-install adapter using the existing V1 lifecycle and validation patterns. Use user-provided/imported game files unless the publisher provides a supported redistributable installer/update mechanism; do not promise SteamCMD installation for an EA App/retail title. Generate only documented server configuration, expose only verified health checks, and use the normal full-server backup path until a safe configuration-file list is established.

**If the gate fails:** do not add a built-in game-server entry. A separate experimental interactive launcher could be considered later, clearly labeled as requiring a desktop session and manual lobby setup. Do not automate lobby controls with brittle window-clicking or claim query/readiness based only on the game's process existing.

**V2:** deferred. Shared installs and per-instance backup are not useful until V1 proves that a server executable and stable per-instance configuration/data roots exist. If V1 later qualifies, prove separate instance configs, concurrent-start behavior, update locking, and recovery before enabling sharing.

### Age of Empires II: Definitive Edition

**V1 feasibility gate**

1. Use a clean, legitimately owned Steam or Microsoft Store install. Confirm whether the game supplies `BattleServer.exe` in that install, record its version and provenance, and distinguish it from any community reimplementation. Do not redistribute game binaries.
2. Run the separate BattleServer by itself and verify its readiness, ports, logs, and clean stop. The Age Server Battle-Server-Manager is useful evidence that a standalone BattleServer process can be started, but its CLI arguments are not automatically a publisher-supported contract.
3. Determine whether that process only provides the lobby/network relay or can host a complete match without a game client. Use a licensed client on a separate machine to create/join a lobby, and verify what happens if the host client exits. A GameRanger-style launcher may automate client startup, but does not establish that the game simulation is headless.
4. Verify the current in-game “Host as Dedicated Server” behavior directly. The support article alone is insufficient: its wording appears to have been confused with Age of Empires: Definitive Edition, and the AoE II: DE match-creation guide describes hosting through the game UI.
5. Confirm lifecycle behavior: reliable readiness, stop, process-tree cleanup, required networking, and behavior after account/session loss. Do not rely on UI automation, binary patching, or undocumented client behavior as a production server contract.

**If the BattleServer gate passes:** implement a V1 adapter for the `BattleServer` service, clearly named as such if it is only a lobby/network component. Support import/validation from a licensed install, Start/Stop, documented configuration, port checks, and only verified health signals. Update only through a supported distribution path. A separate game client may still be needed to create or host a lobby; make that limitation explicit. Live integration tests must be opt-in and must not require user credentials in CI.

**If the BattleServer gate fails:** do not add a dedicated-server adapter. An interactive launcher is technically distinct and would need explicit limitations: visible game client, signed-in user session, manual lobby creation, and no claim of unattended match hosting. The community Age Server project may still be useful as a reference for a separate local lobby/API service, but it is not proof that the official game can run its match simulation without a client.

**V2:** potentially useful only after the BattleServer V1 adapter is qualified. A shared immutable server binary with per-instance ports, region/name, and certificates may be viable; prove two simultaneous instances, port isolation, update locking, and recovery first. Inventory configuration and certificate paths before offering configuration-only backup, and never back up Steam/Xbox credentials or profiles as server configuration. Do not infer multi-instance safety from being able to launch multiple game clients.

## Conditional V1/V2 implementation backlog

This is the proposed work order if a feasibility gate passes. It deliberately does not add either game to the built-in catalog before the game-specific process is identified and tested.

| Phase | Command & Conquer: Generals / Zero Hour | Age of Empires II: Definitive Edition | Exit evidence |
|---|---|---|---|
| Gate 0: identify the server | Search for a publisher-supported, headless server executable or documented server mode for each edition. Treat Generals and Zero Hour as separate products. If only the normal GUI game is available, stop the dedicated-server work. | Identify the installed `BattleServer` executable and establish whether it is the official binary or a third-party reimplementation. Verify whether it hosts the match simulation or only the lobby/network service; do not infer this from its process name or the word “server.” | Written record of binary provenance, license/distribution route, exact start parameters, client requirement, and what service it actually provides. |
| V1: private install/import | If Gate 0 passes, start with import/validation from a user-owned supported install. Use publisher-supported update delivery only; otherwise document updates as managed by the upstream launcher and do not fabricate an updater. | If the BattleServer gate passes, import/validate it from a user-owned Steam/Xbox install. Do not download or redistribute the game executable. Separate the battle service from a client install if upstream provides that layout. | Install/import validation passes on a clean licensed fixture; missing/unsupported files fail clearly. |
| V1: configuration and lifecycle | Implement only documented server settings. Start/Stop must manage the headless server process and its descendants; do not automate the game UI. | Implement only verified BattleServer arguments (candidate settings include region/name/ports and certificate paths, subject to binary verification). Start/Stop the BattleServer process; report separately if a player must launch a game client to create a lobby or match. | Independent Start and Stop checks pass, and a real licensed client can connect. Process-only survival is insufficient. |
| V1: readiness and diagnostics | Use a documented server log, health endpoint, or protocol probe if one exists. If none exists, report process state only and do not call it gameplay-ready. | Probe only the health that the BattleServer actually exposes. A healthy lobby/relay process must not be presented as a running game match. | Tests distinguish process state, service readiness, and successful client join. Unsupported query/map-cycle operations remain unsupported. |
| V1: backup and qualification | Keep host-managed full-server backup/restore. Add config-only paths only after confirming the game writes them and they contain no credentials. | Keep host-managed full-server backup/restore initially. If config-only backup is later added, exclude certificates/keys unless explicitly required and protect them as secrets; never include account/profile state. | Operation-specific tests follow the existing game-integration matrix; live licensed tests are opt-in and verify an actual client join. |
| V2: shared installation | Not planned unless V1 establishes a genuine headless server and stable per-instance write roots. Then test separate settings, concurrent instances, locking, rollback, and recovery before enabling sharing. | Potentially share immutable BattleServer binaries, with per-instance configuration, ports, region/name, certificates, logs, and state. Prove concurrent instances and update isolation; use a separate config-only backup plan only for confirmed per-instance files. | Shared owner/client isolation, simultaneous start, update lock, rollback/recovery, backup/restore, and client-join tests all pass. |

### V1 implementation checklist if a gate passes

1. Add a dedicated built-in adapter and factory registration; do not reuse an unrelated game adapter merely because its process-launch pattern looks similar.
2. Provide an honest installation story: supported upstream install, user import, or a clear unsupported-install error. Never have WindowsGSM download proprietary game files from an unofficial mirror.
3. Implement `CreateServerCFG`, `Start`, `Stop`, `Install`/`Update`, install/import validation, and local/remote build lookup only where each operation has a real, testable meaning. If the required V1 contract cannot be met without falsely reporting success, keep the game out of the built-in list.
4. Add offline adapter tests for path validation, argument/config generation, error reporting, and process cleanup, plus an opt-in licensed integration test for actual client connectivity. Keep credentials and account tokens out of code, logs, fixtures, and CI.
5. Add the game to the existing V1 operation matrix only after the adapter is wired. Record unsupported maps/query/health features explicitly; a smoke test that merely starts a GUI process does not qualify the game.

### V2 implementation checklist if V1 qualifies

1. Inventory all files written by the server while running and identify which are shared binaries versus per-instance configuration, certificates, logs, and mutable match state.
2. Implement shared installation only for immutable/read-only binaries; give each instance isolated config/state and non-conflicting ports. Serialize update/rollback operations against all linked instances.
3. Add a configuration-only backup plan only for explicit per-instance files. Keep credentials, Steam/Xbox profiles, keys, and binaries outside that plan unless a separately reviewed requirement proves they are necessary.
4. Test owner/client isolation, simultaneous starts, independent configuration, update lock/recovery, restore safety, and an actual client join for both shared instances.
5. Do not count ordinary V1 process lifecycle tests or plugin V2 capability tests as built-in V2 shared-install qualification; update the built-in matrix with separate evidence.

## Implementation and qualification sequence

1. **Technical spike only:** record the executable, provenance, supported start parameters, required account/session, visible-UI dependency, readiness signal, ports, client-join result, update path, and stop behavior for each edition. For AoE II: DE, separately record whether `BattleServer.exe` hosts a match or only provides network/lobby services. No server-catalog entry is added during this phase.
2. **Implement only a passing V1:** add the adapter and factory registration (`GameServer/Data/Class.cs`), game selection/resources, documented config behavior, and operation-specific tests. Update the existing game integration matrix rather than treating a process-launch smoke test as qualification.
3. **Qualify V1:** run isolated local tests plus opt-in live tests using a licensed install. Require Start and Stop to pass independently and verify an actual client can connect. Keep unsupported query/map features explicitly unsupported.
4. **Consider V2 separately:** only after V1 qualifies, define per-instance paths and add shared-install/configuration-backup capabilities with owner/client isolation, concurrent-start, update-locking, rollback, restore, and recovery tests.
5. **Keep the boundary clear:** if only a GUI client can host, propose an interactive-launcher product surface separately; do not count it as a WindowsGSM dedicated game server.

## Sources checked

- [Age of Empires II: DE — creating a multiplayer match](https://support.ageofempires.com/hc/en-us/articles/360047306372-How-do-I-create-a-multiplayer-match-in-Age-of-Empires-II-Definitive-Edition)
- [Age of Empires II: DE — optimization tips](https://support.ageofempires.com/hc/en-us/articles/360049024131-Tips-for-Optimizing-Age-of-Empires-II-Definitive-Edition) (contains the potentially misleading “Host as Dedicated Server” wording)
- [Steam discussion: “Host as Dedicated Server” ? Where?](https://steamcommunity.com/app/813780/discussions/0/1741134331873970969/) (reply points out the referenced option is for Age of Empires: Definitive Edition)
- [Age Server README](https://github.com/luskaner/ageServer/blob/main/README.md), [Battle-Server-Manager](https://github.com/luskaner/ageServer/blob/main/battle-server-manager/README.md), and [BattleServer configuration](https://github.com/luskaner/ageServer/blob/main/battle-server-manager/resources/config.game.toml). The project can launch a separate BattleServer executable; its launcher also starts the game client.
- [C&C Generals community multiplayer guide](https://cnc.community/generals/how-to-play)
- Repository baseline: [`GAME-INTEGRATION-ALPHA-MATRIX.md`](GAME-INTEGRATION-ALPHA-MATRIX.md) and [`BuiltInGameCapabilities.cs`](../CdogsWindowsGSM/Functions/BuiltInGameCapabilities.cs)
