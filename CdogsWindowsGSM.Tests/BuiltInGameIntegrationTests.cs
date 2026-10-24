using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using GameServer = WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[TestCategory("GameIntegration")]
[DoNotParallelize]
public sealed class BuiltInGameIntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan ConfigurationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ServerStartupGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ServerShutdownTimeout = TimeSpan.FromSeconds(30);
    private const int ServerDirectoryDeletionRetryAttempts = 12;
    private static readonly TimeSpan ServerDirectoryDeletionRetryDelay = TimeSpan.FromSeconds(5);
    private readonly List<GameInstance> _createdGameInstances = new List<GameInstance>();

    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void DeleteIntegrationTestFiles()
    {
        var failures = new List<string>();
        try
        {
            foreach (GameInstance instance in _createdGameInstances.AsEnumerable().Reverse())
            {
                if (!Directory.Exists(instance.RootPath))
                {
                    continue;
                }

                try
                {
                    using IDisposable rootScope = ServerPath.UseRootForTesting(instance.RootPath);
                    string serverRoot = ServerPath.GetServers(instance.Config.ServerID);
                    if (Directory.Exists(serverRoot))
                    {
                        if (instance.Config.DeleteServerDirectory())
                        {
                            TestContext.WriteLine($"Deleted integration test server '{instance.Config.ServerID}'.");
                        }
                        else
                        {
                            Exception? lastDeleteException = null;
                            for (int attempt = 0; attempt < ServerDirectoryDeletionRetryAttempts && Directory.Exists(serverRoot); attempt++)
                            {
                                try
                                {
                                    Directory.Delete(serverRoot, recursive: true);
                                }
                                catch (Exception exception) when (exception is IOException ||
                                    exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
                                {
                                    lastDeleteException = exception;
                                }

                                if (Directory.Exists(serverRoot) && attempt < ServerDirectoryDeletionRetryAttempts - 1)
                                {
                                    Thread.Sleep(ServerDirectoryDeletionRetryDelay);
                                }
                            }

                            if (Directory.Exists(serverRoot))
                            {
                                string reason = lastDeleteException?.Message ??
                                    $"the directory remained after {ServerDirectoryDeletionRetryAttempts} attempts";
                                failures.Add($"Server '{instance.Config.ServerID}' could not be deleted after retry: {reason}");
                            }
                            else
                            {
                                TestContext.WriteLine($"Deleted integration test server '{instance.Config.ServerID}' after a retry.");
                            }
                        }
                    }

                    foreach (string serverDataRoot in new[]
                    {
                        Path.Combine(instance.RootPath, ServerPath.FolderName.Backups, instance.Config.ServerID),
                        Path.Combine(instance.RootPath, ServerPath.FolderName.Logs, instance.Config.ServerID)
                    })
                    {
                        if (Directory.Exists(serverDataRoot))
                        {
                            Directory.Delete(serverDataRoot, recursive: true);
                        }
                    }
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                    exception is UnauthorizedAccessException || exception is InvalidOperationException ||
                    exception is System.Security.SecurityException)
                {
                    failures.Add($"Server '{instance.Config.ServerID}' cleanup failed: {exception.Message}");
                }
            }

            foreach (IGrouping<string, GameInstance> rootGroup in _createdGameInstances.GroupBy(
                instance => instance.RootPath,
                StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(rootGroup.Key))
                {
                    continue;
                }

                using IDisposable rootScope = ServerPath.UseRootForTesting(rootGroup.Key);
                string[] testServerIds = rootGroup
                    .Select(instance => instance.Config.ServerID)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (string installationId in rootGroup
                    .Select(instance => instance.Config.SharedInstallationId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (SharedInstallationStore.DeleteIfUnreferenced(installationId))
                        {
                            TestContext.WriteLine($"Deleted integration shared installation '{installationId}'.");
                            continue;
                        }

                        if (!SharedInstallationStore.GetInstallations().Any(record =>
                            string.Equals(record.Id, installationId, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        List<string> linkedServerIds = SharedInstallationStore.GetLinkedServerIds(installationId);
                        string[] remainingTestServerIds = linkedServerIds
                            .Where(serverId => testServerIds.Contains(serverId, StringComparer.OrdinalIgnoreCase))
                            .ToArray();
                        if (remainingTestServerIds.Length > 0)
                        {
                            failures.Add(
                                $"Shared installation '{installationId}' still references integration servers: {string.Join(", ", remainingTestServerIds)}.");
                        }
                        else if (linkedServerIds.Count > 0)
                        {
                            TestContext.WriteLine(
                                $"Retained shared installation '{installationId}' because it is still linked to: {string.Join(", ", linkedServerIds)}.");
                        }
                        else
                        {
                            failures.Add($"Unreferenced shared installation '{installationId}' could not be deleted.");
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                        exception is InvalidDataException || exception is UnauthorizedAccessException ||
                        exception is InvalidOperationException)
                    {
                        failures.Add($"Shared installation '{installationId}' cleanup failed: {exception.Message}");
                    }
                }
            }
        }
        finally
        {
            _createdGameInstances.Clear();
        }

        if (failures.Count > 0)
        {
            throw new IOException("Integration test cleanup failed: " + string.Join(" ", failures));
        }
    }

    public static IEnumerable<object[]> BuiltInGameCases()
    {
        foreach (string gameKey in GetBuiltInGameKeys())
        {
            yield return new object[] { gameKey };
        }
    }

    public static IEnumerable<object[]> V2PluginCases()
    {
        yield return new object[] { "GarrysModV2", "GarrysModV2.cs" };
        yield return new object[] { "CS16StandaloneV2", "CS16StandaloneV2.cs" };
    }

    [TestMethod]
    public void EveryBuiltInGameHasAnInstallUpdateAndStartOperation()
    {
        string[] gameKeys = GetBuiltInGameKeys();
        Assert.IsTrue(gameKeys.Length > 0, "No built-in games were found.");
        Assert.AreEqual(gameKeys.Length, gameKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Built-in game names must be unique.");

        foreach (string gameKey in gameKeys)
        {
            var serverConfig = new ServerConfig("integration-catalog-" + GetGameSuffix(gameKey));
            object gameServer = GameServer.Data.Class.Get(gameKey, serverConfig);

            Assert.IsNotNull(gameServer, $"The built-in factory did not create '{gameKey}'.");
            Assert.IsNotNull(FindProcessOperation(gameServer, "Install"), $"'{gameKey}' has no Task<Process> Install operation.");
            Assert.IsNotNull(FindProcessOperation(gameServer, "Update"), $"'{gameKey}' has no Task<Process> Update operation.");
            Assert.IsNotNull(FindProcessOperation(gameServer, "Start"), $"'{gameKey}' has no Task<Process> Start operation.");
            Assert.IsNotNull(FindStopOperation(gameServer), $"'{gameKey}' has no Stop(Process) operation.");
        }
    }

    [TestMethod]
    [DynamicData(nameof(BuiltInGameCases), DynamicDataSourceType.Method)]
    public async Task InstallUpdateVerifyLaunchAndRestoreWorkForBuiltInGame(string gameKey)
    {
        if (!TryGetSettings(gameKey, out IntegrationSettings settings, out string skipReason))
        {
            Assert.Inconclusive(skipReason);
            return;
        }

        if (!SteamCredentials.TryLoad(settings.CredentialsPath, out SteamCredentials credentials, out skipReason))
        {
            Assert.Inconclusive(skipReason);
            return;
        }

        Directory.CreateDirectory(settings.RootPath);
        using IDisposable rootScope = ServerPath.UseRootForTesting(settings.RootPath);

        GameInstance primary = CreateGameInstance(
            gameKey,
            CreateServerId("primary", gameKey),
            sharedInstallationId: string.Empty);

        if (BuiltInGameCapabilities.SupportsSharedInstallation(gameKey, primary.GameServer))
        {
            await RunSharedInstallationLifecycleAsync(primary, gameKey, credentials);
        }
        else
        {
            await RunStandaloneLifecycleAsync(primary, gameKey, credentials);
        }
    }

    [TestMethod]
    [DynamicData(nameof(V2PluginCases), DynamicDataSourceType.Method)]
    public async Task InstallUpdateVerifyLaunchAndV2CapabilitiesWorkForPluginExample(string selectionName, string fixtureFileName)
    {
        if (!TryGetSettings(selectionName, out IntegrationSettings settings, out string skipReason))
        {
            Assert.Inconclusive(skipReason);
            return;
        }

        if (!SteamCredentials.TryLoad(settings.CredentialsPath, out SteamCredentials credentials, out skipReason))
        {
            Assert.Inconclusive(skipReason);
            return;
        }

        Directory.CreateDirectory(settings.RootPath);
        using IDisposable rootScope = ServerPath.UseRootForTesting(settings.RootPath);
        PluginMetadata pluginMetadata = await LoadPluginAsync(fixtureFileName);
        string gameKey = pluginMetadata.FullName;
        var plugins = new List<PluginMetadata> { pluginMetadata };
        GameInstance primary = CreateGameInstance(
            gameKey,
            CreateServerId("v2-primary", selectionName),
            sharedInstallationId: string.Empty,
            plugins: plugins);

        PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(primary.GameServer.GetType());
        Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version, $"'{selectionName}' was not recognized as a V2 plugin.");
        Assert.IsTrue(profile.Capabilities.HasFlag(WindowsGSMPluginCapability.ConfigurationBackup), $"'{selectionName}' does not expose its V2 backup capability.");

        if (profile.Capabilities.HasFlag(WindowsGSMPluginCapability.SharedInstallation))
        {
            await RunV2SharedInstallationLifecycleAsync(primary, gameKey, selectionName, plugins, credentials);
        }
        else
        {
            await RunV2StandaloneLifecycleAsync(primary, gameKey, credentials);
        }
    }

    [TestMethod]
    public async Task V2PluginExamplesRoundTripTheirApprovedConfigurationBackupsThroughTheHost()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "CdogsWindowsGSM.V2PluginIntegration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            using IDisposable rootScope = ServerPath.UseRootForTesting(rootPath);
            foreach (object[] pluginCase in V2PluginCases())
            {
                string selectionName = (string)pluginCase[0];
                string fixtureFileName = (string)pluginCase[1];
                PluginMetadata pluginMetadata = await LoadPluginAsync(fixtureFileName);
                string gameKey = pluginMetadata.FullName;
                var plugins = new List<PluginMetadata> { pluginMetadata };
                GameInstance instance = CreateGameInstance(
                    gameKey,
                    CreateServerId("v2-backup", selectionName),
                    sharedInstallationId: string.Empty,
                    plugins: plugins);
                PluginCapabilityProfile profile = PluginCapabilityInspector.Inspect(instance.GameServer.GetType());

                Assert.AreEqual(PluginApiLevel.CapabilityMethods, profile.Version, $"'{selectionName}' was not recognized as a V2 plugin.");
                Assert.IsTrue(profile.Capabilities.HasFlag(WindowsGSMPluginCapability.ConfigurationBackup), $"'{selectionName}' has no V2 configuration-backup capability.");

                if (profile.Capabilities.HasFlag(WindowsGSMPluginCapability.SharedInstallation))
                {
                    PluginSharedInstallationAdapter adapter = PluginCapabilityInspector.CreateSharedInstallationAdapter(instance.GameServer);
                    var context = new ServerInstanceContext(
                        instance.Config.ServerID,
                        gameKey,
                        "v2-fixture-installation",
                        Path.Combine(rootPath, "v2-shared", selectionName),
                        ServerPath.GetServersServerFiles(instance.Config.ServerID));
                    SharedInstallationOperationResult result = await adapter.InstallConfigOnly(context);
                    Assert.IsNotNull(result, $"'{selectionName}' InstallConfigOnly returned no result.");
                    Assert.AreEqual(SharedInstallationOperationStatus.Succeeded, result.Status, result.ErrorMessage);
                }
                else
                {
                    await PrepareServerConfigurationAsync(instance, gameKey);
                }

                var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "serverfiles", ServerPath.GetServersServerFiles(instance.Config.ServerID) }
                };
                var backupContext = new PluginInstanceContext(
                    instance.Config.ServerID,
                    gameKey,
                    instance.Config.SharedInstallationId,
                    roots);
                PluginBackupPlan pluginPlan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(instance.GameServer, backupContext);
                Assert.IsNotNull(pluginPlan, $"'{selectionName}' returned no V2 backup plan.");
                List<ConfigurationBackupPath> plan = ConfigurationBackupArchive.ValidatePlan(
                    roots,
                    pluginPlan.Paths.Select(path => new ConfigurationBackupPath(path.RootId, path.RelativePath)));

                RunConfigurationBackupRestoreRoundTrip(
                    instance.Config,
                    gameKey,
                    roots,
                    plan,
                    "v2-integration-fixture");
            }
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private async Task RunStandaloneLifecycleAsync(GameInstance instance, string gameKey, SteamCredentials credentials)
    {
        await RunProcessOperationAsync(instance, gameKey, "Install", validate: null, credentials);
        AssertInstallationIsValid(instance, gameKey, "after install");

        await RunUpdateAndVerifyAsync(instance, gameKey, credentials);
        await PrepareServerConfigurationAsync(instance, gameKey);
        await RunConfigurationBackupRestoreAsync(instance, gameKey);
        await RunLaunchSmokeTestAsync(instance, gameKey);
    }

    private async Task RunV2StandaloneLifecycleAsync(GameInstance instance, string gameKey, SteamCredentials credentials)
    {
        await RunProcessOperationAsync(instance, gameKey, "Install", validate: null, credentials);
        AssertInstallationIsValid(instance, gameKey, "after install");

        await RunUpdateAndVerifyAsync(instance, gameKey, credentials);
        await PrepareServerConfigurationAsync(instance, gameKey);
        RunPluginConfigurationBackupRestore(instance, gameKey);
        await RunLaunchSmokeTestAsync(instance, gameKey);
    }

    private async Task RunSharedInstallationLifecycleAsync(GameInstance owner, string gameKey, SteamCredentials credentials)
    {
        var provider = owner.GameServer as GameServer.Engine.ISharedInstallationProvider;
        Assert.IsNotNull(provider, $"'{gameKey}' is marked shareable but has no shared-install provider.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(provider.SharedInstallationAppId), $"'{gameKey}' has no shared-install app ID.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(provider.SharedInstallationExecutableName), $"'{gameKey}' has no shared-install executable.");

        string installationId;
        bool created;
        using (SharedInstallationResolution resolution = SharedInstallationStore.ResolveOrCreate(
            owner.Config.ServerID,
            gameKey,
            provider.SharedInstallationAppId,
            provider.SharedInstallationExecutableName))
        {
            installationId = resolution.InstallationId;
            created = resolution.Created;
            owner.Config.SharedInstallationId = installationId;
            PersistServerConfig(owner.Config);

            if (created)
            {
                bool markedReady = false;
                try
                {
                    await RunProcessOperationAsync(owner, gameKey, "InstallSharedBinaries", validate: null, credentials);
                    AssertInstallationIsValid(owner, gameKey, "after shared install");
                    SharedInstallationStore.MarkReady(installationId, GetLocalBuild(owner.GameServer));
                    markedReady = true;
                }
                finally
                {
                    if (!markedReady)
                    {
                        SharedInstallationStore.MarkFailed(installationId);
                    }
                }
            }
            else
            {
                resolution.Dispose();
                using (SharedInstallationStore.AcquireExclusiveLeaseForServer(owner.Config))
                {
                    await RunProcessOperationAsync(owner, gameKey, "InstallSharedBinaries", validate: null, credentials);
                    AssertInstallationIsValid(owner, gameKey, "after shared install");
                }
            }
        }

        await InstallSharedInstanceConfigurationAsync(owner, gameKey);

        GameInstance client = CreateGameInstance(
            gameKey,
            CreateServerId("client", gameKey),
            installationId);
        await InstallSharedInstanceConfigurationAsync(client, gameKey);

        await RunUpdateAndVerifyAsync(client, gameKey, credentials);
        await RunConfigurationBackupRestoreAsync(client, gameKey);
        await RunLaunchSmokeTestAsync(client, gameKey);
    }

    private async Task RunV2SharedInstallationLifecycleAsync(
        GameInstance owner,
        string gameKey,
        string selectionName,
        List<PluginMetadata> plugins,
        SteamCredentials credentials)
    {
        PluginSharedInstallationAdapter ownerAdapter = PluginCapabilityInspector.CreateSharedInstallationAdapter(owner.GameServer);
        Assert.AreEqual("4020", ownerAdapter.AppId, $"'{selectionName}' shared-install app ID changed unexpectedly.");

        string installationId;
        using (SharedInstallationResolution resolution = SharedInstallationStore.ResolveOrCreate(
            owner.Config.ServerID,
            gameKey,
            ownerAdapter.AppId,
            ownerAdapter.ExecutableName))
        {
            installationId = resolution.InstallationId;
            owner.Config.SharedInstallationId = installationId;
            owner.Config.ServerName = "Integration test - V2 owner - " + selectionName;
            PersistServerConfig(owner.Config);

            if (resolution.Created)
            {
                bool markedReady = false;
                try
                {
                    var context = new SharedInstallationContext(
                        installationId,
                        gameKey,
                        ownerAdapter.AppId,
                        ownerAdapter.ExecutableName,
                        SharedInstallationStore.GetServerBinaryRoot(installationId));
                    await RunProcessOperationAsync(
                        owner,
                        gameKey,
                        "InstallSharedBinaries",
                        () => ownerAdapter.InstallSharedBinaries(context),
                        credentials);
                    AssertInstallationIsValid(owner, gameKey, "after V2 shared install");
                    SharedInstallationStore.MarkReady(installationId, GetLocalBuild(owner.GameServer));
                    markedReady = true;
                }
                finally
                {
                    if (!markedReady)
                    {
                        SharedInstallationStore.MarkFailed(installationId);
                    }
                }
            }
        }

        await InstallV2SharedInstanceConfigurationAsync(owner, gameKey);
        GameInstance client = CreateGameInstance(
            gameKey,
            CreateServerId("v2-client", selectionName),
            installationId,
            plugins);
        client.Config.ServerName = "Integration test - V2 client - " + selectionName;
        PersistServerConfig(client.Config);
        await InstallV2SharedInstanceConfigurationAsync(client, gameKey);

        string ownerConfig = Path.Combine(
            ServerPath.GetServersServerFiles(owner.Config.ServerID),
            ReadStringMember(owner.GameServer, "Game"),
            "cfg",
            "server.cfg");
        string clientConfig = Path.Combine(
            ServerPath.GetServersServerFiles(client.Config.ServerID),
            ReadStringMember(client.GameServer, "Game"),
            "cfg",
            "server.cfg");
        Assert.AreNotEqual(ownerConfig, clientConfig, "V2 shared instances must keep their configuration in separate instance roots.");
        Assert.AreNotEqual(File.ReadAllText(ownerConfig), File.ReadAllText(clientConfig), "V2 shared instances did not receive distinct server configuration.");

        await RunUpdateAndVerifyAsync(client, gameKey, credentials);
        RunPluginConfigurationBackupRestore(client, gameKey);
        await RunLaunchSmokeTestAsync(client, gameKey);
    }

    private async Task InstallV2SharedInstanceConfigurationAsync(GameInstance instance, string gameKey)
    {
        PluginSharedInstallationAdapter adapter = PluginCapabilityInspector.CreateSharedInstallationAdapter(instance.GameServer);
        var context = new ServerInstanceContext(
            instance.Config.ServerID,
            gameKey,
            instance.Config.SharedInstallationId,
            SharedInstallationStore.GetServerBinaryRoot(instance.Config.SharedInstallationId),
            ServerPath.GetServersServerFiles(instance.Config.ServerID));
        SharedInstallationOperationResult result = await adapter.InstallConfigOnly(context);

        Assert.IsNotNull(result, $"'{gameKey}' InstallConfigOnly returned no result.");
        Assert.AreEqual(SharedInstallationOperationStatus.Succeeded, result.Status, $"'{gameKey}' InstallConfigOnly failed: {result.ErrorMessage}");
        AssertNoOperationError(instance.GameServer, gameKey, "InstallConfigOnly");
        string configPath = Path.Combine(
            context.InstanceRoot,
            ReadStringMember(instance.GameServer, "Game"),
            "cfg",
            "server.cfg");
        Assert.IsTrue(File.Exists(configPath), $"'{gameKey}' V2 config-only installation did not create '{configPath}'.");
    }

    private async Task InstallSharedInstanceConfigurationAsync(GameInstance instance, string gameKey)
    {
        var provider = instance.GameServer as GameServer.Engine.ISharedInstallationProvider;
        Assert.IsNotNull(provider, $"'{gameKey}' lost its shared-install provider for '{instance.Config.ServerID}'.");

        ClearError(instance.GameServer);
        bool succeeded = await provider.InstallConfigOnly();
        Assert.IsTrue(succeeded, $"'{gameKey}' could not install configuration for '{instance.Config.ServerID}': {ReadError(instance.GameServer)}");
        AssertNoOperationError(instance.GameServer, gameKey, "InstallConfigOnly");
    }

    private async Task RunUpdateAndVerifyAsync(GameInstance instance, string gameKey, SteamCredentials credentials)
    {
        await RunProcessOperationAsync(instance, gameKey, "Update", validate: false, credentials);
        AssertInstallationIsValid(instance, gameKey, "after update");

        MethodInfo update = FindProcessOperation(instance.GameServer, "Update");
        bool supportsSteamValidation = update.GetParameters().Any(parameter =>
            string.Equals(parameter.Name, "validate", StringComparison.OrdinalIgnoreCase) &&
            parameter.ParameterType == typeof(bool));

        if (supportsSteamValidation)
        {
            await RunProcessOperationAsync(instance, gameKey, "Update", validate: true, credentials);
            AssertInstallationIsValid(instance, gameKey, "after validation");
        }
        else
        {
            AssertInstallationIsValid(instance, gameKey, "during verification");
            TestContext.WriteLine($"{gameKey}: Update has no validate parameter; verified with IsInstallValid or its executable.");
        }
    }

    private async Task RunProcessOperationAsync(
        GameInstance instance,
        string gameKey,
        string operationName,
        bool? validate,
        SteamCredentials credentials)
    {
        MethodInfo operation = FindProcessOperation(instance.GameServer, operationName);
        Assert.IsNotNull(operation, $"'{gameKey}' has no Task<Process> {operationName} operation.");
        object?[] arguments = CreateOperationArguments(operation, validate);

        await RunProcessOperationAsync(
            instance,
            gameKey,
            operationName,
            () =>
            {
                var task = InvokeUnwrapped(operation, instance.GameServer, arguments) as Task<Process>;
                Assert.IsNotNull(task, $"'{gameKey}' {operationName} did not return Task<Process>.");
                return task;
            },
            credentials,
            validate);
    }

    private async Task RunProcessOperationAsync(
        GameInstance instance,
        string gameKey,
        string operationName,
        Func<Task<Process>> operation,
        SteamCredentials credentials,
        bool? validate = null)
    {

        string steamCredentialsPath = ServerPath.GetBin("steamcmd", "userData.txt");
        string credentialsDirectory = Path.GetDirectoryName(steamCredentialsPath);
        if (string.IsNullOrWhiteSpace(credentialsDirectory))
        {
            throw new InvalidOperationException("The isolated SteamCMD credential path has no parent directory.");
        }
        Directory.CreateDirectory(credentialsDirectory);
        File.WriteAllText(steamCredentialsPath, credentials.ToSteamCmdUserData(), new UTF8Encoding(false));

        Process process;
        ClearError(instance.GameServer);
        TestContext.WriteLine($"{gameKey}: starting {operationName}{(validate == true ? " with validation" : string.Empty)}.");
        try
        {
            process = await operation();
        }
        finally
        {
            if (File.Exists(steamCredentialsPath))
            {
                File.Delete(steamCredentialsPath);
            }
        }

        if (process != null)
        {
            using (process)
            {
                await WaitForOperationProcessAsync(process, gameKey, operationName);
            }
        }

        AssertNoOperationError(instance.GameServer, gameKey, operationName);
        TestContext.WriteLine($"{gameKey}: {operationName} completed.");
    }

    private async Task PrepareServerConfigurationAsync(GameInstance instance, string gameKey)
    {
        MethodInfo configurationMethod = instance.GameServer.GetType().GetMethod(
            "CreateServerCFG",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (configurationMethod == null)
        {
            return;
        }

        ClearError(instance.GameServer);
        TestContext.WriteLine($"{gameKey}: creating instance configuration.");
        if (typeof(Task).IsAssignableFrom(configurationMethod.ReturnType))
        {
            var task = InvokeUnwrapped(configurationMethod, instance.GameServer, null) as Task;
            Assert.IsNotNull(task, $"'{gameKey}' CreateServerCFG did not return a task.");
            await WaitForConfigurationTaskAsync(task, gameKey);
        }
        else if (configurationMethod.ReturnType == typeof(void))
        {
            var context = new AsyncVoidOperationContext();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                InvokeUnwrapped(configurationMethod, instance.GameServer, null);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            await WaitForConfigurationTaskAsync(context.Completion, gameKey);
        }
        else
        {
            Assert.Fail($"'{gameKey}' CreateServerCFG has an unsupported return type.");
        }

        AssertNoOperationError(instance.GameServer, gameKey, "CreateServerCFG");
    }

    private async Task WaitForConfigurationTaskAsync(Task task, string gameKey)
    {
        try
        {
            await task.WaitAsync(ConfigurationTimeout);
        }
        catch (TimeoutException)
        {
            throw new AssertFailedException($"'{gameKey}' configuration generation exceeded {ConfigurationTimeout}.");
        }
    }

    private Task RunConfigurationBackupRestoreAsync(GameInstance instance, string gameKey)
    {
        if (!BuiltInGameCapabilities.SupportsConfigurationBackup(gameKey))
        {
            return Task.CompletedTask;
        }

        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(instance.Config.ServerID) }
        };
        if (!string.IsNullOrWhiteSpace(instance.Config.SharedInstallationId))
        {
            roots.Add("sharedserverfiles", SharedInstallationStore.GetServerBinaryRoot(instance.Config.SharedInstallationId));
        }

        List<ConfigurationBackupPath> plan = BuiltInGameCapabilities.GetConfigurationBackupPlan(instance.Config, roots);
        Assert.IsTrue(plan.Count > 0, $"'{gameKey}' advertised configuration backups but returned an empty plan.");
        string buildId = GetSharedInstallationBuildId(instance.Config.SharedInstallationId);
        RunConfigurationBackupRestoreRoundTrip(instance.Config, gameKey, roots, plan, buildId);
        TestContext.WriteLine($"{gameKey}: configuration backup and restore completed.");
        return Task.CompletedTask;
    }

    private void RunPluginConfigurationBackupRestore(GameInstance instance, string gameKey)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serverfiles", ServerPath.GetServersServerFiles(instance.Config.ServerID) }
        };
        if (!string.IsNullOrWhiteSpace(instance.Config.SharedInstallationId))
        {
            roots.Add("sharedserverfiles", SharedInstallationStore.GetServerBinaryRoot(instance.Config.SharedInstallationId));
        }

        var context = new PluginInstanceContext(
            instance.Config.ServerID,
            gameKey,
            instance.Config.SharedInstallationId,
            roots);
        PluginBackupPlan pluginPlan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(instance.GameServer, context);
        Assert.IsNotNull(pluginPlan, $"'{gameKey}' returned no V2 backup plan.");
        Assert.AreEqual(PluginBackupPlan.CurrentSchemaVersion, pluginPlan.SchemaVersion, $"'{gameKey}' returned an unsupported V2 backup schema.");
        List<ConfigurationBackupPath> plan = ConfigurationBackupArchive.ValidatePlan(
            roots,
            pluginPlan.Paths.Select(path => new ConfigurationBackupPath(path.RootId, path.RelativePath)));
        string buildId = string.IsNullOrWhiteSpace(instance.Config.SharedInstallationId)
            ? GetLocalBuild(instance.GameServer)
            : GetSharedInstallationBuildId(instance.Config.SharedInstallationId);

        RunConfigurationBackupRestoreRoundTrip(instance.Config, gameKey, roots, plan, buildId);
        TestContext.WriteLine($"{gameKey}: V2 configuration backup and restore completed.");
    }

    private void RunConfigurationBackupRestoreRoundTrip(
        ServerConfig config,
        string gameKey,
        Dictionary<string, string> roots,
        List<ConfigurationBackupPath> plan,
        string buildId)
    {
        foreach (ConfigurationBackupPath backupPath in plan)
        {
            Assert.IsTrue(
                File.Exists(Path.Combine(roots[backupPath.RootId], backupPath.RelativePath)),
                $"'{gameKey}' did not create the configuration file '{backupPath.RootId}/{backupPath.RelativePath}'.");
        }

        string backupPathOnDisk = Path.Combine(
            ServerPath.GetBackups(config.ServerID),
            "integration-" + Guid.NewGuid().ToString("N") + ConfigurationBackupArchive.FileExtension);
        ConfigurationBackupPath changedPath = plan[0];
        string changedFilePath = Path.Combine(roots[changedPath.RootId], changedPath.RelativePath);
        byte[] originalContents = File.ReadAllBytes(changedFilePath);

        try
        {
            using (SharedInstallationStore.AcquireReadLeaseForServer(config))
            {
                ConfigurationBackupArchive.Create(
                    backupPathOnDisk,
                    config.ServerID,
                    gameKey,
                    config.SharedInstallationId,
                    buildId,
                    roots,
                    plan);

                File.WriteAllText(changedFilePath, "integration-restore-check");
                List<string> preview = ConfigurationBackupArchive.GetRestorePreview(
                    backupPathOnDisk,
                    config.ServerID,
                    gameKey,
                    config.SharedInstallationId,
                    buildId,
                    roots,
                    plan);
                List<string> restored = ConfigurationBackupArchive.Restore(
                    backupPathOnDisk,
                    config.ServerID,
                    gameKey,
                    config.SharedInstallationId,
                    buildId,
                    roots,
                    plan);

                CollectionAssert.AreEqual(preview.ToArray(), restored.ToArray(), $"'{gameKey}' restore changed a different set of files than its preview.");
                CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(changedFilePath), $"'{gameKey}' restore did not restore the original configuration bytes.");
            }
        }
        finally
        {
            if (File.Exists(backupPathOnDisk))
            {
                File.Delete(backupPathOnDisk);
            }
        }
    }

    private async Task RunLaunchSmokeTestAsync(GameInstance instance, string gameKey)
    {
        MethodInfo start = FindProcessOperation(instance.GameServer, "Start");
        ClearError(instance.GameServer);
        TestContext.WriteLine($"{gameKey}: launching for {ServerStartupGracePeriod}.");
        var startTask = InvokeUnwrapped(start, instance.GameServer, null) as Task<Process>;
        Assert.IsNotNull(startTask, $"'{gameKey}' Start did not return Task<Process>.");
        Process process = await startTask;
        if (process == null)
        {
            Assert.Fail($"'{gameKey}' Start returned no process. Error: {ReadError(instance.GameServer)}");
            return;
        }

        using (process)
        {
            try
            {
                BeginDrainingProcessOutput(process);
                AssertNoOperationError(instance.GameServer, gameKey, "Start");
                await Task.Delay(ServerStartupGracePeriod);
                Assert.IsFalse(
                    process.HasExited,
                    $"'{gameKey}' exited during its {ServerStartupGracePeriod} startup smoke window (exit code {process.ExitCode}).");
            }
            finally
            {
                await StopServerAsync(instance.GameServer, gameKey, process);
            }
        }

        TestContext.WriteLine($"{gameKey}: launch stayed alive and was stopped.");
    }

    private async Task StopServerAsync(object gameServer, string gameKey, Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        MethodInfo stop = FindStopOperation(gameServer);
        string stopFailure = stop == null ? $"'{gameKey}' has no Stop(Process) operation." : string.Empty;
        bool forceTerminated = false;
        try
        {
            if (stop != null)
            {
                object result = InvokeUnwrapped(stop, gameServer, new object[] { process });
                if (result is Task stopTask)
                {
                    try
                    {
                        await stopTask.WaitAsync(ServerShutdownTimeout);
                    }
                    catch (TimeoutException)
                    {
                        stopFailure = $"'{gameKey}' Stop(Process) did not complete within {ServerShutdownTimeout}.";
                    }
                }

                if (string.IsNullOrEmpty(stopFailure) && !process.HasExited)
                {
                    using var timeout = new CancellationTokenSource(ServerShutdownTimeout);
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        stopFailure = $"'{gameKey}' remained alive after Stop(Process) for {ServerShutdownTimeout}.";
                    }
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                forceTerminated = true;
                TestContext.WriteLine($"{gameKey}: force-terminating the isolated process after graceful shutdown failed.");
                KillProcessTree(process);
                using var cleanupTimeout = new CancellationTokenSource(ServerShutdownTimeout);
                try
                {
                    await process.WaitForExitAsync(cleanupTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new AssertFailedException($"'{gameKey}' process did not exit after forced termination.");
                }
            }
        }

        if (!string.IsNullOrEmpty(stopFailure) || forceTerminated)
        {
            string reason = string.IsNullOrEmpty(stopFailure) ? "the process required forced termination" : stopFailure;
            throw new AssertFailedException($"{reason}.");
        }
    }

    private static void AssertInstallationIsValid(GameInstance instance, string gameKey, string phase)
    {
        ClearError(instance.GameServer);
        MethodInfo method = instance.GameServer.GetType().GetMethod(
            "IsInstallValid",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (method != null && method.ReturnType == typeof(bool))
        {
            bool valid = (bool)InvokeUnwrapped(method, instance.GameServer, null);
            Assert.IsTrue(valid, $"'{gameKey}' is invalid {phase}. {ReadError(instance.GameServer)}");
            return;
        }

        string binaryRoot = string.IsNullOrWhiteSpace(instance.Config.SharedInstallationId)
            ? ServerPath.GetServersServerFiles(instance.Config.ServerID)
            : SharedInstallationStore.GetServerBinaryRoot(instance.Config.SharedInstallationId);
        string startPath = ReadStringMember(instance.GameServer, "StartPath");
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            Assert.IsTrue(File.Exists(Path.Combine(binaryRoot, startPath)), $"'{gameKey}' executable '{startPath}' is missing {phase}.");
            return;
        }

        Assert.IsTrue(
            Directory.Exists(binaryRoot) && Directory.EnumerateFiles(binaryRoot, "*", SearchOption.AllDirectories).Any(),
            $"'{gameKey}' produced no server files {phase}.");
    }

    private static void AssertNoOperationError(object gameServer, string gameKey, string operationName)
    {
        string error = ReadError(gameServer);
        Assert.IsTrue(string.IsNullOrWhiteSpace(error), $"'{gameKey}' {operationName} reported an error: {error}");
    }

    private static async Task WaitForOperationProcessAsync(Process process, string gameKey, string operationName)
    {
        BeginDrainingProcessOutput(process);
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();
            throw new AssertFailedException($"'{gameKey}' {operationName} did not exit within {ProcessTimeout}.");
        }

        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"'{gameKey}' {operationName} exited with code {process.ExitCode}.");
    }

    private static void BeginDrainingProcessOutput(Process process)
    {
        if (process.StartInfo.RedirectStandardOutput)
        {
            try
            {
                process.BeginOutputReadLine();
            }
            catch (InvalidOperationException)
            {
                // SteamCMD UpdateEx and some game starts already have asynchronous output readers.
            }
        }

        if (process.StartInfo.RedirectStandardError)
        {
            try
            {
                process.BeginErrorReadLine();
            }
            catch (InvalidOperationException)
            {
                // SteamCMD UpdateEx and some game starts already have asynchronous output readers.
            }
        }
    }

    private static void KillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    private GameInstance CreateGameInstance(
        string gameKey,
        string serverId,
        string sharedInstallationId,
        List<PluginMetadata> plugins = null)
    {
        var config = new ServerConfig(serverId);
        object gameServer = GameServer.Data.Class.Get(gameKey, config, plugins);
        Assert.IsNotNull(gameServer, $"The server factory did not create '{gameKey}'.");

        InitializeIntegrationServerConfig(config, gameKey, gameServer, sharedInstallationId);
        var instance = new GameInstance(config, gameServer, Path.GetFullPath(ServerPath.Get()));
        _createdGameInstances.Add(instance);
        PersistServerConfig(config);
        return instance;
    }

    private static void InitializeIntegrationServerConfig(
        ServerConfig config,
        string gameKey,
        object gameServer,
        string sharedInstallationId)
    {
        GetAvailableTestPorts(gameServer, out string serverPort, out string queryPort);
        config.SchemaVersion = ServerConfig.CurrentSchemaVersion;
        config.ServerGame = gameKey;
        config.ServerName = "Integration test - " + gameKey;
        config.ServerIP = "127.0.0.1";
        config.ServerPort = serverPort;
        config.ServerQueryPort = queryPort;
        config.ServerMap = ReadStringMember(gameServer, "Defaultmap");
        config.ServerMaxPlayer = ReadStringMember(gameServer, "Maxplayers");
        config.ServerGSLT = string.Empty;
        config.ServerParam = ReadStringMember(gameServer, "Additional");
        config.CfxLicenseKey = string.Empty;
        config.EmbedConsole = false;
        config.AutoRestart = false;
        config.AutoStart = false;
        config.AutoUpdate = false;
        config.UpdateOnStart = false;
        config.BackupOnStart = false;
        config.DiscordAlert = false;
        config.DiscordMessage = string.Empty;
        config.DiscordWebhook = string.Empty;
        config.RestartCrontab = false;
        config.CrontabFormat = "0 6 * * *";
        config.AutoStartAlert = true;
        config.AutoRestartAlert = true;
        config.AutoUpdateAlert = true;
        config.RestartCrontabAlert = true;
        config.CrashAlert = true;
        config.CPUPriority = "2";
        config.CPUAffinity = string.Concat(Enumerable.Repeat("1", Environment.ProcessorCount));
        config.AutoScroll = true;
        config.Cod1MeatBots = false;
        config.Cod1AlliesBots = "0";
        config.Cod1AxisBots = "0";
        config.SharedInstallationId = sharedInstallationId ?? string.Empty;
    }

    private static void GetAvailableTestPorts(object gameServer, out string serverPort, out string queryPort)
    {
        int defaultPort = ReadIntegerMember(gameServer, "Port");
        int defaultQueryPort = ReadIntegerMember(gameServer, "QueryPort");
        int portIncrement = Math.Max(ReadIntegerMember(gameServer, "PortIncrements"), 1);

        for (int offset = 0; offset < 1000; offset++)
        {
            int candidateServerPort = defaultPort + (offset * portIncrement);
            int candidateQueryPort = defaultQueryPort + (offset * portIncrement);
            if (candidateServerPort > ushort.MaxValue || candidateQueryPort > ushort.MaxValue)
            {
                break;
            }
            if (candidateServerPort == 27020 || candidateQueryPort == 27020)
            {
                continue;
            }

            if (IsTestPortAvailable(candidateServerPort) &&
                (candidateQueryPort == candidateServerPort || IsTestPortAvailable(candidateQueryPort)))
            {
                serverPort = candidateServerPort.ToString();
                queryPort = candidateQueryPort.ToString();
                return;
            }
        }

        throw new InvalidOperationException($"Could not find available integration ports starting at {defaultPort}.");
    }

    private static bool IsTestPortAvailable(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            using (var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                udpSocket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int ReadIntegerMember(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.GetIndexParameters().Length == 0)
        {
            object value = property.GetValue(instance);
            if (value is int integer)
            {
                return integer;
            }
            if (value is string text && int.TryParse(text, out integer))
            {
                return integer;
            }
        }

        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            object value = field.GetValue(instance);
            if (value is int integer)
            {
                return integer;
            }
            if (value is string text && int.TryParse(text, out integer))
            {
                return integer;
            }
        }

        throw new InvalidOperationException($"'{instance.GetType().Name}' does not expose the integer member '{name}'.");
    }

    private static async Task<PluginMetadata> LoadPluginAsync(string fixtureFileName)
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
        Assert.IsTrue(File.Exists(fixturePath), $"The V2 plugin fixture '{fixtureFileName}' was not copied to the test output.");

        PluginMetadata plugin = await new PluginManagement().LoadPlugin(fixturePath);
        Assert.IsTrue(plugin.IsLoaded, $"The V2 plugin fixture '{fixtureFileName}' failed to load: {plugin.Error}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.FullName), $"The V2 plugin fixture '{fixtureFileName}' has no game name.");
        return plugin;
    }

    private static void PersistServerConfig(ServerConfig config)
    {
        string configPath = ServerPath.GetServersConfigs(config.ServerID, "WindowsGSM.cfg");
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        Assert.IsTrue(config.CreateWindowsGSMConfig(), $"Could not create the isolated test config for '{config.ServerID}'.");
    }

    private static MethodInfo FindProcessOperation(object gameServer, string operationName)
    {
        return gameServer.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == operationName && method.ReturnType == typeof(Task<Process>))
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault();
    }

    private static MethodInfo FindStopOperation(object gameServer)
    {
        return gameServer.GetType().GetMethod(
            "Stop",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(Process) },
            modifiers: null);
    }

    private static object?[] CreateOperationArguments(MethodInfo operation, bool? validate)
    {
        ParameterInfo[] parameters = operation.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            if (validate.HasValue && string.Equals(parameter.Name, "validate", StringComparison.OrdinalIgnoreCase) && parameter.ParameterType == typeof(bool))
            {
                arguments[index] = validate.Value;
            }
            else if (parameter.HasDefaultValue)
            {
                arguments[index] = parameter.DefaultValue;
            }
            else if (parameter.ParameterType == typeof(bool))
            {
                arguments[index] = false;
            }
            else if (parameter.ParameterType == typeof(string))
            {
                arguments[index] = null;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported required parameter '{parameter.Name}' on {operation.DeclaringType?.Name}.{operation.Name}.");
            }
        }

        return arguments;
    }

    private static object InvokeUnwrapped(MethodInfo method, object instance, object?[]? arguments)
    {
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void ClearError(object gameServer)
    {
        FieldInfo field = gameServer.GetType().GetField("Error", BindingFlags.Instance | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(string))
        {
            field.SetValue(gameServer, null);
            return;
        }

        PropertyInfo property = gameServer.GetType().GetProperty("Error", BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(gameServer, null);
        }
    }

    private static string ReadError(object gameServer)
    {
        PropertyInfo property = gameServer.GetType().GetProperty("Error", BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(string))
        {
            return property.GetValue(gameServer) as string ?? string.Empty;
        }

        FieldInfo field = gameServer.GetType().GetField("Error", BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(gameServer) as string ?? string.Empty;
    }

    private static string ReadStringMember(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
        {
            return property.GetValue(instance) as string ?? string.Empty;
        }

        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(instance) as string ?? string.Empty;
    }

    private static string GetLocalBuild(object gameServer)
    {
        MethodInfo method = gameServer.GetType().GetMethod(
            "GetLocalBuild",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return method == null ? string.Empty : InvokeUnwrapped(method, gameServer, null) as string ?? string.Empty;
    }

    private static string GetSharedInstallationBuildId(string sharedInstallationId)
    {
        if (string.IsNullOrWhiteSpace(sharedInstallationId))
        {
            return string.Empty;
        }

        SharedInstallationRecord record = SharedInstallationStore.GetInstallations()
            .SingleOrDefault(installation => string.Equals(installation.Id, sharedInstallationId, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(record, $"Shared installation '{sharedInstallationId}' was not registered.");
        return record.BuildId ?? string.Empty;
    }

    private static string[] GetBuiltInGameKeys()
    {
        string gameNamespace = typeof(GameServer.CSGO).Namespace;
        return typeof(GameServer.CSGO).Assembly.GetTypes()
            .Where(type => string.Equals(type.Namespace, gameNamespace, StringComparison.Ordinal))
            .Select(ReadBuiltInGameKey)
            .Where(gameKey => !string.IsNullOrWhiteSpace(gameKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(gameKey => gameKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadBuiltInGameKey(Type gameType)
    {
        FieldInfo field = gameType.GetField("FullName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field?.FieldType == typeof(string))
        {
            return field.GetValue(null) as string ?? string.Empty;
        }

        PropertyInfo property = gameType.GetProperty("FullName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        return property?.PropertyType == typeof(string) && property.GetMethod?.IsStatic == true
            ? property.GetValue(null) as string ?? string.Empty
            : string.Empty;
    }

    private static bool TryGetSettings(string gameKey, out IntegrationSettings settings, out string skipReason)
    {
        settings = null;
        skipReason = string.Empty;
        string selectedGames = Environment.GetEnvironmentVariable("CDOGS_GAME_INTEGRATION_GAMES");
        if (!string.IsNullOrWhiteSpace(selectedGames) && selectedGames.Trim() != "*")
        {
            string[] selected = selectedGames.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
            if (!selected.Contains(gameKey, StringComparer.OrdinalIgnoreCase))
            {
                skipReason = $"'{gameKey}' was not selected by CDOGS_GAME_INTEGRATION_GAMES.";
                return false;
            }
        }

        string testProjectPath = FindTestProjectDirectory();
        if (testProjectPath == null)
        {
            skipReason = "Could not locate CdogsWindowsGSM.Tests.csproj; set CDOGS_STEAM_CREDENTIALS_FILE to an explicit credentials path.";
            return false;
        }

        string rootPath = Environment.GetEnvironmentVariable("CDOGS_GAME_INTEGRATION_ROOT");
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                localApplicationData = Path.GetTempPath();
            }

            rootPath = Path.Combine(localApplicationData, "CdogsWindowsGSM", "GameIntegration");
        }
        rootPath = Path.GetFullPath(rootPath);

        string repositoryPath = FindRepositoryRoot(testProjectPath);
        if (repositoryPath != null && PathsOverlap(rootPath, repositoryPath))
        {
            throw new InvalidOperationException("The game integration root must be outside the repository so test downloads and credentials cannot be added to source control.");
        }

        string credentialsPath = Environment.GetEnvironmentVariable("CDOGS_STEAM_CREDENTIALS_FILE");
        if (string.IsNullOrWhiteSpace(credentialsPath))
        {
            credentialsPath = Path.Combine(testProjectPath, "steam-credentials.txt");
        }
        credentialsPath = Path.GetFullPath(credentialsPath);
        if (!File.Exists(credentialsPath))
        {
            skipReason = $"Steam credentials file was not found at '{credentialsPath}'. Copy steam-credentials.example.txt to steam-credentials.txt and fill it in.";
            return false;
        }

        settings = new IntegrationSettings(rootPath, credentialsPath);
        return true;
    }

    private static string FindTestProjectDirectory()
    {
        for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CdogsWindowsGSM.Tests.csproj")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string FindRepositoryRoot(string testProjectDirectory)
    {
        for (DirectoryInfo directory = new DirectoryInfo(testProjectDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CdogsWindowsGSM.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool PathsOverlap(string firstPath, string secondPath)
    {
        return IsPathWithin(firstPath, secondPath) || IsPathWithin(secondPath, firstPath);
    }

    private static bool IsPathWithin(string candidatePath, string parentPath)
    {
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        return string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateServerId(string role, string gameKey)
    {
        return "integration-" + role + "-" + GetGameSuffix(gameKey);
    }

    private static string GetGameSuffix(string gameKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(gameKey));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private sealed class GameInstance
    {
        public ServerConfig Config { get; }
        public object GameServer { get; }
        public string RootPath { get; }

        public GameInstance(ServerConfig config, object gameServer, string rootPath)
        {
            Config = config;
            GameServer = gameServer;
            RootPath = rootPath;
        }
    }

    private sealed class IntegrationSettings
    {
        public string RootPath { get; }
        public string CredentialsPath { get; }

        public IntegrationSettings(string rootPath, string credentialsPath)
        {
            RootPath = rootPath;
            CredentialsPath = credentialsPath;
        }
    }

    private sealed class SteamCredentials
    {
        private string Username { get; }
        private string Password { get; }

        private SteamCredentials(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public static bool TryLoad(string path, out SteamCredentials credentials, out string skipReason)
        {
            credentials = null;
            skipReason = string.Empty;
            string username = null;
            string password = null;

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    throw new InvalidDataException("Each Steam credential entry must use key=\"value\" format.");
                }

                string key = line.Substring(0, separator).Trim();
                string rawValue = line.Substring(separator + 1).Trim();
                if (rawValue.Length < 2 || rawValue[0] != '"' || rawValue[rawValue.Length - 1] != '"')
                {
                    throw new InvalidDataException("Steam credential values must be surrounded by double quotes.");
                }

                string value = rawValue.Substring(1, rawValue.Length - 2);
                if (value.Contains('"'))
                {
                    throw new InvalidDataException("Steam credential values cannot contain a double quote.");
                }

                if (string.Equals(key, "steamUser", StringComparison.OrdinalIgnoreCase))
                {
                    if (username != null)
                    {
                        throw new InvalidDataException("The Steam credential file contains duplicate steamUser entries.");
                    }
                    username = value;
                }
                else if (string.Equals(key, "steamPass", StringComparison.OrdinalIgnoreCase))
                {
                    if (password != null)
                    {
                        throw new InvalidDataException("The Steam credential file contains duplicate steamPass entries.");
                    }
                    password = value;
                }
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                skipReason = "Fill in both steamUser and steamPass in steam-credentials.txt before enabling live game tests.";
                return false;
            }

            credentials = new SteamCredentials(username, password);
            return true;
        }

        public string ToSteamCmdUserData()
        {
            return $"steamUser=\"{Username}\"{Environment.NewLine}steamPass=\"{Password}\"";
        }
    }

    private sealed class AsyncVoidOperationContext : SynchronizationContext
    {
        private readonly object _gate = new object();
        private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _operationCount;
        private int _pendingPosts;
        private bool _started;
        private Exception _failure;

        public Task Completion
        {
            get
            {
                lock (_gate)
                {
                    return _started ? _completion.Task : Task.CompletedTask;
                }
            }
        }

        public override void OperationStarted()
        {
            lock (_gate)
            {
                _started = true;
                _operationCount++;
            }
        }

        public override void OperationCompleted()
        {
            lock (_gate)
            {
                _operationCount--;
                CompleteIfReady();
            }
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            lock (_gate)
            {
                _pendingPosts++;
            }

            bool queued = ThreadPool.QueueUserWorkItem(static workItemState =>
            {
                var workItem = (AsyncVoidWorkItem)workItemState;
                SynchronizationContext previousContext = Current;
                SetSynchronizationContext(workItem.Context);
                Exception failure = null;
                try
                {
                    workItem.Callback(workItem.State);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                    workItem.Context.CompletePost(failure);
                }
            }, new AsyncVoidWorkItem(this, callback, state));

            if (!queued)
            {
                CompletePost(new InvalidOperationException("Could not schedule a server configuration continuation."));
            }
        }

        private void CompletePost(Exception failure)
        {
            lock (_gate)
            {
                _failure ??= failure;
                _pendingPosts--;
                CompleteIfReady();
            }
        }

        private void CompleteIfReady()
        {
            if (!_started || _operationCount != 0 || _pendingPosts != 0)
            {
                return;
            }

            if (_failure == null)
            {
                _completion.TrySetResult(null);
            }
            else
            {
                _completion.TrySetException(_failure);
            }
        }

        private sealed class AsyncVoidWorkItem
        {
            public AsyncVoidOperationContext Context { get; }
            public SendOrPostCallback Callback { get; }
            public object State { get; }

            public AsyncVoidWorkItem(AsyncVoidOperationContext context, SendOrPostCallback callback, object state)
            {
                Context = context;
                Callback = callback;
                State = state;
            }
        }
    }
}