using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    public static class LegacyDirectXRuntime
    {
        private const string RedistUrl = "https://download.microsoft.com/download/8/4/a/84a35bf1-dafe-4ae8-82af-ad2ae20b6b14/directx_Jun2010_redist.exe";
        private const string RedistFileName = "directx_Jun2010_redist.exe";
        private const long RedistSize = 100275120;
        private const string RedistSha256 = "053F76DCBB28802E23341B6A787E3B0791C0FA5C8D4D011B1044172DBF89C73B";
        private static readonly SemaphoreSlim RuntimeLock = new SemaphoreSlim(1, 1);
        private static readonly (string FileName, string Sha256, string CabinetName)[] RuntimeFiles =
        {
            ("XAPOFX1_5.dll", "E47DCF74D50403B376C562E4121E359E5886E42FCC60B3FA8BA53E6826854C49", "Jun2010_XAudio_x64.cab"),
            ("XAudio2_7.dll", "F8636CCC37BBFC84107992B60E4226EB7237417112267ED64B08F72983AC4314", "Jun2010_XAudio_x64.cab"),
            ("X3DAudio1_7.dll", "9460709339701AD471A5CABE6365355F4D586DC4FCB86507C1331839DC555446", "Feb2010_X3DAudio_x64.cab"),
            ("D3DCompiler_43.dll", "44C3A7E330B54A35A9EFA015831392593AA02E7DA1460BE429D17C3644850E8A", "Jun2010_D3DCompiler_43_x64.cab")
        };

        public static async Task EnsureInstalledAsync(string serverFilesPath)
        {
            await RuntimeLock.WaitAsync();
            string stagingPath = null;

            try
            {
                string runtimePath = ServerPath.Get("bin", "directx");
                Directory.CreateDirectory(runtimePath);

                if (!HasVerifiedFiles(runtimePath))
                {
                    stagingPath = Path.Combine(runtimePath, "__legacy_directx_" + Guid.NewGuid().ToString("N"));
                    string runtimeStagingPath = Path.Combine(stagingPath, "runtime");
                    Directory.CreateDirectory(runtimeStagingPath);

                    string directXCabPath = Path.Combine(serverFilesPath, "_CommonRedist", "DirectX", "Jun2010");
                    if (!HasRequiredCabinets(directXCabPath))
                    {
                        string extractPath = Path.Combine(stagingPath, "extract");
                        Directory.CreateDirectory(extractPath);
                        string redistPath = Path.Combine(stagingPath, RedistFileName);
                        using (var webClient = new WebClient())
                        {
                            webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
                            await webClient.DownloadFileTaskAsync(new Uri(RedistUrl), redistPath);
                        }

                        var redistInfo = new FileInfo(redistPath);
                        if (redistInfo.Length != RedistSize || !VerifySha256(redistPath, RedistSha256))
                        {
                            throw new InvalidDataException("The official DirectX End-User Runtime package failed size or SHA-256 verification.");
                        }

                        await RunToolAsync(redistPath, $"/Q /T:\"{extractPath}\"");
                        directXCabPath = extractPath;
                    }

                    await ExtractRuntimeFilesAsync(directXCabPath, runtimeStagingPath);
                    if (!HasVerifiedFiles(runtimeStagingPath))
                    {
                        throw new InvalidDataException("The DirectX package did not contain the expected legacy runtime files.");
                    }

                    foreach ((string fileName, string sha256, string cabinetName) in RuntimeFiles)
                    {
                        File.Copy(
                            Path.Combine(runtimeStagingPath, fileName),
                            Path.Combine(runtimePath, fileName),
                            overwrite: true);
                    }
                }

                Directory.CreateDirectory(serverFilesPath);
                foreach ((string fileName, string sha256, string cabinetName) in RuntimeFiles)
                {
                    string sourcePath = Path.Combine(runtimePath, fileName);
                    string destinationPath = Path.Combine(serverFilesPath, fileName);
                    if (!File.Exists(destinationPath) || !VerifySha256(destinationPath, sha256))
                    {
                        File.Copy(sourcePath, destinationPath, overwrite: true);
                    }
                }
            }
            catch (Exception exception) when (exception is WebException ||
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is CryptographicException ||
                exception is InvalidOperationException ||
                exception is Win32Exception ||
                exception is TimeoutException ||
                exception is ArgumentException)
            {
                throw new InvalidOperationException("Failed to prepare the legacy DirectX runtime: " + exception.Message, exception);
            }
            finally
            {
                try
                {
                    if (stagingPath != null && Directory.Exists(stagingPath))
                    {
                        Directory.Delete(stagingPath, recursive: true);
                    }
                }
                finally
                {
                    RuntimeLock.Release();
                }
            }
        }

        private static bool HasRequiredCabinets(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            foreach ((string fileName, string sha256, string cabinetName) in RuntimeFiles)
            {
                if (!File.Exists(Path.Combine(path, cabinetName)))
                {
                    return false;
                }
            }

            return true;
        }

        private static async Task ExtractRuntimeFilesAsync(string cabinetDirectory, string destinationDirectory)
        {
            string expandPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "expand.exe");
            foreach ((string fileName, string sha256, string cabinetName) in RuntimeFiles)
            {
                string cabinetPath = Path.Combine(cabinetDirectory, cabinetName);
                if (!File.Exists(cabinetPath))
                {
                    throw new FileNotFoundException("A required DirectX runtime cabinet was not found.", cabinetPath);
                }

                await RunToolAsync(expandPath, $"\"{cabinetPath}\" -F:{fileName} \"{destinationDirectory}\"");
            }
        }

        private static bool HasVerifiedFiles(string path)
        {
            foreach ((string fileName, string sha256, string cabinetName) in RuntimeFiles)
            {
                string filePath = Path.Combine(path, fileName);
                if (!File.Exists(filePath) || !VerifySha256(filePath, sha256))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool VerifySha256(string filePath, string expectedSha256)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                string actualSha256 = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
                return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static async Task RunToolAsync(string executablePath, string arguments)
        {
            using (var process = new Process
            {
                StartInfo =
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException($"Failed to start {Path.GetFileName(executablePath)}.");
                }

                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                {
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                        await Task.WhenAll(standardOutput, standardError);
                        throw new TimeoutException($"{Path.GetFileName(executablePath)} did not complete within two minutes.");
                    }
                }

                await Task.WhenAll(standardOutput, standardError);
                if (process.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        $"{Path.GetFileName(executablePath)} exited with code {process.ExitCode}. " +
                        standardError.Result + standardOutput.Result);
                }
            }
        }
    }
}
