# Example reference: `SkeletonV2.cs`

[Source file](../../Examples/SkeletonV2.cs) | [Plugin V1-to-V2 guide](../PLUGIN-V1-TO-V2-GUIDE.md)

## Purpose and status

`SkeletonV2.cs` is a placeholder V1-style plugin plus one optional V2 method: `GetConfigOnlyBackupPlan(PluginInstanceContext)`. It demonstrates method-based capability discovery and a host-owned config-only backup declaration without requiring an engine base class.

It is **not a complete server integration**. Its game metadata is blank, its lifecycle methods are empty or return placeholder values, and its sample plan names `serverfiles/server.cfg` without creating that file. Before using the pattern, implement the game lifecycle and make the declared path match a real, instance-owned configuration file.

## What the V2 method does

The method returns a `PluginBackupPlan` containing a root ID and relative path. It does not read the file, create an archive, or restore it. WindowsGSM checks the method signature and validates the returned paths; the host owns `.wgsmbackup` creation and targeted restore. Use only a root approved in `context.ApprovedRoots`, and do not list credentials unless you accept that they will be present in an unencrypted archive.

This sample does **not** implement `InstallSharedBinaries` or `InstallConfigOnly`, and it does not provide shared-install identity values. It therefore demonstrates config-only backup declaration only; it is not a shared-install provider. Full-server backup remains available through the host and remains the default.

## Build, adapt, and verify

Follow the [V1-to-V2 guide](../PLUGIN-V1-TO-V2-GUIDE.md) for capability rules and the [from-scratch tutorial](../PLUGIN-FROM-SCRATCH-TUTORIAL.md) for the baseline lifecycle. Copy the adapted source into the development project's `Plugins` folder with matching filename/class name, build, and inspect the host's reported capabilities. Test the exact file list and a restore on a disposable instance. Method detection or compilation alone is not live qualification.