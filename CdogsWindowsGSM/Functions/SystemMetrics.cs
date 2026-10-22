using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Management;

namespace WindowsGSM.Functions
{
    class SystemMetrics
    {
        public string CPUType { get; private set; }
        public int CPUCoreCount { get; private set; }
        public string RAMType { get; private set; }
        public double RAMTotalSize { get; private set; }
        public string DiskName { get; private set; }
        public string DiskType { get; private set; }
        public long DiskTotalSize { get; private set; }

        public void GetCPUStaticInfo()
        {
            try
            {
                var mbo = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor").Get();
                CPUType = mbo.Cast<ManagementBaseObject>().Select(c => c["Name"].ToString()).FirstOrDefault();
                CPUCoreCount = mbo.Cast<ManagementBaseObject>().Sum(x => int.Parse(x["NumberOfCores"].ToString()));
            }
            catch
            {
                CPUType = "Fail to get CPU Type";
                CPUCoreCount = -1;
            }
        }

        public void GetRAMStaticInfo()
        {
            try
            {
                const string RAM_TOTAL_MEMORY = "TotalVisibleMemorySize";
                RAMTotalSize = new ManagementObjectSearcher($"Select {RAM_TOTAL_MEMORY} from Win32_OperatingSystem").Get().Cast<ManagementObject>().Select(m => double.Parse(m[RAM_TOTAL_MEMORY].ToString())).FirstOrDefault();
                RAMType = GetMemoryType();
            }
            catch
            {
                RAMTotalSize = -1;
                RAMType = "Fail to get RAM Type";
            }
        }

        public void GetDiskStaticInfo(string disk = null)
        {
            disk = disk ?? Path.GetPathRoot(Process.GetCurrentProcess().MainModule.FileName);
            DiskName = disk.TrimEnd('\\');
            DiskType = DriveInfo.GetDrives().Where(x => (x.Name == disk) && x.IsReady).Select(x => x.DriveFormat).FirstOrDefault();
            DiskTotalSize = DriveInfo.GetDrives().Where(x => (x.Name == disk) && x.IsReady).Select(x => x.TotalSize).FirstOrDefault();
        }

        public double GetCPUUsage()
        {
            try
            {
                const string CPU_USAGE = "PercentProcessorTime";
                return double.Parse(new ManagementObjectSearcher($"SELECT {CPU_USAGE} FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'").Get().Cast<ManagementObject>().First().Properties[CPU_USAGE].Value.ToString());
            }
            catch
            {
                return 0;
            }
        }

        public double GetRAMUsage()
        {
            try
            {
                const string RAM_FREE_MEMORY = "FreePhysicalMemory";
                double freeMemory = new ManagementObjectSearcher($"Select {RAM_FREE_MEMORY} from Win32_OperatingSystem").Get().Cast<ManagementObject>().Select(m => double.Parse(m[RAM_FREE_MEMORY].ToString())).FirstOrDefault();
                return (RAMTotalSize == 0) ? 0 : (1 - freeMemory / RAMTotalSize) * 100;
            }
            catch
            {
                return 0;
            }
        }

        public double GetDiskUsage(string disk = null)
        {
            disk = disk ?? Path.GetPathRoot(Process.GetCurrentProcess().MainModule.FileName);
            double freeSpace = DriveInfo.GetDrives().Where(x => (x.Name == disk) && x.IsReady).Select(x => x.AvailableFreeSpace).FirstOrDefault();
            return (DiskTotalSize == 0) ? 0 : (1 - freeSpace / DiskTotalSize) * 100;
        }

        public static List<StorageHeadroom> GetStorageHeadroom(IEnumerable<StorageLocation> locations)
        {
            var labelsByVolume = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var unavailableLocations = new List<StorageHeadroom>();

            foreach (StorageLocation location in locations ?? Enumerable.Empty<StorageLocation>())
            {
                if (location == null) { continue; }

                if (!string.IsNullOrWhiteSpace(location.Error))
                {
                    unavailableLocations.Add(new StorageHeadroom(location.Description, location.Path, null, null, null, location.Error));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(location.Path))
                {
                    unavailableLocations.Add(new StorageHeadroom(location.Description, string.Empty, null, null, null, "The storage path is unavailable."));
                    continue;
                }

                string volumeRoot;
                try
                {
                    volumeRoot = Path.GetPathRoot(Path.GetFullPath(location.Path));
                }
                catch (Exception exception) when (IsExpectedStorageException(exception))
                {
                    unavailableLocations.Add(new StorageHeadroom(location.Description, location.Path, null, null, null, exception.Message));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(volumeRoot))
                {
                    unavailableLocations.Add(new StorageHeadroom(location.Description, location.Path, null, null, null, "The path does not identify a storage volume."));
                    continue;
                }

                if (!labelsByVolume.TryGetValue(volumeRoot, out List<string> labels))
                {
                    labels = new List<string>();
                    labelsByVolume.Add(volumeRoot, labels);
                }

                if (!labels.Contains(location.Description, StringComparer.OrdinalIgnoreCase))
                {
                    labels.Add(location.Description);
                }
            }

            var results = new List<StorageHeadroom>();
            foreach (KeyValuePair<string, List<string>> volume in labelsByVolume)
            {
                try
                {
                    var drive = new DriveInfo(volume.Key);
                    if (!drive.IsReady)
                    {
                        results.Add(new StorageHeadroom(string.Join(", ", volume.Value), volume.Key, null, null, null, "The drive is not ready."));
                        continue;
                    }

                    long totalBytes = drive.TotalSize;
                    long freeBytes = drive.AvailableFreeSpace;
                    double freePercent = totalBytes > 0 ? freeBytes * 100.0 / totalBytes : 0;
                    results.Add(new StorageHeadroom(string.Join(", ", volume.Value), drive.Name, freeBytes, totalBytes, freePercent, null));
                }
                catch (Exception exception) when (IsExpectedStorageException(exception))
                {
                    results.Add(new StorageHeadroom(string.Join(", ", volume.Value), volume.Key, null, null, null, exception.Message));
                }
            }

            results.AddRange(unavailableLocations);
            return results.OrderBy(result => result.Volume, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsExpectedStorageException(Exception exception)
        {
            return exception is ArgumentException ||
                exception is IOException ||
                exception is NotSupportedException ||
                exception is SecurityException ||
                exception is UnauthorizedAccessException;
        }

        public static string GetMemoryRatioString(double percent, double totalMemory)
        {
            int count = 0;
            while (totalMemory > 1024.0)
            {
                totalMemory /= 1024.0;
                count++;
            }

            return $"{string.Format("{0:0.00}", totalMemory * percent / 100)}/{string.Format("{0:0.00}", totalMemory)} {(count == 1 ? "MB" : count == 2 ? "GB" : "TB")} ";
        }

        public static string GetDiskRatioString(double percent, double totalDisk)
        {
            int count = 0;
            while (totalDisk > 1024.0)
            {
                totalDisk /= 1024.0;
                count++;
            }

            return $"{string.Format("{0:0.00}", totalDisk * percent / 100)}/{string.Format("{0:0.00}", totalDisk)} {(count == 1 ? "KB" : count == 2 ? "MB" : count == 3 ? "GB" : "TB")} ";
        }

        private string GetMemoryType()
        {
            const string RAM_MEMORY_TYPE = "MemoryType";
            var mbo = new ManagementObjectSearcher($"SELECT {RAM_MEMORY_TYPE} FROM Win32_PhysicalMemory").Get();
            switch (mbo.Cast<ManagementBaseObject>().Select(c => int.Parse(c[RAM_MEMORY_TYPE].ToString())).FirstOrDefault())
            {
                case 1: return "Other";
                case 2: return "DRAM";
                case 3: return "Synchronous DRAM";
                case 4: return "Cache DRAM";
                case 5: return "EDO";
                case 6: return "EDRAM";
                case 7: return "VRAM";
                case 8: return "SRAM";
                case 9: return "RAM";
                case 10: return "ROM";
                case 11: return "Flash";
                case 12: return "EEPROM";
                case 13: return "FEPROM";
                case 14: return "EPROM";
                case 15: return "CDRAM";
                case 16: return "3DRAM";
                case 17: return "SDRAM";
                case 18: return "SGRAM";
                case 19: return "RDRAM";
                case 20: return "DDR";
                case 21: return "DDR2";
                case 22: return "DDR2 FB-DIMM";
                case 23: return "Undefined 23";
                case 24: return "DDR3";
                case 25: return "Undefined 25";
                default: return "Unknown";
            }
        }
    }

    public sealed class StorageLocation
    {
        public string Description { get; private set; }
        public string Path { get; private set; }
        public string Error { get; private set; }

        public StorageLocation(string description, string path, string error = null)
        {
            Description = description ?? string.Empty;
            Path = path ?? string.Empty;
            Error = error;
        }
    }

    public sealed class StorageHeadroom
    {
        private const long LowSpaceThresholdBytes = 10L * 1024 * 1024 * 1024;

        public string Description { get; private set; }
        public string Volume { get; private set; }
        public long? FreeBytes { get; private set; }
        public long? TotalBytes { get; private set; }
        public double? FreePercent { get; private set; }
        public string Error { get; private set; }
        public bool IsLow => FreeBytes.HasValue && FreePercent.HasValue &&
            (FreeBytes.Value < LowSpaceThresholdBytes || FreePercent.Value < 10.0);

        public StorageHeadroom(string description, string volume, long? freeBytes, long? totalBytes, double? freePercent, string error)
        {
            Description = description;
            Volume = volume;
            FreeBytes = freeBytes;
            TotalBytes = totalBytes;
            FreePercent = freePercent;
            Error = error;
        }
    }
}
