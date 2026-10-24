# Shared game installations

**Alpha status:** Shared-install selection is enabled for a limited built-in allowlist, but selection does not mean a game has passed live Alpha qualification. Use disposable test servers and follow the [Alpha tester checklist](ALPHA-TESTER-CHECKLIST.md).

## What a shared installation means

A shared installation keeps one copy of a game's binaries for multiple server instances. The first server that creates the installation owns the binary directory under its `servers\<id>\serverfiles` folder. Each later linked server keeps its own `servers\<id>\serverfiles` directory for its instance-specific configuration and data. Ports, server identity, process state, logs, saves, and other writable data must remain isolated per instance.

The **Shared Installations** view lists registered installations, their game, state, build, and linked servers. It is an inventory view, not a file browser or a way to attach an arbitrary existing directory.

## Current eligibility and test status

The host currently exposes the shared-install option for 10 Source titles, 7 GoldSrc titles, Call of Duty 1, and Minecraft Bedrock. This is code-level eligibility only; it does not establish that every game safely supports concurrent instances, updates, or recovery.

| Provider | Current handling |
|---|---|
| Source titles (10) | Selection is enabled. Each exact title still needs live checks for config/map-cycle selection, writable paths, concurrent runs, updates, and recovery. |
| GoldSrc titles (7) | Selection is enabled. A tester has reported a Team Fortress Classic (TFC) pilot with checks complete except for the shared update check; that report has not been independently reproduced. Other titles are not qualified by the TFC result. |
| Call of Duty 1 | Selection is enabled, but stock and MeatBots runtime behavior still need separate live qualification. |
| Rust | Shared installation is disabled after a live start failure involving the `Bundles` asset root. Private Rust and config-only backup remain available. |
| Minecraft Bedrock | Selection and config-only backup are enabled. Shared world/data isolation, mutable runtime directories, and concurrent update behavior still need live verification. |
| Plugins | Plugins remain private-only in this candidate. V2 methods or capability diagnostics are not runtime approval. |
| Source SDK 2013 | Not enabled; its generic adapter has no concrete game/mod identity. |

No provider has passed every Alpha gate. The [built-in capability report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) has the exact provider list and remaining game-specific evidence. The outstanding TFC update/recovery test is SH-04 in the [tester checklist](ALPHA-TESTER-CHECKLIST.md).

## Create and use a shared installation

1. Use a disposable server and make a separate recovery copy. Do not convert or migrate an existing private installation; private-to-shared migration is not implemented.
2. In WindowsGSM, start a new server installation for a game that the host currently enables. The **Use shared installation** option is shown only when the host recognizes the provider as eligible.
3. Select the option and complete the installation through the app. The host reuses a compatible ready installation for the same game identity when one is registered; otherwise it creates a shared installation. It does not let you select an arbitrary folder.
4. Install another new server for the same compatible game and enable the option. The host links it to the ready shared installation and prepares that server's instance-specific files separately.
5. Give each instance a distinct name, port set, and configuration. Verify the game actually reads each instance's config and writes logs, saves, and generated data to the expected per-instance locations.
6. Use **Shared Installations** to review the registered state/build and linked-server list. If the expected installation is not listed or its state is not ready, stop and resolve that issue before starting servers.

Do not copy an entire private `serverfiles` tree into a shared installation or move config, saves, logs, or generated files into the common binary directory. A game-specific provider must explicitly identify which files can be shared and which remain per-instance.

## Updates, backups, and removal

- Stop every server linked to the shared installation before updating it. The app reports which instances are affected and blocks updates while the shared files are in use. The update lock serializes installation/update work; it is not a substitute for a recovery copy.
- Update/recovery behavior is not yet live-qualified for the full allowlist. Do not use valuable game files for SH-04; use the disposable TFC pilot and capture the result as described in the checklist.
- A full-server backup of the owner includes the shared binaries. A full backup of a linked instance does not duplicate the common binary tree. A full restore of the owner is blocked while other servers still reference the installation.
- Config-only backups are separate and include only an approved file list. Shared GoldSrc archives can include that instance's unique map-cycle file under the shared mod directory. Bedrock archives include only the instance's `server.properties`, `allowlist.json`, and `permissions.json`; they exclude worlds, packs, and shared binaries. See the [config-only backup guide](CONFIG-ONLY-BACKUP-GUIDE.md).
- Removing the last server linked to an installation can also remove the shared game files. Read the app's confirmation and preserve anything you need before removal; do not delete shared files manually.

## Recovery limits and reporting

Private-to-shared migration is not implemented. Keep existing private servers private; do not manually assign a shared-install ID or repoint an existing instance. If a shared install fails, stop the affected test and preserve its files for diagnosis rather than deleting or rebuilding the shared directory.

For the Alpha pilot, complete SH-01 through SH-05 only on assigned, disposable installations. Record the candidate commit, game/mod version, instance IDs, update impact, outcome, and recovery evidence. Mark unsupported or unclear workflows **Blocked** rather than improvising. See [How to report results](ALPHA-TESTER-CHECKLIST.md#how-to-report-results).