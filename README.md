# CdogsWindowsGSM

[![License](https://img.shields.io/github/license/conorjh/CdogsWindowsGSM)](LICENSE)
[![Latest release](https://img.shields.io/github/release/conorjh/CdogsWindowsGSM)](https://github.com/conorjh/CdogsWindowsGSM/releases)
[![Downloads](https://img.shields.io/github/downloads/conorjh/CdogsWindowsGSM/total)](https://github.com/conorjh/CdogsWindowsGSM/releases)

A community-maintained Windows desktop application for installing, importing, and managing dedicated game servers. This codebase is based on the original [WindowsGSM project](https://github.com/WindowsGSM/WindowsGSM). A clanker did all the heavy lifting.

## Fork-specific features

- **Added built-in games:** Call of Duty 1, CoD4x, OpenRCT2, OpenTTD, Mindustry, OpenRA, RedM, SCUM, and Soldat, alongside the Minecraft server variants.
- **FiveM and RedM fixes:** share the current CFX server artifacts and query support; both require a valid CFX license key.
- **OpenRCT2 servers:** download a verified sample park when none is present and start without custom server parameters; a licensed RollerCoaster Tycoon 2 installation is still required.
- **CoD4x:** downloads the upstream [CoD4x 21.2](https://github.com/callofduty4x/CoD4x_Server/releases/tag/21.2) server binary (AGPL-3.0); a licensed Call of Duty 4 game installation is required and proprietary game data is not redistributed. No third-party bot package is included.
- **Map rotation:** double-click to add or remove maps in the Source and GoldSrc map-rotation interfaces.
- **GoldSrc IP binding:** passes the configured Server IP using `+ip` so HLDS uses the selected local address on multi-NIC hosts. Avoid conflicting `+ip` entries in Server Parameters.
- **Server-list actions:** update all stopped servers sequentially, alongside the existing start/stop/restart all actions; hover over a game icon to see the full game name.
- **In-app updates:** checks for releases from this fork.

|               | 🎮 Game Server  | Supported | Query |
| ------------- | --------------- | --------- | ----- |
| ![logo](CdogsWindowsGSM/Images/Games/7dtd.png?raw=true)   | 7 Days to Die Dedicated Server                    |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/arkse.png?raw=true)  | ARK: Survival Evolved Dedicated Server            |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/avorion.png?raw=true)| Avorion Dedicated Server                          |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/bt.png?raw=true)     | Barotrauma Dedicated Server                       |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/bw.png?raw=true)     | BlackWake Dedicated Server                        |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/cod1.png?raw=true)   | Call of Duty 1 Dedicated Server                   |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/cod4.png?raw=true)   | Call of Duty 4: Modern Warfare Dedicated Server (CoD4x) |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/ce.png?raw=true)     | Conan Exiles Dedicated Server                     |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/cs.png?raw=true)     | Counter-Strike: 1.6 Dedicated Server              |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/cscz.png?raw=true)   | Counter-Strike: Condition Zero Dedicated Server   |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/csgo.png?raw=true)   | Counter-Strike: Global Offensive Dedicated Server |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/css.png?raw=true)    | Counter-Strike: Source Dedicated Server           |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/dod.png?raw=true)    | Day of Defeat Dedicated Server                    |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/dods.png?raw=true)   | Day of Defeat: Source Dedicated Server            |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/dayz.png?raw=true)   | DayZ Dedicated Server                             |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/dmc.png?raw=true)    | Deathmatch Classic Dedicated Server               |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/eco.png?raw=true)    | Eco Dedicated Server                              |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/egs.png?raw=true)    | Empyrion - Galactic Survival Dedicated Server     |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/gmod.png?raw=true)   | Garry's Mod Dedicated Server                      |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/gta5.png?raw=true)   | Grand Theft Auto V Dedicated Server ([FiveM](https://github.com/citizenfx/fivem))|✅| FIVEM |
| ![logo](CdogsWindowsGSM/Images/Games/hl2dm.png?raw=true)  | Half-Life 2: Deathmatch Dedicated Server          |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/hlof.png?raw=true)   | Half-Life: Opposing Force Dedicated Server        |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/heat.png?raw=true)   | Heat Dedicated Server                             |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/ins.png?raw=true)    | Insurgency Dedicated Server                       |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/inss.png?raw=true)   | Insurgency: Sandstorm Dedicated Server            |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/l4d2.png?raw=true)   | Left 4 Dead 2 Dedicated Server                    |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/mcbe.png?raw=true)   | Minecraft: Bedrock Edition Server                 |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Forge Server                           |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Java Edition Server                    |✅| UT3  |
| ![logo](CdogsWindowsGSM/Images/Games/mc.png?raw=true)     | Minecraft: Paper Server                           |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/mcpe.png?raw=true)   | Minecraft: Pocket Edition Server ([PocketMine-MP](https://github.com/pmmp/PocketMine-MP)) |✅| UT3  |
| ![logo](CdogsWindowsGSM/Images/Games/mindustry.png?raw=true) | Mindustry Dedicated Server                     |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/mordhau.png?raw=true)| Mordhau Dedicated Server                          |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/nmrih.png?raw=true)  | No More Room in Hell Dedicated Server             |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/onset.png?raw=true)  | Onset Dedicated Server                            |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/openra.png?raw=true) | OpenRA Dedicated Server                           |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/openttd.png?raw=true) | OpenTTD Dedicated Server                         |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/olow.png?raw=true)   | Outlaws of the Old West Dedicated Server          |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/ps.png?raw=true)     | Post Scriptum Dedicated Server                    |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/rcc.png?raw=true)    | Ricochet Dedicated Server                         |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/rct2.png?raw=true)  | RCT2 Dedicated Server                             |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/redm.png?raw=true)  | Red Dead Redemption 2 Server                      |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/rok.png?raw=true)   | Reign Of Kings Dedicated Server                   |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/ror2.png?raw=true)  | Risk of Rain 2 Dedicated Server                   |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/rust.png?raw=true)   | Rust Dedicated Server                             |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/scum.png?raw=true)  | SCUM dedicated server                             |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/soldat.png?raw=true) | Soldat Dedicated Server                           |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/se.png?raw=true)     | Space Engineers Dedicated Server                  |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/sq.png?raw=true)     | Squad Dedicated Server                            |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/sw.png?raw=true)     | Stormworks Dedicated Server                       |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/tf2.png?raw=true)    | Team Fortress 2 Dedicated Server                  |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/tfc.png?raw=true)    | Team Fortress Classic Dedicated Server            |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/tf.png?raw=true)     | The Forest Dedicated Server                       |✅|      |
| ![logo](CdogsWindowsGSM/Images/Games/unt.png?raw=true)    | Unturned Dedicated Server                         |✅| A2S  |
| ![logo](CdogsWindowsGSM/Images/Games/zps.png?raw=true)    | Zombie Panic Source Dedicated Server              |✅| A2S  |

|               | 🎮 Game Server  | Plugin supported | Query |
| ------------- | --------------- | --------- | ----- |
| <img src="https://github.com/DevVault/WindowsGSM.ARMA2/blob/master/ARMA2.cs/ARMA2.png?raw=true" width="16" height="16"/>   | Arma2   |✅| A2S     |
| <img src="https://github.com/DevVault/WindowsGSM.ARMA2/blob/master/ARMA2OA.cs/ARMA2OA.png?raw=true" width="16" height="16"/>   | Arma2: OA   |✅| A2S     |
| ![logo](https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/arma3.png?raw=true)   | Arma3   |✅|      |
| ![logo](https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/astroneer.png?raw=true)   | ASTRONEER   |✅|      |
| ![logo](https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/deadmatter.png?raw=true)   | Dead Matter   |✅| UT3     |
| <img src="https://github.com/DevVault/WindowsGSM.HLDM/blob/master/HLDM.cs/HLDM.png?raw=true" width="16" height="16"/>   | Half-Life: Deathmatch   |✅| A2S     |
| <img src="https://github.com/DevVault/WindowsGSM.L4D/blob/master/L4D.cs/L4D.png?raw=true" width="16" height="16"/>   | Left 4 Dead   |✅| A2S     |
| ![logo](https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/waterfall.png?raw=true)   | PaperMC: Waterfall   |✅| A2S     |
| ![logo](https://github.com/1stian/imgrepo/blob/master/WindowsGSMrepo/spigot.png?raw=true)   | Spigot   |✅| A2S     |

## Features
  * **Easy Install** - No installation knowledge is required, download any game server by one-click install.
  * **Manage multiple servers** - Manage multiple servers with the same software. No more messy server files!
  * **Easy Backup** - A basic backup feature that allows you to backup or restore server files with just one-click.
  * **Easy Action** - No command knowledge is required. Start/Stop/Restart your server with just a click.
  * **Discord Alert** - Get alerted when your servers encounters any special issues
  * **Auto Restart** - A basic function to auto restart your server if it crashes. Therefore, your server will always be up!
  * **Auto Update** - Checks for updates every 30 minutes, making sure your game servers are up to date!
  * **Restart Crontab** - Schedule your game servers to restart at any time, very useful for servers that require restarts to run smoothly.
  * **Start on Login** - Automatically start the application and selected servers when you sign in.
  * **Query Port Discovery** - Detects the runtime query port for supported A2S, UT3, and FiveM servers, shows whether it matches the configured port, and falls back to the configured value when it cannot be verified.
  * **CPU Priority and Affinity** - Set and save the cpu priority and affinity!
  * **Mod Manager** - Manage custom server mods from the application.
  * **Remote Control** - Remote control your game servers with Discord!
  * **Plugins** - Allow support more game servers by installing plugins

### Languages

WindowsGSM includes English, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Romanian, Russian, and Ukrainian UI text. Choose **Use Windows language** or a language in **Settings > Language**. The preference is saved for the current Windows user and takes effect the next time WindowsGSM starts. Unsupported Windows UI languages fall back to English.

Translations cover the main window, settings, common server-management dialogs, status labels, and Minecraft EULA prompts. Game names, game-specific installer output, third-party plugin text, server logs, and game-server console output are provided by their respective sources and are not translated.

Translations are embedded in the application executable so the existing updater keeps them in sync. To add a language, add a `<culture>.<key>` entry to `Localization/Strings.resx` for every neutral resource key, register the culture prefix in `LocalizationManager`, and add a language option and display name. Keep format placeholders consistent with `Strings.resx`.

### Plugins
Allow support more game servers by installing plugins!
##### Some available plugins (Made by [BattlefieldDuck](https://github.com/BattlefieldDuck) and [GTVolk](https://github.com/GTVolk)):
1. [WindowsGSM.ARMA2](https://github.com/DevVault/WindowsGSM.ARMA2)
2. [WindowsGSM.ARMA2OA](https://github.com/DevVault/WindowsGSM.ARMA2)
3. [WindowsGSM.ARMA3](https://github.com/BattlefieldDuck/WindowsGSM.ARMA3)
3. [WindowsGSM.HLDM](https://github.com/DevVault/WindowsGSM.HLDM)
3. [WindowsGSM.L4D](https://github.com/DevVault/WindowsGSM.L4D)

![Screenshot Plugins](https://windowsgsm.com/assets/images/WindowsGSM-Plugins-v1.21.0.png)

## Releases and updates

The current release is [CdogsWindowsGSM 1.24.1](https://github.com/conorjh/CdogsWindowsGSM/releases/tag/v1.24.1). Download both `CdogsWindowsGSM.exe` and `CdogsWindowsGSM-Updater.exe` from its release assets. In-app updates use releases published in this repository.

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
