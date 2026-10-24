using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    public static class PluginApiLevel
    {
        public const int Legacy = 1;
        public const int CapabilityMethods = 2;
    }

    [Flags]
    public enum WindowsGSMPluginCapability
    {
        None = 0,
        ConfigurationBackup = 1,
        SharedInstallation = 2
    }

    public sealed class PluginCapabilityProfile
    {
        public int Version { get; private set; }
        public WindowsGSMPluginCapability Capabilities { get; private set; }
        internal MethodInfo ConfigOnlyBackupPlanMethod { get; private set; }
        internal MethodInfo InstallSharedBinariesMethod { get; private set; }
        internal MethodInfo InstallConfigOnlyMethod { get; private set; }

        internal PluginCapabilityProfile(
            int version,
            WindowsGSMPluginCapability capabilities,
            MethodInfo configOnlyBackupPlanMethod,
            MethodInfo installSharedBinariesMethod,
            MethodInfo installConfigOnlyMethod)
        {
            Version = version;
            Capabilities = capabilities;
            ConfigOnlyBackupPlanMethod = configOnlyBackupPlanMethod;
            InstallSharedBinariesMethod = installSharedBinariesMethod;
            InstallConfigOnlyMethod = installConfigOnlyMethod;
        }
    }

    public static class PluginCapabilityInspector
    {
        public static PluginCapabilityProfile Inspect(Type pluginType)
        {
            if (pluginType == null)
            {
                throw new ArgumentNullException(nameof(pluginType));
            }
            if (!pluginType.IsClass || pluginType.IsAbstract || !pluginType.IsVisible)
            {
                throw new InvalidDataException("A plugin must declare a visible, non-abstract class.");
            }
            if (pluginType.GetConstructor(new[] { typeof(ServerConfig) }) == null)
            {
                throw new InvalidDataException("A plugin must expose a public constructor that accepts ServerConfig.");
            }

            MethodInfo configOnlyBackupPlanMethod = FindOptionalMethod(
                pluginType,
                "GetConfigOnlyBackupPlan",
                typeof(PluginInstanceContext),
                typeof(PluginBackupPlan));
            MethodInfo installSharedBinariesMethod = FindOptionalMethod(
                pluginType,
                "InstallSharedBinaries",
                typeof(SharedInstallationContext),
                typeof(Task<Process>));
            MethodInfo installConfigOnlyMethod = FindOptionalMethod(
                pluginType,
                "InstallConfigOnly",
                typeof(ServerInstanceContext),
                typeof(Task<SharedInstallationOperationResult>));

            if ((installSharedBinariesMethod == null) != (installConfigOnlyMethod == null))
            {
                throw new InvalidDataException("Shared installation is inferred only when both typed InstallSharedBinaries and InstallConfigOnly methods are present.");
            }

            if (installSharedBinariesMethod != null && configOnlyBackupPlanMethod == null)
            {
                throw new InvalidDataException("A shared-install plugin must also provide GetConfigOnlyBackupPlan(PluginInstanceContext).");
            }

            WindowsGSMPluginCapability capabilities = WindowsGSMPluginCapability.None;
            if (configOnlyBackupPlanMethod != null)
            {
                capabilities |= WindowsGSMPluginCapability.ConfigurationBackup;
            }
            if (installSharedBinariesMethod != null)
            {
                if (!HasStringMember(pluginType, "SharedInstallationAppId") && !HasStringMember(pluginType, "AppId"))
                {
                    throw new InvalidDataException("A shared-install plugin must expose a string SharedInstallationAppId or AppId member.");
                }
                if (!HasStringMember(pluginType, "SharedInstallationExecutableName") && !HasStringMember(pluginType, "StartPath"))
                {
                    throw new InvalidDataException("A shared-install plugin must expose a string SharedInstallationExecutableName or StartPath member.");
                }

                capabilities |= WindowsGSMPluginCapability.SharedInstallation;
            }

            int version = capabilities == WindowsGSMPluginCapability.None
                ? PluginApiLevel.Legacy
                : PluginApiLevel.CapabilityMethods;
            return new PluginCapabilityProfile(
                version,
                capabilities,
                configOnlyBackupPlanMethod,
                installSharedBinariesMethod,
                installConfigOnlyMethod);
        }

        public static PluginBackupPlan GetConfigOnlyBackupPlan(object pluginInstance, PluginInstanceContext context)
        {
            if (pluginInstance == null)
            {
                throw new ArgumentNullException(nameof(pluginInstance));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            PluginCapabilityProfile profile = Inspect(pluginInstance.GetType());
            if (profile.ConfigOnlyBackupPlanMethod == null)
            {
                throw new InvalidDataException("The plugin does not provide GetConfigOnlyBackupPlan(PluginInstanceContext).");
            }

            return (PluginBackupPlan)InvokeMethod(pluginInstance, profile.ConfigOnlyBackupPlanMethod, context);
        }

        public static PluginSharedInstallationAdapter CreateSharedInstallationAdapter(object pluginInstance)
        {
            if (pluginInstance == null)
            {
                throw new ArgumentNullException(nameof(pluginInstance));
            }

            PluginCapabilityProfile profile = Inspect(pluginInstance.GetType());
            if (profile.InstallSharedBinariesMethod == null || profile.InstallConfigOnlyMethod == null)
            {
                throw new InvalidDataException("The plugin does not provide both typed shared-install methods.");
            }

            string appId = ReadStringMember(pluginInstance, "SharedInstallationAppId", "AppId");
            string executableName = ReadStringMember(pluginInstance, "SharedInstallationExecutableName", "StartPath");
            if (string.IsNullOrWhiteSpace(appId) ||
                string.IsNullOrWhiteSpace(executableName) ||
                !string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The plugin returned an invalid shared-install application ID or executable name.");
            }

            return new PluginSharedInstallationAdapter(
                pluginInstance,
                appId,
                executableName,
                profile.InstallSharedBinariesMethod,
                profile.InstallConfigOnlyMethod);
        }

        private static MethodInfo FindOptionalMethod(Type pluginType, string name, Type parameterType, Type returnType)
        {
            MethodInfo[] matches = Array.FindAll(
                pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance),
                method => method.Name == name &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == parameterType);
            if (matches.Length > 1)
            {
                throw new InvalidDataException($"The plugin has multiple public {name} methods with the same typed context.");
            }
            if (matches.Length == 0)
            {
                return null;
            }
            if (matches[0].ReturnType != returnType)
            {
                throw new InvalidDataException($"The plugin method {name}({parameterType.Name}) must return {returnType.Name}.");
            }

            return matches[0];
        }

        private static bool HasStringMember(Type pluginType, string name)
        {
            PropertyInfo property = pluginType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.PropertyType == typeof(string) &&
                property.GetIndexParameters().Length == 0 && property.GetMethod != null && property.GetMethod.IsPublic)
            {
                return true;
            }

            FieldInfo field = pluginType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(string);
        }

        private static string ReadStringMember(object pluginInstance, string primaryName, string fallbackName)
        {
            string value = ReadPublicStringMember(pluginInstance, primaryName);
            return string.IsNullOrWhiteSpace(value)
                ? ReadPublicStringMember(pluginInstance, fallbackName)
                : value;
        }

        private static string ReadPublicStringMember(object pluginInstance, string name)
        {
            Type pluginType = pluginInstance.GetType();
            PropertyInfo property = pluginType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.PropertyType == typeof(string) &&
                property.GetIndexParameters().Length == 0 && property.GetMethod != null && property.GetMethod.IsPublic)
            {
                try
                {
                    return (string)property.GetValue(pluginInstance, null);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }

            FieldInfo field = pluginType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(string)
                ? (string)field.GetValue(pluginInstance)
                : null;
        }

        internal static object InvokeMethod(object pluginInstance, MethodInfo method, object context)
        {
            try
            {
                return method.Invoke(pluginInstance, new[] { context });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }

    public sealed class PluginInstanceContext
    {
        public string ServerId { get; private set; }
        public string GameKey { get; private set; }
        public string SharedInstallationId { get; private set; }
        public IReadOnlyDictionary<string, string> ApprovedRoots { get; private set; }

        public PluginInstanceContext(
            string serverId,
            string gameKey,
            string sharedInstallationId,
            IReadOnlyDictionary<string, string> approvedRoots)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                throw new ArgumentException("A game key is required.", nameof(gameKey));
            }
            if (approvedRoots == null)
            {
                throw new ArgumentNullException(nameof(approvedRoots));
            }

            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> root in approvedRoots)
            {
                if (string.IsNullOrWhiteSpace(root.Key) || string.IsNullOrWhiteSpace(root.Value) || !Path.IsPathRooted(root.Value))
                {
                    throw new ArgumentException("Approved root IDs and absolute paths are required.", nameof(approvedRoots));
                }
                if (roots.ContainsKey(root.Key))
                {
                    throw new ArgumentException($"The approved root ID '{root.Key}' is duplicated.", nameof(approvedRoots));
                }

                roots.Add(root.Key, Path.GetFullPath(root.Value));
            }

            ServerId = serverId;
            GameKey = gameKey;
            SharedInstallationId = sharedInstallationId ?? string.Empty;
            ApprovedRoots = new ReadOnlyDictionary<string, string>(roots);
        }
    }

    public sealed class PluginBackupPlan
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; private set; }
        public IReadOnlyList<ConfigurationBackupPath> Paths { get; private set; }

        public PluginBackupPlan(IEnumerable<ConfigurationBackupPath> paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            var copiedPaths = new List<ConfigurationBackupPath>();
            foreach (ConfigurationBackupPath path in paths)
            {
                if (path == null)
                {
                    throw new ArgumentException("A backup plan cannot contain a null path.", nameof(paths));
                }

                copiedPaths.Add(new ConfigurationBackupPath(path.RootId, path.RelativePath));
            }

            SchemaVersion = CurrentSchemaVersion;
            Paths = new ReadOnlyCollection<ConfigurationBackupPath>(copiedPaths);
        }
    }

    public sealed class SharedInstallationContext
    {
        public string InstallationId { get; private set; }
        public string GameKey { get; private set; }
        public string AppId { get; private set; }
        public string ExecutableName { get; private set; }
        public string InstallationRoot { get; private set; }

        public SharedInstallationContext(
            string installationId,
            string gameKey,
            string appId,
            string executableName,
            string installationRoot)
        {
            if (string.IsNullOrWhiteSpace(installationId))
            {
                throw new ArgumentException("A shared installation ID is required.", nameof(installationId));
            }
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                throw new ArgumentException("A game key is required.", nameof(gameKey));
            }
            if (string.IsNullOrWhiteSpace(appId))
            {
                throw new ArgumentException("An application ID is required.", nameof(appId));
            }
            if (string.IsNullOrWhiteSpace(executableName) ||
                !string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal))
            {
                throw new ArgumentException("An executable file name is required.", nameof(executableName));
            }
            if (string.IsNullOrWhiteSpace(installationRoot) || !Path.IsPathRooted(installationRoot))
            {
                throw new ArgumentException("An absolute installation root is required.", nameof(installationRoot));
            }

            InstallationId = installationId;
            GameKey = gameKey;
            AppId = appId;
            ExecutableName = executableName;
            InstallationRoot = Path.GetFullPath(installationRoot);
        }
    }

    public sealed class ServerInstanceContext
    {
        public string ServerId { get; private set; }
        public string GameKey { get; private set; }
        public string SharedInstallationId { get; private set; }
        public string InstallationRoot { get; private set; }
        public string InstanceRoot { get; private set; }

        public ServerInstanceContext(
            string serverId,
            string gameKey,
            string sharedInstallationId,
            string installationRoot,
            string instanceRoot)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                throw new ArgumentException("A game key is required.", nameof(gameKey));
            }
            if (string.IsNullOrWhiteSpace(sharedInstallationId))
            {
                throw new ArgumentException("A shared installation ID is required.", nameof(sharedInstallationId));
            }
            if (string.IsNullOrWhiteSpace(installationRoot) || !Path.IsPathRooted(installationRoot))
            {
                throw new ArgumentException("An absolute installation root is required.", nameof(installationRoot));
            }
            if (string.IsNullOrWhiteSpace(instanceRoot) || !Path.IsPathRooted(instanceRoot))
            {
                throw new ArgumentException("An absolute instance root is required.", nameof(instanceRoot));
            }

            string fullInstallationRoot = Path.GetFullPath(installationRoot);
            string fullInstanceRoot = Path.GetFullPath(instanceRoot);

            ServerId = serverId;
            GameKey = gameKey;
            SharedInstallationId = sharedInstallationId;
            InstallationRoot = fullInstallationRoot;
            InstanceRoot = fullInstanceRoot;
        }
    }

    public enum SharedInstallationOperationStatus
    {
        Succeeded,
        Started,
        Cancelled,
        Failed
    }

    public sealed class SharedInstallationOperationResult
    {
        public SharedInstallationOperationStatus Status { get; private set; }
        public Process Process { get; private set; }
        public string ErrorMessage { get; private set; }

        private SharedInstallationOperationResult(
            SharedInstallationOperationStatus status,
            Process process,
            string errorMessage)
        {
            Status = status;
            Process = process;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static SharedInstallationOperationResult Succeeded()
        {
            return new SharedInstallationOperationResult(SharedInstallationOperationStatus.Succeeded, null, string.Empty);
        }

        public static SharedInstallationOperationResult Started(Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            return new SharedInstallationOperationResult(SharedInstallationOperationStatus.Started, process, string.Empty);
        }

        public static SharedInstallationOperationResult Cancelled(string errorMessage)
        {
            return CreateFailure(SharedInstallationOperationStatus.Cancelled, errorMessage, nameof(errorMessage));
        }

        public static SharedInstallationOperationResult Failed(string errorMessage)
        {
            return CreateFailure(SharedInstallationOperationStatus.Failed, errorMessage, nameof(errorMessage));
        }

        private static SharedInstallationOperationResult CreateFailure(
            SharedInstallationOperationStatus status,
            string errorMessage,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new ArgumentException("A cancellation or failure message is required.", parameterName);
            }

            return new SharedInstallationOperationResult(status, null, errorMessage);
        }
    }

    public sealed class PluginSharedInstallationAdapter
    {
        private readonly object _pluginInstance;
        private readonly MethodInfo _installSharedBinariesMethod;
        private readonly MethodInfo _installConfigOnlyMethod;

        public string AppId { get; private set; }
        public string ExecutableName { get; private set; }

        internal PluginSharedInstallationAdapter(
            object pluginInstance,
            string appId,
            string executableName,
            MethodInfo installSharedBinariesMethod,
            MethodInfo installConfigOnlyMethod)
        {
            _pluginInstance = pluginInstance;
            AppId = appId;
            ExecutableName = executableName;
            _installSharedBinariesMethod = installSharedBinariesMethod;
            _installConfigOnlyMethod = installConfigOnlyMethod;
        }

        public Task<Process> InstallSharedBinaries(SharedInstallationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return InvokeTask<Process>(
                _pluginInstance,
                _installSharedBinariesMethod,
                context);
        }

        public Task<SharedInstallationOperationResult> InstallConfigOnly(ServerInstanceContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return InvokeTask<SharedInstallationOperationResult>(
                _pluginInstance,
                _installConfigOnlyMethod,
                context);
        }

        private static Task<T> InvokeTask<T>(object pluginInstance, MethodInfo method, object context)
        {
            Task<T> operation = PluginCapabilityInspector.InvokeMethod(pluginInstance, method, context) as Task<T>;
            if (operation == null)
            {
                throw new InvalidDataException($"The plugin method {method.Name} returned no task.");
            }

            return operation;
        }
    }
}