# Discord.Net dependency upgrade: implementation plan

Status: proposed; assumes the separate .NET 10 migration is complete first.
Investigated: 21 October 2026.

## Recommendation

After the WPF application has been migrated to and verified on .NET 10, upgrade its Discord client from Discord.Net 2.2.0 to the latest stable Discord.Net release available at implementation time. The latest stable release checked for this plan is **3.20.2**.

Use `Discord.Net.WebSocket` as the direct package dependency because the application uses the WebSocket client. Let that package bring in its matching Core and REST dependencies transitively. Do not add the Discord.Net metapackage or optional modules unless the source audit finds a real consumer.

Keep the existing bot experience: token and admin configuration, prefix-based `wgsm` commands, per-admin server grants, presence updates, and the existing WindowsGSM server actions. Do not combine this work with slash-command conversion or a rewrite of the webhook sender.

This is a small-to-medium compatibility and packaging change, not just a version-number edit. Allow approximately **4-7 engineer-days after the .NET 10 migration**, depending on package changes from that migration and access to a test bot/server. The estimate excludes the .NET 10 migration itself.

### Upgrade outcome

- Build, publish and run the WPF application on the migrated .NET 10 target with Discord.Net 3.20.2 or the latest stable version revalidated at implementation time.
- Preserve login, presence, current prefix commands, authorization checks, server-action responses and clean bot shutdown.
- Ensure Discord.Net assemblies are resolved through the migrated project's normal dependency and publish outputs, without stale or mismatched embedded copies.
- Keep webhook alerts and crash-log delivery working through their existing HTTP implementation.
- Request only the Gateway intents the current message-based bot needs, with Message Content enabled explicitly.

This does **not** mean changing the bot to interactions/slash commands, replacing the HTTP webhook code, changing how tokens or admin lists are stored, or upgrading unrelated NuGet packages unless the .NET 10 build requires it.

## Evidence from this repository

Paths are relative to the repository root. Line numbers refer to the investigated tree and will move during the preceding framework migration.

| Area | Current implementation | Upgrade consequence |
| --- | --- | --- |
| Package versions | `CdogsWindowsGSM\packages.config` lists `Discord.Net`, `Discord.Net.Commands`, `Discord.Net.Core`, `Discord.Net.Rest`, `Discord.Net.Webhook` and `Discord.Net.WebSocket`, all at 2.2.0. | Re-check the post-migration package model and make the Discord.Net graph resolve to one consistent release. |
| Application target | `CdogsWindowsGSM\CdogsWindowsGSM.csproj` currently targets .NET Framework 4.7.2 and uses the old non-SDK project format. | The .NET 10 migration is a hard prerequisite. Do not try to consume the current Discord.Net release from the old target or silently settle for an older package. |
| Assembly references | The current project has explicit Discord DLL `HintPath` references and embeds Discord DLLs as `ReferencesEx` resources. `CdogsWindowsGSM\Program.cs` resolves missing assemblies from those resources. | Inspect the migrated project's actual deployment strategy. Prefer normal package/runtime dependency output and remove Discord-specific embedding if it is no longer needed; do not blindly rewrite old asset paths. |
| Bot lifecycle | `CdogsWindowsGSM\DiscordBot\Bot.cs` creates `DiscordSocketClient`, logs in, starts/stops it, sets the bot profile on Ready, and periodically updates activity. | Review the 3.x API changes and verify Ready, activity, reconnect and shutdown behavior instead of treating a successful login call as full readiness. |
| Commands and authorization | `CdogsWindowsGSM\DiscordBot\Commands.cs` subscribes to `MessageReceived`, reads `SocketMessage.Content`, checks the admin allow-list and server grants, then dispatches WindowsGSM actions on the WPF dispatcher. | Reading message content requires the appropriate Gateway intent and Developer Portal setting. Preserve the current authorization gates and action routing. |
| Command package usage | The application's `Commands` class is its own message parser; source usage does not show the `Discord.Commands` framework. | `Discord.Net.Commands` appears unnecessary. Confirm no reflection or external plugin dependency before removing it. |
| Webhook usage | `CdogsWindowsGSM\Functions\DiscordWebhook.cs` posts JSON using `HttpClient`; it does not use `Discord.Net.Webhook`. | `Discord.Net.Webhook` appears unnecessary. Leave webhook payload and delivery behavior outside the dependency migration, but include it in regression checks. |
| Other project | The current project/config search found the Discord.Net package references in the main WPF app, not the plugin-development project. | Re-run the package/source audit after the framework migration in case project references or package ownership change. |

## Package and framework compatibility

The NuGet stable feed listed Discord.Net **3.20.2** when this plan was investigated. The `Discord.Net.WebSocket` 3.20.2 package metadata lists .NET 8, .NET 9 and .NET 10 target groups. The plan assumes the preceding migration leaves the app on a Windows-specific .NET 10 target such as `net10.0-windows`, which can consume the package's .NET 10 assets.

The `Discord.Net` 3.20.2 metapackage includes Core, REST, WebSocket, Commands, Webhook and Interactions components. The WebSocket component itself depends on matching Core and REST packages and `Discord.Net.Dave`. Prefer the WebSocket package directly to avoid importing optional modules that the application does not use. Validate the resolved graph after restore rather than pinning transitive modules independently without a reason.

Recheck the latest stable package and release notes immediately before implementation. Keep all Discord.Net components on the same release if any additional direct module is proven necessary. If the completed .NET 10 migration cannot build or publish before this work starts, stop at the prerequisite gate and resolve that migration; the older .NET Framework-compatible Discord.Net 3.18.0 is only a fallback, not the latest-release target of this plan.

## Proposed implementation

### 1. Establish the post-migration baseline

Treat the separate .NET 10 migration as completed only after the main app and the solution's supported projects build and publish from a clean restore. Record the final target framework, project/package format, publish mode and any changes to `Program` assembly resolution. Run the existing Discord behavior once before changing packages so later failures can be separated from migration regressions.

Capture or confirm:

- Which channels the bot is expected to accept commands from: guilds, direct messages, or both.
- Whether a dedicated test application/guild and non-production bot token are available.
- The existing bot prefix, admin IDs and server grants, without copying tokens or credentials into the plan, logs or test output.
- A working baseline for the built/published app, since dependency resolution may differ from Visual Studio's development output.

### 2. Reduce and update the package graph

After the target framework migration, use the package model established by that migration, preferably `PackageReference` if the project has moved to SDK-style. Add the latest stable `Discord.Net.WebSocket` version verified at execution time (3.20.2 as of this investigation).

Remove the `Discord.Net` metapackage and explicit `Discord.Net.Commands` / `Discord.Net.Webhook` dependencies if the source audit still finds no consumer. Do not add `Discord.Net.Interactions` as part of this work. Core/REST and any other required WebSocket dependencies should resolve transitively; check `dotnet list package --include-transitive` or the equivalent for the migrated project to ensure one coherent Discord.Net version graph.

Remove or replace the old Discord-specific `<Reference>` and `<EmbeddedResource>` entries left by the legacy project. Keep `Program`'s resource resolver only for assemblies that still require it. Do not embed duplicate copies of Discord DLLs when the migrated build/publish already includes the package assets. If the app uses single-file or custom packaging, explicitly verify that the chosen publish mode carries all managed dependencies and that the resolver does not load a stale copy.

### 3. Adapt the client and gateway configuration

Update `Bot.cs` to configure a `DiscordSocketConfig` with the smallest intent set that preserves the confirmed product behavior. The command handler reads message content, so enable the privileged **Message Content** intent in the Discord Developer Portal and request it in the client configuration. Include guild and/or direct-message message intents according to the baseline decision; include other intents only if code demonstrates a need.

Review the 2.x-to-3.x API changes used by this application. Compile and make only necessary, behavior-preserving adjustments to:

- Client construction, login, readiness and shutdown.
- Activity/presence updates and the existing Ready callback.
- Current-user profile modification, if the existing operation remains supported and intended.
- `MessageReceived`, socket message/channel APIs, embed builders and message sends.
- Exception/logging paths for login failure, missing intent configuration, disconnect and rate limiting.

Keep the existing prefix parser and calls into `MainWindow`; do not switch to `Discord.Commands`, slash commands or a new command framework just because a package is available. Preserve the self-message check, admin allow-list and per-server grant checks before dispatching server actions.

### 4. Preserve packaging and configuration

Do not change the bot token, prefix, dashboard or admin-list storage format as part of the package upgrade. Keep the token out of source control, diagnostics and test artifacts. Verify that the normal install/update flow deploys the new package assemblies and that there are no duplicate 2.2.0 DLLs in output, embedded resources or publish staging folders.

Leave `DiscordWebhook` on its current HTTP path. It is not a Discord.Net dependency consumer; changing its serialization, HTTP response handling or alert semantics would make this a separate change. Smoke-test both server alerts and crash-log delivery after the package migration.

### 5. Integrate, qualify and release

Use a test bot and a non-production game server for real Gateway/REST acceptance checks. Do not validate by exposing the production bot token in logs or by using production server actions. Test the built/published application, not only a unit test or IDE launch. Update dependency documentation or release notes if the repository documents supported Discord behavior or required bot permissions.

## Phased delivery and acceptance gates

Estimates are working days for one engineer after the .NET 10 migration is complete. They exclude review delays, Discord application setup and time spent resolving unrelated framework-migration issues.

| Phase | Work | Exit evidence | Estimate |
| --- | --- | --- | --- |
| 0. Framework prerequisite | Confirm the .NET 10 migration is merged/available, the WPF app builds and publishes from a clean restore, and a pre-upgrade Discord smoke test passes. | The app starts on the migrated target; current package graph and publish mechanism are understood; a test bot/guild or an explicit integration-test limitation is recorded. | Gate; migration effort excluded |
| 1. Package/API audit | Recheck NuGet's latest stable version and release notes; confirm all Discord API consumers and whether guild/DM commands are supported. | A minimal package list and intent set are agreed; no unexplained direct use of Commands/Webhook modules remains. | 0.5-1 day |
| 2. Dependency and packaging update | Add the WebSocket package at the selected stable version, prune unused modules, remove stale legacy references/resources, and restore/build the app. | Clean restore/build succeeds; one coherent Discord.Net graph resolves; app output contains the required package assets and no stale Discord 2.2.0 assemblies. | 1-2 days |
| 3. API and intent migration | Resolve compile/API changes; configure Gateway intents; preserve command/admin/action flow and client lifecycle. | Bot reaches Ready, receives intended message types/content, performs existing actions and shuts down without duplicate handlers or unobserved dependency-load errors. | 1-2 days |
| 4. Integration and release qualification | Run the verification matrix against the published app, test existing webhook paths, and document required Developer Portal intent settings. | All acceptance checks pass on the migrated target; deployment output works outside the IDE; unresolved limitations are documented before release. | 1-2 days |

Total: approximately **4-7 working days after the .NET 10 migration**. If the post-migration project still relies on custom embedded assembly loading or an unsupported publish mode, resolve that design before treating the package upgrade as complete.

### Verification matrix

These are future implementation checks, not test results from this investigation. Use focused automated tests for any extracted configuration/authorization logic, plus a test Discord application for Gateway behavior.

1. **Framework/build:** clean restore, Debug and Release builds, and the intended publish flow succeed on the migrated .NET 10 target. Confirm the Discord dependency graph resolves to one release and the output contains all required assemblies.
2. **Login/readiness:** valid test token reaches Ready; invalid or revoked token produces the existing clear failure outcome; shutdown stops the client and does not leave duplicate event subscriptions on a subsequent start.
3. **Gateway intents:** guild messages and/or DMs work according to the recorded product behavior. Message content is received only with the required privileged intent enabled in the Developer Portal. Missing intent is diagnosed rather than mistaken for an authorization or parsing failure.
4. **Authorization:** bot ignores its own messages and non-admin users; global admins and server-scoped grants retain current permissions; an admin cannot act on an ungranted server.
5. **Commands:** check, list, start, stop, restart, send, backup, update and stats preserve their current success, invalid-argument, nonexistent-server and invalid-state responses. Embeds remain valid and actions still run through the existing WindowsGSM dispatcher.
6. **Lifecycle/presence:** Ready profile behavior and server-count activity updates work; stop/restart and app close/reopen do not create duplicate clients or stale activity loops. Disconnect/reconnect and rate-limit cases do not cause server actions to be reported as successful when they failed.
7. **Message behavior:** test guild and DM routing as applicable, bot-self filtering, empty/oversized content, whitespace/prefix parsing and a message with no server ID. Confirm no message is acted on before both admin and server grant checks pass.
8. **Webhook regression:** use the existing webhook test button and a safe test crash-log endpoint to verify alert and error-feed delivery still work; confirm the HTTP webhook path has not accidentally become dependent on `Discord.Net.Webhook`.
9. **Deployment/runtime resolution:** launch the published build from a clean staging directory without Visual Studio or NuGet package folders present. Verify no assembly load failure, duplicate old DLL or dependency mismatch occurs through the custom resolver/publish output.
10. **Configuration/plugin regression:** existing bot token, prefix and admin settings remain readable without migration; the plugin-development project and supported plugin-loading path remain unaffected.

## Deferred scope

- Slash commands, interactions, application-command registration and gateway event redesign.
- Replacing the custom message parser with `Discord.Net.Commands` or introducing a DI-based command framework.
- Replacing `DiscordWebhook` with `Discord.Net.Webhook`, or changing webhook payload, retry or error semantics.
- Token storage redesign, admin UI changes, permission-policy redesign or unrelated bot feature requests.
- Broad NuGet upgrades outside the Discord dependency graph unless the .NET 10 migration or this package's dependency graph requires them; if required, isolate and review those changes.
- Supporting older .NET Framework targets from this upgrade branch. If the .NET 10 prerequisite is delayed, stop and re-scope rather than silently downgrade the requested Discord.Net version.

## Sources and remaining decisions

NuGet metadata and repository references were checked on 21 October 2026. Re-check package availability, target frameworks, release notes and Discord's intent requirements when implementation begins.

- [Discord.Net 3.20.2 on NuGet](https://www.nuget.org/packages/Discord.Net/3.20.2).
- [Discord.Net.WebSocket 3.20.2 package metadata: target frameworks and dependencies](https://api.nuget.org/v3-flatcontainer/discord.net.websocket/3.20.2/discord.net.websocket.nuspec).
- [Discord.Net 3.20.2 metapackage metadata](https://api.nuget.org/v3-flatcontainer/discord.net/3.20.2/discord.net.nuspec).
- [Discord.Net.WebSocket 3.18.0 metadata](https://api.nuget.org/v3-flatcontainer/discord.net.websocket/3.18.0/discord.net.websocket.nuspec), useful only if the .NET 10 prerequisite is not available and a temporary compatible version must be evaluated.
- [Discord Gateway Message Content intent documentation](https://discord.com/developers/docs/topics/gateway#message-content-intent).

Before implementation, confirm whether direct-message commands are a supported workflow, confirm access to a dedicated test bot/guild, and record the final .NET 10 project's package/publish model. Do not broaden intents or grant additional bot permissions without an explicit feature need.