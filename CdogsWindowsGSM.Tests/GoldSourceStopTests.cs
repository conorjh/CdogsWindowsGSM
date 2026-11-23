using System.Diagnostics;
using System.IO;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GoldSourceStopTests
{
    [TestMethod]
    public async Task Stop_SendsQuitToRedirectedStandardInput()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.GoldSourceStopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string scriptPath = Path.Combine(testRoot, "wait-for-quit.cmd");
        File.WriteAllText(scriptPath, "@echo off\r\nset /p command=\r\nif /i \"%command%\"==\"quit\" exit /b 0\r\nexit /b 1\r\n");

        Process? process = null;
        try
        {
            using IDisposable pathScope = ServerPath.UseRootForTesting(testRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(scriptPath);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the redirected-input test process.");
            var server = new GoldSource(new ServerConfig("goldsrc-stop-test"));

            await server.Stop(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, process.ExitCode);
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

    [TestMethod]
    public void StartInNewConsole_CreatesUnredirectedProcess()
    {
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = "/d /c ping 127.0.0.1 -n 30 > nul",
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Minimized
            };

            process = ServerConsole.StartInNewConsole(startInfo);

            Assert.IsFalse(process.HasExited);
            Assert.IsFalse(ServerConsole.IsStandardInputRedirected(process));
            Assert.IsFalse(ServerConsole.IsStandardOutputRedirected(process));
            Assert.IsFalse(ServerConsole.IsCreatedWithoutWindow(process));
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

    [TestMethod]
    public void Stop_WithoutRedirectedInputOrUsableWindow_Throws()
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = "/d /c ping 127.0.0.1 -n 30 > nul",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the no-window test process.");

        try
        {
            var server = new GoldSource(new ServerConfig("goldsrc-stop-no-window-test"));

            Assert.ThrowsExactly<InvalidOperationException>(() => server.Stop(process));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }
}