using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace CdogsWindowsGSM.Updater
{
    internal static class Program
    {
        private const string ApplicationFileName = "CdogsWindowsGSM.exe";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/conorjh/CdogsWindowsGSM/releases?per_page=1";
        private const int ReplaceAttempts = 40;

        private static int Main(string[] args)
        {
            bool autoStart = Array.Exists(args, argument => string.Equals(argument, "-autostart", StringComparison.OrdinalIgnoreCase));
            DirectoryInfo installDirectory = Directory.GetParent(Environment.CurrentDirectory);
            if (installDirectory == null)
            {
                return Fail("Could not determine the WindowsGSM installation directory.");
            }

            string applicationPath = Path.Combine(installDirectory.FullName, ApplicationFileName);
            string downloadPath = applicationPath + ".download";

            try
            {
                DownloadLatestApplication(downloadPath);
                ValidateApplication(downloadPath);
                ReplaceApplication(downloadPath, applicationPath);
                Console.WriteLine("CdogsWindowsGSM was updated successfully.");

                if (autoStart)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = applicationPath,
                        WorkingDirectory = installDirectory.FullName,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }

                return 0;
            }
            catch (InvalidDataException ex)
            {
                return Fail($"The latest release does not contain a valid {ApplicationFileName}.", ex);
            }
            catch (WebException ex)
            {
                return Fail("Could not download the latest CdogsWindowsGSM release. Check that the release asset is published and try again.", ex);
            }
            catch (IOException ex)
            {
                return Fail("Could not replace CdogsWindowsGSM.exe. Close any running copy and try again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail("Access was denied while updating CdogsWindowsGSM. Run the updater with administrator permissions.", ex);
            }
            catch (Win32Exception ex)
            {
                return Fail("CdogsWindowsGSM was updated, but could not be restarted.", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Fail("CdogsWindowsGSM was updated, but could not be restarted.", ex);
            }
            finally
            {
                DeleteTemporaryFile(downloadPath);
            }
        }

        private static void DownloadLatestApplication(string destinationPath)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string applicationDownloadUrl = GetLatestApplicationDownloadUrl();
            var request = (HttpWebRequest)WebRequest.Create(applicationDownloadUrl);
            request.Method = "GET";
            request.UserAgent = "CdogsWindowsGSM-Updater";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;

            using (WebResponse response = request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (responseStream == null)
                {
                    throw new InvalidDataException("The GitHub release returned an empty response.");
                }

                responseStream.CopyTo(destinationStream);
            }
        }

        private static string GetLatestApplicationDownloadUrl()
        {
            var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
            request.Method = "GET";
            request.UserAgent = "CdogsWindowsGSM-Updater";
            request.Accept = "application/vnd.github+json";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;

            using (WebResponse response = request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            {
                if (responseStream == null)
                {
                    throw new InvalidDataException("GitHub returned an empty release response.");
                }

                using (var responseReader = new StreamReader(responseStream))
                {
                    JsonDocument releaseList;
                    try
                    {
                        releaseList = JsonDocument.Parse(responseReader.ReadToEnd());
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidDataException("GitHub returned invalid release data.", ex);
                    }

                    using (releaseList)
                    {
                        JsonElement releases = releaseList.RootElement;
                        if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
                        {
                            throw new InvalidDataException("GitHub returned no published CdogsWindowsGSM releases.");
                        }

                        JsonElement latestRelease = releases[0];
                        JsonElement assets;
                        if (latestRelease.ValueKind != JsonValueKind.Object ||
                            !latestRelease.TryGetProperty("assets", out assets) ||
                            assets.ValueKind != JsonValueKind.Array)
                        {
                            throw new InvalidDataException("The latest CdogsWindowsGSM release has no usable assets.");
                        }

                        foreach (JsonElement asset in assets.EnumerateArray())
                        {
                            JsonElement assetName;
                            JsonElement downloadUrl;
                            if (asset.ValueKind != JsonValueKind.Object ||
                                !asset.TryGetProperty("name", out assetName) ||
                                assetName.ValueKind != JsonValueKind.String ||
                                !string.Equals(assetName.GetString(), ApplicationFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!asset.TryGetProperty("browser_download_url", out downloadUrl) ||
                                downloadUrl.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            Uri downloadUri;
                            if (!Uri.TryCreate(downloadUrl.GetString(), UriKind.Absolute, out downloadUri) ||
                                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException("The latest application asset does not have a valid GitHub download URL.");
                            }

                            return downloadUri.AbsoluteUri;
                        }

                        throw new InvalidDataException("The latest release does not contain a valid " + ApplicationFileName + " asset.");
                    }
                }
            }
        }

        private static void ValidateApplication(string applicationPath)
        {
            using (FileStream applicationStream = File.OpenRead(applicationPath))
            {
                if (applicationStream.Length < 2 || applicationStream.ReadByte() != 0x4D || applicationStream.ReadByte() != 0x5A)
                {
                    throw new InvalidDataException("The downloaded release asset is not a Windows executable.");
                }
            }
        }

        private static void ReplaceApplication(string replacementPath, string applicationPath)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < ReplaceAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(applicationPath))
                    {
                        File.Replace(replacementPath, applicationPath, null);
                    }
                    else
                    {
                        File.Move(replacementPath, applicationPath);
                    }

                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                Thread.Sleep(500);
            }

            throw new IOException("The application file remained locked after waiting for it to close.", lastError);
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Could not remove temporary file {path}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Could not remove temporary file {path}: {ex.Message}");
            }
        }

        private static int Fail(string message, Exception exception = null)
        {
            Console.Error.WriteLine(message);
            if (exception != null)
            {
                Console.Error.WriteLine(exception);
            }

            Console.WriteLine("Press Enter to close this window.");
            Console.ReadLine();
            return 1;
        }
    }
}
