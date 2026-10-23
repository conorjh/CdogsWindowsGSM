using System.Diagnostics;
using System.Threading.Tasks;

namespace WindowsGSM.GameServer.Engine
{
    public interface ISharedInstallationProvider
    {
        string SharedInstallationAppId { get; }
        string SharedInstallationExecutableName { get; }
        Task<Process> InstallSharedBinaries();
        Task<bool> InstallConfigOnly();
    }
}