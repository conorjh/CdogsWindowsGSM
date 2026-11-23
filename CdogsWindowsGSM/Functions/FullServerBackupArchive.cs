using System.IO.Compression;

namespace WindowsGSM.Functions;

public static class FullServerBackupArchive
{
    public static void CreateFromDirectory(string sourceDirectory, string archivePath)
    {
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
    }

    public static void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        ZipFile.ExtractToDirectory(archivePath, destinationDirectory);
    }
}
