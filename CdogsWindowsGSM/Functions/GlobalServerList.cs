using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    internal static class GlobalServerList
    {
        private const string PublicIpEndpoint = "https://ipinfo.io/ip";
        private const string SteamServerListEndpoint = "https://api.steampowered.com/ISteamApps/GetServersAtAddress/v0001/";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan PublicIpCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ServerListCacheDuration = TimeSpan.FromSeconds(30);
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly SemaphoreSlim PublicIpGate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim ServerListGate = new SemaphoreSlim(1, 1);
        private static string _cachedPublicIp;
        private static DateTime _cachedPublicIpExpiresAtUtc;
        private static string _cachedServerListPublicIp;
        private static string _cachedServerListResponse;
        private static DateTime _cachedServerListExpiresAtUtc;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = RequestTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CdogsWindowsGSM");
            return client;
        }

        public static bool IsSupportedGame(string game)
        {
            return !string.IsNullOrWhiteSpace(game) &&
                !string.Equals(game, GameServer.MCPE.FullName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(game, GameServer.MC.FullName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(game, GameServer.PaperMC.FullName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(game, GameServer.ForgeMC.FullName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(game, GameServer.Luanti.FullName, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<GlobalServerListCheckResult> CheckServerAsync(string port, CancellationToken cancellationToken)
        {
            if (!int.TryParse(port, out int parsedPort) || parsedPort < 1 || parsedPort > ushort.MaxValue)
            {
                throw new InvalidDataException("The configured server query port is missing or invalid.");
            }

            string publicIp = await GetPublicIpAsync(cancellationToken);
            string response = await GetServerListResponseAsync(publicIp, cancellationToken);
            bool listed = ContainsServer(response, publicIp, parsedPort);
            return new GlobalServerListCheckResult(publicIp, listed);
        }

        private static async Task<string> GetServerListResponseAsync(string publicIp, CancellationToken cancellationToken)
        {
            if (IsCachedServerListValid(publicIp))
            {
                return _cachedServerListResponse;
            }

            await ServerListGate.WaitAsync(cancellationToken);
            try
            {
                if (IsCachedServerListValid(publicIp))
                {
                    return _cachedServerListResponse;
                }

                string endpoint = SteamServerListEndpoint + "?addr=" + Uri.EscapeDataString(publicIp) + "&format=json";
                string response = await HttpClient.GetStringAsync(endpoint, cancellationToken);
                _cachedServerListPublicIp = publicIp;
                _cachedServerListResponse = response;
                _cachedServerListExpiresAtUtc = DateTime.UtcNow.Add(ServerListCacheDuration);
                return response;
            }
            finally
            {
                ServerListGate.Release();
            }
        }

        internal static bool ContainsServer(string responseJson, string publicIp, int queryPort)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new InvalidDataException("Steam returned an empty server-list response.");
            }
            if (!IPAddress.TryParse(publicIp, out IPAddress parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("A public IPv4 address is required.", nameof(publicIp));
            }
            if (queryPort < 1 || queryPort > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(queryPort));
            }

            using JsonDocument document = JsonDocument.Parse(responseJson);
            if (!document.RootElement.TryGetProperty("response", out JsonElement responseElement) ||
                responseElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Steam returned an invalid server-list response.");
            }

            if (!responseElement.TryGetProperty("servers", out JsonElement servers))
            {
                throw new InvalidDataException("Steam's server-list response did not contain a server array.");
            }
            if (servers.ValueKind == JsonValueKind.Null)
            {
                return false;
            }
            if (servers.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Steam's server-list response contained an invalid server array.");
            }

            string expectedAddress = parsedIp + ":" + queryPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (JsonElement server in servers.EnumerateArray())
            {
                if (server.ValueKind == JsonValueKind.Object &&
                    server.TryGetProperty("addr", out JsonElement address) &&
                    address.ValueKind == JsonValueKind.String &&
                    string.Equals(address.GetString(), expectedAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<string> GetPublicIpAsync(CancellationToken cancellationToken)
        {
            if (IsCachedPublicIpValid())
            {
                return _cachedPublicIp;
            }

            await PublicIpGate.WaitAsync(cancellationToken);
            try
            {
                if (IsCachedPublicIpValid())
                {
                    return _cachedPublicIp;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);
                string response = await HttpClient.GetStringAsync(PublicIpEndpoint, timeout.Token);
                string publicIp = response.Trim();
                if (!IPAddress.TryParse(publicIp, out IPAddress parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new InvalidDataException("The public-IP service did not return an IPv4 address.");
                }

                _cachedPublicIp = parsedIp.ToString();
                _cachedPublicIpExpiresAtUtc = DateTime.UtcNow.Add(PublicIpCacheDuration);
                return _cachedPublicIp;
            }
            finally
            {
                PublicIpGate.Release();
            }
        }

        private static bool IsCachedPublicIpValid()
        {
            return !string.IsNullOrWhiteSpace(_cachedPublicIp) && DateTime.UtcNow < _cachedPublicIpExpiresAtUtc;
        }

        private static bool IsCachedServerListValid(string publicIp)
        {
            return !string.IsNullOrWhiteSpace(_cachedServerListResponse) &&
                string.Equals(_cachedServerListPublicIp, publicIp, StringComparison.Ordinal) &&
                DateTime.UtcNow < _cachedServerListExpiresAtUtc;
        }
    }

    internal sealed class GlobalServerListCheckResult
    {
        public string PublicIp { get; }
        public bool IsListed { get; }

        public GlobalServerListCheckResult(string publicIp, bool isListed)
        {
            PublicIp = publicIp;
            IsListed = isListed;
        }
    }
}
