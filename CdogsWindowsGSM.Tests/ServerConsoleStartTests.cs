using System.Diagnostics;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class ServerConsoleStartTests
{
    [TestMethod]
    public void StartInNewConsole_PreservesLaunchMetadata()
    {
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("The Windows command processor path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = commandProcessor,
            Arguments = "/d /q",
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Minimized
        };

        Process? process = null;
        try
        {
            process = ServerConsole.StartInNewConsole(startInfo);

            Assert.IsFalse(process.HasExited);
            Assert.IsTrue(ServerConsole.HasProcessStartInfo(process));
            IntPtr consoleWindow = ServerConsole.GetMainWindowHandle(process);
            if (consoleWindow != IntPtr.Zero)
            {
                Assert.IsTrue(ServerConsole.HasUsableWindow(process, consoleWindow));
            }
            Assert.IsTrue(ServerConsole.IsConsoleProcess(process));
            Assert.IsFalse(ServerConsole.IsStandardInputRedirected(process));
            Assert.IsFalse(ServerConsole.IsStandardOutputRedirected(process));
            Assert.AreEqual(startInfo.WorkingDirectory, ServerConsole.GetWorkingDirectory(process));
        }
        finally
        {
            if (process != null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }

                process.Dispose();
            }
        }
    }
}
