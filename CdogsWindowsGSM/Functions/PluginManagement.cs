using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsGSM.Functions
{
    public class PluginMetadata
    {
        public bool IsLoaded;
        public string GameImage, AuthorImage, FullName, FileName, Error;
        public Plugin Plugin;
        public Type Type;
        public List<string> CacheDiagnostics = new List<string>();
    }

    class PluginManagement
    {
        public const string DefaultUserImage = "pack://application:,,,/Images/Plugins/User.png";
        public const string DefaultPluginImage = "pack://application:,,,/Images/WindowsGSM.png";
        private static readonly Lazy<PluginCompilationEnvironment> PluginEnvironment = new Lazy<PluginCompilationEnvironment>(CreatePluginEnvironment);
        private static readonly object LoadedPluginAssembliesLock = new object();
        private static readonly Dictionary<string, Assembly> LoadedPluginAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private static readonly CSharpParseOptions PluginParseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        private static readonly CSharpCompilationOptions PluginCompilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release);

        public PluginManagement()
        {
            Directory.CreateDirectory(ServerPath.GetPlugins());
        }

        public async Task<List<PluginMetadata>> LoadPlugins(bool shouldAwait = true)
        {
            var plugins = new List<PluginMetadata>();
            foreach (var pluginFolder in Directory.GetDirectories(ServerPath.GetPlugins(), "*.cs", SearchOption.TopDirectoryOnly).ToList())
            {
                var pluginFile = Path.Combine(pluginFolder, Path.GetFileName(pluginFolder));
                if (File.Exists(pluginFile))
                {
                    var plugin = await LoadPlugin(pluginFile, shouldAwait);
                    if (plugin != null)
                    {
                        plugins.Add(plugin);
                    }
                }
            }
            
            return plugins;
        }

        public async Task<PluginMetadata> LoadPlugin(string path, bool shouldAwait = true)
        {
            var pluginMetadata = new PluginMetadata
            {
                FileName = Path.GetFileName(path)
            };

            PluginCompilationResult compilation;
            try
            {
                compilation = shouldAwait
                    ? await Task.Run(() => CompilePlugin(path))
                    : CompilePlugin(path);
            }
            catch (Exception exception) when (IsPluginCompilationException(exception))
            {
                pluginMetadata.Error = exception.Message;
                Console.WriteLine(pluginMetadata.Error);
                return pluginMetadata;
            }

            pluginMetadata.CacheDiagnostics.AddRange(compilation.CacheDiagnostics);
            if (compilation.Error != null)
            {
                pluginMetadata.Error = compilation.Error;
                Console.WriteLine(pluginMetadata.Error);
                return pluginMetadata;
            }

            try
            {
                pluginMetadata.Type = compilation.Assembly.GetType($"WindowsGSM.Plugins.{Path.GetFileNameWithoutExtension(path)}");
                var plugin = GetPluginClass(pluginMetadata);
                pluginMetadata.FullName = $"{plugin.FullName} [{pluginMetadata.FileName}]";
                pluginMetadata.Plugin = plugin.Plugin;
                try
                {
                    string gameImage = ServerPath.GetPlugins(pluginMetadata.FileName, $"{Path.GetFileNameWithoutExtension(pluginMetadata.FileName)}.png");
                    ImageSource image = new BitmapImage(new Uri(gameImage));
                    pluginMetadata.GameImage = gameImage;
                }
                catch
                {
                    pluginMetadata.GameImage = DefaultPluginImage;
                }
                try
                {
                    string authorImage = ServerPath.GetPlugins(pluginMetadata.FileName, "author.png");
                    ImageSource image = new BitmapImage(new Uri(authorImage));
                    pluginMetadata.AuthorImage = authorImage;
                }
                catch
                {
                    pluginMetadata.AuthorImage = DefaultUserImage;
                }
                pluginMetadata.IsLoaded = true;
            }
            catch (Exception e)
            {
                pluginMetadata.Error = e.Message;
                Console.WriteLine(pluginMetadata.Error);
                pluginMetadata.IsLoaded = false;
            }

            return pluginMetadata;
        }

        private static PluginCompilationResult CompilePlugin(string path)
        {
            var cacheDiagnostics = new List<string>();
            byte[] sourceBytes = File.ReadAllBytes(path);
            string sourceFileName = Path.GetFileName(path);
            string expectedTypeName = $"WindowsGSM.Plugins.{Path.GetFileNameWithoutExtension(path)}";
            PluginCompilationEnvironment environment = PluginEnvironment.Value;
            if (!string.IsNullOrWhiteSpace(environment.CacheDiagnostic))
            {
                cacheDiagnostics.Add(environment.CacheDiagnostic);
            }

            string fingerprint = environment.Fingerprint == null
                ? null
                : ComputePluginFingerprint(sourceFileName, expectedTypeName, sourceBytes, environment.Fingerprint);
            PluginCompilationCache cache = fingerprint == null
                ? null
                : new PluginCompilationCache(ServerPath.Get(ServerPath.FolderName.Cache, "plugins", "v1"));
            string assemblyName = "WindowsGSMPlugin_" + (fingerprint ?? Guid.NewGuid().ToString("N"));

            if (cache != null)
            {
                byte[] cachedAssembly = cache.TryRead(sourceFileName, fingerprint, expectedTypeName, cacheDiagnostics);
                if (cachedAssembly != null)
                {
                    try
                    {
                        Assembly assembly = LoadPluginAssembly(assemblyName, cachedAssembly, expectedTypeName);
                        return new PluginCompilationResult { Assembly = assembly, CacheDiagnostics = cacheDiagnostics };
                    }
                    catch (Exception exception) when (IsPluginAssemblyLoadException(exception))
                    {
                        cacheDiagnostics.Add($"The cached plugin assembly could not be loaded: {exception.Message}");
                        cache.Invalidate(sourceFileName, fingerprint, cacheDiagnostics);
                    }
                }
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(ReadPluginSource(sourceBytes), PluginParseOptions, path);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                environment.References,
                PluginCompilationOptions);

            using (var assemblyStream = new MemoryStream())
            {
                var emitResult = compilation.Emit(assemblyStream);
                if (!emitResult.Success)
                {
                    var errors = new StringBuilder();
                    foreach (Diagnostic diagnostic in emitResult.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error))
                    {
                        var line = diagnostic.Location.GetLineSpan().StartLinePosition;
                        errors.Append($"{diagnostic.GetMessage()}\nLine: {line.Line + 1} - Column: {line.Character + 1}\n\n");
                    }

                    return new PluginCompilationResult { Error = errors.ToString(), CacheDiagnostics = cacheDiagnostics };
                }

                byte[] compiledAssembly = assemblyStream.ToArray();
                if (!PluginCompilationCache.ContainsExpectedType(compiledAssembly, expectedTypeName))
                {
                    return new PluginCompilationResult
                    {
                        Error = $"The compiled plugin does not define the expected type '{expectedTypeName}'.",
                        CacheDiagnostics = cacheDiagnostics
                    };
                }

                byte[] assemblyToLoad = cache == null
                    ? compiledAssembly
                    : cache.PublishOrUseExisting(sourceFileName, fingerprint, expectedTypeName, compiledAssembly, cacheDiagnostics);
                try
                {
                    Assembly assembly = LoadPluginAssembly(assemblyName, assemblyToLoad, expectedTypeName);
                    return new PluginCompilationResult { Assembly = assembly, CacheDiagnostics = cacheDiagnostics };
                }
                catch (Exception exception) when (IsPluginAssemblyLoadException(exception))
                {
                    if (cache != null && !ReferenceEquals(assemblyToLoad, compiledAssembly))
                    {
                        cacheDiagnostics.Add($"The concurrently published plugin cache assembly could not be loaded: {exception.Message}");
                        cache.Invalidate(sourceFileName, fingerprint, cacheDiagnostics);
                        try
                        {
                            Assembly assembly = LoadPluginAssembly(assemblyName, compiledAssembly, expectedTypeName);
                            return new PluginCompilationResult { Assembly = assembly, CacheDiagnostics = cacheDiagnostics };
                        }
                        catch (Exception compileLoadException) when (IsPluginAssemblyLoadException(compileLoadException))
                        {
                            return new PluginCompilationResult
                            {
                                Error = compileLoadException.Message,
                                CacheDiagnostics = cacheDiagnostics
                            };
                        }
                    }

                    if (cache != null)
                    {
                        cacheDiagnostics.Add($"The newly compiled plugin assembly could not be loaded from the cache: {exception.Message}");
                        cache.Invalidate(sourceFileName, fingerprint, cacheDiagnostics);
                    }
                    return new PluginCompilationResult { Error = exception.Message, CacheDiagnostics = cacheDiagnostics };
                }
            }
        }

        private static PluginCompilationEnvironment CreatePluginEnvironment()
        {
            List<PluginCompilationReference> references = CreatePluginReferences();
            List<MetadataReference> metadataReferences = references.Select(reference => reference.Reference).ToList();
            PluginCompilationReference unhashableReference = references.FirstOrDefault(reference => reference.CacheDiagnostic != null);
            if (unhashableReference != null)
            {
                return new PluginCompilationEnvironment
                {
                    References = metadataReferences,
                    CacheDiagnostic = unhashableReference.CacheDiagnostic
                };
            }

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendFingerprintValue(hash, "CacheFormat", PluginCompilationCache.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture));
                AppendFingerprintValue(hash, "CompilerAssembly", typeof(CSharpCompilation).Assembly.GetName().FullName);
                AppendFingerprintValue(hash, "CompilerMvid", typeof(CSharpCompilation).Module.ModuleVersionId.ToString("N"));
                AppendFingerprintValue(hash, "CodeAnalysisAssembly", typeof(Compilation).Assembly.GetName().FullName);
                AppendFingerprintValue(hash, "CodeAnalysisMvid", typeof(Compilation).Module.ModuleVersionId.ToString("N"));
                AppendFingerprintValue(hash, "TargetFramework", AppContext.TargetFrameworkName);
                AppendFingerprintValue(hash, "Runtime", RuntimeInformation.FrameworkDescription);
                AppendFingerprintValue(hash, "RuntimeVersion", Environment.Version.ToString());
                AppendFingerprintValue(hash, "ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
                AppendFingerprintValue(hash, "OSArchitecture", RuntimeInformation.OSArchitecture.ToString());
                AppendFingerprintValue(hash, "LanguageVersion", PluginParseOptions.LanguageVersion.ToString());
                AppendFingerprintValue(hash, "OutputKind", PluginCompilationOptions.OutputKind.ToString());
                AppendFingerprintValue(hash, "OptimizationLevel", PluginCompilationOptions.OptimizationLevel.ToString());
                foreach (PluginCompilationReference reference in references)
                {
                    AppendFingerprintValue(hash, "ReferenceIdentity", reference.Identity);
                    AppendFingerprintValue(hash, "ReferenceSha256", reference.ContentHash);
                }

                return new PluginCompilationEnvironment
                {
                    References = metadataReferences,
                    Fingerprint = Convert.ToHexString(hash.GetHashAndReset())
                };
            }
        }

        private static List<PluginCompilationReference> CreatePluginReferences()
        {
            var references = new List<PluginCompilationReference>();
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedAssemblies))
            {
                throw new InvalidOperationException("The runtime did not provide trusted platform assemblies for plugin compilation.");
            }

            foreach (string assemblyPath in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                if (string.IsNullOrWhiteSpace(assemblyName) || !assemblyNames.Add(assemblyName))
                {
                    continue;
                }

                if (File.Exists(assemblyPath))
                {
                    references.Add(CreateFileReference(assemblyPath, assemblyName));
                }
                else
                {
                    try
                    {
                        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
                        references.Add(CreateAssemblyReference(assembly));
                    }
                    catch (FileNotFoundException exception)
                    {
                        throw new InvalidOperationException($"Could not load runtime reference '{assemblyName}' for plugin compilation.", exception);
                    }
                    catch (FileLoadException exception)
                    {
                        throw new InvalidOperationException($"Could not load runtime reference '{assemblyName}' for plugin compilation.", exception);
                    }
                    catch (BadImageFormatException exception)
                    {
                        throw new InvalidOperationException($"Could not load runtime reference '{assemblyName}' for plugin compilation.", exception);
                    }
                }
            }

            Assembly entryAssembly = typeof(PluginManagement).Assembly;
            if (assemblyNames.Add(entryAssembly.GetName().Name))
            {
                references.Add(CreateAssemblyReference(entryAssembly));
            }

            return references.OrderBy(reference => reference.Identity, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static PluginCompilationReference CreateFileReference(string path, string assemblyName)
        {
            string fullPath = Path.GetFullPath(path);
            MetadataReference reference = MetadataReference.CreateFromFile(fullPath);
            string identity = assemblyName + "|" + fullPath;
            try
            {
                using (FileStream stream = File.OpenRead(fullPath))
                {
                    return new PluginCompilationReference(reference, identity, Convert.ToHexString(SHA256.HashData(stream)));
                }
            }
            catch (Exception exception) when (IsReferenceFingerprintException(exception))
            {
                return new PluginCompilationReference(
                    reference,
                    identity,
                    null,
                    $"Could not fingerprint compiler reference '{fullPath}'; plugin caching is disabled: {exception.Message}");
            }
        }

        private static unsafe PluginCompilationReference CreateAssemblyReference(Assembly assembly)
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location) &&
                string.Equals(Path.GetExtension(assembly.Location), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return CreateFileReference(assembly.Location, assembly.GetName().Name);
            }

            if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* metadata, out int length))
            {
                string identity = assembly.GetName().FullName + "|" + assembly.ManifestModule.ModuleVersionId.ToString("N");
                string contentHash = Convert.ToHexString(SHA256.HashData(new ReadOnlySpan<byte>(metadata, length)));
                return new PluginCompilationReference(
                    ModuleMetadata.CreateFromMetadata((IntPtr)metadata, length).GetReference(),
                    identity,
                    contentHash);
            }

            throw new InvalidOperationException($"Could not access metadata for runtime assembly '{assembly.GetName().Name}'.");
        }

        private static string ComputePluginFingerprint(string sourceFileName, string expectedTypeName, byte[] sourceBytes, string environmentFingerprint)
        {
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendFingerprintValue(hash, "CacheFormat", PluginCompilationCache.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture));
                AppendFingerprintValue(hash, "SourceFileName", sourceFileName);
                AppendFingerprintValue(hash, "ExpectedType", expectedTypeName);
                AppendFingerprintValue(hash, "CompilerEnvironment", environmentFingerprint);
                hash.AppendData(BitConverter.GetBytes(sourceBytes.Length));
                hash.AppendData(sourceBytes);
                return Convert.ToHexString(hash.GetHashAndReset());
            }
        }

        private static void AppendFingerprintValue(IncrementalHash hash, string name, string value)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
            byte[] valueBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash.AppendData(BitConverter.GetBytes(nameBytes.Length));
            hash.AppendData(nameBytes);
            hash.AppendData(BitConverter.GetBytes(valueBytes.Length));
            hash.AppendData(valueBytes);
        }

        private static string ReadPluginSource(byte[] sourceBytes)
        {
            using (var stream = new MemoryStream(sourceBytes, writable: false))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return reader.ReadToEnd();
            }
        }

        private static Assembly LoadPluginAssembly(string assemblyName, byte[] assemblyBytes, string expectedTypeName)
        {
            lock (LoadedPluginAssembliesLock)
            {
                if (LoadedPluginAssemblies.TryGetValue(assemblyName, out Assembly loadedAssembly))
                {
                    return loadedAssembly;
                }

                using (var assemblyStream = new MemoryStream(assemblyBytes, writable: false))
                {
                    Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
                    if (assembly.GetType(expectedTypeName) == null)
                    {
                        throw new InvalidDataException($"The compiled plugin does not define the expected type '{expectedTypeName}'.");
                    }

                    LoadedPluginAssemblies.Add(assemblyName, assembly);
                    return assembly;
                }
            }
        }

        private static bool IsReferenceFingerprintException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is SecurityException ||
                exception is CryptographicException;
        }

        private static bool IsPluginAssemblyLoadException(Exception exception)
        {
            return exception is IOException ||
                exception is BadImageFormatException ||
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is SecurityException ||
                exception is TypeLoadException ||
                exception is System.Reflection.ReflectionTypeLoadException;
        }

        private static bool IsPluginCompilationException(Exception exception)
        {
            return exception is ArgumentException || exception is BadImageFormatException || exception is IOException ||
                exception is InvalidOperationException || exception is NotSupportedException || exception is UnauthorizedAccessException;
        }

        private sealed class PluginCompilationResult
        {
            public Assembly Assembly;
            public string Error;
            public List<string> CacheDiagnostics = new List<string>();
        }

        private sealed class PluginCompilationEnvironment
        {
            public List<MetadataReference> References;
            public string Fingerprint;
            public string CacheDiagnostic;
        }

        private sealed class PluginCompilationReference
        {
            public MetadataReference Reference { get; private set; }
            public string Identity { get; private set; }
            public string ContentHash { get; private set; }
            public string CacheDiagnostic { get; private set; }

            public PluginCompilationReference(MetadataReference reference, string identity, string contentHash, string cacheDiagnostic = null)
            {
                Reference = reference;
                Identity = identity;
                ContentHash = contentHash;
                CacheDiagnostic = cacheDiagnostic;
            }
        }

        public static BitmapSource GetDefaultUserBitmapSource()
        {
            using (var stream = System.Windows.Application.GetResourceStream(new Uri(DefaultUserImage)).Stream)
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }

        public static BitmapSource GetDefaultPluginBitmapSource()
        {
            using (var stream = System.Windows.Application.GetResourceStream(new Uri(DefaultPluginImage)).Stream)
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }

        public static dynamic GetPluginClass(PluginMetadata plugin, ServerConfig serverConfig = null) => Activator.CreateInstance(plugin.Type, serverConfig);
    }
}
