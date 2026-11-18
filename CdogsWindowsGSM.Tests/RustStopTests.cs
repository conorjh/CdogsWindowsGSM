using System.Diagnostics;
using System.IO;
using WindowsGSM.Functions;
using WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RustStopTests
{
    [TestMethod]
    public void GenerateWebRconPassword_UsesUrlSafeCharacters()
    {
        string password = RUST.GenerateWebRconPassword();

        Assert.AreEqual(12, password.Length);
        Assert.IsTrue(password.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_'));
    }

    [TestMethod]
    public async Task Stop_UsesConsoleForPasswordOutsideUrlSafeAlphabet()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.RustStopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string scriptPath = Path.Combine(testRoot, "wait-for-quit.cmd");
        File.WriteAllText(scriptPath, "@echo off\r\nset /p command=\r\nif /i \"%command%\"==\"quit\" exit /b 0\r\nexit /b 1\r\n");

        Process? process = null;
        try
        {
            using IDisposable pathScope = ServerPath.UseRootForTesting(testRoot);
            const string serverId = "rust-stop-legacy-password";
            string serverFiles = ServerPath.GetServersServerFiles(serverId);
            Directory.CreateDirectory(serverFiles);
            File.WriteAllText(
                Path.Combine(serverFiles, "server.cfg"),
                "+rcon.web 1\r\n+rcon.port 28015\r\n+rcon.password \"Legacy!Rcon\"\r\n");

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
            var server = new RUST(new ServerConfig(serverId));

            await server.Stop(process).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsFalse(string.IsNullOrWhiteSpace(server.Notice));
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

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}