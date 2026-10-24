# Config-only backups

Config-only backup is an opt-in way to preserve a host-declared set of configuration files without copying the server's complete data directory. It is not a replacement for a full-server backup and does not contain game binaries, worlds, saves, logs, or arbitrary files.

## Backup types

| Type | Archive | Scope |
|---|---|---|
| Full server | `.zip` | The host copies the complete per-server directory using the existing full-backup and restore route. This remains the default. |
| Configuration only | `.wgsmbackup` | The host validates and archives only the exact paths declared for the selected built-in game or recognized plugin. Restore merges only those paths. |

Config-only is not selected automatically as a fallback. If the selected game/plugin does not have a valid host-approved plan, use a full-server backup and do not assume that a config file shown by the game is covered.

## Current built-in scopes

The host has explicit config-only plans for 20 built-in identities. These declarations are source-level scope, not live qualification of every game's read/write behavior. The [built-in report](BUILT-IN-GAME-SHARED-INSTALL-CONFIG-REPORT.md) contains the exact title names and implementation notes.

| Built-in family | Declared paths | Notes |
|---|---|---|
| Source (10 titles) | Per-instance `<game>\cfg\server.cfg` and `<game>\mapcycle.txt` | Only those two paths are archived; other game state is excluded. |
| GoldSrc (7 titles) | Per-instance `<mod>\server.cfg` and map-cycle file | In shared mode, the map cycle is a unique `windowsgsm_goldsrc_mapcycle_<serverId>.txt` under the shared mod directory. The matching shared installation/build is part of archive identity. |
| Call of Duty 1 | `main\server.cfg`; when MeatBots is enabled, also `CoD1MeatBots_RC2\server.cfg` | The generated `wgs_cod1_meatbots.cfg` is omitted because the host regenerates it from server settings. |
| Minecraft Bedrock | Per-instance `server.properties`, `allowlist.json`, and `permissions.json` | WindowsGSM synchronizes the managed server name, player limit, level name, IPv4 port, and IPv6 port into `server.properties`; the IPv6 port is set to IPv4 + 1, so the IPv4 port must be 1–65534. Other properties are preserved. Worlds, logs, shared binaries, and behavior/resource-pack runtime directories are excluded. |
| Rust | Per-instance `serverfiles\server.cfg` | Rust shared installation is disabled; this config-only scope remains separate from shared binaries. |

Other built-in games do not currently have a host-owned config-only scope. Plugin V2 can declare a path list through `GetConfigOnlyBackupPlan(PluginInstanceContext)`, but the host validates the list and owns archive creation and restore. Plugin plans currently receive only the approved per-instance `serverfiles` root. No plugin has live Alpha qualification; method presence alone is not approval. Shared-install and config-only eligibility are separate decisions.

## What the archive contains and how restore is limited

A `.wgsmbackup` archive is a ZIP container with a versioned `manifest.json` and the declared files only. The manifest records the server/game identity, creation time, shared-install identity/build when applicable, and each included path, length, and SHA-256 hash. The host validates the plan and target roots; it does not ask a plugin to create or extract an archive.

During restore, WindowsGSM checks the archive type, manifest, target server/game identity, shared-install identity where applicable, file hashes, and the approved destinations. It stages the archive contents and merges only the validated paths. It does not replace the whole server directory or shared installation. Invalid or mismatched archives should be rejected before the selected configuration paths are changed. A current-file snapshot is created before restore when applicable files exist; the app reports when no snapshot was available.

The archive is **not encrypted**. Configuration files may contain passwords, tokens, IP addresses, or other private information. Store archives accordingly and redact logs/screenshots before sharing them. Do not send an archive to a maintainer unless its contents have been reviewed and confirmed safe to share.

## Create and restore safely

1. Stop the server. Backup and restore operations follow the stopped-server safety rule; WindowsGSM does not stop a running game server for you.
2. Make sure the candidate build offers **Configuration only** for that exact game/plugin. Review the included-path confirmation and keep the reported result with your test notes.
3. Store the archive somewhere safe. A config-only archive is small because it excludes binaries and other server data; it is not a complete recovery copy.
4. Test restore on a disposable instance of the matching game. Confirm the target identity and affected paths, then compare selected and non-selected files before and after restore.
5. For shared GoldSrc, use the matching shared installation/build. Do not restore that instance's config archive onto an unrelated shared installation.

Keep full-server backups for broader recovery. For assigned Alpha evidence, use BK-01 through BK-03 in the [tester checklist](ALPHA-TESTER-CHECKLIST.md); do not manufacture malicious archives or test on valuable server data.