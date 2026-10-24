# Example reference: `Skeleton.cs`

[Source file](../../Examples/Skeleton.cs) | [From-scratch authoring tutorial](../PLUGIN-FROM-SCRATCH-TUTORIAL.md)

## Purpose and status

`Skeleton.cs` is a minimal V1-shaped template for a public plugin class that does not inherit from a game-engine class. It shows the `ServerConfig` constructor, `Plugin` metadata, the public properties WindowsGSM reads, and the usual lifecycle method names.

It is **not a working server integration**. The game identity and defaults are blank; `Install` and `Start` return `null`; install/import validation returns `false`; build lookups return empty strings; and the config/start/stop/update bodies are empty or placeholders. Do not load it as a usable game plugin or treat a successful source compile as a game test.

## What to implement

Before adapting the sample, choose a unique public class and matching source filename, then supply the real game's metadata, executable, install identity, ports, query method, and defaults. Implement file validation, config generation, install/update, launch arguments, and a graceful stop based on the game's documented server behavior. Report failures through the plugin's `Error` member and return the failure value expected by that operation.

Start with a private per-server install and the host-managed full-server backup. Do not add shared installation or config-only backup until the game's actual writable paths and restore scope have been verified. This template has no V2 methods and therefore declares neither optional capability.

## Build and load

Use the complete repository and the `CdogsWindowsGSM-Plugin-Development` project as described in the [authoring tutorial](../PLUGIN-FROM-SCRATCH-TUTORIAL.md). The loader expects the public class name and source filename to match. The `Examples` folder is for reference; the development helper copies source files from its `Plugins` folder, not directly from `Examples`.

The sample is a code-shape reference only. Confirm the host's compiler diagnostics, then test install/start/stop/update and backup/restore with a disposable server before describing a real integration as supported.