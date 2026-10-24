# Example reference: `Skeleton-SteamCMDAgent.cs`

[Source file](../../Examples/Skeleton-SteamCMDAgent.cs) | [Plugin authoring and migration notes](../PLUGIN-AUTHORING-AND-MIGRATION.md)

## Purpose and status

`Skeleton-SteamCMDAgent.cs` is a V1-shaped template for a game whose dedicated-server files are managed through the host's `SteamCMDAgent` base class. It illustrates the base constructor, SteamCMD-related overrides (`loginAnonymous`, `AppId`, and `StartPath`), and game-specific config/start/stop members.

It is **not ready to load as a functioning integration**. The app ID, executable, game metadata, and defaults are blank; config generation and stop behavior are empty; and `Start` returns `null`. The sample's authentication choice is a placeholder too. Set it according to the game's official dedicated-server requirements; never put a user's Steam password in source or documentation.

## Adapting the template

Use the `SteamCMDAgent` implementation only when its install/update behavior matches the game. Supply the correct dedicated-server app ID and executable filename, then implement the game's config, launch arguments, stop command, validation, and error reporting. Verify that the server process and its writable files remain inside the intended private instance before adding optional capabilities.

This sample does not declare `GetConfigOnlyBackupPlan`, `InstallSharedBinaries`, or `InstallConfigOnly`. It is private-only and uses the normal host-managed full-server backup unless you deliberately implement and validate an optional V2 method set.

## Build and verification

Use a matching source filename/public class name and follow the repository's plugin-development workflow. `Examples` files are references; copy an adapted source into the development project's `Plugins` folder to use the helper. After compilation, test the actual SteamCMD install/update, start, graceful stop, and backup/restore on a disposable server. A compiler success is not evidence that SteamCMD credentials, the app ID, or runtime behavior are correct.