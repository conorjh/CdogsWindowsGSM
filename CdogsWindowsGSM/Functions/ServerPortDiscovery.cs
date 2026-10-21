using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    public enum QueryPortTransport
    {
        Udp,
        Tcp
    }

    public interface IQueryPortDiscoveryProvider
    {
        QueryPortTransport Transport { get; }
        Task<bool> RespondsOnPortAsync(string address, int port, int timeoutSeconds);
    }

    public static class ServerPortDiscovery
    {
        private const int AddressFamilyIPv4 = 2;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorNoMoreFiles = 18;
        private const uint SnapshotProcesses = 0x00000002;
        private const int UdpTableOwnerPid = 1;
        private const int TcpTableOwnerPidListener = 3;
        private const uint TcpStateListen = 2;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        public static async Task<int?> FindQueryPortAsync(Process serverProcess, string address, int configuredPort, IQueryPortDiscoveryProvider provider)
        {
            if (serverProcess == null || provider == null || string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address, out IPAddress targetAddress) || targetAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            if (!IsRunning(serverProcess))
            {
                return null;
            }

            int rootProcessId = serverProcess.Id;
            HashSet<int> processIds = GetProcessTree(rootProcessId);
            List<BoundEndpoint> endpoints = GetEndpoints(provider.Transport);
            if (!IsRunning(serverProcess))
            {
                return null;
            }

            List<int> candidatePorts = endpoints
                .Where(endpoint => processIds.Contains(endpoint.ProcessId) && endpoint.Port > 0 && IsAddressRelevant(endpoint.LocalAddress, targetAddress))
                .Select(endpoint => endpoint.Port)
                .Distinct()
                .Where(port => !HasConflictingOwner(port, targetAddress, processIds, endpoints))
                .OrderBy(port => port)
                .ToList();

            if (candidatePorts.Count == 0)
            {
                return null;
            }

            if (configuredPort > 0 && configuredPort <= ushort.MaxValue && candidatePorts.Contains(configuredPort))
            {
                if (await provider.RespondsOnPortAsync(address, configuredPort, 1).ConfigureAwait(false))
                {
                    return configuredPort;
                }

                candidatePorts.Remove(configuredPort);
            }

            if (candidatePorts.Count == 0)
            {
                return null;
            }

            using (var semaphore = new SemaphoreSlim(8))
            {
                Task<int?>[] probes = candidatePorts.Select(async port =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        return await provider.RespondsOnPortAsync(address, port, 1).ConfigureAwait(false) ? (int?)port : null;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                int?[] matchingPorts = await Task.WhenAll(probes).ConfigureAwait(false);
                List<int> matches = matchingPorts.Where(port => port.HasValue).Select(port => port.Value).Distinct().ToList();
                return matches.Count == 1 ? matches[0] : (int?)null;
            }
        }

        private static bool IsRunning(Process process)
        {
            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsAddressRelevant(IPAddress localAddress, IPAddress targetAddress)
        {
            return targetAddress.Equals(IPAddress.Any) || localAddress.Equals(IPAddress.Any) || localAddress.Equals(targetAddress);
        }

        private static bool HasConflictingOwner(int port, IPAddress targetAddress, HashSet<int> processIds, List<BoundEndpoint> endpoints)
        {
            return endpoints.Any(endpoint => endpoint.Port == port &&
                !processIds.Contains(endpoint.ProcessId) &&
                IsAddressRelevant(endpoint.LocalAddress, targetAddress));
        }

        private static List<BoundEndpoint> GetEndpoints(QueryPortTransport transport)
        {
            switch (transport)
            {
                case QueryPortTransport.Udp:
                    return GetUdpEndpoints();
                case QueryPortTransport.Tcp:
                    return GetTcpEndpoints();
                default:
                    throw new ArgumentOutOfRangeException(nameof(transport));
            }
        }

        private static List<BoundEndpoint> GetUdpEndpoints()
        {
            int size;
            IntPtr table = GetUdpTable(out size);
            var endpoints = new List<BoundEndpoint>();
            if (table == IntPtr.Zero)
            {
                return endpoints;
            }

            try
            {
                int count = Marshal.ReadInt32(table);
                int rowSize = Marshal.SizeOf(typeof(MibUdpRowOwnerPid));
                for (int i = 0; i < count; i++)
                {
                    IntPtr rowPointer = IntPtr.Add(table, sizeof(uint) + i * rowSize);
                    var row = (MibUdpRowOwnerPid)Marshal.PtrToStructure(rowPointer, typeof(MibUdpRowOwnerPid));
                    endpoints.Add(new BoundEndpoint
                    {
                        LocalAddress = new IPAddress(BitConverter.GetBytes(row.LocalAddress)),
                        Port = GetHostPort(row.LocalPort),
                        ProcessId = unchecked((int)row.OwningProcessId)
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }

            return endpoints;
        }

        private static List<BoundEndpoint> GetTcpEndpoints()
        {
            int size;
            IntPtr table = GetTcpTable(out size);
            var endpoints = new List<BoundEndpoint>();
            if (table == IntPtr.Zero)
            {
                return endpoints;
            }

            try
            {
                int count = Marshal.ReadInt32(table);
                int rowSize = Marshal.SizeOf(typeof(MibTcpRowOwnerPid));
                for (int i = 0; i < count; i++)
                {
                    IntPtr rowPointer = IntPtr.Add(table, sizeof(uint) + i * rowSize);
                    var row = (MibTcpRowOwnerPid)Marshal.PtrToStructure(rowPointer, typeof(MibTcpRowOwnerPid));
                    if (row.State != TcpStateListen)
                    {
                        continue;
                    }

                    endpoints.Add(new BoundEndpoint
                    {
                        LocalAddress = new IPAddress(BitConverter.GetBytes(row.LocalAddress)),
                        Port = GetHostPort(row.LocalPort),
                        ProcessId = unchecked((int)row.OwningProcessId)
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }

            return endpoints;
        }

        private static int GetHostPort(uint networkPort)
        {
            short port = unchecked((short)(networkPort & 0xFFFF));
            return unchecked((ushort)IPAddress.NetworkToHostOrder(port));
        }

        private static IntPtr GetUdpTable(out int size)
        {
            size = 0;
            uint result = GetExtendedUdpTable(IntPtr.Zero, ref size, true, AddressFamilyIPv4, UdpTableOwnerPid, 0);
            if (result == 0 && size == 0)
            {
                return IntPtr.Zero;
            }
            if (result != ErrorInsufficientBuffer && result != 0)
            {
                throw new Win32Exception((int)result);
            }

            return ReadUdpTable(ref size);
        }

        private static IntPtr ReadUdpTable(ref int size)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr table = Marshal.AllocHGlobal(size);
                int tableSize = size;
                uint result = GetExtendedUdpTable(table, ref tableSize, true, AddressFamilyIPv4, UdpTableOwnerPid, 0);
                if (result == 0)
                {
                    size = tableSize;
                    return table;
                }

                Marshal.FreeHGlobal(table);
                if (result != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception((int)result);
                }

                size = tableSize;
            }

            throw new Win32Exception(ErrorInsufficientBuffer);
        }

        private static IntPtr GetTcpTable(out int size)
        {
            size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AddressFamilyIPv4, TcpTableOwnerPidListener, 0);
            if (result == 0 && size == 0)
            {
                return IntPtr.Zero;
            }
            if (result != ErrorInsufficientBuffer && result != 0)
            {
                throw new Win32Exception((int)result);
            }

            return ReadTcpTable(ref size);
        }

        private static IntPtr ReadTcpTable(ref int size)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr table = Marshal.AllocHGlobal(size);
                int tableSize = size;
                uint result = GetExtendedTcpTable(table, ref tableSize, true, AddressFamilyIPv4, TcpTableOwnerPidListener, 0);
                if (result == 0)
                {
                    size = tableSize;
                    return table;
                }

                Marshal.FreeHGlobal(table);
                if (result != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception((int)result);
                }

                size = tableSize;
            }

            throw new Win32Exception(ErrorInsufficientBuffer);
        }

        private static HashSet<int> GetProcessTree(int rootProcessId)
        {
            var childrenByParent = new Dictionary<int, List<int>>();
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandle)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var entry = new ProcessEntry32
                {
                    Size = (uint)Marshal.SizeOf(typeof(ProcessEntry32))
                };

                if (!Process32First(snapshot, ref entry))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorNoMoreFiles)
                    {
                        throw new Win32Exception(error);
                    }
                }
                else
                {
                    do
                    {
                        int processId = unchecked((int)entry.ProcessId);
                        int parentProcessId = unchecked((int)entry.ParentProcessId);
                        if (!childrenByParent.TryGetValue(parentProcessId, out List<int> children))
                        {
                            children = new List<int>();
                            childrenByParent.Add(parentProcessId, children);
                        }
                        children.Add(processId);
                    }
                    while (Process32Next(snapshot, ref entry));
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            var processIds = new HashSet<int> { rootProcessId };
            var pending = new Queue<int>();
            pending.Enqueue(rootProcessId);
            while (pending.Count > 0)
            {
                if (!childrenByParent.TryGetValue(pending.Dequeue(), out List<int> children))
                {
                    continue;
                }

                foreach (int child in children)
                {
                    if (processIds.Add(child))
                    {
                        pending.Enqueue(child);
                    }
                }
            }

            return processIds;
        }

        private sealed class BoundEndpoint
        {
            public IPAddress LocalAddress;
            public int Port;
            public int ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibUdpRowOwnerPid
        {
            public uint LocalAddress;
            public uint LocalPort;
            public uint OwningProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddress;
            public uint LocalPort;
            public uint RemoteAddress;
            public uint RemotePort;
            public uint OwningProcessId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint UsageCount;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint ThreadCount;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExecutableFile;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr udpTable, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
