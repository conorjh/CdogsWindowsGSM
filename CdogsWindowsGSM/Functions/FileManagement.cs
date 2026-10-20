using System;
using System.Collections.Generic;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    public static class FileManagement
    {
        /// <summary>
        /// Extract .tar.gz file async
        /// </summary>
        /// <param name="sourceArchiveFileName"></param>
        /// <param name="destinationDirectoryName"></param>
        /// <returns></returns>
        public static async Task<bool> ExtractTarGZ(string sourceArchiveFileName, string destinationDirectoryName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (Stream inStream = File.OpenRead(sourceArchiveFileName))
                    using (Stream gzipStream = new GZipInputStream(inStream))
                    using (TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream))
                    {
                        tarArchive.ExtractContents(destinationDirectoryName);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static Task<bool> ExtractRar(string sourceArchiveFileName, string destinationDirectoryName)
        {
            return ExtractArchive(sourceArchiveFileName, destinationDirectoryName);
        }

        public static async Task<bool> ExtractArchive(string sourceArchiveFileName, string destinationDirectoryName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string extractionRoot = Path.GetFullPath(destinationDirectoryName);
                    Directory.CreateDirectory(extractionRoot);
                    string rootWithSeparator = extractionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var entries = new List<KeyValuePair<IArchiveEntry, string>>();

                    using (var archive = ArchiveFactory.Open(sourceArchiveFileName, new ReaderOptions()))
                    {
                        foreach (IArchiveEntry entry in archive.Entries)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Key) || Path.IsPathRooted(entry.Key)) { return false; }

                            string relativePath = entry.Key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                            if (relativePath.IndexOf(':') >= 0) { return false; }

                            string destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, relativePath));
                            if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(destinationPath, extractionRoot, StringComparison.OrdinalIgnoreCase)) { return false; }

                            entries.Add(new KeyValuePair<IArchiveEntry, string>(entry, destinationPath));
                        }

                        foreach (KeyValuePair<IArchiveEntry, string> item in entries)
                        {
                            if (item.Key.IsDirectory)
                            {
                                Directory.CreateDirectory(item.Value);
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(item.Value));
                            using (Stream source = item.Key.OpenEntryStream())
                            using (Stream destination = File.Create(item.Value)) { source.CopyTo(destination); }
                        }
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Extract .zip file async
        /// </summary>
        /// <param name="sourceArchiveFileName"></param>
        /// <param name="destinationDirectoryName"></param>
        /// <returns></returns>
        public static async Task<bool> ExtractZip(string sourceArchiveFileName, string destinationDirectoryName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    ZipFile.ExtractToDirectory(sourceArchiveFileName, destinationDirectoryName);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Delete file async
        /// </summary>
        /// <param name="sourceArchiveFileName"></param>
        /// <param name="destinationDirectoryName"></param>
        /// <returns></returns>
        public static async Task<bool> DeleteAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    File.Delete(path);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
