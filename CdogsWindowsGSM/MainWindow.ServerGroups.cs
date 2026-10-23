using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MahApps.Metro.Controls.Dialogs;
using WindowsGSM.Functions;
using WindowsGSM.Localization;

namespace WindowsGSM
{
    public partial class MainWindow
    {
        private const string AllServerGroupsFilterId = "*";
        private const string UngroupedServerGroupsFilterId = "";
        private const string NoServerGroupTargetId = "none";
        private readonly List<ServerTable> _allServerRows = new List<ServerTable>();
        private ServerGroupStore _serverGroupStore;
        private string _serverGroupFilterId = AllServerGroupsFilterId;
        private string _serverGroupTargetId;
        private bool _serverGroupControlsLoaded;
        private bool _serverGroupActionRunning;
        private bool _serverGroupFilterSelectionSuppressed;
        private bool _serverGroupTargetSelectionSuppressed;

        private void ServerGroups_BeginServerReload()
        {
            ServerGrid.Items.Filter = null;
        }

        private void ServerGroups_EndServerReload()
        {
            _allServerRows.Clear();
            _allServerRows.AddRange(ServerGrid.Items.Cast<ServerTable>());
            ServerGroups_ApplySelectedFilter();
        }

        internal List<ServerTable> ServerGroups_GetAllServerRows()
        {
            return _allServerRows.ToList();
        }

        private void ServerGroups_Controls_Loaded(object sender, RoutedEventArgs e)
        {
            if (_serverGroupControlsLoaded) { return; }
            _serverGroupControlsLoaded = true;
            ServerGrid.SelectionChanged += ServerGroups_SelectionChanged;

            try
            {
                _serverGroupStore = ServerGroupStore.Load(Path.Combine(WGSM_PATH, "ServerGroups.json"));
                textBlock_ServerGroupStatus.Text = string.Empty;
                ServerGroups_RefreshFilterOptions(AllServerGroupsFilterId);
                ServerGroups_RefreshTargetOptions(null);
                ServerGroups_ApplySelectedFilter();
            }
            catch (Exception exception) when (IsExpectedServerGroupStorageException(exception))
            {
                _serverGroupStore = null;
                _serverGroupFilterId = AllServerGroupsFilterId;
                _serverGroupTargetId = null;
                ServerGrid.Items.Filter = null;
                comboBox_ServerGroupTarget.ItemsSource = null;
                SetAccessibleLiveText(textBlock_ServerGroupStatus, string.Format(LocalizationManager.Get("ServerGroupLoadFailed"), exception.Message));
                ServerGroups_UpdateControls();
            }
        }

        private void ServerGroups_Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_serverGroupFilterSelectionSuppressed) { return; }

            _serverGroupFilterId = comboBox_ServerGroupFilter.SelectedValue as string ?? AllServerGroupsFilterId;
            ServerGroups_ApplySelectedFilter();
        }

        private void ServerGroups_Manage_Click(object sender, RoutedEventArgs e)
        {
            ToggleMahappFlyout(MahAppFlyout_ServerGroups);
        }

        private void ServerGroups_Target_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_serverGroupTargetSelectionSuppressed) { return; }

            string selectedGroupId = comboBox_ServerGroupTarget.SelectedValue as string;
            _serverGroupTargetId = selectedGroupId == NoServerGroupTargetId ? null : selectedGroupId;
            ServerGroups_UpdateControls();
        }

        private void ServerGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServerGroups_UpdateControls();
        }

        private void ServerGroups_RefreshTargetOptions(string selectedGroupId)
        {
            var options = new List<ServerGroupFilterOption>
            {
                new ServerGroupFilterOption(NoServerGroupTargetId, LocalizationManager.Get("ServerGroupTargetSelect"))
            };
            options.AddRange(_serverGroupStore.Groups
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ServerGroupFilterOption(group.Id, group.Name)));

            _serverGroupTargetSelectionSuppressed = true;
            comboBox_ServerGroupTarget.ItemsSource = options;
            comboBox_ServerGroupTarget.SelectedValue = options.Any(option => option.Id == selectedGroupId)
                ? selectedGroupId
                : NoServerGroupTargetId;
            string selectedValue = comboBox_ServerGroupTarget.SelectedValue as string;
            _serverGroupTargetId = selectedValue == NoServerGroupTargetId ? null : selectedValue;
            _serverGroupTargetSelectionSuppressed = false;
            ServerGroups_UpdateControls();
        }

        private void ServerGroups_RefreshFilterOptions(string selectedGroupId)
        {
            var options = new List<ServerGroupFilterOption>
            {
                new ServerGroupFilterOption(AllServerGroupsFilterId, LocalizationManager.Get("ServerGroupAll")),
                new ServerGroupFilterOption(UngroupedServerGroupsFilterId, LocalizationManager.Get("ServerGroupUngrouped"))
            };
            options.AddRange(_serverGroupStore.Groups
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ServerGroupFilterOption(group.Id, group.Name)));

            _serverGroupFilterSelectionSuppressed = true;
            comboBox_ServerGroupFilter.ItemsSource = options;
            comboBox_ServerGroupFilter.SelectedValue = options.Any(option => option.Id == selectedGroupId)
                ? selectedGroupId
                : AllServerGroupsFilterId;
            _serverGroupFilterId = comboBox_ServerGroupFilter.SelectedValue as string ?? AllServerGroupsFilterId;
            _serverGroupFilterSelectionSuppressed = false;
            ServerGroups_UpdateControls();
        }

        private void ServerGroups_ApplySelectedFilter()
        {
            if (_serverGroupStore == null || _serverGroupFilterId == AllServerGroupsFilterId)
            {
                ServerGrid.Items.Filter = null;
            }
            else
            {
                ServerGrid.Items.Filter = item => item is ServerTable server && ServerGroups_IsVisible(server);
            }

            ServerGrid.Items.Refresh();
            grid_action.Visibility = ServerGrid.Items.Count == 0 ? Visibility.Hidden : Visibility.Visible;
            label_select.Visibility = grid_action.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
            ServerGroups_UpdateControls();
        }

        private bool ServerGroups_IsVisible(ServerTable server)
        {
            if (_serverGroupFilterId == UngroupedServerGroupsFilterId)
            {
                return !_serverGroupStore.Groups.Any(group => group.ServerIds.Contains(server.ID, StringComparer.OrdinalIgnoreCase));
            }

            ServerGroup group = _serverGroupStore.Groups.FirstOrDefault(item => item.Id == _serverGroupFilterId);
            return group != null && group.ServerIds.Contains(server.ID, StringComparer.OrdinalIgnoreCase);
        }

        private ServerGroup ServerGroups_GetSelectedGroup()
        {
            return _serverGroupStore?.Groups.FirstOrDefault(group => group.Id == _serverGroupTargetId);
        }

        private void ServerGroups_UpdateControls()
        {
            bool enabled = _serverGroupStore != null && !_serverGroupActionRunning;
            ServerGroup selectedGroup = ServerGroups_GetSelectedGroup();
            ServerTable selectedServer = ServerGrid.SelectedItem as ServerTable;
            bool assigned = selectedGroup != null && selectedServer != null &&
                selectedGroup.ServerIds.Contains(selectedServer.ID, StringComparer.OrdinalIgnoreCase);

            button_ServerGroupManage.IsEnabled = !_serverGroupActionRunning;
            comboBox_ServerGroupFilter.IsEnabled = enabled;
            comboBox_ServerGroupTarget.IsEnabled = enabled;
            button_ServerGroupCreate.IsEnabled = enabled;
            button_ServerGroupRename.IsEnabled = enabled && selectedGroup != null;
            button_ServerGroupDelete.IsEnabled = enabled && selectedGroup != null;
            button_ServerGroupAssign.IsEnabled = enabled && selectedGroup != null && selectedServer != null && !assigned;
            button_ServerGroupRemove.IsEnabled = enabled && selectedGroup != null && assigned;
            button_ServerGroupStart.IsEnabled = enabled && selectedGroup != null && selectedGroup.ServerIds.Count > 0;
            button_ServerGroupStop.IsEnabled = enabled && selectedGroup != null && selectedGroup.ServerIds.Count > 0;
            button_ServerGroupRestart.IsEnabled = enabled && selectedGroup != null && selectedGroup.ServerIds.Count > 0;
            button_ServerGroupUpdate.IsEnabled = enabled && selectedGroup != null && selectedGroup.ServerIds.Count > 0;
        }

        private async void ServerGroups_Create_Click(object sender, RoutedEventArgs e)
        {
            if (_serverGroupStore == null) { return; }

            string name = await this.ShowInputAsync(
                LocalizationManager.Get("ServerGroupCreateTitle"),
                LocalizationManager.Get("ServerGroupNamePrompt"),
                new MetroDialogSettings { AffirmativeButtonText = LocalizationManager.Get("SaveButton") });
            if (name == null) { return; }

            name = name.Trim();
            if (!ServerGroups_IsValidName(name, null, out string validationMessage))
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, validationMessage);
                return;
            }

            var group = new ServerGroup { Id = Guid.NewGuid().ToString("N"), Name = name };
            _serverGroupStore.Groups.Add(group);
            if (!ServerGroups_TrySave(out string error))
            {
                _serverGroupStore.Groups.Remove(group);
                ServerGroups_ShowSaveError(error);
                return;
            }

            textBlock_ServerGroupStatus.Text = string.Empty;
            ServerGroups_RefreshFilterOptions(_serverGroupFilterId);
            ServerGroups_RefreshTargetOptions(group.Id);
            ServerGroups_ApplySelectedFilter();
        }

        private async void ServerGroups_Rename_Click(object sender, RoutedEventArgs e)
        {
            ServerGroup group = ServerGroups_GetSelectedGroup();
            if (group == null || _serverGroupStore == null) { return; }

            string name = await this.ShowInputAsync(
                LocalizationManager.Get("ServerGroupRenameTitle"),
                LocalizationManager.Get("ServerGroupNamePrompt"),
                new MetroDialogSettings
                {
                    AffirmativeButtonText = LocalizationManager.Get("SaveButton"),
                    DefaultText = group.Name
                });
            if (name == null) { return; }

            name = name.Trim();
            if (!ServerGroups_IsValidName(name, group.Id, out string validationMessage))
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, validationMessage);
                return;
            }

            string previousName = group.Name;
            group.Name = name;
            if (!ServerGroups_TrySave(out string error))
            {
                group.Name = previousName;
                ServerGroups_ShowSaveError(error);
                return;
            }

            textBlock_ServerGroupStatus.Text = string.Empty;
            ServerGroups_RefreshFilterOptions(_serverGroupFilterId);
            ServerGroups_RefreshTargetOptions(group.Id);
            ServerGroups_ApplySelectedFilter();
        }

        private void ServerGroups_Delete_Click(object sender, RoutedEventArgs e)
        {
            ServerGroup group = ServerGroups_GetSelectedGroup();
            if (group == null || _serverGroupStore == null) { return; }

            MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                this,
                string.Format(LocalizationManager.Get("ServerGroupDeleteConfirmation"), group.Name),
                LocalizationManager.Get("ConfirmationTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) { return; }

            int index = _serverGroupStore.Groups.IndexOf(group);
            _serverGroupStore.Groups.RemoveAt(index);
            if (!ServerGroups_TrySave(out string error))
            {
                _serverGroupStore.Groups.Insert(index, group);
                ServerGroups_ShowSaveError(error);
                return;
            }

            textBlock_ServerGroupStatus.Text = string.Empty;
            string filterId = _serverGroupFilterId == group.Id ? AllServerGroupsFilterId : _serverGroupFilterId;
            ServerGroups_RefreshFilterOptions(filterId);
            ServerGroups_RefreshTargetOptions(null);
            ServerGroups_ApplySelectedFilter();
        }

        private void ServerGroups_Assign_Click(object sender, RoutedEventArgs e)
        {
            ServerGroup group = ServerGroups_GetSelectedGroup();
            ServerTable server = ServerGrid.SelectedItem as ServerTable;
            if (group == null)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupSelectRequired"));
                return;
            }
            if (server == null)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupServerSelectRequired"));
                return;
            }
            if (group.ServerIds.Contains(server.ID, StringComparer.OrdinalIgnoreCase)) { return; }

            group.ServerIds.Add(server.ID);
            if (!ServerGroups_TrySave(out string error))
            {
                group.ServerIds.Remove(server.ID);
                ServerGroups_ShowSaveError(error);
                return;
            }

            SetAccessibleLiveText(textBlock_ServerGroupStatus, string.Format(LocalizationManager.Get("ServerGroupAssigned"), group.Name));
            ServerGroups_ApplySelectedFilter();
        }

        private void ServerGroups_Remove_Click(object sender, RoutedEventArgs e)
        {
            ServerGroup group = ServerGroups_GetSelectedGroup();
            ServerTable server = ServerGrid.SelectedItem as ServerTable;
            if (group == null)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupSelectRequired"));
                return;
            }
            if (server == null)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupServerSelectRequired"));
                return;
            }

            int index = group.ServerIds.FindIndex(id => string.Equals(id, server.ID, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { return; }
            group.ServerIds.RemoveAt(index);
            if (!ServerGroups_TrySave(out string error))
            {
                group.ServerIds.Insert(index, server.ID);
                ServerGroups_ShowSaveError(error);
                return;
            }

            SetAccessibleLiveText(textBlock_ServerGroupStatus, string.Format(LocalizationManager.Get("ServerGroupRemoved"), group.Name));
            ServerGroups_ApplySelectedFilter();
        }

        private bool ServerGroups_IsValidName(string name, string currentGroupId, out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
            {
                validationMessage = LocalizationManager.Get("ServerGroupInvalidName");
                return false;
            }
            if (_serverGroupStore.Groups.Any(group => group.Id != currentGroupId &&
                string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                validationMessage = LocalizationManager.Get("ServerGroupNameExists");
                return false;
            }

            validationMessage = null;
            return true;
        }

        private bool ServerGroups_TrySave(out string error)
        {
            try
            {
                _serverGroupStore.Save(Path.Combine(WGSM_PATH, "ServerGroups.json"));
                error = null;
                return true;
            }
            catch (Exception exception) when (IsExpectedServerGroupStorageException(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        private void ServerGroups_ShowSaveError(string error)
        {
            SetAccessibleLiveText(textBlock_ServerGroupStatus, string.Format(LocalizationManager.Get("ServerGroupSaveFailed"), error));
        }

        private static bool IsExpectedServerGroupStorageException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is SecurityException ||
                exception is Newtonsoft.Json.JsonException;
        }

        private async void ServerGroups_Start_Click(object sender, RoutedEventArgs e)
        {
            await ServerGroups_RunActionAsync(ServerGroupAction.Start);
        }

        private async void ServerGroups_Stop_Click(object sender, RoutedEventArgs e)
        {
            await ServerGroups_RunActionAsync(ServerGroupAction.Stop);
        }

        private async void ServerGroups_Restart_Click(object sender, RoutedEventArgs e)
        {
            await ServerGroups_RunActionAsync(ServerGroupAction.Restart);
        }

        private async void ServerGroups_Update_Click(object sender, RoutedEventArgs e)
        {
            await ServerGroups_RunActionAsync(ServerGroupAction.Update);
        }

        private async Task ServerGroups_RunActionAsync(ServerGroupAction action)
        {
            ServerGroup group = ServerGroups_GetSelectedGroup();
            if (group == null)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupSelectRequired"));
                return;
            }

            List<ServerTable> servers = ServerGroups_GetAllServerRows()
                .Where(server => group.ServerIds.Contains(server.ID, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (servers.Count == 0)
            {
                SetAccessibleLiveText(textBlock_ServerGroupStatus, LocalizationManager.Get("ServerGroupNoMembers"));
                return;
            }

            if (action != ServerGroupAction.Start)
            {
                string verbKey = action == ServerGroupAction.Stop
                    ? "ServerGroupActionStopVerb"
                    : action == ServerGroupAction.Restart
                        ? "ServerGroupActionRestartVerb"
                        : "ServerGroupActionUpdateVerb";
                MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                    this,
                    string.Format(LocalizationManager.Get("ServerGroupActionConfirmation"), LocalizationManager.Get(verbKey), group.Name),
                    LocalizationManager.Get("ConfirmationTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes) { return; }
            }

            _serverGroupActionRunning = true;
            textBlock_ServerGroupStatus.Text = string.Empty;
            var results = new List<string>();
            int succeeded = 0;
            int failed = 0;
            int skipped = 0;
            ServerGroups_UpdateControls();

            try
            {
                for (int index = 0; index < servers.Count; index++)
                {
                    ServerTable server = servers[index];
                    textBlock_ServerGroupActionResults.Text = string.Format(
                        LocalizationManager.Get("ServerGroupProgress"),
                        index + 1,
                        servers.Count,
                        server.Name);
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    ServerGroupActionOutcome outcome = await ServerGroups_RunActionForServerAsync(action, server);
                    if (outcome.Succeeded) { succeeded++; }
                    else if (outcome.Skipped) { skipped++; }
                    else { failed++; }

                    results.Add(string.Format(
                        LocalizationManager.Get("ServerGroupActionResult"),
                        server.Name,
                        outcome.Message));
                    textBlock_ServerGroupActionResults.Text = string.Join(Environment.NewLine, results);
                    textBlock_ServerGroupActionResults.ToolTip = textBlock_ServerGroupActionResults.Text;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                SetAccessibleLiveText(
                    textBlock_ServerGroupStatus,
                    string.Format(
                        LocalizationManager.Get("ServerGroupActionComplete"),
                        succeeded,
                        failed,
                        skipped));
            }
            finally
            {
                _serverGroupActionRunning = false;
                ServerGroups_UpdateControls();
            }
        }

        private async Task<ServerGroupActionOutcome> ServerGroups_RunActionForServerAsync(ServerGroupAction action, ServerTable server)
        {
            var metadata = GetServerMetadata(server.ID);
            if (metadata == null)
            {
                return ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
            }

            bool statusAllowsAction = action == ServerGroupAction.Start
                ? metadata.ServerStatus == ServerStatus.Stopped
                : action == ServerGroupAction.Update
                    ? metadata.ServerStatus == ServerStatus.Stopped
                    : metadata.ServerStatus == ServerStatus.Started;
            if (!statusAllowsAction)
            {
                return ServerGroupActionOutcome.AsSkipped(LocalizationManager.Get("ServerGroupResultSkipped"));
            }

            try
            {
                switch (action)
                {
                    case ServerGroupAction.Start:
                        await GameServer_Start(server);
                        return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started
                            ? ServerGroupActionOutcome.Success(LocalizationManager.Get("ServerGroupResultSucceeded"))
                            : ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
                    case ServerGroupAction.Stop:
                        await GameServer_Stop(server);
                        return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped
                            ? ServerGroupActionOutcome.Success(LocalizationManager.Get("ServerGroupResultSucceeded"))
                            : ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
                    case ServerGroupAction.Restart:
                        await GameServer_Restart(server);
                        return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started
                            ? ServerGroupActionOutcome.Success(LocalizationManager.Get("ServerGroupResultSucceeded"))
                            : ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
                    case ServerGroupAction.Update:
                        bool updated = await GameServer_Update(server);
                        return updated
                            ? ServerGroupActionOutcome.Success(LocalizationManager.Get("ServerGroupResultSucceeded"))
                            : ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
                    default:
                        return ServerGroupActionOutcome.Failed(LocalizationManager.Get("ServerGroupResultFailed"));
                }
            }
            catch (Exception exception) when (IsExpectedServerGroupActionException(exception))
            {
                Log(server.ID, $"[ERROR] Group {action.ToString().ToLowerInvariant()} failed: {exception.Message}");
                return ServerGroupActionOutcome.Failed($"{LocalizationManager.Get("ServerGroupResultFailed")}: {exception.Message}");
            }
        }

        private static bool IsExpectedServerGroupActionException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is System.ComponentModel.Win32Exception ||
                exception is SecurityException ||
                exception is System.Reflection.TargetInvocationException ||
                exception is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
        }

        private enum ServerGroupAction
        {
            Start,
            Stop,
            Restart,
            Update
        }

        private sealed class ServerGroupActionOutcome
        {
            public bool Succeeded { get; private set; }
            public bool Skipped { get; private set; }
            public string Message { get; private set; }

            private ServerGroupActionOutcome(bool succeeded, bool skipped, string message)
            {
                Succeeded = succeeded;
                Skipped = skipped;
                Message = message;
            }

            public static ServerGroupActionOutcome Success(string message) => new ServerGroupActionOutcome(true, false, message);
            public static ServerGroupActionOutcome Failed(string message) => new ServerGroupActionOutcome(false, false, message);
            public static ServerGroupActionOutcome AsSkipped(string message) => new ServerGroupActionOutcome(false, true, message);
        }

        private sealed class ServerGroupFilterOption
        {
            public string Id { get; private set; }
            public string Name { get; private set; }

            public ServerGroupFilterOption(string id, string name)
            {
                Id = id;
                Name = name;
            }
        }
    }
}
