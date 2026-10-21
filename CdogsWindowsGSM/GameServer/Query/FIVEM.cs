using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;

namespace WindowsGSM.GameServer.Query
{
    public class FIVEM : IQueryPortDiscoveryProvider
    {
        private string _address;
        private int _port;
        private int _timeout;

        public QueryPortTransport Transport => QueryPortTransport.Tcp;

        public FIVEM() { }

        public FIVEM(string address, int port, int timeout = 5)
        {
            SetAddressPort(address, port, timeout);
        }

        public void SetAddressPort(string address, int port, int timeout = 5)
        {
            _address = address;
            _port = port;
            _timeout = Math.Max(1000, timeout * 1000);
        }

        public async Task<bool> RespondsOnPortAsync(string address, int port, int timeoutSeconds)
        {
            if (port < 1 || port > ushort.MaxValue) { return false; }

            var query = new FIVEM(address, port, timeoutSeconds);
            Dictionary<string, string> response = await query.GetInfo();
            return response != null &&
                response.ContainsKey("clients") &&
                response.ContainsKey("sv_maxclients");
        }

        /// <summary>Get general information of specific game server.</summary>
        public async Task<Dictionary<string, string>> GetInfo()
        {
            if (string.IsNullOrWhiteSpace(_address) || _port < 1 || _port > 65535)
            {
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var uriBuilder = new UriBuilder(Uri.UriSchemeHttp, _address, _port, "/dynamic.json");
                    var request = (HttpWebRequest)WebRequest.Create(uriBuilder.Uri);
                    request.Timeout = _timeout;
                    request.ReadWriteTimeout = _timeout;
                    request.Accept = "application/json";
                    request.UserAgent = "CdogsWindowsGSM";

                    using (WebResponse response = request.GetResponse())
                    {
                        using (Stream responseStream = response.GetResponseStream())
                        {
                            if (responseStream == null)
                            {
                                return null;
                            }

                            using (var reader = new StreamReader(responseStream))
                            {
                                JObject responseData = JObject.Parse(reader.ReadToEnd());
                                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                foreach (JProperty property in responseData.Properties())
                                {
                                    values[property.Name] = property.Value.Type == JTokenType.String
                                        ? property.Value.Value<string>()
                                        : property.Value.ToString(Formatting.None);
                                }
                                return values.Count == 0 ? null : values;
                            }
                        }
                    }
                }
                catch (WebException)
                {
                    return null;
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (UriFormatException)
                {
                    return null;
                }
            });
        }

        public async Task<string> GetPlayersAndMaxPlayers()
        {
            try
            {
                Dictionary<string, string> info = await GetInfo();
                if (info == null || !info.TryGetValue("clients", out string clients) ||
                    !info.TryGetValue("sv_maxclients", out string maxPlayers))
                {
                    return null;
                }
                return clients + '/' + maxPlayers;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
