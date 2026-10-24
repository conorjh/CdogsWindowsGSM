# Engine-specific settings in Edit Config

**Status:** Proposed implementation plan

## Goal

Extend the existing Edit Config flyout with typed, discoverable settings for game engines and individual games. Deliver GoldSrc settings first, including a Counter-Strike 1.6-specific override in the same UI. Add other engine families only where the codebase has a reliable shared configuration or launch path.

## Current implementation

- `MainWindow.xaml` defines the Edit Config flyout. Its common fields include server identity, ports, player limit, start map, map rotation, GSLT, and free-form start parameters. COD1 bot controls and CFX license-key input are existing examples of conditional settings.
- `MainWindow.xaml.cs` loads the selected game's implementation in `Refresh_EditConfig_Data`, sets per-game section visibility, validates COD1 settings, and persists values from the save handler.
- `Functions/ServerConfig.cs` reads and writes named values in `WindowsGSM.cfg`. It currently has schema version 2 and demonstrates optional per-game settings for COD1 and CFX.
- `GameServer/Engine/GoldSource.cs` builds HLDS launch arguments and uses a per-server mod `server.cfg`. In shared-install mode it explicitly points HLDS at that instance's config. Keep new settings and generated files per-instance, not in shared game binaries.
- GoldSrc games include CS 1.6, Condition Zero, Day of Defeat, Deathmatch Classic, Half-Life: Opposing Force, Ricochet, and Team Fortress Classic.

### Important game identity rule

Do not select the Counter-Strike settings by checking only `Game == "cstrike"`. CS 1.6 (`CS`) is GoldSrc, while Counter-Strike: Source (`CSS`) is Source; both use the `cstrike` game-directory name. Select the section using the resolved game implementation/profile (or an equally explicit game identifier). This prevents Source servers from receiving GoldSrc cvars.

## Proposed design

1. Keep existing common Edit Config controls as-is. Add a GoldSrc section, then a nested Counter-Strike 1.6 section that appears only for the CS 1.6 profile. Retain the COD1 and CFX sections unchanged. Ensure the flyout content can scroll as new controls are added.
2. Use typed controls for bounded values and choices: checkboxes for toggles, numeric inputs for rates/timers/player limits, and a selection control for enumerated values such as server-browser region. Offer an engine-default/unchanged option where an unset cvar should preserve the game server's own default.
3. Store the values in `ServerConfig`/`WindowsGSM.cfg`, following the existing named-setting pattern. Treat newly added keys as optional when loading old files; use defaults that preserve current server behavior. Increment the schema version only if a real persisted-format migration is needed.
4. Generate/apply settings through a per-instance GoldSrc config mechanism. Do not overwrite a user's complete `server.cfg`. First verify how HLDS resolves an `exec` file and the load order in both private and shared-install modes. Prefer a separate WindowsGSM-managed config file loaded after the normal `server.cfg`; if that cannot be made reliable, use a clearly delimited, idempotently updated managed block while preserving all content outside it. Write generated files atomically and report failures rather than silently starting with stale settings.
5. Define and document precedence among UI settings, `server.cfg`, and free-form Server Start Param. Avoid duplicate launch arguments for values promoted from existing defaults. The UI's typed settings should have predictable behavior even if the user also supplies the same cvar in free-form parameters.

## Settings to consider

Confirm compatibility and semantics against supported HLDS/mod versions before implementing each item. Start with a short shared set; keep unfamiliar or mod-specific cvars out of the engine-wide section.

### GoldSrc shared section

| Setting | GoldSrc cvar | UI / validation notes |
|---|---|---|
| LAN-only server | `sv_lan` | Toggle. Explain that enabling it limits the server to LAN use. |
| Browser region | `sv_region` | Selection list from the supported HLDS values; verify the value mapping and provide an engine-default choice if appropriate. |
| Minimum client rate | `sv_minrate` | Non-negative numeric value; allow unchanged/default. |
| Maximum client rate | `sv_maxrate` | Non-negative numeric value; validate against minimum rate when both are set. |

Do not repeat existing common fields such as server port, player limit, map, or map rotation in this section.

### Counter-Strike 1.6 override

Show these in the same flyout, but only for GoldSrc CS 1.6. Verify each cvar and its supported range before finalizing the UI. Candidates:

| Setting | Counter-Strike cvar | UI / validation notes |
|---|---|---|
| Round duration | `mp_roundtime` | Numeric minutes; enforce the documented range and precision. |
| Freeze duration | `mp_freezetime` | Non-negative seconds. |
| Buy time | `mp_buytime` | Numeric minutes; enforce the documented range and precision. |
| C4 timer | `mp_c4timer` | Positive duration in seconds, within the supported range. |
| Starting money | `mp_startmoney` | Non-negative amount within the supported range. |
| Friendly fire | `mp_friendlyfire` | Toggle. |
| Automatic team balance | `mp_autoteambalance` | Toggle. |
| Team limit difference | `mp_limitteams` | Non-negative integer. |

Keep this first override scoped to the CS 1.6 profile. Add Condition Zero or other mods later only after verifying their behavior; do not infer support from a shared directory name.

## Other engine families for follow-on work

Add these after the GoldSrc flow is working, preferably one family at a time. Preserve current defaults and make sure each setting is applied through the engine's actual config/launch mechanism.

### Source

The Source-derived game classes already place useful defaults in `Additional`. Make those values editable without changing existing installations' behavior or duplicating their launch arguments:

- **Counter-Strike: Source:** tick rate (current default: 64).
- **Counter-Strike: Global Offensive:** tick rate (64), game type, game mode, and map group (currently represented in `Additional`).
- **Garry's Mod:** tick rate (66), game mode, and workshop collection (currently represented in `Additional`).

Promote current defaults to typed settings only when the corresponding game profile is selected. Preserve free-form extra parameters for advanced options and clearly resolve conflicts with values managed by typed controls. Consider shared Source cvars only after verifying that they apply consistently across the supported Source titles.

### Minecraft Java

PaperMC and Forge use the shared `GameServer/Engine/MinecraftJava.cs` implementation and its `server.properties` writer. Candidate settings include difficulty, game mode, PvP, online mode, and view distance; add simulation distance only where the installed server version supports it. The older `GameServer/MC.cs` implementation is separate, so either include it with its own compatible config-writer work or explicitly leave it out of the first Minecraft pass.

### Other engines and game-specific integrations

Unity and Unreal engine classes are currently marker classes, while their games implement their own launch/config logic. Do not add generic engine panels until there is a real shared settings contract. For other games, add typed controls only when there is a stable, testable configuration target and a clear per-game setting profile. Keep existing COD1 and CFX special sections working throughout.

## Delivery phases

### Phase 0 — Confirm behavior and settings

- Verify GoldSrc cvar support and value ranges across the supported HLDS/mod versions.
- Verify config lookup and execution order for private installs and shared installs, including how an instance-specific generated config can be loaded.
- Decide the precedence rule for typed settings versus `server.cfg` and Server Start Param.
- Finalize which settings preserve engine defaults when left unset; record defaults and units in labels/help text.

**Exit gate:** the cvar list, validation rules, game-profile mapping, config-load path, and precedence are documented and verified before UI work begins.

### Phase 1 — GoldSrc settings

- Add GoldSrc and CS 1.6 conditional sections to the existing Edit Config flyout.
- Add setting names, typed values, load defaults, parsing, and save behavior to `ServerConfig`.
- Generate or update the per-instance managed config using the verified load mechanism. Keep it out of shared binary directories and preserve user-owned config content.
- Add actionable validation and save/start errors; never silently discard invalid values or fall back to stale generated settings.

**Exit gate:** values entered in Edit Config survive save/reload and are applied to the intended GoldSrc instance after restart without changing another instance's settings.

### Phase 2 — Counter-Strike 1.6 override

- Add CS-only match settings under the GoldSrc controls in the same flyout.
- Validate numeric ranges and cross-field rules before saving.
- Confirm the section is hidden for CSS and other GoldSrc titles, and does not change CSCZ behavior unless CSCZ is explicitly added as a supported profile.

**Exit gate:** CS 1.6 settings apply only to the CS 1.6 profile; CSS and other games retain their existing startup/config behavior.

### Phase 3 — Source profiles

- Convert selected existing `Additional` defaults into typed per-game values for CSS, CS:GO, and Garry's Mod.
- Preserve current launch behavior for existing servers and prevent duplicate command-line flags when a setting is edited.
- Add shared Source settings only where support is verified across all targeted titles.

### Phase 4 — Minecraft Java and further profiles

- Add version-aware Minecraft Java fields through the shared `MinecraftJava` settings/config path.
- Decide separately whether the legacy `MC` implementation is in scope and add its config writing/tests if so.
- Evaluate other engines/game integrations only when a shared config target and clear per-game applicability exist.

## Verification and acceptance

Add tests in `CdogsWindowsGSM.Tests` for the non-UI behavior and perform a Windows UI smoke test:

- Profile selection shows GoldSrc controls for GoldSrc games and shows the CS 1.6 override only for CS 1.6. Include a regression case for CS 1.6 and CSS both having a `cstrike` directory name.
- New values round-trip through `WindowsGSM.cfg`; older config files without the new keys continue to load with behavior-preserving defaults.
- Invalid values are rejected with a visible, localized message; rate and match-setting cross-field constraints are enforced.
- Generated config contains the expected cvars, is idempotent, preserves all user-owned config content, and is written under the correct server instance even when binaries are shared.
- Existing map rotation, COD1 settings, CFX license-key handling, and free-form parameters continue to work.
- Source and Minecraft follow-on profiles preserve their existing defaults when the new controls are left unchanged.
- Labels, units, help text, and validation messages follow the existing localization resource conventions.

## Likely code touchpoints

- `CdogsWindowsGSM/MainWindow.xaml` — Edit Config sections and scrollable layout.
- `CdogsWindowsGSM/MainWindow.xaml.cs` — profile detection, refresh/load, validation, and save.
- `CdogsWindowsGSM/Functions/ServerConfig.cs` — setting names, defaults, parsing, and persistence.
- `CdogsWindowsGSM/GameServer/Engine/GoldSource.cs` — apply the managed settings through the verified per-instance config path.
- `CdogsWindowsGSM/GameServer/CS.cs` and related game classes — explicit CS 1.6 versus other GoldSrc profile identity.
- `CdogsWindowsGSM/GameServer/CSS.cs`, `CSGO.cs`, and `GMOD.cs` — possible Source-profile follow-on work.
- `CdogsWindowsGSM/GameServer/Engine/MinecraftJava.cs` and, if included, `MC.cs` — possible Minecraft follow-on work.
- `CdogsWindowsGSM/Localization/Strings.resx` and applicable localized resource files — labels and validation text.
- `CdogsWindowsGSM.Tests` — persistence, validation, profile-selection logic, and generated-config tests.