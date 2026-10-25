# Game integration alpha audit and AI handoff

- Audit date: 24-25 October 2026.
- Branch: `audit/alpha-game-integration-readiness`.
- Source baseline: `42e07a9950203fb266358b01aff84f90d2df8b02` (`dev`). Line references below refer to that baseline, not a future implementation branch.
- Resumption source tree: `4ebcb5121e54cee4a5825d2c63eff8f8de8ea029`, which includes the audit and the portable Debug-output fix. Game and test implementations are unchanged from the source baseline.
- Live-run candidate: `f83c0766c5b137203fb3d011708e8e182c034d78`. Both TFC attempts and all 51 selected non-TFC invocations used the same existing Debug assemblies and unchanged game/test code; no product fix was applied.
- Scope: all 57 built-ins selected by `BuiltInGameIntegrationTests.BuiltInGameCases`, both live V2 example cases, all four test methods in that class, and the host paths needed to interpret their results.
- Deliverable: an audit/backlog with recorded execution evidence, not game fixes, support removal, or new capability enablement. The build-path correction is recorded under ENV-01. Existing user changes are outside this audit.

Navigation: [live TFC evidence](#live-tfc-failure-evidence), [remaining live rerun results](#remaining-live-rerun-results), [test inventory](#test-inventory-and-oracle-review), [harness/host tasks](#shared-harness-and-host-work), [qualification gaps](#qualification-gaps-not-demonstrated-game-failures), [every game](#game-by-game-ledger), [adapter tasks](#consolidated-adapter-work), [handoff and evidence](#handoff-workflow-and-evidence-contract).

## Read this first

**No game is alpha-qualified by this report.** The SDK/build gate is resolved and the user's saved credentials file passes TFC's preflight. On the original live-run candidate, TFC failed twice at the same 30-second shutdown timeout; the 58-row remaining-game run had 17 smoke passes, 32 failures, eight blocks, and one Space Engineers abort. Those original results are retained below as historical evidence. The implementation branch has since corrected HAR-01 and retested all seven GoldSrc rows, including TFC, in the configured non-embedded mode: all seven passed the existing smoke oracle. Other old failures/blocks were not retested after the harness-mode correction. Readiness, save/restart, recovery and shared-isolation evidence remain open; a smoke pass is not alpha qualification.

| Evidence | Result in this audit |
|---|---|
| Native Visual Studio build of `CdogsWindowsGSM.Tests` | **Passed** using installed .NET SDK **10.0.402**, after NuGet restore and removal of the hard-coded E-drive Debug output. The repository's `global.json` remains unchanged (`10.0.100`, `latestFeature`, no prereleases). |
| Native Test Explorer discovery | **103 tests discovered** during the original native integration run; the four methods in this class expanded to the expected 61 cases. After adding two focused GoldSrc-stop tests, Test Explorer discovered **105**. These discovery counts are not claims that all tests were run. |
| Initial native run of the integration class, before credentials were supplied | **61 cases: 2 Passed, 0 Failed, 59 Skipped.** The catalog and local V2 plugin-backup methods passed. All 57 built-in live rows and both plugin live rows stopped because the separate credentials file was missing. These are historical preflight results, not the later TFC live outcomes. |
| Relevant local regression run through `dotnet test` | **38 executed, 38 Passed, 0 Failed, 0 Skipped.** Covers `BuiltInGameCapabilitiesTests`, `SharedInstallationStoreTests`, `ConfigurationBackupArchiveTests`, `PluginApiContractTests`, and both non-live integration methods. These two methods overlap the native run; do not add the passing counts together. |
| Initial selected TFC preflight, before credentials were supplied | **One intended live row, Blocked / Skipped**: `Fill in both steamUser and steamPass in steam-credentials.txt before enabling live game tests.` The method expanded to 57 rows: TFC stopped at credentials preflight and the other **56** were deliberately excluded by the exact game selector. Zero lifecycle operations, passes, or failures. A zero runner exit code with all rows skipped is not a successful live test. |
| First TFC live attempt after the user saved credentials | **1 Failed, 0 Passed, 56 selector-excluded rows**; duration `00:04:11.7563750`. The suite completed shared binary install, normal update, validation update and config backup/restore, then entered launch. Shutdown remained alive beyond 30 seconds, so the harness force-terminated the process. |
| Unchanged TFC live retry requested by the user | **1 Failed, 0 Passed, 56 selector-excluded rows**; duration `00:03:50.7706336`. The same stages completed and the exact same shutdown timeout recurred. Code, assembly hashes, selector, root and timeout policy were unchanged. |
| Remaining-game live rerun (56 built-ins plus two V2 examples) | **51 serial invocations; 50 TRXs with one selected row each; 17 Passed, 32 Failed, 8 Blocked, 1 Aborted.** Seven explicit prerequisite rows were not run; the selected Mindustry row failed on missing Java 17+; Space Engineers exited without writing a TRX. See the per-game table below. |
| Focused GoldSrc shutdown retest on `fix/goldsrc-graceful-shutdown` | **7 selected rows, 7 Passed, 0 Failed** in the explicitly configured `EmbedConsole=false` mode: CS, CSCZ, DOD, DMC, HLOF, RCC and TFC. Each TRX has one selected row and 56 exact-selector exclusions. These smoke results supersede the earlier GoldSrc shutdown timeouts for this configured mode; see [GoldSrc follow-up](#goldsrc-shutdown-follow-up). |
| GoldSrc stop unit tests | **2 Passed, 0 Failed:** redirected stdin receives `quit`; a non-redirected process with no usable window fails explicitly instead of silently accepting a no-op. |
| Game install/update/launch qualification | The seven GoldSrc rows now pass the existing install/update/validation/config/15-second launch/stop smoke oracle in the corrected non-embedded mode. The earlier 26 non-GoldSrc failures, seven explicit preflight blocks, Mindustry's missing Java runtime, and Space Engineers' no-TRX abort remain unresolved or untested after HAR-01 was fixed. Readiness/query state, save/restart integrity, embedded-mode live behavior, and concurrent-instance isolation remain unqualified. No mutation experiments were applied. |
| SteamCMD setup requested during the audit | Official SteamCMD is installed on **C:** under `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\bin\steamcmd`; the user previously reported completing interactive login. That interactive process subsequently closed. The user-level `CDOGS_GAME_INTEGRATION_ROOT` remains set to the C-drive root. No credentials or Steam Guard codes were read into this report. |
| Authentication boundary | The user filled and saved the git-ignored credentials file locally; TFC now passes the harness's credential parser. TFC requests **anonymous** SteamCMD login, so this does not prove account authentication, entitlements or licensed-game access. Interactive SteamCMD login still does not replace the file. No credential contents, raw login command lines or Guard codes are included here; do not extract secrets from a console, cache or process. |
| Historical pilot | The [tester checklist](ALPHA-TESTER-CHECKLIST.md) records a tester-reported TFC shared-install pilot with update still open. It is not an independently reproduced result for this baseline or any other game. |

All **59 distinct built-in/example IDs** have at least one live attempt or an explicit prerequisite block: the 58 non-TFC rows in the original results table and TFC. Seven non-TFC rows were blocked before a test invocation; Space Engineers was invoked but produced no TRX. The seven GoldSrc rows were subsequently retested and passed their configured-mode smoke checks; the two earlier TFC failures are preserved as historical evidence. Selector-excluded rows are not additional game attempts.

Resumption target: `net10.0-windows10.0.19041`, Debug test assemblies on Windows `10.0.26200.0`. The tests exercise managed adapters, not the published application's UI. Raw TRXs and runner logs remain local outside the repository; the checked-in results below contain only summarized, sanitized fields. Do not share or commit raw logs without reviewing them for private data:

- Local baseline: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-213634-sdk-resume\alpha-local-baseline.trx` (38 passing local tests).
- Selected TFC preflight: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-214652-tfc-preflight\alpha-tfc-preflight.trx` (one credential-blocked row, 56 selector exclusions). The 59 skipped native live rows are a separate run, not the count in this artifact.
- First TFC live failure: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-220129-tfc-live\alpha-tfc-live.trx` (one failed TFC execution, 56 selector exclusions).
- Unchanged TFC retry failure: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-221820-tfc-retry\alpha-tfc-retry.trx` (one failed TFC execution, 56 selector exclusions). The first artifact was retained, not overwritten.
- First remaining-game batch: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-remaining-games-live\summary.json` (preserves 31 selected TRXs; its runner stopped during NMRIH cleanup).
- Completed remaining-game resume: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261025-remaining-games-resume\summary.json` (all 58 IDs, 51 invocations, 50 TRXs, and the Space Engineers no-TRX abort).
- Delayed NMRIH cleanup confirmation: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261024-remaining-games-live\nmrih-cleanup-final.json` (the exact residual directory was removed after PID 7296 disappeared; no process was killed by name).
- SHA-256 of assemblies used for the saved local baseline and all live runs: `CdogsWindowsGSM.Tests.dll` = `7FDD748F072B8E08A4AB1C52E0952F28A3A04E6004FCD5EB77B5B916D8AB45A6`; `CdogsWindowsGSM.dll` = `1E708AF384D5867BCC5ECAD983F2458420180E6D5F1900F2E2501FAE1EA0144D`.
- G-CS focused retest: `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261025-goldsrc-shutdown-probe-2\G-CS\goldsrc-smoke.trx` and `summary.json`.
- Six-row GoldSrc continuation (CSCZ, DOD, DMC, HLOF, RCC, TFC): `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results\20261025-goldsrc-shutdown-confirmation\summary.json` and per-ID `goldsrc-smoke.trx` files. Authentication-capable TRXs/logs remain private outside Git.
- SHA-256 of assemblies used by the GoldSrc implementation retest: `CdogsWindowsGSM.Tests.dll` = `7FCBAA3B1EB5C9113FA07B236CFD8056DAF7D8731D4DC56695FBE9988C61C21C`; `CdogsWindowsGSM.dll` = `8EB8FEBA2145D09233239950219F0FC6A006D64E8F8F4A8B805BE04B73D2E5CE`.

### Live TFC failure evidence

Both post-credentials attempts selected `Team Fortress Classic Dedicated Server` only and exercised the shared client path at `BuiltInGameIntegrationTests.cs:395-455`. Their test output records completion of `InstallSharedBinaries`, normal `Update`, `Update` with validation, and the configuration backup/restore round trip. Both then record launch entry, the same failure below, and forced termination; neither records the final launch/stop success message. Do not infer ready state, a successful 15-second launch test, correct map/config consumption or saved state from launch entry alone.

```text
'Team Fortress Classic Dedicated Server' remained alive after Stop(Process) for 00:00:30..
```

The failure is reported by `StopServerAsync` (`873-937`, throw at `936`), reached through the launch helper (`839-870`). Both runs report deleting the owner, client and shared installation. After the retry, the owner/client server directories, their per-server backup/log directories and the transient `bin\steamcmd\userData.txt` copy were verified absent; no visible SteamCMD/HLDS process under the integration root remained. The user-supplied ignored credentials file and non-secret test artifacts were retained. Both live runs used the assembly hashes recorded above. Game build/depot IDs were not captured before cleanup, so do not claim a pinned game build or full qualification.

**Follow-up: HAR-01 is resolved for the configured non-embedded test mode.** The original two TFC timeouts were reproduced while the fixture accidentally left GoldSource at its embedded-console default. After aligning the adapter with `ServerConfig.EmbedConsole`, TFC and the six other GoldSrc rows passed the selected install/update/validation/config/launch/stop smoke test; see [GoldSrc shutdown follow-up](#goldsrc-shutdown-follow-up). G-TFC remains open for QUAL-01 readiness/query and save/restart evidence, SH-04 update/recovery evidence, and ENG-03 build identity. The embedded mode received a redirected-stdin unit test but has not been live-qualified. A smoke pass does not establish graceful save, map/config consumption, or alpha support.

To reproduce from the repository root on disposable C-drive instances, using locally supplied credentials and existing built assemblies:

```powershell
$env:CDOGS_GAME_INTEGRATION_ROOT = Join-Path $env:LOCALAPPDATA 'CdogsWindowsGSM\GameIntegration'
$env:CDOGS_GAME_INTEGRATION_GAMES = 'Team Fortress Classic Dedicated Server'
$env:CDOGS_STEAM_CREDENTIALS_FILE = Join-Path (Get-Location) 'CdogsWindowsGSM.Tests\steam-credentials.txt'
$results = Join-Path $env:CDOGS_GAME_INTEGRATION_ROOT ('audit-results\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-tfc-repro')
dotnet test CdogsWindowsGSM.Tests\CdogsWindowsGSM.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName=CdogsWindowsGSM.Tests.BuiltInGameIntegrationTests.InstallUpdateVerifyLaunchAndRestoreWorkForBuiltInGame' --logger 'trx;LogFileName=tfc-repro.trx' --results-directory $results
```

The [21 October built-in audit](BUILTIN-GAME-SERVER-AUDIT.md) is historical context. Its endpoint/player-count snapshots were not refreshed here. In particular, its old claim that the host update method always returns success no longer matches this baseline: `MainWindow.xaml.cs:3934-3975` returns `updateSucceeded`. Do not reopen that already-changed issue from the historical prose alone.

## Remaining live rerun results

The frozen candidate was `f83c0766c5b137203fb3d011708e8e182c034d78`; both Debug assembly hashes are recorded above. The first C-drive, serial batch started at `2026-10-24T22:58:07Z` and stopped at `G-NMRIH` after 31 selected invocations. NMRIH failed its launch smoke window; harness cleanup then reported access denied for `datacache.dll`, so the runner stopped and marked only the 20 never-started rows for continuation. It did not classify those 20 as game failures. The residual NMRIH process/directory were handled separately: after the exact PID disappeared, the exact server directory was removed. The initial runner-failure summary and later cleanup confirmation are both retained locally.

The continuation started at `2026-10-25T18:04:54Z` and completed at `2026-10-25T20:07:05Z`. It carried forward all 31 prior outcomes unchanged and invoked only the 20 previously unattempted, eligible rows, each in a fresh C-drive root. The final summary contains all 58 remaining IDs. Verification found 51 selected test invocations, 50 TRXs each with exactly one selected result and the expected selector exclusions, and one Space Engineers invocation with no TRX. Built-in TRXs contain 56 intentionally excluded rows; each plugin TRX contains one. Selector exclusions are not test attempts.

| Final status for the 58 remaining IDs | Count | Interpretation |
|---|---:|---|
| SuitePassed | 17 | The selected row passed the existing install/update/validation/configuration/15-second launch/stop smoke oracle. This is not alpha qualification. |
| Failed | 32 | The selected row failed; the phase and stable classification are in the game table below. |
| Blocked | 8 | Seven were not invoked because required consent/configuration was absent; Mindustry's selected row failed because Java 17 or newer was unavailable. |
| Aborted | 1 | Space Engineers (`G-SE`) returned process exit `0xC000013A` without a TRX. Cause is undetermined; this is not a pass or a demonstrated game failure. |

The seven not-run prerequisites were explicit and were not bypassed: Minecraft Java EULA consent (`G-MC`, `G-PaperMC`, `G-ForgeMC`); Minecraft Bedrock EULA/privacy consent (`G-MCBE`); a CFX license key the harness cannot supply (`G-GTA5`, `G-RedM`); and an operator-configured SDK2013 mod/map (`G-SDK2013`). The additional Mindustry block is a runtime prerequisite failure, not a missing selector. No license key, EULA acceptance, mod, or map was fabricated.

The table records nonzero Steam appmanifest build IDs observed while each case was running, when available. They are metadata observations, not a pinned candidate, proof of successful validation, or a qualification claim; `-` means no ID was captured. SuitePassed reflects only the existing test oracle described above. Every source-review/work item remains open until its additional readiness, effective-settings, recovery, save/restart, console-mode, and (where applicable) shared-isolation gates pass.

| Work ID | Result | Phase / stable reason | Duration | Manifest build ID(s) observed |
|---|---|---|---:|---|
| G-ARKSE | Failed | Launch / ExitedDuringStartup | 00:36:29.7859215 | 21241282 |
| G-AVORION | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:00:45.8608744 | 22295362 |
| G-BT | Failed | Stop / ShutdownTimeout30Seconds | 00:01:18.4413728 | 23799137 |
| G-BW | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:33.8812344 | 5059939 |
| G-CE | Failed | Stop / ShutdownTimeout30Seconds | 00:03:51.5084824 | 25792439 |
| G-COD1 | Failed | Install/configuration / TestAssertionFailed | 00:00:06.0857000 | - |
| G-COD4 | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:03:12.1437737 | 2737681 |
| G-CS | Failed | Stop / ShutdownTimeout30Seconds | 00:03:05.9957090 | 15961496 |
| G-CSCZ | Failed | Stop / ShutdownTimeout30Seconds | 00:03:40.2125822 | 15961496 |
| G-CSGO | Failed | Stop / ShutdownTimeout30Seconds | 00:06:48.2940687 | 12426148 |
| G-CSS | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:10.1638572 | 25338297 |
| G-DAYZ | Failed | Launch / ExitedDuringStartup | 00:01:31.5579236 | 25963285 |
| G-DMC | Failed | Stop / ShutdownTimeout30Seconds | 00:02:51.3343168 | 15961496 |
| G-DOD | Failed | Stop / ShutdownTimeout30Seconds | 00:04:12.5389002 | 15961496 |
| G-DODS | Failed | Launch / ExitedDuringStartup | 00:01:10.7545843 | 25338204 |
| G-ECO | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:55.3818304 | 25987282 |
| G-EGS | Failed | Launch/stop / TestAssertionFailed | 00:05:07.1828248 | 25490402 |
| G-ForgeMC | Blocked (not run) | Preflight / ExplicitMinecraftEulaConsentRequired | - | - |
| G-GMOD | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:43.3947878 | 25375525 |
| G-GTA5 | Blocked (not run) | Preflight / HarnessDoesNotSupplyCfxLicenseKey | - | - |
| G-HEAT | Failed | Launch / ExitedDuringStartup | 00:03:35.8887898 | 6929872 |
| G-HL2DM | Failed | Stop / ShutdownTimeout30Seconds | 00:02:39.7504722 | 24600426 |
| G-HLOF | Failed | Stop / ShutdownTimeout30Seconds | 00:03:42.4947333 | 15961496 |
| G-INS | Failed | Launch / ExitedDuringStartup | 00:02:43.7056673 | 20000984 |
| G-INSS | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:02:15.0949270 | 25534773 |
| G-L4D2 | Failed | Launch / ExitedDuringStartup | 00:06:03.4415656 | 23990100 |
| G-MC | Blocked (not run) | Preflight / ExplicitMinecraftEulaConsentRequired | - | - |
| G-MCBE | Blocked (not run) | Preflight / ExplicitMinecraftEulaAndPrivacyConsentRequired | - | - |
| G-MCPE | Failed | Install/configuration / AdapterReportedError | 00:00:00.4659386 | - |
| G-Mindustry | Blocked (selected failure) | Runtime prerequisites / JavaRuntimePrerequisite | 00:00:01.2309170 | - |
| G-MORDHAU | Failed | Launch / ExitedDuringStartup | 00:01:49.3616866 | 16147292 |
| G-NMRIH | Failed | Launch / ExitedDuringStartup | 00:03:03.3106665 | 24996675 |
| G-OLOW | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:02:23.5270412 | 5113531 |
| G-ONSET | Failed | Launch / ExitedDuringStartup | 00:00:49.0169784 | 8141104 |
| G-OpenRA | Failed | Install/configuration / AdapterReportedError | 00:00:08.2740370 | - |
| G-OpenTTD | Failed | Install/configuration / Http403 | 00:00:00.3677114 | - |
| G-PaperMC | Blocked (not run) | Preflight / ExplicitMinecraftEulaConsentRequired | - | - |
| G-PS | Failed | Launch / ExitedDuringStartup | 00:07:58.9196336 | 21989442 |
| G-RCC | Failed | Stop / ShutdownTimeout30Seconds | 00:02:54.3115904 | 15961496 |
| G-RCT2 | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:00:46.1697152 | - |
| G-RedM | Blocked (not run) | Preflight / HarnessDoesNotSupplyCfxLicenseKey | - | - |
| G-ROK | Failed | Launch/stop / TestAssertionFailed | 00:00:59.4161464 | 4088734 |
| G-ROR2 | Failed | Install validation / InstallValidationFailed | 00:00:10.9787099 | 20243729 |
| G-RUST | Failed | Stop / ShutdownTimeout30Seconds | 00:02:41.6412168 | 26139947 |
| G-SCUM | Failed | Launch / StartReturnedNoProcess | 00:06:55.6751087 | 25803875 |
| G-SDK2013 | Blocked (not run) | Preflight / SdkModAndMapNotConfigured | - | - |
| G-SDTD | Failed | Stop / ShutdownTimeout30Seconds | 00:06:27.5305204 | 25661908 |
| G-SE | Aborted (no TRX) | Test host / RunnerExitedWithoutTrx (`0xC000013A`) | 00:06:57.7096734 | 24675709 |
| G-Soldat | Failed | Launch / ExitedDuringStartup | 00:00:26.7136501 | - |
| G-SQ | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:11:29.7270869 | 26005357 |
| G-SW | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:00:38.4533030 | 14768962 |
| G-TF | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:17.6912234 | 3488796 |
| G-TF2 | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:04:13.8337090 | 25995664 |
| G-UNT | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:03:29.0442170 | 25613089 |
| G-VTS | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:20.1757686 | - |
| G-ZPS | Failed | Launch / ExitedDuringStartup | 00:02:05.8441239 | 20602753 |
| P-CS16StandaloneV2 | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:02:37.8101860 | 15961496 |
| P-GarrysModV2 | SuitePassed | Completed / ExistingLifecycleOraclePassed | 00:01:51.9361091 | 25375525 |

After the continuation, verification found no live process under any owned case root, no residual server/backup/log directories, no active shared SteamCMD process, and no transient `userData.txt` copy. This is cleanup evidence only; it does not convert a failed or aborted test into a pass.

### GoldSrc shutdown follow-up

**Root cause of the misleading shutdown evidence:** the integration fixture set `ServerConfig.EmbedConsole=false` but did not apply it to the adapter's public `AllowsEmbedConsole` member. GoldSrc therefore launched with its constructor default (`true`): stdout/stderr were redirected, stdin was not, and `Stop` posted `quit` characters to `MainWindowHandle`. This was not the configured non-embedded mode that the test claimed to exercise. The original TFC and six other GoldSrc shutdown timeouts remain valid records of that mismatched test path, but did not demonstrate a timeout in the host's configured non-embedded mode.

On branch `fix/goldsrc-graceful-shutdown` (commit `4c5eec740f689695d2279a9e5bbafa8a89f62f99`), the integration helper now applies and asserts the configured mode before `Start`. GoldSource also redirects stdin in its embedded mode and sends `quit` through that pipe; when stdin is not redirected, it now requires a usable console window rather than silently posting to an absent one. Two focused `GoldSourceStopTests` passed: one verifies `quit` reaches a redirected process, and one verifies a no-window/no-stdin process surfaces an error. **The seven live retests below used `EmbedConsole=false`; embedded-console game execution has not been claimed or live-verified.**

All seven inherited GoldSrc rows were run serially in fresh C-drive roots on the same built test/production assemblies. Every invocation produced exactly one selected `Passed` row and 56 expected selector exclusions; no appmanifest build ID was captured. Post-run checks found no owned game processes or residual server directories, no active shared SteamCMD, and no transient credential copy.

| Work ID | Selected outcome | Duration | Sanitized TRX (relative to `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration\audit-results`) |
|---|---|---:|---|
| G-CS | Passed | 00:02:47.8765011 | `20261025-goldsrc-shutdown-probe-2\G-CS\goldsrc-smoke.trx` |
| G-CSCZ | Passed | 00:03:20.9331859 | `20261025-goldsrc-shutdown-confirmation\G-CSCZ\goldsrc-smoke.trx` |
| G-DOD | Passed | 00:03:10.8984670 | `20261025-goldsrc-shutdown-confirmation\G-DOD\goldsrc-smoke.trx` |
| G-DMC | Passed | 00:02:40.1407811 | `20261025-goldsrc-shutdown-confirmation\G-DMC\goldsrc-smoke.trx` |
| G-HLOF | Passed | 00:03:12.6307726 | `20261025-goldsrc-shutdown-confirmation\G-HLOF\goldsrc-smoke.trx` |
| G-RCC | Passed | 00:02:41.2114009 | `20261025-goldsrc-shutdown-confirmation\G-RCC\goldsrc-smoke.trx` |
| G-TFC | Passed | 00:02:56.2751495 | `20261025-goldsrc-shutdown-confirmation\G-TFC\goldsrc-smoke.trx` |

These outcomes supersede the earlier shutdown timeouts **only for the configured non-embedded smoke mode**. They do not prove ready/query behavior, map/config consumption, save/restart, update recovery, shared-instance isolation, or alpha qualification. The other historical failures and preflight blocks have not been retested on this branch.

## Scope, terminology, and priorities

Authoritative sources:

- Factory inventory: `CdogsWindowsGSM\GameServer\Data\Class.cs:12-68` (57 entries).
- Built-in data discovery: `CdogsWindowsGSM.Tests\BuiltInGameIntegrationTests.cs:185-191,1336-1359`.
- Plugin data: the same file, `193-197`: `GarrysModV2` and `CS16StandaloneV2`.
- Capability gates and backup scopes: `CdogsWindowsGSM\Functions\BuiltInGameCapabilities.cs:9-101,120-207`.
- Release expectations: [Alpha roadmap](MULTI-INSTANCE-ALPHA-ROADMAP.md) and [tester checklist](ALPHA-TESTER-CHECKLIST.md).

`S+B` means the built-in is selectable for shared installation and has a config-only backup plan; `P+B` means private-only with a config-only plan; `P` means private-only, with full-server backup remaining the host default. These are implementation gates, not runtime approvals. There are **19 S+B, 1 P+B (Rust), and 37 P built-ins**. Plugin method capabilities are recorded separately from host eligibility: all plugins remain private-only in the UI (`MainWindow.xaml.cs:2636-2646,2679-2703`).

Priorities are work order, not a claim that a hypothetical failure happened:

- **P0:** resolve before relying on qualification results or shipping a known destructive path. An explicit maintainer decision to gate an integration is an alternative to repairing it; do not silently remove support.
- **P1:** resolve or explicitly gate/document before claiming the affected lifecycle/capability works in alpha.
- **P2:** follow-up hardening/diagnostics; retain a documented limitation if deferred.

Findings marked **Source defect** follow directly from current code. **Qualification gap** means the required runtime observation is absent, not that the game failed. **Environment blocker** is not counted as a repository bug. Task IDs remain **Open** except **ENV-01 and HAR-01**, now Resolved for the documented build and configured non-embedded test mode respectively; per-game qualification entries are separate from shared root-cause findings and must not be double-counted as independent bugs.

### Recommended claim order

| Order | Work | Why it precedes broad live runs |
|---|---|---|
| 1 | ENV-01 and HAR-01 (resolved); HAR-02/03 | Re-establish the executable baseline on each candidate, preserve the selected console mode, bound whole operations and preflight the selected game's real requirements. The seven GoldSrc rows pass the corrected non-embedded smoke mode; other rows still need reruns after harness-fidelity changes. |
| 2 | CORE-01; CUSTOM-01/02; ENG-07 | Remove known configuration/runtime/recovery-copy destruction risks before applying regeneration, update or fallback to valuable state. |
| 3 | CUSTOM-03/12; CUSTOM-04; ENG-08 | Make shared build transitions, Bedrock pack ownership and runtime publication coherent and recoverable. |
| 4 | HOST-01/02/03; STEAM-01/05/06 | Prevent false install success, hidden update retry, stuck startup, wrong prerequisite approval, ineffective shutdown and orphan/unverified processes. |
| 5 | CUSTOM-06/07; CORE-02/03; ENG-03/05 | Fix runtime/consent, build/account identity/parsing and query protocol prerequisites needed for meaningful qualification. |
| 6 | STEAM-04/07; QUAL-01/02/03/04/05 | Prove supported save/stop modes and private/shared runtime/backup/update behavior. Finish TFC SH-04 and the GoldSrc rows' remaining readiness/restart checks; do not infer family alpha qualification from the smoke retest. |
| 7 | Remaining P2 rows, per-game effective settings/package checks | Close narrower correctness gaps or obtain explicit maintainer deferrals before recording the exact alpha support matrix. |

Several tasks touch the same files: CORE-01/HOST-01, CORE-02/03, and CUSTOM-02/03/12 must be coordinated rather than implemented by parallel AIs making incompatible edits. Independent per-game qualification can run in parallel only with separate owned roots and nonconflicting port sets.

## Test inventory and oracle review

Four declared integration-test methods expand to **61 cases**, confirmed by the native run: 57 built-in live rows, two plugin live rows, one catalog test, and one local plugin-backup test. In that initial pre-credentials native run, the two local cases passed and 59 live rows were skipped before lifecycle operations; the later selected live evidence is recorded in [the rerun table](#remaining-live-rerun-results). The implementation branch adds two focused GoldSrc stop unit tests; latest Test Explorer discovery found 105 tests total.

| Method in `BuiltInGameIntegrationTests.cs` | Cases | What the existing oracle actually verifies | Boundary still open |
|---|---:|---|---|
| `EveryBuiltInGameHasAnInstallUpdateAndStartOperation` (`199-217`) | 1, looping the catalog | Factory construction and public install/update/start/stop operation presence. Appropriate contract smoke checks, not lifecycle execution. | Raw-name uniqueness is lost before the uniqueness assertion; see HAR-04. |
| `InstallUpdateVerifyLaunchAndRestoreWorkForBuiltInGame` (`219-251`) | 57 live | Installer process exit/error and install validator; update and optional Steam validation; generated config; approved backup round trip where enabled; process survives 15 seconds; adapter stop. | No readiness query, restart/save reload, private-plus-shared matrix, two-running-instance isolation, or failed-update recovery. See QUAL-01 through QUAL-04. |
| `InstallUpdateVerifyLaunchAndV2CapabilitiesWorkForPluginExample` (`253-292`) | 2 live | V2 recognition plus backup capability; direct provider lifecycle; Garry's Mod also checks distinct owner/client config text (`520-531`). | A direct provider call is not proof the UI permits plugin sharing. No plugin shared-install allowlist change is authorized. |
| `V2PluginExamplesRoundTripTheirApprovedConfigurationBackupsThroughTheHost` (`294-371`) | 1, looping both examples | Loads/compiles both fixture plugins, creates local config, validates host-approved paths, and restores changed bytes. Does not need game binaries or Steam login. | Does not launch either game or qualify shared runtime behavior. |

The two lifecycle methods assert through shared helpers; they are **not assertion-free**. Of the 53 assertion/control-call sites in this file, four are `Assert.Inconclusive` prerequisite/selection exits, not successful checks. Null guards precede meaningful checks, public reflection matches the repository's dynamic adapter contract, and byte-for-byte backup round trips are valid state assertions, not tautologies. No cosmetic naming or forced assertion-diversity rewrite is requested.

Test-harness anti-pattern summary, separate from game defects and qualification gaps: **0 Critical, 1 High (HAR-02), 2 Medium (HAR-04/HAR-05), 0 Low**. HAR-01's mode-fidelity defect is resolved on the implementation branch; HAR-03 is prerequisite/setup work, not an assertion-free-test finding. The positive protections and the precise limitations of all four methods are accounted for above and below.

### Cleared suspicious constructs and existing protections

- **The launch message is not an eager `ExitCode` defect.** At `860-862`, `Assert.IsFalse(process.HasExited, $"...{process.ExitCode}...")` uses MSTest 4.0.2's conditional interpolated-string handler with this project's modern C# target. The message expressions run only when the assertion fails (the process has exited). This was checked against the [exact v4.0.2 framework source](https://github.com/microsoft/testfx/blob/v4.0.2/src/TestFramework/TestFramework/Assertions/Assert.IsTrue.cs). Do not file or "fix" it based on older MSTest string-overload semantics.
- Configuration helpers await `Task` operations and explicitly track `async void` completion/errors (`663-718,1574-1686`). The synchronization-context exception capture propagates failure; it is not an empty catch that turns an exception into a pass.
- Process installers have a 90-minute exit timeout and nonzero-exit assertion (`977-994`); config generation has a five-minute wait; stop has a 30-second wait and fails when the harness must force-kill (`873-937`). HAR-02 identifies the waits those limits do not cover.
- Cleanup aggregates failures and throws (`35-183`); it retains shared installations referenced by another server. The root is outside the repository and overlapping roots are rejected (`1384-1401`). `ServerPath` uses a scoped `AsyncLocal` override (`Functions\ServerPath.cs:10-11,48-79`), not a single process-global mutable root.
- Existing unit tests already protect important behavior: `BuiltInGameCapabilitiesTests.cs:38-109,149-209,213-378` pins gates/scopes and Rust rejection; `SharedInstallationStoreTests.cs:45-177` covers reuse, locks, ownership, reference lifetime, and legacy roots; `ConfigurationBackupArchiveTests.cs:37-142,217-406` checks exact paths/bytes, non-target files, identity/build mismatch, corruption, traversal, and missing roots; `PluginApiContractTests.cs:15-214` checks typed capabilities and rejection paths. QUAL tasks concern real game behavior beyond those fixture checks, not an assertion that the unit protection is absent.

## Shared harness and host work

### Environment blocker ENV-01 - restore an executable baseline

**Resolved on 24 October 2026.** The original `NETSDK1141` is cleared: installed stable .NET SDK **10.0.402** now resolves the existing `global.json` policy. The first retry reported `NETSDK1004`; `dotnet restore CdogsWindowsGSM.Tests\CdogsWindowsGSM.Tests.csproj` restored the existing dependencies. The next build exposed `MSB3027` because `CdogsWindowsGSM\CdogsWindowsGSM.csproj` hard-coded Debug `BaseOutputPath` to the unavailable `E:\CdogsWindowsGSM\bin\`.

Commit `4ebcb51` removes that single machine-specific override instead of hard-coding another machine's path. MSBuild now evaluates `BaseOutputPath` as `bin\` and the Debug target under this checkout's **C:** drive. No SDK policy, package versions, game implementation, or test implementation was changed.

Acceptance satisfied on the original audit candidate: native project build passes; Test Explorer discovered 103 tests; the catalog and local plugin-backup methods both passed. The separate saved 38-test local baseline also passed. **HAR-03 remains Open:** its game-specific prerequisite model has not changed. See the implementation-branch GoldSrc retest below; passing a smoke row does not qualify a game.

### HAR-01 - test the console mode the host actually selects

**Resolved for the configured smoke mode on 25 October 2026.** The suite sets `ServerConfig.EmbedConsole = false` (`1056-1097`, specifically `1074`) and now applies that value to an adapter's public Boolean `AllowsEmbedConsole` field/property immediately before `Start`, asserting the effective value. Read-only adapter declarations must already match the configured mode; the helper does not silently substitute an unsupported value. This mirrors the host's assignment immediately before start (`MainWindow.xaml.cs:3533-3534`). The seven GoldSrc rows passed in the corrected false mode. A live embedded-true game run remains unqualified; the GoldSource embedded input path is covered only by a focused unit test.

Acceptance satisfied for the existing `EmbedConsole=false` test mode. The focused GoldSrc regression checks cover redirected-stdin command delivery and explicit failure when neither stdin nor a usable window is available. Add live true-mode runs before claiming embedded-mode qualification; preserve adapters that intentionally disallow embedding.

### HAR-02 - bound the whole operation, not just a returned process

**P1 / Source defect (unbounded waits).** `await operation()` (`636-649`) and `await startTask` (`844-846`) have no outer deadline. `ProcessTimeout` starts only after an installer task has returned a process (`651-655,977-989`). Download/EULA/Java/Steam Guard/child-discovery work inside an adapter can therefore prevent the test from ever reaching that timeout. Synchronous `Stop` invocation is also outside the returned-task timeout (`887-892`).

Acceptance: separately bound operation acquisition, process execution, launch/readiness, and shutdown; propagate cancellation where supported and explicitly account for processes created before failure. A never-completing acquisition must fail at its configured deadline, not remain running indefinitely; cleanup must identify and stop only this case's processes. A timeout must remain a failure even if cleanup succeeds. Retain the existing 90-minute installer, five-minute config, and 30-second shutdown policies unless deliberately revised and documented.

### HAR-03 - use game-specific prerequisites and a noninteractive preflight

**P1 / Harness limitation (prerequisite model).** Both live entry points unconditionally load Steam credentials (`223-233,257-267`), including non-Steam games and anonymous-only adapters. Test configuration also clears the CFX license key and GSLT (`1071-1073`), so signing into Steam cannot supply the FiveM/RedM prerequisite. `TryGetSettings` requires a discoverable test-project directory even when an explicit credentials override is supplied (`1377-1382,1403-1407`).

Acceptance: declare prerequisites per selected game; separate any deliberate live-run authorization gate from Steam authentication. Do not accidentally make default CI download games simply by removing the credential check. Missing CFX key, licensed data, Java/PHP/runtime, or EULA consent must produce a precise **Blocked/Inconclusive** preflight result before downloads/launch, never a success. Support the documented explicit credential path outside a repository checkout or correct that promise. Keep credentials/Guard entry outside chat and reports; do not silently consent to an EULA. Any future cached-Steam-login support must be explicit and tested, not inferred from the interactive session used here.

### HAR-04 - check the raw catalog before deduplicating it

**P2 / Source defect (ineffective assertion).** `GetBuiltInGameKeys` calls `Distinct` (`1343`) before the catalog test compares the result's length with its distinct count (`204`). Two adapter types declaring the same `FullName` are coalesced; that particular assertion cannot detect the defect it names.

Acceptance: inspect raw discovered names first, reject duplicate names case-insensitively with the offending types named, then build rows. Compare raw adapter inventory/factory coverage without silently losing an entry. A fixture with two identical names must fail uniqueness, while each of the 57 current factory entries has exactly one built-in row. Keep the independent operation-presence checks.

### HAR-05 - isolate a run, not only a server role

**P2 / Source reliability risk.** The default root is reused (`1384-1395`) and server IDs depend only on role/game (`1458-1466`). `[DoNotParallelize]` protects one test execution, not another Visual Studio/CLI process using that root. Interrupted runs can also leave the same IDs and shared state for a later run. Port checks release their sockets immediately (`1099-1150`).

Acceptance: give each run an owned subdirectory/run identity or an exclusive root lease; refuse to delete/adopt another run's state. Cover interrupted-run recovery and two processes targeting the same base root. Allocate distinct owner/client game, query, and auxiliary ports immediately before launch; verify the actual bound endpoints. Keep the existing repository-overlap rejection and reference-aware cleanup.

### HOST-01 - do not report successful install after config failure

**P1 / Source defect.** The real install UI invokes most `CreateServerCFG` implementations without awaiting and wraps config generation in a catch that ignores the exception (`MainWindow.xaml.cs:1864-1889`), then logs `Install: Success` (`1891-1895`). The test helper has more completion/error handling than the production flow (`BuiltInGameIntegrationTests.cs:663-718`), so passing it would not cover this UI behavior.

Acceptance: preserve the adapter compatibility contract while awaiting/tracking completion, validate required generated files, and surface the specific error in the existing UI/log path. A config-write failure must not log install success or leave an apparently launch-ready server. Verify both Task-returning and legacy async-void adapters, plus normal successful installation.

### HOST-02 - do not retry a real update exception as a different operation

**P1 / Source defect.** `Server_BeginUpdate` catches every exception from `gameServer.Update(validate, custum)` and invokes `Update()` (`MainWindow.xaml.cs:3733-3742`). This fallback is intended for signature compatibility but also catches a real adapter failure; it can repeat an already-started update and discard the validation/custom-branch request.

Acceptance: select the supported public overload before invocation, using existing signature/capability patterns. Preserve real runtime exceptions as failures and retain the requested validation/custom parameters where supported. A throwing compatible overload is invoked exactly once and must not cause a second normal update; a parameterless legacy adapter still updates normally. Recheck the current `updateSucceeded` return behavior, not the obsolete historical finding.

### HOST-03 - bound the UI's window-discovery loop

**P1 / Source defect.** `Server_BeginStart` loops while the process is alive and `ShowWindow` fails, without a wait or timeout (`MainWindow.xaml.cs:3556-3588`). A headless process without a usable window can keep the UI in Starting and spin a worker indefinitely. After this stage, a surviving process is marked Started without a game-ready observation (`3807-3826`).

Acceptance: bound/yield window discovery, tolerate intentional headless mode without busy-waiting, and distinguish process-started from game-ready/query health. A live process with `MainWindowHandle == IntPtr.Zero` must reach a defined state within the configured deadline; early exit must produce the original diagnostic and release only that instance's resources.

## Qualification gaps, not demonstrated game failures

These are source-to-observation gaps in the lifecycle suite. No mutation was executed and none is labeled a verified survivor. Do not duplicate the already-covered archive/store/API unit tests merely to increase test counts.

| ID / priority | Public outcome not qualified | Existing boundary / evidence | Required acceptance evidence |
|---|---|---|---|
| QUAL-01 / P1 | Installed server becomes ready, binds its assigned endpoints, and survives a save/restart | `RunLaunchSmokeTestAsync` (`839-871`) only asserts 15-second process liveness. A live but unusable server meets that predicate. `StopServerAsync` (`873-937`) detects harness fallback, not whether an adapter internally killed without saving. | Per-game bounded readiness (A2S/UT3/FiveM where supported, otherwise documented game-ready signal/client check), expected server name/map/ports, clean save and stop, restart with persisted state. Optional absence of a query protocol is not itself a defect. |
| QUAL-02 / P1 | Two shared instances run independently and updates/removal preserve the other instance | Built-in shared lifecycle (`395-455`) creates owner/client but launches only the client, and never removes the owner while proving the client remains usable. V2 equivalent (`457-536`) adds distinct config text, not concurrent runtime. | Run both with distinct ports/names/config/saves/logs; alter/restart/stop A while B remains healthy; record all runtime writes. Block updates while in use; update once with both stopped; verify both after success and supplied failure/recovery. Remove owner and client in both orders without losing referenced binaries. Use SH-01 through SH-05. |
| QUAL-03 / P1 | Private lifecycle still works for a shared-enabled built-in | Entry point chooses shared **or** standalone (`243-250`), never both for the same game. | Run a private case as well as the allowed shared case for all 19 S+B games. Existing private installations stay private; do not implement or improvise migration as part of qualification. |
| QUAL-04 / P1 | The actual game consumes every restored config file, and unrelated runtime state stays unchanged | Backup helper checks plan files exist, changes only `plan[0]`, compares preview to returned restore paths, and restores that file's bytes (`774-837`). Independent unit tests already pin archive scope and multi-file behavior. | On disposable game instances, change every declared file independently, verify the exact planned path set and all original bytes after restore, then start/query the game to show the restored config is used. Hash non-target saves, binaries, and the other instance's config before/after. Full backup/restore remains required for P-only games, not a missing config-only feature to invent. |
| QUAL-05 / P1 | A passing CI/build summary represents completed live qualification | CI runs the solution tests without an explicit live-results gate or test-result artifact (`.github\workflows\build.yml:20-21`); live methods use `Assert.Inconclusive` for missing credentials/selection (`223-233,257-267`). | Separate local/unit and controlled live lanes; publish sanitized TRX/log evidence and a per-game executed/passed/failed/blocked count. An all-skipped/unselected live lane cannot approve alpha. Do not put account secrets in CI logs. |

## Game-by-game ledger

Each row is an independent, open qualification work item. Shared finding IDs identify root causes once; completing one game does not close the same live checks for its siblings. The family ledgers below cover every factory entry and both plugin examples.

All game/example qualification work items remain **Open / source-reviewed**. The original 58-row candidate produced 17 smoke passes, 32 failures, eight blocks and one no-TRX abort; seven GoldSrc rows have since passed a focused retest in configured non-embedded mode. The other historical failures and blocks still need owner-specific follow-up. These smoke outcomes do not close readiness, restart, recovery, or sharing work. The two local V2 examples' fixture round trips passed, which is not live qualification. `A` means the adapter requests anonymous SteamCMD login; `C` means configured Steam account credentials are requested. Neither proves current entitlement. Evidence paths in these tables are relative to `CdogsWindowsGSM\` unless explicitly prefixed with another project. Runtime/native dependencies must be checked on a clean supported host, not inferred from a developer machine.

### Source, GoldSrc, and Call of Duty - 20 built-ins

Common engine evidence: `GameServer\Engine\Source.cs:33-53,95-213,215-330`; `GameServer\Engine\GoldSource.cs:20-65,106-223,225-336`. All 18 Source/GoldSrc titles need CORE-01/02, ENG-02/05 and QUAL-01. The 17 enabled shared Source/GoldSrc titles also need QUAL-02/03/04; SDK2013 stays private. These inherited findings are not 18 independent root causes.

The seven GoldSrc adapters share `Engine\GoldSource`. Their original stop timeouts were run with the wrong console mode due to HAR-01; after mode alignment, all seven passed the non-embedded smoke test. Keep their remaining per-game package/map/readiness/shared-mode evidence open.

| Work ID / key | Exact built-in selector (`FullName`) | Acquisition / special prerequisites | Gate | Specific next work beyond the common checks | Adapter evidence |
|---|---|---|---|---|---|
| G-CSGO / `CSGO` | Counter-Strike: Global Offensive Dedicated Server | Steam 740, A; legacy `csgo` content/launcher; public-auth token if that mode is claimed | S+B | Qualify the current 740 payload against `srcds.exe`, `de_dust2`, tickrate 64 and game mode defaults; explicitly retain a legacy-CSGO support claim, not an assumed CS2 integration. Verify instance cfg/mapcycle and A2S identity. | `GameServer\CSGO.cs:5-9` |
| G-GMOD / `GMOD` | Garry's Mod Dedicated Server | Steam 4020, A; `garrysmod` assets | S+B | ENG-09: omit the valueless workshop collection command for stock sandbox. Test `gm_construct`/sandbox separately from a supplied workshop collection and its writes. | `GameServer\GMOD.cs:5-9` |
| G-TF2 / `TF2` | Team Fortress 2 Dedicated Server | Steam 232250, A; `tf` assets | S+B | Check the actual Windows launcher/depot instead of assuming inherited `srcds.exe`; verify `cp_badlands`, effective player count, cfg/mapcycle and queried identity. | `GameServer\TF2.cs:5-8` |
| G-HL2DM / `HL2DM` | Half-Life 2: Deathmatch Dedicated Server | Steam 232370, A; `hl2mp` assets | S+B | Demonstrate `dm_runoff` and effective `+mp_teamplay 1`, plus independent config/mapcycle consumption in two instances. | `GameServer\HL2DM.cs:5-9` |
| G-L4D2 / `L4D2` | Left 4 Dead 2 Dedicated Server | Steam 222860, A; campaign content | S+B | Qualify `c1m1_hotel`, campaign/lobby behavior and the effective stock mode/player limit; inherited max players 24 is not evidence the game accepts it. | `GameServer\L4D2.cs:5-8`; `GameServer\Engine\Source.cs:41,126` |
| G-CSS / `CSS` | Counter-Strike: Source Dedicated Server | Steam 232330, A; `cstrike` assets | S+B | Verify qualified Windows launcher, `de_dust2`, tickrate and per-instance cfg/mapcycle; do not infer readiness from the generic Source launch returning a process. | `GameServer\CSS.cs:5-9` |
| G-INS / `INS` | Insurgency Dedicated Server | Steam 237410, A; `insurgency` assets | S+B | Preserve and test the intentional two-token map/mode value `market skirmish` and `-usercon`; verify effective mode through game/query evidence. | `GameServer\INS.cs:5-9`; `GameServer\Engine\Source.cs:140-141` |
| G-NMRIH / `NMRIH` | No More Room in Hell Dedicated Server | Steam 317670, A; `nmrih` assets | S+B | Check shipped `nmo_broadway`, effective player cap, A2S readiness and instance-specific cfg/mapcycle. | `GameServer\NMRIH.cs:5-8` |
| G-ZPS / `ZPS` | Zombie Panic Source Dedicated Server | Steam 17505, A; `zps` assets | S+B | Verify shipped `zps_cinema`, cfg/mapcycle selection and all runtime writes; no current map/package failure is claimed from source alone. | `GameServer\ZPS.cs:5-8` |
| G-DODS / `DODS` | Day of Defeat: Source Dedicated Server | Steam 232290, A; `dod` assets | S+B | Prove Windows launcher, `dod_anzio`, effective settings and A2S readiness for this exact title. | `GameServer\DODS.cs:5-8` |
| G-SDK2013 / `SDK2013` | Source SDK Base 2013 Dedicated Server | Steam 244310, A, plus operator-supplied SDK mod/map | P | Manual prerequisite work: `Game` is empty, map and `-game` are `<set me>`. Changing only launch parameters does not redirect cfg/map helpers. Require an explicit supported mod setup or clearly block this stock-data case; do not enable sharing. | `GameServer\SDK2013.cs:5-9`; `GameServer\Engine\Source.cs:51-53,105,133,232` |
| G-CS / `CS` | Counter-Strike: 1.6 Dedicated Server | Steam 90, A, mod `cstrike`; game identity 10 | S+B | ENG-03: track the installed app-90 build, not the client ID. Configured non-embedded live smoke **Passed** after HAR-01 was fixed. Verify `de_dust2`, mod assets and effective cfg/unique shared mapcycle. | `GameServer\CS.cs:5-8` |
| G-CSCZ / `CSCZ` | Counter-Strike: Condition Zero Dedicated Server | Steam 90, A, mod `czero`; identity 80 | S+B | ENG-03; configured non-embedded live smoke **Passed** after HAR-01 was fixed. Prove actual Condition Zero content/dependencies and `de_dust2`, not just the existence of a different HLDS mod or `hlds.exe`. | `GameServer\CSCZ.cs:5-8` |
| G-DOD / `DOD` | Day of Defeat Dedicated Server | Steam 90, A, mod `dod`; identity 30 | S+B | ENG-03; configured non-embedded live smoke **Passed** after HAR-01 was fixed. Check shipped `dod_Anzio` and effective mod config/mapcycle. Case spelling on Windows is not counted as a defect. | `GameServer\DOD.cs:5-8` |
| G-DMC / `DMC` | Deathmatch Classic Dedicated Server | Steam 90, A, mod `dmc`; identity 40 | S+B | ENG-03; configured non-embedded live smoke **Passed** after HAR-01 was fixed. Check suspicious default `dcdm5` against the downloaded BSP inventory and prove the chosen map loads. Its absence was not demonstrated in this audit. | `GameServer\DMC.cs:5-8` |
| G-HLOF / `HLOF` | Half-Life: Opposing Force Dedicated Server | Steam 90, A, mod `gearbox`; identity 50 | S+B | ENG-03; configured non-embedded live smoke **Passed** after HAR-01 was fixed. Prove Opposing Force dependencies, `op4_bootcamp`, cfg selection and unique mapcycle. | `GameServer\HLOF.cs:5-8` |
| G-RCC / `RCC` | Ricochet Dedicated Server | Steam 90, A, mod `ricochet`; identity 60 | S+B | ENG-03; configured non-embedded live smoke **Passed** after HAR-01 was fixed. Prove `rc_arena`, Ricochet identity and mod content under its app-90 installation. Same app ID alone does not authorize sharing across different games. | `GameServer\RCC.cs:5-8`; `GameServer\Engine\GoldSource.cs:117-149,252-253` |
| G-TFC / `TFC` | Team Fortress Classic Dedicated Server | Steam 90, A, mod `tfc`; identity 20 | S+B | Two original runs failed at shutdown in the harness's mismatched embedded mode; configured non-embedded live smoke **Passed** after HAR-01 was fixed. See [GoldSrc follow-up](#goldsrc-shutdown-follow-up). QUAL-01 still requires ready/query and save/restart evidence; finish SH-04's update/recovery evidence separately, and keep ENG-03 open. Prove `dustbowl`, TFC identity, and config/mapcycle consumption. No full lifecycle or family-wide qualification is claimed. | `GameServer\TFC.cs:5-8`; `GameServer\Engine\GoldSource.cs:130-149`; `CdogsWindowsGSM.Tests\BuiltInGameIntegrationTests.cs:839-937` |
| G-COD1 / `COD1` | Call of Duty 1 Dedicated Server | Steam 2620, C; CoDMP 1.5 and required PK3s; optional verified fallback/MeatBots | S+B | ENG-06/07 and CORE-02/03: honor selected map, preserve fallback/mod recovery files, fix build/account parsing. Qualify stock and MeatBots separately with `fs_basepath`/`fs_homepath`; query is not implemented, so use a documented ready/client observation. | `GameServer\COD1.cs:15-39,74-85,117-142,218-322,371-400,506-529` |
| G-COD4 / `COD4` | Call of Duty 4: Modern Warfare Dedicated Server (CoD4x) | Steam 7940, C; separately pinned CoD4x 21.2 and IWD/zone data | P | ENG-08 and CORE-03: preserve wrapper/stamp on failed licensed acquisition. Prove `+exec server.cfg` lookup, actual ready state, stdin `quit` in the default mode and nonembedded shutdown separately; query is not implemented. | `GameServer\COD4.cs:17-36,61-76,119-126,220-310,363-414` |

Private Source launches use `Environment.CurrentDirectory`, not the calculated instance root (`GameServer\Engine\Source.cs:104,151,172`). For all 11 Source rows, qualification must include launch from an unrelated directory and a path containing spaces. Establish an explicit working directory if that behavior is not reliable. In shared mode, redirecting selected cfg paths does not prove isolation of bans, logs, plugins, workshop/downloaded content or caches; inventory them under QUAL-02.

### Custom download/runtime games - 13 built-ins

All 13 use non-Steam acquisition; a Steam login is not their runtime prerequisite. Only MCBE is S+B. All still need QUAL-01, applicable backup evidence and fresh metadata/package fixtures; historical endpoint claims are not current outage evidence.

| Work ID / key | Exact built-in selector (`FullName`) | Acquisition / special prerequisites | Gate | Specific next work beyond the common checks | Source evidence |
|---|---|---|---|---|---|
| G-MCPE / `MCPE` | Minecraft: Pocket Edition Server (PocketMine-MP) | Independently selected PHP-8.0 x64 ZIP and Jenkins PHAR; PHP/PHAR compatibility and properties file | P | CUSTOM-01/11, CORE-01, SET-01: preserve PHAR/config, reject missing PHP imports, use requested player count. Recheck upstream support/endpoints and qualify the exact PHP/PHAR pair before retaining the alpha support claim; no fresh outage is asserted here. | `GameServer\MCPE.cs:17-18,34-72,134-228` |
| G-MC / `MC` | Minecraft: Java Edition Server | Mojang latest release JAR; Java and explicit EULA consent | P | CUSTOM-01/06/07/13, CORE-01, SET-01, PORT-01: staged update, compatible version-aware Java, preserve EULA refusal, apply requested world/player values, release build-log handles and parse supported version forms. | `GameServer\MC.cs:21,38-63,147-297,326-354`; `Functions\JavaHelper.cs:49-65,231-294` |
| G-PaperMC / `PaperMC` | Minecraft: Paper Server | Paper Fill v3 stable JAR; per-instance compatible Java from Adoptium; WPF EULA consent | P | CUSTOM-11, PORT-01: align accepted JAR imports with required build metadata. Retain staged/hash/manifest checks; qualify Fill schemas, Java selection, config consumption and both supported stop modes. | `GameServer\PaperMC.cs:14-45,48-103`; `GameServer\Engine\MinecraftJava.cs:72,89-118,129-216,234-271` |
| G-ForgeMC / `ForgeMC` | Minecraft: Forge Server | Forge promotions/Maven installer and checksum; compatible Java; WPF EULA consent | P | CUSTOM-04 and PORT-01: make publication recoverable after staged install; preserve `user_jvm_args.txt`. Exercise both supported legacy JAR and modern `win_args.txt` paths, installer timeout/cancel, server readiness and graceful stop. | `GameServer\ForgeMC.cs:13-49,57-133`; `GameServer\Engine\MinecraftJava.cs:89-118,129-216` |
| G-MCBE / `MCBE` | Minecraft: Bedrock Edition Server | Official download-links API; EULA/privacy consent; complete runtime inventory and IPv6 companion port | S+B | **CUSTOM-02/03/12 before shared update qualification**; CORE-01 and QUAL-02/03/04. Verify owner-root coupling, custom pack preservation, separate worlds/access lists/permissions, runtime writes and record/build consistency after update. | `GameServer\MCBE.cs:84-175,190-345,416-701,751-819` |
| G-GTA5 / `GTA5` | Grand Theft Auto V Dedicated Server (FiveM) | Cfx recommended Windows artifact/server data; non-placeholder CFX license | P | HAR-03: provide a secure explicit license fixture; blank-key rejection is correct. Retain staged runtime/server-data separation. Qualify artifact parsing, resource start, actual listener/FiveM response, config preservation and `quit` persistence. | `GameServer\GTA5.cs:34-44,64-195,265-297,381-667` |
| G-RedM / `RedM` | Red Dead Redemption 2 Server | Inherited Cfx acquisition and license gate; usable `rdr3` resources | P | HAR-03; qualify distinct RedM resources and `gamename rdr3`, not merely a live FXServer or FiveM result. Preserve the existing license rejection and runtime/data separation. | `GameServer\GTA5.cs:51-61,107-110,620-667,700-708` |
| G-RCT2 / `RCT2` | RCT2 Dedicated Server | OpenRCT2 portable release; pinned/hash-checked bootstrap park; any required original assets/rights | P | CUSTOM-04: recoverable runtime publication. Confirm asset/park rights and current headless prerequisites; test default host/park/port and custom-parameter replacement. Establish whether name/max-player settings are supported or explicitly unavailable. | `GameServer\RCT2.cs:17-35,65-233,241-284,287-343,398-445` |
| G-Soldat / `Soldat` | Soldat Dedicated Server | Pinned `soldatserver2.8.2_1.7.1.1.zip` with size/hash; valid port and 1-32 players | P | CUSTOM-04: rollback after partial copy. Preserve the existing INI/map/script policy; verify current pinned-package support, full mutable-file inventory and hidden-window stop. `validate=true` currently runs the same reinstall path, not distinct integrity validation. | `GameServer\Soldat.cs:17-25,42-81,213-216,264-425,451-470` |
| G-OpenTTD / `OpenTTD` | OpenTTD Dedicated Server | Tag-derived official CDN OpenTTD/OpenGFX packages; valid port and 1-255 clients | P | CUSTOM-04: recoverable runtime publication while retaining cfg/saves. Qualify tag/CDN/base-set layouts, dedicated-mode listener, `-g` new-game versus save/resume behavior, and stdin `quit` persistence. | `GameServer\OpenTTD.cs:20-39,56-96,207-328,330-365,414-439,482-531` |
| G-Mindustry / `Mindustry` | Mindustry Dedicated Server | Latest GitHub server JAR with digest; externally installed/version-probed Java 17+ | P | Retain hash-checked JAR replacement; qualify release-tag grammar (including integer-only `v146` fixtures), digest availability, actual acknowledgment of configure/host commands, game-ready endpoint and `exit` save/restart. A live command console alone is not hosting. | `GameServer\Mindustry.cs:19-32,94-158,227-295,385-430,445-539` |
| G-OpenRA / `OpenRA` | OpenRA Dedicated Server | GitHub x64 portable runtime plus Red Alert mod; valid port and map capacity | P | Retain staged runtime replacement/rollback and separate `OpenRA-data`. Qualify portable dependencies, usable map/listener, state isolation and actual `quit` behavior; establish support/limits of the generic max-player field rather than silently promising it applies. | `GameServer\OpenRA.cs:16-30,47-111,172-208,293-302,385-501` |
| G-VTS / `VTS` | Vintage Story Dedicated Server | AUR-derived version string -> vendor x64 ZIP; compatible native/runtime prerequisites | P | **CUSTOM-01/10 before update qualification**; CORE-01 and FMT-01. Preserve non-`data` content and recovery copy, reject empty/malformed metadata before mutation, verify official acquisition/runtime contract and `/stop` persistence. | `GameServer\VTS.cs:19-46,49-102,105-214,244-259` |

MCBE owner qualification needs special care: the store's binary root can be the owner's own `serverfiles`, not a separate immutable installation folder (`Functions\SharedInstallationStore.cs:227-246,540-545`). Configuration preparation then touches that root; a consumer initializes missing allowlist/permissions from it (`GameServer\MCBE.cs:463-492,564-580`). Test distinct access policies and worlds and record any intentional inheritance. This coupling is demonstrated; consumer-world corruption has **not** been demonstrated. Config-only backup is not world/addon recovery.

For these adapters, stop dispatch returning before exit is not by itself a defect: the host and harness already wait afterward. Under their unchanged defaults, all 13 select embedding; MCPE/MC/Paper/Forge/MCBE/VTS send commands through redirected stdin. Nonembedded window-message delivery needs its own explicit case. Soldat does not redirect stdin and needs its window-close/fallback behavior qualified. OpenTTD/Mindustry/OpenRA keep stdin available regardless of embedding. Do not mislabel these actual default paths as nonembedded tests.

### Standalone Steam games - 24 built-ins

All 24 are private-only. Steam app IDs/login modes below describe code, not freshly verified download access. All use the CORE-02 remote-build helper; only Rust has a config-only plan. CORE-01's exact affected list excludes Rust's preserving configuration implementation.

| Work ID / key | Exact built-in selector (`FullName`) | Steam acquisition / special prerequisites | Gate | Specific next work beyond the common checks | Adapter evidence |
|---|---|---|---|---|---|
| G-RUST / `RUST` | Rust Dedicated Server | 258550, A; exact Rust runtime prerequisites unqualified | P+B | Preserve the existing config and shared-operation rejection. HOST-01: use the existing awaitable config helper. Prove private readiness/quit/save and full plus `server.cfg`-only backup. The known shared bundle-root failure is not permission to re-enable sharing. | `GameServer\RUST.cs:27-41,48-116,158-220` |
| G-SCUM / `SCUM` | SCUM dedicated server | 3792580, A; inherited SteamCMD operations; clean-host/runtime/entitlement preflight | P | STEAM-05: meaningful zero-HWND shutdown. Current nested validators and launch diagnostics are good. Verify effective hostname/IP/query settings because default arguments apply only port/player count; config hook is empty. | `GameServer\SCUM.cs:14-28,35-64,89-176`; `GameServer\Engine\SteamCMDAgent.cs:20-45` |
| G-SDTD / `SDTD` | 7 Days to Die Dedicated Server | 294420, A; client identity 251570 | P | CORE-01, SET-01, PORT-01, FMT-01: awaited/preserving XML config, requested capacity and valid telnet port. Prove window-close/Enter actually saves/exits and requested config is consumed. | `GameServer\SDTD.cs:35-47,54-93,144-184` |
| G-MORDHAU / `MORDHAU` | Mordhau Dedicated Server | 629800, A; nested shipping executable | P | STEAM-01/04/07, CORE-01, PORT-01: fix validator mismatch; address documented embedding/custom-map risk and kill-based shutdown. Verify beacon, capacity and nested launch layout. | `GameServer\MORDHAU.cs:13-41,48-91,111-176` |
| G-SE / `SE` | Space Engineers Dedicated Server | 298740, A; template/world downloads and clean-host native/runtime prerequisites | P | CORE-01, SET-01, STEAM-04: surface extraction failure, honor selected world and save before stop. Prove profile/config resolution with `USERPROFILE`; downloaded batch is not the launched command. | `GameServer\SE.cs:22-34,41-134,139-203` |
| G-DAYZ / `DAYZ` | DayZ Dedicated Server | 223350, C; optional launcher and actual server dependencies | P | CORE-01/03, SET-01, STEAM-04/12: safe config, requested capacity, defensive account parsing, graceful save and immutable canonical executable selection. Verify wrapper/child ownership. | `GameServer\DAYZ.cs:27-40,47-81,109-159` |
| G-OLOW / `OLOW` | Outlaws of the Old West Dedicated Server | 915070, A; native/runtime prerequisites unqualified | P | STEAM-04: replace unconditional kill with supported shutdown/fallback reporting. Empty cfg generation is intentional; prove map/name/player/query arguments and establish admin configuration behavior without inferring an exploit from a blank default. | `GameServer\OLOW.cs:24-36,43-62,107-140` |
| G-ARKSE / `ARKSE` | ARK: Survival Evolved Dedicated Server | 376030, A; complete ShooterGame server data | P | STEAM-01/04/10: reject marker-only imports, preserve custom flag boundaries and save before stop. Test Unreal URL options separately from standalone flags. | `GameServer\ARKSE.cs:15-27,34-78,102-106` |
| G-SW / `SW` | Stormworks Dedicated Server | 1247090, A; native/runtime prerequisites unqualified | P | CORE-01, FMT-01, STEAM-04: safe XML and graceful stop. Prove generated `AppData\Roaming\Stormworks` config is found with current `USERPROFILE`/`APPDATA` overrides; mismatch effect is not yet established. | `GameServer\SW.cs:14-26,33-81,100-108` |
| G-ROK / `ROK` | Reign Of Kings Dedicated Server | 381690, A; `ROK.exe` plus actual `Server.exe` launcher | P | STEAM-01/11, CORE-01, SET-01: validate both required files, honor selected capacity, and actually wait for `/shutdown` completion instead of discarding a delay. Track launcher/server ownership live. | `GameServer\ROK.cs:18-30,37-93,112-120` |
| G-HEAT / `HEAT` | Heat Dedicated Server | 996600, A; `Heat.exe` plus actual `Server.exe` launcher | P | Same concrete validator/config/shutdown work as ROK: STEAM-01/11, CORE-01, SET-01. Separately confirm current app access/package layout; old public app-detail absence is not a fresh SteamCMD failure. | `GameServer\HEAT.cs:14-26,33-89,108-116` |
| G-BW / `BW` | BlackWake Dedicated Server | 423410, A; native/runtime prerequisites unqualified | P | CORE-01, PORT-01, STEAM-04: preserve config, validate derived Steam port and meaningful shutdown. Despite `AllowsEmbedConsole=false`, Start is windowless; verify effective capacity/config and owned endpoints. | `GameServer\BW.cs:14-26,33-87,106-114` |
| G-ONSET / `ONSET` | Onset Dedicated Server | 1204170, A; native/runtime prerequisites unqualified | P | CORE-01, SET-01, FMT-01, STEAM-04: valid/preserving JSON with selected capacity; use a supported graceful channel instead of killing a stdin-redirected process. | `GameServer\ONSET.cs:14-26,33-45,80-112,131-139` |
| G-EGS / `EGS` | Empyrion - Galactic Survival Dedicated Server | 530870, A; WMI command-line discovery plus real dedicated/playfield processes | P | STEAM-06/04, CORE-01, PORT-01, FMT-01: verified PID ownership, failure cleanup, complete child teardown and safe YAML/telnet setup. A crash-handler `--attach` match alone is not server identity. | `GameServer\EGS.cs:16-28,35-115,133-141`; `Functions\ProcessManagement.cs:19-29` |
| G-UNT / `UNT` | Unturned Dedicated Server | 1110390, A; current flags/content must be qualified | P | STEAM-04: default embedded branch kills despite redirected stdin; nonembedded uses `shutdown`. Empty cfg hook is not a defect by itself. Verify `--nographics`, map/player/port argument interpretation and effective hostname. | `GameServer\UNT.cs:14-52,72-139` |
| G-AVORION / `AVORION` | Avorion Dedicated Server | 565060, A; configured admin/galaxy behavior unqualified | P | STEAM-04/10: preserve galaxy-name token boundaries and use save/stop in the default mode, not only the alternate branch. Retain the explicit per-instance galaxy data path; validate admin policy and save reload. | `GameServer\AVORION.cs:14-26,38-50,71-114,133-142` |
| G-CE / `CE` | Conan Exiles Dedicated Server | 443030, A; nested shipping executable | P | STEAM-01/05, CORE-01: align validators with shipping binary, preserve config and replace window-close against a windowless process with a supported control path. | `GameServer\CE.cs:14-52,79-118,136-145` |
| G-INSS / `INSS` | Insurgency: Sandstorm Dedicated Server | 581330, A; current nested shipping executable | P | STEAM-04/10: quote hostname and save/exit appropriately. Current nested validators already agree and clear errors on success; do not file the old root-path issue. Empty config hook needs effective-setting qualification, not automatic replacement. | `GameServer\INSS.cs:14-54,74-111,130-153` |
| G-TF / `TF` | The Forest Dedicated Server | 556450, A; native/runtime prerequisites unqualified | P | CORE-01, PORT-01, STEAM-04: valid derived Steam port, safe configuration and actual save/exit instead of unconditional kill. | `GameServer\TF.cs:14-57,78-110,105-138` |
| G-SQ / `SQ` | Squad Dedicated Server | 403240, A; nested shipping executable | P | STEAM-01/04, CORE-01: fix root-only import path while retaining correct nested install validation, preserve cfg, and replace default kill with supported shutdown. | `GameServer\SQ.cs:15-60,109-149` |
| G-BT / `BT` | Barotrauma Dedicated Server | 1026340, A; native/runtime prerequisites unqualified | P | CORE-01 and FMT-01: preserved/valid XML settings. `quit` is attempted, not a hard-kill normal path; prove real exit, readiness and state persistence before calling it graceful. | `GameServer\BT.cs:17-49,51-90,109-118` |
| G-PS / `PS` | Post Scriptum Dedicated Server | 746200, A; current product/package/map qualification needed | P | STEAM-01/04, CORE-01: correct import executable; preserve both config and map rotation on failed generation; demonstrate actual save/shutdown and ready/query identity. | `GameServer\PS.cs:15-73,121-161` |
| G-ROR2 / `ROR2` | Risk of Rain 2 Dedicated Server | 1180760, A; native/runtime prerequisites unqualified | P | CORE-01 and STEAM-04: safe config and supported stop. Verify `server.cfg` is executed and a playable session starts rather than accepting a live console as ready. | `GameServer\ROR2.cs:15-53,100-139` |
| G-ECO / `ECO` | Eco Dedicated Server | 739590, A; clean-host runtime/license/first-run requirements | P | CORE-01 and STEAM-04: safe config and graceful save. Qualify Unity-style defaults and config-app/process ownership; the source explicitly allows a separate config application, so its window alone is not a defect. | `GameServer\ECO.cs:11-36,43-62,109-148` |

Actual default console paths matter: 15 of these adapters retain `AllowsEmbedConsole=true`; nine retain false. All 17 games listed in STEAM-04 choose their kill-based policy under unchanged adapter defaults. SCUM redirects output but explicitly permits a window; its zero-HWND shutdown problem is conditional. CE's ineffective window-close path is the windowless default. These facts correct any assumption that `ServerConfig.EmbedConsole=false` made the current suite nonembedded (HAR-01).

### V2 plugin examples - 2 additional live cases

These selectors are example names from `V2PluginCases`, not their displayed game names. Evidence paths here are relative to the repository root.

| Work ID / selector | Displayed game name / acquisition | Method capability versus actual host gate | Specific next work | Source evidence |
|---|---|---|---|---|
| P-GarrysModV2 / `GarrysModV2` | Garry's Mod Dedicated Server (V2 tutorial); Steam 4020, A, inherited Source runtime | Shared + config-only methods detected; **host UI remains private-only**. The suite directly exercises the shared provider as a qualification fixture. | Local host config/backup fixture **Passed**; selected live row **SuitePassed** through the existing smoke oracle. Still verify exact `garrysmod\cfg\server.cfg` and `garrysmod\mapcycle.txt` scopes under the full evidence contract. Qualify provider runtime separately from UI gating, both cfg text and effective game settings, concurrent writes/updates and CORE-02/ENG-05 inherited behavior. Test repeated config creation with operator changes: this example unconditionally writes both files, unlike the preserving CS sample; define/reset policy explicitly before reusing it for existing instances. | `CdogsWindowsGSM-Plugin-Development\Examples\GarrysModV2.cs:23-63,65-133`; `CdogsWindowsGSM.Tests\BuiltInGameIntegrationTests.cs:457-558` |
| P-CS16StandaloneV2 / `CS16StandaloneV2` | Counter-Strike 1.6 (standalone plugin tutorial); Steam 90, A, `cstrike`, identity 10 | Config-only methods detected; no shared-install method set; private-only | Local host config/backup fixture **Passed**; selected live row **SuitePassed** through the existing smoke oracle. Still verify exact `cstrike\server.cfg`/`cstrike\mapcycle.txt` scopes and preservation of existing files under the full evidence contract. Qualify actual mod-content imports, `de_dust2`, A2S readiness, window-message `quit`, and CORE-02/ENG-05. Its build methods already request app 90: do not incorrectly apply ENG-03's inherited GoldSrc client-ID bug to this standalone sample. | `CdogsWindowsGSM-Plugin-Development\Examples\CS16StandaloneV2.cs:14-41,48-118,120-246`; `CdogsWindowsGSM.Tests\BuiltInGameIntegrationTests.cs:384-393` |

## Consolidated adapter work

The following tasks supplement HAR/HOST and QUAL, with one identity per root cause. Family rows cite these shared tasks rather than counting each instance as a separate finding. A stop request that returns before exit is not automatically wrong in this repository's caller-waits contract; forced-stop policy and ineffective signals are called out separately.

### CORE-01 - preserve configuration and expose its terminal result

**High / P0 for destructive regeneration; P1 for completion/error propagation / Source defect.** `Functions\Github.cs:14-35,46-75` deletes an existing destination before downloading, catches the failure and judges success by file existence; partial content can appear successful. Source/GoldSrc and many adapters ignore false results or expose only an `async void` config hook. SE also suppresses world-extraction failure (`GameServer\SE.cs:51-62`). HOST-01 is the corresponding caller-side false-success boundary, not another copy of this helper defect.

Affected destructive-template users, **39 built-ins**: CSGO, GMOD, TF2, HL2DM, L4D2, CSS, INS, NMRIH, ZPS, DODS, SDK2013; CS, CSCZ, DOD, DMC, HLOF, RCC, TFC; SDTD, MORDHAU, SE, DAYZ, SW, ROK, HEAT, BW, ONSET, EGS, CE, TF, SQ, BT, PS, ROR2, ECO; MCPE, MC, MCBE, VTS. Rust preserves existing config and already has an awaitable helper (`GameServer\RUST.cs:48-88`); its wrapper/orchestration needs HOST-01, not a destructive-helper finding. Both V2 samples create config locally rather than using this downloader.

Evidence call sites: `GameServer\Engine\Source.cs:215-261`; `GameServer\Engine\GoldSource.cs:225-259`; Steam table's cited config ranges; `GameServer\MCPE.cs:34-46`; `GameServer\MC.cs:38-53`; `GameServer\MCBE.cs:145-175,463-492`; `GameServer\VTS.cs:36-46`.

Acceptance: preserve existing config by default and make reset explicit; download/render/validate a sibling staged file before atomic replacement. Retain a compatible legacy entry point while exposing completion and an actionable failure through existing host conventions. Failed/offline transfer, corrupt template, denied write, or SE world extraction leaves old bytes unchanged; first-time failure produces neither success nor a partial final config. Delayed work cannot be reported complete early. Preserve PS map rotation as well as server cfg. Do not make 39 divergent fixes to the same downloader.

### CORE-02 - read the remote build match that was actually validated

**Medium / P1 / Source defect.** `Installer\SteamCMD.cs:459-469` checks `matches.Count < 1`, then accesses `matches[1]` even though it returns `matches[0]`. Exactly one valid match throws. This affects the 24 standalone Steam adapters, all 18 Source/GoldSrc titles and COD1: **43 built-ins**, plus the two V2 game examples. COD4's pinned wrapper version path is different; do not assume it uses this helper for remote version reporting.

Acceptance: validate and return the same match. Synthetic outputs with zero matches fail explicitly; one valid match returns its exact build ID; multiple matches follow a documented branch-selection rule without indexing failure. No live Steam format change needs to be invented to reproduce this local defect.

### CORE-03 - use one defensive Steam account parser

**Medium / P1 / Source defect.** Authenticated users are DAYZ, COD1 and COD4. `Installer\SteamCMD.cs:69-88,164-180` indexes characters and split values without checking lengths/shape; ordinary blank/short/malformed lines can throw instead of returning the existing account-not-configured error. The suite's separate parser is more defensive (`BuiltInGameIntegrationTests.cs:1506-1565`) but does not repair these production parsers.

Acceptance: reuse/extract a single validated parser with blank/comment handling, duplicate/key/value/quote rules and safe diagnostics. Synthetic blank, whitespace, `/`, missing `=`, and malformed quoted values return controlled errors, never leak values or throw incidental indexing exceptions. Preserve valid synthetic credentials and existing anonymous behavior. Never test by printing a real account file.

### ENG-02 - validate the selected Source/GoldSrc game, not only its launcher

**Medium / P1 / Validation hardening.** All 18 Source/GoldSrc titles accept launcher-only layouts (`GameServer\Engine\Source.cs:95-115,321-330`; `GameServer\Engine\GoldSource.cs:108-128,315-325`). A different/missing mod can therefore satisfy that predicate. Import validators also retain an Invalid Path error on a true result. This is a weak prerequisite contract, not proof a complete current depot fails.

Acceptance: define each selected game's minimal required binary/content set, distinguish generated config from downloaded prerequisites, reject executable-only/wrong-mod fixtures and clear `Error` on a complete accepted layout. SDK2013 must require an explicit supported mod rather than pretend its empty `Game` is a stock title. Do not require a generated cfg before the install flow has created it.

### ENG-03 - separate GoldSrc server-build identity from client identity

**Medium / P1 / Source defect.** CS, CSCZ, DOD, DMC, HLOF, RCC and TFC install/update app 90 but request local/remote build information for their client IDs 10/80/30/40/50/60/20 (`GameServer\Engine\GoldSource.cs:32,263-267,294,327-336`; `Installer\SteamCMD.cs:371-379`). They therefore do not track the installed app-90 server manifest.

Acceptance: preserve client identity where needed while using app 90 for server build/install/update checks. With only an app-90 manifest present, all seven return that build and compare with the correct remote server app. The standalone CS V2 sample already does this and is useful prior art.

### ENG-05 - qualify and correct A2S packet handling

**Medium / P1 / Source defects.** `GameServer\Query\A2S.cs:104-107,188-218` initially sends INFO for a PLAYER exchange and unconditionally reads optional EDF data. `Functions\UdpClientHandler.cs:39-57` reads one datagram without split-response reassembly. Valid protocol variants can therefore be rejected or misinterpreted. This is shared by A2S integrations, not a separate defect in each game.

Affected: the 18 Source/GoldSrc built-ins, plus RUST, SCUM, MORDHAU, SE, DAYZ, OLOW, ARKSE, BW, UNT, CE, INSS, SQ, BT and PS: **32 built-ins**, plus both V2 game examples. Query declarations for those additional 14: `GameServer\RUST.cs:31`; `GameServer\SCUM.cs:23`; `GameServer\MORDHAU.cs:33`; `GameServer\SE.cs:26`; `GameServer\DAYZ.cs:31`; `GameServer\OLOW.cs:28`; `GameServer\ARKSE.cs:19`; `GameServer\BW.cs:18`; `GameServer\UNT.cs:18`; `GameServer\CE.cs:18`; `GameServer\INSS.cs:18`; `GameServer\SQ.cs:19`; `GameServer\BT.cs:21`; `GameServer\PS.cs:19`. The other ten standalone Steam adapters declare no query protocol; absence is not an A2S defect.

Acceptance: correct PLAYER challenge/type exchange; conditionally parse optional fields with length checks; implement the supported split-response contract. Add byte fixtures for direct/challenged INFO, valid no-EDF INFO, PLAYER challenge/data and split/truncated responses. Pin exact decoded server/map/player fields and explicit malformed-input results. Then qualify actual game endpoints rather than treating parser fixtures as runtime passes.

### ENG-06 - honor COD1's selected starting map

**Medium / P2 / Source defect.** `GameServer\COD1.cs:37,74-85,117-142` writes fixed `mp_carentan`/`mp_dawnville` rotations and starts with `+map_rotate`; it does not consume `ServerMap`. A default-valued smoke case masks this.

Acceptance: define the selected-map/custom-rotation ownership rule; a nondefault requested starting map is effective without overwriting an existing operator rotation. Verify stock and MeatBots independently.

### ENG-07 - preserve COD1 fallback content and failed rollback material

**High / P0 / Source defects.** `GameServer\COD1.cs:278-320` replaces an existing entire binary root with a fallback archive and deletes the previous root; non-archive user cfg/maps/mods are lost. At `371-400`, a failed MeatBots restoration can still reach cleanup that deletes the backup while `previousMoved` remains true.

Acceptance: enforce a clearly reported fresh-target-only fallback or preserve declared mutable content transactionally. Inject commit and rollback failures; the original installation or an explicitly reported recoverable backup remains intact. Never delete the only recovery copy after restoration failed. Qualify ordinary Steam acquisition separately from this optional fallback.

### ENG-08 - commit CoD4x and licensed game data coherently

**Medium / P1 / Source defect.** COD4 replaces its CoD4x executable and version stamp before starting licensed Steam acquisition/update (`GameServer\COD4.cs:259-310,363-372`). Missing account setup or later Steam failure leaves the wrapper/stamp changed without a coherent updated game installation.

Acceptance: preflight account/prerequisites and publish only after acquisition/validation, or restore the old wrapper/stamp on failure. Missing-account, failed-process and interrupted-publication fixtures preserve prior executable, version, configuration and game-data hashes. Keep the existing binary size/hash checks.

### ENG-09 - do not emit a valueless Garry's Mod workshop command

**Low / P2 / Source defect.** `GameServer\GMOD.cs:7` always includes `+host_workshop_collection` without an ID; `GameServer\Engine\Source.cs:128-141` emits it.

Acceptance: stock sandbox arguments omit the command; a supplied collection ID is emitted exactly once with its value. Qualify stock and workshop paths separately, including content writes.

### CUSTOM-01 - retain legacy game runtimes until replacement commits

**High / P0 / Source defects.** MCPE removes its PHAR, MC removes its JAR, and VTS deletes/recreates its whole server-files tree before replacement succeeds (`GameServer\MCPE.cs:178-210`; `GameServer\MC.cs:242-286`; `GameServer\VTS.cs:137-214`). VTS restores only `data` and removes its temporary data backup before reacquisition, losing other custom content and its previous runnable install on failure.

Acceptance: acquire and validate first; stage publication with rollback and a declared mutable-file inventory. Inject metadata/download/extraction and mid-publication failures: old runtime and user-state hashes remain unchanged and the prior install remains startable. Preserve supported non-`data` VTS custom content and keep recovery material until the entire update commits.

### CUSTOM-02 - publish a complete Bedrock runtime transactionally

**High / P0 / Source defect.** `GameServer\MCBE.cs:751-819` deletes live runtime directories/files, then moves a fixed list from extraction without complete staged-runtime validation or rollback. Missing expected files or a later move failure leaves a partial install; vendor root-level dependencies outside the list are not refreshed.

Acceptance: validate required versus optional package files and the full vendor-runtime inventory before mutation; atomically publish or restore on failure while retaining worlds/config/user assets. Missing executable, omitted optional release/support files and mid-move failure must leave the old shared installation usable. A successful update refreshes changed vendor dependencies consistently for all consumers.

### CUSTOM-03 - update the build of a Ready shared installation legally

**High / P1 / Source defect.** Bedrock update obtains a lease requiring a Ready installation, then calls the initial-install-only `MarkReady` after publishing files/version (`GameServer\MCBE.cs:757,815-819`; `Functions\SharedInstallationStore.cs:125-138,400-403,461-475`). The Ready -> MarkReady transition is rejected because that transition requires Installing. Files can already be new while registry build stays old and the update reports an exception.

Acceptance: introduce/use an explicit exclusive-lease-protected update transaction or Ready-build-update operation without weakening initial-install validation. An existing Ready record updates successfully with matching filesystem/registry build and remains Ready; failed publication retains its prior build/state. Add a deterministic store/provider regression test before another SH-04 attempt.

### CUSTOM-04 - finish staged installs with recoverable publication

**High / P1 / Source defect.** ForgeMC, RCT2, Soldat and OpenTTD stage acquisition but recursively overwrite live runtime files without rollback (`GameServer\ForgeMC.cs:119-128`; `GameServer\RCT2.cs:211-216,276-284`; `GameServer\Soldat.cs:331-333,395-409`; `GameServer\OpenTTD.cs:268-273,305-318`). A later lock/permission/disk failure leaves mixed versions.

Acceptance: use a runtime-directory transaction where layout permits, otherwise retain a rollback journal/copy of affected vendor files. Fail after the first overwrite and near completion; restore prior binaries/build metadata and retain all user state. Preserve existing `user_jvm_args.txt`, Soldat INI/maps/scripts and OpenTTD cfg/save policies. Staged download alone is not this acceptance test.

### CUSTOM-06 - use compatible, bounded Java provisioning for legacy MC

**High / P1 / Source defects.** `GameServer\MC.cs:56-63,157-175,229-240` accepts discovered Java without comparing the selected Minecraft requirement. `Functions\JavaHelper.cs:12-17,49-65,178-180,231-294` orders versions lexicographically, bootstraps fixed Java 8u241 and can wait indefinitely for `java.exe` without observing installer failure/exit.

Acceptance: reuse/extract the existing version-aware per-instance `Engine\MinecraftJava` runtime path rather than create another installer. Numeric version comparison, metadata-required major, declined/cancelled installation, failed installer and bounded completion all need deterministic tests. Do not silently change another server's machine-wide Java. Qualify the exact selected release/runtime pair after fixture tests.

### CUSTOM-07 - never convert Minecraft EULA refusal into consent during update

**High / P1 / Source defect.** `GameServer\MC.cs:288-297` truncates `eula.txt` and writes `eula=true`, unlike Install's explicit consent dialog.

Acceptance: preserve `eula=false` and existing acceptance; absence/refusal never becomes acceptance automatically. Only explicit affirmative consent through the documented policy can create acceptance. Decline/cancel leaves the previous install/state intact. This is not a request for an AI to accept an EULA on the user's behalf.

### CUSTOM-10 - reject unresolved Vintage Story versions before mutation

**Medium / P1 / Source defect.** `GameServer\VTS.cs:105-110,244-259` returns empty on lookup/regex failure but Install checks only null and constructs `vs_server_win-x64_.zip`. Update can reach it after deleting the old install (CUSTOM-01).

Acceptance: failed lookup, empty/nonmatching HTML and malformed versions produce a specific resolution error before a package request or filesystem change. Valid fixture metadata resolves the intended complete filename. Establish a supported vendor version source instead of assuming the AUR HTML contract is stable.

### CUSTOM-11 - align MCPE/Paper imports with actual startup prerequisites

**Medium / P2 / Source defects.** MCPE accepts a PHAR-only import but needs PHP to start (`GameServer\MCPE.cs:49-72,214-228`). Paper accepts a `paper.jar` layout but additionally needs a discoverable version/build from `.windowsgsm-build` or `version_history.json` (`GameServer\PaperMC.cs:33-45`; `GameServer\Engine\MinecraftJava.cs:234-239,265-271`).

Acceptance: provision/infer the missing supported requirements or reject the layout before import approval with precise instructions. An accepted minimal import reaches runtime prerequisite resolution without the later missing-PHP/cannot-determine-version contradiction. Retain Paper's JAR integrity and runtime checks.

### CUSTOM-12 - retain user-owned Bedrock packs during runtime synchronization

**High / P1 / Source defect/policy gap.** Build/directory mismatch replaces complete instance `behavior_packs`, `definitions`, `resource_packs` and `structures` trees; absent source trees cause destination deletion (`GameServer\MCBE.cs:583-701`). Untracked custom additions in those trees are discarded on a successful refresh.

Acceptance: separate vendor assets from user additions or track vendor-owned files and preserve unowned content during refresh. If custom additions there are unsupported, get an explicit maintainer restriction and surface it before touching them. Seed custom behavior/resource packs; shared-build refresh preserves them and worlds/config, handles obsolete vendor files and remains recoverable if interrupted.

### CUSTOM-13 - dispose Minecraft build-log readers and parse supported versions

**Low / P2 / Source defects.** `GameServer\MC.cs:326-354` can return a found version before disposing its stream/reader and recognizes only a three-component numeric form.

Acceptance: deterministic disposal; two- and three-component supported version-line fixtures return the full version; repeated calls release the log immediately without garbage collection. Do not broaden parsing to arbitrary text without an actual accepted version grammar.

### STEAM-01 - make validators match the canonical startup binaries

**High / P1 / Source defects.** Affected: MORDHAU, CE, ARKSE, SQ, PS, ROK, HEAT. MORDHAU/CE check root launchers but start nested shipping executables; ARK import accepts only `PackageInfo.bin`; SQ/PS import checks root paths unlike correct nested install/start paths; ROK/HEAT omit required `Server.exe` from validity.

Evidence: `GameServer\MORDHAU.cs:30,74-78,167-176`; `GameServer\CE.cs:15,48-52,136-145`; `GameServer\ARKSE.cs:41-45,102-106`; `GameServer\SQ.cs:16,70-71,140-149`; `GameServer\PS.cs:16,82-83,152-161`; `GameServer\ROK.cs:54-75,112-120`; `GameServer\HEAT.cs:50-71,108-116`.

Acceptance: reuse one canonical per-adapter prerequisite set across install/import/start. Wrongly accepted marker/root-only fixtures fail with exact missing-path diagnostics; complete launchable layouts pass. Keep SCUM/INSS's already-correct nested validators unchanged.

### STEAM-04 - distinguish game-controlled shutdown from hard kill

**High operational risk / P1 / Demonstrated policy, not measured data loss.** Normal Stop always kills DAYZ, OLOW, ARKSE, SW, BW, ONSET, EGS and TF. Default embedded Stop kills MORDHAU, SE, UNT, AVORION, INSS, SQ, PS, ROR2 and ECO. All 17 select these policies under unchanged adapter defaults. The harness considers an exited process stopped; an internal adapter kill does not trigger its own force-termination diagnostic.

Evidence: `GameServer\DAYZ.cs:126-131`; `GameServer\OLOW.cs:107-112`; `GameServer\ARKSE.cs:73-78`; `GameServer\SW.cs:76-81`; `GameServer\BW.cs:82-87`; `GameServer\ONSET.cs:107-112`; `GameServer\EGS.cs:109-114`; `GameServer\TF.cs:105-110`; `GameServer\MORDHAU.cs:136-147`; `GameServer\SE.cs:139-174`; `GameServer\UNT.cs:99-110`; `GameServer\AVORION.cs:98-114`; `GameServer\INSS.cs:99-110`; `GameServer\SQ.cs:109-120`; `GameServer\PS.cs:121-132`; `GameServer\ROR2.cs:100-111`; `GameServer\ECO.cs:109-120`.

Acceptance: establish each game's supported save/shutdown channel, await bounded exit at the appropriate orchestration layer and make forced fallback explicit. Stateful games retain a recent known world change across stop/restart; session-only games demonstrate orderly termination and owned-child cleanup. Test supported console modes. Where graceful shutdown is genuinely unsupported, publish that limitation and obtain a maintainer decision rather than pretend a kill proves a save.

### STEAM-05 - do not use a nonexistent window as the only shutdown channel

**High / P1 / Source defects.** CE's windowless default calls `CloseMainWindow` (`GameServer\CE.cs:79-118`); SCUM's zero-HWND path also tries window-close before its five-second kill fallback (`GameServer\SCUM.cs:127-153`). SCUM's fallback Notice is positive, but the attempted signal cannot close a nonexistent window.

Acceptance: use a supported game control channel independent of HWND, or explicitly disable the unsupported mode. A zero-HWND case delivers a real graceful signal or an actionable failure, not a successful window-close claim. Confirm process exit and saved state; keep SCUM's fallback reporting.

### STEAM-06 - own Empyrion's actual server process and failure cleanup

**High / P1 / Source defects.** EGS starts a launcher, finds a crash-handler command line, parses `--attach` and returns that PID without proving target identity/ancestry; discovery failures do not clean up launched processes (`GameServer\EGS.cs:65-106`; `Functions\ProcessManagement.cs:19-29`).

Acceptance: track this attempt's owned process tree and verify executable/path/ancestry/start time/liveness before adoption. WMI denial, missing/malformed crash handler and timeout leave no owned orphan. Success returns the intended dedicated process and its children can be monitored/stopped. Never terminate another instance merely because an executable name matches.

### STEAM-07 - reconcile Mordhau's console default with its compatibility warning

**Medium / P1 / Source-documented risk.** `GameServer\MORDHAU.cs:13-19` warns embedding can freeze custom-map/output behavior, but the default remains true at `31` and selects redirected output (`111-130`). The historical freeze was not reproduced here.

Acceptance: keep that mode unavailable/default-off until demonstrated safe, or fix and qualify sustained output and custom-map transitions before retaining it. Do not claim a new freeze solely from the old comment, and do not change the default without updating the supported console-mode docs/tests.

### SET-01 - honor supported nondefault instance settings

**Medium / P2 / Source defects plus explicit support decisions.** Constant capacity replaces instance values in SDTD (`GameServer\SDTD.cs:65`), DAYZ (`GameServer\DAYZ.cs:55`), ROK (`GameServer\ROK.cs:45`), HEAT (`GameServer\HEAT.cs:41`), ONSET (`GameServer\ONSET.cs:42`), MCPE (`GameServer\MCPE.cs:40-44`) and MC (`GameServer\MC.cs:44-51`). SE uses `Defaultmap` for both world fields (`GameServer\SE.cs:65-75`); MC similarly uses its default world. RCT2/OpenRA generic name/capacity support and SCUM/Unturned/Blackwake/Mordhau effective settings need explicit capability decisions, not speculative configuration rewrites.

Acceptance: seed capacity different from defaults and an existing nondefault world; generated config/effective game state reflects the requested supported values. Unsupported settings are clearly unavailable/documented rather than silently substituted. Preserve operator-managed rotations/worlds and do not add a new generic setting abstraction merely to fix constants.

### PORT-01 - validate and allocate every derived auxiliary port

**Medium / P2 / Source defects.** Derived ports are not bounded in SDTD (`ServerPort - 18819`, `GameServer\SDTD.cs:64`), MORDHAU (`+7223`, `GameServer\MORDHAU.cs:67`), BW (`+2014`, `GameServer\BW.cs:42`), EGS (`+4`, `GameServer\EGS.cs:45`), TF (`-18249`, `GameServer\TF.cs:40`) and Java MC/Paper/Forge RCON (`+10`, `GameServer\MC.cs:47`; `GameServer\Engine\MinecraftJava.cs:72`). MCBE already rejects the invalid IPv6 `+1` range and has a targeted unit test; do not reopen that fixed boundary.

Acceptance: parse/derive/range-check before writing and reserve the complete game/query/auxiliary footprint (HAR-05). Exact lower/upper witnesses: SDTD `18819 -> 0` rejects and `18820 -> 1` accepts; MORDHAU `58312 -> 65535` accepts and `58313 -> 65536` rejects; BW `63521/63522` separates valid/invalid; EGS `65531/65532` separates valid/invalid; TF `18249 -> 0` rejects and `18250 -> 1` accepts; Java RCON `65525/65526` separates valid/invalid. Defaults must remain unchanged, and no invalid case leaves a partially rewritten config.

### STEAM-10 - retain CLI token boundaries for custom flags and names

**Medium / P2 / Source defects.** ARK appends `ServerParam` without a delimiter (`GameServer\ARKSE.cs:48-55`), so a standalone `-NoBattlEye` can concatenate with the preceding query-port URL value. INSS hostname and Avorion galaxy values are unquoted (`GameServer\INSS.cs:53`; `GameServer\AVORION.cs:44`).

Acceptance: preserve intentional Unreal `?option` adjacency while separating ordinary flags, and use correct Windows scalar argument escaping. Verify `-NoBattlEye`, a URL-style option, hostname `Alpha Server`, galaxy `Alpha Galaxy` and quote-containing names arrive as intended single values. Reuse existing argument helpers where available.

### STEAM-11 - actually wait for ROK/Heat shutdown progress

**Medium / P2 / Source defect.** `GameServer\ROK.cs:87-93` and `GameServer\HEAT.cs:83-89` discard `Task.Delay(5000)` inside a synchronous `Task.Run` callback. The returned task does not implement the intended grace period.

Acceptance: prefer confirmed bounded exit over a blind delay. Stop remains pending while shutdown is pending and completes on exit or an explicitly handled timeout; do not just add a retry that labels eventual forced kill graceful.

### STEAM-12 - keep DayZ's canonical executable stable

**Medium / P2 / Source defect.** `GameServer\DAYZ.cs:62-81` mutates `StartPath` when `DZSALModServer.exe` exists; after removal, a reused adapter can keep that value, including in validators (`150-159`).

Acceptance: select a local launch executable each time without mutating the canonical startup contract. On one adapter instance, select optional launcher, remove it, and verify fallback selects the original DayZ executable and validators remain stable. Verify wrapper dependencies separately.

### FMT-01 - validate structured templates with adversarial-but-valid names

**Medium risk / P2 / Qualification gap, not a verified malformed-template incident.** SDTD/SW/BT insert raw values into XML-targeted config, ONSET into JSON, EGS into YAML and VTS into JSON (`GameServer\SDTD.cs:57-66`; `GameServer\SW.cs:35-42`; `GameServer\BT.cs:38-47`; `GameServer\ONSET.cs:35-43`; `GameServer\EGS.cs:37-46`; `GameServer\VTS.cs:36-46`; `Functions\Github.cs:65-68`). Current remote template bodies were not fetched, so exact placeholder placement/schema must be confirmed before choosing an escaping fix.

Acceptance: retain sanitized versioned template fixtures, render names containing `&`, `<`, quotes, backslashes and newlines, and parse using the actual format. Decoded values match the intended name with no added fields/unresolved placeholders. Use serializers/format-correct escaping, not a generic replacement hack. Confirm SE/Eco's formats before extending the affected list to them.

## Handoff workflow and evidence contract

1. Claim one stable shared finding or game row on a **new implementation branch** from the chosen candidate. Record assignee/branch, status, and candidate commit in this report; do not silently mark a sibling game complete.
2. Re-read the cited source at that commit. This report is anchored to the baseline above; verify whether a newer change already fixed the issue before editing. Prefer existing helpers/patterns over new per-game infrastructure.
3. Re-establish ENV-01's now-passing baseline on the implementation candidate and resolve the applicable HAR/HOST finding before using live results as release evidence. HAR-01 is now fixed for the configured non-embedded mode and the seven GoldSrc rows pass its smoke test; their original wrong-mode timeouts remain in the historical table. The other 26 selected failures, seven explicit preflight blocks, Mindustry's missing Java 17+ runtime, and Space Engineers' no-TRX abort still need row-specific follow-up. Do not use smoke passes as readiness, save/restart, or alpha evidence. Prioritize preservation/rollback issues before alpha claims.
4. Test only licensed, disposable instances on C: or an explicitly available drive, outside the repository. Use a fresh run-specific subdirectory under `%LOCALAPPDATA%\CdogsWindowsGSM\GameIntegration`. Check disk space, game runtime, entitlements, auxiliary ports, and consent first. Do not read or commit real credentials, token-bearing configs, raw command lines, or Steam Guard codes.
5. Run the smallest relevant existing unit tests, then the exact selected live row. Do not start `*` as a substitute for planning storage/prerequisites. Current `CDOGS_GAME_INTEGRATION_GAMES` expects exact `FullName` values, not class keys; plugin selectors are exactly `GarrysModV2` / `CS16StandaloneV2`. Confirm nonzero selected execution rather than accepting an inconclusive result.
6. For each claimed feature attach sanitized evidence using the fields below. Keep large logs/TRX outside source control and link an approved artifact location; the checked-in ledger should contain concise results and hashes/versions, not game packages or private backups.
7. Mark a row **Qualified** only for the explicitly tested modes/version when all applicable gates pass. Otherwise use **Blocked**, **Failed**, or **Deferred by maintainer**, with the reason and affected support claim. A maintainer-approved gate/removal must update the factory/UI/docs/tests consistently; an AI must not prune a game based solely on a historical player count or an unrechecked endpoint.

### Required result fields for every game/example

| Field | What the implementing AI must record |
|---|---|
| Identity | Game row ID, class key and exact test selector; source commit; Windows build; candidate app/publish mode; game build/depot/artifact version and hash where available. |
| Setup | Private/shared mode; supported console mode; runtime/license/EULA preflight result; C-drive run root; disk/bandwidth estimate; instance IDs and distinct game/query/auxiliary ports. Do not record secrets. |
| Install/import | Fresh install result; the exact required executable/data/config set; empty and deliberately incomplete import rejected with a useful diagnostic; complete import accepted where supported. |
| Update/validation | Normal update and Steam integrity validation if implemented; otherwise explicitly label package refresh instead of integrity validation. Seed non-default config/save sentinels and prove preservation. Include a supported failed-download/update recovery case before claiming safety. |
| Runtime | Ready signal and elapsed time; process and bound-endpoint ownership; server identity/map observed through query/client/log; clean stop and saved state after restart. Test each supported console mode. |
| Shared only | Two concurrent instances; write inventory; A/B configuration, save, log and port isolation; in-use update rejection; stopped update/recovery; reference-aware removal/owner lifetime. Preserve the current Rust/SDK2013/plugin gates unless separately approved and qualified. |
| Backup | Full-server default route; plus exact config-only scope if advertised. All planned config bytes restored; non-target files unchanged; matching identity/build checks; behavior after restart. Never describe config-only as a world/save backup. |
| Outcome | Actual selected/executed/passed/failed/blocked counts; sanitized artifact link; remaining limitations; reviewer/maintainer sign-off. No status inferred from a successful build, API detection, or a running process alone. |

### Definition of done for alpha

- [ ] No known destructive update or cross-instance config/save defect remains in an enabled integration; unresolved paths are explicitly gated with maintainer approval.
- [ ] HAR/HOST blockers and selected-game prerequisites are addressed; native build/discovery/local tests have real passing evidence.
- [ ] Every intended alpha game has a completed ledger row with applicable private/shared, console-mode, readiness, update/recovery, save/restart, and backup evidence; unsupported/deferred cases are explicit.
- [ ] TFC's outstanding shared update check is completed; no family-wide runtime approval is inferred from it.
- [ ] Rust/SDK2013/plugins are not accidentally enabled for sharing, and no private-to-shared migration is claimed.
- [ ] README, the capability inventory, tester checklist, and alpha roadmap match the exact candidate and its blocked/deferred cases.

This game audit does not replace the roadmap's clean-machine app/updater, accessibility, plugin compatibility, dependency, or recovery gates.