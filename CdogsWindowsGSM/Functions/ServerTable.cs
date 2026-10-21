using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using WindowsGSM.Localization;

namespace WindowsGSM.Functions
{
    class ServerTable : INotifyPropertyChanged
    {
        private int? _detectedQueryPort;
        private int? _detectedMainPort;
        private string _maxplayers;

        public event PropertyChangedEventHandler PropertyChanged;

        public string ID { get; set; }
        public string PID { get; set; }
        public string Game { get; set; }
        public string Icon { get; set; }
        public string Status { get; set; }
        public MainWindow.ServerStatus StatusCode { get; set; }
        public string Name { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public int? DetectedMainPort
        {
            get => _detectedMainPort;
            set
            {
                if (_detectedMainPort == value) { return; }

                _detectedMainPort = value;
                OnPropertyChanged(nameof(DetectedMainPort));
                OnPropertyChanged(nameof(PortSort));
                OnPropertyChanged(nameof(MainPortMismatchDisplay));
                OnPropertyChanged(nameof(MainPortToolTip));
            }
        }
        public string QueryPort { get; set; }
        public int? DetectedQueryPort
        {
            get => _detectedQueryPort;
            set
            {
                if (_detectedQueryPort == value) { return; }

                _detectedQueryPort = value;
                OnPropertyChanged(nameof(DetectedQueryPort));
                OnPropertyChanged(nameof(QueryPortSort));
                OnPropertyChanged(nameof(QueryPortMismatchDisplay));
                OnPropertyChanged(nameof(QueryPortToolTip));
            }
        }
        public string Defaultmap { get; set; }
        public string Maxplayers
        {
            get => _maxplayers;
            set
            {
                if (_maxplayers == value) { return; }

                _maxplayers = value;
                OnPropertyChanged(nameof(Maxplayers));
                OnPropertyChanged(nameof(PlayerCapacitySort));
                OnPropertyChanged(nameof(PlayerCapacity));
            }
        }
        public int IDSort => ParseSortValue(ID);
        public int PIDSort => ParseSortValue(PID);
        public int PortSort => DetectedMainPort ?? ParseSortValue(Port);
        public int QueryPortSort => DetectedQueryPort ?? ParseSortValue(QueryPort);
        public int PlayerCapacitySort => GetPlayerCapacity(Maxplayers);
        public int PlayerCapacity => GetPlayerCapacity(Maxplayers);
        public string MainPortMismatchDisplay =>
            DetectedMainPort.HasValue &&
            int.TryParse(Port, out int configuredPort) &&
            DetectedMainPort.Value != configuredPort
                ? $"\u2717 ({DetectedMainPort.Value})"
                : string.Empty;
        public string MainPortToolTip
        {
            get
            {
                if (!DetectedMainPort.HasValue)
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("MainPortNotVerified"), Port);
                }

                if (!int.TryParse(Port, out int configuredPort))
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("MainPortDetected"), DetectedMainPort.Value);
                }

                if (DetectedMainPort.Value == configuredPort)
                {
                    return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("MainPortMatches"), configuredPort);
                }

                return string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("MainPortMismatch"), configuredPort, DetectedMainPort.Value);
            }
        }
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

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
