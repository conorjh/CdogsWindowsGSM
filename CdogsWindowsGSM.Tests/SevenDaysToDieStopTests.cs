using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WindowsGSM.Functions;
using WindowsGSM.GameServer;

namespace CdogsWindowsGSM.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SevenDaysToDieStopTests
{
    [TestMethod]
    public async Task Stop_AuthenticatesThroughConfiguredTelnet_AndSendsShutdown()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.SevenDaysToDieStopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            using IDisposable pathScope = ServerPath.UseRootForTesting(testRoot);
            string serverId = "7dtd-stop-test";
            string configPath = ServerPath.GetServersServerFiles(serverId, "serverconfig.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            const string password = "local-test-password";
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                File.WriteAllText(configPath,
                    $"<ServerSettings><property name=\"TelnetEnabled\" value=\"true\" /><property name=\"TelnetPort\" value=\"{port}\" /><property name=\"TelnetPassword\" value=\"{password}\" /></ServerSettings>");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                Task<string> commandTask = ReceiveShutdownCommandAsync(listener, password, timeout.Token);
                try
                {
                    using Process process = Process.GetCurrentProcess();
                    var server = new SDTD(new ServerConfig(serverId));

                    await server.Stop(process);
                    string command = await commandTask.WaitAsync(TimeSpan.FromSeconds(5));

                    Assert.AreEqual("shutdown", command);
                }
                finally
                {
                    timeout.Cancel();
                    listener.Stop();
                    await ObserveServerTaskAsync(commandTask);
                }
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static async Task<string> ReceiveShutdownCommandAsync(TcpListener listener, string expectedPassword, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = client.GetStream();
        await WriteResponseAsync(stream, "Please enter password:\r\n", cancellationToken);

        string password = await ReadLineAsync(stream, cancellationToken);
        if (!string.Equals(password, expectedPassword, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The adapter did not send the configured Telnet password.");
        }

        await WriteResponseAsync(stream, "Logon successful.\r\n", cancellationToken);
        string command = await ReadLineAsync(stream, cancellationToken);
        await WriteResponseAsync(stream, "Shutting server down...\r\n", cancellationToken);
        return command;
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        byte[] buffer = new byte[1];

        while (true)
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("The adapter closed the Telnet connection before sending a line.");
            }

            if (buffer[0] == (byte)'\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append((char)buffer[0]);
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string response, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }

    private static async Task ObserveServerTaskAsync(Task<string> serverTask)
    {
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }
}