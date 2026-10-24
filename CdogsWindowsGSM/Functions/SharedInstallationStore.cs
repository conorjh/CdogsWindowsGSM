using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WindowsGSM.Localization;

namespace WindowsGSM.Functions
{
    public sealed class SharedInstallationResolution : IDisposable
    {
        private FileStream _installLease;

        public string InstallationId { get; }
        public bool Created { get; }

        internal SharedInstallationResolution(string installationId, bool created, FileStream installLease = null)
        {
            InstallationId = installationId;
            Created = created;
            _installLease = installLease;
        }

        public void Dispose()
        {
            FileStream installLease = _installLease;
            _installLease = null;
            installLease?.Dispose();
        }
    }

    public sealed class SharedInstallationRecord
    {
        public string Id { get; set; }
        public string GameKey { get; set; }
        public string AppId { get; set; }
        public string OwnerServerId { get; set; }
        public int CapabilityVersion { get; set; }
        public string BuildId { get; set; }
        public string State { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public static class SharedInstallationStore
    {
        private const string InstallingState = "Installing";
        private const string ReadyState = "Ready";
        private const string FailedState = "Failed";

        public static string GetServerBinaryRoot(ServerConfig serverConfig)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }

            return string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId)
                ? ServerPath.GetServersServerFiles(serverConfig.ServerID)
                : GetServerBinaryRoot(serverConfig.SharedInstallationId);
        }

        public static string GetServerBinaryRoot(string installationId)
        {
            ServerPath.GetSharedInstallationRoot(installationId);
            using (AcquireRegistryLock())
            {
                SharedInstallationRecord record = ReadRegistry().Installations
                    .SingleOrDefault(item => string.Equals(item.Id, installationId, StringComparison.OrdinalIgnoreCase));
                if (record == null)
                {
                    throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationNotReady"));
                }

                return GetServerBinaryRoot(record);
            }
        }

        public static bool IsServerFilesRootOwner(string serverId, string installationId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }

            ServerPath.GetSharedInstallationRoot(installationId);
            using (AcquireRegistryLock())
            {
                SharedInstallationRecord record = ReadRegistry().Installations
                    .SingleOrDefault(item => string.Equals(item.Id, installationId, StringComparison.OrdinalIgnoreCase));
                return record != null && string.Equals(record.OwnerServerId, serverId, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsServerFilesRootOwner(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }

            using (AcquireRegistryLock())
            {
                return ReadRegistry().Installations.Any(record =>
                    string.Equals(record.OwnerServerId, serverId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static FileStream AcquireReadLeaseForServer(ServerConfig serverConfig)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }

            if (string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
            {
                return null;
            }

            EnsureReady(serverConfig.SharedInstallationId);
            return AcquireReadLease(serverConfig.SharedInstallationId);
        }

        public static FileStream AcquireExclusiveLeaseForServer(ServerConfig serverConfig)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }

            if (string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
            {
                return null;
            }

            EnsureReady(serverConfig.SharedInstallationId);
            return AcquireExclusiveLease(serverConfig.SharedInstallationId);
        }

        public static SharedInstallationResolution ResolveOrCreate(
            string ownerServerId,
            string gameKey,
            string appId,
            string executableName,
            int capabilityVersion = 1)
        {
            if (!IsValidServerId(ownerServerId))
            {
                throw new ArgumentException("A valid owner server ID is required.", nameof(ownerServerId));
            }
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                throw new ArgumentException("A game key is required.", nameof(gameKey));
            }
            if (string.IsNullOrWhiteSpace(appId))
            {
                throw new ArgumentException("A Steam application ID is required.", nameof(appId));
            }
            if (capabilityVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capabilityVersion));
            }
            if (string.IsNullOrWhiteSpace(executableName) || !string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal))
            {
                throw new ArgumentException("A file name is required.", nameof(executableName));
            }

            using (AcquireRegistryLock())
            {
                Registry registry = ReadRegistry();
                List<SharedInstallationRecord> matching = registry.Installations
                    .Where(record => string.Equals(record.GameKey, gameKey, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(record.AppId, appId, StringComparison.Ordinal)
                        && record.CapabilityVersion == capabilityVersion)
                    .ToList();

                bool recoveredIncompleteInstall = false;
                foreach (SharedInstallationRecord installing in matching.Where(record => string.Equals(record.State, InstallingState, StringComparison.Ordinal)))
                {
                    if (Directory.Exists(GetServerBinaryRoot(installing)))
                    {
                        try
                        {
                            using (AcquireExclusiveLease(installing.Id))
                            {
                                installing.State = FailedState;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationInstallInProgress"));
                        }
                    }
                    else
                    {
                        installing.State = FailedState;
                    }

                    recoveredIncompleteInstall = true;
                }

                if (recoveredIncompleteInstall)
                {
                    WriteRegistry(registry);
                }

                List<SharedInstallationRecord> ready = matching
                    .Where(record => string.Equals(record.State, ReadyState, StringComparison.Ordinal))
                    .ToList();
                if (ready.Count > 1)
                {
                    throw new InvalidDataException(LocalizationManager.Get("SharedInstallationMultipleReady"));
                }
                if (ready.Count == 1)
                {
                    SharedInstallationRecord existing = ready[0];
                    string existingServerFiles = GetServerBinaryRoot(existing);
                    if (!File.Exists(Path.Combine(existingServerFiles, executableName)))
                    {
                        throw new InvalidDataException(LocalizationManager.Get("SharedInstallationMissingExecutable"));
                    }

                    return new SharedInstallationResolution(existing.Id, false, AcquireReadLease(existing.Id));
                }

                string installationId = Guid.NewGuid().ToString("N");
                string serverFilesPath = ServerPath.GetServersServerFiles(ownerServerId);
                Directory.CreateDirectory(serverFilesPath);
                FileStream installLease = AcquireExclusiveLease(installationId);
                bool leaseTransferred = false;
                try
                {
                    registry.Installations.Add(new SharedInstallationRecord
                    {
                        Id = installationId,
                        GameKey = gameKey,
                        AppId = appId,
                        OwnerServerId = ownerServerId,
                        CapabilityVersion = capabilityVersion,
                        State = InstallingState,
                        CreatedUtc = DateTime.UtcNow
                    });
                    WriteRegistry(registry);
                    leaseTransferred = true;
                    return new SharedInstallationResolution(installationId, true, installLease);
                }
                finally
                {
                    if (!leaseTransferred)
                    {
                        installLease.Dispose();
                    }
                }
            }
        }

        public static void EnsureReady(string installationId)
        {
            using (AcquireRegistryLock())
            {
                SharedInstallationRecord record = ReadRegistry().Installations
                    .SingleOrDefault(item => string.Equals(item.Id, installationId, StringComparison.OrdinalIgnoreCase));
                if (record == null || !string.Equals(record.State, ReadyState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationNotReady"));
                }
            }
        }

        public static List<SharedInstallationRecord> GetInstallations()
        {
            using (AcquireRegistryLock())
            {
                return ReadRegistry().Installations
                    .OrderByDescending(record => record.CreatedUtc)
                    .Select(record => new SharedInstallationRecord
                    {
                        Id = record.Id,
                        GameKey = record.GameKey,
                        AppId = record.AppId,
                        OwnerServerId = record.OwnerServerId,
                        CapabilityVersion = record.CapabilityVersion,
                        BuildId = record.BuildId,
                        State = record.State,
                        CreatedUtc = record.CreatedUtc
                    })
                    .ToList();
            }
        }

        public static bool DeleteIfUnreferenced(string installationId)
        {
            ServerPath.GetSharedInstallationRoot(installationId);
            using (AcquireRegistryLock())
            {
                Registry registry = ReadRegistry();
                SharedInstallationRecord record = registry.Installations
                    .SingleOrDefault(item => string.Equals(item.Id, installationId, StringComparison.OrdinalIgnoreCase));
                if (record == null || HasServerReference(installationId))
                {
                    return false;
                }

                string installationRoot = GetServerBinaryRoot(record);
                FileStream installationLease = null;
                try
                {
                    installationLease = AcquireExclusiveLease(installationId);

                    if (HasServerReference(installationId))
                    {
                        return false;
                    }

                    if (!string.Equals(record.State, FailedState, StringComparison.Ordinal))
                    {
                        record.State = FailedState;
                        record.BuildId = null;
                        WriteRegistry(registry);
                    }

                    installationLease?.Dispose();
                    installationLease = null;

                    if (Directory.Exists(installationRoot))
                    {
                        Directory.Delete(installationRoot, true);
                    }

                    if (!string.IsNullOrWhiteSpace(record.OwnerServerId))
                    {
                        string ownerRoot = ServerPath.GetServers(record.OwnerServerId);
                        if (Directory.Exists(ownerRoot) && !Directory.EnumerateFileSystemEntries(ownerRoot).Any())
                        {
                            Directory.Delete(ownerRoot);
                        }
                    }

                    registry.Installations.Remove(record);
                    WriteRegistry(registry);
                    return true;
                }
                finally
                {
                    installationLease?.Dispose();
                }
            }
        }

        public static List<string> GetLinkedServerIds(string installationId)
        {
            ServerPath.GetSharedInstallationRoot(installationId);
            string serversRoot = ServerPath.Get(ServerPath.FolderName.Servers);
            if (!Directory.Exists(serversRoot))
            {
                return new List<string>();
            }

            var linkedServerIds = new List<string>();
            foreach (string serverDirectory in Directory.GetDirectories(serversRoot))
            {
                string configPath = Path.Combine(serverDirectory, ServerPath.FolderName.Configs, "WindowsGSM.cfg");
                if (!File.Exists(configPath))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(configPath))
                {
                    int separator = line.IndexOf('=');
                    if (separator < 0 || !string.Equals(
                        line.Substring(0, separator).Trim(),
                        ServerConfig.SettingName.SharedInstallationId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string configuredId = line.Substring(separator + 1).Trim();
                    if (configuredId.Length >= 2 && configuredId[0] == '"' && configuredId[configuredId.Length - 1] == '"')
                    {
                        configuredId = configuredId.Substring(1, configuredId.Length - 2);
                    }

                    if (string.Equals(configuredId, installationId, StringComparison.OrdinalIgnoreCase))
                    {
                        linkedServerIds.Add(Path.GetFileName(serverDirectory));
                        break;
                    }
                }
            }

            return linkedServerIds
                .OrderBy(serverId => int.TryParse(serverId, out int numericId) ? numericId : int.MaxValue)
                .ThenBy(serverId => serverId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void MarkReady(string installationId, string buildId = null)
        {
            SetState(installationId, ReadyState, buildId);
        }

        public static void MarkFailed(string installationId)
        {
            SetState(installationId, FailedState, null);
        }

        public static FileStream AcquireReadLease(string installationId)
        {
            string lockPath = ServerPath.GetSharedInstallationLockFile(installationId);
            try
            {
                if (!File.Exists(lockPath))
                {
                    using (AcquireExclusiveLease(installationId))
                    {
                    }
                }

                return new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationUpdateInProgress"), exception);
            }
        }

        public static FileStream AcquireExclusiveLease(string installationId)
        {
            string lockPath = ServerPath.GetSharedInstallationLockFile(installationId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationInUse"), exception);
            }
        }

        public static void ReleaseLeaseWhenProcessExits(Process process, FileStream lease)
        {
            process.EnableRaisingEvents = true;
            EventHandler exitedHandler = null;
            exitedHandler = (sender, eventArgs) =>
            {
                process.Exited -= exitedHandler;
                lease.Dispose();
            };

            process.Exited += exitedHandler;
            if (process.HasExited)
            {
                exitedHandler(process, EventArgs.Empty);
            }
        }

        private static void SetState(string installationId, string state, string buildId)
        {
            using (AcquireRegistryLock())
            {
                Registry registry = ReadRegistry();
                SharedInstallationRecord record = registry.Installations
                    .SingleOrDefault(item => string.Equals(item.Id, installationId, StringComparison.OrdinalIgnoreCase));
                if (record == null || !string.Equals(record.State, InstallingState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationStateInvalid"));
                }

                record.State = state;
                record.BuildId = buildId;
                WriteRegistry(registry);
            }
        }

        private static FileStream AcquireRegistryLock()
        {
            string lockPath = ServerPath.GetSharedInstallationsRegistryLock();
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(LocalizationManager.Get("SharedInstallationRegistryBusy"), exception);
            }
        }

        private static bool HasServerReference(string installationId)
        {
            return GetLinkedServerIds(installationId).Count > 0;
        }

        private static Registry ReadRegistry()
        {
            string registryPath = ServerPath.GetSharedInstallationsRegistry();
            if (!File.Exists(registryPath))
            {
                return new Registry { SchemaVersion = 1 };
            }

            Registry registry;
            try
            {
                registry = JsonConvert.DeserializeObject<Registry>(File.ReadAllText(registryPath));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(LocalizationManager.Get("SharedInstallationRegistryInvalid"), exception);
            }

            if (registry == null || registry.SchemaVersion != 1 || registry.Installations == null)
            {
                throw new InvalidDataException(LocalizationManager.Get("SharedInstallationRegistryInvalid"));
            }

            var installationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SharedInstallationRecord record in registry.Installations)
            {
                if (record == null || !Guid.TryParseExact(record.Id, "N", out _) ||
                    string.IsNullOrWhiteSpace(record.GameKey) || string.IsNullOrWhiteSpace(record.AppId) ||
                    (!string.IsNullOrWhiteSpace(record.OwnerServerId) && !IsValidServerId(record.OwnerServerId)) ||
                    record.CapabilityVersion < 1 || record.CreatedUtc == default(DateTime) ||
                    !(string.Equals(record.State, InstallingState, StringComparison.Ordinal) ||
                      string.Equals(record.State, ReadyState, StringComparison.Ordinal) ||
                      string.Equals(record.State, FailedState, StringComparison.Ordinal)) ||
                    !installationIds.Add(record.Id))
                {
                    throw new InvalidDataException(LocalizationManager.Get("SharedInstallationRegistryInvalid"));
                }
            }

            return registry;
        }

        private static string GetServerBinaryRoot(SharedInstallationRecord record)
        {
            return string.IsNullOrWhiteSpace(record.OwnerServerId)
                ? ServerPath.GetSharedInstallationServerFiles(record.Id)
                : ServerPath.GetServersServerFiles(record.OwnerServerId);
        }

        private static bool IsValidServerId(string serverId)
        {
            return !string.IsNullOrWhiteSpace(serverId) &&
                !string.Equals(serverId, ".", StringComparison.Ordinal) &&
                !string.Equals(serverId, "..", StringComparison.Ordinal) &&
                string.Equals(Path.GetFileName(serverId), serverId, StringComparison.Ordinal);
        }

        private static void WriteRegistry(Registry registry)
        {
            string registryPath = ServerPath.GetSharedInstallationsRegistry();
            string temporaryPath = registryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(registry, Formatting.Indented));
            if (File.Exists(registryPath))
            {
                File.Replace(temporaryPath, registryPath, null);
            }
            else
            {
                File.Move(temporaryPath, registryPath);
            }
        }

        private sealed class Registry
        {
            public int SchemaVersion { get; set; }
            public List<SharedInstallationRecord> Installations { get; set; } = new List<SharedInstallationRecord>();
        }
    }
}