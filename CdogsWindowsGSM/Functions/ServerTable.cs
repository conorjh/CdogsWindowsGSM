using System;
using System.Diagnostics;

namespace WindowsGSM.Functions
{
    class ServerTable
    {
        public string ID { get; set; }
        public string PID { get; set; }
        public string Game { get; set; }
        public string Icon { get; set; }
        public string Status { get; set; }
        public string Name { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string QueryPort { get; set; }
        public string Defaultmap { get; set; }
        public string Maxplayers { get; set; }
        public int IDSort => ParseSortValue(ID);
        public int PIDSort => ParseSortValue(PID);
        public int PortSort => ParseSortValue(Port);
        public int QueryPortSort => ParseSortValue(QueryPort);
        public int PlayerCapacitySort => GetPlayerCapacity(Maxplayers);
        public int PlayerCapacity => GetPlayerCapacity(Maxplayers);

        private static int ParseSortValue(string value)
        {
            return int.TryParse(value, out int number) ? number : int.MinValue;
        }

        private static int GetPlayerCapacity(string players)
        {
            if (string.IsNullOrWhiteSpace(players)) { return 0; }

            string[] playerCounts = players.Split('/');
            string capacity = playerCounts[playerCounts.Length - 1].Trim();
            return int.TryParse(capacity, out int number) && number > 0 ? number : 0;
        }

        public string Uptime 
        { 
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(PID) && int.TryParse(PID, out int pid))
                    {
                        var time = DateTime.Now - Process.GetProcessById(pid).StartTime;
                        int numberOfDay = (int)time.TotalDays;
                        return $"{numberOfDay} Day{(numberOfDay > 1 ? "s" : string.Empty)}, {time.Hours:D2}:{time.Minutes:D2}";
                    }
                }
                catch { }

                return string.Empty;
            }
        }
    }
}
