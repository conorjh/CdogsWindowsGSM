using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Globalization;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Newtonsoft.Json.Linq;
using NCrontab;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using System.Management;
using System.Windows.Media.Imaging;
using ControlzEx.Theming;
using WindowsGSM.Functions;
using WindowsGSM.Localization;
using Label = System.Windows.Controls.Label;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows.Documents;
using MessageBox = System.Windows.MessageBox;

namespace WindowsGSM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetWindowText(IntPtr hWnd, string windowName);

        private static class RegistryKeyName
        {
            public const string HardWareAcceleration = "HardWareAcceleration";
            public const string UIAnimation = "UIAnimation";
            public const string DarkTheme = "DarkTheme";
            public const string StartOnBoot = "StartOnBoot";
            public const string RestartOnCrash = "RestartOnCrash";
            public const string ThemeColor = "ThemeColor";
            public const string SendStatistics = "SendStatistics";
            public const string Height = "Height";
            public const string Width = "Width";
            public const string DiscordBotAutoStart = "DiscordBotAutoStart";
        }

        public class ServerMetadata
        {
            public ServerStatus ServerStatus = ServerStatus.Stopped;
            public Process Process;
            public bool ProcessStartInfoAvailable;
            public int? DetectedMainPort;
            public int? DetectedMainPortProcessId;
            public bool ProcessExitedUnexpectedly;
            public bool? QuerySupported;
            public bool QueryInProgress;
            public int ConsecutiveQueryFailures;
            public DateTime? LastQueryAttemptAt;
            public DateTime? LastQuerySucceededAt;
            public string LastQueryFailureMessage;
            public IntPtr MainWindow;
            public ServerConsole ServerConsole;

            // Basic Game Server Settings
            public bool AutoRestart;
            public bool AutoStart;
            public bool AutoUpdate;
            public bool UpdateOnStart;
            public bool BackupOnStart;

            // Discord Alert Settings
            public bool DiscordAlert;
            public string DiscordMessage;
            public string DiscordWebhook;
            public bool AutoRestartAlert;
            public bool AutoStartAlert;
            public bool AutoUpdateAlert;
            public bool RestartCrontabAlert;
            public bool CrashAlert;

            // Restart Crontab Settings
            public bool RestartCrontab;
            public string CrontabFormat;

            // Game Server Start Priority and Affinity
            public string CPUPriority;
            public string CPUAffinity;

            public bool EmbedConsole;
            public bool AutoScroll;
        }

        private enum WindowShowStyle : uint
        {
            Hide = 0,
            ShowNormal = 1,
            Show = 5,
            Minimize = 6,
            ShowMinNoActivate = 7
        }

        public enum ServerStatus
        {
            Started = 0,
            Starting = 1,
            Stopped = 2,
            Stopping = 3,
            Restarted = 4,
            Restarting = 5,
            Updated = 6,
            Updating = 7,
            Backuped = 8,
            Backuping = 9,
            Restored = 10,
            Restoring = 11,
            Deleting = 12
        }

        public static readonly string WGSM_VERSION = "v" + System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly()).InformationalVersion;
        public static readonly int MAX_SERVER = 256;
        public static readonly string WGSM_PATH = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        public static readonly string DEFAULT_THEME = "Emerald";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/conorjh/CdogsWindowsGSM/releases/latest";
        private const string UpdaterAssetUrl = "https://github.com/conorjh/CdogsWindowsGSM/releases/latest/download/CdogsWindowsGSM-Updater.exe";
        private const string UpdaterFileName = "CdogsWindowsGSM-Updater.exe";

        private readonly NotifyIcon notifyIcon;
        private Process Installer;

        public static readonly Dictionary<int, ServerMetadata> _serverMetadata = new Dictionary<int, ServerMetadata>();
        private ServerMetadata GetServerMetadata(object serverId) => _serverMetadata.TryGetValue(int.Parse(serverId.ToString()), out var s) ? s : null;

        public List<PluginMetadata> PluginsList = new List<PluginMetadata>();

        private readonly List<System.Windows.Controls.CheckBox> _checkBoxes = new List<System.Windows.Controls.CheckBox>();
        private readonly ObservableCollection<ISeries> _playerChartSeries = new ObservableCollection<ISeries>();
        private readonly Dictionary<string, (ColumnSeries<ObservableValue> Series, ObservableValue Value)> _playerChartValues =
            new Dictionary<string, (ColumnSeries<ObservableValue>, ObservableValue)>(StringComparer.Ordinal);
        private readonly Flyout[] _flyouts;
        private readonly Dictionary<Flyout, IInputElement> _flyoutFocusAtOpen = new Dictionary<Flyout, IInputElement>();
        private readonly Axis _playerChartXAxis = new Axis { Labels = Array.Empty<string>() };
        private readonly Axis _playerChartYAxis = new Axis { MinLimit = 0, MaxLimit = 10 };

        private int _peakActivePlayers;
        private IInputElement _focusBeforeFlyout;
        private bool _flyoutFocusCaptured;
        private readonly Random _mapRotationRandom = new Random();
        private readonly System.Windows.Threading.DispatcherTimer _backupScheduleTimer = new System.Windows.Threading.DispatcherTimer();
        private bool _isLoadingBackupScheduleEditor;
        private bool _backupScheduleTimeChanged;
        private DateTime _backupScheduleStartedAt;
        private DateTime _backupScheduleLastTickAt;
        private bool _backupScheduleTickRunning;
        private bool _editConfigQueryUsesServerPort;
        private readonly IServerHealthCheck[] _serverHealthChecks =
        {
            new ProcessServerHealthCheck(),
            new QueryServerHealthCheck()
        };

        private readonly DiscordBot.Bot g_DiscordBot = new DiscordBot.Bot();

        public MainWindow(bool showCrashHint)
        {
            //Add SplashScreen
            var splashScreen = new SplashScreen("Images/SplashScreen.png");
            splashScreen.Show(false, true);
            DiscordWebhook.SendErrorLog();

            InitializeComponent();
            _flyouts = new[]
            {
                MahAppFlyout_ViewPlugins,
                MahAppFlyout_Settings,
                MahAppFlyout_DiscordAlert,
                MahAppFlyout_RestoreBackup,
                MahAppFlyout_SetAffinity,
                MahAppFlyout_EditConfig,
                MahAppFlyout_ManageAddons,
                MahAppFlyout_InstallGameServer,
                MahAppFlyout_ImportGameServer,
                MahAppFlyout_ServerGroups
            };
            RegisterFlyoutFocusHandlers();
            _playerChartYAxis.Name = LocalizationManager.Get("Players");
            livechart_players.Series = _playerChartSeries;
            livechart_players.XAxes = new[] { _playerChartXAxis };
            livechart_players.YAxes = new[] { _playerChartYAxis };
            Title = $"Cdog's WindowsGSM {WGSM_VERSION}";
            comboBox_Language.SelectionChanged -= ComboBox_Language_SelectionChanged;
            comboBox_Language.SelectedValue = LocalizationManager.SelectedLanguage;
            comboBox_Language.SelectionChanged += ComboBox_Language_SelectionChanged;

            //Close SplashScreen
            splashScreen.Close(new TimeSpan(0, 0, 1));

            // Add all themes to comboBox_Themes
            ThemeManager.Current.Themes.Select(t => Path.GetExtension(t.Name).Trim('.')).Distinct().OrderBy(x => x).ToList().ForEach(delegate (string name) { comboBox_Themes.Items.Add(name); });

            // Set up _serverMetadata
            for (int i = 0; i <= MAX_SERVER; i++)
            {
                _serverMetadata[i] = new ServerMetadata
                {
                    ServerStatus = ServerStatus.Stopped,
                    ServerConsole = new ServerConsole(i)
                };
            }

            var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM");
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowsGSM");
                key.SetValue(RegistryKeyName.HardWareAcceleration, "True");
                key.SetValue(RegistryKeyName.UIAnimation, "True");
                key.SetValue(RegistryKeyName.DarkTheme, "False");
                key.SetValue(RegistryKeyName.StartOnBoot, "False");
                key.SetValue(RegistryKeyName.RestartOnCrash, "False");
                key.SetValue(RegistryKeyName.ThemeColor, DEFAULT_THEME);
                key.SetValue(RegistryKeyName.SendStatistics, "True");
                key.SetValue(RegistryKeyName.Height, Height);
                key.SetValue(RegistryKeyName.Width, Width);
                key.SetValue(RegistryKeyName.DiscordBotAutoStart, "False");
            }

            MahAppSwitch_HardWareAcceleration.IsOn = (key.GetValue(RegistryKeyName.HardWareAcceleration) ?? true).ToString() == "True";
            MahAppSwitch_UIAnimation.IsOn = (key.GetValue(RegistryKeyName.UIAnimation) ?? true).ToString() == "True";
            MahAppSwitch_DarkTheme.IsOn = (key.GetValue(RegistryKeyName.DarkTheme) ?? false).ToString() == "True";
            MahAppSwitch_StartOnBoot.IsOn = (key.GetValue(RegistryKeyName.StartOnBoot) ?? false).ToString() == "True";
            MahAppSwitch_RestartOnCrash.IsOn = (key.GetValue(RegistryKeyName.RestartOnCrash) ?? false).ToString() == "True";
            MahAppSwitch_SendStatistics.IsOn = (key.GetValue(RegistryKeyName.SendStatistics) ?? true).ToString() == "True";
            MahAppSwitch_DiscordBotAutoStart.IsOn = (key.GetValue(RegistryKeyName.DiscordBotAutoStart) ?? false).ToString() == "True";
            string color = (key.GetValue(RegistryKeyName.ThemeColor) ?? key.GetValue("DonorColor") ?? string.Empty).ToString();
            comboBox_Themes.SelectionChanged -= ComboBox_Themes_SelectionChanged;
            comboBox_Themes.SelectedItem = comboBox_Themes.Items.Contains(color) ? color : DEFAULT_THEME;
            comboBox_Themes.SelectionChanged += ComboBox_Themes_SelectionChanged;

            Height = (key.GetValue(RegistryKeyName.Height) == null) ? Height : double.Parse(key.GetValue(RegistryKeyName.Height).ToString());
            Width = (key.GetValue(RegistryKeyName.Width) == null) ? Width : double.Parse(key.GetValue(RegistryKeyName.Width).ToString());
            key.Close();

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsOn ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsOn;
            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem}");
            //Not required - it is set in windows settings
            //SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked ?? false);
            if (MahAppSwitch_DiscordBotAutoStart.IsOn)
            {
                switch_DiscordBot.IsOn = true;
            }

            // Add items to Set Affinity Flyout
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                StackPanel stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(15, 0, 0, 0)
                };

                string cpuLabel = string.Format(CultureInfo.CurrentUICulture, "{0} {1}", LocalizationManager.Get("Cpu"), i);
                System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox
                {
                    IsTabStop = true
                };
                AutomationProperties.SetName(checkBox, cpuLabel);
                _checkBoxes.Add(checkBox);
                var label = new Label
                {
                    Content = cpuLabel,
                    Target = checkBox,
                    Padding = new Thickness(0, 5, 0, 5)
                };
                AutomationProperties.SetLabeledBy(checkBox, label);

                stackPanel.Children.Add(_checkBoxes[i]);
                stackPanel.Children.Add(label);
                StackPanel_SetAffinity.Children.Add(stackPanel);
            }

            // Add click listener on each checkBox
            foreach (var checkBox in _checkBoxes)
            {
                checkBox.Click += (sender, e) =>
                {
                    var server = (ServerTable)ServerGrid.SelectedItem;
                    if (server == null) { return; }

                CheckPrioity:
                    string priority = string.Empty;
                    for (int i = _checkBoxes.Count - 1; i >= 0; i--)
                    {
                        priority += (_checkBoxes[i].IsChecked ?? false) ? "1" : "0";
                    }

                    if (!priority.Contains("1"))
                    {
                        checkBox.IsChecked = true;
                        goto CheckPrioity;
                    }

                    string affinityDisplay = Functions.CPU.Affinity.GetAffinityValidatedString(priority);
                    AutomationProperties.SetItemStatus(button_SetAffinity, affinityDisplay);

                    _serverMetadata[int.Parse(server.ID)].CPUAffinity = priority;
                    ServerConfig.SetSetting(server.ID, "cpuaffinity", priority);

                    if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(priority);
                    }
                };
            }

            notifyIcon = new NotifyIcon
            {
                BalloonTipTitle = "WindowsGSM",
                BalloonTipText = LocalizationManager.Get("RunningInBackground"),
                Text = "WindowsGSM",
                BalloonTipIcon = ToolTipIcon.Info,
                Visible = true
            };

            notifyIcon.Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/WindowsGSM-Icon.ico")).Stream);
            notifyIcon.BalloonTipClicked += OnBalloonTipClick;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            ServerPath.CreateAndFixDirectories();

            LoadPlugins(shouldAwait: false);
            AddGamesToComboBox();

            LoadServerTable();

            if (ServerGrid.Items.Count > 0)
            {
                ServerGrid.SelectedItem = ServerGrid.Items[0];
            }

            foreach (var server in ServerGroups_GetAllServerRows())
            {
                int pid = ServerCache.GetPID(server.ID);
                if (pid != -1)
                {
                    Process p = null;
                    try
                    {
                        p = Process.GetProcessById(pid);
                    }
                    catch
                    {
                        continue;
                    }

                    string pName = ServerCache.GetProcessName(server.ID);
                    if (!string.IsNullOrWhiteSpace(pName) && p.ProcessName == pName)
                    {
                        ServerConfig recoveredConfig = new ServerConfig(server.ID);
                        if (!string.IsNullOrWhiteSpace(recoveredConfig.SharedInstallationId))
                        {
                            FileStream sharedLease = null;
                            try
                            {
                                SharedInstallationStore.EnsureReady(recoveredConfig.SharedInstallationId);
                                sharedLease = SharedInstallationStore.AcquireReadLease(recoveredConfig.SharedInstallationId);
                                SharedInstallationStore.ReleaseLeaseWhenProcessExits(p, sharedLease);
                                sharedLease = null;
                            }
                            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                            {
                                sharedLease?.Dispose();
                                Log(server.ID, "[ERROR] Could not restore the shared installation lock: " + exception.Message);
                            }
                        }

                        ServerMetadata metadata = GetServerMetadata(server.ID);
                        metadata.Process = p;
                        metadata.ProcessStartInfoAvailable = false;
                        IntPtr cachedWindow = ServerCache.GetWindowsIntPtr(server.ID);
                        if (!Functions.ServerConsole.HasUsableWindow(p, cachedWindow))
                        {
                            cachedWindow = p.MainWindowHandle;
                        }
                        metadata.MainWindow = Functions.ServerConsole.HasUsableWindow(p, cachedWindow) ? cachedWindow : IntPtr.Zero;
                        metadata.ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started");

                        /*// Get Console process - untested
                        Process console = GetConsoleProcess(pid);
                        if (console != null)
                        {
                            ReadConsoleOutput(server.ID, console);
                        }
                        */

                        Process process = p;
                        p.Exited += (sender, e) => OnGameServerExited(server, process);

                        StartAutoUpdateCheck(server);
                        StartRestartCrontabCheck(server);
                        StartSendHeartBeat(server);
                        StartPortDiscoveryAndQuery(server, p);
                    }
                }
            }

            if (showCrashHint)
            {
                string logFile = $"CRASH_{DateTime.Now:yyyyMMdd}.log";
                Log("System", string.Format(LocalizationManager.Get("CrashLogHint"), logFile));
            }

            AutoStartServer();

            if (MahAppSwitch_SendStatistics.IsOn)
            {
                SendGoogleAnalytics();
            }

            StartConsoleRefresh();

            StartServerTableRefresh();

            StartDashBoardRefresh();

            StartBackupScheduleScheduler();
        }

        private Process GetConsoleProcess(int processId)
        {
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={processId}");
                foreach (ManagementObject mo in mos.Get())
                {
                    Process p = Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]));
                    if (Equals(p, "conhost"))
                    {
                        return p;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        // Read console redirect output - not tested
        private async void ReadConsoleOutput(string serverId, Process p)
        {
            await Task.Run(() =>
            {
                var reader = p.StandardOutput;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        GetServerMetadata(serverId).ServerConsole.Add(line);
                    });
                }
            });
        }

        public void AddGamesToComboBox()
        {
            comboBox_InstallGameServer.Items.Clear();
            comboBox_ImportGameServer.Items.Clear();

            //Add games to ComboBox
            SortedList sortedList = new SortedList();
            List<DictionaryEntry> gameName = GameServer.Data.Icon.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.CurrentUICulture, true, true).Cast<DictionaryEntry>().ToList();
            gameName.ForEach(delegate (DictionaryEntry entry) { sortedList.Add(entry.Key, $"/CdogsWindowsGSM;component/{entry.Value}"); });
            int pluginLoaded = 0;
            PluginsList.ForEach(delegate (PluginMetadata plugin)
            {
                if (plugin.IsLoaded && !sortedList.ContainsKey(plugin.FullName))
                {
                    pluginLoaded++;
                    sortedList.Add(plugin.FullName, plugin.GameImage == PluginManagement.DefaultPluginImage ? plugin.GameImage.Replace("pack://application:,,,", "/CdogsWindowsGSM;component") : plugin.GameImage);
                }
            });

            label_GameServerCount.Content = $"{gameName.Count + pluginLoaded} game servers supported";

            for (int i = 0; i < sortedList.Count; i++)
            {
                var row = new Images.Row
                {
                    Image = sortedList.GetByIndex(i).ToString(),
                    Name = sortedList.GetKey(i).ToString()
                };

                comboBox_InstallGameServer.Items.Add(row);
                comboBox_ImportGameServer.Items.Add(row);
            }
        }

        public async void LoadPlugins(bool shouldAwait = true)
        {
            var pm = new PluginManagement();
            PluginsList = await pm.LoadPlugins(shouldAwait);

            foreach (PluginMetadata plugin in PluginsList)
            {
                foreach (string diagnostic in plugin.CacheDiagnostics)
                {
                    Log("Plugins", $"{plugin.FileName} cache: {diagnostic}");
                }
            }

            int loadedCount = 0;
            PluginsList.ForEach(delegate (PluginMetadata plugin)
            {
                if (!plugin.IsLoaded)
                {
                    Directory.CreateDirectory(ServerPath.GetLogs(ServerPath.FolderName.Plugins));
                    string logFile = ServerPath.GetLogs(ServerPath.FolderName.Plugins, $"{plugin.FileName}.log");
                    File.WriteAllText(ServerPath.GetLogs(logFile), plugin.Error);
                    Log("Plugins", $"{plugin.FileName} fail to load. Please view the log: {logFile.Replace(WGSM_PATH, string.Empty)}");
                }
                else
                {
                    loadedCount++;
                    Log("Plugins", $"{plugin.FileName} loaded with API v{plugin.ApiVersion}; capabilities: {plugin.Capabilities}.");
                    var converter = new BrushConverter();
                    Brush brush;
                    try
                    {
                        brush = (Brush)converter.ConvertFromString(plugin.Plugin.color);
                    }
                    catch
                    {
                        brush = Brushes.DimGray;
                    }

                    var borderBase = new Border
                    {
                        BorderBrush = brush,
                        Background = Brushes.SlateGray,
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6),
                        Margin = new Thickness(10, 0, 10, 10)
                    };
                    DockPanel.SetDock(borderBase, Dock.Top);
                    var dockPanelBase = new DockPanel();
                    var gameImage = new Border
                    {
                        BorderBrush = Brushes.White,
                        Background = new ImageBrush
                        {
                            Stretch = Stretch.Fill,
                            ImageSource = plugin.GameImage == PluginManagement.DefaultPluginImage ? PluginManagement.GetDefaultPluginBitmapSource() : new BitmapImage(new Uri(plugin.GameImage))
                        },
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10),
                        Width = 63,
                        Height = 63,
                        MinWidth = 63,
                        MinHeight = 63,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    dockPanelBase.Children.Add(gameImage);

                    var dockPanel = new DockPanel { Margin = new Thickness(0, 0, 3, 0) };
                    DockPanel.SetDock(dockPanel, Dock.Top);
                    var label = new Label { Content = $"v{plugin.Plugin.version}", Padding = new Thickness(0) };
                    DockPanel.SetDock(label, Dock.Right);
                    dockPanel.Children.Add(label);
                    label = new Label { Content = plugin.Plugin.name.Split('.')[1], Padding = new Thickness(0), FontSize = 14, FontWeight = FontWeights.Bold };
                    DockPanel.SetDock(label, Dock.Left);
                    dockPanel.Children.Add(label);
                    dockPanelBase.Children.Add(dockPanel);

                    var textBlock = new TextBlock { Text = plugin.Plugin.description };
                    DockPanel.SetDock(textBlock, Dock.Top);
                    dockPanelBase.Children.Add(textBlock);

                    var stackPanelBase = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                    var authorImage = new Border
                    {
                        Background = new ImageBrush
                        {
                            Stretch = Stretch.Fill,
                            ImageSource = plugin.AuthorImage == PluginManagement.DefaultUserImage ? PluginManagement.GetDefaultUserBitmapSource() : new BitmapImage(new Uri(plugin.AuthorImage))
                        },
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(30),
                        Padding = new Thickness(10),
                        Width = 25,
                        Height = 25,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    stackPanelBase.Children.Add(authorImage);
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    label = new Label { Content = plugin.Plugin.author, Padding = new Thickness(0) };
                    DockPanel.SetDock(label, Dock.Top);
                    stackPanel.Children.Add(label);
                    label = new Label { Content = "•", Padding = new Thickness(0), Margin = new Thickness(5, 0, 5, 0) };
                    DockPanel.SetDock(label, Dock.Top);
                    stackPanel.Children.Add(label);
                    textBlock = new TextBlock();
                    var hyperlink = new Hyperlink(new Run(plugin.Plugin.url)) { Foreground = brush };
                    try
                    {
                        hyperlink.NavigateUri = new Uri(plugin.Plugin.url);
                        hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                    }
                    catch { }

                    textBlock.Inlines.Add(hyperlink);
                    stackPanel.Children.Add(textBlock);
                    stackPanelBase.Children.Add(stackPanel);
                    dockPanelBase.Children.Add(stackPanelBase);

                    borderBase.Child = dockPanelBase;
                    StackPanel_PluginList.Children.Add(borderBase);
                }
            });

            AddGamesToComboBox();

            Label_PluginInstalled.Content = PluginsList.Count.ToString();
            Label_PluginLoaded.Content = loadedCount.ToString();
            Label_PluginFailed.Content = (PluginsList.Count - loadedCount).ToString();

            Log("Plugins", $"Installed: {PluginsList.Count}, Loaded: {loadedCount}, Failed: {PluginsList.Count - loadedCount}");
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            OpenWithShell(e.Uri.AbsoluteUri);
        }

        private async void ImportPlugin_Click(object sender, RoutedEventArgs e)
        {
            // If a server is installing or import => return
            if (progressbar_InstallProgress.IsIndeterminate || progressbar_ImportProgress.IsIndeterminate)
            {
                MessageBox.Show(LocalizationManager.Get("BusyDuringImportInstall"), LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string pluginsDir = ServerPath.FolderName.Plugins;

            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Filter = "zip files (*.zip)|*.zip";
            ofd.InitialDirectory = pluginsDir;

            DialogResult dr = ofd.ShowDialog();
            if (dr == System.Windows.Forms.DialogResult.OK)
            {
                Button_ImportPlugin.IsEnabled = false;
                ProgressRing_LoadPlugins.Visibility = Visibility.Visible;
                Label_PluginInstalled.Content = "-";
                Label_PluginLoaded.Content = "-";
                Label_PluginFailed.Content = "-";
                StackPanel_PluginList.Children.Clear();

                /// This is relying on it keeps the naming shceme of the ZIP files that're downloaed from GitHub releases. Like WindowsGSM.Spigot-1.0.zip,
                /// Just by following WindowsGSM naming of plugins, and this will be fine!
                string zipPath = ofd.FileName;
                string dirName = ofd.SafeFileName.Split('.')[1].Split('-')[0] + ".cs";
                string knownPattern = ".cs";
                // Unziping plugin
                using (ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    var result = from entry in zip.Entries
                                 where Path.GetDirectoryName(entry.FullName).Contains(knownPattern)
                                 where !String.IsNullOrEmpty(entry.Name)
                                 select entry;

                    Directory.CreateDirectory(Path.Combine(pluginsDir, dirName));
                    foreach (ZipArchiveEntry entryFile in result)
                    {
                        entryFile.ExtractToFile(Path.Combine(pluginsDir, dirName, entryFile.Name), true);
                    }
                }

                await Task.Delay(500);
                LoadPlugins();
                LoadServerTable();

                Button_ImportPlugin.IsEnabled = true;
                ProgressRing_LoadPlugins.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            // If a server is installing or import => return
            if (progressbar_InstallProgress.IsIndeterminate || progressbar_ImportProgress.IsIndeterminate)
            {
                MessageBox.Show(LocalizationManager.Get("BusyDuringImportInstall"), LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Button_RefreshPlugins.IsEnabled = false;
            ProgressRing_LoadPlugins.Visibility = Visibility.Visible;
            Label_PluginInstalled.Content = "-";
            Label_PluginLoaded.Content = "-";
            Label_PluginFailed.Content = "-";
            StackPanel_PluginList.Children.Clear();

            await Task.Delay(500);
            LoadPlugins();
            LoadServerTable();

            Button_RefreshPlugins.IsEnabled = true;
            ProgressRing_LoadPlugins.Visibility = Visibility.Collapsed;
        }

        public void LoadServerTable()
        {
            ServerGroups_BeginServerReload();
            string[] livePlayerData = new string[MAX_SERVER + 1];
            foreach (ServerTable item in ServerGrid.Items)
            {
                livePlayerData[int.Parse(item.ID)] = item.Maxplayers;
            }

            var selectedrow = (ServerTable)ServerGrid.SelectedItem;
            ServerGrid.Items.Clear();

            //Add server to datagrid
            for (int i = 1; i <= MAX_SERVER; i++)
            {
                string serverid_path = Path.Combine(WGSM_PATH, "servers", i.ToString());
                if (!Directory.Exists(serverid_path)) { continue; }

                string configpath = ServerPath.GetServersConfigs(i.ToString(), "WindowsGSM.cfg");
                if (!File.Exists(configpath)) { continue; }

                var serverConfig = new ServerConfig(i.ToString());

                object gameServer = GameServer.Data.Class.Get(serverConfig.ServerGame, pluginList: PluginsList);
                if (gameServer == null) { continue; }

                ServerMetadata metadata = GetServerMetadata(i);
                int? detectedMainPort = GetDetectedMainPort(metadata);

                ServerStatus statusCode = GetServerMetadata(i).ServerStatus;
                if (!Enum.IsDefined(typeof(ServerStatus), statusCode))
                {
                    statusCode = ServerStatus.Stopped;
                    _serverMetadata[i].ServerStatus = statusCode;
                }
                string status = LocalizationManager.Get("Status" + statusCode);

                try
                {
                    string icon = GameServer.Data.Icon.ResourceManager.GetString(serverConfig.ServerGame);
                    if (icon == null)
                    {
                        PluginsList.ForEach(delegate (PluginMetadata plugin)
                        {
                            if (plugin.FullName == serverConfig.ServerGame && plugin.IsLoaded)
                            {
                                icon = plugin.GameImage == PluginManagement.DefaultPluginImage
                                    ? plugin.GameImage.Replace("pack://application:,,,", "/CdogsWindowsGSM;component")
                                    : plugin.GameImage;
                            }
                        });
                    }
                    if (icon == null)
                    {
                        icon = PluginManagement.DefaultPluginImage.Replace("pack://application:,,,", "/CdogsWindowsGSM;component");
                    }

                    string serverId = i.ToString();
                    string pidString = string.Empty;

                    try
                    {
                        pidString = Process.GetProcessById(ServerCache.GetPID(serverId)).Id.ToString();
                    }
                    catch { }

                    var server = new ServerTable
                    {
                        ID = i.ToString(),
                        PID = pidString,
                        Game = serverConfig.ServerGame,
                        Icon = icon,
                        Status = status,
                        StatusCode = statusCode,
                        Name = serverConfig.ServerName,
                        IP = serverConfig.ServerIP,
                        Port = serverConfig.ServerPort,
                        DetectedMainPort = detectedMainPort,
                        QueryPort = GetQueryPortDisplay(serverConfig, gameServer, detectedMainPort),
                        Defaultmap = serverConfig.ServerMap,
                        Maxplayers = (metadata.ServerStatus != ServerStatus.Started) ? serverConfig.ServerMaxPlayer : livePlayerData[i]
                    };

                    RefreshServerHealth(server);
                    SaveServerConfigToServerMetadata(i, serverConfig);
                    ServerGrid.Items.Add(server);

                    if (selectedrow != null)
                    {
                        if (selectedrow.ID == server.ID)
                        {
                            ServerGrid.SelectedItem = server;
                        }
                    }

                }
                catch
                {
                    // ignore
                }
            }

            grid_action.Visibility = (ServerGrid.Items.Count != 0) ? Visibility.Visible : Visibility.Hidden;
            label_select.Visibility = grid_action.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
            ServerGroups_EndServerReload();
        }

        private int? GetDetectedMainPort(ServerMetadata metadata)
        {
            if (metadata == null || metadata.Process == null || !metadata.DetectedMainPort.HasValue) { return null; }

            try
            {
                return !metadata.Process.HasExited && metadata.DetectedMainPortProcessId == metadata.Process.Id
                    ? metadata.DetectedMainPort
                    : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool QueryUsesServerPort(object gameServer)
        {
            return gameServer is GameServer.Engine.Source ||
                gameServer is GameServer.Engine.GoldSource ||
                gameServer is GameServer.MCBE;
        }

        private static string GetQueryPortDisplay(ServerConfig serverConfig, object gameServer, int? detectedMainPort)
        {
            return QueryUsesServerPort(gameServer)
                ? detectedMainPort?.ToString() ?? serverConfig.ServerPort
                : serverConfig.ServerQueryPort;
        }

        private int? GetHealthQueryPort(ServerTable server, object gameServer, ServerMetadata metadata)
        {
            string queryPort = QueryUsesServerPort(gameServer)
                ? GetDetectedMainPort(metadata)?.ToString() ?? server.Port
                : server.QueryPort;

            return int.TryParse(queryPort, out int parsedPort) && IsValidPort(queryPort)
                ? parsedPort
                : (int?)null;
        }

        private ServerHealthSummary GetServerHealthSummary(ServerMetadata metadata)
        {
            if (metadata == null)
            {
                return new ServerHealthSummary(Enumerable.Empty<ServerHealthCheckResult>());
            }

            bool? processRunning = false;
            if (metadata.Process != null)
            {
                try
                {
                    processRunning = !metadata.Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    processRunning = null;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    processRunning = null;
                }
                catch (System.Security.SecurityException)
                {
                    processRunning = null;
                }
                catch (NotSupportedException)
                {
                    processRunning = null;
                }
            }

            ServerStatus status = metadata.ServerStatus;
            bool stateChanging = status == ServerStatus.Starting ||
                status == ServerStatus.Stopping ||
                status == ServerStatus.Restarting ||
                status == ServerStatus.Updating ||
                status == ServerStatus.Restoring ||
                status == ServerStatus.Backuping ||
                status == ServerStatus.Deleting;
            var context = new ServerHealthContext(
                status == ServerStatus.Started,
                stateChanging,
                metadata.ProcessExitedUnexpectedly,
                processRunning,
                metadata.QuerySupported,
                metadata.QueryInProgress,
                metadata.ConsecutiveQueryFailures,
                metadata.LastQueryAttemptAt,
                metadata.LastQuerySucceededAt,
                metadata.LastQueryFailureMessage,
                DateTime.Now);

            return new ServerHealthSummary(_serverHealthChecks.Select(check => check.Evaluate(context)));
        }

        private void RefreshServerHealth(ServerTable server)
        {
            if (server == null) { return; }

            ServerHealthSummary summary = GetServerHealthSummary(GetServerMetadata(server.ID));
            ServerTable selectedServer = ServerGrid.SelectedItem as ServerTable;
            bool selectedServerHealthChanged = selectedServer != null && selectedServer.ID == server.ID &&
                selectedServer.HealthStatus != summary.OverallStatus;

            SetServerHealthDisplay(server, summary);

            foreach (ServerTable row in ServerGroups_GetAllServerRows())
            {
                if (row != null && row.ID == server.ID)
                {
                    SetServerHealthDisplay(row, summary);
                    break;
                }
            }

            if (selectedServer != null && selectedServer.ID == server.ID)
            {
                label_ServerStatus.Text = selectedServer.Status;
                AutomationProperties.SetName(label_ServerStatus, selectedServer.Status);
                label_ServerHealthDetails.Text = summary.Details;
                label_ServerHealthDetails.ToolTip = summary.Details;
                AutomationProperties.SetName(label_ServerHealthDetails, string.Format(
                    CultureInfo.CurrentUICulture,
                    "{0}: {1}",
                    LocalizationManager.Get("ServerHealth"),
                    summary.CompactDetails));
                AutomationProperties.SetHelpText(label_ServerHealthDetails, summary.Details);
                AutomationProperties.SetLiveSetting(label_ServerHealthDetails, AutomationLiveSetting.Polite);
                if (selectedServerHealthChanged) { RaiseLiveRegionChanged(label_ServerHealthDetails); }
            }
        }

        private static void SetServerHealthDisplay(ServerTable server, ServerHealthSummary summary)
        {
            server.HealthStatus = summary.OverallStatus;
            server.HealthCompactDetails = summary.CompactDetails;
            server.HealthDetails = summary.Details;
        }

        private static void ResetServerQueryHealth(ServerMetadata metadata)
        {
            if (metadata == null) { return; }

            metadata.QuerySupported = null;
            metadata.QueryInProgress = false;
            metadata.ConsecutiveQueryFailures = 0;
            metadata.LastQueryAttemptAt = null;
            metadata.LastQuerySucceededAt = null;
            metadata.LastQueryFailureMessage = null;
        }

        private void ClearDetectedMainPort(ServerTable server)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            if (metadata != null)
            {
                metadata.DetectedMainPort = null;
                metadata.DetectedMainPortProcessId = null;
            }

            RefreshServerMainPortDisplay(server, null);
        }

        private void RefreshServerMainPortDisplay(ServerTable server, int? detectedPort)
        {
            server.DetectedMainPort = detectedPort;
            if (QueryUsesServerPort(GameServer.Data.Class.Get(server.Game, pluginList: PluginsList)))
            {
                server.QueryPort = detectedPort?.ToString() ?? server.Port;
            }

            foreach (ServerTable row in ServerGroups_GetAllServerRows())
            {
                if (row != null && row.ID == server.ID)
                {
                    row.DetectedMainPort = detectedPort;
                    row.QueryPort = server.QueryPort;
                    return;
                }
            }
        }

        private void SaveServerConfigToServerMetadata(object serverId, ServerConfig serverConfig)
        {
            int i = int.Parse(serverId.ToString());

            // Basic Game Server Settings
            _serverMetadata[i].AutoRestart = serverConfig.AutoRestart;
            _serverMetadata[i].AutoStart = serverConfig.AutoStart;
            _serverMetadata[i].AutoUpdate = serverConfig.AutoUpdate;
            _serverMetadata[i].UpdateOnStart = serverConfig.UpdateOnStart;
            _serverMetadata[i].BackupOnStart = serverConfig.BackupOnStart;

            // Discord Alert Settings
            _serverMetadata[i].DiscordAlert = serverConfig.DiscordAlert;
            _serverMetadata[i].DiscordMessage = serverConfig.DiscordMessage;
            _serverMetadata[i].DiscordWebhook = serverConfig.DiscordWebhook;
            _serverMetadata[i].AutoRestartAlert = serverConfig.AutoRestartAlert;
            _serverMetadata[i].AutoStartAlert = serverConfig.AutoStartAlert;
            _serverMetadata[i].AutoUpdateAlert = serverConfig.AutoUpdateAlert;
            _serverMetadata[i].RestartCrontabAlert = serverConfig.RestartCrontabAlert;
            _serverMetadata[i].CrashAlert = serverConfig.CrashAlert;

            // Restart Crontab Settings
            _serverMetadata[i].RestartCrontab = serverConfig.RestartCrontab;
            _serverMetadata[i].CrontabFormat = serverConfig.CrontabFormat;

            // Game Server Start Priority and Affinity
            _serverMetadata[i].CPUPriority = serverConfig.CPUPriority;
            _serverMetadata[i].CPUAffinity = serverConfig.CPUAffinity;

            _serverMetadata[i].EmbedConsole = serverConfig.EmbedConsole;
            _serverMetadata[i].AutoScroll = serverConfig.AutoScroll;
        }

        private async void AutoStartServer()
        {
            foreach (ServerTable server in ServerGroups_GetAllServerRows())
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(serverId).AutoStart && GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server, " | Auto Start");

                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoStartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Started | Auto Start", server.Name, server.IP, server.Port);
                        }
                    }
                }
            }
        }

        private async void StartServerTableRefresh()
        {
            while (true)
            {
                await Task.Delay(60 * 1000);
                ServerGrid.Items.Refresh();
            }
        }

        private async void StartConsoleRefresh()
        {
            while (true)
            {
                await Task.Delay(10);
                var row = (ServerTable)ServerGrid.SelectedItem;
                if (row != null)
                {
                    string text = GetServerMetadata(int.Parse(row.ID)).ServerConsole.Get();
                    if (text.Length != console.Text.Length && text != console.Text)
                    {
                        console.Text = text;

                        if (GetServerMetadata(row.ID).AutoScroll)
                        {
                            console.ScrollToEnd();
                        }
                    }
                }
            }
        }

        private async void StartDashBoardRefresh()
        {
            var system = new SystemMetrics();

            // Get CPU info and Set
            await Task.Run(() => system.GetCPUStaticInfo());
            dashboard_cpu_type.Content = system.CPUType;

            // Get RAM info and Set
            await Task.Run(() => system.GetRAMStaticInfo());
            dashboard_ram_type.Content = system.RAMType;

            // Get Disk info and Set
            await Task.Run(() => system.GetDiskStaticInfo());
            dashboard_disk_name.Content = $"({system.DiskName})";
            dashboard_disk_type.Content = system.DiskType;

            while (true)
            {
                dashboard_cpu_bar.Value = await Task.Run(() => system.GetCPUUsage());
                dashboard_cpu_bar.Value = (dashboard_cpu_bar.Value > 100.0) ? 100.0 : dashboard_cpu_bar.Value;
                dashboard_cpu_usage.Content = $"{dashboard_cpu_bar.Value}%";

                dashboard_ram_bar.Value = await Task.Run(() => system.GetRAMUsage());
                dashboard_ram_bar.Value = (dashboard_ram_bar.Value > 100.0) ? 100.0 : dashboard_ram_bar.Value;
                dashboard_ram_usage.Content = $"{string.Format("{0:0.00}", dashboard_ram_bar.Value)}%";
                dashboard_ram_ratio.Content = SystemMetrics.GetMemoryRatioString(dashboard_ram_bar.Value, system.RAMTotalSize);

                dashboard_disk_bar.Value = await Task.Run(() => system.GetDiskUsage());
                dashboard_disk_bar.Value = (dashboard_disk_bar.Value > 100.0) ? 100.0 : dashboard_disk_bar.Value;
                dashboard_disk_usage.Content = $"{string.Format("{0:0.00}", dashboard_disk_bar.Value)}%";
                dashboard_disk_ratio.Content = SystemMetrics.GetDiskRatioString(dashboard_disk_bar.Value, system.DiskTotalSize);

                List<ServerTable> allServers = ServerGroups_GetAllServerRows();
                dashboard_servers_bar.Value = allServers.Count * 100.0 / MAX_SERVER;
                dashboard_servers_bar.Value = (dashboard_servers_bar.Value > 100.0) ? 100.0 : dashboard_servers_bar.Value;
                dashboard_servers_usage.Content = $"{string.Format("{0:0.00}", dashboard_servers_bar.Value)}%";
                dashboard_servers_ratio.Content = $"{allServers.Count}/{MAX_SERVER}";

                int startedCount = allServers.Count(server => server.StatusCode == ServerStatus.Started);
                dashboard_started_bar.Value = allServers.Count == 0 ? 0 : startedCount * 100.0 / allServers.Count;
                dashboard_started_bar.Value = (dashboard_started_bar.Value > 100.0) ? 100.0 : dashboard_started_bar.Value;
                dashboard_started_usage.Content = $"{string.Format("{0:0.00}", dashboard_started_bar.Value)}%";
                dashboard_started_ratio.Content = $"{startedCount}/{allServers.Count}";

                int activePlayers = allServers
                    .Where(server => server.Maxplayers != null && server.Maxplayers.Contains('/'))
                    .Sum(server => int.TryParse(server.Maxplayers.Split('/')[0], out int count) ? count : 0);
                _peakActivePlayers = Math.Max(_peakActivePlayers, activePlayers);
                dashboard_players_count.Content = $"{activePlayers} / {_peakActivePlayers}";

                Refresh_DashBoard_LiveChart();

                await Task.Delay(2000);
            }
        }

        public int GetStartedServerCount()
        {
            return ServerGroups_GetAllServerRows().Count(server => server.StatusCode == ServerStatus.Started);
        }

        public int GetActivePlayers()
        {
            return ServerGroups_GetAllServerRows()
                .Where(server => server.Maxplayers != null && server.Maxplayers.Contains('/'))
                .Sum(server => int.TryParse(server.Maxplayers.Split('/')[0], out int count) ? count : 0);
        }

        private void Refresh_DashBoard_LiveChart()
        {
            // List<(ServerType, PlayerCount)> Example: ("Ricochet Dedicated Server", 0)
            List<(string, int)> typePlayers = ServerGroups_GetAllServerRows()
                .Where(s => s.StatusCode == ServerStatus.Started && s.Maxplayers != null && s.Maxplayers.Contains("/"))
                .Select(s => (type: s.Game, players: int.TryParse(s.Maxplayers.Split('/')[0], out int players) ? players : 0))
                .GroupBy(s => s.type)
                .Select(s => (type: s.Key, players: s.Sum(p => p.players)))
                .ToList();

            string chartHelpText = typePlayers.Count == 0
                ? string.Format(CultureInfo.CurrentUICulture, "{0}: 0", LocalizationManager.Get("Players"))
                : string.Join(Environment.NewLine, typePlayers.Select(item => string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}: {1}",
                    item.Item1,
                    item.Item2)));
            AutomationProperties.SetHelpText(livechart_players, chartHelpText);

            // Ajust the maxvalue of axis Y base on PlayerCount
            if (typePlayers.Count > 0)
            {
                int maxValue = typePlayers.Select(s => s.Item2).Max() + 5;
                _playerChartYAxis.MaxLimit = (maxValue > 10) ? maxValue : 10;
            }

            var activeTypes = new HashSet<string>(typePlayers.Select(item => item.Item1), StringComparer.Ordinal);
            foreach (string inactiveType in _playerChartValues.Keys.Where(type => !activeTypes.Contains(type)).ToList())
            {
                _playerChartSeries.Remove(_playerChartValues[inactiveType].Series);
                _playerChartValues.Remove(inactiveType);
            }

            foreach (var item in typePlayers)
            {
                if (_playerChartValues.TryGetValue(item.Item1, out var chartValue))
                {
                    if (chartValue.Value.Value != item.Item2)
                    {
                        chartValue.Value.Value = item.Item2;
                    }
                }
                else
                {
                    var value = new ObservableValue(item.Item2);
                    var series = new ColumnSeries<ObservableValue>
                    {
                        Name = item.Item1,
                        Values = new ObservableCollection<ObservableValue> { value }
                    };
                    _playerChartValues.Add(item.Item1, (series, value));
                    _playerChartSeries.Add(series);
                }
            }
        }

        private async void SendGoogleAnalytics()
        {
            var analytics = new GoogleAnalytics();
            analytics.SendWindowsOS();
            analytics.SendWindowsGSMVersion();
            analytics.SendProcessorName();
            analytics.SendRAM();
            analytics.SendDisk();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save height and width
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue("Height", Height.ToString());
                key?.SetValue("Width", Width.ToString());
            }

            // Get rid of system tray icon
            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            // Stop Discord Bot
            g_DiscordBot.Stop().ConfigureAwait(false);
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateConfigurationBackupMenuVisibility(ServerGrid.SelectedItem as ServerTable);
            if (ServerGrid.SelectedIndex != -1)
            {
                DataGrid_RefreshElements();
            }
        }

        private void UpdateConfigurationBackupMenuVisibility(ServerTable server)
        {
            if (menuItem_ConfigurationBackup != null)
            {
                menuItem_ConfigurationBackup.Visibility = IsConfigurationBackupSupported(server?.Game)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ServerRowContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.ContextMenu contextMenu = sender as System.Windows.Controls.ContextMenu;
            if (contextMenu == null) { return; }

            DataGridRow row = contextMenu.PlacementTarget as DataGridRow;
            ServerTable server = row?.Item as ServerTable ?? ServerGrid.SelectedItem as ServerTable;
            System.Windows.Controls.MenuItem backupMenuItem = contextMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                .FirstOrDefault(item => item.Name == "menuItem_Backup_Context");
            System.Windows.Controls.MenuItem configurationBackupItem = backupMenuItem?.Items
                .OfType<System.Windows.Controls.MenuItem>()
                .FirstOrDefault(item => item.Name == "menuItem_ConfigurationBackup_Context");
            if (configurationBackupItem != null)
            {
                configurationBackupItem.Visibility = IsConfigurationBackupSupported(server?.Game)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ServerGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.ContextMenu = (System.Windows.Controls.ContextMenu)ServerGrid.Resources["ServerRowContextMenu"];
        }

        private void ServerGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject originalSource = e.OriginalSource as DependencyObject;
            DataGridRow row = originalSource == null
                ? null
                : ItemsControl.ContainerFromElement(ServerGrid, originalSource) as DataGridRow;

            if (row != null)
            {
                ServerGrid.SelectedItem = row.Item;
            }
        }

        private void DataGrid_RefreshElements()
        {
            var row = (ServerTable)ServerGrid.SelectedItem;

            if (row != null)
            {
                Console.WriteLine("Datagrid Changed");

                if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Stopped)
                {
                    button_Start.IsEnabled = true;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = true;
                    button_Backup.IsEnabled = true;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }
                else if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Started)
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = true;
                    button_Restart.IsEnabled = true;
                    ServerMetadata metadata = GetServerMetadata(row.ID);
                    Process p = metadata.Process;
                    bool processRunning = p != null && !p.HasExited;
                    bool hasConsoleWindow = processRunning && Functions.ServerConsole.HasUsableWindow(p, metadata.MainWindow);
                    bool standardInputRedirected = processRunning && Functions.ServerConsole.IsStandardInputRedirected(p);
                    button_Console.IsEnabled = processRunning && hasConsoleWindow
                        && !Functions.ServerConsole.IsCreatedWithoutWindow(p)
                        && !Functions.ServerConsole.IsStandardOutputRedirected(p);
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    bool canSendConsoleCommand = processRunning && (standardInputRedirected || hasConsoleWindow);
                    textbox_servercommand.IsEnabled = canSendConsoleCommand;
                    button_servercommand.IsEnabled = canSendConsoleCommand;
                }
                else
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }

                switch (GetServerMetadata(row.ID).ServerStatus)
                {
                    case ServerStatus.Restarting:
                    case ServerStatus.Restarted:
                    case ServerStatus.Started:
                    case ServerStatus.Starting:
                    case ServerStatus.Stopping:
                        button_Kill.IsEnabled = true;
                        break;
                    default: button_Kill.IsEnabled = false; break;
                }

                button_ManageAddons.IsEnabled = ServerAddon.IsGameSupportManageAddons(row.Game) &&
                    string.IsNullOrWhiteSpace(new ServerConfig(row.ID).SharedInstallationId);
                if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Deleting || GetServerMetadata(row.ID).ServerStatus == ServerStatus.Restoring)
                {
                    button_ManageAddons.IsEnabled = false;
                }

                slider_ProcessPriority.Value = Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(row.ID).CPUPriority);
                textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

                string affinityDisplay = Functions.CPU.Affinity.GetAffinityValidatedString(GetServerMetadata(row.ID).CPUAffinity);
                AutomationProperties.SetItemStatus(button_SetAffinity, affinityDisplay);
                string affinity = new string(affinityDisplay.Reverse().ToArray());
                for (int i = 0; i < _checkBoxes.Count; i++)
                {
                    _checkBoxes[i].IsChecked = affinity[i] == '1';
                }

                label_ServerStatus.Text = row.Status;
                AutomationProperties.SetName(label_ServerStatus, row.Status);
                label_ServerHealthDetails.Text = row.HealthDetails;
                label_ServerHealthDetails.ToolTip = row.HealthDetails;
                AutomationProperties.SetName(label_ServerHealthDetails, string.Format(
                    CultureInfo.CurrentUICulture,
                    "{0}: {1}",
                    LocalizationManager.Get("ServerHealth"),
                    row.HealthStatusDisplay));
                AutomationProperties.SetHelpText(label_ServerHealthDetails, row.HealthDetails);

                var gameServer = GameServer.Data.Class.Get(row.Game, pluginList: PluginsList);
                switch_embedconsole.IsEnabled = gameServer.AllowsEmbedConsole;
                switch_embedconsole.IsOn = gameServer.AllowsEmbedConsole ? GetServerMetadata(row.ID).EmbedConsole : false;
                SetAutoScrollButtonStatus(GetServerMetadata(row.ID).AutoScroll);

                switch_autorestart.IsOn = GetServerMetadata(row.ID).AutoRestart;
                switch_restartcrontab.IsOn = GetServerMetadata(row.ID).RestartCrontab;
                switch_autostart.IsOn = GetServerMetadata(row.ID).AutoStart;
                switch_autoupdate.IsOn = GetServerMetadata(row.ID).AutoUpdate;
                switch_updateonstart.IsOn = GetServerMetadata(row.ID).BackupOnStart;
                switch_backuponstart.IsOn = GetServerMetadata(row.ID).BackupOnStart;
                switch_discordalert.IsOn = GetServerMetadata(row.ID).DiscordAlert;
                button_discordtest.IsEnabled = switch_discordalert.IsOn;

                textBox_restartcrontab.Text = GetServerMetadata(row.ID).CrontabFormat;
                textBox_nextcrontab.Text = CrontabSchedule.TryParse(textBox_restartcrontab.Text)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");

                MahAppSwitch_AutoStartAlert.IsOn = GetServerMetadata(row.ID).AutoStartAlert;
                MahAppSwitch_AutoRestartAlert.IsOn = GetServerMetadata(row.ID).AutoRestartAlert;
                MahAppSwitch_AutoUpdateAlert.IsOn = GetServerMetadata(row.ID).AutoUpdateAlert;
                MahAppSwitch_RestartCrontabAlert.IsOn = GetServerMetadata(row.ID).RestartCrontabAlert;
                MahAppSwitch_CrashAlert.IsOn = GetServerMetadata(row.ID).CrashAlert;
            }
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGroups_GetAllServerRows().Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            OpenFlyout(MahAppFlyout_InstallGameServer);

            if (!progressbar_InstallProgress.IsIndeterminate)
            {
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = string.Empty;
                button_Install.IsEnabled = true;
                checkBox_Install_Cod1MeatBots.IsChecked = false;
                numericUpDown_Install_Cod1AlliesBots.Value = 0;
                numericUpDown_Install_Cod1AxisBots.Value = 0;
                passwordBox_Install_CfxLicenseKey.Password = string.Empty;
                checkBox_Install_SharedInstallation.IsChecked = false;
                checkBox_Install_SharedInstallation.IsEnabled = true;

                ComboBox_InstallGameServer_SelectionChanged(sender, null);

                var newServerConfig = new ServerConfig(null);
                textbox_InstallServerName.Text = $"WindowsGSM - Server #{newServerConfig.ServerID}";
            }
        }

        private static bool IsCod1Game(string gameName)
        {
            return string.Equals(gameName, GameServer.COD1.FullName, StringComparison.Ordinal);
        }

        private static bool IsCfxGame(string gameName)
        {
            return string.Equals(gameName, GameServer.GTA5.FullName, StringComparison.Ordinal) ||
                string.Equals(gameName, GameServer.RedM.FullName, StringComparison.Ordinal);
        }

        private static bool IsRustGame(string gameName)
        {
            return string.Equals(gameName, GameServer.RUST.FullName, StringComparison.Ordinal);
        }

        private async void Button_Install_Click(object sender, RoutedEventArgs e)
        {
            if (Installer != null)
            {
                if (!Installer.HasExited) { Installer.Kill(); }
                Installer = null;
            }

            var selectedgame = (Images.Row)comboBox_InstallGameServer.SelectedItem;
            if (string.IsNullOrWhiteSpace(textbox_InstallServerName.Text))
            {
                SetAccessibleLiveText(textblock_InstallProgress, LocalizationManager.Get("ServerNameRequired"));
                textbox_InstallServerName.Focus();
                return;
            }
            if (selectedgame == null)
            {
                SetAccessibleLiveText(textblock_InstallProgress, LocalizationManager.Get("GameServerRequired"));
                comboBox_InstallGameServer.Focus();
                return;
            }

            var newServerConfig = new ServerConfig(null);
            string servername = textbox_InstallServerName.Text;
            string servergame = selectedgame.Name;
            bool isRust = IsRustGame(servergame);
            bool isBedrock = string.Equals(servergame, GameServer.MCBE.FullName, StringComparison.Ordinal);
            bool useSharedInstallation = checkBox_Install_SharedInstallation.IsChecked == true;
            object gameServerObject = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);
            dynamic gameServer = gameServerObject;
            GameServer.Engine.ISharedInstallationProvider sharedProvider = null;
            PluginSharedInstallationAdapter pluginSharedProvider = null;
            string sharedInstallationAppId = null;
            string sharedInstallationExecutableName = null;
            if (useSharedInstallation && !TryGetSharedInstallationProvider(
                gameServerObject,
                servergame,
                out sharedProvider,
                out pluginSharedProvider,
                out sharedInstallationAppId,
                out sharedInstallationExecutableName,
                out string sharedProviderError))
            {
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(sharedProviderError)
                        ? LocalizationManager.Get("SharedInstallationUnsupported")
                        : sharedProviderError,
                    LocalizationManager.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            if ((isRust || isBedrock) && sharedProvider == null)
            {
                sharedProvider = gameServerObject as GameServer.Engine.ISharedInstallationProvider;
            }

            bool isCod1 = IsCod1Game(servergame);
            bool isCfx = IsCfxGame(servergame);
            if (isCod1)
            {
                newServerConfig.Cod1MeatBots = checkBox_Install_Cod1MeatBots.IsChecked == true;
                newServerConfig.Cod1AlliesBots = numericUpDown_Install_Cod1AlliesBots.Value.ToString();
                newServerConfig.Cod1AxisBots = numericUpDown_Install_Cod1AxisBots.Value.ToString();
                if (!ServerConfig.ValidateCod1Settings(newServerConfig.Cod1AlliesBots, newServerConfig.Cod1AxisBots,
                    "32", out string settingsError))
                {
                    MessageBox.Show(settingsError, LocalizationManager.Get("CallOfDutySettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            string installPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(string.Format(LocalizationManager.Get("InstallPathInaccessible"), installPath), LocalizationManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            try
            {
                newServerConfig.CreateServerDirectory();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                System.Windows.Forms.MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SharedInstallationResolution sharedInstallation = null;
            if (useSharedInstallation)
            {
                try
                {
                    sharedInstallation = SharedInstallationStore.ResolveOrCreate(
                        newServerConfig.ServerID,
                        servergame,
                        sharedInstallationAppId,
                        sharedInstallationExecutableName);
                    newServerConfig.SharedInstallationId = sharedInstallation.InstallationId;
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            //Installation start
            textbox_InstallServerName.IsEnabled = false;
            comboBox_InstallGameServer.IsEnabled = false;
            checkBox_Install_SharedInstallation.IsEnabled = false;
            progressbar_InstallProgress.IsIndeterminate = true;
            SetAccessibleLiveText(textblock_InstallProgress, LocalizationManager.Get("InstallProgress"));
            button_Install.IsEnabled = false;
            textbox_InstallLog.Text = string.Empty;

            SharedInstallationOperationResult sharedBinaryInstallResult = null;
            if (useSharedInstallation && !sharedInstallation.Created)
            {
                AppendLogText(textbox_InstallLog, LocalizationManager.Get("ReuseSharedInstallation") + Environment.NewLine);
                Installer = null;
            }
            else if (useSharedInstallation)
            {
                if (pluginSharedProvider != null)
                {
                    sharedBinaryInstallResult = await InvokeSharedBinaryInstallAsync(
                        () => pluginSharedProvider.InstallSharedBinaries(new SharedInstallationContext(
                            sharedInstallation.InstallationId,
                            servergame,
                            sharedInstallationAppId,
                            sharedInstallationExecutableName,
                            SharedInstallationStore.GetServerBinaryRoot(sharedInstallation.InstallationId))),
                        () => Convert.ToString(gameServer.Error),
                        () => gameServer.IsInstallValid(),
                        servergame,
                        "installing shared binaries");
                    Installer = sharedBinaryInstallResult.Process;
                }
                else if (sharedProvider != null)
                {
                    sharedBinaryInstallResult = await InvokeSharedBinaryInstallAsync(
                        () => sharedProvider.InstallSharedBinaries(),
                        () => Convert.ToString(gameServer.Error),
                        () => gameServer.IsInstallValid(),
                        servergame,
                        "installing shared binaries");
                    Installer = sharedBinaryInstallResult.Process;
                }
                else
                {
                    sharedBinaryInstallResult = SharedInstallationOperationResult.Failed(
                        "No shared-install provider is available for the selected game.");
                    Installer = null;
                }
            }
            else
            {
                Installer = await gameServer.Install();
            }

            if (Installer != null)
            {
                Process installer = Installer;
                await Task.Run(() =>
                {
                    if (installer.StartInfo.RedirectStandardOutput)
                    {
                        var reader = installer.StandardOutput;
                        while (!reader.EndOfStream)
                        {
                            var nextLine = reader.ReadLine();
                            if (nextLine.Contains("Logging in user "))
                            {
                                nextLine += Environment.NewLine + "Please send the Login Token:";
                            }

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                AppendLogText(textbox_InstallLog, nextLine + Environment.NewLine);
                            });
                        }
                    }

                    installer.WaitForExit();
                });
            }

            string installFailure;
            if (useSharedInstallation && sharedInstallation != null && !sharedInstallation.Created)
            {
                installFailure = string.Empty;
            }
            else if (useSharedInstallation)
            {
                installFailure = GetSharedInstallationFailure(sharedBinaryInstallResult);
            }
            else
            {
                installFailure = Installer == null
                    ? (string.IsNullOrWhiteSpace(gameServer.Error) ? "Installer did not return a process." : gameServer.Error)
                    : (Installer.ExitCode == 0 ? string.Empty : $"Installer exited with code {Installer.ExitCode}.");
            }
            bool baseInstallValid = isCod1 ? gameServer.IsBaseInstallValid() : gameServer.IsInstallValid();
            if (isCod1 && (!useSharedInstallation || (sharedInstallation != null && sharedInstallation.Created)) &&
                (!baseInstallValid || Installer == null || Installer.ExitCode != 0))
            {
                string steamFailure = installFailure;
                SetAccessibleLiveText(textblock_InstallProgress, LocalizationManager.Get("InstallFallbackMessage"));
                await gameServer.InstallFallback();
                baseInstallValid = gameServer.IsBaseInstallValid();
                if (!baseInstallValid)
                {
                    installFailure = $"SteamCMD: {steamFailure}{Environment.NewLine}HTTPS fallback: {gameServer.Error}";
                }
            }

            string componentFailure = string.Empty;
            if (isCod1 && baseInstallValid && newServerConfig.Cod1MeatBots)
            {
                FileStream sharedAddonLease = null;
                try
                {
                    if (useSharedInstallation && sharedInstallation != null && !sharedInstallation.Created && !gameServer.IsMeatBotsInstalled())
                    {
                        sharedAddonLease = SharedInstallationStore.AcquireExclusiveLease(sharedInstallation.InstallationId);
                    }

                    if (!await gameServer.InstallMeatBots())
                    {
                        componentFailure = gameServer.Error;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    componentFailure = exception.Message;
                }
                finally
                {
                    sharedAddonLease?.Dispose();
                }
            }

            bool sharedBinariesReady = !useSharedInstallation || !sharedInstallation.Created;
            if (useSharedInstallation && sharedInstallation.Created)
            {
                bool sharedBinaryInstallSucceeded = sharedBinaryInstallResult != null &&
                    (sharedBinaryInstallResult.Status == SharedInstallationOperationStatus.Succeeded ||
                        (sharedBinaryInstallResult.Status == SharedInstallationOperationStatus.Started &&
                            Installer != null && Installer.ExitCode == 0));
                if (baseInstallValid && (sharedBinaryInstallSucceeded || isCod1))
                {
                    try
                    {
                        string buildId = gameServer.GetLocalBuild();
                        SharedInstallationStore.MarkReady(sharedInstallation.InstallationId, buildId);
                        sharedBinariesReady = true;
                    }
                    catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                    {
                        installFailure = exception.Message;
                    }
                }
            }

            bool installValid = sharedBinariesReady && baseInstallValid && string.IsNullOrWhiteSpace(componentFailure) && gameServer.IsInstallValid();
            if (installValid)
            {
                newServerConfig.ServerIP = newServerConfig.GetIPAddress();
                newServerConfig.ServerPort = newServerConfig.GetAvailablePort(gameServer.Port, gameServer.PortIncrements);

                // Create WindowsGSM.cfg
                newServerConfig.SetData(servergame, servername, gameServer);
                if (isCod1)
                {
                    newServerConfig.Cod1MeatBots = checkBox_Install_Cod1MeatBots.IsChecked == true;
                    newServerConfig.Cod1AlliesBots = numericUpDown_Install_Cod1AlliesBots.Value.ToString();
                    newServerConfig.Cod1AxisBots = numericUpDown_Install_Cod1AxisBots.Value.ToString();
                }
                if (isCfx)
                {
                    newServerConfig.CfxLicenseKey = passwordBox_Install_CfxLicenseKey.Password.Trim();
                }

                if (isRust || isBedrock || useSharedInstallation)
                {
                    if (useSharedInstallation && pluginSharedProvider != null)
                    {
                        SharedInstallationOperationResult configResult = await InvokePluginConfigOnlyOperationAsync(
                            () => pluginSharedProvider.InstallConfigOnly(new ServerInstanceContext(
                                newServerConfig.ServerID,
                                servergame,
                                newServerConfig.SharedInstallationId,
                                SharedInstallationStore.GetServerBinaryRoot(newServerConfig.SharedInstallationId),
                                ServerPath.GetServersServerFiles(newServerConfig.ServerID))),
                            servergame,
                            "preparing instance configuration");
                        if (configResult.Status != SharedInstallationOperationStatus.Succeeded)
                        {
                            installValid = false;
                            installFailure = configResult.Status == SharedInstallationOperationStatus.Started
                                ? "The plugin returned a running process for config-only setup."
                                : configResult.ErrorMessage;
                        }
                    }
                    else if (sharedProvider == null)
                    {
                        installValid = false;
                        installFailure = "No config-only provider is available for the selected game.";
                    }
                    else if (!await sharedProvider.InstallConfigOnly())
                    {
                        installValid = false;
                        installFailure = gameServer.Error;
                    }
                }
            }

            if (!installValid && string.IsNullOrWhiteSpace(installFailure))
            {
                installFailure = string.IsNullOrWhiteSpace(gameServer.Error)
                    ? LocalizationManager.Get("InstallValidationFailed")
                    : gameServer.Error;
            }

            if (!installValid)
            {
                string failedInstallationId = sharedInstallation != null && sharedInstallation.Created
                    ? sharedInstallation.InstallationId
                    : null;
                sharedInstallation?.Dispose();
                sharedInstallation = null;

                if (!string.IsNullOrWhiteSpace(failedInstallationId))
                {
                    try
                    {
                        if (!SharedInstallationStore.DeleteIfUnreferenced(failedInstallationId))
                        {
                            installFailure += Environment.NewLine + string.Format(
                                LocalizationManager.Get("SharedInstallationCleanupFailed"),
                                LocalizationManager.Get("SharedInstallationCleanupNoUnreferencedRecord"));
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                        exception is InvalidDataException || exception is UnauthorizedAccessException ||
                        exception is InvalidOperationException)
                    {
                        installFailure += Environment.NewLine + string.Format(
                            LocalizationManager.Get("SharedInstallationCleanupFailed"),
                            exception.Message);
                    }
                }
            }

            if (installValid)
            {
                try
                {
                    newServerConfig.CreateWindowsGSMConfig();
                }
                finally
                {
                    sharedInstallation?.Dispose();
                    sharedInstallation = null;
                }

                // Create WindowsGSM.cfg and game server config
                try
                {
                    gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);
                    if (isCfx)
                    {
                        WindowsGSM.GameServer.GTA5 cfxServer = (WindowsGSM.GameServer.GTA5)gameServer;
                        await cfxServer.CreateServerCFG();
                        if (!string.IsNullOrWhiteSpace(cfxServer.Error))
                        {
                            Log(newServerConfig.ServerID, servergame + " config: " + cfxServer.Error);
                        }
                        if (!string.IsNullOrWhiteSpace(cfxServer.Notice))
                        {
                            Log(newServerConfig.ServerID, servergame + " config: " + cfxServer.Notice);
                        }
                    }
                    else if (!isRust && !isBedrock && !useSharedInstallation)
                    {
                        gameServer.CreateServerCFG();
                    }
                }
                catch
                {
                    // ignore
                }

                LoadServerTable();
                Log(newServerConfig.ServerID, "Install: Success");

                SetAccessibleLiveText(textblock_InstallProgress, LocalizationManager.Get("AddonInstallSuccess"));
                MahAppFlyout_InstallGameServer.IsOpen = false;
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                passwordBox_Install_CfxLicenseKey.Password = string.Empty;
                progressbar_InstallProgress.IsIndeterminate = false;

                if (MahAppSwitch_SendStatistics.IsOn)
                {
                    var analytics = new GoogleAnalytics();
                    analytics.SendGameServerInstall(newServerConfig.ServerID, servergame);
                }
            }
            else
            {
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                checkBox_Install_SharedInstallation.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = LocalizationManager.Get("Install");
                button_Install.IsEnabled = true;

                if (!string.IsNullOrWhiteSpace(componentFailure)) { installFailure += Environment.NewLine + "MeatBots installation: " + componentFailure; }
                if (string.IsNullOrWhiteSpace(installFailure)) { installFailure = gameServer.Error; }
                SetAccessibleLiveText(textblock_InstallProgress, string.Format(LocalizationManager.Get("InstallFailed"), installFailure));
            }
        }

        private void ComboBox_InstallGameServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Set the elements visibility of Install Server Flyout
            var selectedgame = (Images.Row)comboBox_InstallGameServer.SelectedItem;
            button_InstallSetAccount.IsEnabled = false;
            label_InstallToken.Visibility = Visibility.Hidden;
            textBox_InstallToken.Visibility = Visibility.Hidden;
            button_InstallSendToken.Visibility = Visibility.Hidden;
            stackPanel_Install_CfxSettings.Visibility = selectedgame != null && IsCfxGame(selectedgame.Name) ? Visibility.Visible : Visibility.Collapsed;
            stackPanel_Install_Cod1Settings.Visibility = selectedgame != null && IsCod1Game(selectedgame.Name) ? Visibility.Visible : Visibility.Collapsed;
            checkBox_Install_SharedInstallation.Visibility = Visibility.Collapsed;
            checkBox_Install_SharedInstallation.IsChecked = false;
            if (selectedgame == null) { return; }

            try
            {
                object gameServerObject = GameServer.Data.Class.Get(selectedgame.Name, pluginList: PluginsList);
                dynamic gameServer = gameServerObject;
                bool isSupportedSharedProvider = IsSharedInstallationProviderAvailable(gameServerObject, selectedgame.Name);
                checkBox_Install_SharedInstallation.Visibility = isSupportedSharedProvider ? Visibility.Visible : Visibility.Collapsed;
                if (!gameServer.loginAnonymous)
                {
                    button_InstallSetAccount.IsEnabled = true;
                    label_InstallToken.Visibility = Visibility.Visible;
                    textBox_InstallToken.Visibility = Visibility.Visible;
                    button_InstallSendToken.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void Button_SetAccount_Click(object sender, RoutedEventArgs e)
        {
            var steamCMD = new Installer.SteamCMD();
            steamCMD.CreateUserDataTxtIfNotExist();

            string userDataPath = ServerPath.GetBin("steamcmd", "userData.txt");
            if (File.Exists(userDataPath))
            {
                OpenWithShell(userDataPath);
            }
        }

        private void Button_SendToken_Click(object sender, RoutedEventArgs e)
        {
            if (Installer != null)
            {
                Installer.StandardInput.WriteLine(textBox_InstallToken.Text);
            }

            textBox_InstallToken.Text = string.Empty;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGroups_GetAllServerRows().Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            OpenFlyout(MahAppFlyout_ImportGameServer);

            if (!progressbar_ImportProgress.IsIndeterminate)
            {
                textbox_ImportServerName.IsEnabled = true;
                comboBox_ImportGameServer.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                textblock_ImportProgress.Text = string.Empty;
                button_Import.Content = LocalizationManager.Get("Import");

                var newServerConfig = new ServerConfig(null);
                textbox_ImportServerName.Text = $"WindowsGSM - Server #{newServerConfig.ServerID}";
            }
        }

        private async void Button_Import_Click(object sender, RoutedEventArgs e)
        {
            var selectedgame = (Images.Row)comboBox_ImportGameServer.SelectedItem;
            SetAccessibleLiveText(label_ServerDirWarn, Directory.Exists(textbox_ServerDir.Text) ? string.Empty : LocalizationManager.Get("ServerDirectoryInvalid"));
            if (string.IsNullOrWhiteSpace(textbox_ImportServerName.Text))
            {
                SetAccessibleLiveText(textblock_ImportProgress, LocalizationManager.Get("ServerNameRequired"));
                textbox_ImportServerName.Focus();
                return;
            }
            if (selectedgame == null)
            {
                SetAccessibleLiveText(textblock_ImportProgress, LocalizationManager.Get("GameServerRequired"));
                comboBox_ImportGameServer.Focus();
                return;
            }

            string servername = textbox_ImportServerName.Text;
            string servergame = selectedgame.Name;

            var newServerConfig = new ServerConfig(null);
            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);

            if (!gameServer.IsImportValid(textbox_ServerDir.Text))
            {
                SetAccessibleLiveText(label_ServerDirWarn, gameServer.Error);
                return;
            }

            string importPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(importPath))
            {
                try
                {
                    Directory.Delete(importPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(string.Format(LocalizationManager.Get("ImportPathInaccessible"), importPath), LocalizationManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            //Import start
            textbox_ImportServerName.IsEnabled = false;
            comboBox_ImportGameServer.IsEnabled = false;
            textbox_ServerDir.IsEnabled = false;
            button_Browse.IsEnabled = false;
            progressbar_ImportProgress.IsIndeterminate = true;
            SetAccessibleLiveText(textblock_ImportProgress, LocalizationManager.Get("ImportProgress"));

            string sourcePath = textbox_ServerDir.Text;
            string importLog = await Task.Run(() =>
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourcePath, importPath);

                    // Scary error while moving the directory, some files may lost - Risky
                    //Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourcePath, importPath);

                    // This doesn't work on cross drive - Not working on cross drive
                    //Directory.Move(sourcePath, importPath);

                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            });

            if (importLog != null)
            {
                textbox_ImportServerName.IsEnabled = true;
                comboBox_ImportGameServer.IsEnabled = true;
                textbox_ServerDir.IsEnabled = true;
                button_Browse.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                SetAccessibleLiveText(textblock_ImportProgress, LocalizationManager.Get("ImportFailed"));
                string message = string.Format(LocalizationManager.Get("ImportCopyFailure"), textbox_ServerDir.Text, importPath, importLog);
                MessageBox.Show(message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Create WindowsGSM.cfg
            newServerConfig.SetData(servergame, servername, gameServer);
            newServerConfig.CreateWindowsGSMConfig();

            LoadServerTable();
            Log(newServerConfig.ServerID, "Import: Success");

            SetAccessibleLiveText(textblock_ImportProgress, LocalizationManager.Get("ImportCompleted"));
            MahAppFlyout_ImportGameServer.IsOpen = false;
            textbox_ImportServerName.IsEnabled = true;
            comboBox_ImportGameServer.IsEnabled = true;
            textbox_ServerDir.IsEnabled = true;
            button_Browse.IsEnabled = true;
            progressbar_ImportProgress.IsIndeterminate = false;
            textblock_ImportProgress.Text = string.Empty;
        }

        private void Button_Browse_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.ShowDialog();

            if (!string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                textbox_ServerDir.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            string sharedInstallationId = new ServerConfig(server.ID).SharedInstallationId;
            string confirmation = LocalizationManager.Get("DeleteServerConfirmation");
            if (!string.IsNullOrWhiteSpace(sharedInstallationId))
            {
                confirmation += Environment.NewLine + LocalizationManager.Get("SharedInstallationDeleteConfirmation");
            }

            MessageBoxResult result = MessageBox.Show(confirmation, LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Delete(server, sharedInstallationId);
        }

        private async void Button_DiscordEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string webhookUrl = ServerConfig.GetSetting(server.ID, Functions.ServerConfig.SettingName.DiscordWebhook);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("SaveButton"),
                DefaultText = webhookUrl
            };

            webhookUrl = await this.ShowInputAsync(LocalizationManager.Get("DiscordWebhookTitle"), LocalizationManager.Get("DiscordWebhookPrompt"), settings);
            if (webhookUrl == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordWebhook = webhookUrl;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordWebhook, webhookUrl);
        }

        private async void Button_DiscordSetMessage_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var message = ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.DiscordMessage);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("SaveButton"),
                DefaultText = message
            };

            message = await this.ShowInputAsync(LocalizationManager.Get("DiscordCustomMessageTitle"), LocalizationManager.Get("DiscordCustomMessagePrompt"), settings);
            if (message == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordMessage = message;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordMessage, message);
        }

        private async void Button_DiscordWebhookTest_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            int serverId = int.Parse(server.ID);
            if (!GetServerMetadata(serverId).DiscordAlert) { return; }

            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
            await webhook.Send(server.ID, server.Game, "Webhook Test Alert", server.Name, server.IP, server.Port);
        }

        private void Button_ServerCommand_Click(object sender, RoutedEventArgs e)
        {
            string command = textbox_servercommand.Text;
            textbox_servercommand.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(command)) { return; }

            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            SendCommand(server, command);
        }

        private void Textbox_ServerCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                if (textbox_servercommand.Text.Length != 0)
                {
                    GetServerMetadata(0).ServerConsole.Add(textbox_servercommand.Text);
                }

                Button_ServerCommand_Click(this, new RoutedEventArgs());
            }
        }

        private void Textbox_ServerCommand_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.IsDown && e.Key == Key.Up)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetPreviousCommand();
            }
            else if (e.IsDown && e.Key == Key.Down)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetNextCommand();
            }
        }

        #region Actions - Button Click
        private void Actions_Crash_Click(object sender, RoutedEventArgs e)
        {
            int test = 0;
            _ = 0 / test; // Crash
        }

        private async void Actions_Start_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            // Reload WindowsGSM.cfg on start
            SaveServerConfigToServerMetadata(server.ID, new ServerConfig(server.ID));

            await GameServer_Start(server);
        }

        private async void Actions_Stop_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Stop(server);
        }

        private async void Actions_Restart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Restart(server);
        }

        private async void Actions_Kill_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            switch (GetServerMetadata(server.ID).ServerStatus)
            {
                case ServerStatus.Restarting:
                case ServerStatus.Restarted:
                case ServerStatus.Started:
                case ServerStatus.Starting:
                case ServerStatus.Stopping:
                    Process p = GetServerMetadata(server.ID).Process;
                    if (p != null && !p.HasExited)
                    {
                        Log(server.ID, "Actions: Kill");
                        p.Kill();

                        ClearDetectedMainPort(server);
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                        Log(server.ID, "Server: Killed");
                        SetServerStatus(server, "Stopped");
                        _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
                        _serverMetadata[int.Parse(server.ID)].Process = null;
                    }

                    break;
            }
        }

        private void Actions_ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ServerMetadata metadata = GetServerMetadata(server.ID);
            Process p = metadata.Process;
            if (p == null || p.HasExited || Functions.ServerConsole.IsCreatedWithoutWindow(p)
                || Functions.ServerConsole.IsStandardOutputRedirected(p)
                || !Functions.ServerConsole.HasUsableWindow(p, metadata.MainWindow)) { return; }

            //If console is useless, return
            IntPtr hWnd = metadata.MainWindow;
            ShowWindow(hWnd, ShowWindow(hWnd, WindowShowStyle.Hide) ? WindowShowStyle.Hide : WindowShowStyle.ShowNormal);
        }

        private async void Actions_StartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGroups_GetAllServerRows())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StartServersWithAutoStartEnabled_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGroups_GetAllServerRows())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped && GetServerMetadata(server.ID).AutoStart)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StopAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGroups_GetAllServerRows())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Stop(server);
                }
            }
        }

        private async void Actions_RestartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGroups_GetAllServerRows())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Restart(server);
                }
            }
        }

        private async void Actions_UpdateAllServers_Click(object sender, RoutedEventArgs e)
        {
            var updatedSharedInstallations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in ServerGroups_GetAllServerRows())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    string installationId;
                    try
                    {
                        installationId = new ServerConfig(server.ID).SharedInstallationId;
                    }
                    catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
                    {
                        Log(server.ID, "Server: Fail to update");
                        Log(server.ID, $"[ERROR] Could not read server configuration: {exception.Message}");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(installationId) && !updatedSharedInstallations.Add(installationId))
                    {
                        Log(server.ID, "[NOTICE] Skipped because another linked server already handled this shared installation in Update All.");
                        continue;
                    }

                    await GameServer_Update(server);
                }
            }
        }

        private async void Actions_Update_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(LocalizationManager.Get("UpdateServerConfirmation"), LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }
            if (!ConfirmSharedInstallationUpdateImpact(server)) { return; }

            await GameServer_Update(server);
        }

        private async void Actions_UpdateValidate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(LocalizationManager.Get("ValidateServerConfirmation"), LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }
            if (!ConfirmSharedInstallationUpdateImpact(server)) { return; }

            await GameServer_Update(server, notes: " | Validate", validate: true);
        }

        private bool ConfirmSharedInstallationUpdateImpact(ServerTable server)
        {
            try
            {
                string installationId = new ServerConfig(server.ID).SharedInstallationId;
                if (string.IsNullOrWhiteSpace(installationId))
                {
                    return true;
                }

                Dictionary<string, ServerTable> serversById = ServerGroups_GetAllServerRows()
                    .ToDictionary(row => row.ID, StringComparer.OrdinalIgnoreCase);
                string linkedServers = string.Join(", ", SharedInstallationStore.GetLinkedServerIds(installationId)
                    .Select(serverId =>
                    {
                        if (!serversById.TryGetValue(serverId, out ServerTable linkedServer))
                        {
                            return serverId;
                        }

                        return string.IsNullOrWhiteSpace(linkedServer.Name)
                            ? serverId
                            : $"{linkedServer.Name} ({serverId})";
                    }));
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Get("SharedInstallationUpdateImpact"),
                    linkedServers);
                return System.Windows.MessageBox.Show(message, LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async void Actions_Backup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(LocalizationManager.Get("BackupServerConfirmation"), LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Backup(server);
        }

        private async void Actions_ConfigurationBackup_Click(object sender, RoutedEventArgs e)
        {
            ServerTable server = ServerGrid.SelectedItem as ServerTable;
            if (server == null) { return; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            if (!IsConfigurationBackupSupported(server.Game))
            {
                MessageBox.Show(LocalizationManager.Get("ConfigurationBackupUnsupported"), LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ConfigurationBackupContext configurationContext;
            try
            {
                List<PluginMetadata> pluginSnapshot = PluginsList?.ToList();
                configurationContext = await Task.Run(() => GetConfigurationBackupContext(server.ID, pluginSnapshot));
            }
            catch (Exception exception) when (IsExpectedBackupFileException(exception))
            {
                Log(server.ID, $"[ERROR] Could not prepare the configuration backup: {exception.Message}");
                MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string confirmation = GetBackupConfirmationMessage(
                server,
                BackupScheduleConfig.BackupTypeConfiguration,
                configurationContext.Paths);
            if (MessageBox.Show(confirmation, LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { return; }

            await GameServer_Backup(
                server,
                string.Empty,
                BackupScheduleConfig.BackupTypeConfiguration,
                configurationContext);
        }

        private bool IsConfigurationBackupSupported(string serverGame)
        {
            if (BuiltInGameCapabilities.SupportsConfigurationBackup(serverGame))
            {
                return true;
            }

            PluginMetadata plugin = GetLoadedPlugin(serverGame);
            return plugin != null &&
                plugin.ApiVersion == PluginApiLevel.CapabilityMethods &&
                (plugin.Capabilities & WindowsGSMPluginCapability.ConfigurationBackup) != 0;
        }

        private PluginMetadata GetLoadedPlugin(string gameKey, IEnumerable<PluginMetadata> pluginSnapshot = null)
        {
            return (pluginSnapshot ?? PluginsList)?.FirstOrDefault(item =>
                item.IsLoaded &&
                string.Equals(item.FullName, gameKey, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<SharedInstallationOperationResult> InvokePluginConfigOnlyOperationAsync(
            Func<Task<SharedInstallationOperationResult>> operation,
            string gameKey,
            string operationName)
        {
            try
            {
                SharedInstallationOperationResult result = await operation();
                if (result == null)
                {
                    PluginMetadata plugin = GetLoadedPlugin(gameKey);
                    string pluginName = plugin == null ? gameKey : plugin.FileName;
                    return SharedInstallationOperationResult.Failed(
                        $"Plugin '{pluginName}' returned no result while {operationName}.");
                }
                if (result.Status == SharedInstallationOperationStatus.Started &&
                    result.Process.Id <= 0)
                {
                    PluginMetadata plugin = GetLoadedPlugin(gameKey);
                    string pluginName = plugin == null ? gameKey : plugin.FileName;
                    return SharedInstallationOperationResult.Failed(
                        $"Plugin '{pluginName}' must return a started installer process while {operationName}.");
                }

                return result;
            }
            catch (OperationCanceledException exception)
            {
                PluginMetadata plugin = GetLoadedPlugin(gameKey);
                string pluginName = plugin == null ? gameKey : plugin.FileName;
                string detail = string.IsNullOrWhiteSpace(exception.Message) ? "The operation was cancelled." : exception.Message;
                return SharedInstallationOperationResult.Cancelled(
                    $"Plugin '{pluginName}' was cancelled while {operationName}: {detail}");
            }
            catch (Exception exception)
            {
                PluginMetadata plugin = GetLoadedPlugin(gameKey);
                string pluginName = plugin == null ? gameKey : plugin.FileName;
                string detail = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
                return SharedInstallationOperationResult.Failed(
                    $"Plugin '{pluginName}' failed while {operationName}: {detail}");
            }
        }

        private async Task<SharedInstallationOperationResult> InvokeSharedBinaryInstallAsync(
            Func<Task<Process>> operation,
            Func<string> getFailureMessage,
            Func<bool> isInstallValid,
            string gameKey,
            string operationName)
        {
            string providerName = GetLoadedPlugin(gameKey)?.FileName ?? gameKey;
            try
            {
                Process process = await operation();
                if (process == null)
                {
                    string failureMessage = getFailureMessage();
                    if (!string.IsNullOrWhiteSpace(failureMessage))
                    {
                        return SharedInstallationOperationResult.Failed(failureMessage);
                    }

                    return isInstallValid()
                        ? SharedInstallationOperationResult.Succeeded()
                        : SharedInstallationOperationResult.Failed(
                            $"Provider '{providerName}' returned no installer process and the shared installation is invalid while {operationName}.");
                }
                if (process.Id <= 0)
                {
                    return SharedInstallationOperationResult.Failed(
                        $"Provider '{providerName}' returned a process that has not started while {operationName}.");
                }

                return SharedInstallationOperationResult.Started(process);
            }
            catch (OperationCanceledException exception)
            {
                string detail = string.IsNullOrWhiteSpace(exception.Message) ? "The operation was cancelled." : exception.Message;
                return SharedInstallationOperationResult.Cancelled(
                    $"Provider '{providerName}' was cancelled while {operationName}: {detail}");
            }
            catch (Exception exception)
            {
                string detail = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
                return SharedInstallationOperationResult.Failed(
                    $"Provider '{providerName}' failed while {operationName}: {detail}");
            }
        }

        private static string GetSharedInstallationFailure(SharedInstallationOperationResult result)
        {
            if (result == null)
            {
                return "The shared-install provider returned no operation result.";
            }

            switch (result.Status)
            {
                case SharedInstallationOperationStatus.Succeeded:
                    return string.Empty;
                case SharedInstallationOperationStatus.Started:
                    if (result.Process == null)
                    {
                        return "The shared-install provider reported a running operation without a process.";
                    }

                    return result.Process.ExitCode == 0
                        ? string.Empty
                        : $"Installer exited with code {result.Process.ExitCode}.";
                case SharedInstallationOperationStatus.Cancelled:
                case SharedInstallationOperationStatus.Failed:
                    return result.ErrorMessage;
                default:
                    return "The shared-install provider returned an unsupported operation status.";
            }
        }

        private bool IsSharedInstallationProviderAvailable(object gameServerObject, string gameKey)
        {
            if (gameServerObject is GameServer.Engine.ISharedInstallationProvider &&
                gameServerObject.GetType().Assembly == typeof(GameServer.RUST).Assembly)
            {
                return BuiltInGameCapabilities.SupportsSharedInstallation(gameKey, gameServerObject);
            }

            // API capability signatures do not prove provider-specific runtime isolation.
            return false;
        }

        private string GetSharedInstallationEligibilityError(
            ServerConfig serverConfig,
            object gameServerObject,
            string gameKey)
        {
            if (serverConfig == null ||
                string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId) ||
                IsSharedInstallationProviderAvailable(gameServerObject, gameKey))
            {
                return null;
            }

            return string.Equals(gameKey, GameServer.RUST.FullName, StringComparison.OrdinalIgnoreCase)
                ? LocalizationManager.Get("RustSharedInstallationUnavailable")
                : LocalizationManager.Get("SharedInstallationUnsupported");
        }

        private bool TryGetSharedInstallationProvider(
            object gameServerObject,
            string gameKey,
            out GameServer.Engine.ISharedInstallationProvider provider,
            out PluginSharedInstallationAdapter pluginProvider,
            out string appId,
            out string executableName,
            out string error)
        {
            provider = null;
            pluginProvider = null;
            appId = null;
            executableName = null;
            error = null;
            if (gameServerObject is GameServer.Engine.ISharedInstallationProvider builtInProvider &&
                gameServerObject.GetType().Assembly == typeof(GameServer.RUST).Assembly)
            {
                if (!BuiltInGameCapabilities.SupportsSharedInstallation(gameKey, builtInProvider))
                {
                    error = LocalizationManager.Get("SharedInstallationUnsupported");
                    return false;
                }

                provider = builtInProvider;
                try
                {
                    appId = builtInProvider.SharedInstallationAppId;
                    executableName = builtInProvider.SharedInstallationExecutableName;
                    return true;
                }
                catch (Exception exception)
                {
                    error = $"The game integration failed to describe its shared installation: {exception.Message}";
                    return false;
                }
            }

            error = LocalizationManager.Get("SharedInstallationUnsupported");
            return false;
        }

        private static string GetBackupConfirmationMessage(
            ServerTable server,
            string backupType,
            IEnumerable<ConfigurationBackupPath> configurationPaths = null)
        {
            if (backupType == BackupScheduleConfig.BackupTypeConfiguration)
            {
                string includedPaths = string.Join(
                    ", ",
                    (configurationPaths ?? Enumerable.Empty<ConfigurationBackupPath>())
                        .Select(path => path.RootId + "/" + path.RelativePath));
                return string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Get("ConfigurationBackupConfirmation"),
                    server.Name,
                    includedPaths);
            }

            if (backupType != BackupScheduleConfig.BackupTypeFullServer)
            {
                throw new InvalidDataException($"Unsupported backup type: {backupType}.");
            }

            return LocalizationManager.Get("BackupServerConfirmation");
        }

        private void BackupScheduleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _isLoadingBackupScheduleEditor = true;
            try
            {
                _backupScheduleTimeChanged = false;
                BackupScheduleRow row = BackupScheduleGrid.SelectedItem as BackupScheduleRow;
                if (row == null)
                {
                    label_BackupScheduleServer.Text = string.Empty;
                    textBox_BackupScheduleTime.Text = string.Empty;
                    comboBox_BackupScheduleFrequency.SelectedValue = null;
                    comboBox_BackupScheduleType.SelectedValue = null;
                    comboBox_BackupScheduleType.IsEnabled = false;
                    comboBox_BackupScheduleDay.SelectedValue = null;
                    switch_BackupScheduleEnabled.IsOn = false;
                    switch_BackupScheduleEnabled.IsEnabled = false;
                    button_RunBackupNow.IsEnabled = false;
                    label_BackupScheduleStorage.Text = string.Empty;
                    label_BackupScheduleStatus.Text = string.Empty;
                    UpdateBackupScheduleEditor();
                    return;
                }

                label_BackupScheduleServer.Text = row.ServerName;
                switch_BackupScheduleEnabled.IsOn = row.Enabled;
                comboBox_BackupScheduleType.SelectedValue = row.BackupType;
                bool configurationAvailable = string.IsNullOrEmpty(row.ConfigurationError);
                switch_BackupScheduleEnabled.IsEnabled = configurationAvailable;
                button_RunBackupNow.IsEnabled = configurationAvailable;
                label_BackupScheduleStorage.Text = row.BackupStorage;
                SetAccessibleLiveText(label_BackupScheduleStatus, row.LastResultMessage);

                if (TryGetSimpleBackupSchedule(row.CronExpression, out bool weekly, out int hour, out int minute, out int dayOfWeek))
                {
                    textBox_BackupScheduleTime.Text = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", hour, minute);
                    comboBox_BackupScheduleDay.SelectedValue = dayOfWeek.ToString(CultureInfo.InvariantCulture);
                    comboBox_BackupScheduleFrequency.SelectedValue = weekly ? "Weekly" : "Daily";
                }
                else
                {
                    textBox_BackupScheduleTime.Text = string.Empty;
                    comboBox_BackupScheduleDay.SelectedValue = null;
                    comboBox_BackupScheduleFrequency.SelectedValue = "Custom";
                }

                UpdateBackupScheduleEditor();
            }
            finally
            {
                _isLoadingBackupScheduleEditor = false;
            }
        }

        private void BackupScheduleFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor) { return; }
            UpdateBackupScheduleEditor();
            SaveBackupScheduleFromEditor();
        }

        private void BackupScheduleType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor) { return; }
            UpdateBackupScheduleEditor();
            SaveBackupScheduleFromEditor();
        }

        private void BackupScheduleDay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor) { return; }
            SaveBackupScheduleFromEditor();
        }

        private void BackupScheduleEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor) { return; }
            SaveBackupScheduleFromEditor();
        }

        private void BackupScheduleTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor)
            {
                return;
            }

            _backupScheduleTimeChanged = true;
            SaveBackupScheduleFromEditor(refreshGrid: false, reportValidationErrors: false);
        }

        private void BackupScheduleTime_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoadingBackupScheduleEditor || !_backupScheduleTimeChanged)
            {
                return;
            }

            BackupScheduleRow row = BackupScheduleGrid.SelectedItem as BackupScheduleRow;
            if (row != null && SaveBackupScheduleFromEditor(refreshGrid: false))
            {
                _backupScheduleTimeChanged = false;
                RefreshBackupSchedules(row.ServerId);
            }
        }

        private void UpdateBackupScheduleEditor()
        {
            if (comboBox_BackupScheduleFrequency == null || comboBox_BackupScheduleType == null || label_BackupScheduleTime == null || label_BackupScheduleDay == null)
            {
                return;
            }

            BackupScheduleRow row = BackupScheduleGrid.SelectedItem as BackupScheduleRow;
            if (row == null)
            {
                label_BackupScheduleTime.Visibility = Visibility.Collapsed;
                textBox_BackupScheduleTime.Visibility = Visibility.Collapsed;
                label_BackupScheduleDay.Visibility = Visibility.Collapsed;
                comboBox_BackupScheduleDay.Visibility = Visibility.Collapsed;
                comboBox_BackupScheduleFrequency.IsEnabled = false;
                comboBox_BackupScheduleType.IsEnabled = false;
                textBox_BackupScheduleTime.IsEnabled = false;
                comboBox_BackupScheduleDay.IsEnabled = false;
                label_BackupScheduleHint.Text = LocalizationManager.Get("ScheduleNoServerSelected");
                return;
            }

            bool configurationAvailable = string.IsNullOrEmpty(row.ConfigurationError);
            string frequency = comboBox_BackupScheduleFrequency.SelectedValue as string;
            string backupType = comboBox_BackupScheduleType.SelectedValue as string;
            ComboBoxItem configurationTypeItem = comboBox_BackupScheduleType.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == BackupScheduleConfig.BackupTypeConfiguration);
            if (configurationTypeItem != null)
            {
                configurationTypeItem.IsEnabled = row.SupportsConfigurationBackup;
            }

            bool weekly = frequency == "Weekly";
            bool simpleSchedule = frequency == "Daily" || weekly;
            label_BackupScheduleTime.Visibility = simpleSchedule ? Visibility.Visible : Visibility.Collapsed;
            textBox_BackupScheduleTime.Visibility = simpleSchedule ? Visibility.Visible : Visibility.Collapsed;
            label_BackupScheduleDay.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
            comboBox_BackupScheduleDay.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
            comboBox_BackupScheduleFrequency.IsEnabled = configurationAvailable;
            comboBox_BackupScheduleType.IsEnabled = configurationAvailable;
            textBox_BackupScheduleTime.IsEnabled = configurationAvailable && simpleSchedule;
            comboBox_BackupScheduleDay.IsEnabled = configurationAvailable && weekly;
            label_BackupScheduleHint.Text = !configurationAvailable
                ? row.ConfigurationError
                : backupType == BackupScheduleConfig.BackupTypeConfiguration
                    ? row.SupportsConfigurationBackup
                        ? LocalizationManager.Get("ScheduleConfigurationBackupHint")
                        : LocalizationManager.Get("ConfigurationBackupUnsupported")
                    : frequency == "Custom"
                        ? LocalizationManager.Get("ScheduleCustomHint")
                        : LocalizationManager.Get("ScheduleSimpleHint");
        }

        private bool SaveBackupScheduleFromEditor(bool refreshGrid = true, bool reportValidationErrors = true)
        {
            if (_isLoadingBackupScheduleEditor) { return false; }

            BackupScheduleRow row = BackupScheduleGrid.SelectedItem as BackupScheduleRow;
            if (row == null || !string.IsNullOrEmpty(row.ConfigurationError)) { return false; }

            string backupType = comboBox_BackupScheduleType.SelectedValue as string;
            if (backupType != BackupScheduleConfig.BackupTypeFullServer && backupType != BackupScheduleConfig.BackupTypeConfiguration)
            {
                if (reportValidationErrors)
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                }
                return false;
            }
            if (backupType == BackupScheduleConfig.BackupTypeConfiguration && !row.SupportsConfigurationBackup)
            {
                if (reportValidationErrors)
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ConfigurationBackupUnsupported"));
                }
                return false;
            }

            string frequency = comboBox_BackupScheduleFrequency.SelectedValue as string;
            string expression;
            if (frequency == "Custom")
            {
                expression = row.CronExpression;
            }
            else
            {
                if (!DateTime.TryParseExact(textBox_BackupScheduleTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime scheduledTime))
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }

                if (frequency == "Daily")
                {
                    expression = string.Format(CultureInfo.InvariantCulture, "{0} {1} * * *", scheduledTime.Minute, scheduledTime.Hour);
                }
                else if (frequency == "Weekly" &&
                    int.TryParse(comboBox_BackupScheduleDay.SelectedValue as string, NumberStyles.None, CultureInfo.InvariantCulture, out int dayOfWeek) &&
                    dayOfWeek >= 0 && dayOfWeek <= 6)
                {
                    expression = string.Format(CultureInfo.InvariantCulture, "{0} {1} * * {2}", scheduledTime.Minute, scheduledTime.Hour, dayOfWeek);
                }
                else
                {
                    if (reportValidationErrors)
                    {
                        SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                    }
                    return false;
                }
            }

            CrontabSchedule schedule = string.IsNullOrWhiteSpace(expression) ? null : CrontabSchedule.TryParse(expression);
            bool validSchedule = schedule != null && TryGetNextBackupOccurrence(schedule, DateTime.Now, out DateTime _);
            if ((frequency != "Custom" || switch_BackupScheduleEnabled.IsOn) && !validSchedule)
            {
                if (reportValidationErrors)
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleInvalidMessage"));
                }
                return false;
            }

            if (!TryLoadBackupSchedule(row.ServerId, out BackupScheduleConfig config, out string error))
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), error));
                return false;
            }

            config.CronExpression = expression;
            config.Enabled = switch_BackupScheduleEnabled.IsOn;
            config.BackupType = backupType;
            config.LastEvaluatedAt = DateTime.Now;
            if (!TrySaveBackupSchedule(row.ServerId, config, out error))
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), error));
                return false;
            }

            SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleSaved"));
            if (refreshGrid)
            {
                _backupScheduleTimeChanged = false;
                RefreshBackupSchedules(row.ServerId);
            }
            return true;
        }

        private async void Button_RunBackupNow_Click(object sender, RoutedEventArgs e)
        {
            BackupScheduleRow row = BackupScheduleGrid.SelectedItem as BackupScheduleRow;
            if (row == null) { return; }

            ServerTable server = FindServerTable(row.ServerId);
            if (server == null)
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ScheduleNoServerSelected"));
                return;
            }

            if (!TryLoadBackupSchedule(row.ServerId, out BackupScheduleConfig config, out string error))
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), error));
                return;
            }

            if (config.BackupType == BackupScheduleConfig.BackupTypeConfiguration && !row.SupportsConfigurationBackup)
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, LocalizationManager.Get("ConfigurationBackupUnsupported"));
                return;
            }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                config.LastAttemptedAt = DateTime.Now;
                config.LastResult = BackupScheduleConfig.ResultSkipped;
                config.LastResultMessage = BackupScheduleConfig.MessageServerBusy;
                if (!TrySaveBackupSchedule(server.ID, config, out string saveError))
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), saveError));
                    return;
                }

                Log(server.ID, $"[NOTICE] {LocalizationManager.Get("ScheduleServerBusyMessage")}");
                RefreshBackupSchedules(server.ID);
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show(GetBackupConfirmationMessage(server, config.BackupType), LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await RunBackupScheduleNow(server, config);
        }

        private async Task RunBackupScheduleNow(ServerTable server, BackupScheduleConfig config)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                config.LastAttemptedAt = DateTime.Now;
                config.LastResult = BackupScheduleConfig.ResultSkipped;
                config.LastResultMessage = BackupScheduleConfig.MessageServerBusy;
                if (!TrySaveBackupSchedule(server.ID, config, out string saveError))
                {
                    SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), saveError));
                    return;
                }

                Log(server.ID, $"[NOTICE] {LocalizationManager.Get("ScheduleServerBusyMessage")}");
                RefreshBackupSchedules(server.ID);
                return;
            }

            config.LastAttemptedAt = DateTime.Now;
            config.LastResult = BackupScheduleConfig.ResultRunning;
            config.LastResultMessage = string.Empty;
            if (!TrySaveBackupSchedule(server.ID, config, out string error))
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), error));
                return;
            }

            bool completed = await GameServer_Backup(server, " | Run from Schedules", config.BackupType);
            config.LastAttemptedAt = DateTime.Now;
            config.LastResult = completed ? BackupScheduleConfig.ResultCompleted : BackupScheduleConfig.ResultFailed;
            config.LastResultMessage = completed ? BackupScheduleConfig.MessageCompleted : BackupScheduleConfig.MessageFailed;
            bool saved = TrySaveBackupSchedule(server.ID, config, out string resultSaveError);
            RefreshBackupSchedules(server.ID);
            if (!saved)
            {
                SetAccessibleLiveText(label_BackupScheduleStatus, string.Format(LocalizationManager.Get("ScheduleSaveFailed"), resultSaveError));
            }
        }

        private void RefreshBackupSchedules(string selectedServerId = null)
        {
            var rows = new List<BackupScheduleRow>();
            foreach (ServerTable server in ServerGroups_GetAllServerRows())
            {
                string backupStorage = GetBackupScheduleStorage(server.ID);
                if (!TryLoadBackupSchedule(server.ID, out BackupScheduleConfig config, out string error))
                {
                    rows.Add(new BackupScheduleRow(
                        server.ID,
                        server.Name,
                        server.Game,
                        BackupScheduleConfig.BackupTypeFullServer,
                        IsConfigurationBackupSupported(server.Game),
                        false,
                        string.Empty,
                        LocalizationManager.Get("ScheduleConfigError"),
                        LocalizationManager.Get("ScheduleConfigError"),
                        string.Empty,
                        LocalizationManager.Get("ScheduleConfigError"),
                        error,
                        error,
                        backupStorage));
                    continue;
                }

                CrontabSchedule schedule = CrontabSchedule.TryParse(config.CronExpression);
                DateTime nextOccurrence = default(DateTime);
                bool validSchedule = schedule != null && TryGetNextBackupOccurrence(schedule, DateTime.Now, out nextOccurrence);
                string nextRun = !config.Enabled
                    ? LocalizationManager.Get("ScheduleDisabled")
                    : validSchedule
                        ? nextOccurrence.ToString("g")
                        : LocalizationManager.Get("ScheduleInvalid");
                string result = GetBackupScheduleResult(config.LastResult);
                string message = GetBackupScheduleMessage(config.LastResultMessage);
                if (config.Enabled && !validSchedule)
                {
                    result = LocalizationManager.Get("ScheduleInvalid");
                    message = LocalizationManager.Get("ScheduleInvalidMessage");
                }
                else if (string.IsNullOrWhiteSpace(result))
                {
                    result = LocalizationManager.Get("ScheduleNoRuns");
                }

                rows.Add(new BackupScheduleRow(
                    server.ID,
                    server.Name,
                    server.Game,
                    config.BackupType,
                    IsConfigurationBackupSupported(server.Game),
                    config.Enabled,
                    config.CronExpression,
                    GetBackupScheduleDescription(config.CronExpression),
                    nextRun,
                    config.LastAttemptedAt?.ToString("g") ?? string.Empty,
                    result,
                    message,
                    string.Empty,
                    backupStorage));
            }

            label_BackupScheduleTimeZone.Content = string.Format(LocalizationManager.Get("ScheduleLocalTimeZone"), TimeZoneInfo.Local.DisplayName);
            BackupScheduleGrid.ItemsSource = rows;
            BackupScheduleGrid.SelectedItem = rows.FirstOrDefault(row => row.ServerId == selectedServerId) ?? rows.FirstOrDefault();
        }

        private static string GetBackupScheduleDescription(string expression)
        {
            if (!TryGetSimpleBackupSchedule(expression, out bool weekly, out int hour, out int minute, out int dayOfWeek))
            {
                return LocalizationManager.Get("ScheduleCustom");
            }

            string time = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", hour, minute);
            return weekly
                ? string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("ScheduleWeeklyAt"), GetBackupScheduleDayName(dayOfWeek), time)
                : string.Format(CultureInfo.CurrentUICulture, LocalizationManager.Get("ScheduleDailyAt"), time);
        }

        private static bool TryGetSimpleBackupSchedule(string expression, out bool weekly, out int hour, out int minute, out int dayOfWeek)
        {
            weekly = false;
            hour = 0;
            minute = 0;
            dayOfWeek = 0;

            if (string.IsNullOrWhiteSpace(expression)) { return false; }

            string[] fields = expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out minute) || minute > 59 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out hour) || hour > 23 ||
                fields[2] != "*" || fields[3] != "*")
            {
                return false;
            }

            if (fields[4] == "*") { return true; }

            if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out dayOfWeek) || dayOfWeek > 6)
            {
                return false;
            }

            weekly = true;
            return true;
        }

        private static string GetBackupScheduleDayName(int dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case 0: return LocalizationManager.Get("ScheduleSunday");
                case 1: return LocalizationManager.Get("ScheduleMonday");
                case 2: return LocalizationManager.Get("ScheduleTuesday");
                case 3: return LocalizationManager.Get("ScheduleWednesday");
                case 4: return LocalizationManager.Get("ScheduleThursday");
                case 5: return LocalizationManager.Get("ScheduleFriday");
                case 6: return LocalizationManager.Get("ScheduleSaturday");
                default: throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
            }
        }

        private string GetBackupScheduleStorage(string serverId)
        {
            try
            {
                BackupConfig config = new BackupConfig(serverId);
                string location = string.IsNullOrWhiteSpace(config.BackupLocation)
                    ? Path.Combine(WGSM_PATH, ServerPath.FolderName.Backups, serverId)
                    : config.BackupLocation;
                return string.Format(LocalizationManager.Get("ScheduleBackupStorage"), location, config.MaximumBackups);
            }
            catch (Exception e) when (IsExpectedBackupFileException(e))
            {
                Log(serverId, $"[ERROR] Backup storage configuration could not be read: {e.Message}");
                return string.Format(LocalizationManager.Get("ScheduleBackupStorageError"), e.Message);
            }
        }

        private static string GetBackupScheduleResult(string result)
        {
            switch (result)
            {
                case BackupScheduleConfig.ResultRunning: return LocalizationManager.Get("ScheduleResultRunning");
                case BackupScheduleConfig.ResultCompleted: return LocalizationManager.Get("ScheduleResultCompleted");
                case BackupScheduleConfig.ResultSkipped: return LocalizationManager.Get("ScheduleResultSkipped");
                case BackupScheduleConfig.ResultFailed: return LocalizationManager.Get("ScheduleResultFailed");
                case BackupScheduleConfig.ResultMissed: return LocalizationManager.Get("ScheduleResultMissed");
                case BackupScheduleConfig.ResultInterrupted: return LocalizationManager.Get("ScheduleResultInterrupted");
                default: return string.Empty;
            }
        }

        private static string GetBackupScheduleMessage(string message)
        {
            switch (message)
            {
                case BackupScheduleConfig.MessageServerBusy: return LocalizationManager.Get("ScheduleServerBusyMessage");
                case BackupScheduleConfig.MessageMissed: return LocalizationManager.Get("ScheduleMissedMessage");
                case BackupScheduleConfig.MessageInterrupted: return LocalizationManager.Get("ScheduleInterruptedMessage");
                case BackupScheduleConfig.MessageCompleted: return LocalizationManager.Get("ScheduleBackupCompletedMessage");
                case BackupScheduleConfig.MessageFailed: return LocalizationManager.Get("ScheduleBackupFailedMessage");
                default: return message ?? string.Empty;
            }
        }

        private bool TryLoadBackupSchedule(string serverId, out BackupScheduleConfig config, out string error)
        {
            try
            {
                config = BackupScheduleConfig.Load(serverId);
                error = null;
                return true;
            }
            catch (IOException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be read: {e.Message}");
                config = null;
                error = e.Message;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be read: {e.Message}");
                config = null;
                error = e.Message;
                return false;
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be read: {e.Message}");
                config = null;
                error = e.Message;
                return false;
            }
            catch (ArgumentException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be read: {e.Message}");
                config = null;
                error = e.Message;
                return false;
            }
        }

        private bool TrySaveBackupSchedule(string serverId, BackupScheduleConfig config, out string error)
        {
            try
            {
                config.Save(serverId);
                error = null;
                return true;
            }
            catch (IOException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be saved: {e.Message}");
                error = e.Message;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be saved: {e.Message}");
                error = e.Message;
                return false;
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be saved: {e.Message}");
                error = e.Message;
                return false;
            }
            catch (ArgumentException e)
            {
                Log(serverId, $"[ERROR] Backup schedule configuration could not be saved: {e.Message}");
                error = e.Message;
                return false;
            }
        }

        private ServerTable FindServerTable(string serverId)
        {
            return ServerGroups_GetAllServerRows().FirstOrDefault(server => server.ID == serverId);
        }

        private static bool TryGetNextBackupOccurrence(CrontabSchedule schedule, DateTime after, out DateTime occurrence)
        {
            DateTime endTime;
            try
            {
                endTime = after.AddYears(5);
            }
            catch (ArgumentOutOfRangeException)
            {
                endTime = DateTime.MaxValue;
            }

            occurrence = schedule.GetNextOccurrences(after, endTime).FirstOrDefault();
            return occurrence != default(DateTime);
        }

        private async void Actions_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            listbox_RestoreBackup.Items.Clear();
            var backupConfig = new BackupConfig(server.ID);
            if (Directory.Exists(backupConfig.BackupLocation))
            {
                string fullBackupPrefix = $"WGSM-Backup-Server-{server.ID}-";
                string configurationBackupPrefix = $"WGSM-Backup-Config-Server-{server.ID}-";
                bool configurationBackupSupported = IsConfigurationBackupSupported(server.Game);
                foreach (FileInfo fi in new DirectoryInfo(backupConfig.BackupLocation).GetFiles()
                    .Where(file =>
                        (string.Equals(file.Extension, ".zip", StringComparison.OrdinalIgnoreCase) && file.Name.StartsWith(fullBackupPrefix, StringComparison.OrdinalIgnoreCase)) ||
                        (configurationBackupSupported &&
                            string.Equals(file.Extension, ConfigurationBackupArchive.FileExtension, StringComparison.OrdinalIgnoreCase) &&
                            file.Name.StartsWith(configurationBackupPrefix, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(file => file.LastWriteTimeUtc))
                {
                    listbox_RestoreBackup.Items.Add(fi.Name);
                }
            }

            if (listbox_RestoreBackup.Items.Count > 0)
            {
                listbox_RestoreBackup.SelectedIndex = 0;
            }

            label_RestoreBackupServerName.Content = server.Name;
            OpenFlyout(MahAppFlyout_RestoreBackup);
        }

        private async void Button_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            if (listbox_RestoreBackup.SelectedIndex >= 0)
            {
                MahAppFlyout_RestoreBackup.IsOpen = false;
                await GameServer_RestoreBackup(server, listbox_RestoreBackup.SelectedItem.ToString());
            }
        }

        private void Actions_ManageAddons_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ListBox_ManageAddons_Refresh();
            ToggleMahappFlyout(MahAppFlyout_ManageAddons);
        }
        #endregion

        private void ListBox_ManageAddonsLeft_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listBox_ManageAddonsLeft.SelectedItem != null)
            {
                var server = (ServerTable)ServerGrid.SelectedItem;
                if (server == null) { return; }

                string item = listBox_ManageAddonsLeft.SelectedItem.ToString();
                listBox_ManageAddonsLeft.Items.Remove(listBox_ManageAddonsLeft.Items[listBox_ManageAddonsLeft.SelectedIndex]);
                listBox_ManageAddonsRight.Items.Add(item);
                var serverAddon = new ServerAddon(server.ID, server.Game);
                serverAddon.AddToRight(listBox_ManageAddonsRight.Items.OfType<string>().ToList(), item);

                ListBox_ManageAddons_Refresh();

                foreach (var selected in listBox_ManageAddonsRight.Items)
                {
                    if (selected.ToString() == item)
                    {
                        listBox_ManageAddonsRight.SelectedItem = selected;
                    }
                }
            }
        }

        private void ListBox_ManageAddonsRight_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listBox_ManageAddonsRight.SelectedItem != null)
            {
                var server = (ServerTable)ServerGrid.SelectedItem;
                if (server == null) { return; }

                string item = listBox_ManageAddonsRight.SelectedItem.ToString();
                listBox_ManageAddonsRight.Items.Remove(listBox_ManageAddonsRight.Items[listBox_ManageAddonsRight.SelectedIndex]);
                listBox_ManageAddonsLeft.Items.Add(item);
                var serverAddon = new ServerAddon(server.ID, server.Game);
                serverAddon.AddToLeft(listBox_ManageAddonsRight.Items.OfType<string>().ToList(), item);

                ListBox_ManageAddons_Refresh();

                foreach (var selected in listBox_ManageAddonsLeft.Items)
                {
                    if (selected.ToString() == item)
                    {
                        listBox_ManageAddonsLeft.SelectedItem = selected;
                    }
                }
            }
        }

        private void ListBox_ManageAddons_Refresh()
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var serverAddon = new ServerAddon(server.ID, server.Game);
            label_ManageAddonsName.Content = server.Name;
            label_ManageAddonsGame.Content = server.Game;
            label_ManageAddonsType.Content = serverAddon.GetModsName();

            listBox_ManageAddonsLeft.Items.Clear();
            foreach (string item in serverAddon.GetLeftListBox())
            {
                listBox_ManageAddonsLeft.Items.Add(item);
            }

            listBox_ManageAddonsRight.Items.Clear();
            foreach (string item in serverAddon.GetRightListBox())
            {
                listBox_ManageAddonsRight.Items.Add(item);
            }
        }

        private async Task<dynamic> Server_BeginStart(ServerTable server)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            ClearDetectedMainPort(server);
            ServerConfig serverConfig = new ServerConfig(server.ID);
            if (!string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId) &&
                !BuiltInGameCapabilities.SupportsSharedInstallation(server.Game))
            {
                string unsupportedSharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, null, server.Game);
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + unsupportedSharedInstallationError);
                return null;
            }

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, serverConfig, PluginsList);
            if (gameServer == null)
            {
                if (!string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
                {
                    Log(server.ID, "Server: Fail to start");
                    Log(server.ID, "[ERROR] " + LocalizationManager.Get("SharedInstallationUnsupported"));
                }
                return null;
            }

            string sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, gameServer, server.Game);
            if (sharedInstallationError != null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + sharedInstallationError);
                SetServerStatus(server, "Stopped");
                return null;
            }

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(500);

            //Add Start File to WindowsFirewall before start
            string startPath;
            try
            {
                startPath = !string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId)
                    ? Path.Combine(SharedInstallationStore.GetServerBinaryRoot(serverConfig.SharedInstallationId), gameServer.StartPath)
                    : ServerPath.GetServersServerFiles(server.ID, gameServer.StartPath);
            }
            catch (ArgumentException exception)
            {
                Log(server.ID, "[ERROR] " + exception.Message);
                startPath = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(gameServer.StartPath))
            {
                if (!string.IsNullOrWhiteSpace(startPath))
                {
                    WindowsFirewall firewall = new WindowsFirewall(Path.GetFileName(startPath), startPath);
                    if (!await firewall.IsRuleExist())
                    {
                        await firewall.AddRule();
                    }
                }
            }

            gameServer.AllowsEmbedConsole = GetServerMetadata(server.ID).EmbedConsole;
            Process p = await gameServer.Start();

            //Fail to start
            if (p == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + gameServer.Error);
                SetServerStatus(server, "Stopped");

                return null;
            }

            _serverMetadata[int.Parse(server.ID)].Process = p;
            metadata.ProcessStartInfoAvailable = HasProcessStartInfo(p);
            if (!metadata.ProcessStartInfoAvailable)
            {
                Log(server.ID, "[NOTICE] The server process was attached by PID. Console controls are unavailable until the server is restarted from WindowsGSM.");
            }
            Process startedProcess = p;
            p.Exited += (sender, e) => OnGameServerExited(server, startedProcess);

            if (metadata.ProcessStartInfoAvailable)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (!p.StartInfo.CreateNoWindow)
                        {
                            while (!p.HasExited && !ShowWindow(p.MainWindowHandle, WindowShowStyle.Minimize))
                            {
                                //Debug.WriteLine("Try Setting ShowMinNoActivate Console Window");
                            }

                            Debug.WriteLine("Set ShowMinNoActivate Console Window");

                            //Save MainWindow
                            _serverMetadata[int.Parse(server.ID)].MainWindow = p.MainWindowHandle;
                        }

                        //Fix for Factorio - The WaitForInputIdle never returns my guess is because Factorio is built
                        //  without a message loop seems to work fine with this commented out
                        //p.WaitForInputIdle();

                        if (!p.StartInfo.CreateNoWindow)
                        {
                            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);
                        }
                    }
                    catch
                    {
                        Debug.WriteLine("No Window require to hide");
                    }
                });
            }

            //An error may occur on ShowWindow if not adding this
            if (p == null || p.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = null;

                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] Exit Code: " + p.ExitCode.ToString());
                SetServerStatus(server, "Stopped");

                return null;
            }

            if (metadata != null)
            {
                metadata.ProcessExitedUnexpectedly = false;
                ResetServerQueryHealth(metadata);
            }

            // Set Priority
            p = Functions.CPU.Priority.SetProcessWithPriority(p, Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(server.ID).CPUPriority));

            // Set Affinity
            try
            {
                p.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(GetServerMetadata(server.ID).CPUAffinity);
            }
            catch (Exception e)
            {
                Log(server.ID, $"[NOTICE] Fail to set affinity. ({e.Message})");
            }

            // Save Cache
            ServerCache.SavePID(server.ID, p.Id);
            ServerCache.SaveProcessName(server.ID, p.ProcessName);
            ServerCache.SaveWindowsIntPtr(server.ID, GetServerMetadata(server.ID).MainWindow);

            SetWindowText(p.MainWindowHandle, server.Name);

            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);

            StartAutoUpdateCheck(server);

            StartRestartCrontabCheck(server);

            StartSendHeartBeat(server);

            StartPortDiscoveryAndQuery(server, GetServerMetadata(server.ID).Process);

            if (MahAppSwitch_SendStatistics.IsOn)
            {
                var analytics = new GoogleAnalytics();
                analytics.SendGameServerStart(server.ID, server.Game);
            }

            return gameServer;
        }

        private static bool HasProcessStartInfo(Process process)
        {
            try
            {
                _ = process.StartInfo;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private async Task<bool> Server_BeginStop(ServerTable server, Process p)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            bool processStartInfoAvailable = metadata.ProcessStartInfoAvailable;
            metadata.Process = null;
            metadata.ProcessStartInfoAvailable = false;
            ClearDetectedMainPort(server);

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, pluginList: PluginsList);
            try
            {
                await gameServer.Stop(p);
            }
            catch (InvalidOperationException exception) when (!processStartInfoAvailable)
            {
                Log(server.ID, $"[NOTICE] The recovered process cannot use its original console settings for a graceful stop. WindowsGSM will use its normal stop timeout and termination fallback. {exception.Message}");
            }

            for (int i = 0; i < 10; i++)
            {
                if (p == null || p.HasExited) { break; }
                await Task.Delay(1000);
            }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();

            // Save Cache
            ServerCache.SavePID(server.ID, -1);
            ServerCache.SaveProcessName(server.ID, string.Empty);
            ServerCache.SaveWindowsIntPtr(server.ID, (IntPtr)0);

            if (p != null && !p.HasExited)
            {
                p.Kill();
                return false;
            }

            return true;
        }

        private async Task<(Process, string, dynamic)> Server_BeginUpdate(ServerTable server, bool silenceCheck, bool forceUpdate, bool validate = false, string custum = null)
        {
            ServerConfig serverConfig = new ServerConfig(server.ID);
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, serverConfig, PluginsList);
            string sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, gameServer, server.Game);
            if (sharedInstallationError != null)
            {
                if (gameServer != null)
                {
                    gameServer.Error = sharedInstallationError;
                }
                return (null, string.Empty, gameServer);
            }

            string localVersion = gameServer.GetLocalBuild();
            if (string.IsNullOrWhiteSpace(localVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            string remoteVersion = await gameServer.GetRemoteBuild();
            if (string.IsNullOrWhiteSpace(remoteVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            if (!silenceCheck)
            {
                Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");
            }

            if ((!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion) && localVersion != remoteVersion) || forceUpdate)
            {
                try
                {
                    return (await gameServer.Update(validate, custum), remoteVersion, gameServer);
                }
                catch
                {
                    return (await gameServer.Update(), remoteVersion, gameServer);
                }
            }

            return (null, remoteVersion, gameServer);
        }

        #region Actions - Game Server
        private async Task GameServer_Start(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            string error = string.Empty;
            if (!string.IsNullOrWhiteSpace(server.IP) && !IsValidIPAddress(server.IP))
            {
                error += " IP address is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.Port) && !IsValidPort(server.Port))
            {
                error += " Port number is not valid.";
            }

            if (error != string.Empty)
            {
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR]" + error);

                return;
            }

            Process p = GetServerMetadata(server.ID).Process;
            if (p != null) { return; }

            ServerConfig serverConfig = new ServerConfig(server.ID);
            if (!string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
            {
                string sharedInstallationError;
                if (!BuiltInGameCapabilities.SupportsSharedInstallation(server.Game))
                {
                    sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, null, server.Game);
                }
                else
                {
                    object gameServerObject = GameServer.Data.Class.Get(server.Game, serverConfig, PluginsList);
                    sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, gameServerObject, server.Game);
                }

                if (sharedInstallationError != null)
                {
                    Log(server.ID, "Server: Fail to start");
                    Log(server.ID, "[ERROR] " + sharedInstallationError);
                    return;
                }
            }

            if (GetServerMetadata(server.ID).BackupOnStart)
            {
                await GameServer_Backup(server, " | Backup on Start");
            }

            if (GetServerMetadata(server.ID).UpdateOnStart)
            {
                await GameServer_Update(server, " | Update on Start");
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
            Log(server.ID, "Action: Start" + notes);
            SetServerStatus(server, "Starting");

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Started");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        private async Task GameServer_Stop(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //Begin stop
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
            Log(server.ID, "Action: Stop");
            SetServerStatus(server, "Stopping");

            bool stopGracefully = await Server_BeginStop(server, p);

            Log(server.ID, "Server: Stopped");
            if (!stopGracefully)
            {
                Log(server.ID, "[NOTICE] Server fail to stop gracefully");
            }
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");
        }

        private async Task GameServer_Restart(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            _serverMetadata[int.Parse(server.ID)].Process = null;

            //Begin Restart
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
            Log(server.ID, "Action: Restart");
            SetServerStatus(server, "Restarting");

            await Server_BeginStop(server, p);

            await Task.Delay(1000);

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Restarted");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        private async Task<bool> GameServer_Update(ServerTable server, string notes = "", bool validate = false)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            try
            {
                ServerConfig serverConfig = new ServerConfig(server.ID);
                string installationId = serverConfig.SharedInstallationId;
                if (!string.IsNullOrWhiteSpace(installationId))
                {
                    string sharedInstallationError;
                    if (!BuiltInGameCapabilities.SupportsSharedInstallation(server.Game))
                    {
                        sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, null, server.Game);
                    }
                    else
                    {
                        object gameServerObject = GameServer.Data.Class.Get(server.Game, serverConfig, PluginsList);
                        sharedInstallationError = GetSharedInstallationEligibilityError(serverConfig, gameServerObject, server.Game);
                    }

                    if (sharedInstallationError != null)
                    {
                        Log(server.ID, "Server: Fail to update");
                        Log(server.ID, "[ERROR] " + sharedInstallationError);
                        return false;
                    }

                    List<string> linkedServerIds = SharedInstallationStore.GetLinkedServerIds(installationId);
                    Log(server.ID, $"[NOTICE] Shared installation update affects server instances: {string.Join(", ", linkedServerIds)}.");
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Log(server.ID, "Server: Fail to update");
                Log(server.ID, $"[ERROR] Could not determine shared installation impact: {exception.Message}");
                return false;
            }

            //Begin update
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
            Log(server.ID, "Action: Update" + notes);
            SetServerStatus(server, "Updating");

            bool updateSucceeded = false;
            try
            {
                var (p, remoteVersion, gameServer) = await Server_BeginUpdate(server, silenceCheck: validate, forceUpdate: true, validate: validate);

                if (gameServer == null)
                {
                    Log(server.ID, "Server: Fail to update");
                    Log(server.ID, "[ERROR] " + LocalizationManager.Get("GameServerIntegrationUnavailable"));
                }
                else if (p == null && string.IsNullOrEmpty(gameServer.Error))
                {
                    Log(server.ID, $"Server: Updated ({remoteVersion})");
                    updateSucceeded = true;
                }
                else if (p != null)
                {
                    await Task.Run(() => p.WaitForExit());
                    if (p.ExitCode == 0)
                    {
                        Log(server.ID, $"Server: Updated ({remoteVersion})");
                        updateSucceeded = true;
                    }
                    else
                    {
                        Log(server.ID, "Server: Fail to update");
                        Log(server.ID, $"[ERROR] Update process exited with code {p.ExitCode}.");
                    }
                }
                else
                {
                    Log(server.ID, "Server: Fail to update");
                    Log(server.ID, "[ERROR] " + gameServer.Error);
                }
            }
            finally
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
            }

            return updateSucceeded;
        }

        private async Task<bool> GameServer_Backup(
            ServerTable server,
            string notes = "",
            string backupType = BackupScheduleConfig.BackupTypeFullServer,
            ConfigurationBackupContext preparedConfigurationContext = null)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            bool configurationOnly = backupType == BackupScheduleConfig.BackupTypeConfiguration;
            if (!configurationOnly && backupType != BackupScheduleConfig.BackupTypeFullServer)
            {
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, $"[ERROR] Unsupported backup type: {backupType}.");
                return false;
            }

            //Begin backup
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Backuping;
            Log(server.ID, "Action: Backup" + notes);
            SetServerStatus(server, "Backuping");

            string temporaryZipPath = null;
            try
            {
                ConfigurationBackupContext configurationContext = preparedConfigurationContext;
                if (configurationOnly && configurationContext == null)
                {
                    List<PluginMetadata> pluginSnapshot = PluginsList?.ToList();
                    configurationContext = await Task.Run(() => GetConfigurationBackupContext(server.ID, pluginSnapshot));
                }
                await EndAllRunningProcess(server.ID);
                await Task.Delay(1000);

                BackupConfig backupConfig = new BackupConfig(server.ID);
                string backupLocation = ServerPath.GetBackups(server.ID);
                string backupPrefix = configurationOnly
                    ? $"WGSM-Backup-Config-Server-{server.ID}-"
                    : $"WGSM-Backup-Server-{server.ID}-";
                string backupExtension = configurationOnly ? ConfigurationBackupArchive.FileExtension : ".zip";
                string backupFileName = configurationOnly
                    ? $"{backupPrefix}{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{backupExtension}"
                    : $"{backupPrefix}{DateTime.Now:yyyyMMddHHmmssfff}{backupExtension}";
                string backupPath = Path.Combine(backupLocation, backupFileName);

                if (configurationOnly)
                {
                    ConfigurationBackupResult backupResult = await Task.Run(() => CreateConfigurationBackup(configurationContext, backupPath));
                    string includedPaths = string.Join(", ", backupResult.IncludedPaths.Select(path => path.RootId + "/" + path.RelativePath));
                    Log(server.ID, string.Format(LocalizationManager.Get("ConfigurationBackupResult"), includedPaths));
                }
                else
                {
                    temporaryZipPath = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    string startPath = ServerPath.GetServers(server.ID);

                    using (SharedInstallationStore.AcquireReadLeaseForServer(new ServerConfig(server.ID)))
                    {
                        await Task.Run(() => ZipFile.CreateFromDirectory(startPath, temporaryZipPath));
                    }
                    File.Move(temporaryZipPath, backupPath);
                    temporaryZipPath = null;
                }

                string retentionError = null;
                try
                {
                    await Task.Run(() => ApplyBackupRetention(
                        backupLocation,
                        backupPrefix,
                        backupExtension,
                        backupConfig.MaximumBackups));
                }
                catch (Exception e) when (IsExpectedBackupFileException(e))
                {
                    retentionError = e.Message;
                }

                if (!string.IsNullOrWhiteSpace(retentionError))
                {
                    Log(server.ID, $"[NOTICE] Backup completed, but old backups could not be removed: {retentionError}");
                }

                Log(server.ID, "Server: Backuped");
                return true;
            }
            catch (Exception e) when (IsExpectedBackupFileException(e))
            {
                string cleanupError = null;
                if (!string.IsNullOrWhiteSpace(temporaryZipPath) && File.Exists(temporaryZipPath))
                {
                    try
                    {
                        File.Delete(temporaryZipPath);
                    }
                    catch (Exception cleanupException) when (IsExpectedBackupFileException(cleanupException))
                    {
                        cleanupError = cleanupException.Message;
                    }
                }

                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, $"[ERROR] {e.Message}");
                if (!string.IsNullOrWhiteSpace(cleanupError))
                {
                    Log(server.ID, $"[ERROR] Temporary backup cleanup failed: {cleanupError}");
                }
                return false;
            }
            finally
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
            }
        }

        private sealed class ConfigurationBackupContext
        {
            public ServerConfig ServerConfig { get; set; }
            public Dictionary<string, string> Roots { get; set; }
            public List<ConfigurationBackupPath> Paths { get; set; }
            public string SharedInstallationBuildId { get; set; } = string.Empty;
        }

        private ConfigurationBackupContext GetConfigurationBackupContext(
            string serverId,
            IEnumerable<PluginMetadata> pluginSnapshot = null)
        {
            ServerConfig serverConfig = new ServerConfig(serverId);
            if (!IsConfigurationBackupSupported(serverConfig.ServerGame))
            {
                throw new InvalidOperationException(LocalizationManager.Get("ConfigurationBackupUnsupported"));
            }

            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "serverfiles", ServerPath.GetServersServerFiles(serverId) }
            };

            List<ConfigurationBackupPath> paths;
            if (BuiltInGameCapabilities.SupportsConfigurationBackup(serverConfig.ServerGame))
            {
                if (BuiltInGameCapabilities.IsGoldSourceGame(serverConfig.ServerGame) &&
                    !string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
                {
                    SharedInstallationStore.EnsureReady(serverConfig.SharedInstallationId);
                    roots.Add(
                        "sharedserverfiles",
                        SharedInstallationStore.GetServerBinaryRoot(serverConfig.SharedInstallationId));
                }

                paths = ConfigurationBackupArchive.ValidatePlan(
                    roots,
                    BuiltInGameCapabilities.GetConfigurationBackupPlan(serverConfig, roots));
            }
            else
            {
                PluginMetadata plugin = GetLoadedPlugin(serverConfig.ServerGame, pluginSnapshot);
                if (plugin == null ||
                    plugin.ApiVersion != PluginApiLevel.CapabilityMethods ||
                    (plugin.Capabilities & WindowsGSMPluginCapability.ConfigurationBackup) == 0)
                {
                    throw new InvalidOperationException(LocalizationManager.Get("ConfigurationBackupUnsupported"));
                }

                object pluginInstance;
                try
                {
                    pluginInstance = PluginManagement.GetPluginClass(plugin, serverConfig);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{plugin.FileName}' could not be initialized for its backup plan: {exception.Message}",
                        exception);
                }

                var pluginContext = new PluginInstanceContext(
                    serverConfig.ServerID,
                    serverConfig.ServerGame,
                    serverConfig.SharedInstallationId,
                    roots);
                PluginBackupPlan plan;
                try
                {
                    plan = PluginCapabilityInspector.GetConfigOnlyBackupPlan(pluginInstance, pluginContext);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{plugin.FileName}' failed to provide its backup plan: {exception.Message}",
                        exception);
                }

                if (plan == null || plan.SchemaVersion != PluginBackupPlan.CurrentSchemaVersion)
                {
                    throw new InvalidDataException($"Plugin '{plugin.FileName}' returned an empty or unsupported backup plan.");
                }

                paths = ConfigurationBackupArchive.ValidatePlan(roots, plan.Paths);
            }

            return new ConfigurationBackupContext
            {
                ServerConfig = serverConfig,
                Roots = roots,
                Paths = paths
            };
        }

        private static ConfigurationBackupResult CreateConfigurationBackup(ConfigurationBackupContext context, string archivePath)
        {
            using (SharedInstallationStore.AcquireReadLeaseForServer(context.ServerConfig))
            {
                context.SharedInstallationBuildId = GetSharedInstallationBuildId(context.ServerConfig.SharedInstallationId);
                return ConfigurationBackupArchive.Create(
                    archivePath,
                    context.ServerConfig.ServerID,
                    context.ServerConfig.ServerGame,
                    context.ServerConfig.SharedInstallationId,
                    context.SharedInstallationBuildId,
                    context.Roots,
                    context.Paths);
            }
        }

        private static List<string> GetConfigurationBackupRestorePreview(ConfigurationBackupContext context, string archivePath)
        {
            using (SharedInstallationStore.AcquireReadLeaseForServer(context.ServerConfig))
            {
                context.SharedInstallationBuildId = GetSharedInstallationBuildId(context.ServerConfig.SharedInstallationId);
                return ConfigurationBackupArchive.GetRestorePreview(
                    archivePath,
                    context.ServerConfig.ServerID,
                    context.ServerConfig.ServerGame,
                    context.ServerConfig.SharedInstallationId,
                    context.SharedInstallationBuildId,
                    context.Roots,
                    context.Paths);
            }
        }

        private static List<string> RestoreConfigurationBackup(ConfigurationBackupContext context, string archivePath)
        {
            using (SharedInstallationStore.AcquireReadLeaseForServer(context.ServerConfig))
            {
                context.SharedInstallationBuildId = GetSharedInstallationBuildId(context.ServerConfig.SharedInstallationId);
                return ConfigurationBackupArchive.Restore(
                    archivePath,
                    context.ServerConfig.ServerID,
                    context.ServerConfig.ServerGame,
                    context.ServerConfig.SharedInstallationId,
                    context.SharedInstallationBuildId,
                    context.Roots,
                    context.Paths);
            }
        }

        private static string GetSharedInstallationBuildId(string installationId)
        {
            if (string.IsNullOrWhiteSpace(installationId))
            {
                return string.Empty;
            }

            SharedInstallationRecord installation = SharedInstallationStore.GetInstallations()
                .SingleOrDefault(record => string.Equals(record.Id, installationId, StringComparison.OrdinalIgnoreCase));
            if (installation == null)
            {
                throw new InvalidDataException("The referenced shared installation could not be found.");
            }

            return installation.BuildId ?? string.Empty;
        }

        private static void ApplyBackupRetention(string backupLocation, string backupPrefix, string backupExtension, int maximumBackups)
        {
            foreach (FileInfo backupFile in new DirectoryInfo(backupLocation)
                .GetFiles("*" + backupExtension)
                .Where(file => file.Name.StartsWith(backupPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(maximumBackups))
            {
                backupFile.Delete();
            }
        }

        private static bool IsExpectedBackupFileException(Exception exception)
        {
            return exception is IOException ||
                exception is InvalidDataException ||
                exception is InvalidOperationException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is System.Security.SecurityException;
        }

        private async Task<bool> GameServer_RestoreBackup(ServerTable server, string backupFile)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            string backupLocation = ServerPath.GetBackups(server.ID);
            string backupPath = Path.Combine(backupLocation, backupFile);
            if (!File.Exists(backupPath))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Backup not found");
                return false;
            }

            if (string.Equals(Path.GetExtension(backupPath), ConfigurationBackupArchive.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return await GameServer_RestoreConfigurationBackup(server, backupPath);
            }
            if (!string.Equals(Path.GetExtension(backupPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Unsupported backup file type");
                return false;
            }

            try
            {
                string sharedInstallationId = new ServerConfig(server.ID).SharedInstallationId;
                if (!string.IsNullOrWhiteSpace(sharedInstallationId) &&
                    SharedInstallationStore.IsServerFilesRootOwner(server.ID, sharedInstallationId) &&
                    SharedInstallationStore.GetLinkedServerIds(sharedInstallationId)
                        .Any(serverId => !string.Equals(serverId, server.ID, StringComparison.OrdinalIgnoreCase)))
                {
                    string message = LocalizationManager.Get("SharedInstallationOwnerRestoreBlocked");
                    Log(server.ID, "Server: Fail to restore backup");
                    Log(server.ID, "[ERROR] " + message);
                    MessageBox.Show(message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                exception is InvalidDataException || exception is UnauthorizedAccessException ||
                exception is InvalidOperationException)
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] " + exception.Message);
                MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Backup");
            SetServerStatus(server, "Restoring");

            string extractPath = ServerPath.GetServers(server.ID);
            if (Directory.Exists(extractPath))
            {
                string ex = string.Empty;
                await Task.Run(() =>
                {
                    try
                    {
                        Directory.Delete(extractPath, true);
                    }
                    catch (Exception e)
                    {
                        ex = e.Message;
                    }
                });

                if (ex != string.Empty)
                {
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    Log(server.ID, "Server: Fail to restore backup");
                    Log(server.ID, $"[ERROR] {ex}");
                    SetServerStatus(server, "Stopped");
                    return false;
                }
            }

            string error = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    ZipFile.ExtractToDirectory(backupPath, extractPath);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Restored");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_RestoreConfigurationBackup(ServerTable server, string backupPath)
        {
            ConfigurationBackupContext configurationContext;
            List<string> includedPaths;
            try
            {
                List<PluginMetadata> pluginSnapshot = PluginsList?.ToList();
                configurationContext = await Task.Run(() => GetConfigurationBackupContext(server.ID, pluginSnapshot));
                includedPaths = await Task.Run(() => GetConfigurationBackupRestorePreview(configurationContext, backupPath));
            }
            catch (Exception exception) when (IsExpectedBackupFileException(exception))
            {
                Log(server.ID, "Server: Fail to restore configuration backup");
                Log(server.ID, $"[ERROR] {exception.Message}");
                return false;
            }

            string preview = string.Join(Environment.NewLine, includedPaths);
            string confirmation = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Get("ConfigurationBackupRestoreConfirmation"),
                server.Name,
                preview);
            if (MessageBox.Show(confirmation, LocalizationManager.Get("ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return false;
            }

            if (ConfigurationBackupArchive.HasExistingFiles(configurationContext.Roots, configurationContext.Paths))
            {
                try
                {
                    BackupConfig backupConfig = new BackupConfig(server.ID);
                    string backupLocation = ServerPath.GetBackups(server.ID);
                    string snapshotPrefix = $"WGSM-Backup-Config-Server-{server.ID}-";
                    string snapshotPath = Path.Combine(
                        backupLocation,
                        $"{snapshotPrefix}{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{ConfigurationBackupArchive.FileExtension}");
                    ConfigurationBackupResult snapshot = await Task.Run(() => CreateConfigurationBackup(configurationContext, snapshotPath));
                    Log(server.ID, string.Format(LocalizationManager.Get("ConfigurationBackupSnapshotCreated"), snapshot.ArchivePath));

                    try
                    {
                        await Task.Run(() => ApplyBackupRetention(
                            backupLocation,
                            snapshotPrefix,
                            ConfigurationBackupArchive.FileExtension,
                            backupConfig.MaximumBackups));
                    }
                    catch (Exception exception) when (IsExpectedBackupFileException(exception))
                    {
                        Log(server.ID, $"[NOTICE] Configuration snapshot was created, but old snapshots could not be removed: {exception.Message}");
                    }
                }
                catch (Exception exception) when (IsExpectedBackupFileException(exception))
                {
                    Log(server.ID, "Server: Fail to restore configuration backup");
                    Log(server.ID, $"[ERROR] Could not create the pre-restore configuration snapshot: {exception.Message}");
                    return false;
                }
            }
            else
            {
                Log(server.ID, $"[NOTICE] {LocalizationManager.Get("ConfigurationBackupSnapshotUnavailable")}");
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Configuration Backup");
            SetServerStatus(server, "Restoring");
            try
            {
                List<string> restoredPaths = await Task.Run(() => RestoreConfigurationBackup(configurationContext, backupPath));
                Log(server.ID, "Server: Restored");
                Log(server.ID, string.Format(
                    LocalizationManager.Get("ConfigurationBackupResult"),
                    string.Join(", ", restoredPaths)));
                return true;
            }
            catch (Exception exception) when (IsExpectedBackupFileException(exception))
            {
                Log(server.ID, "Server: Fail to restore configuration backup");
                Log(server.ID, $"[ERROR] {exception.Message}");
                return false;
            }
            finally
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
            }
        }

        private async Task<bool> GameServer_Delete(ServerTable server, string sharedInstallationId)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            bool preserveSharedBinaryRoot = false;
            if (!string.IsNullOrWhiteSpace(sharedInstallationId))
            {
                try
                {
                    preserveSharedBinaryRoot = SharedInstallationStore.IsServerFilesRootOwner(server.ID, sharedInstallationId);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                    exception is InvalidDataException || exception is UnauthorizedAccessException ||
                    exception is InvalidOperationException)
                {
                    Log(server.ID, "Server: Fail to delete server");
                    Log(server.ID, "[ERROR] " + exception.Message);
                    MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            //Begin delete
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Deleting;
            Log(server.ID, "Action: Delete");
            SetServerStatus(server, "Deleting");

            //Remove firewall rule
            var firewall = new WindowsFirewall(null, ServerPath.GetServers(server.ID));
            await firewall.RemoveRuleEx();

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string serverPath = ServerPath.GetServers(server.ID);
            string deleteError = string.Empty;

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(serverPath))
                    {
                        if (preserveSharedBinaryRoot)
                        {
                            string preservedRoot = Path.GetFullPath(ServerPath.GetServersServerFiles(server.ID))
                                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            foreach (string entry in Directory.GetFileSystemEntries(serverPath))
                            {
                                string fullEntryPath = Path.GetFullPath(entry)
                                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                if (string.Equals(fullEntryPath, preservedRoot, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                if (Directory.Exists(entry))
                                {
                                    Directory.Delete(entry, true);
                                }
                                else
                                {
                                    File.Delete(entry);
                                }
                            }
                        }
                        else
                        {
                            Directory.Delete(serverPath, true);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                    exception is System.Security.SecurityException)
                {
                    deleteError = exception.Message;
                }
            });

            await Task.Delay(1000);

            if (!string.IsNullOrWhiteSpace(deleteError))
            {
                Log(server.ID, "Server: Fail to delete server");
                Log(server.ID, "[ERROR] " + deleteError);

                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");

                MessageBox.Show(deleteError, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (Directory.Exists(serverPath))
            {
                string wgsmCfgPath = ServerPath.GetServersConfigs(server.ID, "WindowsGSM.cfg");
                if (File.Exists(wgsmCfgPath))
                {
                    Log(server.ID, "Server: Fail to delete server");
                    Log(server.ID, "[ERROR] Directory is not accessible");

                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    SetServerStatus(server, "Stopped");

                    return false;
                }
            }

            string sharedInstallationDeleteError = string.Empty;
            if (!string.IsNullOrWhiteSpace(sharedInstallationId))
            {
                try
                {
                    if (!SharedInstallationStore.DeleteIfUnreferenced(sharedInstallationId))
                    {
                        WindowsGSM.GameServer.Engine.GoldSource.DeleteSharedMapCycleFile(sharedInstallationId, server.ID);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    sharedInstallationDeleteError = exception.Message;
                }
            }

            Log(server.ID, "Server: Deleted server");

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            LoadServerTable();
            if (!string.IsNullOrWhiteSpace(sharedInstallationDeleteError))
            {
                MessageBox.Show(
                    string.Format(LocalizationManager.Get("SharedInstallationDeleteFailed"), sharedInstallationDeleteError),
                    LocalizationManager.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }
        #endregion

        private async void OnGameServerExited(ServerTable server, Process process)
        {
            if (System.Windows.Application.Current == null) { return; }

            await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                int serverId = int.Parse(server.ID);
                ServerMetadata metadata = GetServerMetadata(server.ID);
                if (metadata == null || !ReferenceEquals(metadata.Process, process)) { return; }

                ClearDetectedMainPort(server);

                if (metadata.ServerStatus == ServerStatus.Started)
                {
                    metadata.ProcessExitedUnexpectedly = true;
                    bool autoRestart = GetServerMetadata(serverId).AutoRestart;
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = autoRestart ? ServerStatus.Restarting : ServerStatus.Stopped;
                    Log(server.ID, "Server: Crashed");
                    SetServerStatus(server, autoRestart ? "Restarting" : "Stopped");

                    if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).CrashAlert)
                    {
                        var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                        await webhook.Send(server.ID, server.Game, "Crashed", server.Name, server.IP, server.Port);
                    }

                    _serverMetadata[int.Parse(server.ID)].Process = null;

                    if (autoRestart)
                    {
                        if (GetServerMetadata(server.ID).BackupOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Backup(server, " | Backup on Start");
                        }

                        if (GetServerMetadata(server.ID).UpdateOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Update(server, " | Update on Start");
                        }

                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null)
                        {
                            if (metadata.ServerStatus != ServerStatus.Stopped)
                            {
                                metadata.ServerStatus = ServerStatus.Stopped;
                                SetServerStatus(server, "Stopped");
                            }
                            return;
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Started | Auto Restart");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoRestartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Auto Restart", server.Name, server.IP, server.Port);
                        }
                    }
                }
            });
        }

        const int UPDATE_INTERVAL_MINUTE = 30;
        private async void StartAutoUpdateCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID), PluginsList);

            string localVersion = gameServer.GetLocalBuild();

            while (p != null && !p.HasExited)
            {
                await Task.Delay(60000 * UPDATE_INTERVAL_MINUTE);

                if (!GetServerMetadata(server.ID).AutoUpdate || GetServerMetadata(server.ID).ServerStatus == ServerStatus.Updating)
                {
                    continue;
                }

                if (p == null || p.HasExited) { break; }

                //Try to get local build again if not found just now
                if (string.IsNullOrWhiteSpace(localVersion))
                {
                    localVersion = gameServer.GetLocalBuild();
                }

                //Get remote build
                string remoteVersion = await gameServer.GetRemoteBuild();

                //Continue if success to get localVersion and remoteVersion
                if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion))
                {
                    if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                    {
                        break;
                    }

                    Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");

                    if (localVersion != remoteVersion)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin stop
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
                        SetServerStatus(server, "Stopping");

                        //Stop the server
                        await Server_BeginStop(server, p);

                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
                        SetServerStatus(server, "Updating");

                        //Update the server
                        await gameServer.Update();

                        if (string.IsNullOrWhiteSpace(gameServer.Error))
                        {
                            Log(server.ID, $"Server: Updated ({remoteVersion})");

                            if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoUpdateAlert)
                            {
                                var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                                await webhook.Send(server.ID, server.Game, "Updated | Auto Update", server.Name, server.IP, server.Port);
                            }
                        }
                        else
                        {
                            Log(server.ID, "Server: Fail to update");
                            Log(server.ID, "[ERROR] " + gameServer.Error);
                        }

                        //Start the server
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
                        SetServerStatus(server, "Starting");

                        var gameServerStart = await Server_BeginStart(server);
                        if (gameServerStart == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Started");
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        break;
                    }
                }
                else if (string.IsNullOrWhiteSpace(localVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get local build.");
                }
                else if (string.IsNullOrWhiteSpace(remoteVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get remote build.");
                }
            }
        }

        private async void StartRestartCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                //If not enable return
                if (!GetServerMetadata(serverId).RestartCrontab)
                {
                    await Task.Delay(1000);

                    continue;
                }

                //Try get next DataTime restart
                DateTime? crontabTime = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now);

                //Delay 1 second for later compare
                await Task.Delay(1000);

                //Return if crontab expression is invalid
                if (crontabTime == null) { continue; }

                //If now >= crontab time
                if (DateTime.Compare(DateTime.Now, crontabTime ?? DateTime.Now) >= 0)
                {
                    //Update the next crontab
                    var currentRow = (ServerTable)ServerGrid.SelectedItem;
                    if (currentRow.ID == server.ID)
                    {
                        textBox_nextcrontab.Text = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");
                    }

                    if (p == null || p.HasExited)
                    {
                        break;
                    }

                    //Restart the server
                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin Restart
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
                        Log(server.ID, "Action: Restart");
                        SetServerStatus(server, "Restarting");

                        await Server_BeginStop(server, p);
                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Restarted | Restart Crontab");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).RestartCrontabAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Restart Crontab", server.Name, server.IP, server.Port);
                        }

                        break;
                    }
                }
            }
        }

        private async void StartSendHeartBeat(ServerTable server)
        {
            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                if (MahAppSwitch_SendStatistics.IsOn)
                {
                    var analytics = new GoogleAnalytics();
                    analytics.SendGameServerHeartBeat(server.Game, server.Name);
                }

                await Task.Delay(300000);
            }
        }

        private async void StartPortDiscoveryAndQuery(ServerTable server, Process process)
        {
            await StartMainPortDiscovery(server, process);
            if (IsCurrentServerProcess(server.ID, process))
            {
                StartQuery(server);
            }
        }

        private void StartBackupScheduleScheduler()
        {
            _backupScheduleStartedAt = DateTime.Now;
            _backupScheduleLastTickAt = _backupScheduleStartedAt;
            _backupScheduleTimer.Interval = TimeSpan.FromSeconds(30);
            _backupScheduleTimer.Tick += BackupScheduleTimer_Tick;
            _backupScheduleTimer.Start();
        }

        private async void BackupScheduleTimer_Tick(object sender, EventArgs e)
        {
            if (_backupScheduleTickRunning) { return; }

            _backupScheduleTickRunning = true;
            DateTime now = DateTime.Now;
            DateTime previousTick = _backupScheduleLastTickAt;
            _backupScheduleLastTickAt = now;

            try
            {
                foreach (ServerTable server in ServerGroups_GetAllServerRows())
                {
                    if (!TryLoadBackupSchedule(server.ID, out BackupScheduleConfig config, out _)) { continue; }

                    ServerMetadata metadata = GetServerMetadata(server.ID);
                    if (config.LastResult == BackupScheduleConfig.ResultRunning)
                    {
                        if (metadata != null && metadata.ServerStatus == ServerStatus.Backuping) { continue; }

                        config.LastResult = BackupScheduleConfig.ResultInterrupted;
                        config.LastResultMessage = BackupScheduleConfig.MessageInterrupted;
                        config.LastEvaluatedAt = now;
                        if (!TrySaveBackupSchedule(server.ID, config, out _)) { continue; }
                    }

                    if (!config.Enabled) { continue; }

                    CrontabSchedule schedule = CrontabSchedule.TryParse(config.CronExpression);
                    if (schedule == null) { continue; }

                    DateTime baseline = config.LastEvaluatedAt ?? _backupScheduleStartedAt;
                    if (config.LastAttemptedOccurrence.HasValue && config.LastAttemptedOccurrence.Value > baseline)
                    {
                        baseline = config.LastAttemptedOccurrence.Value;
                    }
                    if (baseline >= now) { continue; }

                    List<DateTime> dueOccurrences = schedule.GetNextOccurrences(baseline, now).Take(2).ToList();
                    if (dueOccurrences.Count == 0) { continue; }

                    DateTime scheduledOccurrence = dueOccurrences[0];
                    bool missed = scheduledOccurrence <= _backupScheduleStartedAt ||
                        dueOccurrences.Count > 1 ||
                        now - previousTick > TimeSpan.FromMinutes(2) ||
                        now - scheduledOccurrence > TimeSpan.FromMinutes(1);

                    config.LastEvaluatedAt = now;
                    config.LastAttemptedOccurrence = scheduledOccurrence;
                    config.LastAttemptedAt = now;
                    if (missed)
                    {
                        config.LastResult = BackupScheduleConfig.ResultMissed;
                        config.LastResultMessage = BackupScheduleConfig.MessageMissed;
                        if (TrySaveBackupSchedule(server.ID, config, out _))
                        {
                            Log(server.ID, $"[NOTICE] {LocalizationManager.Get("ScheduleMissedMessage")}");
                        }
                        if (hMenu_Schedules.Visibility == Visibility.Visible) { RefreshBackupSchedules(server.ID); }
                        continue;
                    }

                    if (metadata == null || metadata.ServerStatus != ServerStatus.Stopped)
                    {
                        config.LastResult = BackupScheduleConfig.ResultSkipped;
                        config.LastResultMessage = BackupScheduleConfig.MessageServerBusy;
                        if (TrySaveBackupSchedule(server.ID, config, out _))
                        {
                            Log(server.ID, $"[NOTICE] {LocalizationManager.Get("ScheduleServerBusyMessage")}");
                        }
                        if (hMenu_Schedules.Visibility == Visibility.Visible) { RefreshBackupSchedules(server.ID); }
                        continue;
                    }

                    config.LastResult = BackupScheduleConfig.ResultRunning;
                    config.LastResultMessage = string.Empty;
                    if (!TrySaveBackupSchedule(server.ID, config, out _)) { continue; }

                    bool completed = await GameServer_Backup(server, " | Scheduled backup", config.BackupType);
                    config.LastEvaluatedAt = DateTime.Now;
                    config.LastAttemptedAt = DateTime.Now;
                    config.LastResult = completed ? BackupScheduleConfig.ResultCompleted : BackupScheduleConfig.ResultFailed;
                    config.LastResultMessage = completed ? BackupScheduleConfig.MessageCompleted : BackupScheduleConfig.MessageFailed;
                    TrySaveBackupSchedule(server.ID, config, out _);
                    if (hMenu_Schedules.Visibility == Visibility.Visible) { RefreshBackupSchedules(server.ID); }
                }
            }
            finally
            {
                _backupScheduleTickRunning = false;
            }
        }

        private async Task StartMainPortDiscovery(ServerTable server, Process process)
        {
            if (process == null ||
                !IsValidIPAddress(server.IP) ||
                !int.TryParse(server.Port, out int configuredPort) ||
                !IsValidPort(server.Port))
            {
                return;
            }

            int configuredQueryPort = int.TryParse(server.QueryPort, out int queryPort) && IsValidPort(server.QueryPort)
                ? queryPort
                : 0;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (attempt == 0) { await Task.Delay(1000); }
                if (!IsCurrentServerProcess(server.ID, process)) { return; }

                int? detectedMainPort;
                try
                {
                    detectedMainPort = await Task.Run(() => ServerPortDiscovery.FindClientPort(
                        process,
                        server.IP,
                        configuredPort,
                        configuredQueryPort));
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    Log(server.ID, $"[NOTICE] Main port discovery failed: {e.Message}");
                    return;
                }

                if (!IsCurrentServerProcess(server.ID, process)) { return; }
                if (detectedMainPort.HasValue && (detectedMainPort.Value == configuredPort || attempt == 4))
                {
                    ServerMetadata metadata = GetServerMetadata(server.ID);
                    metadata.DetectedMainPort = detectedMainPort;
                    metadata.DetectedMainPortProcessId = process.Id;
                    RefreshServerMainPortDisplay(server, detectedMainPort);
                    return;
                }

                if (attempt < 4) { await Task.Delay(1000); }
            }

            Log(server.ID, "[NOTICE] Main port could not be verified; the configured value remains displayed.");
        }

        private async void StartQuery(ServerTable server)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            if (metadata == null) { return; }

            ResetServerQueryHealth(metadata);
            if (string.IsNullOrWhiteSpace(server.IP) || !IsValidIPAddress(server.IP))
            {
                metadata.QuerySupported = false;
                RefreshServerHealth(server);
                return;
            }

            // Check the server support Query Method
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, pluginList: PluginsList);
            if (gameServer == null)
            {
                metadata.QuerySupported = false;
                RefreshServerHealth(server);
                return;
            }
            object queryMethod = gameServer.QueryMethod;
            if (queryMethod == null)
            {
                metadata.QuerySupported = false;
                RefreshServerHealth(server);
                return;
            }

            // Save the process of game server
            Process p = metadata.Process;
            if (p == null)
            {
                metadata.QuerySupported = false;
                RefreshServerHealth(server);
                return;
            }

            int? resolvedQueryPort = GetHealthQueryPort(server, gameServer, metadata);
            if (!resolvedQueryPort.HasValue)
            {
                metadata.QuerySupported = false;
                RefreshServerHealth(server);
                return;
            }

            metadata.QuerySupported = true;
            RefreshServerHealth(server);

            dynamic query = queryMethod;
            int queryPort = resolvedQueryPort.Value;

            // Query server every 5 seconds
            while (IsCurrentServerProcess(server.ID, p))
            {
                metadata = GetServerMetadata(server.ID);
                if (metadata == null || metadata.ServerStatus == ServerStatus.Stopped || metadata.ServerStatus == ServerStatus.Stopping)
                {
                    break;
                }

                DateTime attemptAt = DateTime.Now;
                metadata.QueryInProgress = true;
                RefreshServerHealth(server);

                string players = null;
                string queryError = null;
                try
                {
                    query.SetAddressPort(server.IP, queryPort);
                    players = await query.GetPlayersAndMaxPlayers();
                }
                catch (Exception e) when (IsExpectedServerQueryException(e))
                {
                    queryError = e.Message;
                }

                if (!IsCurrentServerProcess(server.ID, p)) { break; }

                metadata.QueryInProgress = false;
                metadata.LastQueryAttemptAt = attemptAt;

                if (players != null)
                {
                    metadata.LastQuerySucceededAt = DateTime.Now;
                    metadata.ConsecutiveQueryFailures = 0;
                    metadata.LastQueryFailureMessage = string.Empty;
                    server.Maxplayers = players;

                    foreach (ServerTable row in ServerGroups_GetAllServerRows())
                    {
                        if (row != null && server.ID == row.ID)
                        {
                            row.Maxplayers = players;
                            break;
                        }
                    }
                }
                else
                {
                    metadata.ConsecutiveQueryFailures++;
                    metadata.LastQueryFailureMessage = queryError;
                }

                RefreshServerHealth(server);

                await Task.Delay(5000);
            }
        }

        private static bool IsExpectedServerQueryException(Exception exception)
        {
            return exception is IOException ||
                exception is System.Net.WebException ||
                exception is System.Net.Sockets.SocketException ||
                exception is TimeoutException ||
                exception is OperationCanceledException ||
                exception is InvalidOperationException ||
                exception is ArgumentException ||
                exception is System.Security.SecurityException;
        }

        private bool IsCurrentServerProcess(string serverId, Process process)
        {
            ServerMetadata metadata = GetServerMetadata(serverId);
            if (metadata == null || process == null || !ReferenceEquals(metadata.Process, process)) { return false; }

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private async Task EndAllRunningProcess(string serverId)
        {
            await Task.Run(() =>
            {
                //LINQ query for windowsgsm old processes
                var processes = (from p in Process.GetProcesses()
                                 where ((Predicate<Process>)(p_ =>
                                 {
                                     try
                                     {
                                         return p_.MainModule.FileName.Contains(Path.Combine(WGSM_PATH, "servers", serverId) + "\\");

                                     }
                                     catch
                                     {
                                         return false;
                                     }
                                 }))(p)
                                 select p).ToList();

                // Kill all processes
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        //ignore
                    }
                }
            });
        }

        private void SetServerStatus(ServerTable server, string status, string pid = null)
        {
            if (!Enum.TryParse(status, out ServerStatus statusCode) || !Enum.IsDefined(typeof(ServerStatus), statusCode))
            {
                throw new ArgumentException("The server status is invalid.", nameof(status));
            }

            ServerTable selectedServerBeforeUpdate = ServerGrid.SelectedItem as ServerTable;
            ServerStatus previousStatusCode = selectedServerBeforeUpdate != null && selectedServerBeforeUpdate.ID == server.ID
                ? selectedServerBeforeUpdate.StatusCode
                : server.StatusCode;
            server.StatusCode = statusCode;
            server.Status = LocalizationManager.Get("Status" + statusCode);
            if (pid != null)
            {
                server.PID = pid;
            }
            if (statusCode == ServerStatus.Stopped)
            {
                server.PID = string.Empty;
                ResetServerQueryHealth(GetServerMetadata(server.ID));
            }

            if (statusCode != ServerStatus.Started && server.Maxplayers.Contains('/'))
            {
                var serverConfig = new ServerConfig(server.ID);
                server.Maxplayers = serverConfig.ServerMaxPlayer;
            }

            RefreshServerHealth(server);

            int allRowsIndex = _allServerRows.FindIndex(row => row.ID == server.ID);
            if (allRowsIndex >= 0)
            {
                _allServerRows[allRowsIndex] = server;
            }

            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                if (server.ID == ((ServerTable)ServerGrid.Items[i]).ID)
                {
                    int selectedIndex = ServerGrid.SelectedIndex;
                    ServerGrid.Items[i] = server;
                    ServerGrid.SelectedIndex = selectedIndex;
                    ServerGrid.Items.Refresh();
                    break;
                }
            }

            DataGrid_RefreshElements();
            ServerTable selectedServerAfterUpdate = ServerGrid.SelectedItem as ServerTable;
            if (previousStatusCode != statusCode && selectedServerAfterUpdate != null && selectedServerAfterUpdate.ID == server.ID)
            {
                label_ServerStatus.Text = selectedServerAfterUpdate.Status;
                AutomationProperties.SetName(label_ServerStatus, selectedServerAfterUpdate.Status);
                AutomationProperties.SetLiveSetting(label_ServerStatus, AutomationLiveSetting.Polite);
                RaiseLiveRegionChanged(label_ServerStatus);
            }
        }

        public void Log(string serverId, string logText)
        {
            string title = int.TryParse(serverId, out int i) ? $"#{i.ToString()}" : serverId;
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}][{title}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}.log");
            File.AppendAllText(logFile, log);

            AppendLogText(textBox_wgsmlog, log, true);
        }

        public void DiscordBotLog(string logText)
        {
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}-DiscordBot.log");
            File.AppendAllText(logFile, log);

            AppendLogText(textBox_DiscordBotLog, log, true);
        }

        private void AppendLogText(System.Windows.Controls.TextBox textBox, string text, bool trimOldLines = false)
        {
            int lastVisibleLine = textBox.GetLastVisibleLineIndex();
            bool followTail = lastVisibleLine < 0 || lastVisibleLine >= textBox.LineCount - 1;
            textBox.AppendText(text);

            if (trimOldLines)
            {
                string trimmedText = RemovedOldLog(textBox.Text);
                if (!string.Equals(textBox.Text, trimmedText, StringComparison.Ordinal))
                {
                    textBox.Text = trimmedText;
                }
            }

            if (followTail) { textBox.ScrollToEnd(); }
        }

        private string RemovedOldLog(string logText)
        {
            const int MAX_LOG_LINE = 50;
            int lineCount = logText.Count(f => f == '\n');
            return (lineCount > MAX_LOG_LINE) ? string.Join("\n", logText.Split('\n').Skip(lineCount - MAX_LOG_LINE).ToArray()) : logText;
        }

        private void Button_ClearServerConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
            console.Clear();
        }

        private void Button_ClearWGSMLog_Click(object sender, RoutedEventArgs e)
        {
            textBox_wgsmlog.Clear();
        }

        private void SendCommand(ServerTable server, string command)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            Process p = metadata.Process;
            if (p == null || p.HasExited) { return; }
            if (!Functions.ServerConsole.IsStandardInputRedirected(p)
                && !Functions.ServerConsole.HasUsableWindow(p, metadata.MainWindow)) { return; }

            metadata.ServerConsole.Input(p, command, metadata.MainWindow);
        }

        private static bool IsValidIPAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            string[] splitValues = ip.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            return splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
        }

        private static bool IsValidPort(string port)
        {
            if (!int.TryParse(port, out int portnum))
            {
                return false;
            }

            return portnum > 1 && portnum < 65535;
        }

        #region Menu - Browse
        private static void OpenWithShell(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                MessageBox.Show(exception.Message, LocalizationManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Browse_ServerBackups_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            OpenWithShell(Functions.ServerPath.GetBackups(server.ID));
        }

        private void Browse_BackupFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _ = new Functions.BackupConfig(server.ID);
            OpenWithShell(Functions.ServerPath.GetServersConfigs(server.ID, "BackupConfig.cfg"));
        }

        private void Browse_ServerConfigs_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersConfigs(server.ID);
            if (Directory.Exists(path))
            {
                OpenWithShell(path);
            }
        }

        private void Browse_ServerFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersServerFiles(server.ID);
            if (Directory.Exists(path))
            {
                OpenWithShell(path);
            }
        }
        #endregion

        #region Top Bar Button
        private void Button_Website_Click(object sender, RoutedEventArgs e)
        {
            OpenWithShell("https://windowsgsm.com/");
        }

        private void Button_Discord_Click(object sender, RoutedEventArgs e)
        {
            OpenWithShell("https://discord.gg/bGc7t2R");
        }

        private void Button_Settings_Click(object sender, RoutedEventArgs e)
        {
            ToggleMahappFlyout(MahAppFlyout_Settings);
        }

        private void Button_Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(0);
            notifyIcon.Visible = false;
            notifyIcon.Visible = true;
        }
        #endregion

        #region Settings Flyout
        private void HardWareAcceleration_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.HardWareAcceleration, MahAppSwitch_HardWareAcceleration.IsOn.ToString());
            }

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsOn ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
        }

        private void UIAnimation_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.UIAnimation, MahAppSwitch_UIAnimation.IsOn.ToString());
            }

            WindowTransitionsEnabled = MahAppSwitch_UIAnimation.IsOn;
        }

        private void DarkTheme_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.DarkTheme, MahAppSwitch_DarkTheme.IsOn.ToString());
            }

            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem ?? DEFAULT_THEME}");
        }

        private void StartOnLogin_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.StartOnBoot, MahAppSwitch_StartOnBoot.IsOn.ToString());
            }

            SetStartOnBoot(MahAppSwitch_StartOnBoot.IsOn);
        }

        private void RestartOnCrash_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.RestartOnCrash, MahAppSwitch_RestartOnCrash.IsOn.ToString());
            }
        }

        private void SendStatistics_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.SendStatistics, MahAppSwitch_SendStatistics.IsOn.ToString());
            }
        }

        private void SetStartOnBoot(bool enable)
        {
            string taskName = "WindowsGSM";
            string wgsmPath = Process.GetCurrentProcess().MainModule.FileName;

            Process schtasks = new Process
            {
                StartInfo =
                {
                    FileName = "schtasks",
                    Arguments = enable ? $"/create /tn {taskName} /tr \"{wgsmPath}\" /sc onlogon /rl HIGHEST /f" : $"/delete /tn {taskName} /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            schtasks.Start();
        }
        #endregion

        #region Language Settings
        private void ComboBox_Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string language = comboBox_Language.SelectedValue as string;
            if (string.IsNullOrEmpty(language) || language == LocalizationManager.SelectedLanguage)
            {
                return;
            }

            LocalizationManager.SaveLanguage(language);
        }
        #endregion

        #region Theme Settings
        private void ComboBox_Themes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBox_Themes.SelectedItem == null) { return; }

            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.ThemeColor, comboBox_Themes.SelectedItem.ToString());
            }

            //Set theme
            ThemeManager.Current.ChangeTheme(this, $"{(MahAppSwitch_DarkTheme.IsOn ? "Dark" : "Light")}.{comboBox_Themes.SelectedItem}");
        }
        #endregion

        #region Menu - Help
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AssociateAccessibleLabels();

            string latestVersion = await GetLatestVersion();
            SetUpdateAvailableIndicator(latestVersion);
        }

        private void AssociateAccessibleLabels()
        {
            var visited = new HashSet<DependencyObject>();
            AssociateAccessibleLabels(this, visited);

            foreach (Flyout flyout in _flyouts)
            {
                AssociateAccessibleLabels(flyout, visited);
            }
        }

        private static void AssociateAccessibleLabels(DependencyObject element, HashSet<DependencyObject> visited)
        {
            if (element == null || !visited.Add(element)) { return; }

            if (element is Label label && label.Target != null)
            {
                AutomationProperties.SetLabeledBy(label.Target, label);
            }

            foreach (DependencyObject child in GetAccessibleTreeChildren(element))
            {
                AssociateAccessibleLabels(child, visited);
            }
        }

        private static void SetAccessibleLiveText(TextBlock textBlock, string text)
        {
            string statusText = text ?? string.Empty;
            if (string.Equals(textBlock.Text, statusText, StringComparison.Ordinal)) { return; }

            textBlock.Text = statusText;
            if (!string.IsNullOrEmpty(statusText)) { RaiseLiveRegionChanged(textBlock); }
        }

        private static void RaiseLiveRegionChanged(UIElement element)
        {
            if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) { return; }

            AutomationPeer peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void RegisterFlyoutFocusHandlers()
        {
            foreach (Flyout flyout in _flyouts)
            {
                flyout.IsOpenChanged += (sender, e) =>
                {
                    if (flyout.IsOpen)
                    {
                        CaptureFlyoutFocus(flyout);
                    }
                    else
                    {
                        _flyoutFocusAtOpen.Remove(flyout);
                    }
                };

                flyout.OpeningFinished += (sender, e) => FocusFlyoutContent(flyout);
                flyout.ClosingFinished += (sender, e) => RestoreFlyoutFocusIfClosed();
            }
        }

        private void OpenFlyout(Flyout flyout)
        {
            CaptureFlyoutFocus(flyout);
            flyout.IsOpen = true;
        }

        private void CaptureFlyoutFocus(Flyout flyout)
        {
            IInputElement focusedElement = Keyboard.FocusedElement;
            if (!_flyoutFocusAtOpen.ContainsKey(flyout))
            {
                _flyoutFocusAtOpen.Add(flyout, focusedElement);
            }

            if (!_flyoutFocusCaptured)
            {
                _focusBeforeFlyout = focusedElement;
                _flyoutFocusCaptured = true;
            }
        }

        private void FocusFlyoutContent(Flyout flyout)
        {
            if (!flyout.IsOpen
                || !_flyoutFocusAtOpen.TryGetValue(flyout, out IInputElement focusedElementAtOpen)
                || !ReferenceEquals(Keyboard.FocusedElement, focusedElementAtOpen))
            {
                return;
            }

            if (flyout.Content is DependencyObject content)
            {
                System.Windows.Controls.Control focusTarget = FindFirstFocusableControl(content, new HashSet<DependencyObject>());
                focusTarget?.Focus();
            }
        }

        private void RestoreFlyoutFocusIfClosed()
        {
            if (_flyouts.Any(flyout => flyout.IsOpen)) { return; }

            IInputElement focusTarget = _focusBeforeFlyout;
            _focusBeforeFlyout = null;
            _flyoutFocusCaptured = false;
            _flyoutFocusAtOpen.Clear();

            if (focusTarget is UIElement element && element.IsVisible && element.IsEnabled && element.Focusable)
            {
                Keyboard.Focus(element);
            }
            else if (focusTarget is ContentElement contentElement && contentElement.Focusable)
            {
                Keyboard.Focus(contentElement);
            }
        }

        private static System.Windows.Controls.Control FindFirstFocusableControl(DependencyObject element, HashSet<DependencyObject> visited)
        {
            if (element == null || !visited.Add(element)) { return null; }

            if (element is System.Windows.Controls.Control control
                && !(control is Label)
                && control.IsVisible
                && control.IsEnabled
                && control.Focusable
                && control.IsTabStop)
            {
                return control;
            }

            foreach (DependencyObject child in GetAccessibleTreeChildren(element))
            {
                System.Windows.Controls.Control focusTarget = FindFirstFocusableControl(child, visited);
                if (focusTarget != null) { return focusTarget; }
            }

            return null;
        }

        private static IEnumerable<DependencyObject> GetAccessibleTreeChildren(DependencyObject element)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                if (child is DependencyObject dependencyObject)
                {
                    yield return dependencyObject;
                }
            }

            if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
            {
                int childCount = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < childCount; i++)
                {
                    yield return VisualTreeHelper.GetChild(element, i);
                }
            }
        }

        private void SetUpdateAvailableIndicator(string latestVersion)
        {
            bool updateAvailable = IsUpdateAvailable(latestVersion);
            button_UpdateAvailableIndicator.Visibility = updateAvailable ? Visibility.Visible : Visibility.Collapsed;

            if (updateAvailable)
            {
                button_UpdateAvailableIndicator.ToolTip = string.Format(
                    LocalizationManager.Get("UpdateAvailableIndicatorTooltip"), latestVersion);
            }
        }

        private static bool IsUpdateAvailable(string latestVersion)
        {
            if (string.IsNullOrWhiteSpace(latestVersion)) { return false; }

            string normalizedLatestVersion = NormalizeVersion(latestVersion);
            string normalizedCurrentVersion = NormalizeVersion(WGSM_VERSION);
            return !string.Equals(normalizedLatestVersion, normalizedCurrentVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeVersion(string version)
        {
            string normalizedVersion = version.Trim();
            return normalizedVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? normalizedVersion.Substring(1)
                : normalizedVersion;
        }

        private void Help_OnlineDocumentation_Click(object sender, RoutedEventArgs e)
        {
            OpenWithShell("https://docs.windowsgsm.com");
        }

        private void Help_ReportIssue_Click(object sender, RoutedEventArgs e)
        {
            OpenWithShell("https://github.com/conorjh/CdogsWindowsGSM/issues");
        }

        private async void Help_SoftwareUpdates_Click(object sender, RoutedEventArgs e)
        {
            ProgressDialogController controller = await this.ShowProgressAsync(LocalizationManager.Get("CheckingUpdates"), LocalizationManager.Get("PleaseWait"));
            controller.SetIndeterminate();
            string latestVersion = await GetLatestVersion();
            await controller.CloseAsync();
            SetUpdateAvailableIndicator(latestVersion);

            if (string.IsNullOrEmpty(latestVersion))
            {
                await this.ShowMessageAsync(LocalizationManager.Get("SoftwareUpdates"), LocalizationManager.Get("UpdateReleaseUnavailable"));
                return;
            }

            if (!IsUpdateAvailable(latestVersion))
            {
                await this.ShowMessageAsync(LocalizationManager.Get("SoftwareUpdates"), LocalizationManager.Get("SoftwareUpToDate"));
                return;
            }

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("UpdateButton"),
                DefaultButtonFocus = MessageDialogResult.Affirmative
            };

            var result = await this.ShowMessageAsync(LocalizationManager.Get("SoftwareUpdates"), string.Format(LocalizationManager.Get("UpdateAvailable"), latestVersion), MessageDialogStyle.AffirmativeAndNegative, settings);

            if (result.ToString().Equals("Affirmative"))
            {
                string installPath = ServerPath.GetBin();
                Directory.CreateDirectory(installPath);

                string filePath = Path.Combine(installPath, UpdaterFileName);
                controller = await this.ShowProgressAsync(LocalizationManager.Get("DownloadingUpdater"), LocalizationManager.Get("PleaseWait"));
                controller.SetIndeterminate();
                bool updaterDownloaded = await DownloadWindowsGSMUpdater();
                await controller.CloseAsync();

                if (!updaterDownloaded || !File.Exists(filePath))
                {
                    await this.ShowMessageAsync(LocalizationManager.Get("SoftwareUpdates"), string.Format(LocalizationManager.Get("UpdaterDownloadFailed"), UpdaterFileName));
                    return;
                }

                if (File.Exists(filePath))
                {
                    //Kill all the server
                    for (int i = 0; i <= MAX_SERVER; i++)
                    {
                        if (GetServerMetadata(i) == null || GetServerMetadata(i).Process == null)
                        {
                            continue;
                        }

                        if (!GetServerMetadata(i).Process.HasExited)
                        {
                            _serverMetadata[i].Process.Kill();
                        }
                    }

                    // Run the fork's updater.
                    Process updater = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = installPath,
                            FileName = filePath,
                            Arguments = "-autostart -forceupdate"
                        }
                    };
                    updater.Start();

                    Close();
                }
                else
                {
                    await this.ShowMessageAsync(LocalizationManager.Get("SoftwareUpdates"), string.Format(LocalizationManager.Get("UpdaterDownloadError"), UpdaterFileName));
                }
            }
        }

        private async Task<string> GetLatestVersion()
        {
            try
            {
                var webRequest = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
                webRequest.Method = "GET";
                webRequest.UserAgent = "CdogsWindowsGSM";
                webRequest.ServicePoint.Expect100Continue = false;
                webRequest.Timeout = 15000;
                webRequest.ReadWriteTimeout = 15000;

                using (WebResponse response = await webRequest.GetResponseAsync())
                {
                    Stream responseStream = response.GetResponseStream();
                    if (responseStream == null)
                    {
                        throw new InvalidDataException("GitHub returned an empty release response.");
                    }

                    using (responseStream)
                    using (var responseReader = new StreamReader(responseStream))
                    {
                        JObject release = JObject.Parse(responseReader.ReadToEnd());
                        string tag = (string)release["tag_name"];
                        JArray assets = release["assets"] as JArray;
                        bool hasApplication = false;
                        bool hasUpdater = false;

                        if (assets != null)
                        {
                            foreach (JToken asset in assets)
                            {
                                string assetName = (string)asset["name"];
                                hasApplication |= string.Equals(assetName, "CdogsWindowsGSM.exe", StringComparison.OrdinalIgnoreCase);
                                hasUpdater |= string.Equals(assetName, UpdaterFileName, StringComparison.OrdinalIgnoreCase);
                            }
                        }

                        if (string.IsNullOrWhiteSpace(tag) || !hasApplication || !hasUpdater)
                        {
                            throw new InvalidDataException("The latest CdogsWindowsGSM release is missing its version tag or required executable assets.");
                        }

                        return tag;
                    }
                }
            }
            catch (WebException ex)
            {
                Debug.WriteLine($"Failed to get the latest CdogsWindowsGSM release: {ex}");
                return null;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Debug.WriteLine($"Failed to parse the latest CdogsWindowsGSM release: {ex}");
                return null;
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"The latest CdogsWindowsGSM release is not usable: {ex}");
                return null;
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Failed to read the latest CdogsWindowsGSM release: {ex}");
                return null;
            }
        }

        private async Task<bool> DownloadWindowsGSMUpdater()
        {
            string filePath = ServerPath.GetBin(UpdaterFileName);
            string downloadPath = filePath + ".download";

            try
            {
                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.UserAgent] = "CdogsWindowsGSM";
                    await webClient.DownloadFileTaskAsync(UpdaterAssetUrl, downloadPath);
                }

                using (FileStream updaterStream = File.OpenRead(downloadPath))
                {
                    if (updaterStream.Length < 2 || updaterStream.ReadByte() != 0x4D || updaterStream.ReadByte() != 0x5A)
                    {
                        throw new InvalidDataException("The downloaded updater is not a Windows executable.");
                    }
                }

                if (File.Exists(filePath))
                {
                    File.Replace(downloadPath, filePath, null);
                }
                else
                {
                    File.Move(downloadPath, filePath);
                }

                return true;
            }
            catch (WebException ex)
            {
                Debug.WriteLine($"Failed to download {UpdaterFileName}: {ex}");
                DeleteUpdaterDownload(downloadPath);
                return false;
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"Invalid {UpdaterFileName}: {ex}");
                DeleteUpdaterDownload(downloadPath);
                return false;
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Failed to save {UpdaterFileName}: {ex}");
                DeleteUpdaterDownload(downloadPath);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Access denied while saving {UpdaterFileName}: {ex}");
                DeleteUpdaterDownload(downloadPath);
                return false;
            }
        }

        private static void DeleteUpdaterDownload(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Could not remove temporary updater file {path}: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Could not remove temporary updater file {path}: {ex}");
            }
        }

        private async void Help_AboutWindowsGSM_Click(object sender, RoutedEventArgs e)
        {
            await this.ShowMessageAsync(LocalizationManager.Get("AboutTitle"), string.Format(LocalizationManager.Get("AboutDetails"), WGSM_VERSION.Substring(1)));
        }
        #endregion

        #region Menu - Tools
        private void Tools_GlobalServerListCheck_Click(object sender, RoutedEventArgs e)
        {
            var row = (ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            if (row.Game == GameServer.MCPE.FullName || row.Game == GameServer.MC.FullName ||
                row.Game == GameServer.PaperMC.FullName || row.Game == GameServer.ForgeMC.FullName)
            {
                Log(row.ID, string.Format(LocalizationManager.Get("GlobalFeatureNotApplicable"), row.Game));
                return;
            }

            string publicIP = GetPublicIP();
            if (publicIP == null)
            {
                Log(row.ID, LocalizationManager.Get("GlobalCheckPublicIpFailed"));
                return;
            }

            string messageText = string.Join(Environment.NewLine, new[]
            {
                string.Format(LocalizationManager.Get("GlobalServerName"), row.Name),
                string.Format(LocalizationManager.Get("GlobalPublicIp"), publicIP),
                string.Format(LocalizationManager.Get("GlobalQueryPort"), row.QueryPort)
            });
            if (GlobalServerList.IsServerOnSteamServerList(publicIP, row.QueryPort))
            {
                MessageBox.Show(string.Format(LocalizationManager.Get("GlobalServerListOnline"), messageText), LocalizationManager.Get("GlobalServerListCheckTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(string.Format(LocalizationManager.Get("GlobalServerListOffline"), messageText), LocalizationManager.Get("GlobalServerListCheckTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Tool_InstallAMXModXMetamodP_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsAMXModXAndMetaModPExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonAmxTitle"), string.Format(LocalizationManager.Get("AddonUnsupported"), server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonAmxTitle"), string.Format(LocalizationManager.Get("AddonAlreadyInstalled"), server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(LocalizationManager.Get("AddonAmxTitle"), string.Format(LocalizationManager.Get("AddonInstallConfirm"), server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                ProgressDialogController controller = await this.ShowProgressAsync(LocalizationManager.Get("InstallingProgress"), LocalizationManager.Get("PleaseWait"));
                controller.SetIndeterminate();
                bool installed = await InstallAddons.AMXModXAndMetaModP(server);
                await controller.CloseAsync();

                string message = LocalizationManager.Get(installed ? "AddonInstallSuccess" : "AddonInstallFailed");
                await this.ShowMessageAsync(LocalizationManager.Get("AddonAmxTitle"), string.Format(LocalizationManager.Get("AddonResultWithId"), message, server.ID));
            }
        }

        private async void Tools_InstallSourcemodMetamod_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsSourceModAndMetaModExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonSourceTitle"), string.Format(LocalizationManager.Get("AddonUnsupported"), server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonSourceTitle"), string.Format(LocalizationManager.Get("AddonAlreadyInstalled"), server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(LocalizationManager.Get("AddonSourceTitle"), string.Format(LocalizationManager.Get("AddonInstallConfirm"), server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                var controller = await this.ShowProgressAsync(LocalizationManager.Get("InstallingProgress"), LocalizationManager.Get("PleaseWait"));
                controller.SetIndeterminate();
                bool installed = await InstallAddons.SourceModAndMetaMod(server);
                await controller.CloseAsync();

                var message = LocalizationManager.Get(installed ? "AddonInstallSuccess" : "AddonInstallFailed");
                await this.ShowMessageAsync(LocalizationManager.Get("AddonSourceTitle"), string.Format(LocalizationManager.Get("AddonResultWithId"), message, server.ID));
            }
        }

        private async void Tools_InstallDayZSALModServer_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsDayZSALModServerExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonDayzTitle"), string.Format(LocalizationManager.Get("AddonUnsupported"), server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(LocalizationManager.Get("AddonDayzTitle"), string.Format(LocalizationManager.Get("AddonAlreadyInstalled"), server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(LocalizationManager.Get("AddonDayzTitle"), string.Format(LocalizationManager.Get("AddonInstallConfirm"), server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                ProgressDialogController controller = await this.ShowProgressAsync(LocalizationManager.Get("InstallingProgress"), LocalizationManager.Get("PleaseWait"));
                controller.SetIndeterminate();
                bool installed = await InstallAddons.DayZSALModServer(server);
                await controller.CloseAsync();

                string message = LocalizationManager.Get(installed ? "AddonInstallSuccess" : "AddonInstallFailed");
                await this.ShowMessageAsync(LocalizationManager.Get("AddonDayzTitle"), string.Format(LocalizationManager.Get("AddonResultWithId"), message, server.ID));
            }
        }

        private async void Tools_InstallOxideMod_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string messageTitle = LocalizationManager.Get("AddonOxideTitle");

            bool? existed = InstallAddons.IsOxideModExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(messageTitle, string.Format(LocalizationManager.Get("AddonUnsupported"), server.Game, server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(messageTitle, string.Format(LocalizationManager.Get("AddonInstallOrUpdateConfirm"), server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                ProgressDialogController controller = await this.ShowProgressAsync(LocalizationManager.Get("InstallingProgress"), LocalizationManager.Get("PleaseWait"));
                controller.SetIndeterminate();
                bool installed = await InstallAddons.OxideMod(server);
                await controller.CloseAsync();

                string message = LocalizationManager.Get(installed ? "AddonInstallSuccess" : "AddonInstallFailed");
                await this.ShowMessageAsync(messageTitle, string.Format(LocalizationManager.Get("AddonResultWithId"), message, server.ID));
            }
        }
        #endregion

        private string GetPublicIP()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString("https://ipinfo.io/ip").Replace("\n", string.Empty);
                }
            }
            catch
            {
                return null;
            }
        }

        private void OnBalloonTipClick(object sender, EventArgs e)
        {
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Normal;
                Show();
            }
        }

        #region Left Buttom Grid
        private void Slider_ProcessPriority_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].CPUPriority = ((int)slider_ProcessPriority.Value).ToString();
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CPUPriority, GetServerMetadata(server.ID).CPUPriority);
            textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

            if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = Functions.CPU.Priority.SetProcessWithPriority(GetServerMetadata(server.ID).Process, (int)slider_ProcessPriority.Value);
            }
        }

        private void Button_SetAffinity_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ToggleMahappFlyout(MahAppFlyout_SetAffinity);
        }

        private void Button_EditConfig_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (Refresh_EditConfig_Data(server.ID))
            {
                ToggleMahappFlyout(MahAppFlyout_EditConfig);
            }
            else
            {
                MahAppFlyout_EditConfig.IsOpen = false;
            }
        }

        private bool Refresh_EditConfig_Data(string serverId)
        {
            var serverConfig = new ServerConfig(serverId);
            if (string.IsNullOrWhiteSpace(serverConfig.ServerGame)) { return false; }
            var gameServer = GameServer.Data.Class.Get(serverConfig.ServerGame, serverConfig, PluginsList);
            if (gameServer == null) { return false; }
            var goldSourceServer = gameServer as GameServer.Engine.GoldSource;
            var sourceServer = gameServer as GameServer.Engine.Source;
            _editConfigQueryUsesServerPort = QueryUsesServerPort(gameServer);
            bool supportsMapRotation = goldSourceServer != null || (sourceServer != null && !string.IsNullOrWhiteSpace(sourceServer.Game));

            stackPanel_EC_MapRotation.Visibility = supportsMapRotation ? Visibility.Visible : Visibility.Collapsed;
            listBox_EC_AvailableMaps.Items.Clear();
            listBox_EC_MapRotation.Items.Clear();
            SetAccessibleLiveText(textBlock_EC_MapRotationStatus, string.Empty);
            if (supportsMapRotation)
            {
                string mapDirectory = goldSourceServer != null ? goldSourceServer.MapDirectory : sourceServer.MapDirectory;
                List<string> availableMaps = goldSourceServer != null ? goldSourceServer.GetAvailableMaps() : sourceServer.GetAvailableMaps();
                foreach (string map in availableMaps)
                {
                    listBox_EC_AvailableMaps.Items.Add(map);
                }

                var availableMapNames = new HashSet<string>(availableMaps, StringComparer.OrdinalIgnoreCase);
                List<string> savedMapRotation = goldSourceServer != null ? goldSourceServer.GetMapRotation() : sourceServer.GetMapRotation();
                foreach (string map in savedMapRotation.Where(availableMapNames.Contains))
                {
                    if (!listBox_EC_MapRotation.Items.Cast<string>().Contains(map, StringComparer.OrdinalIgnoreCase))
                    {
                        listBox_EC_MapRotation.Items.Add(map);
                    }
                }

                SetAccessibleLiveText(textBlock_EC_MapRotationStatus, availableMaps.Count == 0
                    ? string.Format(LocalizationManager.Get("MapRotationNoBspMapsFound"), mapDirectory)
                    : string.Format(LocalizationManager.Get("MapRotationBspMapsFound"), availableMaps.Count, mapDirectory));
            }

            textbox_EC_ServerID.Text = serverConfig.ServerID;
            textbox_EC_ServerGame.Text = serverConfig.ServerGame;
            textbox_EC_ServerName.Text = serverConfig.ServerName;
            textbox_EC_ServerIP.Text = serverConfig.ServerIP;
            numericUpDown_EC_ServerMaxplayer.Value = int.TryParse(serverConfig.ServerMaxPlayer, out var maxplayer) ? maxplayer : int.Parse(gameServer.Maxplayers);
            numericUpDown_EC_ServerPort.Value = int.TryParse(serverConfig.ServerPort, out var port) ? port : int.Parse(gameServer.Port);
            numericUpDown_EC_ServerQueryPort.IsEnabled = !_editConfigQueryUsesServerPort;
            numericUpDown_EC_ServerQueryPort.ToolTip = _editConfigQueryUsesServerPort
                ? LocalizationManager.Get("QueryPortUsesServerPort")
                : null;
            numericUpDown_EC_ServerQueryPort.Value = _editConfigQueryUsesServerPort
                ? numericUpDown_EC_ServerPort.Value
                : int.TryParse(serverConfig.ServerQueryPort, out var queryPort) ? queryPort : int.Parse(gameServer.QueryPort);
            textbox_EC_ServerMap.Text = serverConfig.ServerMap;
            textbox_EC_ServerGSLT.Text = serverConfig.ServerGSLT;
            textbox_EC_ServerParam.Text = serverConfig.ServerParam;
            bool isCod1 = IsCod1Game(serverConfig.ServerGame);
            stackPanel_EC_Cod1Settings.Visibility = isCod1 ? Visibility.Visible : Visibility.Collapsed;
            stackPanel_EC_CfxSettings.Visibility = IsCfxGame(serverConfig.ServerGame) ? Visibility.Visible : Visibility.Collapsed;
            passwordBox_EC_CfxLicenseKey.Password = string.Empty;
            checkBox_EC_Cod1MeatBots.IsChecked = serverConfig.Cod1MeatBots;
            numericUpDown_EC_Cod1AlliesBots.Value = int.TryParse(serverConfig.Cod1AlliesBots, out var alliesBots) ? alliesBots : 0;
            numericUpDown_EC_Cod1AxisBots.Value = int.TryParse(serverConfig.Cod1AxisBots, out var axisBots) ? axisBots : 0;
            return true;
        }

        private void NumericUpDown_EC_ServerPort_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            if (_editConfigQueryUsesServerPort && e.NewValue.HasValue)
            {
                numericUpDown_EC_ServerQueryPort.Value = e.NewValue.Value;
            }
        }

        private void Button_EditConfig_Save_Click(object sender, RoutedEventArgs e)
        {
            string serverId = textbox_EC_ServerID.Text;
            if (string.IsNullOrWhiteSpace(serverId)) { return; }

            bool isCod1 = IsCod1Game(textbox_EC_ServerGame.Text);
            if (isCod1 && !ServerConfig.ValidateCod1Settings(
                    numericUpDown_EC_Cod1AlliesBots.Value.ToString(),
                    numericUpDown_EC_Cod1AxisBots.Value.ToString(),
                    numericUpDown_EC_ServerMaxplayer.Value.ToString(),
                    out string settingsError))
            {
                MessageBox.Show(settingsError, LocalizationManager.Get("CallOfDutySettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var serverConfig = new ServerConfig(serverId);
            var gameServer = GameServer.Data.Class.Get(serverConfig.ServerGame, serverConfig, PluginsList);
            if (gameServer is GameServer.MCBE)
            {
                try
                {
                    GameServer.MCBE.ParseServerPort(numericUpDown_EC_ServerPort.Value.ToString());
                }
                catch (InvalidDataException exception)
                {
                    MessageBox.Show(exception.Message, LocalizationManager.Get("SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            var goldSourceServer = gameServer as GameServer.Engine.GoldSource;
            var sourceServer = gameServer as GameServer.Engine.Source;
            string mapCyclePath = goldSourceServer != null ? goldSourceServer.MapCyclePath : sourceServer != null && !string.IsNullOrWhiteSpace(sourceServer.Game) ? sourceServer.MapCyclePath : null;
            if (mapCyclePath != null && Directory.Exists(Path.GetDirectoryName(mapCyclePath)))
            {
                try
                {
                    if (goldSourceServer != null)
                    {
                        goldSourceServer.SaveMapRotation(listBox_EC_MapRotation.Items.Cast<string>());
                    }
                    else
                    {
                        sourceServer.SaveMapRotation(listBox_EC_MapRotation.Items.Cast<string>());
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(LocalizationManager.Get("MapRotationSaveError"), ex.Message), LocalizationManager.Get("SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerGame, textbox_EC_ServerGame.Text.Trim());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerName, textbox_EC_ServerName.Text.Trim());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerIP, textbox_EC_ServerIP.Text.Trim());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerMaxPlayer, numericUpDown_EC_ServerMaxplayer.Value.ToString());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerPort, numericUpDown_EC_ServerPort.Value.ToString());
            string queryPort = QueryUsesServerPort(gameServer)
                ? numericUpDown_EC_ServerPort.Value.ToString()
                : numericUpDown_EC_ServerQueryPort.Value.ToString();
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerQueryPort, queryPort);
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerMap, textbox_EC_ServerMap.Text.Trim());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerGSLT, textbox_EC_ServerGSLT.Text.Trim());
            ServerConfig.SetSetting(serverId, ServerConfig.SettingName.ServerParam, textbox_EC_ServerParam.Text.Trim());
            if (isCod1)
            {
                ServerConfig.SetSetting(serverId, ServerConfig.SettingName.Cod1MeatBots, checkBox_EC_Cod1MeatBots.IsChecked == true ? "1" : "0");
                ServerConfig.SetSetting(serverId, ServerConfig.SettingName.Cod1AlliesBots, numericUpDown_EC_Cod1AlliesBots.Value.ToString());
                ServerConfig.SetSetting(serverId, ServerConfig.SettingName.Cod1AxisBots, numericUpDown_EC_Cod1AxisBots.Value.ToString());
            }
            string cfxLicenseKey = passwordBox_EC_CfxLicenseKey.Password.Trim();
            if (IsCfxGame(textbox_EC_ServerGame.Text) && !string.IsNullOrWhiteSpace(cfxLicenseKey))
            {
                ServerConfig.SetSetting(serverId, ServerConfig.SettingName.CfxLicenseKey, cfxLicenseKey);
            }

            if (gameServer is GameServer.MCBE)
            {
                var updatedServerConfig = new ServerConfig(serverId);
                var bedrockServer = new GameServer.MCBE(updatedServerConfig);
                if (!bedrockServer.UpdateServerPropertiesFromWindowsGsmSettings())
                {
                    MessageBox.Show(
                        bedrockServer.Error,
                        LocalizationManager.Get("SaveErrorTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            LoadServerTable();
            MahAppFlyout_EditConfig.IsOpen = false;
        }

        private void ListBox_EC_AvailableMaps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(listBox_EC_AvailableMaps, e.OriginalSource as DependencyObject) == null) { return; }
            Button_EC_AddMap_Click(sender, e);
        }

        private void ListBox_EC_MapRotation_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(listBox_EC_MapRotation, e.OriginalSource as DependencyObject) == null) { return; }
            Button_EC_RemoveMap_Click(sender, e);
        }

        private void Button_EC_AddMap_Click(object sender, RoutedEventArgs e)
        {
            string map = listBox_EC_AvailableMaps.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(map) || listBox_EC_MapRotation.Items.Cast<string>().Contains(map, StringComparer.OrdinalIgnoreCase)) { return; }

            listBox_EC_MapRotation.Items.Add(map);
            listBox_EC_MapRotation.SelectedItem = map;
        }

        private void Button_EC_RemoveMap_Click(object sender, RoutedEventArgs e)
        {
            int index = listBox_EC_MapRotation.SelectedIndex;
            if (index < 0) { return; }

            listBox_EC_MapRotation.Items.RemoveAt(index);
            if (listBox_EC_MapRotation.Items.Count > 0)
            {
                listBox_EC_MapRotation.SelectedIndex = Math.Min(index, listBox_EC_MapRotation.Items.Count - 1);
            }
        }

        private void MoveSelectedMap(int offset)
        {
            int index = listBox_EC_MapRotation.SelectedIndex;
            int newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= listBox_EC_MapRotation.Items.Count) { return; }

            object map = listBox_EC_MapRotation.Items[index];
            listBox_EC_MapRotation.Items.RemoveAt(index);
            listBox_EC_MapRotation.Items.Insert(newIndex, map);
            listBox_EC_MapRotation.SelectedIndex = newIndex;
        }

        private void Button_EC_MoveMapUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedMap(-1);
        }

        private void Button_EC_MoveMapDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedMap(1);
        }

        private void Button_EC_RandomiseMapRotation_Click(object sender, RoutedEventArgs e)
        {
            List<string> maps = listBox_EC_MapRotation.Items.Cast<string>().ToList();
            if (maps.Count < 2) { return; }

            for (int i = maps.Count - 1; i > 0; i--)
            {
                int swapIndex = _mapRotationRandom.Next(i + 1);
                string map = maps[i];
                maps[i] = maps[swapIndex];
                maps[swapIndex] = map;
            }

            listBox_EC_MapRotation.Items.Clear();
            foreach (string map in maps)
            {
                listBox_EC_MapRotation.Items.Add(map);
            }
            listBox_EC_MapRotation.SelectedIndex = 0;
        }

        private void Button_RestartCrontab_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontab = switch_restartcrontab.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontab, GetServerMetadata(server.ID).RestartCrontab ? "1" : "0");
        }

        private void Button_EmbedConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].EmbedConsole = switch_embedconsole.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.EmbedConsole, GetServerMetadata(server.ID).EmbedConsole ? "1" : "0");
        }

        private void Button_AutoRestart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestart = switch_autorestart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestart, GetServerMetadata(server.ID).AutoRestart ? "1" : "0");
        }

        private void Button_AutoStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStart = switch_autostart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStart, GetServerMetadata(server.ID).AutoStart ? "1" : "0");
        }

        private void Button_AutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdate = switch_autoupdate.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdate, GetServerMetadata(server.ID).AutoUpdate ? "1" : "0");
        }

        private async void Button_DiscordAlertSettings_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            ToggleMahappFlyout(MahAppFlyout_DiscordAlert);
        }

        private void Button_UpdateOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].UpdateOnStart = switch_updateonstart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.UpdateOnStart, GetServerMetadata(server.ID).UpdateOnStart ? "1" : "0");
        }

        private void Button_BackupOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].BackupOnStart = switch_backuponstart.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupOnStart, GetServerMetadata(server.ID).BackupOnStart ? "1" : "0");
        }

        private void Button_DiscordAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].DiscordAlert = switch_discordalert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordAlert, GetServerMetadata(server.ID).DiscordAlert ? "1" : "0");
            button_discordtest.IsEnabled = GetServerMetadata(server.ID).DiscordAlert;
        }

        private async void Button_CrontabEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string crontabFormat = ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.CrontabFormat);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("SaveButton"),
                DefaultText = crontabFormat
            };

            crontabFormat = await this.ShowInputAsync(LocalizationManager.Get("CrontabFormatTitle"), LocalizationManager.Get("CrontabFormatPrompt"), settings);
            if (crontabFormat == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].CrontabFormat = crontabFormat;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrontabFormat, crontabFormat);

            textBox_restartcrontab.Text = crontabFormat;
            textBox_nextcrontab.Text = CrontabSchedule.TryParse(crontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss") ?? string.Empty;
        }
        #endregion

        #region Switches
        private void Switch_AutoStartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStartAlert = MahAppSwitch_AutoStartAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStartAlert, GetServerMetadata(server.ID).AutoStartAlert ? "1" : "0");
        }

        private void Switch_AutoRestartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestartAlert = MahAppSwitch_AutoRestartAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestartAlert, GetServerMetadata(server.ID).AutoRestartAlert ? "1" : "0");
        }

        private void Switch_AutoUpdateAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdateAlert = MahAppSwitch_AutoUpdateAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdateAlert, GetServerMetadata(server.ID).AutoUpdateAlert ? "1" : "0");
        }

        private void Switch_RestartCrontabAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontabAlert = MahAppSwitch_RestartCrontabAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontabAlert, GetServerMetadata(server.ID).RestartCrontabAlert ? "1" : "0");
        }

        private void Switch_CrashAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].CrashAlert = MahAppSwitch_CrashAlert.IsOn;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrashAlert, GetServerMetadata(server.ID).CrashAlert ? "1" : "0");
        }
        #endregion

        private async void Window_Activated(object sender, EventArgs e)
        {
            if (MahAppFlyout_ManageAddons.IsOpen)
            {
                ListBox_ManageAddons_Refresh();
            }

            // Fix the windows cannot toggle issue because of LoadServerTable
            await Task.Delay(1);

            if (ShowActivated)
            {
                LoadServerTable();
            }
        }

        #region Discord Bot
        private async void Switch_DiscordBot_Toggled(object sender, RoutedEventArgs e)
        {
            if (!switch_DiscordBot.IsEnabled) { return; }

            if (switch_DiscordBot.IsOn)
            {
                switch_DiscordBot.IsEnabled = false;
                button_DiscordBotInvite.IsEnabled = switch_DiscordBot.IsOn = await g_DiscordBot.Start();
                DiscordBotLog("Discord Bot " + (switch_DiscordBot.IsOn ? "started." : "fail to start. Reason: Bot Token is invalid."));
                switch_DiscordBot.IsEnabled = true;
            }
            else
            {
                button_DiscordBotInvite.IsEnabled = switch_DiscordBot.IsEnabled = false;
                await g_DiscordBot.Stop();
                DiscordBotLog("Discord Bot stopped.");
                switch_DiscordBot.IsEnabled = true;
            }
        }

        private void Button_DiscordBotPrefixEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotPrefixEdit.Content.ToString() == LocalizationManager.Get("Edit"))
            {
                button_DiscordBotPrefixEdit.Content = LocalizationManager.Get("Save");
                textBox_DiscordBotPrefix.IsEnabled = true;
                textBox_DiscordBotPrefix.Focus();
                textBox_DiscordBotPrefix.SelectAll();
            }
            else
            {
                button_DiscordBotPrefixEdit.Content = LocalizationManager.Get("Edit");
                textBox_DiscordBotPrefix.IsEnabled = false;
                DiscordBot.Configs.SetBotPrefix(textBox_DiscordBotPrefix.Text);
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
            }
        }

        private void Button_DiscordBotTokenEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotTokenEdit.Content.ToString() == LocalizationManager.Get("Edit"))
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Hidden;
                button_DiscordBotTokenEdit.Content = LocalizationManager.Get("Save");
                textBox_DiscordBotToken.IsEnabled = true;
                textBox_DiscordBotToken.Focus();
                textBox_DiscordBotToken.SelectAll();
            }
            else
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Visible;
                button_DiscordBotTokenEdit.Content = LocalizationManager.Get("Edit");
                textBox_DiscordBotToken.IsEnabled = false;
                DiscordBot.Configs.SetBotToken(textBox_DiscordBotToken.Text);
            }
        }

        /*
        private void Button_DiscordBotDashboardEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotDashboardEdit.Content.ToString() == "Edit")
            {
                button_DiscordBotDashboardEdit.Content = "Save";
                textBox_DiscordBotDashboard.IsEnabled = true;
                textBox_DiscordBotDashboard.Focus();
                textBox_DiscordBotDashboard.SelectAll();
            }
            else
            {
                button_DiscordBotDashboardEdit.Content = "Edit";
                textBox_DiscordBotDashboard.IsEnabled = false;
                DiscordBot.Configs.SetDashboardChannel(textBox_DiscordBotDashboard.Text);
            }
        }


        private void NumericUpDown_DiscordRefreshRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            double rate = numericUpDown_DiscordRefreshRate.Value ?? 5;
            DiscordBot.Configs.SetDashboardRefreshRate((int)rate);
        }
        */

        private async void Button_DiscordBotAddID_Click(object sender, RoutedEventArgs e)
        {
            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("AddButton")
            };

            string newAdminID = await this.ShowInputAsync(LocalizationManager.Get("AddAdminId"), LocalizationManager.Get("DiscordUserIdPrompt"), settings);
            if (newAdminID == null) { return; } //If pressed cancel

            var adminList = DiscordBot.Configs.GetBotAdminList();
            adminList.Add((newAdminID, "0"));
            DiscordBot.Configs.SetBotAdminList(adminList);
            Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);
        }

        private async void Button_DiscordBotEditServerID_Click(object sender, RoutedEventArgs e)
        {
            var adminListItem = (DiscordBot.AdminListItem)listBox_DiscordBotAdminList.SelectedItem;
            if (adminListItem == null) { return; }

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = LocalizationManager.Get("SaveButton"),
                DefaultText = adminListItem.ServerIds
            };

            string example = LocalizationManager.Get("ServerIdsExample");
            string title = string.Format(LocalizationManager.Get("EditServerIdsTitle"), adminListItem.AdminId);
            string prompt = string.Format(LocalizationManager.Get("EditServerIdsPrompt"), example);
            string newServerIds = await this.ShowInputAsync(title, prompt, settings);
            if (newServerIds == null) { return; } //If pressed cancel

            var adminList = DiscordBot.Configs.GetBotAdminList();
            for (int i = 0; i < adminList.Count; i++)
            {
                if (adminList[i].Item1 == adminListItem.AdminId)
                {
                    adminList.RemoveAt(i);
                    adminList.Insert(i, (adminListItem.AdminId, newServerIds.Trim()));
                    break;
                }
            }
            DiscordBot.Configs.SetBotAdminList(adminList);
            Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);
        }

        private void Button_DiscordBotRemoveID_Click(object sender, RoutedEventArgs e)
        {
            if (listBox_DiscordBotAdminList.SelectedIndex >= 0)
            {
                var adminList = DiscordBot.Configs.GetBotAdminList();
                try
                {
                    adminList.RemoveAt(listBox_DiscordBotAdminList.SelectedIndex);
                }
                catch
                {
                    Console.WriteLine($"Fail to delete item {listBox_DiscordBotAdminList.SelectedIndex} in adminIDs.txt");
                }
                DiscordBot.Configs.SetBotAdminList(adminList);

                listBox_DiscordBotAdminList.Items.Remove(listBox_DiscordBotAdminList.Items[listBox_DiscordBotAdminList.SelectedIndex]);
            }
        }

        public void Refresh_DiscordBotAdminList(int selectIndex = 0)
        {
            listBox_DiscordBotAdminList.Items.Clear();
            foreach (var (adminID, serverIDs) in DiscordBot.Configs.GetBotAdminList())
            {
                listBox_DiscordBotAdminList.Items.Add(new DiscordBot.AdminListItem { AdminId = adminID, ServerIds = serverIDs });
            }
            listBox_DiscordBotAdminList.SelectedIndex = listBox_DiscordBotAdminList.Items.Count >= 0 ? selectIndex : -1;
        }

        public int GetServerCount()
        {
            return ServerGroups_GetAllServerRows().Count;
        }

        public List<(string, string, string)> GetServerList()
        {
            var list = new List<(string, string, string)>();

            foreach (ServerTable server in ServerGroups_GetAllServerRows())
            {
                list.Add((server.ID, server.Status, server.Name));
            }

            return list;
        }

        public bool IsServerExist(string serverId)
        {
            foreach (ServerTable server in ServerGroups_GetAllServerRows())
            {
                if (server.ID == serverId) { return true; }
            }

            return false;
        }

        public ServerStatus GetServerStatus(string serverId)
        {
            return GetServerMetadata(serverId).ServerStatus;
        }

        public string GetServerName(string serverId)
        {
            var server = GetServerTableById(serverId);
            return server?.Name ?? string.Empty;
        }

        private ServerTable GetServerTableById(string serverId)
        {
            foreach (ServerTable server in ServerGroups_GetAllServerRows())
            {
                if (server.ID == serverId) { return server; }
            }

            return null;
        }

        public async Task<bool> StartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive START action | {adminName} ({adminID})");
            await GameServer_Start(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> StopServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive STOP action | {adminName} ({adminID})");
            await GameServer_Stop(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> RestartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive RESTART action | {adminName} ({adminID})");
            await GameServer_Restart(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> SendCommandById(string serverId, string command, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive SEND action | {adminName} ({adminID}) | {command}");
            SendCommand(server, command);
            return true;
        }

        public async Task<bool> BackupServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive BACKUP action | {adminName} ({adminID})");
            await GameServer_Backup(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> UpdateServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive UPDATE action | {adminName} ({adminID})");
            await GameServer_Update(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        private void Switch_DiscordBotAutoStart_Click(object sender, RoutedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue("DiscordBotAutoStart", MahAppSwitch_DiscordBotAutoStart.IsOn.ToString());
            }
        }

        private void Button_DiscordBotInvite_Click(object sender, RoutedEventArgs e)
        {
            string inviteLink = g_DiscordBot.GetInviteLink();
            if (!string.IsNullOrWhiteSpace(inviteLink))
            {
                OpenWithShell(g_DiscordBot.GetInviteLink());
            }
        }
        #endregion

        /// <summary>Hide others Flyout and toggle the flyout</summary>
        /// <param name="flyout"></param>
        private void ToggleMahappFlyout(Flyout flyout)
        {
            if (!flyout.IsOpen)
            {
                CaptureFlyoutFocus(flyout);
            }

            MahAppFlyout_DiscordAlert.IsOpen = flyout == MahAppFlyout_DiscordAlert && !MahAppFlyout_DiscordAlert.IsOpen;
            MahAppFlyout_EditConfig.IsOpen = flyout == MahAppFlyout_EditConfig && !MahAppFlyout_EditConfig.IsOpen;
            MahAppFlyout_ImportGameServer.IsOpen = flyout == MahAppFlyout_ImportGameServer && !MahAppFlyout_ImportGameServer.IsOpen;
            MahAppFlyout_InstallGameServer.IsOpen = flyout == MahAppFlyout_InstallGameServer && !MahAppFlyout_InstallGameServer.IsOpen;
            MahAppFlyout_ManageAddons.IsOpen = flyout == MahAppFlyout_ManageAddons && !MahAppFlyout_ManageAddons.IsOpen;
            MahAppFlyout_RestoreBackup.IsOpen = flyout == MahAppFlyout_RestoreBackup && !MahAppFlyout_RestoreBackup.IsOpen;
            MahAppFlyout_SetAffinity.IsOpen = flyout == MahAppFlyout_SetAffinity && !MahAppFlyout_SetAffinity.IsOpen;
            MahAppFlyout_Settings.IsOpen = flyout == MahAppFlyout_Settings && !MahAppFlyout_Settings.IsOpen;
            MahAppFlyout_ServerGroups.IsOpen = flyout == MahAppFlyout_ServerGroups && !MahAppFlyout_ServerGroups.IsOpen;
            MahAppFlyout_ViewPlugins.IsOpen = flyout == MahAppFlyout_ViewPlugins && !MahAppFlyout_ViewPlugins.IsOpen;
        }

        private void HamburgerMenu_ItemClick(object sender, ItemClickEventArgs e)
        {
            HamburgerMenuControl.IsPaneOpen = false;

            hMenu_Home.Visibility = (HamburgerMenuControl.SelectedIndex == 0) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Dashboard.Visibility = (HamburgerMenuControl.SelectedIndex == 1) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Schedules.Visibility = (HamburgerMenuControl.SelectedIndex == 2) ? Visibility.Visible : Visibility.Hidden;
            hMenu_SharedInstallations.Visibility = (HamburgerMenuControl.SelectedIndex == 3) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Discordbot.Visibility = (HamburgerMenuControl.SelectedIndex == 4) ? Visibility.Visible : Visibility.Hidden;

            if (HamburgerMenuControl.SelectedIndex == 2)
            {
                RefreshBackupSchedules();
            }
            else if (HamburgerMenuControl.SelectedIndex == 3)
            {
                RefreshSharedInstallations();
            }
            else if (HamburgerMenuControl.SelectedIndex == 4)
            {
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
                button_DiscordBotPrefixEdit.Content = LocalizationManager.Get("Edit");
                textBox_DiscordBotPrefix.IsEnabled = false;
                textBox_DiscordBotPrefix.Text = DiscordBot.Configs.GetBotPrefix();

                button_DiscordBotTokenEdit.Content = LocalizationManager.Get("Edit");
                textBox_DiscordBotToken.IsEnabled = false;
                textBox_DiscordBotToken.Text = DiscordBot.Configs.GetBotToken();
                //textBox_DiscordBotDashboard.Text = DiscordBot.Configs.GetDashboardChannel();
                //numericUpDown_DiscordRefreshRate.Value = DiscordBot.Configs.GetDashboardRefreshRate();

                Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);

                if (listBox_DiscordBotAdminList.Items.Count > 0 && listBox_DiscordBotAdminList.SelectedItem == null)
                {
                    listBox_DiscordBotAdminList.SelectedItem = listBox_DiscordBotAdminList.Items[0];
                }
            }
        }

        private void Button_RefreshSharedInstallations_Click(object sender, RoutedEventArgs e)
        {
            RefreshSharedInstallations();
        }

        private void RefreshSharedInstallations()
        {
            try
            {
                Dictionary<string, ServerTable> serversById = ServerGroups_GetAllServerRows()
                    .ToDictionary(server => server.ID, StringComparer.OrdinalIgnoreCase);

                var rows = SharedInstallationStore.GetInstallations()
                    .Select(installation =>
                    {
                        List<string> serverNames = SharedInstallationStore.GetLinkedServerIds(installation.Id)
                            .Select(serverId =>
                            {
                                if (!serversById.TryGetValue(serverId, out ServerTable server))
                                {
                                    return serverId;
                                }

                                string serverName = string.IsNullOrWhiteSpace(server.Name) ? serverId : server.Name;
                                return serverName + " (" + serverId + ")";
                            })
                            .ToList();
                        return new
                        {
                            installation.Id,
                            Game = installation.GameKey,
                            AppId = installation.AppId,
                            State = installation.State,
                            Build = installation.BuildId,
                            Created = installation.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                            LinkedServers = string.Join(", ", serverNames)
                        };
                    })
                    .ToList();

                dataGrid_SharedInstallations.ItemsSource = rows;
                textBlock_SharedInstallationsStatus.Text = rows.Count == 0
                    ? LocalizationManager.Get("SharedInstallationsEmpty")
                    : string.Empty;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ArgumentException)
            {
                dataGrid_SharedInstallations.ItemsSource = null;
                textBlock_SharedInstallationsStatus.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Get("SharedInstallationsLoadError"),
                    exception.Message);
            }
        }

        private async void HamburgerMenu_OptionsItemClick(object sender, ItemClickEventArgs e)
        {
            if (HamburgerMenuControl.SelectedOptionsIndex == 0)
            {
                ToggleMahappFlyout(MahAppFlyout_ViewPlugins);
            }
            else if (HamburgerMenuControl.SelectedOptionsIndex == 1)
            {
                ToggleMahappFlyout(MahAppFlyout_Settings);
            }

            HamburgerMenuControl.SelectedOptionsIndex = -1;

            await Task.Delay(1); // Delay 0.001 sec due to UI not sync
            if (hMenu_Home.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 0;
            }
            else if (hMenu_Dashboard.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 1;
            }
            else if (hMenu_Schedules.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 2;
            }
            else if (hMenu_SharedInstallations.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 3;
            }
            else if (hMenu_Discordbot.Visibility == Visibility.Visible)
            {
                HamburgerMenuControl.SelectedIndex = 4;
            }
        }

        private async void HamburgerMenu_Loaded(object sender, RoutedEventArgs e)
        {
            HamburgerMenuControl.Visibility = Visibility.Visible;
            hMenu_Home.Visibility = Visibility.Visible;
            hMenu_Dashboard.Visibility = Visibility.Hidden;
            hMenu_Schedules.Visibility = Visibility.Hidden;
            hMenu_SharedInstallations.Visibility = Visibility.Hidden;
            hMenu_Discordbot.Visibility = Visibility.Hidden;

            await Task.Delay(1); // Delay 0.001 sec due to a bug
            HamburgerMenuControl.SelectedIndex = 0;
        }

        private void Button_AutoScroll_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ServerMetadata metadata = GetServerMetadata(server.ID);
            metadata.AutoScroll = !metadata.AutoScroll;
            SetAutoScrollButtonStatus(metadata.AutoScroll);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoScroll, metadata.AutoScroll ? "1" : "0");
        }

        private void SetAutoScrollButtonStatus(bool isEnabled)
        {
            Button_AutoScroll.Content = LocalizationManager.Get(isEnabled ? "AutoScrollEnabled" : "AutoScrollDisabled");
            AutomationProperties.SetItemStatus(Button_AutoScroll, LocalizationManager.Get(isEnabled ? "ScheduleEnabled" : "ScheduleDisabled"));
        }
    }
}
