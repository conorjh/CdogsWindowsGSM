# Road to Alpha: Tester Checklist

**Status:** Controlled-pilot checklist. Passing a build or automated test suite alone does not qualify a feature for Alpha.

This checklist collects live Windows evidence for the [Alpha roadmap](MULTI-INSTANCE-ALPHA-ROADMAP.md). Test only the features and games included in the exact candidate build you were given; record anything that is unclear or unavailable as **Blocked**, not as a pass.

## Latest pilot report — 24 October 2026

This progress snapshot records the tester's report; the live results have not been independently reproduced here.

| Area | Reported result | Still open |
|---|---|---|
| Shared-install checks (SH series) | On the Team Fortress Classic (TFC) pilot, the tester reports the shared install looks good and the SH checks were completed except for update. | Do not count the shared-install update check as passed; update behavior remains unqualified. Selection is now enabled for the 10 Source titles, 7 GoldSrc titles, COD1, and Minecraft Bedrock; these additional providers have not been live-qualified. |
| BK-03 | Skipped. | Corrupt/truncated and mismatched-archive rejection has not been live-tested. |
| PL-01 and PL-02 | Not done. | Live plugin v1/v2 loading, capability diagnostics, and cache behavior remain unqualified. |
| AL-01 | Clean-machine test not done. | Fresh Windows app/updater launch and update remain unqualified. |
| Rust shared-start failure | The prior E: test log shows Rust looking for `Bundles\\Bundles` under the per-instance `serverfiles` directory instead of the shared install. | Rust shared-install selection and start/update operations are now gated in code; existing shared files are preserved. Private Rust and config-only backup remain available. Re-enable sharing only after a supported asset-root/per-instance-data model is verified. |

The app/test project build succeeded; all 41 test-project tests and all 8 `BuiltInGameCapabilitiesTests` passed. These checks validate host capability gates and path plans only; they do not substitute for any of the remaining live checks above.

## Highest-priority test

If you can test only one lane, finish SH-04 for the existing TFC pilot on a disposable shared installation. Do not use a valuable install for the update check.

The tester reports the other SH checks complete for TFC, but that report has not been independently reproduced and does not qualify other providers. Source games, CoD1, other GoldSrc titles, and Minecraft Bedrock are now selectable but still need game-specific live tests on disposable installations. Rust and plugins remain unavailable; do not force the option or manually attach a shared installation for them. Rust config-only backup/restore is a separate optional test when the maintainer confirms the build and disk space are suitable.

## Before testing

- Use a test machine or VM and test servers only. Do not use production servers or valuable data.
- Make a separate, verified copy of anything you may need to recover. Keep it outside the test app's server and backup folders.
- Record the candidate version/commit, Windows edition/build, game and mod versions, and available disk space.
- Use licensed game files and follow the game's distribution terms. Do not share proprietary game files.
- Keep servers on loopback or a private test network. Do not expose a test server publicly just to complete this checklist.
- Do **not** try to migrate an existing private installation into a shared installation. That migration flow is not implemented. Keep existing private installs private.
- Do not update a shared installation that contains a real or valuable server. Use a disposable installation for update and removal checks.
- Do not force-terminate the app or power off the machine during an install, update, backup, or restore. Use only a safe failure scenario supplied by the maintainer.
- Logs and configs can contain passwords, tokens, IP addresses, or other private information. Redact them before sharing; do not send a backup archive unless it has been confirmed safe to share.

## Test scope

| Priority | Candidate | What to test |
|---|---|---|
| P0 | Existing Team Fortress Classic (TFC) pilot | Complete the outstanding shared-install update check (SH-04) on a disposable install; capture impact, result, and recovery evidence. |
| P1 | Minecraft Bedrock shared install and config-only backup | On disposable instances, verify separate properties/allowlist/permissions and world data; test two instances, update exclusion while running, and that config-only backup/restore touches only the three declared files. |
| P1 | Plugin v1 and v2 examples | v1 compatibility; v2 capability reporting and host validation; cache behavior if diagnostics are available. Do not test plugin shared binaries. |
| P1 | Clean supported Windows VM | Self-contained app and updater launch/update without developer-machine dependencies. |
| Optional | Rust config-only backup/restore | Test only when assigned and there is sufficient disposable storage. |
| Not live-qualified | Source-derived games, CoD1, non-TFC GoldSrc titles, and Minecraft Bedrock | Shared-install selection is enabled, but these providers have not completed game-specific live qualification. TFC's reported update check remains open. Do not infer runtime approval from an engine family or an old server configuration. |
| Gated | Rust shared installs and all plugins | The shared-install option is unavailable for these providers in this candidate. Do not force it or infer runtime approval from method signatures. |
| Out of scope unless explicitly enabled | SDK2013 shared installs, private-to-shared migration | Do not attempt these workflows. |

## Shared-install pilot

Use the supported shared-install workflow in the candidate. If the workflow or expected result is unclear, stop and report that rather than improvising a migration or deleting data.

### SH-01 — Create two instances from one install

- [ ] Use a disposable Team Fortress Classic (TFC) shared installation; do not convert an existing private install.
- [ ] Create two server instances with distinct IDs, names, and non-conflicting ports, both referencing the same shared installation.
- [ ] Record the instance IDs and the shared-install path/identifier shown by the app.

**Expected:** The game binaries are shared, while each server retains its own identity, settings, and instance data. Creating the second instance does not silently create an unrelated private copy or overwrite the first instance's settings.

### SH-02 — Run concurrently and verify isolation

- [ ] Give each instance a visibly different server name and configuration. Use a different map cycle for each if the candidate exposes it.
- [ ] Start both servers and leave them running together. Connect a client to each if available; otherwise record the process, console, and query evidence the app provides.
- [ ] Change one setting or map cycle for instance A, then restart A while B remains running.
- [ ] Stop A and verify B continues to run; then repeat with B as appropriate.

**Expected:** Each server uses the correct name, configuration, map cycle, port, console/process entry, logs, and saves. A change, restart, or stop for A does not change or stop B. For GoldSrc, verify the per-instance config and unique map-cycle selection even though the mod binaries are shared.

### SH-03 — Reopen and reattach (if supported by the candidate)

- [ ] Use the documented app-close/reopen workflow without stopping the test servers, if the candidate supports reattachment.
- [ ] Verify each app entry reconnects to the correct server process and controls.

**Expected:** The app does not confuse the instances or claim a stopped/healthy state incorrectly. If the candidate has no supported reattachment workflow, mark this check **Blocked** and explain what happened.

### SH-04 — Update and recovery safety

- [ ] With both test servers stopped and a recovery copy made, inspect the update flow. Record which instances the app says are affected.
- [ ] Apply one update only on the disposable shared installation. Confirm the operation is serialized and the resulting install is usable by both instances.
- [ ] On a disposable setup, check what happens if an instance is running when an update is requested. Do not proceed with an update that would silently replace binaries in use; record whether it is blocked or gives a clear, safe supported path.
- [ ] If the maintainer supplied a safe failure scenario, use it to verify the prior installation remains usable and the error is visible. Do not manufacture failure by killing the process or removing files.

**Expected:** Update impact is visible, concurrent install/update work is not allowed to corrupt the shared install, and a failed operation does not report success or leave instances with unusable/partially replaced binaries.

### SH-05 — Remove one instance

- [ ] Remove instance A using the candidate's normal workflow, only after preserving any test data you want to keep.
- [ ] Verify instance B still references the shared installation and can start.

**Expected:** Removing one instance does not silently remove the shared binaries or B's configuration/data. If the candidate does not offer a safe removal workflow, mark the check **Blocked** rather than guessing which files are safe to delete.

## Backup and restore

Run restore checks only on disposable instances. Compare the selected files and at least one non-selected file before and after restore; hashes or copies are useful evidence.

### BK-01 — Existing full-server backup

- [ ] With a server stopped, create the normal full-server backup and record its type, result, and reported contents.
- [ ] Restore it to a disposable test target using the existing full-server restore route.
- [ ] Confirm the legacy full backup remains available and is not treated as a config-only archive.

**Expected:** Full-server backup remains host-managed and follows its existing route. A test restore recovers the expected per-server data without overwriting a valuable server. The separate shared binary installation is not treated as part of a per-instance config-only archive.

### BK-02 — Config-only backup and restore

- [ ] Select **Config-only** explicitly, if offered for the assigned game. Record the exact included paths and archive type (`.wgsmbackup`).
- [ ] For GoldSrc, check the instance's mod `server.cfg` and its unique map-cycle file. For Rust, if assigned, check only the declared `serverfiles/server.cfg` scope.
- [ ] Create the backup while the server is stopped. Confirm the archive contains no executable/binary tree and does not include another instance's files.
- [ ] Change a test setting in instance A, restore A's config-only backup, and verify only the declared paths are restored. Check that B, shared binaries, saves, and logs remain unchanged.
- [ ] Confirm the app creates/reports the pre-restore snapshot when the current config exists.

**Expected:** The archive and restore affect only the approved paths for the selected instance. A config-only restore never deletes or replaces the whole instance directory or shared install. The app reports included/restored paths and does not claim success if the operation fails.

### BK-03 — Invalid archive and error handling

- [ ] If supplied by the maintainer, try a truncated/corrupt archive and an archive for a different instance/game on a disposable target.
- [ ] Compare the target before and after each rejected restore; confirm no files changed.
- [ ] Record any permission, disk-space, or write error that occurs during an assigned test.

**Expected:** Invalid or mismatched archives are rejected before changing files, with a visible actionable error. Use only maintainer-provided invalid test archives; do not create or share archives containing malicious payloads.

### BK-04 — Scheduled backup (if included in the candidate)

- [ ] Verify schedules default to full-server backup unless config-only is explicitly selected.
- [ ] Use the run-now option, if available, and compare its scope and result reporting with the equivalent manual backup.
- [ ] Confirm the app must be running for a scheduled job, and that the stopped-server safety rule is respected.

## Plugin compatibility

Use the sample plugins supplied with the candidate or by the maintainer; you do not need to write a plugin.

### PL-01 — Existing v1 plugin

- [ ] Load/compile the supplied v1 sample without adding v2 methods.
- [ ] Exercise the plugin's normal supported lifecycle and record compiler/load diagnostics.
- [ ] Verify it is not treated as shared-install or config-only capable merely because it loaded.

**Expected:** The v1 sample remains compatible through the documented path. A v1 plugin without the optional v2 capability set remains private-only and uses the host's full-server backup behavior.

### PL-02 — v2 capability and validation

- [ ] Load the supplied v2 sample and record the capabilities the host reports.
- [ ] If a config-only plan is exposed, confirm only approved per-instance paths are included and the host creates/restores the archive.
- [ ] If the maintainer supplies an incomplete or invalid capability sample, confirm the host reports a clear diagnostic and does not silently enable the capability.
- [ ] If compiler diagnostics expose caching, compile unchanged source twice, then change the source and confirm the changed input is not served as a stale result.

**Expected:** Capability reporting reflects the documented method set, but does not by itself qualify a plugin for live sharing. Plugins do not own archive creation or restore. Do not test shared binaries with an unqualified third-party plugin.

## Clean-machine and everyday regression checks

### AL-01 — Published app and updater on a clean machine

- [ ] Use a fresh supported Windows VM with no developer tools or manually installed .NET dependency. Do not remove Windows' built-in components.
- [ ] Install and launch the exact self-contained app/updater build supplied by the maintainer. Record any first-run prompts, missing-file errors, or elevation requirements.
- [ ] Close/reopen the app, then use the documented update flow if an update candidate is supplied. Confirm the app and updater start and the test data/settings remain available.
- [ ] If Windows Firewall integration is in scope, use only a disposable machine; record the Windows network profile, permission prompt, and whether the rule can be removed. Do not expose a public port.

**Expected:** The published app and updater run without relying on developer-machine files or a separately installed .NET Framework runtime. The update path does not silently lose user data.

### AL-02 — Core workflow and accessibility smoke test

- [ ] If the candidate includes a normal private server workflow, install or import a disposable server; start, stop, restart, use the console, and check logs.
- [ ] Open the Server Files and Server Configs browse actions for a private and a shared instance. Confirm the app stays open and Server Files opens the selected instance's own `servers\<id>\serverfiles` folder; shared binaries are kept in the separate installation.
- [ ] Verify health/query states distinguish a running process from a supported query result; an unsupported or never-verified query should not be presented as a confirmed failure.
- [ ] Use keyboard-only navigation through the main server and backup flows. Check focus order, visible focus, labels, and whether dialogs can be completed without a mouse.
- [ ] If available, use Narrator or NVDA for the same flows. Check high-contrast mode and a non-default display scale; report clipped text, hidden controls, or missing announcements.
- [ ] Change the UI language if translations are included, restart the app, and confirm the selection persists and missing text remains understandable.

## How to report results

For every assigned test, report **Pass**, **Fail**, **Blocked**, or **Not run**. A failure that risks data loss or one instance changing another is a stop signal: stop further destructive tests, preserve the test copy, and notify the maintainer promptly.

Include this information with each issue:

```text
Test ID and result:
Candidate version/commit:
Windows edition and build:
Game/mod and version:
Plugin and version (if applicable):
Starting state and relevant instance IDs:
Steps taken:
Expected result:
Actual result:
Data affected or possible data loss:
Evidence attached (redacted logs/screenshots; no backup archive unless approved):
```

For a **Blocked** result, say what prevented the test (missing option, unsupported game, missing hardware/client, unclear workflow, or environment issue). For a **Fail**, include the smallest repeatable steps and whether it happened once or every time. Report confusing or missing UI as feedback too; it is useful even when no data was damaged.