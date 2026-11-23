using System;
using System.Reflection;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM;

public partial class MainWindow
{
    private void LogConfiguredA2SQueryPortConflicts(ServerTable server, object gameServer)
    {
        if (!HasA2SQueryMethod(gameServer)) { return; }

        int? queryPort = GetHealthQueryPort(server, gameServer, GetServerMetadata(server.ID));
        if (!queryPort.HasValue) { return; }

        foreach (ServerTable otherServer in ServerGroups_GetAllServerRows())
        {
            if (otherServer == null || string.Equals(otherServer.ID, server.ID, StringComparison.OrdinalIgnoreCase)) { continue; }

            ServerConfig otherConfig = new ServerConfig(otherServer.ID);
            object otherGameServer = GameServer.Data.Class.Get(otherServer.Game, otherConfig, PluginsList);
            if (!HasA2SQueryMethod(otherGameServer)) { continue; }

            int? otherQueryPort = GetHealthQueryPort(otherServer, otherGameServer, GetServerMetadata(otherServer.ID));
            if (otherQueryPort.HasValue && ServerPortDiscovery.AreConfiguredUdpEndpointsConflicting(
                server.IP,
                queryPort.Value,
                otherServer.IP,
                otherQueryPort.Value))
            {
                Log(
                    server.ID,
                    $"[NOTICE] Query port {queryPort.Value} is also configured for server #{otherServer.ID} ({otherServer.Name}). Assign A2S servers unique UDP query ports.");
            }
        }
    }

    private static bool HasA2SQueryMethod(object gameServer)
    {
        if (gameServer == null) { return false; }

        Type gameServerType = gameServer.GetType();
        object queryMethod = gameServerType.GetField("QueryMethod", BindingFlags.Instance | BindingFlags.Public)?.GetValue(gameServer)
            ?? gameServerType.GetProperty("QueryMethod", BindingFlags.Instance | BindingFlags.Public)?.GetValue(gameServer);
        return queryMethod is A2S;
    }
}
