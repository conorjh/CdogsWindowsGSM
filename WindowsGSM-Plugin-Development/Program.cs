using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM_Plugin_Development
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            var title = "Welcome to WindowsGSM Plugin Development Environment";
            Console.WriteLine($"\t{string.Concat(Enumerable.Repeat("-", title.Length))}\t\t");
            Console.WriteLine($"\t{title}\t\t");
            Console.WriteLine($"\t{string.Concat(Enumerable.Repeat("-", title.Length))}\t\t");
            Console.ResetColor();

            var currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var repositoryRoot = FindRepositoryRoot(currentDir);
#if DEBUG
            var target = "Debug";
#else
            var target = "Release";
#endif
            var appOutputDirectory = Path.Combine(repositoryRoot, "CdogsWindowsGSM", "bin", target, "net10.0-windows10.0.19041", "win-x64");
            var applicationPath = Path.Combine(appOutputDirectory, "CdogsWindowsGSM.exe");
            if (!File.Exists(applicationPath))
            {
                Console.Error.WriteLine($"Build CdogsWindowsGSM.Net10 before installing plugins. Application output was not found at {applicationPath}.");
                return;
            }

            var wgsmPlugins = Directory.CreateDirectory(Path.Combine(appOutputDirectory, "plugins")).FullName;
            var localPlugins = Path.Combine(repositoryRoot, "WindowsGSM-Plugin-Development", "Plugins");

            Console.Write("\nSearching your new plugins in ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"({localPlugins})\n");
            Console.ResetColor();

            var pluginsList = new List<string>();
            foreach (var pluginFile in Directory.GetFiles(localPlugins, "*.cs", SearchOption.TopDirectoryOnly).ToList())
            {
                pluginsList.Add(pluginFile);
                Console.Write("Found => ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{Path.GetFileName(pluginFile)}\n");
                Console.ResetColor();
            }

            Console.Write("\nReady to install your new plugins to ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"({wgsmPlugins})\n");
            Console.ResetColor();

            foreach (var pluginFile in pluginsList)
            {
                var pluginFolder = Directory.CreateDirectory(Path.Combine(wgsmPlugins, Path.GetFileName(pluginFile))).FullName;
                var plugin = Path.Combine(pluginFolder, Path.GetFileName(pluginFile));
                Console.Write("Install => ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{Path.GetFileName(pluginFile)}");
                Console.ResetColor();
                Console.Write(" => ");

                if (DeleteOldPlugin(plugin))
                {
                    File.Copy(pluginFile, Path.Combine(pluginFolder, Path.GetFileName(pluginFile)));
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("Success\n");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("Fail\n");
                }

                Console.ResetColor();
            }

            Console.Write("\nPlugins installation done, please change the Startup Project to ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("WindowsGSM");
            Console.ResetColor();
            Console.Write(" for testing your new plugins!\n");

            Console.WriteLine("\nPress Any Key To Exit...");
            Console.ReadKey();

            /*
            Console.WriteLine("\nAuto close in 5 seconds...");
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(1000);
            }*/
        }

        private static string FindRepositoryRoot(string startDirectory)
        {
            for (DirectoryInfo directory = new DirectoryInfo(startDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CdogsWindowsGSM.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate CdogsWindowsGSM.sln from the plugin development tool directory.");
        }

        private static bool DeleteOldPlugin(string oldPluginFile)
        {
            if (!File.Exists(oldPluginFile))
            {
                return true;
            }

            try
            {
                File.Delete(oldPluginFile);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }
    }
}
