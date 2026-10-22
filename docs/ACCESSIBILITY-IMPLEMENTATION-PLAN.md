# Accessibility Implementation Plan

Status: In progress; the first keyboard and UI Automation slice is implemented on `feature/accessibility-retrofit`.
Investigated: 22 October 2026.
Scope: Accessibility of the WindowsGSM Windows desktop application and its WindowsGSM-owned UI.

## Goal and target

Make the main WindowsGSM workflows usable by people who rely on a keyboard, screen reader, magnification, high contrast, or reduced motion, while preserving the existing WPF/MahApps design and application behavior.

Use Windows UI Automation and native Windows accessibility guidance as the implementation target. Use WCAG 2.2 principles and applicable contrast criteria as a practical checklist, not as a claim that this native desktop application is formally WCAG-certified. Do not redesign the whole app before testing shows a redesign is needed.

Prioritize these user tasks:

1. Navigate to the server list, identify a server, and start, stop, or restart it.
2. Read server health, port verification, logs, and operation results.
3. Install or import a server and edit its configuration.
4. Configure and review backup schedules.
5. Use settings, server groups, Dashboard, plugin, and Discord Bot screens.

## Initial baseline and implications (before the first implementation slice)

Paths below are relative to the repository root.

| Area | Current implementation | Plan implication |
|---|---|---|
| Main window and navigation | `CdogsWindowsGSM\MainWindow.xaml` uses WPF, MahApps controls, flyouts, and a HamburgerMenu. The XAML has no explicit `AutomationProperties` declarations. | Inspect the runtime UI Automation tree; WPF defaults may provide some semantics, but do not assume custom or third-party controls expose correct names, roles, or states. |
| Server list | `ServerGrid` uses a DataGrid. The game cell is an image with a tooltip; health is a colored symbol with details in a tooltip; main-port verification appends a colored check/cross. The displayed values are prepared by `Functions\ServerTable.cs`. | Make each value understandable as text to screen-reader and keyboard users. Tooltips may supplement, but must not be the only source of essential information. |
| CPU affinity | `MainWindow.xaml.cs` creates CPU-affinity checkboxes at runtime and sets `Focusable = false`. | Treat this as an early keyboard-access check; make the controls operable and identifiable without a mouse. |
| Server detail and logs | The selected-server panel contains toggles, actions, health details, log text, and an embedded console. Some content is updated at runtime. | Verify tab order, label associations, focus visibility, readable output, and whether state changes are communicated without excessive announcements. |
| Dashboard | The Dashboard includes text metrics and a LiveCharts player chart, and refreshes data periodically. | Verify chart accessibility and provide a useful text alternative. Announce meaningful changes, not every refresh. |
| Layout and themes | The main window has a 1000×650 minimum size; the lower server-detail area uses fixed positioning and a fixed-width pane. App-wide and user-selectable themes are present. | Test resizing, Windows text scaling/DPI, high contrast, and longer localized text. Change rigid layout only where testing demonstrates clipping or loss of access. |
| Localization | UI strings are stored in `Localization\Strings*.resx`; the app supports multiple UI languages. | Add new accessible names, instructions, and status messages to the existing localization flow and verify every supported language. |

The observations above describe the pre-implementation source baseline; they are not a substitute for testing the running application with assistive technology.

## Implementation status

The first implementation slice now:

- Makes the dynamically created CPU-affinity checkboxes tab-focusable, gives them localized names, and associates their labels.
- Exposes server-game, health, and main-port information through UI Automation; displays health states as localized words instead of only a colored symbol.
- Associates labels with the main server settings, install/import (including the conditional Steam Guard code field), edit-config, restore, backup-schedule, and Discord Bot controls through UI Automation while preserving `Label.Target` keyboard behavior.
- Gives navigation items, the server-group filter, server/schedule grids, progress indicators, server console controls, auto-scroll state, and group-action results accessible names and keyboard access where needed.
- Preserves keyboard focus in the server command field after Enter submits a command and excludes the non-interactive server-status badge from tab order.
- Adds a visible current-player-by-game text summary and accessible name/help text for the Dashboard chart.

The application project build succeeds with these changes. Manual keyboard-only, Narrator/NVDA, Accessibility Insights, high-contrast, scaling, and full workflow checks remain before treating the phases below as complete.

## Implementation principles

- Prefer standard WPF control semantics and existing MahApps support. Add `AutomationProperties` where a visible label or a control's default name is insufficient; add a custom automation peer only when runtime inspection confirms a control cannot expose the needed semantics otherwise.
- Keep visible text and the programmatic accessible name consistent. Use `AutomationProperties.LabeledBy` for UI Automation; `Label.Target` only handles access-key focus.
- Never communicate important server state through color, an icon, or a hover-only tooltip alone.
- Keep accessible strings in the localization resources; do not add English-only labels in code-behind or templates.
- Do not make decorative images, borders, or accent bars extra screen-reader stops.
- Avoid announcing routine polling, every log line, or every chart update. Announcements should be concise and tied to meaningful user-visible state changes.
- Preserve existing actions, safety confirmations, configuration formats, and server lifecycle behavior while making the UI more accessible.

## Delivery phases

### Phase 0 — Baseline audit and acceptance checklist

1. Walk through the five priority tasks above and inventory the screens, dialogs/flyouts, and dynamically created controls involved.
2. Test keyboard-only use, Windows Narrator, and NVDA. Use Accessibility Insights for Windows or an equivalent UI Automation inspector to check the exposed control tree.
3. Test the default and dark themes, Windows high-contrast mode, resizing, 200% text scaling/DPI, and the longest supported translations.
4. Record each issue with its workflow, affected control, impact, reproduction steps, and a proposed acceptance check. Fix blockers before cosmetic issues.

**Exit evidence:** A ranked issue list and a repeatable manual test checklist for the priority tasks.

### Phase 1 — Keyboard access and focus

1. Make every task-relevant control reachable and operable by keyboard, including runtime-generated controls such as CPU-affinity checkboxes.
2. Set a logical tab order and provide a visible focus indicator. Check focus after controls are enabled/disabled and after server selection changes; do not unexpectedly steal focus.
3. Verify keyboard navigation for the HamburgerMenu, DataGrids, menus, flyouts, and dialogs. Provide keyboard access to row actions, including the standard context-menu key or Shift+F10 where applicable.
4. Verify that modal flyouts/dialogs move focus into their content, keep navigation appropriately contained while open, close through expected keyboard commands, and return focus to the invoking control.
5. Add localized access keys for frequent actions where useful. Do not create shortcuts that conflict with standard Windows or control behavior.
6. Check that read-only logs and console output remain reachable, selectable, and usable with a screen reader, without making every output surface an unnecessary tab stop.

**Exit evidence:** A user can complete the priority tasks without a mouse and can always identify the current keyboard focus.

### Phase 2 — Names, states, and server information

1. Inspect form fields throughout settings, install/import, edit-config, restore, schedules, and server-group flows. Associate labels with inputs and provide localized names/help text for controls whose purpose is not clear from visible text.
2. Ensure MahApps toggles, numeric controls, sliders, progress bars, and custom navigation items expose a meaningful name, role, value, and state through UI Automation.
3. Update server-grid presentation so:
   - The game/mod image has an accessible game name.
   - Health is expressed as a state such as Healthy, Warning, Unhealthy, or Unknown, with a way to obtain the relevant details.
   - Port verification is expressed as verified, mismatch, or not verified, including configured/detected values when applicable.
   - Sorting, row selection, and row actions remain understandable when using a screen reader.
4. Expose validation messages, operation progress, and completion/failure outcomes as text. Make essential information available without hovering.
5. Treat decorative images and visual-only symbols as decorative when their meaning is already conveyed by nearby text.

**Exit evidence:** A screen reader can identify actionable controls and understand the selected server's identity, status, and operation result without relying on color or hover.

### Phase 3 — Visual access, resizing, and motion

1. Check text, control, status, and focus-indicator contrast in every supported theme and in Windows high-contrast mode. Where applicable, use at least 4.5:1 contrast for normal text, 3:1 for large text, and 3:1 for important non-text indicators.
2. Ensure status distinctions do not depend on red/green, hue, or symbols alone. Keep focus visible against both light and dark surfaces.
3. Test window resizing, DPI scaling, larger text, and long translated labels. Replace fixed positioning or dimensions only where they cause content to overlap, clip, or become unreachable; use responsive layout and scrolling as appropriate.
4. Verify that full health details, validation messages, and operation results are not hidden behind ellipses or available only through a tooltip.
5. Respect the existing UI-animation setting and avoid motion or automatic scrolling that prevents users from reading or controlling content. Keep automatic log scrolling user-controllable.

**Exit evidence:** Priority workflows remain usable at larger text/DPI settings and in high contrast, with no clipped essential content or color-only status.

### Phase 4 — Dynamic content and Dashboard alternatives

1. Verify the UI Automation values for installation/import progress and server status transitions. Announce meaningful completion, failure, or health-state changes once; do not announce every periodic sample or log append.
2. Inspect LiveCharts with a screen reader. If the chart is not adequately exposed, add a concise localized text summary or an accessible tabular alternative for the current player data.
3. Confirm Dashboard values (for example, counts, temperatures, and sample time) can be read without the chart and are clearly labelled. Preserve the existing visible summary and drill-through actions.
4. Ensure text-based logs and console output can be focused, read, and selected; make auto-scroll behavior predictable and optional.

**Exit evidence:** Users who cannot perceive the chart or live visual updates can still obtain the same essential current status and results.

### Phase 5 — Regression testing and release gate

1. Repeat the priority workflow checklist using keyboard-only interaction, Narrator, and NVDA.
2. Run Accessibility Insights or an equivalent UI Automation review on the main window, server list, Dashboard, schedules, and representative flyouts/dialogs. Address all critical findings and document any justified exceptions.
3. Verify contrast and focus visibility in light/dark themes and Windows high contrast; check at 200% text scaling/DPI and with supported translations.
4. Run the application build and existing tests. Add automated checks for deterministic accessible text/state behavior where practical; retain manual assistive-technology testing for behaviors that cannot be reliably asserted in unit tests.
5. Confirm ordinary mouse use, server actions, settings, and localization still behave as before.

**Release gate:** No known keyboard blocker remains in a priority workflow; assistive technology receives useful names, roles, values, and states; important information is not color/icon/tooltip-only; and essential content remains available under the tested display settings.

## Suggested implementation order

| Order | Work | Why first | Completion evidence |
|---|---|---|---|
| 1 | Baseline audit and keyboard/focus fixes, including CPU affinity and server-row actions | Removes barriers that prevent users from reaching core operations | Keyboard-only completion of start/stop/restart and configuration workflows |
| 2 | Server-grid names and textual health/port states | Server identity and status are central to daily operation and currently rely on icons, color, and tooltips | Narrator/NVDA can read server identity, health, and verification status |
| 3 | Form labels, dialog/flyout focus, and localized accessible names | Makes common setup and configuration flows discoverable and predictable | Screen-reader and keyboard checks pass for install/import, settings, schedules, and edit-config |
| 4 | Contrast, scaling, layout, and motion improvements | Addresses low-vision access without prematurely redesigning unrelated screens | High-contrast, scaling, and localization checks pass |
| 5 | Chart alternatives and dynamic announcements, then remaining secondary screens | Completes non-visual access to live information while controlling announcement noise | Current Dashboard information is available without relying on chart visuals |

## Risks and decisions

- **MahApps and LiveCharts support:** Confirm actual UI Automation exposure at runtime before deciding to add custom peers or replace controls.
- **Localization workload:** Any new accessible label, help string, or state description must be translated consistently across supported languages; plan translation updates with the implementation, not afterward.
- **Layout scope:** The existing minimum window size and fixed server-detail layout may limit scaling. Change these only after recording reproducible clipping or navigation failures.
- **Conformance claims:** Passing automated scans is not a certification. Describe the result as an accessibility improvement unless a formal conformance assessment is commissioned.
- **Third-party text:** Game-server output, external plugins, and other externally supplied content may not have authored accessibility metadata. Keep the app's own controls accessible and present that content in a readable, selectable form where possible.