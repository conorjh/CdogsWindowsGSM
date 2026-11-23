using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class ServerPortDiscoveryTests
{
    [TestMethod]
    public void IsTcpPortBoundToProcess_ReturnsTrueForListenerOwnedByProcess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using Process process = Process.GetCurrentProcess();

        Assert.IsTrue(ServerPortDiscovery.IsTcpPortBoundToProcess(process, port));
    }

    [TestMethod]
    public void FindClientPort_ReturnsConfiguredTcpPortOwnedByProcess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using Process process = Process.GetCurrentProcess();

        Assert.AreEqual(port, ServerPortDiscovery.FindClientPort(process, IPAddress.Loopback.ToString(), port, port + 1));
    }

    [TestMethod]
    public void FindClientPort_ReturnsConfiguredUdpPortOwnedByProcess()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        int port = ((IPEndPoint)listener.Client.LocalEndPoint).Port;
        using Process process = Process.GetCurrentProcess();

        Assert.AreEqual(port, ServerPortDiscovery.FindClientPort(process, IPAddress.Loopback.ToString(), port, port + 1));
    }
}