# Windows Defender Firewall

WindowsGSM's **Firewall** tab maintains inbound port rules for saved game-server configurations and provides a shortcut to Windows Defender Firewall with Advanced Security (`wf.msc`). The application already requires administrator privileges to manage servers and firewall policy.

## Port source and synchronization

- Automatic firewall-rule creation and updates are **opt-in** and off by default. Enable **Automatically create and update firewall rules** in the Firewall tab to synchronize on startup, server install/import, config edits, and before server start. Opening the tab alone does not install rules.
- **Refresh and synchronize** and **Enable All Rules** are explicit manual actions. **Disable All Rules** turns off all existing WindowsGSM-managed server rules without deleting them. Deleting a server still removes its managed rules.
- Rules use the configured **Server Port** and **Query Port** in each server's `WindowsGSM.cfg`.
- The planner uses the game adapter's transport protocol and keeps TCP and UDP port rules separate when both are needed. The Firewall tab groups those native rules into one managed row per server.
- Runtime-discovered ports are deliberately never used to update firewall rules. If a running game selects a different port, the existing server-health warning remains the signal to investigate; WindowsGSM does not chase that transient port.
- When automatic synchronization is enabled, WindowsGSM synchronizes rules on startup, after server install/import, after saving Edit Config, and before server start. Refresh is always a manual synchronization action.
- Deleting a server removes its managed port rules after the server configuration has actually been deleted. When automatic synchronization is enabled, startup synchronization also removes abandoned rules whose server configuration no longer exists.

Rules are written to the local persistent firewall policy store with the **Any** profile. New rules are enabled by default; existing enabled/disabled state is preserved during port synchronization. The Enable All and Disable All actions change state without removing rules. Rules are still created when Windows Defender Firewall is turned off; Windows does not enforce them until the relevant firewall profile is enabled. The tab reports rule-creation, state-change, or cleanup failures rather than silently treating them as successful.

Windows Firewall represents a port rule with one transport protocol. Therefore, a game requiring both TCP and UDP may have two native rule objects, grouped under the server's single WindowsGSM Firewall entry. The managed rules are named `CdogsWindowsGSM-Server-<id>-TCP` and/or `CdogsWindowsGSM-Server-<id>-UDP`.
