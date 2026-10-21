using System;
using System.Diagnostics;
using System.Globalization;
using WindowsGSM.Localization;

namespace WindowsGSM.Functions
{
    class ServerTable
    {
        public string ID { get; set; }
        public string PID { get; set; }
        public string Game { get; set; }
        public string Icon { get; set; }
        public string Status { get; set; }
        public MainWindow.ServerStatus StatusCode { get; set; }
        public string Name { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string QueryPort { get; set; }
        public int? DetectedQueryPort { get; set; }
        public string Defaultmap { get; set; }
        public string Maxplayers { get; set; }
        public int IDSort => ParseSortValue(ID);
        public int PIDSort => ParseSortValue(PID);
        public int PortSort => ParseSortValue(Port);
        public int QueryPortSort => DetectedQueryPort ?? ParseSortValue(QueryPort);
        public int PlayerCapacitySort => GetPlayerCapacity(Maxplayers);
        public int PlayerCapacity => GetPlayerCapacity(Maxplayers);
        public string QueryPortMismatchDisplay =>
            DetectedQueryPort.HasValue &&
            int.TryParse(QueryPort, out int configuredPort) &&
            DetectedQueryPort.Value != configuredPort
                ? $"\u2717 ({DetectedQueryPort.Value})"
                : string.Empty;
        public string QueryPortToolTip
        {
            get
            {
                if (!DetectedQueryPort.HasValue)
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("QueryPortNotVerified"), QueryPort);
                }

                if (!int.TryParse(QueryPort, out int configuredPort))
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("QueryPortDetected"), DetectedQueryPort.Value);
                }

                if (DetectedQueryPort.Value == configuredPort)
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("QueryPortMatches"), configuredPort);
                }

                return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("QueryPortMismatch"), configuredPort, DetectedQueryPort.Value);
            }
        }

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
                if (string.IsNullOrWhiteSpace(PID) || !int.TryParse(PID, out int pid))
                {
                    return string.Empty;
                }

                TimeSpan time;
                try
                {
                    time = DateTime.Now - Process.GetProcessById(pid).StartTime;
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
                catch (InvalidOperationException)
                {
                    return string.Empty;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return string.Empty;
                }
                catch (System.Security.SecurityException)
                {
                    return string.Empty;
                }
                catch (NotSupportedException)
                {
                    return string.Empty;
                }

                int numberOfDay = (int)time.TotalDays;
                string resourceKey = numberOfDay == 0 ? "UptimeZero" : numberOfDay == 1 ? "UptimeSingular" : "UptimePlural";
                return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get(resourceKey), numberOfDay, time.Hours, time.Minutes);
            }
        }
    }
}
