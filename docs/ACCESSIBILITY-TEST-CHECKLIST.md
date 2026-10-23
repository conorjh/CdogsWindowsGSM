# WindowsGSM Accessibility Tester Checklist

Use this checklist to review the accessibility implementation in the running application. It is a manual acceptance aid, not a certification or a claim of WCAG conformance.

**Recommended build:** `2.0.0-preview.7` or the newer build under review.
**Test safely:** Use a disposable/test server and non-production data for start, stop, restart, install, import, backup, restore, and delete workflows. Do not perform destructive tests on production servers.

## Test record

- Tester and date:
- Windows edition/build:
- WindowsGSM build:
- Screen reader and version (Narrator, NVDA, or both):
- Display resolution and scaling/DPI:
- Windows contrast setting:
- WindowsGSM theme and UI language:
- Keyboard layout:
- Test server/game and test data:

Mark an item `[x]` when it passes. For failures, blocked items, or items not applicable, record the reason and an issue ID in the results table at the end.

## 1. Keyboard access and focus

- [ ] Complete the checks below using the keyboard only; do not use the mouse to recover focus.
- [ ] Tab and Shift+Tab move through the main navigation, server list, selected-server controls, logs, and console in a logical visual order.
- [ ] The current focus is always visible, including in light and dark themes.
- [ ] Use arrow keys to navigate the server grid; verify the selected row and its values are apparent.
- [ ] Use the grid headers to sort. Verify sort direction and selected row remain understandable.
- [ ] Open the server row context menu with Shift+F10 or the context-menu key; reach and dismiss an action without executing it.
- [ ] Check that the selected-server controls follow their visual row order, including process priority, affinity, restart schedule, Discord controls, and the toggle rows.
- [ ] Open and operate the CPU-affinity controls with the keyboard; each checkbox has a useful name and checked state.
- [ ] Open each representative flyout from its invoking control. Focus moves into the flyout, stays within it while modal, closes by its expected keyboard command, and returns to the invoker when the last flyout closes.
- [ ] Disabled controls are skipped or reported as disabled, and their state is not communicated by color alone.
- [ ] Change the UI-animation setting. Verify that reduced animation does not introduce focus loss or unwanted scrolling.

## 2. Server identity, status, and health

Use a disposable server for lifecycle checks.

- [ ] A screen reader can identify the server row, game, server name, address, ports, player count, and uptime without relying on an image or tooltip alone.
- [ ] Health is shown as localized text (Healthy, Warning, Unhealthy, or Unknown) rather than only a colored symbol.
- [ ] Read the selected-server health details and the server-grid health cell. Full details are available without hovering.
- [ ] Port state is understandable as Verified, Mismatch, or Not verified; when applicable, configured and detected values can be obtained.
- [ ] Exercise a safe start, stop, and restart. The selected-server status is announced when it changes, once per transition, without repeated announcements from periodic refreshes.
- [ ] Where safely possible, exercise a health-state change on a test server. Verify that the selected server's changed health state is announced once, and unchanged polling samples are not.
- [ ] If a failure is safely reproducible, verify that the user receives an understandable result and can find any relevant detail in the UI or log.
- [ ] Verify that sorting, selection, and server actions continue to work with a mouse after the keyboard checks.

## 3. Install, import, and configuration

Use disposable files and test servers. Avoid creating production installations.

- [ ] Open Install and Import with the keyboard; verify labels, field names, focus entry, and focus return.
- [ ] Submit Install with a blank server name, then with no game selected. Each validation message is visible and announced, and focus moves to the relevant field.
- [ ] Submit Import with a blank server name, no game selected, and an invalid directory. Each validation result is visible, readable in full, announced, and followed by focus at the relevant input.
- [ ] Run a safe import or installation. Progress, completion, and failure results are available as text and announced once; long messages wrap instead of being clipped.
- [ ] Read and select text in the install log while it is updating. New output does not force the view to the bottom after scrolling upward.
- [ ] In Edit Config, verify the labels, accessible names, values, and enabled/disabled states of numeric controls and fields.
- [ ] If map rotation is available for the test game, verify the available-map status is readable and announced when the configuration flyout opens.
- [ ] Verify that editing and saving configuration still work with both keyboard and mouse.

## 4. Backup schedules and server groups

- [ ] Navigate and operate the backup schedule grid and controls using the keyboard.
- [ ] Verify the frequency, enabled state, time, storage, and next-run values are clearly labelled and exposed to the screen reader.
- [ ] Enter an invalid schedule and attempt a save. The validation message is visible and announced.
- [ ] Save a valid test schedule. Verify the success or failure result is announced once and remains available as text.
- [ ] Open Server Groups. Filter servers and navigate its controls; verify names, selected values, disabled states, and result text.
- [ ] On test servers only, exercise a reversible group assignment or action. Verify results are understandable without relying on color and that per-server output does not create excessive announcements.

## 5. Dashboard and live information

- [ ] Read the CPU, memory, and disk cards. Their labels and current values are understandable without relying on the colored bars.
- [ ] Read CPU/GPU temperature values and the last-sample time. Verify unavailable/error text is also understandable if a sensor is unavailable.
- [ ] Read server totals, started-server totals, active/peak players, and the fleet summary.
- [ ] Verify the visible player-by-game text summary provides useful current data without needing to interpret the LiveCharts graph.
- [ ] Inspect the chart with the screen reader. Its name/help text and the text alternative provide equivalent essential player information.
- [ ] Leave the Dashboard open through several refreshes. Periodic metric/chart updates do not repeatedly interrupt the screen reader or move focus.
- [ ] Activate the fleet-summary action with the keyboard and verify it navigates to the relevant server or schedule without losing focus unexpectedly.

## 6. Logs and console

- [ ] Reach the WindowsGSM log, Discord Bot log, install log, server-console output, and server-command input with the keyboard.
- [ ] Read and select text in the read-only logs and console output; verify the associated label/name is meaningful.
- [ ] Scroll upward in a log while new output is added. The view stays where the user is reading; return to the bottom and verify tail-follow resumes.
- [ ] Toggle server-console AutoScroll off and on. Verify the name and state are exposed, and that the chosen setting controls console scrolling.
- [ ] Clear the relevant log or console using its labelled action. Verify the action does not steal focus unexpectedly.
- [ ] Send a safe test command. Verify Enter submits it and keyboard focus remains in the command field; verify command history works if available.
- [ ] Confirm routine log lines and periodic console output are not announced as live-region events.

## 7. Visual access, scaling, and localization

Run the relevant checks in both the light and dark app themes.

- [ ] Enable Windows High Contrast. Verify text, focus indicators, buttons, status text, grids, and flyouts remain readable and usable.
- [ ] Check that server health, port state, enabled/disabled state, and operation results are not conveyed by color or symbol alone.
- [ ] Check the contrast of normal text, large text, focus indicators, and important non-text controls. Use the plan's practical targets where applicable: 4.5:1 for normal text and 3:1 for large text and important non-text indicators.
- [ ] Set Windows display scaling/text size to 200% (or the highest practical setting) and resize the window above its minimum size. No essential text or control is clipped, overlapped, or unreachable.
- [ ] Check a long supported translation, including server names and validation/status messages. Text wraps or scrolls without hiding essential information.
- [ ] Verify focus remains visible and does not depend on a single accent color in each tested theme/contrast mode.

## 8. UI Automation inspection

Use Accessibility Insights for Windows or an equivalent UI Automation inspector on the main window, server grid, Dashboard, backup schedules, and representative flyouts.

- [ ] Run the available automated checks and record each actionable finding; do not treat a passing scan as proof of accessibility.
- [ ] Inspect representative controls for a useful Name, ControlType/role, enabled/selected/toggle state, and value where applicable.
- [ ] Inspect the server health and port cells, operation status regions, chart, and visible player summary.
- [ ] Verify status regions announce meaningful changes only; logs and periodically refreshed Dashboard data are not live-announced.
- [ ] Record third-party control findings separately and note any justified exception or follow-up.

## 9. Regression and sign-off

- [ ] Repeat the priority workflows with a mouse to ensure ordinary operation is preserved.
- [ ] Repeat the core workflows with the keyboard and at least one screen reader.
- [ ] No critical keyboard blocker remains in a priority workflow.
- [ ] Essential identity, status, progress, and results are available without color-only cues or hover-only tooltips.
- [ ] Any remaining failures or blocked tests have an owner, severity, reproduction steps, and follow-up decision.

### Results log

| ID | Workflow/control | Test mode (keyboard/AT/visual/UIA) | Result | Reproduction and expected/actual behavior | Severity/evidence |
|---|---|---|---|---|---|
| | | | | | |
| | | | | | |
| | | | | | |

**Overall result:** Pass / Pass with documented exceptions / Fail / Not completed
**Tester notes and follow-ups:**
