using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class PluginSourceCompatibilityTests
{
    [TestMethod]
    public void ExistingCSharp73SkeletonCompilesAndRemainsVersionOne()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Skeleton.cs");
        Assembly assembly = CompilePluginSource(File.ReadAllText(sourcePath));
        Type pluginType = assembly.GetType("WindowsGSM.Plugins.Skeleton");
        Assert.IsNotNull(pluginType);
        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(pluginType);
        Assert.AreEqual(PluginApiLevel.Legacy, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.None, profile.Capabilities);
    }

    [TestMethod]
    public void ConfigOnlyBackupPlanExampleCompilesAndIsInferredWithTheCSharp73PluginLanguageLevel()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SkeletonV2.cs");
        Assembly assembly = CompilePluginSource(File.ReadAllText(sourcePath));
        Type pluginType = assembly.GetType("WindowsGSM.Plugins.SkeletonV2");
        Assert.IsNotNull(pluginType);

        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(pluginType);
        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.ConfigurationBackup, profile.Capabilities);
    }

    [TestMethod]
    public async Task CompleteGarrysModTutorialPluginCompilesAndRunsItsTypedSetupWithCSharp73()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GarrysModV2.cs");
        Assembly assembly = CompilePluginSource(File.ReadAllText(sourcePath));
        Type pluginType = assembly.GetType("WindowsGSM.Plugins.GarrysModV2");
        Assert.IsNotNull(pluginType);

        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(pluginType);
        object plugin = Activator.CreateInstance(pluginType, new object[] { null! });
        Assert.IsNotNull(plugin);
        PluginSharedInstallationAdapter adapter = PluginCapabilityInspector.CreateSharedInstallationAdapter(plugin);
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", Path.GetTempPath() }
        };
        var backupContext = new PluginInstanceContext("1", "GarrysModV2", string.Empty, roots);
        PluginBackupPlan plan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(plugin, backupContext);

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "WindowsGSM.GarrysModV2.Tests", Guid.NewGuid().ToString("N"));
        string installationRoot = Path.Combine(temporaryRoot, "installation");
        string instanceRoot = Path.Combine(temporaryRoot, "instance");
        var installContext = new ServerInstanceContext("1", "GarrysModV2", "install-1", installationRoot, instanceRoot);

        try
        {
            SharedInstallationOperationResult result = await adapter.InstallConfigOnly(installContext);

            Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
            Assert.AreEqual(
                WindowsGSMPluginCapability.ConfigurationBackup | WindowsGSMPluginCapability.SharedInstallation,
                profile.Capabilities);
            Assert.AreEqual("4020", adapter.AppId);
            Assert.AreEqual("srcds.exe", adapter.ExecutableName);
            CollectionAssert.AreEqual(
                new[]
                {
                    Path.Combine("garrysmod", "cfg", "server.cfg"),
                    Path.Combine("garrysmod", "mapcycle.txt")
                },
                plan.Paths.Select(path => path.RelativePath).ToArray());
            Assert.AreEqual(SharedInstallationOperationStatus.Succeeded, result.Status);
            Assert.AreEqual(
                "hostname \"Garry's Mod V2 tutorial\"" + Environment.NewLine +
                "sv_lan 0" + Environment.NewLine +
                "mp_timelimit 30" + Environment.NewLine,
                File.ReadAllText(Path.Combine(instanceRoot, "garrysmod", "cfg", "server.cfg")));
            Assert.AreEqual(
                "gm_construct" + Environment.NewLine,
                File.ReadAllText(Path.Combine(instanceRoot, "garrysmod", "mapcycle.txt")));
            Assert.IsFalse(Directory.Exists(installationRoot));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }

    [TestMethod]
    public void StandaloneCounterStrikeExampleCompilesWithoutEngineBaseAndDeclaresItsConfigurationFiles()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CS16StandaloneV2.cs");
        Assembly assembly = CompilePluginSource(File.ReadAllText(sourcePath));
        Type pluginType = assembly.GetType("WindowsGSM.Plugins.CS16StandaloneV2")!;
        Assert.IsNotNull(pluginType);
        Assert.AreEqual(typeof(object), pluginType.BaseType);

        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(pluginType);
        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
        Assert.AreEqual(WindowsGSMPluginCapability.ConfigurationBackup, profile.Capabilities);

        object plugin = Activator.CreateInstance(pluginType, new object[] { null! })!;
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", Path.GetTempPath() }
        };
        var context = new PluginInstanceContext("1", "CS16StandaloneV2", string.Empty, roots);
        PluginBackupPlan plan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(plugin, context);

        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine("cstrike", "server.cfg"),
                Path.Combine("cstrike", "mapcycle.txt")
            },
            plan.Paths.Select(path => path.RelativePath).ToArray());
    }

    [TestMethod]
    public void TypedSharedInstallMethodsRequireAndInferConfigurationBackupWithoutAContractMarker()
    {
        const string source = @"using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace WindowsGSM.Plugins
{
    public sealed class SharedProviderCompatibility
    {
        public string AppId { get { return ""12345""; } }
        public string StartPath { get { return ""server.exe""; } }

        public SharedProviderCompatibility(ServerConfig serverConfig)
        {
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath(""serverfiles"", ""server.cfg"")
            });
        }

        public Task<System.Diagnostics.Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            return Task.FromResult<System.Diagnostics.Process>(null);
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            return Task.FromResult(SharedInstallationOperationResult.Failed(""Not configured.""));
        }
    }
}";
        Assembly assembly = CompilePluginSource(source);
        Type pluginType = assembly.GetType("WindowsGSM.Plugins.SharedProviderCompatibility");
        Assert.IsNotNull(pluginType);

        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(pluginType);
        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version);
        Assert.AreEqual(
            WindowsGSMPluginCapability.ConfigurationBackup | WindowsGSMPluginCapability.SharedInstallation,
            profile.Capabilities);
    }

    private static Assembly CompilePluginSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp7_3));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "PluginCompatibility_" + Guid.NewGuid().ToString("N"),
            new[] { syntaxTree },
            GetCompilerReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var assemblyStream = new MemoryStream();
        EmitResult result = compilation.Emit(assemblyStream);
        string errors = string.Join(
            Environment.NewLine,
            result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
        Assert.IsTrue(result.Success, errors);

        assemblyStream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
    }

    private static List<MetadataReference> GetCompilerReferences()
    {
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.IsFalse(string.IsNullOrWhiteSpace(trustedAssemblies));

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                paths.Add(Path.GetFullPath(path));
            }
        }

        string applicationAssemblyPath = typeof(Plugin).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(applicationAssemblyPath) && File.Exists(applicationAssemblyPath))
        {
            paths.Add(Path.GetFullPath(applicationAssemblyPath));
        }

        var references = new List<MetadataReference>();
        foreach (string path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }
}