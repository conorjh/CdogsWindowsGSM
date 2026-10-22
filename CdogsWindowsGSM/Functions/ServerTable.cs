using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WindowsGSM.Localization;

namespace WindowsGSM.Functions
{
    class ServerTable : INotifyPropertyChanged
    {
        private int? _detectedMainPort;
        private string _maxplayers;
        private ServerHealthStatus _healthStatus = ServerHealthStatus.Unknown;
        private string _healthCompactDetails = string.Empty;
        private string _healthDetails = string.Empty;

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
                OnPropertyChanged(nameof(MainPortStatusDisplay));
                OnPropertyChanged(nameof(MainPortStatusBrush));
                OnPropertyChanged(nameof(MainPortToolTip));
            }
        }
        public string QueryPort { get; set; }
        public string Defaultmap { get; set; }
        public ServerHealthStatus HealthStatus
        {
            get => _healthStatus;
            set
            {
                if (_healthStatus == value) { return; }

                _healthStatus = value;
                OnPropertyChanged(nameof(HealthStatus));
                OnPropertyChanged(nameof(HealthStatusIcon));
                OnPropertyChanged(nameof(HealthStatusIconBrush));
            }
        }
        public string HealthCompactDetails
        {
            get => _healthCompactDetails;
            set
            {
                if (_healthCompactDetails == value) { return; }

                _healthCompactDetails = value;
                OnPropertyChanged(nameof(HealthCompactDetails));
            }
        }
        public string HealthDetails
        {
            get => _healthDetails;
            set
            {
                if (_healthDetails == value) { return; }

                _healthDetails = value;
                OnPropertyChanged(nameof(HealthDetails));
            }
        }
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
        public int QueryPortSort => ParseSortValue(QueryPort);
        public int PlayerCapacitySort => GetPlayerCapacity(Maxplayers);
        public int PlayerCapacity => GetPlayerCapacity(Maxplayers);
        public string MainPortStatusDisplay =>
            DetectedMainPort.HasValue && int.TryParse(Port, out int configuredPort)
                ? DetectedMainPort.Value == configuredPort ? "\u2713" : $"\u2717 ({DetectedMainPort.Value})"
                : string.Empty;
        public Brush MainPortStatusBrush =>
            DetectedMainPort.HasValue &&
            int.TryParse(Port, out int configuredPort) &&
            DetectedMainPort.Value == configuredPort
                ? Brushes.ForestGreen
                : Brushes.Firebrick;
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
        public string HealthStatusIcon
        {
            get
            {
                switch (HealthStatus)
                {
                    case ServerHealthStatus.Healthy: return "\u2713";
                    case ServerHealthStatus.Warning: return "\u26A0";
                    case ServerHealthStatus.Unhealthy: return "\u2717";
                    default: return "\u25CF";
                }
            }
        }
        public Brush HealthStatusIconBrush
        {
            get
            {
                switch (HealthStatus)
                {
                    case ServerHealthStatus.Healthy: return Brushes.ForestGreen;
                    case ServerHealthStatus.Warning: return Brushes.DarkOrange;
                    case ServerHealthStatus.Unhealthy: return Brushes.Firebrick;
                    default: return Brushes.Gray;
                }
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

    public enum ServerHealthStatus
    {
        Healthy,
        Warning,
        Unhealthy,
        Unknown
    }

    public sealed class ServerHealthContext
    {
        public bool ProcessExpectedToRun { get; private set; }
        public bool ProcessStateChanging { get; private set; }
        public bool ProcessExitedUnexpectedly { get; private set; }
        public bool? ProcessRunning { get; private set; }
        public bool? QuerySupported { get; private set; }
        public bool QueryInProgress { get; private set; }
        public int ConsecutiveQueryFailures { get; private set; }
        public DateTime? LastQueryAttemptAt { get; private set; }
        public DateTime? LastQuerySucceededAt { get; private set; }
        public string LastQueryFailureMessage { get; private set; }
        public DateTime CheckedAt { get; private set; }

        public ServerHealthContext(
            bool processExpectedToRun,
            bool processStateChanging,
            bool processExitedUnexpectedly,
            bool? processRunning,
            bool? querySupported,
            bool queryInProgress,
            int consecutiveQueryFailures,
            DateTime? lastQueryAttemptAt,
            DateTime? lastQuerySucceededAt,
            string lastQueryFailureMessage,
            DateTime checkedAt)
        {
            ProcessExpectedToRun = processExpectedToRun;
            ProcessStateChanging = processStateChanging;
            ProcessExitedUnexpectedly = processExitedUnexpectedly;
            ProcessRunning = processRunning;
            QuerySupported = querySupported;
            QueryInProgress = queryInProgress;
            ConsecutiveQueryFailures = consecutiveQueryFailures;
            LastQueryAttemptAt = lastQueryAttemptAt;
            LastQuerySucceededAt = lastQuerySucceededAt;
            LastQueryFailureMessage = lastQueryFailureMessage;
            CheckedAt = checkedAt;
        }
    }

    public sealed class ServerHealthCheckResult
    {
        public string CheckName { get; private set; }
        public ServerHealthStatus Status { get; private set; }
        public string Explanation { get; private set; }
        public DateTime? LastCheckedAt { get; private set; }

        public ServerHealthCheckResult(string checkName, ServerHealthStatus status, string explanation, DateTime? lastCheckedAt)
        {
            CheckName = checkName;
            Status = status;
            Explanation = explanation;
            LastCheckedAt = lastCheckedAt;
        }
    }

    public interface IServerHealthCheck
    {
        ServerHealthCheckResult Evaluate(ServerHealthContext context);
    }

    public sealed class ProcessServerHealthCheck : IServerHealthCheck
    {
        public ServerHealthCheckResult Evaluate(ServerHealthContext context)
        {
            if (context.ProcessStateChanging)
            {
                return new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Unknown, LocalizationManager.Get("HealthProcessTransition"), context.CheckedAt);
            }

            if (context.ProcessExitedUnexpectedly)
            {
                return new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Unhealthy, LocalizationManager.Get("HealthProcessExited"), context.CheckedAt);
            }

            if (!context.ProcessExpectedToRun)
            {
                return new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Unknown, LocalizationManager.Get("HealthProcessStopped"), context.CheckedAt);
            }

            if (!context.ProcessRunning.HasValue)
            {
                return new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Unknown, LocalizationManager.Get("HealthProcessUnavailable"), context.CheckedAt);
            }

            return context.ProcessRunning.Value
                ? new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Healthy, LocalizationManager.Get("HealthProcessRunning"), context.CheckedAt)
                : new ServerHealthCheckResult("HealthProcessCheck", ServerHealthStatus.Unhealthy, LocalizationManager.Get("HealthProcessExited"), context.CheckedAt);
        }
    }

    public sealed class QueryServerHealthCheck : IServerHealthCheck
    {
        private static readonly TimeSpan QueryStaleAfter = TimeSpan.FromSeconds(20);

        public ServerHealthCheckResult Evaluate(ServerHealthContext context)
        {
            if (context.QuerySupported != true)
            {
                return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Unknown, LocalizationManager.Get("HealthQueryUnsupported"), context.LastQueryAttemptAt);
            }

            if (!context.LastQueryAttemptAt.HasValue)
            {
                string explanation = context.QueryInProgress
                    ? LocalizationManager.Get("HealthQueryInProgress")
                    : LocalizationManager.Get("HealthQueryWaiting");
                return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Unknown, explanation, null);
            }

            if (context.ConsecutiveQueryFailures >= 3)
            {
                string explanation = string.IsNullOrWhiteSpace(context.LastQueryFailureMessage)
                    ? LocalizationManager.Get("HealthQueryRepeatedFailure")
                    : string.Format(LocalizationManager.Get("HealthQueryFailureReason"), context.LastQueryFailureMessage);
                return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Warning, explanation, context.LastQueryAttemptAt);
            }

            if (context.CheckedAt - context.LastQueryAttemptAt.Value > QueryStaleAfter)
            {
                return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Warning, LocalizationManager.Get("HealthQueryStale"), context.LastQueryAttemptAt);
            }

            if (context.LastQuerySucceededAt.HasValue && context.LastQuerySucceededAt.Value >= context.LastQueryAttemptAt.Value)
            {
                return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Healthy, LocalizationManager.Get("HealthQuerySucceeded"), context.LastQueryAttemptAt);
            }

            string failure = string.IsNullOrWhiteSpace(context.LastQueryFailureMessage)
                ? LocalizationManager.Get("HealthQueryFailed")
                : string.Format(LocalizationManager.Get("HealthQueryFailureReason"), context.LastQueryFailureMessage);
            return new ServerHealthCheckResult("HealthQueryCheck", ServerHealthStatus.Warning, failure, context.LastQueryAttemptAt);
        }
    }

    public sealed class ServerHealthSummary
    {
        public IList<ServerHealthCheckResult> Checks { get; private set; }

        public ServerHealthSummary(IEnumerable<ServerHealthCheckResult> checks)
        {
            Checks = checks.ToList();
        }

        public ServerHealthStatus OverallStatus
        {
            get
            {
                List<ServerHealthStatus> knownStatuses = Checks
                    .Select(check => check.Status)
                    .Where(status => status != ServerHealthStatus.Unknown)
                    .ToList();

                if (knownStatuses.Contains(ServerHealthStatus.Unhealthy)) { return ServerHealthStatus.Unhealthy; }
                if (knownStatuses.Contains(ServerHealthStatus.Warning)) { return ServerHealthStatus.Warning; }
                return knownStatuses.Count == 0 ? ServerHealthStatus.Unknown : ServerHealthStatus.Healthy;
            }
        }

        public string CompactDetails
        {
            get
            {
                return string.Join(" | ", Checks.Select(check => string.Format(
                    CultureInfo.CurrentUICulture,
                    "{0}: {1}",
                    LocalizationManager.Get(check.CheckName),
                    LocalizationManager.Get("Health" + check.Status))));
            }
        }

        public string Details
        {
            get
            {
                return string.Join(Environment.NewLine, Checks.Select(check =>
                {
                    string lastChecked = check.LastCheckedAt.HasValue
                        ? string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("HealthLastChecked"), check.LastCheckedAt.Value.ToString("g", CultureInfo.CurrentUICulture))
                        : LocalizationManager.Get("HealthNotChecked");
                    return string.Format(
                        CultureInfo.CurrentUICulture,
                        "{0}: {1} - {2} ({3})",
                        LocalizationManager.Get(check.CheckName),
                        LocalizationManager.Get("Health" + check.Status),
                        check.Explanation,
                        lastChecked);
                }));
            }
    }
    }
}
