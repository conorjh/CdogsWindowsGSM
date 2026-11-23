using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace WindowsGSM
{
    public partial class StartupSplashWindow : Window
    {
        public StartupSplashWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatus(status));
                return;
            }

            status = status ?? string.Empty;
            if (string.Equals(StartupStatusText.Text, status, StringComparison.Ordinal))
            {
                return;
            }

            StartupStatusText.Text = status;
            AutomationProperties.SetHelpText(StartupStatusText, status);
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                AutomationPeer peer = UIElementAutomationPeer.FromElement(StartupStatusText) ??
                    UIElementAutomationPeer.CreatePeerForElement(StartupStatusText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }

            StartupStatusText.UpdateLayout();
            var dispatcherFrame = new DispatcherFrame();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => dispatcherFrame.Continue = false));
            Dispatcher.PushFrame(dispatcherFrame);
        }
    }
}
