# CdogsWindowsGSM

[![License](https://img.shields.io/github/license/conorjh/CdogsWindowsGSM)](LICENSE)
[![Latest release](https://img.shields.io/github/release/conorjh/CdogsWindowsGSM)](https://github.com/conorjh/CdogsWindowsGSM/releases)
[![Downloads](https://img.shields.io/github/downloads/conorjh/CdogsWindowsGSM/total)](https://github.com/conorjh/CdogsWindowsGSM/releases)

A community-maintained Windows desktop application for installing, importing, and managing dedicated game servers. This codebase is based on the original [WindowsGSM project](https://github.com/WindowsGSM/WindowsGSM). A clanker did all the heavy lifting.

## Fork-specific features

- **Added built-in games:** Call of Duty 1, CoD4x, OpenRCT2, OpenTTD, Mindustry, OpenRA, RedM, SCUM, and Soldat, alongside the Minecraft server variants.
- **Shared installs and backups:** share a single game installation across compatible servers. Full-server backups cover each server's directory; config-only backups include only host-approved configuration files where supported (see the table below). Read the [shared-installation guide](docs/SHARED-INSTALLATION-GUIDE.md), [config-only backup guide](docs/CONFIG-ONLY-BACKUP-GUIDE.md), and [scheduled backup guide](docs/SCHEDULED-BACKUPS-GUIDE.md) for current behavior and limits.
- **FiveM and RedM fixes:** share the current CFX server artifacts and query support; both require a valid CFX license key.
- **OpenRCT2 servers:** download a verified sample park when none is present and start without custom server parameters; a licensed RollerCoaster Tycoon 2 installation is still required.
- **CoD4x:** downloads the upstream [CoD4x 21.2](https://github.com/callofduty4x/CoD4x_Server/releases/tag/21.2) server binary (AGPL-3.0); a licensed Call of Duty 4 game installation is required and proprietary game data is not redistributed. No third-party bot package is included.
- **Map rotation:** edit Source and GoldSrc map rotations; double-click maps to add or remove them.

### General improvements

- **GoldSrc IP binding:** passes the configured Server IP using `+ip` so HLDS uses the selected local address on multi-NIC hosts. Avoid conflicting `+ip` entries in Server Parameters.
- **Server-list actions:** update all stopped servers sequentially, alongside the existing start/stop/restart all actions; hover over a game icon to see the full game name.
- **In-app updates:** checks for releases from this fork.
- **Fleet management:** scheduled backups, server health, server groups, and a live system dashboard.
- **Expanded language support:** use any of the interface languages listed in [Languages](#languages).

See [Changes from WindowsGSM](ChangesFromBase.md) for a detailed comparison with the upstream project.

|               | 🎮 Game Server  | Supported | Query | Shared install | Config-only backup |
| ------------- | --------------- | --------- | ----- | -------------- | ------------------ |
| ![logo](CdogsWindowsGSM/Images/Games/7dtd.png?raw=true)   | 7 Days to Die Dedicated Server                    |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/arkse.png?raw=true)  | ARK: Survival Evolved Dedicated Server            |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/avorion.png?raw=true)| Avorion Dedicated Server                          |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/bt.png?raw=true)     | Barotrauma Dedicated Server                       |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/bw.png?raw=true)     | BlackWake Dedicated Server                        |✅|      | — | — |
| <img src="CdogsWindowsGSM/Images/Games/cod1.png?raw=true" width="16" height="16" alt="Call of Duty 1 logo"/>   | Call of Duty 1 Dedicated Server                   |✅|      | ✅ | ✅ |
| <img src="CdogsWindowsGSM/Images/Games/cod4.png?raw=true" width="16" height="16" alt="Call of Duty 4 logo"/>   | Call of Duty 4: Modern Warfare Dedicated Server (CoD4x) |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/ce.png?raw=true)     | Conan Exiles Dedicated Server                     |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/cs.png?raw=true)     | Counter-Strike: 1.6 Dedicated Server              |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/cscz.png?raw=true)   | Counter-Strike: Condition Zero Dedicated Server   |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/csgo.png?raw=true)   | Counter-Strike: Global Offensive Dedicated Server |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/css.png?raw=true)    | Counter-Strike: Source Dedicated Server           |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/dod.png?raw=true)    | Day of Defeat Dedicated Server                    |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/dods.png?raw=true)   | Day of Defeat: Source Dedicated Server            |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/dayz.png?raw=true)   | DayZ Dedicated Server                             |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/dmc.png?raw=true)    | Deathmatch Classic Dedicated Server               |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/eco.png?raw=true)    | Eco Dedicated Server                              |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/egs.png?raw=true)    | Empyrion - Galactic Survival Dedicated Server     |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/gmod.png?raw=true)   | Garry's Mod Dedicated Server                      |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/gta5.png?raw=true)   | Grand Theft Auto V Dedicated Server ([FiveM](https://github.com/citizenfx/fivem))|✅| FIVEM | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/hl2dm.png?raw=true)  | Half-Life 2: Deathmatch Dedicated Server          |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/hlof.png?raw=true)   | Half-Life: Opposing Force Dedicated Server        |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/heat.png?raw=true)   | Heat Dedicated Server                             |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/ins.png?raw=true)    | Insurgency Dedicated Server                       |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/inss.png?raw=true)   | Insurgency: Sandstorm Dedicated Server            |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/l4d2.png?raw=true)   | Left 4 Dead 2 Dedicated Server                    |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/mcbe.png?raw=true)   | Minecraft: Bedrock Edition Server                 |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Forge Server                           |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Java Edition Server                    |✅| UT3  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Paper Server                           |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/mcpe.png?raw=true)   | Minecraft: Pocket Edition Server ([PocketMine-MP](https://github.com/pmmp/PocketMine-MP)) |✅| UT3  | — | — |
| <img src="CdogsWindowsGSM/Images/Games/mindustry.png?raw=true" width="16" height="16" alt="Mindustry logo"/> | Mindustry Dedicated Server                     |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/mordhau.png?raw=true)| Mordhau Dedicated Server                          |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/nmrih.png?raw=true)  | No More Room in Hell Dedicated Server             |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/onset.png?raw=true)  | Onset Dedicated Server                            |✅|      | — | — |
| <img src="CdogsWindowsGSM/Images/Games/openra.png?raw=true" width="16" height="16" alt="OpenRA logo"/> | OpenRA Dedicated Server                           |✅|      | — | — |
| <img src="CdogsWindowsGSM/Images/Games/openttd.png?raw=true" width="16" height="16" alt="OpenTTD logo"/> | OpenTTD Dedicated Server                         |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/olow.png?raw=true)   | Outlaws of the Old West Dedicated Server          |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/ps.png?raw=true)     | Post Scriptum Dedicated Server                    |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/rcc.png?raw=true)    | Ricochet Dedicated Server                         |✅| A2S  | ✅ | ✅ |
| <img src="CdogsWindowsGSM/Images/Games/rct2.png?raw=true" width="16" height="16" alt="RollerCoaster Tycoon 2 logo"/>  | RCT2 Dedicated Server                             |✅|      | — | — |
| <img src="CdogsWindowsGSM/Images/Games/redm.png?raw=true" width="16" height="16" alt="RedM logo"/>  | Red Dead Redemption 2 Server                      |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/rok.png?raw=true)   | Reign Of Kings Dedicated Server                   |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/ror2.png?raw=true)  | Risk of Rain 2 Dedicated Server                   |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/rust.png?raw=true)   | Rust Dedicated Server                             |✅| A2S  | — | ✅ |
| <img src="CdogsWindowsGSM/Images/Games/scum.png?raw=true" width="16" height="16" alt="SCUM logo"/>  | SCUM dedicated server                             |✅| A2S  | — | — |
| <img src="CdogsWindowsGSM/Images/Games/soldat.png?raw=true" width="16" height="16" alt="Soldat logo"/> | Soldat Dedicated Server                           |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/se.png?raw=true)     | Space Engineers Dedicated Server                  |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/sq.png?raw=true)     | Squad Dedicated Server                            |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/sw.png?raw=true)     | Stormworks Dedicated Server                       |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/tf2.png?raw=true)    | Team Fortress 2 Dedicated Server                  |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/tfc.png?raw=true)    | Team Fortress Classic Dedicated Server            |✅| A2S  | ✅ | ✅ |
| ![logo](CdogsWindowsGSM/Images/Games/tf.png?raw=true)     | The Forest Dedicated Server                       |✅|      | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/unt.png?raw=true)    | Unturned Dedicated Server                         |✅| A2S  | — | — |
| ![logo](CdogsWindowsGSM/Images/Games/zps.png?raw=true)    | Zombie Panic Source Dedicated Server              |✅| A2S  | — | — |

|               | 🎮 Game Server  | Plugin supported | Query | Shared install | Config-only backup |
| ------------- | --------------- | ---------------- | ----- | -------------- | ------------------ |
| <img src="https://github.com/DevVault/WindowsGSM.ARMA2/blob/master/ARMA2.cs/ARMA2.png?raw=true" width="16" height="16"/>   | Arma2   |✅| A2S     | — | — |
| <img src="https://github.com/DevVault/WindowsGSM.ARMA2/blob/master/ARMA2OA.cs/ARMA2OA.png?raw=true" width="16" height="16"/>   | Arma2: OA   |✅| A2S     | — | — |
| <img src="https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/arma3.png?raw=true" width="16" height="16" alt="Arma 3 logo"/>   | Arma3   |✅|      | — | — |
| <img src="https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/astroneer.png?raw=true" width="16" height="16" alt="Astroneer logo"/>   | ASTRONEER   |✅|      | — | — |
| <img src="https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/deadmatter.png?raw=true" width="16" height="16" alt="Dead Matter logo"/>   | Dead Matter   |✅| UT3     | — | — |
| <img src="https://github.com/DevVault/WindowsGSM.HLDM/blob/master/HLDM.cs/HLDM.png?raw=true" width="16" height="16"/>   | Half-Life: Deathmatch   |✅| A2S     | — | — |
| <img src="https://github.com/DevVault/WindowsGSM.L4D/blob/master/L4D.cs/L4D.png?raw=true" width="16" height="16"/>   | Left 4 Dead   |✅| A2S     | — | — |
| <img src="https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/waterfall.png?raw=true" width="16" height="16" alt="Waterfall logo"/>   | PaperMC: Waterfall   |✅| A2S     | — | — |
| <img src="https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/spigot.png?raw=true" width="16" height="16" alt="Spigot logo"/>   | Spigot   |✅| A2S     | — | — |

  `✅` marks a capability enabled by the current host implementation; it does not mean the game has passed live Alpha qualification. Shared-install selection is enabled for the 10 Source games, 7 GoldSrc games, and Call of Duty 1 that have config-only backup plans; Rust remains disabled after a shared-start bundle-root failure. TFC is the only one with a tester-reported shared-install run, and its update test remains outstanding. New shared binaries are stored in the first server's `serverfiles`; later linked servers retain their own instance `serverfiles`. A full backup of the owner includes the shared binaries, while linked-server backups do not duplicate them; a full owner restore is blocked while other servers still reference those files. Config-only backup support and shared-install runtime qualification are separate. Full-server backups remain host-managed copies of the entire per-server directory, outside the plugin interface. For config-only backups, plugins may identify the files, but the host owns archive creation and restore. The current plugin API requires a plugin shared-install provider to also expose `GetConfigOnlyBackupPlan`; `InstallSharedBinaries` returns the same `Task<Process>` used by regular install operations, while `InstallConfigOnly` reports its terminal result. Built-in game gates are separate. GoldSrc config-only backups include the instance's mod `server.cfg` and mapcycle file; in shared mode only the server-specific mapcycle file is read from the shared root, never the shared binaries. Restoring shared GoldSrc config requires the matching shared installation/build recorded in the archive. For overall readiness and remaining tests, see the [Roadmap to Alpha](docs/MULTI-INSTANCE-ALPHA-ROADMAP.md) and [Alpha tester checklist](docs/ALPHA-TESTER-CHECKLIST.md).

## Features
  * **Easy Install** - No installation knowledge is required, download any game server by one-click install.
  * **Manage multiple servers** - Manage multiple servers with the same software. No more messy server files!
	* **Backups** - Full-server backups copy the complete per-server directory; opt-in config-only backups include only host-approved configuration files.
  * **Easy Action** - No command knowledge is required. Start/Stop/Restart your server with just a click.
  * **Discord Alert** - Get alerted when your servers encounters any special issues
  * **Auto Restart** - A basic function to auto restart your server if it crashes. Therefore, your server will always be up!
  * **Auto Update** - Checks for updates every 30 minutes, making sure your game servers are up to date!
  * **Restart Crontab** - Schedule your game servers to restart at any time, very useful for servers that require restarts to run smoothly.
  * **Start on Login** - Automatically start the application and selected servers when you sign in.
  * **Runtime Port Verification** - Detects the running server's process-owned client/main port where supported. Source and GoldSrc A2S health checks use the game/server UDP port; other supported query checks use their configured endpoint.
  * **CPU Priority and Affinity** - Set and save the cpu priority and affinity!
  * **Mod Manager** - Manage custom server mods from the application.
  * **Remote Control** - Remote control your game servers with Discord!
  * **Plugins** - Allow support more game servers by installing plugins

### Languages

WindowsGSM now offers a multilingual interface in English, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Romanian, Russian, Ukrainian, Simplified Chinese, Japanese, Korean, and Turkish. Choose a language or **Use Windows language** in **Settings > Language**; restart WindowsGSM to apply the change.

### Plugins
Allow support more game servers by installing plugins!

New plugin authors can start with the [from-scratch tutorial without an engine base class](CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-FROM-SCRATCH-TUTORIAL.md). To add V2 capabilities to an existing plugin, see the [V1-to-V2 guide](CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-V1-TO-V2-GUIDE.md). Exact capability signatures and host responsibilities are in the [V2 API plan](CdogsWindowsGSM-Plugin-Development/docs/PLUGIN-API-V2-PLAN.md). Companion notes explain the [V1 skeleton](CdogsWindowsGSM-Plugin-Development/docs/examples/Skeleton.md), [V2 skeleton](CdogsWindowsGSM-Plugin-Development/docs/examples/SkeletonV2.md), [SteamCMD-agent skeleton](CdogsWindowsGSM-Plugin-Development/docs/examples/Skeleton-SteamCMDAgent.md), [Garry's Mod V2 sample](CdogsWindowsGSM-Plugin-Development/docs/examples/GarrysModV2.md), and [standalone CS 1.6 sample](CdogsWindowsGSM-Plugin-Development/docs/examples/CS16StandaloneV2.md). The initial plugin-facing contract is finalized for config-only backups and shared installation; game-specific runtime qualification is still in progress.

##### Some available plugins (Made by [BattlefieldDuck](https://github.com/BattlefieldDuck) and [GTVolk](https://github.com/GTVolk)):
1. [WindowsGSM.ARMA2](https://github.com/DevVault/WindowsGSM.ARMA2)
2. [WindowsGSM.ARMA2OA](https://github.com/DevVault/WindowsGSM.ARMA2)
3. [WindowsGSM.ARMA3](https://github.com/BattlefieldDuck/WindowsGSM.ARMA3)
3. [WindowsGSM.HLDM](https://github.com/DevVault/WindowsGSM.HLDM)
3. [WindowsGSM.L4D](https://github.com/DevVault/WindowsGSM.L4D)

![Screenshot Plugins](https://windowsgsm.com/assets/images/WindowsGSM-Plugins-v1.21.0.png)

## Built-in game integration tests

`CdogsWindowsGSM.Tests` includes lifecycle tests for every built-in game. They run the game's install, update, validation (where supported), and launch operations; shared-install games use the shared binary/configuration path. Games with configuration-backup support also run a backup/restore round trip. The same live suite exercises the V2 `GarrysModV2` shared-install/config-only methods and the `CS16StandaloneV2` backup-plan capability from the plugin examples. Running the `GameIntegration` category starts the live operations directly; there is no environment-variable opt-in.

Copy `CdogsWindowsGSM.Tests\steam-credentials.example.txt` to `CdogsWindowsGSM.Tests\steam-credentials.txt` and fill in `steamUser` and `steamPass` before running the live tests. The real credentials file is git-ignored. Use a dedicated Steam account with the required game entitlements: SteamCMD receives the credentials through its existing command-line login flow. Minecraft EULA prompts may also require confirmation during a run.

From Windows PowerShell, run:

```powershell
dotnet test CdogsWindowsGSM.Tests\CdogsWindowsGSM.Tests.csproj --filter "TestCategory=GameIntegration"
```

By default, integration data is rooted outside the repository at `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration`. After each test case, cleanup deletes test-created server directories, per-server backup/log folders, and shared game installations no longer referenced by any server. A shared installation still referenced by another server is retained. The integration root itself is not removed, and an interrupted process may leave files behind. Set `CDOGS_GAME_INTEGRATION_ROOT` to choose another directory outside the repository, `CDOGS_GAME_INTEGRATION_GAMES` to a comma-separated list of exact built-in game names or the `GarrysModV2` / `CS16StandaloneV2` example names to run a subset, or `CDOGS_STEAM_CREDENTIALS_FILE` to use a credentials file elsewhere. Firewall rules are not changed while the test path override is active.

## Releases and updates

The latest published release is [CdogsWindowsGSM 1.24.1](https://github.com/conorjh/CdogsWindowsGSM/releases/tag/v1.24.1). Download both `CdogsWindowsGSM.exe` and `CdogsWindowsGSM-Updater.exe` from its release assets. In-app updates use releases published in this repository.

## Upstream resources
- **Original source:** [WindowsGSM/WindowsGSM](https://github.com/WindowsGSM/WindowsGSM)
- **Documentation:** [WindowsGSM documentation](https://docs.windowsgsm.com/) and its [source repository](https://github.com/WindowsGSM/Documentation)
- **Community:** [Steam group](https://steamcommunity.com/groups/WindowsGSM) and [Discord](https://discord.gg/bGc7t2R)

## Credits

- **CdogsWindowsGSM contributors** — fork maintenance and development
- **TatLead** — original application work ([BattlefieldDuck](https://github.com/BattlefieldDuck))
- **Deathreus** — code optimization ([Deathreus](https://github.com/Deathreus))
- **Soljian** — SCUM integration adapted from [WindowsGSM.SCUM](https://github.com/soljian/WindowsGSM.SCUM), distributed under MIT (see [third-party notices](THIRD-PARTY-NOTICES.txt))
- **! AssaultLine** and **Dennis** — Mordhau server testing

## License

Licensed under the MIT License; see [LICENSE](LICENSE). The original copyright notice is retained, with fork contributions attributed to CdogsWindowsGSM contributors.
