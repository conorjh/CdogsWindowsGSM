using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace WindowsGSM.Functions
{
    public sealed class ConfigurationBackupPath
    {
        public string RootId { get; set; }
        public string RelativePath { get; set; }

        public ConfigurationBackupPath()
        {
        }

        public ConfigurationBackupPath(string rootId, string relativePath)
        {
            RootId = rootId;
            RelativePath = relativePath;
        }
    }

    public sealed class ConfigurationBackupManifestEntry
    {
        public string RootId { get; set; }
        public string RelativePath { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class ConfigurationBackupManifest
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string BackupType { get; set; } = "Configuration";
        public string ServerId { get; set; }
        public string GameKey { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SharedInstallationId { get; set; } = string.Empty;
        public string SharedInstallationBuildId { get; set; } = string.Empty;
        public List<ConfigurationBackupManifestEntry> Files { get; set; } = new List<ConfigurationBackupManifestEntry>();
    }

    public static class ConfigurationBackupArchive
    {
        public const string FileExtension = ".wgsmbackup";
        private const string ManifestEntryName = "manifest.json";
        private const int MaximumManifestBytes = 1024 * 1024;
        private const int MaximumFileCount = 1000;
        private const long MaximumFileBytes = 64L * 1024 * 1024;
        private const long MaximumTotalBytes = 256L * 1024 * 1024;

        public static List<ConfigurationBackupPath> ValidatePlan(
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> paths)
        {
            Dictionary<string, string> normalizedRoots = NormalizeRoots(roots);
            List<ResolvedPath> resolvedPaths = NormalizePaths(paths, normalizedRoots);
            ValidatePlan(resolvedPaths);
            return resolvedPaths
                .Select(path => new ConfigurationBackupPath(path.RootId, path.RelativePath))
                .ToList();
        }

        internal static bool HasExistingFiles(
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> paths)
        {
            Dictionary<string, string> normalizedRoots = NormalizeRoots(roots);
            List<ResolvedPath> resolvedPaths = NormalizePaths(paths, normalizedRoots);
            ValidatePlan(resolvedPaths);
            return resolvedPaths.Any(path => File.Exists(path.FullPath));
        }

        public static ConfigurationBackupResult Create(
            string archivePath,
            string serverId,
            string gameKey,
            string sharedInstallationId,
            string sharedInstallationBuildId,
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> paths)
        {
            ValidateIdentity(serverId, gameKey);
            string installationId = NormalizeSharedInstallationId(sharedInstallationId);
            if (string.IsNullOrWhiteSpace(archivePath) ||
                !string.Equals(Path.GetExtension(archivePath), FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Configuration backup files must use the {FileExtension} extension.", nameof(archivePath));
            }

            Dictionary<string, string> normalizedRoots = NormalizeRoots(roots);
            List<ResolvedPath> resolvedPaths = NormalizePaths(paths, normalizedRoots);
            ValidatePlan(resolvedPaths);

            var manifest = new ConfigurationBackupManifest
            {
                ServerId = serverId,
                GameKey = gameKey,
                CreatedUtc = DateTime.UtcNow,
                SharedInstallationId = installationId,
                SharedInstallationBuildId = sharedInstallationBuildId ?? string.Empty
            };
            foreach (ResolvedPath path in resolvedPaths)
            {
                EnsureSourceFileExists(path.RootPath, path.RelativePath, path.FullPath);
            }

            string fullArchivePath = Path.GetFullPath(archivePath);
            string archiveDirectory = Path.GetDirectoryName(fullArchivePath);
            if (string.IsNullOrWhiteSpace(archiveDirectory))
            {
                throw new ArgumentException("The configuration backup path must have a parent directory.", nameof(archivePath));
            }

            Directory.CreateDirectory(archiveDirectory);
            if (File.Exists(fullArchivePath) || Directory.Exists(fullArchivePath))
            {
                throw new IOException("The configuration backup file already exists.");
            }

            string temporaryPath = fullArchivePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            long sourceBytes = 0;
            try
            {
                using (ZipArchive archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
                {
                    foreach (ResolvedPath path in resolvedPaths)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(path.ArchivePath, CompressionLevel.Optimal);
                        using (Stream source = new FileStream(path.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (Stream destination = entry.Open())
                        {
                            string hash = CopyAndHash(source, destination, out long length);
                            if (length > MaximumFileBytes || sourceBytes + length > MaximumTotalBytes)
                            {
                                throw new InvalidDataException("The configuration backup exceeds the maximum supported size.");
                            }
                            sourceBytes += length;
                            manifest.Files.Add(new ConfigurationBackupManifestEntry
                            {
                                RootId = path.RootId,
                                RelativePath = path.RelativePath,
                                Length = length,
                                Sha256 = hash
                            });
                        }
                    }

                    ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                    using (Stream stream = manifestEntry.Open())
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
                    }
                }

                File.Move(temporaryPath, fullArchivePath);
                return new ConfigurationBackupResult
                {
                    ArchivePath = fullArchivePath,
                    SourceBytes = sourceBytes,
                    ArchiveBytes = new FileInfo(fullArchivePath).Length,
                    IncludedPaths = manifest.Files.Select(file => new ConfigurationBackupPath(file.RootId, file.RelativePath)).ToList()
                };
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static List<string> GetRestorePreview(
            string archivePath,
            string serverId,
            string gameKey,
            string sharedInstallationId,
            string sharedInstallationBuildId,
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> allowedPaths)
        {
            ValidateArchiveExtension(archivePath);
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                ValidatedArchive validated = ReadAndValidateArchive(
                    archive,
                    serverId,
                    gameKey,
                    sharedInstallationId,
                    sharedInstallationBuildId,
                    roots,
                    allowedPaths);
                return validated.Files
                    .Select(file => file.RootId + "/" + file.RelativePath)
                    .ToList();
            }
        }

        public static List<string> Restore(
            string archivePath,
            string serverId,
            string gameKey,
            string sharedInstallationId,
            string sharedInstallationBuildId,
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> allowedPaths)
        {
            ValidateArchiveExtension(archivePath);
            string stagingRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM-ConfigRestore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            Exception operationFailure = null;
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                {
                    ValidatedArchive validated = ReadAndValidateArchive(
                        archive,
                        serverId,
                        gameKey,
                        sharedInstallationId,
                        sharedInstallationBuildId,
                        roots,
                        allowedPaths);
                    StageAndApply(archive, validated, stagingRoot);
                    return validated.Files
                        .Select(file => file.RootId + "/" + file.RelativePath)
                        .ToList();
                }
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingRoot))
                    {
                        Directory.Delete(stagingRoot, true);
                    }
                }
                catch (Exception cleanupException) when (IsExpectedFileException(cleanupException))
                {
                    if (operationFailure != null)
                    {
                        throw new AggregateException("Configuration restore failed and its temporary files could not be fully removed.", operationFailure, cleanupException);
                    }

                    throw new IOException("Configuration restore completed, but its temporary files could not be fully removed.", cleanupException);
                }
            }
        }

        private static ValidatedArchive ReadAndValidateArchive(
            ZipArchive archive,
            string serverId,
            string gameKey,
            string sharedInstallationId,
            string sharedInstallationBuildId,
            IReadOnlyDictionary<string, string> roots,
            IEnumerable<ConfigurationBackupPath> allowedPaths)
        {
            ValidateIdentity(serverId, gameKey);
            Dictionary<string, string> normalizedRoots = NormalizeRoots(roots);
            List<ResolvedPath> approvedPaths = NormalizePaths(allowedPaths, normalizedRoots);
            var approvedDestinations = new HashSet<string>(approvedPaths.Select(path => path.DestinationKey), StringComparer.OrdinalIgnoreCase);
            var zipEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.Contains("\\"))
                {
                    throw new InvalidDataException("The configuration backup contains an invalid ZIP entry path.");
                }
                if (zipEntries.ContainsKey(entry.FullName))
                {
                    throw new InvalidDataException($"The configuration backup contains a duplicate ZIP entry: {entry.FullName}.");
                }

                zipEntries.Add(entry.FullName, entry);
            }

            if (!zipEntries.TryGetValue(ManifestEntryName, out ZipArchiveEntry manifestEntry) || manifestEntry.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("The configuration backup manifest is missing or too large.");
            }

            ConfigurationBackupManifest manifest;
            try
            {
                using (Stream stream = manifestEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    manifest = JsonConvert.DeserializeObject<ConfigurationBackupManifest>(reader.ReadToEnd());
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The configuration backup manifest is invalid.", exception);
            }

            if (manifest == null || manifest.SchemaVersion != ConfigurationBackupManifest.CurrentSchemaVersion ||
                !string.Equals(manifest.BackupType, "Configuration", StringComparison.Ordinal) ||
                !string.Equals(manifest.ServerId, serverId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manifest.GameKey, gameKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(NormalizeSharedInstallationId(manifest.SharedInstallationId), NormalizeSharedInstallationId(sharedInstallationId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manifest.SharedInstallationBuildId ?? string.Empty, sharedInstallationBuildId ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The configuration backup target, installation, or schema does not match this server.");
            }

            if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > MaximumFileCount)
            {
                throw new InvalidDataException("The configuration backup manifest does not contain a valid file list.");
            }

            var validatedFiles = new List<ValidatedFile>();
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expectedArchiveEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntryName };
            long totalBytes = 0;
            foreach (ConfigurationBackupManifestEntry file in manifest.Files)
            {
                if (file == null || file.Length < 0 || file.Length > MaximumFileBytes || !IsSha256(file.Sha256))
                {
                    throw new InvalidDataException("The configuration backup manifest contains an invalid file record.");
                }

                totalBytes += file.Length;
                if (totalBytes > MaximumTotalBytes)
                {
                    throw new InvalidDataException("The configuration backup exceeds the maximum supported size.");
                }

                ResolvedPath resolvedPath = NormalizePath(file.RootId, file.RelativePath, normalizedRoots);
                if (!approvedDestinations.Contains(resolvedPath.DestinationKey) || !destinations.Add(resolvedPath.DestinationKey))
                {
                    throw new InvalidDataException($"The configuration backup includes a path that is not approved for this server: {resolvedPath.RootId}/{resolvedPath.RelativePath}.");
                }

                string entryPath = resolvedPath.ArchivePath;
                if (!expectedArchiveEntries.Add(entryPath) || !zipEntries.TryGetValue(entryPath, out ZipArchiveEntry dataEntry))
                {
                    throw new InvalidDataException($"The configuration backup is missing or duplicates an archived file: {entryPath}.");
                }
                if (dataEntry.Length != file.Length)
                {
                    throw new InvalidDataException($"The archived file length does not match its manifest: {entryPath}.");
                }

                EnsureSafeDestination(resolvedPath.RootPath, resolvedPath.RelativePath, false);
                validatedFiles.Add(new ValidatedFile
                {
                    RootId = resolvedPath.RootId,
                    RelativePath = resolvedPath.RelativePath,
                    RootPath = resolvedPath.RootPath,
                    DestinationPath = resolvedPath.FullPath,
                    ArchivePath = entryPath,
                    ZipEntry = dataEntry,
                    Length = file.Length,
                    Sha256 = file.Sha256
                });
            }

            if (zipEntries.Keys.Any(entryPath => !expectedArchiveEntries.Contains(entryPath)))
            {
                throw new InvalidDataException("The configuration backup contains files not listed in its manifest.");
            }

            return new ValidatedArchive { Files = validatedFiles };
        }

        private static void StageAndApply(ZipArchive archive, ValidatedArchive validated, string stagingRoot)
        {
            string filesRoot = Path.Combine(stagingRoot, "files");
            string rollbackRoot = Path.Combine(stagingRoot, "rollback");
            Directory.CreateDirectory(filesRoot);
            Directory.CreateDirectory(rollbackRoot);

            foreach (ValidatedFile file in validated.Files)
            {
                string stagedPath = GetContainedPath(filesRoot, file.RootId + "/" + file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                using (Stream input = file.ZipEntry.Open())
                using (Stream output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    string hash = CopyAndHash(input, output, out long length);
                    if (length != file.Length || !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"The archived file failed integrity validation: {file.ArchivePath}.");
                    }
                }

                file.StagedPath = stagedPath;
                EnsureSafeDestination(file.RootPath, file.RelativePath, false);
                file.ExistedBeforeRestore = File.Exists(file.DestinationPath);
                if (file.ExistedBeforeRestore)
                {
                    string rollbackPath = GetContainedPath(rollbackRoot, file.RootId + "/" + file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(rollbackPath));
                    File.Copy(file.DestinationPath, rollbackPath, false);
                    file.RollbackPath = rollbackPath;
                }
            }

            var appliedFiles = new List<ValidatedFile>();
            try
            {
                foreach (ValidatedFile file in validated.Files)
                {
                    EnsureSafeDestination(file.RootPath, file.RelativePath, true);
                    appliedFiles.Add(file);
                    PublishFile(file.StagedPath, file.DestinationPath);
                }
            }
            catch (Exception restoreException)
            {
                var rollbackErrors = new List<Exception>();
                foreach (ValidatedFile file in appliedFiles.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (file.ExistedBeforeRestore)
                        {
                            PublishFile(file.RollbackPath, file.DestinationPath);
                        }
                        else if (File.Exists(file.DestinationPath))
                        {
                            File.Delete(file.DestinationPath);
                        }
                    }
                    catch (Exception rollbackException) when (IsExpectedFileException(rollbackException))
                    {
                        rollbackErrors.Add(rollbackException);
                    }
                }

                if (rollbackErrors.Count > 0)
                {
                    rollbackErrors.Insert(0, restoreException);
                    throw new AggregateException("Configuration restore failed and rollback was incomplete.", rollbackErrors);
                }

                throw new IOException("Configuration restore failed; all files changed by the operation were rolled back.", restoreException);
            }
        }

        private static Dictionary<string, string> NormalizeRoots(IReadOnlyDictionary<string, string> roots)
        {
            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }

            var normalizedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> root in roots)
            {
                string rootId = NormalizeRootId(root.Key);
                if (string.IsNullOrWhiteSpace(root.Value))
                {
                    throw new ArgumentException($"The backup root '{rootId}' has no path.", nameof(roots));
                }
                if (normalizedRoots.ContainsKey(rootId))
                {
                    throw new ArgumentException($"The backup root ID '{rootId}' is duplicated.", nameof(roots));
                }

                normalizedRoots.Add(rootId, Path.GetFullPath(root.Value));
            }

            return normalizedRoots;
        }

        private static List<ResolvedPath> NormalizePaths(IEnumerable<ConfigurationBackupPath> paths, Dictionary<string, string> roots)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            var normalizedPaths = new List<ResolvedPath>();
            foreach (ConfigurationBackupPath path in paths)
            {
                if (path == null)
                {
                    throw new InvalidDataException("The configuration backup plan contains a missing path record.");
                }

                normalizedPaths.Add(NormalizePath(path.RootId, path.RelativePath, roots));
            }

            return normalizedPaths;
        }

        private static void ValidatePlan(IReadOnlyCollection<ResolvedPath> resolvedPaths)
        {
            if (resolvedPaths.Count == 0)
            {
                throw new InvalidDataException("The configuration backup plan contains no files.");
            }
            if (resolvedPaths.Count > MaximumFileCount)
            {
                throw new InvalidDataException("The configuration backup plan contains too many files.");
            }

            var uniqueDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ResolvedPath path in resolvedPaths)
            {
                if (!uniqueDestinations.Add(path.DestinationKey))
                {
                    throw new InvalidDataException($"The configuration backup plan contains a duplicate path: {path.RootId}/{path.RelativePath}.");
                }
            }
        }

        private static ResolvedPath NormalizePath(string rootId, string relativePath, Dictionary<string, string> roots)
        {
            string normalizedRootId = NormalizeRootId(rootId);
            if (!roots.TryGetValue(normalizedRootId, out string rootPath))
            {
                throw new InvalidDataException($"The configuration backup path uses an unknown root: {normalizedRootId}.");
            }

            string normalizedRelativePath = NormalizeRelativePath(relativePath);
            return new ResolvedPath
            {
                RootId = normalizedRootId,
                RootPath = rootPath,
                RelativePath = normalizedRelativePath,
                FullPath = GetContainedPath(rootPath, normalizedRelativePath),
                ArchivePath = "files/" + normalizedRootId + "/" + normalizedRelativePath,
                DestinationKey = normalizedRootId + "|" + normalizedRelativePath
            };
        }

        private static string NormalizeRootId(string rootId)
        {
            if (string.IsNullOrWhiteSpace(rootId) || rootId.Any(character =>
                !(character >= 'a' && character <= 'z') &&
                !(character >= 'A' && character <= 'Z') &&
                !(character >= '0' && character <= '9') && character != '_' && character != '-'))
            {
                throw new InvalidDataException("A configuration backup root ID is invalid.");
            }

            return rootId;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            {
                throw new InvalidDataException("A configuration backup path must be relative.");
            }

            string[] segments = relativePath.Replace('\\', '/').Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                throw new InvalidDataException("A configuration backup path contains an unsafe segment.");
            }

            return string.Join("/", segments);
        }

        private static string GetContainedPath(string rootPath, string relativePath)
        {
            string fullRoot = Path.GetFullPath(rootPath);
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A configuration backup path escapes its approved root.");
            }

            return fullPath;
        }

        private static void EnsureSourceFileExists(string rootPath, string relativePath, string fullPath)
        {
            EnsureSafeDestination(rootPath, relativePath, false);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"A required configuration backup file was not found: {relativePath}.", fullPath);
            }
        }

        private static void EnsureSafeDestination(string rootPath, string relativePath, bool createDirectories)
        {
            string fullRoot = Path.GetFullPath(rootPath);
            EnsureDirectoryPathHasNoReparsePoints(fullRoot, createDirectories);

            string[] segments = NormalizeRelativePath(relativePath).Split('/');
            string currentPath = fullRoot;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                currentPath = Path.Combine(currentPath, segments[index]);
                if (File.Exists(currentPath))
                {
                    throw new InvalidDataException("A configuration backup destination has a file in its parent path.");
                }
                if (!Directory.Exists(currentPath))
                {
                    if (!createDirectories)
                    {
                        break;
                    }

                    Directory.CreateDirectory(currentPath);
                }
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("A configuration backup destination cannot pass through a reparse point.");
                }
            }

            string destinationPath = GetContainedPath(fullRoot, relativePath);
            if (Directory.Exists(destinationPath))
            {
                throw new InvalidDataException("A configuration backup destination is a directory, not a file.");
            }
            if (File.Exists(destinationPath) && (File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("A configuration backup destination cannot be a reparse point.");
            }
        }

        private static void EnsureDirectoryPathHasNoReparsePoints(string directoryPath, bool createMissingDirectories)
        {
            string fullPath = Path.GetFullPath(directoryPath);
            string currentPath = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
            {
                throw new DirectoryNotFoundException($"The approved configuration backup root does not exist: {fullPath}.");
            }

            foreach (string segment in fullPath.Substring(currentPath.Length)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                if (!Directory.Exists(currentPath))
                {
                    if (File.Exists(currentPath))
                    {
                        throw new InvalidDataException("An approved configuration backup root contains a file in its parent path.");
                    }
                    if (!createMissingDirectories)
                    {
                        return;
                    }

                    Directory.CreateDirectory(currentPath);
                }
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("An approved configuration backup root cannot pass through a reparse point.");
                }
            }
        }

        private static void ValidateArchiveExtension(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath) ||
                !string.Equals(Path.GetExtension(archivePath), FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Configuration backup files must use the {FileExtension} extension.", nameof(archivePath));
            }
        }

        private static void PublishFile(string sourcePath, string destinationPath)
        {
            string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(sourcePath, temporaryPath, false);
                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, null);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string CopyAndHash(Stream source, Stream destination, out long length)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] buffer = new byte[81920];
                length = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destination.Write(buffer, 0, read);
                    sha256.TransformBlock(buffer, 0, read, buffer, 0);
                    length += read;
                }

                sha256.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty);
            }
        }

        private static string NormalizeSharedInstallationId(string installationId)
        {
            if (string.IsNullOrWhiteSpace(installationId))
            {
                return string.Empty;
            }
            if (!Guid.TryParseExact(installationId, "N", out _))
            {
                throw new InvalidDataException("The configuration backup shared installation ID is invalid.");
            }

            return installationId;
        }

        private static bool IsSha256(string hash)
        {
            return !string.IsNullOrWhiteSpace(hash) && hash.Length == 64 && hash.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F'));
        }

        private static void ValidateIdentity(string serverId, string gameKey)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("A server ID is required.", nameof(serverId));
            }
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                throw new ArgumentException("A game key is required.", nameof(gameKey));
            }
        }

        private static bool IsExpectedFileException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is SecurityException;
        }

        private sealed class ResolvedPath
        {
            public string RootId { get; set; }
            public string RootPath { get; set; }
            public string RelativePath { get; set; }
            public string FullPath { get; set; }
            public string ArchivePath { get; set; }
            public string DestinationKey { get; set; }
        }

        private sealed class ValidatedArchive
        {
            public List<ValidatedFile> Files { get; set; }
        }

        private sealed class ValidatedFile
        {
            public string RootId { get; set; }
            public string RootPath { get; set; }
            public string RelativePath { get; set; }
            public string DestinationPath { get; set; }
            public string ArchivePath { get; set; }
            public ZipArchiveEntry ZipEntry { get; set; }
            public long Length { get; set; }
            public string Sha256 { get; set; }
            public string StagedPath { get; set; }
            public string RollbackPath { get; set; }
            public bool ExistedBeforeRestore { get; set; }
        }
    }

    public sealed class ConfigurationBackupResult
    {
        public string ArchivePath { get; set; }
        public long SourceBytes { get; set; }
        public long ArchiveBytes { get; set; }
        public List<ConfigurationBackupPath> IncludedPaths { get; set; }
    }
}