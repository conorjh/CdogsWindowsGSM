# Game integration Alpha matrix

**Updated: 31 October 2026.** This is the single current game-readiness report. V1 is the gate: verify the private, built-in V1 operations for all 59 built-ins before qualifying built-in shared-install or plugin V2 behavior. Update the existing cells and per-game row on each pass; do not append another historical run table. **No game is Alpha-qualified by the evidence below.**

The V1 live tests now target one operation per test method and use private installs, even for games whose shared-install choice is enabled. A `DynamicData` case is one game. Prior all-in-one lifecycle smoke results are retained as historical context below, not substituted for the new operation-specific results.

## Operation/test map

The method names below are the operation selectors in `CdogsWindowsGSM.Tests`. The current per-operation tests are in the `V1` category; select a game with `CDOGS_GAME_INTEGRATION_GAMES` when running a live operation.

For Java Edition, Forge, and Paper, `CDOGS_GAME_INTEGRATION_MINECRAFT_EULA_CONSENT=I_HAVE_READ_AND_ACCEPTED` enables the consented EULA prompt in an isolated integration run. For Bedrock, `CDOGS_GAME_INTEGRATION_MINECRAFT_BEDROCK_EULA_PRIVACY_CONSENT=I_HAVE_READ_AND_ACCEPTED` enables its combined prompt only after the user accepts both the EULA and privacy policy. Both flags require `CDOGS_GAME_INTEGRATION_GAMES` and `CDOGS_GAME_INTEGRATION_ROOT` and are test-only consent inputs.

| Phase / ID | Operation | Test method | Coverage boundary |
|---|---|---|---|
| V1-CAT | Adapter catalog | `EveryBuiltInGameExposesTheRequiredV1OperationSurface` | All 59; contract presence only. |
| V1-I | Install | `V1InstallOperationCompletesForBuiltInGame` | All 59; private install. |
| V1-U | Update | `V1UpdateOperationCompletesForBuiltInGame` | All 59; private install is setup, the target test covers normal Update and `validate=true` where exposed. |
| V1-V | Install validation | `V1InstallValidationReflectsInstalledStateForBuiltInGame` | All 59; checks empty and installed states. The private Install test also asserts `IsInstallValid` after installation; `V1InstallValidationRejectsAnEmptyPrivateInstallForBuiltInGame` is the offline negative-path companion. |
| V1-M | Import validation | `V1ImportValidationAcceptsInstalledFilesAndRejectsAnEmptyDirectory` | All 59 live cases are available; `V1ImportValidationRejectsAnEmptyDirectoryForBuiltInGame` covers the local negative path, and `V1GoldSourceImportValidationAcceptsAValidExecutableFixture` covers the seven GoldSrc positive paths without another download. |
| V1-C | Configuration generation | `V1ConfigurationGenerationCompletesForBuiltInGame` | All 59 built-ins have operation-specific cases, including Rust, Bedrock, and Luanti configuration paths. |
| V1-LB | Local build | `V1LocalBuildLookupReturnsForInstalledBuiltInGame` | Installed build lookup where exposed; GoldSrc and Source-engine results are recorded in the current live V1 status below. |
| V1-RB | Remote build | `V1RemoteBuildLookupReturnsForBuiltInGame` | Remote metadata lookup; five-minute bound and dedicated test-only SteamCMD root so it cannot remove the user's normal AppInfo cache. |
| V1-S | Start | `V1StartOperationSurvivesItsStartupWindowForBuiltInGame` | All 59; 15-second startup window, Rust readiness/query check. The test force-terminates its disposable process as cleanup; it does not count that cleanup as a Stop pass. |
| V1-T | Stop | `V1StopOperationStopsTheBuiltInGameProcess` | All 59; starts a disposable process as setup and requires adapter Stop to exit within 30 seconds. Java Edition, Forge, and Paper wait for the Minecraft ready marker before Stop. |
| V1-MAP | Maps / rotation | `V1MapCycleOperationsRoundTripForSourceAndGoldSourceGames` | 18 concrete Source/GoldSrc games; local map fixtures exercise enumeration, rotation read, and save. SDK2013 has no concrete game identity. |
| V1-FB | Full-server backup / restore | `V1FullServerBackupRestoreRoundTripsForBuiltInGame` | All 59; local ZIP round-trip through the same archive helper called by the host. This does not qualify live game data or scheduled-backup timing. |
| V1-CB | Config-only backup / restore | `V1ConfigurationBackupRestoreRoundTripsForBuiltInGame` | All 22 built-ins with an explicit host plan; seeded local files, not live game writes. |

### V1 status legend

- `P` — operation-specific test passed on the current candidate.
- `P*` — local synthetic/file-backed test passed; not a live server qualification.
- `P*/NR` — local negative-path case passed; the live positive-path operation test has not run.
- `P*/B` — local negative-path case passed; the live positive-path operation test is blocked by an explicit external/operator prerequisite.
- `P*/P` — local negative-path case and live positive-state check both passed.
- `NR` — operation-specific test has not run on the current candidate.
- `F` — operation-specific live test failed on the current candidate and needs a fix/retest.
- `B` — blocked by an explicit external/operator prerequisite; no pass is counted.
- `GATED` — deliberately disabled by the host after a known failure; do not enable by changing only an allowlist.
- `—` — not applicable or not supported by that adapter.

The operation matrix below reports the new operation-specific tests. The historical integrated outcome is reported separately; it never changes an `NR` cell to `P`.

**Current live V1 cases:** The previous operation-specific live run covered 57 built-ins. Fifty-one passed all nine V1 operations (459/459). Six remain externally gated: COD1 and Risk of Rain 2 each have seven install-dependent operations `B` because Steam reported `No subscription`; Insurgency Start/Stop are `B` for the same subscription gate; FiveM and RedM Start/Stop are `B` until a CFX license key is supplied; and SDK2013 Start/Stop are `B` until a concrete mod/map is supplied. Those results total 491 runnable passes and 22 `B` cells. Left 4 Dead and Luanti were added afterward; their live/positive V1 cases remain `NR`. Local L4D config/client-port, validation, map-cycle, and backup fixtures passed; Luanti config/import-validation and backup fixtures passed. Config and Remote Build passed for the previous 57 built-ins (114/114); both subsequent additions' build lookups remain `NR`. Synthetic map and backup cases are local fixtures, not live game-data qualification. Start/Stop results measure process behavior; they do not prove client connectivity, gameplay readiness, persistence, or V2 shared-install behavior.

## V1 feature matrix — all 59 built-ins

| ID / game | Install | Update | Valid | Import | Config | Local build | Remote build | Start | Stop | Full B/R | Config B/R | Maps |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| G-ARKSE ARK: Survival Evolved | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-AVORION Avorion | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-BT Barotrauma | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-BW BlackWake | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-CE Conan Exiles | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-COD1 Call of Duty 1 | B | B | P*/B | P*/B | P | B | P | B | B | P* | P* | — |
| G-COD4 Call of Duty 4 (CoD4x) | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-CS Counter-Strike: 1.6 | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-CSCZ Counter-Strike: Condition Zero | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-CSGO Counter-Strike: Global Offensive | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-CSS Counter-Strike: Source | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-DAYZ DayZ | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-DMC Deathmatch Classic | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-DOD Day of Defeat | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-DODS Day of Defeat: Source | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-ECO Eco | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-EGS Empyrion - Galactic Survival | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-ForgeMC Minecraft: Forge | P | P | P*/P | P*/P | P | P | P | P | P | P* | — | — |
| G-GMOD Garry's Mod | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-GTA5 FiveM server | P | P | P*/P | P | P | P | P | B | B | P* | — | — |
| G-HEAT Heat | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-HL2DM Half-Life 2: Deathmatch | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-HLOF Half-Life: Opposing Force | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-INS Insurgency | P | P | P*/P | P*/P | P | P | P | B | B | P* | P* | P* |
| G-INSS Insurgency: Sandstorm | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-L4D Left 4 Dead | NR | NR | P*/NR | P*/NR | P* | NR | NR | NR | NR | P* | P* | P* |
| G-L4D2 Left 4 Dead 2 | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-LUANTI Luanti | NR | NR | P*/NR | P*/NR | P* | NR | NR | NR | NR | P* | P* | — |
| G-MC Minecraft: Java Edition | P | P | P*/P | P*/P | P | P | P | P | P | P* | — | — |
| G-MCBE Minecraft: Bedrock Edition | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | — |
| G-MCPE Minecraft: Pocket Edition (PocketMine-MP) | P | P | P*/P | P*/P | P | P | P | P | P | P* | — | — |
| G-Mindustry Mindustry | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-MORDHAU Mordhau | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-NMRIH No More Room in Hell | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-OLOW Outlaws of the Old West | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-ONSET Onset | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-OpenRA OpenRA | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-OpenTTD OpenTTD | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-PaperMC Minecraft: Paper | P | P | P*/P | P*/P | P | P | P | P | P | P* | — | — |
| G-PS Post Scriptum | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-RCC Ricochet | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-RCT2 RCT2 | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-RedM RedM server | P | P | P*/P | P | P | P | P | B | B | P* | — | — |
| G-ROK Reign of Kings | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-ROR2 Risk of Rain 2 | B | B | P*/B | P*/B | P | B | P | B | B | P* | — | — |
| G-RUST Rust | P | P | P*/P | P | P | P | P | P | P | P* | P* | — |
| G-SCUM SCUM | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-SDK2013 Source SDK Base 2013 | P | P | P*/P | P | P | P | P | B | B | P* | — | — |
| G-SDTD 7 Days to Die | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-SE Space Engineers | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-Soldat Soldat | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-SQ Squad | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-SW Stormworks | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-TF The Forest | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-TFC Team Fortress Classic | P | P | P*/P | P | P | P | P | P | P | P* | P* | P* |
| G-TF2 Team Fortress 2 | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |
| G-UNT Unturned | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-VTS Vintage Story | P | P | P*/P | P | P | P | P | P | P | P* | — | — |
| G-ZPS Zombie Panic Source | P | P | P*/P | P*/P | P | P | P | P | P | P* | P* | P* |

`P*/P` in Valid means the empty-install negative fixture and live installed-state check passed. `P*/NR` means the negative fixture passed but the live positive-path check has not run; `P*/B` means it is blocked by an external/operator prerequisite. GoldSrc Import `P` means both the negative input and live installer-produced directory were checked. All 22 Config B/R passes are local seeded-file archive round-trips. All 18 Maps passes are local synthetic map fixtures. All 59 Full B/R passes exercise the production ZIP helper; they do not test game persistence or the scheduled-backup trigger.

## V2 operation matrix — deferred until V1 is complete

V2 plugins retain the V1 lifecycle surface and may optionally expose capability methods. Their contract/local tests do not count as live provider qualification. The existing V2 live lifecycle test is still combined; split it into one live test per V2 operation before starting that phase.

| V2 operation | Current test mapping | Current result / next step |
|---|---|---|
| Capability/profile detection | `PluginApiContractTests.TypedSharedInstallMethodsAndConfigOnlyBackupPlanInferCapabilitiesWithoutInterfaces`; related malformed/legacy cases in `PluginApiContractTests` | Local contract coverage; not live qualification. |
| `GetConfigOnlyBackupPlan(PluginInstanceContext)` | `V2PluginExamplesRoundTripTheirApprovedConfigurationBackupsThroughTheHost` | Local example round-trip; rerun in V2 phase. |
| `InstallSharedBinaries(SharedInstallationContext)` | `InstallUpdateVerifyLaunchAndV2CapabilitiesWorkForPluginExample` | Combined live method; operation-specific live test is deferred. |
| `InstallConfigOnly(ServerInstanceContext)` | `InstallUpdateVerifyLaunchAndV2CapabilitiesWorkForPluginExample`; local host round-trip also calls this for Garry's Mod V2 | Combined live method; operation-specific live test is deferred. |
| Host-owned V2 config backup/restore | `V2PluginExamplesRoundTripTheirApprovedConfigurationBackupsThroughTheHost` | Local file-backed round-trip only; live plugin scope remains unqualified. |
| Inherited plugin Install / Update / Start / Stop | `InstallUpdateVerifyLaunchAndV2CapabilitiesWorkForPluginExample` | Combined live lifecycle; V2 phase must split operation results and qualify runtime behavior. |

### Built-in shared-install feature gates (V2 host work)

These are host-owned built-in capabilities, separate from the V2 plugin API above. Twenty built-ins have shared binary/config-only setup code enabled: eleven Source titles, seven GoldSrc titles, Call of Duty 1, and Minecraft Bedrock. This is code enablement, not live qualification; the newly integrated Left 4 Dead is included but has not been live-qualified. Luanti has a config-only backup plan and process health support but remains private-only; query and Steam global-list checks are unsupported. Rust also has a config-only backup plan but shared installation is deliberately disabled after the `Bundles\\Bundles` startup-path failure. The other 39 built-ins remain private-only. The shared/config-only implementation notes and exact host-approved backup paths are in the [config-only backup guide](CONFIG-ONLY-BACKUP-GUIDE.md); live shared-mode tests stay deferred until the V1 private matrix passes.

The app's **Built-in V2 Features** screen lists shared-installation and configuration-only backup eligibility plus process, game-query, and Steam global-list health-check support for all built-in games. Query support follows each game's configured query method; Steam-list support follows the existing game gate. Capability states display as check/cross marks while exposing the localized status through tooltips and UI Automation names/help text. These are host capability indicators, not live qualification, and do not alter the V1/V2 test status.
Use this screen as an operator visibility aid only; qualification still comes from the operation-specific test matrix in this report.

| Built-in V2 host operation | Applicable games | Current test mapping | Status |
|---|---|---|---|
| Install shared binaries | 20 enabled providers | `BuiltInGameCapabilitiesTests.SharedInstallationSupportsBackedUpGamesWithEnabledProviders` checks gates; no current operation-specific live test is active. | Deferred until all V1 cells pass. |
| Install config-only instance files | The same 20 shared providers | The capability test checks eligibility; GoldSrc/source unit tests check selected local config paths. No current operation-specific live test is active. | Deferred until all V1 cells pass. |
| Shared-install eligibility gate | Rust, SDK2013, unknown identities, and the enabled provider families | `BuiltInGameCapabilitiesTests.SharedInstallationSupportsBackedUpGamesWithEnabledProviders` | Gate test is not yet an exhaustive negative assertion for every one of the 38 private-only built-ins; add that check in the deferred V2 phase. Rust remains `GATED`. |

## Per-game effort, current position, and observed build IDs

The outcome below combines the latest integrated smoke with the new operation-specific V1 results. Build IDs are metadata observed during a test, not a pinned candidate or proof of validation. GoldSrc's current V1 Install logs report AppID-90 BuildID `15961496`; the same value was present in the earlier baseline. `—` means no ID was captured.

| ID / game | Latest integrated evidence, V1 result, or next check | Observed manifest/build ID(s) |
|---|---|---:|
| G-ARKSE ARK: Survival Evolved | All nine current private V1 operations passed (9/9). Start now stages the verified legacy DirectX runtime and exposes the packaged Unreal dependency folders to fix the `0xC0000135` startup failure; gameplay readiness remains outside this process-level pass. | 21241282 |
| G-AVORION Avorion | All nine current private V1 operations passed (9/9), including Stop after the headless-shutdown fix. Readiness, effective settings, save/restart, and persistence remain open. | 22295362 |
| G-BT Barotrauma | All nine current private V1 operations passed (9/9). The corrected non-embedded shutdown smoke also passed; readiness and save/restart remain open. | 23799137 |
| G-BW BlackWake | All nine current private V1 operations passed (9/9). Effective settings, endpoint/query behavior, and gameplay readiness remain unqualified. | 5059939 |
| G-CE Conan Exiles | All nine current private V1 operations passed (9/9). Readiness, effective settings, save/restart, and persistence remain open. | 25792439 |
| G-COD1 Call of Duty 1 | V1 Config and Remote Build passed. SteamCMD's private install of AppID `2620` returned `No subscription`, so Install, Update, live validation/import, local build, Start, and Stop are `B` until a licensed account or operator-supplied base archive is available; local negative-path fixtures passed. | — |
| G-COD4 Call of Duty 4 (CoD4x) | All nine current private V1 operations passed (9/9). The Cod4x server package and AppID-7940 server install completed; licensed game-data provenance, process identity, and recovery remain separate checks. | 2737681 |
| G-CS Counter-Strike: 1.6 | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. Earlier config-template retest observed client manifest `12934623`. | 15961496 (AppID 90); 12934623 (client config retest) |
| G-CSCZ Counter-Strike: Condition Zero | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. | 15961496 (AppID 90) |
| G-CSGO Counter-Strike: Global Offensive | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, 15-second Start survival, and Stop passed. Earlier corrected-mode smoke passed; effective settings and readiness remain open. | 12426148 |
| G-CSS Counter-Strike: Source | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, 15-second Start survival, and Stop passed. Historical baseline smoke passed; map, query, and config consumption remain open. | 25338297 |
| G-DAYZ DayZ | All nine current private V1 operations passed (9/9). `DayZServer_x64.exe` lacked legacy XAudio/XACT imports on this Windows image; Start now stages the required x64 DirectX runtime privately after validating Microsoft's redist and each extracted DLL. No system-wide DirectX install is performed. | 25963285 |
| G-DMC Deathmatch Classic | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. | 15961496 (AppID 90) |
| G-DOD Day of Defeat | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. | 15961496 (AppID 90) |
| G-DODS Day of Defeat: Source | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, 15-second Start survival, and Stop passed; `GetLocalBuild` returned `25338204`, and the prior exit-8/AppInfo-timeout failure did not reproduce. | 25338204 |
| G-ECO Eco | All nine current private V1 operations passed (9/9). Readiness, effective settings, and graceful save remain open. | 25987282 |
| G-EGS Empyrion - Galactic Survival | All nine current private V1 operations passed (9/9); the earlier launch/stop assertion did not reproduce. Process identity, effective settings, and save/restart remain unqualified. | 25490402 |
| G-ForgeMC Minecraft: Forge | All nine current private V1 operations passed. `GetLocalBuild` returned `26.3-66.1.0`. The user's explicit EULA consent was used only for disposable test instances; MinecraftJava now provides a version-appropriate private runtime. Stop was tested after the server logged readiness. | 26.3-66.1.0 |
| G-GMOD Garry's Mod | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, 15-second Start survival, and Stop passed; `GetLocalBuild` returned `25375525`, also observed in appmanifest during Update. Historical baseline smoke passed; workshop, settings, and shared-instance behavior remain open. | 25375525 |
| G-GTA5 FiveM server | All seven non-license-gated V1 operations passed (7/7), including Install, Update, validation/import, and local build; Config and Remote Build also pass. Start/Stop are `B` pending a CFX license key. Upstream `cfx-server-data` supplies resources but no `server.cfg`; Install validity now checks the resources directory and the separate Config operation generates `server.cfg`. | 35245-6efb47dff473c0e2a12fb50b08d74c0eb24a50d5 |
| G-HEAT Heat | All nine current private V1 operations passed (9/9). The Unity server's .NET bootstrap requires a console context; Stop now awaits `/shutdown` and force-stops the process tree if no usable window exists or shutdown times out. | 6929872 |
| G-HL2DM Half-Life 2: Deathmatch | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, and 15-second Start survival passed; `GetLocalBuild` returned `24600426`. Historical corrected-mode smoke passed after an install retry. | 24600426 |
| G-HLOF Half-Life: Opposing Force | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. | 15961496 (AppID 90) |
| G-INS Insurgency | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, and local/remote build lookup passed; `GetLocalBuild` returned `20000984`, also observed in appmanifest during Update. Start exits with code `-1`; an authenticated SteamCMD install of paid game AppID `222880` returned `No subscription`. Start/Stop remain `B` until an account licensed for that game is available; the Stop test cannot reach the operation. | 20000984 |
| G-INSS Insurgency: Sandstorm | All nine current private V1 operations passed (9/9). Effective settings, query readiness, save/restart, and persistence remain open. | 25534773 |
| G-L4D Left 4 Dead | Live/positive V1 install, update, validation/import, build lookup, and Start/Stop checks remain `NR`. Local config generation/client-port substitution, negative validation, map rotation, full-server backup, and config-only backup fixtures passed; this verifies host-side plumbing only, not a live installation or shared-instance safety. | — |
| G-L4D2 Left 4 Dead 2 | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, and 15-second Start survival passed; `GetLocalBuild` returned `23990100`, also observed in appmanifest during Update. Historical integrated startup exited earlier; readiness remains unqualified. | 23990100 |
| G-LUANTI Luanti | Live/positive V1 install, update, build lookup, and Start/Stop checks remain `NR`. Local config generation, import validation, invalid-port rejection, full-server backup, and config-only backup fixtures passed. The adapter downloads the official engine and separate Minetest Game default; query/Steam-list checks are unsupported and shared installation remains disabled. | — |
| G-MC Minecraft: Java Edition | All nine current private V1 operations passed. `GetLocalBuild` returned `26.3`. Replaced the legacy system-Java 8 install path with the shared MinecraftJava engine, which downloads a version-appropriate private runtime and verifies Mojang's server JAR checksum. The user's explicit EULA consent was used only for disposable test instances. | 26.3 |
| G-MCBE Minecraft: Bedrock Edition | All nine current private V1 operations passed. `GetLocalBuild` returned `1.26.52.3`. The user explicitly consented to both the EULA and privacy policy for disposable tests. Fixed Update's assumption that the archive always includes `bedrock_server.pdb`; non-embedded Stop now uses redirected stdin. The stale-port prelaunch regression passed locally; the actual Bedrock listener remains unverified. | 1.26.52.3 |
| G-MCPE Minecraft: Pocket Edition (PocketMine-MP) | All nine current V1 operations passed: private Install, Update, installed-state validation, import validation, config generation, local/remote build lookup, Start, and graceful Stop. `GetLocalBuild` returned `2617`. Replaced retired Jenkins URLs with official PMMP GitHub release metadata and its matching PHP runtime asset; persisted the release metadata for local build lookup and made Stop send `stop` through redirected stdin. | 2617 |
| G-Mindustry Mindustry | All nine current private V1 operations passed (9/9). Release metadata and SHA-256 come from the public Atom/expanded-assets pages; Start uses the shared private Java 17 runtime when no compatible system Java exists. | — |
| G-MORDHAU Mordhau | All nine current private V1 operations passed (9/9). Start now stages the verified legacy DirectX runtime and adds packaged Unreal dependency folders to the child PATH, fixing the `0xC0000135` startup failure. | 16147292 |
| G-NMRIH No More Room in Hell | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, and local/remote build lookup passed; `GetLocalBuild` returned `24996675`, also observed in appmanifest during Update. Start passed after removing the `-console` argument. Stop passed using process-tree termination because this launch has no console input channel. | 24996675 |
| G-OLOW Outlaws of the Old West | All nine current private V1 operations passed (9/9). Effective settings and graceful shutdown remain open. | 5113531 |
| G-ONSET Onset | All nine current private V1 operations passed (9/9). Non-embedded Start now launches through a shell-provided console context; Start and Stop passed their operation-specific retests. | 8141104 |
| G-OpenRA OpenRA | All nine current private V1 operations passed (9/9). Release metadata uses the public Atom/expanded-assets pages; install, validation, import, and Start handle root- or `bin`-level server executables. | — |
| G-OpenTTD OpenTTD | All nine current private V1 operations passed (9/9). Release metadata uses the public GitHub Atom feed; Start drains output so native console logging cannot disrupt TestHost. | — |
| G-PaperMC Minecraft: Paper | All nine current private V1 operations passed. `GetLocalBuild` returned `26.3/226`. The user's explicit EULA consent was used only for disposable test instances; MinecraftJava now shares the version-appropriate private runtime. Stop was tested after the server logged readiness. | 26.3/226 |
| G-PS Post Scriptum | All nine current private V1 operations passed (9/9). Start now stages the packaged DirectX compiler DLL and adds the bundled Unreal/third-party DLL folders to the child PATH; process-level readiness and game connectivity still need deeper qualification. | 21989442 |
| G-RCC Ricochet | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; map and backup fixtures passed locally. Shared V2 behavior remains deferred. | 15961496 (AppID 90) |
| G-RCT2 RCT2 | All nine current private V1 operations passed (9/9). Runtime publication and original-game/park gameplay readiness remain outside this process-level pass. | — |
| G-RedM RedM server | All seven non-license-gated V1 operations passed (7/7), including Install, Update, validation/import, and local build; Config and Remote Build also pass. Start/Stop are `B` pending a CFX license key. The upstream data archive supplies resources but no `server.cfg`; Install validity checks resources and Config generates `server.cfg` separately. | 35245-6efb47dff473c0e2a12fb50b08d74c0eb24a50d5 |
| G-ROK Reign of Kings | All nine current private V1 operations passed (9/9). Its .NET console bootstrap now receives a real console context; Stop awaits `/shutdown` and force-stops the process tree if no usable window exists. | 4088734 |
| G-ROR2 Risk of Rain 2 | The dedicated-server AppID `1180760` installs only a zero-byte depot. The adapter now targets base-game AppID `632360` for install/update/local build, but SteamCMD returned `No subscription` on the tested account; those install-dependent cells are `B`. Config and Remote Build pass against the dedicated-server tool metadata. | 20243729 |
| G-RUST Rust | All nine current private V1 operations passed (9/9) after increasing the Rust readiness bound to ten minutes; one cold Stop setup took nearly six minutes to emit startup complete. A2S readiness and WebRCON Stop passed. Save/restart and full/config-only backup remain open; shared install stays gated. | 26139947 |
| G-SCUM SCUM | All nine current private V1 operations passed (9/9). Start now stages the bundled DirectX compiler runtime and adds the packaged PhysX/Vorbis/NVIDIA DLL folders to PATH; Stop falls back to process-tree termination if Ctrl+C does not complete. | 25803875 |
| G-SDK2013 Source SDK Base 2013 | The five nonblocked Install, Update, validation/import, and local-build operations passed; Config and Remote Build also pass. Start/Stop remain `B` until an operator supplies a concrete mod/map. Generic config generation creates a minimal `server.cfg` when `Game` is empty. | 17413217 |
| G-SDTD 7 Days to Die | All nine current private V1 operations passed (9/9). Stop passed after Telnet was enabled and configured with the instance's port/password; world save/restart remains open. | 26167630 |
| G-SE Space Engineers | All nine current private V1 operations passed (9/9); the earlier `0xC000013A` runner exit did not reproduce. Live gameplay readiness and persistence remain outside the process-level V1 check. | 24675709 |
| G-Soldat Soldat | All nine current private V1 operations passed (9/9). Fixed the installer to preserve empty archive directories required at startup, including the server's logging/runtime paths. | — |
| G-SQ Squad | All nine current private V1 operations passed (9/9); shared-path validation and graceful save remain open. | 26005357 |
| G-SW Stormworks | All nine current private V1 operations passed (9/9); effective configuration and graceful save remain open. | 14768962 |
| G-TF The Forest | All nine current private V1 operations passed (9/9); derived-port behavior, configuration consumption, and gameplay persistence remain open. | 3488796 |
| G-TFC Team Fortress Classic | Current private V1 Install, Update, CreateServerCFG, import validation, local/remote build lookup, Start, and Stop passed. `GetLocalBuild` returned AppID-90 BuildID `15961496`; corrected non-embedded shutdown smoke also passed historically. Shared V2 update/recovery remains deferred. | 15961496 (AppID 90) |
| G-TF2 Team Fortress 2 | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, and 15-second Start survival passed; `GetLocalBuild` returned `25995664`, also observed in appmanifest during Update. Historical baseline smoke passed; launcher, map, and effective settings remain open. | 25995664 |
| G-UNT Unturned | All nine current private V1 operations passed (9/9). Stop now sends `shutdown` over redirected input and uses a bounded process-tree fallback if graceful exit stalls; effective settings remain open. | 25613089 |
| G-VTS Vintage Story | All nine current private V1 operations passed (9/9). Stop sends `/stop` over redirected input in headless mode and has a bounded process-tree fallback; updater safety remains open. | 1.22.7 |
| G-ZPS Zombie Panic Source | Current private V1 Install, normal/validated Update, installed-state validation, live import validation, config generation, local/remote build lookup, and 15-second Start survival passed; `GetLocalBuild` returned `20602753`, also observed in appmanifest during Update. Historical integrated startup exited earlier; readiness remains unqualified. | 20602753 |
| P-CS16StandaloneV2 Counter-Strike 1.6 V2 example | Baseline smoke and local backup fixture passed; plugin runtime and configuration scope remain unqualified. V2 live work is deferred. | 15961496 |
| P-GarrysModV2 Garry's Mod V2 example | Baseline smoke and local backup fixture passed; runtime/shared behavior remains unqualified. V2 live work is deferred. | 25375525 |

## Current work order

1. The previous 57-game V1 run has 51 built-ins at 9/9 and six built-ins with 22 explicitly blocked cells. Left 4 Dead and Luanti's live/positive operation cells remain `NR`; their local validation/configuration fixtures have passed.
2. Run the remaining Left 4 Dead and Luanti V1 operations and resolve the external gates with operator-provided prerequisites: Steam subscriptions for COD1, Risk of Rain 2, and Insurgency; CFX license key(s) for FiveM and RedM; and concrete mod/map data for SDK2013. Retest only those blocked operations when prerequisites become available.
3. If a prerequisite cannot be supplied or a game cannot be made viable, decide whether to remove that built-in; do not convert `B` to `P` without the corresponding passing operation test.
4. Do not count any V1 pass as V2 shared-install qualification. Shared owner/client isolation, concurrent starts, update locking, rollback, and recovery remain deferred while any V1 cell is blocked.
5. After every retained built-in has a complete V1 matrix, split and run the V2 operation tests: built-in shared binary/config setup, then plugin capability profile, backup plan, shared install, config-only install, host backup/restore, and inherited lifecycle.
6. Preserve explicit EULA/operator-consent gates; never bypass or fabricate prerequisites. Keep credentials and license values out of this file, console output, and screenshots. The real Steam credentials file remains local and ignored.
