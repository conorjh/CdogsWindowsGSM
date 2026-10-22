using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace WindowsGSM.Functions
{
    internal sealed class PluginCompilationCache
    {
        internal const int CurrentFormatVersion = 1;
        private const int LockWaitMilliseconds = 1000;
        private static readonly TimeSpan ObsoleteEntryAge = TimeSpan.FromDays(30);
        private readonly string _rootDirectory;

        public PluginCompilationCache(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
        }

        public byte[] TryRead(string sourceFileName, string fingerprint, string expectedTypeName, List<string> diagnostics)
        {
            CachePaths paths = GetPaths(sourceFileName, fingerprint);
            if (!File.Exists(paths.ManifestPath) && !File.Exists(paths.AssemblyPath)) { return null; }

            try
            {
                using (FileStream cacheLock = TryAcquireLock(paths.LockPath, exclusive: false, LockWaitMilliseconds))
                {
                    if (cacheLock == null)
                    {
                        diagnostics.Add("The plugin cache entry is busy; compiling the plugin source.");
                        return null;
                    }

                    return ReadEntry(paths, fingerprint, expectedTypeName, diagnostics);
                }
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not read the plugin cache entry: {exception.Message}");
                return null;
            }
        }

        public byte[] PublishOrUseExisting(string sourceFileName, string fingerprint, string expectedTypeName, byte[] compiledAssembly, List<string> diagnostics)
        {
            CachePaths paths = GetPaths(sourceFileName, fingerprint);
            string temporaryAssemblyPath = null;
            string temporaryManifestPath = null;

            try
            {
                Directory.CreateDirectory(paths.DirectoryPath);
                using (FileStream cacheLock = TryAcquireLock(paths.LockPath, exclusive: true, LockWaitMilliseconds))
                {
                    if (cacheLock == null)
                    {
                        diagnostics.Add("Could not acquire the plugin cache write lock; the compiled plugin will be used without caching.");
                        return compiledAssembly;
                    }

                    byte[] existingAssembly = ReadEntry(paths, fingerprint, expectedTypeName, diagnostics);
                    if (existingAssembly != null) { return existingAssembly; }

                    DeleteEntryFiles(paths);
                    temporaryAssemblyPath = paths.AssemblyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    temporaryManifestPath = paths.ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllBytes(temporaryAssemblyPath, compiledAssembly);

                    var manifest = new PluginCacheManifest
                    {
                        FormatVersion = CurrentFormatVersion,
                        Fingerprint = fingerprint,
                        ExpectedTypeName = expectedTypeName,
                        AssemblySha256 = ComputeSha256(compiledAssembly)
                    };
                    File.WriteAllText(temporaryManifestPath, JsonConvert.SerializeObject(manifest), new UTF8Encoding(false));

                    File.Move(temporaryAssemblyPath, paths.AssemblyPath);
                    temporaryAssemblyPath = null;
                    File.Move(temporaryManifestPath, paths.ManifestPath);
                    temporaryManifestPath = null;

                    CleanupAbandonedTemporaryFiles(paths, diagnostics);
                    PruneObsoleteEntries(paths, diagnostics);
                    return compiledAssembly;
                }
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not publish the plugin cache entry: {exception.Message}");
                return compiledAssembly;
            }
            finally
            {
                DeleteTemporaryFile(temporaryAssemblyPath, diagnostics);
                DeleteTemporaryFile(temporaryManifestPath, diagnostics);
            }
        }

        public void Invalidate(string sourceFileName, string fingerprint, List<string> diagnostics)
        {
            CachePaths paths = GetPaths(sourceFileName, fingerprint);
            try
            {
                if (!Directory.Exists(paths.DirectoryPath)) { return; }
                using (FileStream cacheLock = TryAcquireLock(paths.LockPath, exclusive: true, LockWaitMilliseconds))
                {
                    if (cacheLock == null)
                    {
                        diagnostics.Add("Could not acquire the plugin cache lock to invalidate a bad entry.");
                        return;
                    }

                    DeleteEntryFiles(paths);
                }
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not invalidate the plugin cache entry: {exception.Message}");
            }
        }

        public static bool ContainsExpectedType(byte[] assemblyBytes, string expectedTypeName)
        {
            using (var stream = new MemoryStream(assemblyBytes, writable: false))
            using (var peReader = new PEReader(stream))
            {
                if (!peReader.HasMetadata) { return false; }

                MetadataReader metadataReader = peReader.GetMetadataReader();
                foreach (TypeDefinitionHandle handle in metadataReader.TypeDefinitions)
                {
                    TypeDefinition definition = metadataReader.GetTypeDefinition(handle);
                    string typeName = metadataReader.GetString(definition.Name);
                    string typeNamespace = metadataReader.GetString(definition.Namespace);
                    string fullName = string.IsNullOrEmpty(typeNamespace) ? typeName : typeNamespace + "." + typeName;
                    if (string.Equals(fullName, expectedTypeName, StringComparison.Ordinal)) { return true; }
                }
            }

            return false;
        }

        private static byte[] ReadEntry(CachePaths paths, string fingerprint, string expectedTypeName, List<string> diagnostics)
        {
            bool hasManifest = File.Exists(paths.ManifestPath);
            bool hasAssembly = File.Exists(paths.AssemblyPath);
            if (!hasManifest && !hasAssembly) { return null; }
            if (!hasManifest || !hasAssembly)
            {
                diagnostics.Add("The plugin cache entry is incomplete; compiling the plugin source.");
                return null;
            }

            try
            {
                PluginCacheManifest manifest = JsonConvert.DeserializeObject<PluginCacheManifest>(File.ReadAllText(paths.ManifestPath));
                if (manifest == null || manifest.FormatVersion != CurrentFormatVersion ||
                    !string.Equals(manifest.Fingerprint, fingerprint, StringComparison.Ordinal) ||
                    !string.Equals(manifest.ExpectedTypeName, expectedTypeName, StringComparison.Ordinal))
                {
                    diagnostics.Add("The plugin cache manifest did not match the current compiler inputs; compiling the plugin source.");
                    return null;
                }

                byte[] assemblyBytes = File.ReadAllBytes(paths.AssemblyPath);
                if (!string.Equals(manifest.AssemblySha256, ComputeSha256(assemblyBytes), StringComparison.Ordinal))
                {
                    diagnostics.Add("The plugin cache assembly failed its integrity check; compiling the plugin source.");
                    return null;
                }
                if (!ContainsExpectedType(assemblyBytes, expectedTypeName))
                {
                    diagnostics.Add("The plugin cache assembly did not contain the expected plugin type; compiling the plugin source.");
                    return null;
                }

                return assemblyBytes;
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"The plugin cache entry could not be validated: {exception.Message}");
                return null;
            }
        }

        private static FileStream TryAcquireLock(string lockPath, bool exclusive, int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            do
            {
                try
                {
                    if (!File.Exists(lockPath))
                    {
                        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                    }

                    return new FileStream(
                        lockPath,
                        FileMode.Open,
                        exclusive ? FileAccess.ReadWrite : FileAccess.Read,
                        exclusive ? FileShare.None : FileShare.Read);
                }
                catch (IOException)
                {
                    if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds) { return null; }
                    Thread.Sleep(25);
                }
            }
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds);

            return null;
        }

        private static void DeleteEntryFiles(CachePaths paths)
        {
            if (File.Exists(paths.ManifestPath)) { File.Delete(paths.ManifestPath); }
            if (File.Exists(paths.AssemblyPath)) { File.Delete(paths.AssemblyPath); }
        }

        private static void DeleteTemporaryFile(string path, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { return; }
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not remove a temporary plugin cache file: {exception.Message}");
            }
        }

        private static void CleanupAbandonedTemporaryFiles(CachePaths currentPaths, List<string> diagnostics)
        {
            DateTime cutoff = DateTime.UtcNow.Subtract(ObsoleteEntryAge);
            try
            {
                foreach (string path in GetFilesForFingerprint(currentPaths.DirectoryPath, currentPaths.Fingerprint)
                    .Where(file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        DeleteTemporaryFile(path, diagnostics);
                    }
                }
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not clean old temporary plugin cache files: {exception.Message}");
            }
        }

        private static void PruneObsoleteEntries(CachePaths currentPaths, List<string> diagnostics)
        {
            DateTime cutoff = DateTime.UtcNow.Subtract(ObsoleteEntryAge);
            try
            {
                string[] files = Directory.GetFiles(currentPaths.DirectoryPath);
                var fingerprints = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in files)
                {
                    if (TryGetFingerprint(file, out string fingerprint) &&
                        !string.Equals(fingerprint, currentPaths.Fingerprint, StringComparison.Ordinal))
                    {
                        fingerprints.Add(fingerprint);
                    }
                }

                foreach (string fingerprint in fingerprints)
                {
                    string[] entryFiles = GetFilesForFingerprint(currentPaths.DirectoryPath, fingerprint);
                    if (entryFiles.Length == 0 || entryFiles.Any(file => File.GetLastWriteTimeUtc(file) >= cutoff)) { continue; }

                    string lockPath = Path.Combine(currentPaths.DirectoryPath, fingerprint + ".lock");
                    using (FileStream cacheLock = TryAcquireLock(lockPath, exclusive: true, timeoutMilliseconds: 100))
                    {
                        if (cacheLock == null) { continue; }

                        entryFiles = GetFilesForFingerprint(currentPaths.DirectoryPath, fingerprint);
                        if (entryFiles.Any(file => File.GetLastWriteTimeUtc(file) >= cutoff)) { continue; }
                        foreach (string entryFile in entryFiles)
                        {
                            if (File.Exists(entryFile)) { File.Delete(entryFile); }
                        }
                    }
                }
            }
            catch (Exception exception) when (IsCacheException(exception))
            {
                diagnostics.Add($"Could not prune obsolete plugin cache entries: {exception.Message}");
            }
        }

        private static string[] GetFilesForFingerprint(string directoryPath, string fingerprint)
        {
            return Directory.GetFiles(directoryPath)
                .Where(path => TryGetFingerprint(path, out string fileFingerprint) &&
                    string.Equals(fileFingerprint, fingerprint, StringComparison.Ordinal))
                .ToArray();
        }

        private static bool TryGetFingerprint(string path, out string fingerprint)
        {
            string fileName = Path.GetFileName(path);
            string extension = Path.GetExtension(fileName);
            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                fingerprint = Path.GetFileNameWithoutExtension(fileName);
                return IsFingerprint(fingerprint);
            }

            int temporarySuffix = fileName.IndexOf(".dll.", StringComparison.OrdinalIgnoreCase);
            if (temporarySuffix < 0)
            {
                temporarySuffix = fileName.IndexOf(".json.", StringComparison.OrdinalIgnoreCase);
            }
            fingerprint = temporarySuffix > 0 ? fileName.Substring(0, temporarySuffix) : string.Empty;
            return IsFingerprint(fingerprint) && fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        private CachePaths GetPaths(string sourceFileName, string fingerprint)
        {
            if (!IsFingerprint(fingerprint))
            {
                throw new ArgumentException("A SHA-256 plugin fingerprint is required.", nameof(fingerprint));
            }

            string sourceKey = ComputeSha256(Encoding.UTF8.GetBytes(sourceFileName));
            string directoryPath = Path.Combine(_rootDirectory, sourceKey);
            return new CachePaths
            {
                DirectoryPath = directoryPath,
                Fingerprint = fingerprint,
                AssemblyPath = Path.Combine(directoryPath, fingerprint + ".dll"),
                ManifestPath = Path.Combine(directoryPath, fingerprint + ".json"),
                LockPath = Path.Combine(directoryPath, fingerprint + ".lock")
            };
        }

        private static string ComputeSha256(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static bool IsFingerprint(string value)
        {
            return value != null && value.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'A' && character <= 'F') ||
                (character >= 'a' && character <= 'f'));
        }

        private static bool IsCacheException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is SecurityException ||
                exception is InvalidDataException ||
                exception is BadImageFormatException ||
                exception is Newtonsoft.Json.JsonException;
        }

        private sealed class CachePaths
        {
            public string DirectoryPath { get; set; }
            public string Fingerprint { get; set; }
            public string AssemblyPath { get; set; }
            public string ManifestPath { get; set; }
            public string LockPath { get; set; }
        }

        private sealed class PluginCacheManifest
        {
            public int FormatVersion { get; set; }
            public string Fingerprint { get; set; }
            public string ExpectedTypeName { get; set; }
            public string AssemblySha256 { get; set; }
        }
    }
}
