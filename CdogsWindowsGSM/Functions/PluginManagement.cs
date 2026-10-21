using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
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
    }

    class PluginManagement
    {
        public const string DefaultUserImage = "pack://application:,,,/Images/Plugins/User.png";
        public const string DefaultPluginImage = "pack://application:,,,/Images/WindowsGSM.png";
        private static readonly Lazy<IReadOnlyList<MetadataReference>> PluginReferences = new Lazy<IReadOnlyList<MetadataReference>>(CreatePluginReferences);

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
            var syntaxTree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.CSharp7_3),
                path);
            var compilation = CSharpCompilation.Create(
                $"{Path.GetFileNameWithoutExtension(path)}_{Guid.NewGuid():N}",
                new[] { syntaxTree },
                PluginReferences.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

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

                    return new PluginCompilationResult { Error = errors.ToString() };
                }

                assemblyStream.Position = 0;
                return new PluginCompilationResult { Assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream) };
            }
        }

        private static IReadOnlyList<MetadataReference> CreatePluginReferences()
        {
            var references = new List<MetadataReference>();
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
                    references.Add(MetadataReference.CreateFromFile(assemblyPath));
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

            return references;
        }

        private static unsafe MetadataReference CreateAssemblyReference(Assembly assembly)
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location) &&
                string.Equals(Path.GetExtension(assembly.Location), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return MetadataReference.CreateFromFile(assembly.Location);
            }

            if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* metadata, out int length))
            {
                return ModuleMetadata.CreateFromMetadata((IntPtr)metadata, length).GetReference();
            }

            throw new InvalidOperationException($"Could not access metadata for runtime assembly '{assembly.GetName().Name}'.");
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
