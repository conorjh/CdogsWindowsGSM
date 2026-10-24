using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class PluginApiContractTests
{
    [TestMethod]
    public void LegacyPluginWithoutOptionalMethodsIsInferredAsVersionOne()
    {
        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(typeof(LegacyPlugin));

        Assert.AreEqual(PluginApiLevel.Legacy, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.None, profile.Capabilities);
    }

    [TestMethod]
    public void ConfigOnlyBackupPlanMethodAutomaticallyInfersConfigurationBackup()
    {
        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(typeof(ConfigOnlyBackupPlanMethodPlugin));
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", Path.GetTempPath() }
        };
        var context = new PluginInstanceContext("1", "Example", string.Empty, roots);
        PluginBackupPlan plan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(new ConfigOnlyBackupPlanMethodPlugin(null), context);

        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.ConfigurationBackup, profile.Capabilities);
        CollectionAssert.AreEqual(new[] { "server.cfg" }, plan.Paths.Select(path => path.RelativePath).ToArray());
    }

    [TestMethod]
    public void TypedSharedInstallMethodsAndConfigOnlyBackupPlanInferCapabilitiesWithoutInterfaces()
    {
        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(typeof(SharedMethodsPlugin));
        var plugin = new SharedMethodsPlugin(null);
        PluginSharedInstallationAdapter adapter = PluginCapabilityInspector.CreateSharedInstallationAdapter(plugin);
        string root = Path.Combine(Path.GetTempPath(), "WindowsGSM.SharedInstallContextTests", Guid.NewGuid().ToString("N"));
        var sharedContext = new SharedInstallationContext(
            "install-1",
            "Example",
            "12345",
            "server.exe",
            Path.Combine(root, "servers", "owner", "serverfiles"));
        var instanceContext = new ServerInstanceContext(
            "server-1",
            "Example",
            "install-1",
            sharedContext.InstallationRoot,
            Path.Combine(root, "servers", "server-1", "serverfiles"));

        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
        Assert.AreEqual(
            WindowsGSMPluginCapability.ConfigurationBackup | WindowsGSMPluginCapability.SharedInstallation,
            profile.Capabilities);
        Assert.AreEqual("12345", adapter.AppId);
        Assert.AreEqual("server.exe", adapter.ExecutableName);
        using (Process returnedProcess = adapter.InstallSharedBinaries(sharedContext).GetAwaiter().GetResult())
        using (Process currentProcess = Process.GetCurrentProcess())
        {
            Assert.AreEqual(currentProcess.Id, returnedProcess.Id);
        }
        Assert.AreEqual(SharedInstallationOperationStatus.Succeeded, adapter.InstallConfigOnly(instanceContext).GetAwaiter().GetResult().Status);
    }

    [TestMethod]
    public void SharedBinaryInstallMustReturnTaskOfProcess()
    {
        Assert.Throws<InvalidDataException>(
            () => PluginCapabilityInspector.Inspect(typeof(SharedMethodsWithResultReturningBinaryInstallPlugin)));
    }

    [TestMethod]
    public void SharedInstallationContextsNormalizeRootsAndAllowTheOwnerRootToBeTheInstanceRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "WindowsGSM.SharedInstallContextTests", Guid.NewGuid().ToString("N"));
        string installationRoot = Path.Combine(root, "servers", "owner", "serverfiles");
        string instanceRoot = Path.Combine(root, "servers", "server-1", "serverfiles");

        var sharedContext = new SharedInstallationContext(
            "install-1",
            "Example",
            "12345",
            "server.exe",
            installationRoot);
        var instanceContext = new ServerInstanceContext(
            "server-1",
            "Example",
            "install-1",
            installationRoot,
            instanceRoot);

        Assert.AreEqual(Path.GetFullPath(installationRoot), sharedContext.InstallationRoot);
        Assert.AreEqual(Path.GetFullPath(installationRoot), instanceContext.InstallationRoot);
        Assert.AreEqual(Path.GetFullPath(instanceRoot), instanceContext.InstanceRoot);
        Assert.Throws<ArgumentException>(() => new SharedInstallationContext(
            "install-1",
            "Example",
            "12345",
            "server.exe",
            "relative-root"));
        var ownerInstanceContext = new ServerInstanceContext(
            "server-1",
            "Example",
            "install-1",
            installationRoot,
            installationRoot);
        Assert.AreEqual(Path.GetFullPath(installationRoot), ownerInstanceContext.InstallationRoot);
        Assert.AreEqual(Path.GetFullPath(installationRoot), ownerInstanceContext.InstanceRoot);
    }

    [TestMethod]
    public void SharedInstallationOperationResultsRepresentEachOutcomeExplicitly()
    {
        using (var process = new Process())
        {
            SharedInstallationOperationResult started = SharedInstallationOperationResult.Started(process);

            Assert.AreEqual(SharedInstallationOperationStatus.Started, started.Status);
            Assert.AreSame(process, started.Process);
        }

        SharedInstallationOperationResult succeeded = SharedInstallationOperationResult.Succeeded();
        SharedInstallationOperationResult cancelled = SharedInstallationOperationResult.Cancelled("Cancelled by provider.");
        SharedInstallationOperationResult failed = SharedInstallationOperationResult.Failed("Provider failed.");

        Assert.AreEqual(SharedInstallationOperationStatus.Succeeded, succeeded.Status);
        Assert.AreEqual(SharedInstallationOperationStatus.Cancelled, cancelled.Status);
        Assert.AreEqual("Cancelled by provider.", cancelled.ErrorMessage);
        Assert.AreEqual(SharedInstallationOperationStatus.Failed, failed.Status);
        Assert.AreEqual("Provider failed.", failed.ErrorMessage);
        Assert.Throws<ArgumentNullException>(() => SharedInstallationOperationResult.Started(null));
        Assert.Throws<ArgumentException>(() => SharedInstallationOperationResult.Failed(string.Empty));
    }

    [TestMethod]
    public void LegacyParameterlessSharedMethodsDoNotOptAPluginIntoSharing()
    {
        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(typeof(LegacySharedPlugin));

        Assert.AreEqual(PluginApiLevel.Legacy, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.None, profile.Capabilities);
    }

    [TestMethod]
    public void PartialSharedInstallMethodSetIsRejected()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PluginCapabilityInspector.Inspect(typeof(PartialSharedMethodsPlugin)));

        StringAssert.Contains(exception.Message, "both typed");
    }

    [TestMethod]
    public void SharedInstallationRequiresConfigurationBackup()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PluginCapabilityInspector.Inspect(typeof(SharedMethodsWithoutBackupPlugin)));

        StringAssert.Contains(exception.Message, "GetConfigOnlyBackupPlan");
    }

    [TestMethod]
    public void TypedSharedMethodsRequireAnApplicationAndExecutableIdentity()
    {
        Assert.Throws<InvalidDataException>(
            () => PluginCapabilityInspector.Inspect(typeof(SharedMethodsWithoutIdentityPlugin)));
    }

    [TestMethod]
    public void ConfigOnlyBackupPlanMethodWithWrongReturnTypeIsRejected()
    {
        Assert.Throws<InvalidDataException>(
            () => PluginCapabilityInspector.Inspect(typeof(MalformedConfigOnlyBackupPlanPlugin)));
    }

    [TestMethod]
    public void ConfigOnlyBackupPlanRejectsUnapprovedRootsAndTraversal()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", Path.GetTempPath() }
        };

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.ValidatePlan(
            roots,
            new[] { new ConfigurationBackupPath("logs", "server.log") }));
        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.ValidatePlan(
            roots,
            new[] { new ConfigurationBackupPath("serverfiles", "..\\server.cfg") }));
    }

    [TestMethod]
    public void ConfigOnlyBackupPlanRejectsDuplicateDestinations()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", Path.GetTempPath() }
        };

        Assert.Throws<InvalidDataException>(() => ConfigurationBackupArchive.ValidatePlan(
            roots,
            new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg"),
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            }));
    }

    public sealed class LegacyPlugin
    {
        public LegacyPlugin(ServerConfig serverConfig)
        {
        }
    }

    public sealed class ConfigOnlyBackupPlanMethodPlugin
    {
        public ConfigOnlyBackupPlanMethodPlugin(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }
    }

    public sealed class MalformedConfigOnlyBackupPlanPlugin
    {
        public MalformedConfigOnlyBackupPlanPlugin(ServerConfig serverConfig)
        {
        }

        public string GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return "server.cfg";
        }
    }

    public sealed class SharedMethodsPlugin
    {
        public string AppId { get; private set; } = "12345";
        public string StartPath { get; private set; } = "server.exe";

        public SharedMethodsPlugin(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult(Process.GetCurrentProcess());
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }
    }

    public sealed class SharedMethodsWithoutBackupPlugin
    {
        public string AppId { get; private set; } = "12345";
        public string StartPath { get; private set; } = "server.exe";

        public SharedMethodsWithoutBackupPlugin(ServerConfig serverConfig)
        {
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult<Process>(null);
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }
    }

    public sealed class PartialSharedMethodsPlugin
    {
        public string AppId { get; private set; } = "12345";
        public string StartPath { get; private set; } = "server.exe";

        public PartialSharedMethodsPlugin(ServerConfig serverConfig)
        {
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult<Process>(null);
        }
    }

    public sealed class SharedMethodsWithoutIdentityPlugin
    {
        public SharedMethodsWithoutIdentityPlugin(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult<Process>(null);
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }
    }

    public sealed class SharedMethodsWithResultReturningBinaryInstallPlugin
    {
        public string AppId { get; private set; } = "12345";
        public string StartPath { get; private set; } = "server.exe";

        public SharedMethodsWithResultReturningBinaryInstallPlugin(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }

        public Task<SharedInstallationOperationResult> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Succeeded());
        }
    }

    public sealed class LegacySharedPlugin : WindowsGSM.GameServer.Engine.ISharedInstallationProvider
    {
        public string SharedInstallationAppId => "12345";
        public string SharedInstallationExecutableName => "server.exe";

        public LegacySharedPlugin(ServerConfig serverConfig)
        {
        }

        public Task<Process> InstallSharedBinaries()
        {
            return Task.FromResult<Process>(null);
        }

        public Task<bool> InstallConfigOnly()
        {
            return Task.FromResult(true);
        }
    }
}