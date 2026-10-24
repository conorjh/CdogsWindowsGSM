# Example reference: `GarrysModV2.cs`

[Source file](../../Examples/GarrysModV2.cs) | [Full V1-to-V2 walkthrough](../PLUGIN-V1-TO-V2-GUIDE.md)

## Purpose and status

`GarrysModV2.cs` is a Source-derived tutorial sample for the optional V2 config-only backup and shared-install methods. It inherits the Source lifecycle and uses Garry's Mod's game folder (`garrysmod`), Steam app ID `4020`, default map `gm_construct`, and sample launch arguments `-tickrate 66 +gamemode sandbox`.

This is a learning/reference sample, not a qualified third-party plugin. Garry's Mod already has a built-in integration in this fork, and the current Alpha candidate keeps all plugin shared-install providers private-only. The presence of these methods does not enable the sample for shared runtime testing or qualify the game.

## Files and optional capabilities

The sample writes an instance `garrysmod/cfg/server.cfg` and `garrysmod/mapcycle.txt`. Its `GetConfigOnlyBackupPlan` returns those two paths under the approved per-instance `serverfiles` root. WindowsGSM, not the plugin, creates and restores the `.wgsmbackup` archive.

For the shared-install API, `InstallSharedBinaries` asks SteamCMD to install at the host-supplied installation root. `InstallConfigOnly` writes each server's config using its instance root. The inherited Source values provide the game identity members expected by the host. The methods describe the plugin's implementation; they do not override the host's current provider gates.

**Important:** `WriteInstanceFiles` uses `File.WriteAllText` for both files. Calling the sample's config-generation hook can replace existing edits. Use disposable files or change the sample to preserve user-owned configuration before testing it.

## Build and verification

Follow the [V1-to-V2 guide](../PLUGIN-V1-TO-V2-GUIDE.md), copy the adapted source into the development project's `Plugins` folder with matching filename and public class name, then build and inspect host diagnostics. Keep a private test instance for ordinary lifecycle checks. Do not attempt shared plugin binaries in the current candidate; separate code-level method detection from live qualification. Test config-only paths and restore only with disposable data.