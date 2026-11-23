using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    public sealed class GameServerConfigDownloadResult
    {
        internal GameServerConfigDownloadResult(bool succeeded, bool existingFilePreserved, string errorMessage)
        {
            Succeeded = succeeded;
            ExistingFilePreserved = existingFilePreserved;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Succeeded { get; }
        public bool ExistingFilePreserved { get; }
        public string ErrorMessage { get; }
    }

    public static class Github
    {
        public static async Task<bool> DownloadGameServerConfig(string filePath, string gameFullName)
        {
            GameServerConfigDownloadResult result = await DownloadGameServerConfigAsync(filePath, gameFullName).ConfigureAwait(false);
            return result.Succeeded;
        }

        public static async Task<bool> DownloadGameServerConfig(string configPath, string serverGame, List<(string, string)> replaceValues)
        {
            GameServerConfigDownloadResult result = await DownloadGameServerConfigAsync(
                configPath,
                serverGame,
                replaceValues).ConfigureAwait(false);
            return result.Succeeded;
        }

        public static Task<GameServerConfigDownloadResult> DownloadGameServerConfigAsync(
            string configPath,
            string serverGame,
            List<(string, string)> replaceValues = null,
            bool replaceExisting = false)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("A destination path is required.", nameof(configPath));
            }

            if (string.IsNullOrWhiteSpace(serverGame))
            {
                throw new ArgumentException("A server game name is required.", nameof(serverGame));
            }

            string fileName = Path.GetFileName(configPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("The destination path must include a file name.", nameof(configPath));
            }

            string gamePath = Uri.EscapeDataString(serverGame.Replace(":", string.Empty));
            string escapedFileName = Uri.EscapeDataString(fileName);
            var sourceUri = new Uri(
                $"https://github.com/WindowsGSM/Game-Server-Configs/raw/master/{gamePath}/{escapedFileName}",
                UriKind.Absolute);

            return DownloadGameServerConfigAsync(sourceUri, configPath, replaceValues, replaceExisting);
        }

        internal static async Task<GameServerConfigDownloadResult> DownloadGameServerConfigAsync(
            Uri sourceUri,
            string configPath,
            List<(string, string)> replaceValues,
            bool replaceExisting)
        {
            if (sourceUri == null)
            {
                throw new ArgumentNullException(nameof(sourceUri));
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("A destination path is required.", nameof(configPath));
            }

            string destinationPath = Path.GetFullPath(configPath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            string stagedPath = null;
            try
            {
                Directory.CreateDirectory(destinationDirectory);
                if (!replaceExisting && File.Exists(destinationPath))
                {
                    return new GameServerConfigDownloadResult(true, true, string.Empty);
                }

                stagedPath = Path.Combine(
                    destinationDirectory,
                    $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync(sourceUri, stagedPath).ConfigureAwait(false);
                }

                if (new FileInfo(stagedPath).Length == 0)
                {
                    return new GameServerConfigDownloadResult(false, false, "The downloaded config template is empty.");
                }

                if (IsHtmlErrorPage(stagedPath))
                {
                    return new GameServerConfigDownloadResult(false, false, "The downloaded config template is an HTML error page.");
                }

                if (replaceValues != null && replaceValues.Count > 0)
                {
                    string configText = await File.ReadAllTextAsync(stagedPath).ConfigureAwait(false);
                    foreach ((string oldValue, string newValue) in replaceValues)
                    {
                        configText = configText.Replace(oldValue, newValue);
                    }

                    await File.WriteAllTextAsync(stagedPath, configText).ConfigureAwait(false);
                }

                bool published = TryPublishStagedFile(stagedPath, destinationPath, replaceExisting);
                return published
                    ? new GameServerConfigDownloadResult(true, false, string.Empty)
                    : new GameServerConfigDownloadResult(true, true, string.Empty);
            }
            catch (Exception exception) when (
                exception is WebException ||
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is InvalidDataException)
            {
                return new GameServerConfigDownloadResult(false, false, exception.Message);
            }
            finally
            {
                if (stagedPath != null)
                {
                    File.Delete(stagedPath);
                }
            }
        }

        private static bool IsHtmlErrorPage(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            {
                char[] prefix = new char[256];
                int charactersRead = reader.Read(prefix, 0, prefix.Length);
                string trimmedPrefix = new string(prefix, 0, charactersRead).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                return trimmedPrefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedPrefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedPrefix.StartsWith("404: Not Found", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool TryPublishStagedFile(string stagedPath, string destinationPath, bool replaceExisting)
        {
            if (File.Exists(destinationPath))
            {
                if (!replaceExisting)
                {
                    return false;
                }

                File.Replace(stagedPath, destinationPath, null);
                return true;
            }

            try
            {
                File.Move(stagedPath, destinationPath);
                return true;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                if (!replaceExisting)
                {
                    return false;
                }

                File.Replace(stagedPath, destinationPath, null);
                return true;
            }
        }
    }
}
