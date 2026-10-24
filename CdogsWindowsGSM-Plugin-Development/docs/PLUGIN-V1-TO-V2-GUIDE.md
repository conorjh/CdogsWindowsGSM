# Plugin API v2: migrate a v1 plugin (Garry's Mod example)

This guide uses a Source-engine Garry's Mod example to show V2 capabilities and V1 migration. For a complete new plugin that does not inherit an engine class, start with the [from-scratch tutorial](PLUGIN-FROM-SCRATCH-TUTORIAL.md). A WindowsGSM plugin is a C# source file that the app compiles when it loads the plugin; the development project helps copy that source into the app's build output.

## Source-engine V2 worked example

### 1. Get the full repository

Install Visual Studio with the .NET 10 SDK and the .NET desktop development workload. Clone the complete repository:

```powershell
git clone https://github.com/conorjh/CdogsWindowsGSM.git
```

Alternatively, use GitHub's **Code > Download ZIP** and extract the whole repository. Do not download only the plugin-development folder: its helper searches upward for `CdogsWindowsGSM.sln` and needs the built WindowsGSM application.

### 2. Open the solution and build WindowsGSM

Open `CdogsWindowsGSM.sln` from the repository root in Visual Studio. Build the `CdogsWindowsGSM` project in **Debug** or **Release**. Keep the same solution configuration when you build and run the plugin-development project; its helper looks for the matching application output, for example:

```text
CdogsWindowsGSM\bin\Debug\net10.0-windows10.0.19041\win-x64\CdogsWindowsGSM.exe
```

For a Release build, the configuration folder is `Release` instead of `Debug`.

### 3. Create the source file

The worked example is [`GarrysModV2.cs`](../Examples/GarrysModV2.cs), with [companion sample notes](examples/GarrysModV2.md). It is a complete plugin class for a Garry's Mod server: it reuses WindowsGSM's public Source-engine lifecycle, writes per-instance files, declares a config-only backup plan, and implements both typed shared-install methods.

Copy it to `CdogsWindowsGSM-Plugin-Development\Plugins\GarrysModV2.cs`. The source filename and public class name must match because the loader uses them to find the plugin type. Update `Plugin.name` as display metadata; it does not determine the class name. The helper copies every top-level `*.cs` file from `Plugins` and does not copy `Examples`. The repository has a `Plugins\NewPlugin.cs` sample too, so the helper will copy that file as well unless you move it out of `Plugins` while testing.

This tutorial deliberately uses Garry's Mod so the sample can demonstrate both V2 capabilities using a real Source-engine game. It is an authoring example, not a recommendation to install a duplicate plugin over WindowsGSM's built-in Garry's Mod entry.

### 4. Declare the plugin class, metadata, and constructor

Start with the imports, namespace, class, metadata, and constructor:

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Plugins
{
	public class GarrysModV2 : Source
	{
		public Plugin Plugin = new Plugin
		{
			name = "WindowsGSM.GarrysModV2",
			author = "Your name",
			description = "Garry's Mod V2 plugin tutorial",
			version = "1.0",
			url = "",
			color = "#ffffff"
		};

		public GarrysModV2(ServerConfig serverData) : base(serverData)
		{
		}
	}
}
```

The host finds a public `WindowsGSM.Plugins.GarrysModV2` class and calls its public `ServerConfig` constructor. `Source` is an existing public host class; this example uses it rather than copying the same engine process code into a new plugin. Your `Plugin` metadata supplies the display name and author.

### 5. Set the game's identity and defaults

Add these members inside the class:

```csharp
public string FullName { get { return "Garry's Mod Dedicated Server (V2 tutorial)"; } }

public override string Game { get { return "garrysmod"; } }
public override string AppId { get { return "4020"; } }
public override string Defaultmap { get { return "gm_construct"; } }
public override string Additional { get { return "-tickrate 66 +gamemode sandbox"; } }
```

The inherited Source class supplies the executable name (`srcds.exe`), default ports, query method, and existing lifecycle. Its `AppId` and `StartPath` values also satisfy the shared-install identity requirement through the inherited `SharedInstallationAppId` and `SharedInstallationExecutableName` properties. `AppId` is the dedicated-server Steam app ID; `Game` is the installed game folder.

### 6. Reuse the existing install, start, stop, and update lifecycle

Because this class derives from `Source`, it already has working `Install`, `Start`, `Stop`, `Update`, `IsInstallValid`, `IsImportValid`, `GetLocalBuild`, and `GetRemoteBuild` methods. `Start` resolves the private or shared binary root, uses the selected instance's `server.cfg` and mapcycle, and launches `srcds.exe`; `Install` and `Update` use SteamCMD. We only write the game-specific configuration and add the typed V2 entry points below.

If your game is not a Source-engine game, do not inherit `Source` just to copy this example. Start with the [from-scratch no-base-class tutorial](PLUGIN-FROM-SCRATCH-TUTORIAL.md), implement the lifecycle members your game needs, and then add the same typed V2 methods where the game-specific behavior is safe. [`Skeleton.cs`](../Examples/Skeleton.cs) is a bare lifecycle template.

### 7. Write the per-instance configuration

The example implements `CreateServerCFG` for a private install and factors file creation into `WriteInstanceFiles`. The typed shared-install method later calls the same helper with the instance root supplied by the host:

```csharp
public new void CreateServerCFG()
{
	Error = string.Empty;
	try
	{
		WriteInstanceFiles(ServerPath.GetServersServerFiles(serverData.ServerID));
	}
	catch (Exception exception) when (IsExpectedOperationFailure(exception))
	{
		Error = GetFailureMessage("Could not create the instance configuration", exception);
	}
}

private void WriteInstanceFiles(string instanceRoot)
{
	string gameRoot = Path.Combine(instanceRoot, Game);
	string configDirectory = Path.Combine(gameRoot, "cfg");
	Directory.CreateDirectory(configDirectory);

	string serverName = serverData == null || string.IsNullOrWhiteSpace(serverData.ServerName)
		? "Garry's Mod V2 tutorial"
		: serverData.ServerName;
	serverName = serverName.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\"", string.Empty);

	string configPath = Path.Combine(configDirectory, "server.cfg");
	File.WriteAllText(configPath,
		"hostname \"" + serverName + "\"" + Environment.NewLine +
		"sv_lan 0" + Environment.NewLine +
		"mp_timelimit 30" + Environment.NewLine);

	string mapCyclePath = Path.Combine(gameRoot, "mapcycle.txt");
	File.WriteAllText(mapCyclePath, Defaultmap + Environment.NewLine);
}
```

`CreateServerCFG` is the existing private-install hook. The shared-install hook must use its supplied `context.InstanceRoot`, not write configuration into `context.InstallationRoot`. This example deliberately does not put passwords in either file.

### 8. Add the config-only backup plan

Declare the two files the helper just created. The method returns paths; WindowsGSM reads them, creates the config-only archive, validates restore paths, and restores only those files:

```csharp
public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
{
	if (context == null)
	{
		throw new ArgumentNullException(nameof(context));
	}
	if (!context.ApprovedRoots.ContainsKey("serverfiles"))
	{
		throw new InvalidOperationException("The host did not approve the serverfiles backup root.");
	}

	return new PluginBackupPlan(new[]
	{
		new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "cfg", "server.cfg")),
		new ConfigurationBackupPath("serverfiles", Path.Combine(Game, "mapcycle.txt"))
	});
}
```

Only use roots from `context.ApprovedRoots`; today the plugin root is the per-instance `serverfiles` directory. This is not a regular full-server backup: full-server backup stays host-managed and copies the whole per-server folder. Config-only archives are not encrypted, so review the selected files for credentials before adding them.

### 9. Add shared-binary installation and per-instance setup

The first method asks the existing SteamCMD helper to install the shared binaries at the host-provided installation root. Like the regular v1 `Install()` operation, it returns the process for WindowsGSM to monitor; on failure, set the plugin's normal `Error` value and return `null`:

```csharp
public async Task<Process> InstallSharedBinaries(SharedInstallationContext context)
{
	if (context == null)
	{
		throw new ArgumentNullException(nameof(context));
	}

	Error = string.Empty;
	try
	{
		var steamCmd = new WindowsGSM.Installer.SteamCMD();
		Process process = await steamCmd.InstallToPath(context.InstallationRoot, string.Empty, context.AppId, true);
		if (process == null)
		{
			Error = string.IsNullOrWhiteSpace(steamCmd.Error)
				? "SteamCMD did not start the shared installation."
				: steamCmd.Error;
			return null;
		}

		return process;
	}
	catch (Exception exception) when (IsExpectedOperationFailure(exception))
	{
		Error = GetFailureMessage("Failed to start the shared installation", exception);
		return null;
	}
}
```

The second method runs for each server, including the first one. It creates that server's config under `InstanceRoot`; it does not install or modify the shared binaries:

```csharp
public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
{
	if (context == null)
	{
		throw new ArgumentNullException(nameof(context));
	}

	Error = string.Empty;
	try
	{
		WriteInstanceFiles(context.InstanceRoot);
		return Task.FromResult(SharedInstallationOperationResult.Succeeded());
	}
	catch (Exception exception) when (IsExpectedOperationFailure(exception))
	{
		Error = GetFailureMessage("Failed to create the instance configuration", exception);
		return Task.FromResult(SharedInstallationOperationResult.Failed(Error));
	}
}
```

Keep the helper methods `IsExpectedOperationFailure` and `GetFailureMessage` from the complete [`GarrysModV2.cs`](../Examples/GarrysModV2.cs) example; they ensure expected I/O/process errors are surfaced as a failure rather than reported as success. Shared installation requires all three V2 methods and the identity values. This is the current WindowsGSM policy, not an inherent technical dependency between sharing and backups.

### 10. Build, copy, load, and exercise the plugin

1. Build the `CdogsWindowsGSM-Plugin-Development` project. It compiles the source in `Examples`, including the complete worked example, against the host assembly.
2. Copy your finished source to the top level of `CdogsWindowsGSM-Plugin-Development\Plugins` and build the WindowsGSM app in **Debug** or **Release**.
3. Set `CdogsWindowsGSM-Plugin-Development` as the startup project and run it. It copies each top-level `Plugins\*.cs` file to `CdogsWindowsGSM\bin\<configuration>\net10.0-windows10.0.19041\win-x64\plugins\<FileName>.cs\<FileName>.cs`.
4. Set `CdogsWindowsGSM` as the startup project and run the app. Find the plugin in the game list and inspect the Plugins log if it does not load. The runtime compiler uses Roslyn 5.9.0, C# 7.3 syntax, .NET 10 references, and the WindowsGSM application assembly; compiling the development project alone is not runtime-load proof.
5. In a disposable data root, test config-only backup/restore and verify the archive contains only `garrysmod/cfg/server.cfg` and `garrysmod/mapcycle.txt`; confirm unrelated files survive restore. Test shared installation with two instances and verify that their config/mapcycle files differ while both use the same binaries. SteamCMD downloads game files, so check disk space before installing. Do not claim the plugin qualified until its concurrent runtime behavior, update impact, and recovery have been tested.

The complete class at [`GarrysModV2.cs`](../Examples/GarrysModV2.cs) is the reference result of these steps. Change its game-specific values and backup file list for your own game; do not copy a config path just because it appears in this tutorial.

## The short version

- Existing plugins remain v1 when they do not expose any of the optional typed methods. No version attribute, marker interface, or capability interface is required.
- The host infers each optional capability from exact public instance method signatures.
- Config-only backup and shared installation are different capabilities, but under the current plugin API a shared-install plugin must also provide a config-only backup plan, along with both shared-install methods and install identity values.
- Full-server backups remain host-managed copies of the whole per-server directory. They do not call plugin methods.
- A detected method is not proof that the game is safe to share or back up. Test the actual server behavior before advertising support.

## API v2 is an extension, not a rewrite

Keep the plugin class that already describes the game. Its `Plugin` metadata supplies the display details, its `ServerConfig` constructor gives it the selected server's settings, and its existing methods (`Install`, `Start`, `Stop`, `Update`, and any others WindowsGSM uses) continue to do the game-specific work. V2 adds optional public methods to that same class; it does not replace the lifecycle or require a new base class.

The host loads one source file at `plugins\\<Name>.cs\\<Name>.cs` and expects a public, non-abstract `WindowsGSM.Plugins.<Name>` class with a public `(<ServerConfig>)` constructor. The class name must match the plugin name. Keep any per-server state you need from the constructor, as in a normal plugin. A shortened outline is:

```csharp
using WindowsGSM.Functions;

namespace WindowsGSM.Plugins
{
	public class MyGame
	{
		public Plugin Plugin = new Plugin
		{
			name = "WindowsGSM.MyGame",
			author = "Your name",
			description = "Dedicated server support",
			version = "1.0",
			url = "",
			color = "#ffffff"
		};

		private readonly ServerConfig _serverData;

		public MyGame(ServerConfig serverData)
		{
			_serverData = serverData;
		}

		// Keep your existing game properties and lifecycle methods.
		// Add supported API v2 methods to this class.
	}
}
```

The host detects V2 features from exact public instance method names, parameter types, and return types. It does not use an API-version attribute or marker interface. The source is compiled with C# 7.3, so keep the syntax and referenced APIs compatible with that plugin compiler.

## What every plugin still needs

The host continues to discover source at `plugins\<Name>.cs\<Name>.cs` and expects a public, non-abstract `WindowsGSM.Plugins.<Name>` class with a public constructor accepting `ServerConfig`. Keep your public `Plugin` metadata and existing lifecycle methods such as `Install`, `Start`, `Stop`, and `Update`.

If your plugin does not need config-only backups or shared installation, you do not need to change it. Existing lifecycle methods are dispatched as before. The plugin source is compiled by the .NET 10 host with C# 7.3 syntax; source relying on .NET Framework-only APIs may need updates. Compiled plugin binaries are not guaranteed to load unchanged.

## Choose the optional features you need

The host looks for these exact optional methods:

| Capability | Required public method(s) | Result |
|---|---|---|
| Config-only backup | `PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)` | The plugin supplies a declarative list of instance files. The host performs archive creation and restore. |
| Shared installation | `GetConfigOnlyBackupPlan(PluginInstanceContext context)`, plus `Task<Process> InstallSharedBinaries(SharedInstallationContext context)` and `Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)` | The host can install binaries once, prepare each server's separate configuration, and use the plugin's config-only backup plan. |

Shared installation also requires public string fields or properties for the install identity: `SharedInstallationAppId` or the existing `AppId`, and `SharedInstallationExecutableName` or the existing `StartPath`. The executable value must be a root-level filename, not a path. If only one shared-install method is present, or a matching method has the wrong return type, the host reports a plugin load error.

The choices are:

- No optional methods: the plugin remains v1 and uses private installs and full-server backups.
- `GetConfigOnlyBackupPlan` only: the plugin is v2 and supports config-only backups, but not shared installation.
- `GetConfigOnlyBackupPlan` plus both shared-install methods and identity values: the plugin is v2 and supports both features.
- Shared-install methods without `GetConfigOnlyBackupPlan`: rejected. For this API, shared installation must include config-only backup support.

`InstallConfigOnly` is the shared-install setup step that prepares one server's files. It is different from `GetConfigOnlyBackupPlan`, which tells the host which files to include in a config-only backup. Do not add `[WindowsGSMPluginApi(2)]`, `IWindowsGSMPluginV2`, `IPluginBackupPlanProvider`, or `IPluginSharedInstallationProvider`; the host infers API level 2 from supported methods.

## Config-only backups

To opt into config-only backup, add a public instance method with this exact signature:

```csharp
public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
{
	return new PluginBackupPlan(new[]
	{
		new ConfigurationBackupPath("serverfiles", "server.cfg")
	});
}
```

Use only root IDs from `context.ApprovedRoots`; the current host supplies the per-instance `serverfiles` root. Paths must be relative to that root. The host validates the list, reads the files, creates the config-only archive, and restores only the validated destinations. The method must not read or write archive files.

Config-only archives are not encrypted. Do not include credentials or other secrets unless that is an intentional, clearly understood part of the backup behavior.

This capability is separate from a full-server backup. A full-server backup is a host-managed copy of the complete per-server directory and does not use `GetConfigOnlyBackupPlan` or any other plugin method. A plugin can support config-only backup without shared installation. Under the current plugin API policy, however, a plugin that supports shared installation must also provide `GetConfigOnlyBackupPlan`; this is a host requirement, not an inherent technical dependency of shared binaries.

## Worked example: adding V2 backup to a Source plugin

Use the existing [WindowsGSM.L4D plugin](https://github.com/GTVolk/WindowsGSM.L4D) as a Source-engine example. Its V1 class already handles installation, server startup, shutdown, and default configuration. Leave those methods alone for a backup-only V2 conversion.

First trace where the plugin writes the game configuration. Its `Game` value is `left4dead`, and `CreateServerCFG` writes `cfg/server.cfg` under that game directory. The backup root exposed to plugins is `serverfiles`, so the relative file to save is `left4dead/cfg/server.cfg`. The host's `WindowsGSM.cfg` is outside that approved root and should not be added to this plan.

Add this public method inside the existing `L4D` class. That source already imports `WindowsGSM.Functions` and `System.IO`:

```csharp
public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
{
	if (context == null)
	{
		throw new ArgumentNullException(nameof(context));
	}

	return new PluginBackupPlan(new[]
	{
		new ConfigurationBackupPath(
			"serverfiles",
			Path.Combine(Game, "cfg", "server.cfg"))
	});
}
```

That method alone opts the plugin into V2 config-only backup; it does not enable shared installation. The host reads and restores only the declared file. Before enabling it, test a backup and restore on a disposable server and verify the preview names only `serverfiles/left4dead/cfg/server.cfg`. This plugin substitutes the RCON password into `server.cfg`, and config-only archives are not encrypted, so account for that exposure before using the example unchanged.

For a shared-install conversion, keep this backup plan and additionally implement both typed shared-install methods and the install identity members described below. The host rejects shared-install methods without `GetConfigOnlyBackupPlan`.

## Shared installation

Shared installation is suitable only when the game can keep all writable state and configuration specific to each server instance. Implement both methods and the config-only backup method:

- `GetConfigOnlyBackupPlan(PluginInstanceContext context)` must list the instance configuration files that the host will back up and restore.
- `InstallSharedBinaries(SharedInstallationContext context)` runs when the host creates a shared binary installation. Use `context.InstallationRoot` for shared binaries and return the installer `Process` for WindowsGSM to monitor. Set the plugin's normal `Error` value and return `null` on failure; return `null` after successful synchronous work that did not start a process.
- `InstallConfigOnly(ServerInstanceContext context)` runs for every server using the shared binaries, including the first one. Use `context.InstanceRoot` for that instance's configuration/data. For the first server, `InstanceRoot` is the same `serverfiles` directory as `InstallationRoot`; later linked servers use their own `serverfiles` directories. Do not download, repair, replace, or update shared binaries here. Return a terminal `Succeeded()`, `Cancelled(message)`, or `Failed(message)` result.

The contexts contain host-derived roots, but plugins are trusted in-process code, not sandboxed. A method pair does not prove that a game is safe for concurrent instances. Verify where the game writes files, whether different configurations are actually loaded, what happens during updates, and how failures are recovered. If a game cannot isolate its mutable state, do not implement or advertise shared installation for it.

## Migration checklist

1. Keep the existing v1 constructor, metadata, lifecycle methods, and private-install behavior.
2. Add `GetConfigOnlyBackupPlan(PluginInstanceContext)` if the game has a safe, useful set of config-only backup files; it is required for shared installation.
3. Add both typed shared-install methods and the required identity members only if the game can safely separate shared binaries from per-instance configuration and writable data.
4. Test backup and restore effects, then test two simultaneous instances, update behavior, and failure recovery before calling the capability supported.
5. Review plugin load diagnostics for malformed or partial method sets; do not treat a load failure as a fallback to a usable capability.

See the [Plugin API v2 plan](PLUGIN-API-V2-PLAN.md), the [v1 skeleton](../Examples/Skeleton.cs), the [`SkeletonV2.cs` example](../Examples/SkeletonV2.cs), and the [from-scratch no-base-class tutorial](PLUGIN-FROM-SCRATCH-TUTORIAL.md).
