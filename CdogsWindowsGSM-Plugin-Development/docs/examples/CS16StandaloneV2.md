# Example reference: `CS16StandaloneV2.cs`

[Source file](../../Examples/CS16StandaloneV2.cs) | [From-scratch authoring tutorial](../PLUGIN-FROM-SCRATCH-TUTORIAL.md)

## Purpose and status

`CS16StandaloneV2.cs` is a no-engine-base reference for a standalone GoldSrc-style plugin. It implements the ordinary install, start, stop, update, validation, and build-lookup members, and demonstrates the optional config-only backup plan. The accompanying tutorial describes its development workflow.

Counter-Strike 1.6 is already a built-in game in this fork, so this example is for learning and isolated development, not for installing a duplicate game entry. The source is a compile-tested tutorial sample; compilation does not prove live server behavior or Alpha qualification.

## What the sample implements

- Installs and updates the `cstrike` mod through SteamCMD using dedicated-server app ID `90`.
- Writes `steam_appid.txt` with game app ID `10`, then launches `hlds.exe` with the `cstrike` mod and the selected server settings.
- Creates `cstrike/server.cfg` and `cstrike/mapcycle.txt` only when they do not already exist, preserving edits on later config-generation calls.
- Declares those two files in `GetConfigOnlyBackupPlan(PluginInstanceContext)`. The host validates the list and owns archive creation and restore.

The sample has no `InstallSharedBinaries` or `InstallConfigOnly` method and no shared-install identity contract. It is a private-install example with a config-only backup declaration, not a shared-install implementation. The `.wgsmbackup` plan is a file list, not a backup archive created by the plugin.

## Build and verification

Follow the [from-scratch tutorial](../PLUGIN-FROM-SCRATCH-TUTORIAL.md) using the complete repository. The helper copies source from the development project's `Plugins` folder, not directly from `Examples`; use a matching filename and class name. Because CS 1.6 is built in, use an isolated development setup and do not replace or confuse the built-in entry. Test SteamCMD, launch/stop, config preservation, and a config-only restore on disposable files before drawing conclusions about the game.