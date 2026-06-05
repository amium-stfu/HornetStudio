# Changelog

## Unreleased

- Fixed UDL client writeback handling so `State`, `Read`, `Set`, and `Out` `.write` changes use the same pending retry and acknowledgement path.
- Added an initial `src/HornetStudio/docs/manual/` handbook structure, documented the host-registry interaction model for shared help/PDF source content, and updated repository documentation maintenance rules for manual, widget description, and widget help pages.
- Changed the Functions widget catalog to use compact rows with consistent `YAML`/`Python` type badges, quiet status text, a scrollable list body, and a single `Run`/`Stop` row action toggle.
- Fixed the Functions widget row state so declarative functions started or stopped through Button Interaction Rules update the matching `Running`/`Stopping` display.
- Added the first WorkflowWidget editor flow with dialog-based create/edit/delete actions for workflow YAML files, flat `Log`/`SetValue`/`Delay` step editing, read-only preservation of existing `IfThenElse` steps, and updated widget help text.
- Replaced the unfinished dialog-screen model with `DialogWidget` overlays targeted by `OpenDialog(dialogWidgetId, origin = Screen, position = Center)` and `CloseDialog(dialogWidgetId)`.
- Fixed Item client host-write priority so declarative YAML `SetValue` writes and Python host-value writes to resolved writable targets are not reverted by stale broker `read` or property state.
- Fixed Item client writable publish definitions so recent local Host writes keep short-term priority over conflicting retained or echoed MQTT state, while external non-retained MQTT `write` requests remain allowed.
- Changed `VisualRules` version 1 to a constrained widget-specific surface: `Signal` and `Item` now expose only `BodyBackColor`, `Button` exposes only `ButtonBackColor`, `CircleDisplay` exposes only `DisplayBackColor`, unsupported widgets no longer show a Visual editor, and legacy body `Background` rules continue to load for compatible widgets.
- Changed LogControl-owned process log files to use a project-local `Logs/<folder>/<widget>/` directory when the project root can be resolved.
- Fixed LogControl-owned process log entries so item-driven writes are persisted to the log file and auto-created `TargetLog` values are saved as folder-relative `logs.<widget>` paths.
- Simplified LogControl behavior so each widget always owns and displays its own process log instead of exposing `TargetLog` and `AutoCreateLog` as user-facing choices.
- Added LogControl-owned process logs with writable `debug`, `info`, `warning`, `error`, and `fatal` runtime input items.
- Fixed Item client publish/write-back feedback by omitting command-like `write` properties from retained snapshots, ignoring property-style `write` state on write-back, consuming self-published live `write` echoes once, and preserving notifications for repeated external live `write` requests.
- Changed Item clients to use an empty MQTT `BrokerBaseTopic` by default, allow intentionally empty saved values, and pass empty base topics through to the Item client/server so unprefixed retained topics are visible.
- Documented Item client write-back semantics explicitly: external `.../write` requests must be non-retained, retained `.../write` messages are reconstructed only as broker state, and accidental retained write values should be cleared explicitly.
- Fixed Item client receive discovery so MQTT `/read` values reconstructed as item values are offered as attachable UI items, and added compact receive diagnostics that compare direct received roots, visible roots, and attach options.
- Changed Item client received item paths to omit the `.Mqtt` transport segment, so attach identities now use flat paths such as `item_client1.edm1.pressure` while older `.Mqtt` selections are still normalized.
- Fixed Item client snapshot publishing so local HornetStudio metadata properties with non-MQTT names such as CamelCase signal diagnostics are skipped instead of repeatedly failing Item client topic validation and flooding the host log.
- Kept the normal Item and Signal widget `Property` editor field hidden while keeping `write` out of display-property pickers, and documented that runtime input automatically writes to an item's `write` property when available before falling back to `read`.
- Removed the legacy `tools/` tree, including the old Amium multimaster demo build output and the obsolete `Item.Server.Monitor` project, and detached the monitor from the solution and host tests.
- Migrated HornetStudio from embedded `amium_item/` source and artifact DLL references to fixed-version internal NuGet packages `Amium.Item`, `Amium.Item.Client`, and `Amium.Item.Server` from the configured `amium-at` feed, and simplified the local build flow to restore and build `HornetStudio.sln` directly.
- Reworked the default `Amium.Item.Server` MQTT host path into a lean embedded MQTT item host: it no longer creates an implicit in-memory item broker for normal MQTT traffic, publishes core and MQTT health directly as retained `$SYS/...` topics, keeps only cheap visible-topic/client metrics for health, and documents MQTT retained topics plus client-side reconstruction as the current last-known-state source.
- Modularized the root agent instructions into focused `/agents/*.md` rule files, kept root `AGENTS.md` as the authoritative entry point, simplified Copilot instructions to route to the shared rule modules, and added reproducible debug validation plus autonomous stop-condition rules.
- Simplified the `Amium.Item.Server` MQTT adapter so inbound shared-topic publishes now map directly to generic broker updates without server-side `writable` or `write_path` enforcement, keeping those fields as application metadata and leaving Mesh-specific behavior in `Amium.Item.Mesh`.
- Slimmed the `Amium.Item.Client` MQTT surface by removing the thin client-side `MqttItemTopicMapper` wrapper, centralizing client MQTT payload/topic helpers internally, keeping `MqttRemoteItemClient` as the primary MQTT facade, and moving Mesh-specific MQTT session ownership into an internal `MeshMqttNodeClient`.
- Moved the standalone Amium.Item codebase into `amium_item/`, added artifact export to `artifacts/amium_item/{debug,release}`, switched HornetStudio-side Amium references from `ProjectReference` to artifact DLL references, and split the local build flow into `amium_item/Amium.Item.sln` followed by `HornetStudio.sln`.
- Added a dedicated `Amium.Item.sln`, moved Amium.Item tests to `tests/`, demos to `samples/`, operational utilities to `tools/`, removed obsolete `.__merge` folders, and extracted reusable multimaster mesh orchestration into the new `Amium.Item.Mesh` library while keeping the sample UI in `samples/amium_item_server_multimaster_demo`.
- Split shared broker contracts, message records, canonical broker path/value helpers, health path constants, and subscription options into `Amium.Item.Server.Abstractions`; moved the shared MQTT topic mapping back into `Amium.Item`; kept `Amium.Item` focused on the core item model; and updated projects, samples, tools, solution membership, and docs to use the simplified assembly boundaries while preserving existing source namespaces.
- Fixed multimaster mesh read mirroring so repeated identical mirror snapshots no longer create update storms, mesh cross-writes converge, runtime-created items are mirrored, and observed MQTT property imports no longer replace existing item values with null snapshots.
- Added Phase 2 mesh read mirroring so each multimaster demo broker republishes peer-owned items locally, MQTT Explorer can inspect the full mesh tree from any node broker, and the mesh self-test now verifies broker-level mirrored visibility alongside observer visibility.
- Added a mesh-style multimaster demo self-test with three local MQTT Item Servers, observer-only peer visibility, cross-node writes through dedicated writer sessions, and separate mesh JSONL/summary logs.
- Moved core item server health publishing into a reusable `ItemServerHealthPublisher`, kept MQTT-specific health in `MqttItemServerHost`, and started the same core health publisher inside `Item.Server.Monitor` so local monitor runtimes expose `sys.*` health without requiring the MQTT host facade.
- Extended `Item.Server.Monitor` with a local `InMemoryItemServer`, manual adapter lifecycle management, a transport-neutral monitor host, MQTT start/stop controls, adapter status/error display, and a follow-up TODO for future `config.yaml` startup support.
- Added `Item.Server.Monitor`, a standalone Avalonia monitor for `HostRegistries` with a flat throttled item tree, live value display, search filter, freeze control, update interval selection, and a selected-item detail panel.
- Clarified the MQTT stress test load and backlog metrics by renaming the UI labels to `Signal values`, `Total updates/s`, `Pending`, and `Max pending`, adding a derived average updates-per-value display, and replacing the final assessment log with a PASS/WARN/FAIL block for delivery, throughput, latency, backlog, and load profile.
- Added `Amium.Item.Server.MqttStressTest`, a standalone WinForms MQTT stress test tool with configurable load generation, raw MQTT receive measurement, delivery counters, throughput, and latency metrics.
- Aligned `AGENTS.md` and `.github/copilot-instructions.md` to eliminate redundant and competing rules: `AGENTS.md` is now the primary cross-tool rule source; `.github/copilot-instructions.md` contains only Copilot-specific context and project domain rules.
- Renamed the `Amium.Items.Item.Params` API to `Properties` and updated internal callers and documentation.
- Renamed `Amium.Items.Parameter` to `ItemProperty`, `ParameterDictionary` to `ItemPropertyDictionary`, and aligned the `Amium.Item` helper API naming.
- Changed `Amium.Items.Item` to use `read` as the primary value property with an optional `write` channel, and updated ItemBroker/MQTT defaults from `/value` to `/read`.
- Added `Amium.Item.Server.MqttDemoWinForms`, a WinForms MQTT demo with local service start/stop, two live demo publishers, one writable demo item, and in-app status/log output.
- Reorganized `AGENTS.md` into a shorter priority-based structure and replaced the flat handoff/debug guidance with `docs/workitems/<timestamp>-<slug>/...` workitem rules triggered by `PLAN`.
- Added `TODO` mode guidance for creating standalone backlog entries under `docs/todos/`.
- Reduced remaining redundancy in `AGENTS.md` by consolidating overlapping scope and workflow guidance.
- Tightened `PLAN` and implementation handoff guidance so plans remain human-readable while handoffs are more execution-ready for other models.
- Added the initial `Amium.Item.Server` project with transport-neutral broker contracts, message contracts, retained in-memory state, subscription routing, and write routing.
- Added `Amium.Item.Client` SDK scaffolding, broker publish/retention policy contracts, subscription options, and health path contracts.
- Added ItemBroker usage documentation covering current in-process usage, retained data guidance, and planned MQTT inspection.
- Added the standalone `Amium.Item.Server` scaffold and focused `Amium.Item.Server.Tests`.
- Added `Amium.Item.Server` architecture documentation.
- Added `Amium.Item.Server.Mqtt` with MQTTnet-based local MQTT inspection, topic mapping, incoming publish mapping, service health publishing, and focused adapter tests.
- Added `Amium.Item.Server.DemoClient` as a small 10 Hz in-process publishing template for two demo items.
- Added recursive MQTT client publishing for item snapshots and value updates with focused coverage for child item topics.
- Added MQTT ItemBroker client subscriptions, remote retained item reconstruction, direct retained writes, and BaseTopic-aware item topic mapping.
- Added a HornetStudio Item client that exposes MQTT ItemBroker runtime items under `Runtime.ItemBroker.{WidgetName}.{RemoteClientId}.{ItemPath}`.
- Added generated readonly local MQTT client ids for Item clients and normalized older remote-client values on load.
- Fixed Item client retained MQTT item loading during connection and regenerated widget ids on layout load.
- Removed legacy incoming MQTT value topic handling with trailing `/value`; the primary channel now uses `/read`.
- Added MQTT subscribe and receive diagnostics for Item client debugging.
- Added central `IDataRegistry.TryResolve` item path resolving for root and descendant items.
- Updated signal, chart, logger, custom signal, UDL exposure, and UI target lookups to use the central resolver.
- Added indexed descendant item resolving, canonical data-change keys, and focused host registry tests.
- Added explicit Item client `BrokerMode` support for external endpoints or widget-owned in-process MQTT ItemBroker instances.
- Added protected host registry parameter policy with picker filtering and guarded user parameter writes.
- Hid the normal widget `Parameter` property in editor dialogs and defaulted invalid target parameter paths to `Value`.
- Added Item client write-back for active published definitions with `Writable=true`, protected by the central host registry parameter policy.
- Changed Item client MQTT publishing and write-back to use shared flat item topics such as `hornet/Studio/.../Request`.
- Fixed broker write-back numeric payloads so MQTT integers can update existing floating-point target values.
- Added host data registry item roles/capabilities and changed Item client publish selection to show only explicitly publishable registry items.
- Hid Item client internal status/options registry items from publish selection and filtered self-published broker items from received remote items.
- Prevented Item client received broker items from being offered again in `PublishItems` while keeping them visible and attachable.
- Changed new Item client published item defaults and documentation examples to use `Studio.<LocalPath>` broker paths while preserving existing explicit `HornetStudio.*` paths.
- Normalized project/runtime item paths to the canonical `Studio.<Folder>...` root while preserving legacy `Project.<Folder>...` resolution.
- Changed Item client received MQTT item registration to use `Studio.<Folder>.<ItemClient>...` paths while preserving legacy shared and `.Mqtt` attach selections.
- Added reusable `MqttItemServerHost` and `MqttRemoteItemClient` facades so selfhosted, remote, and hybrid MQTT ItemBroker scenarios are consumable without HornetStudio-specific classes.
- Slimmed `HostItemBrokerClient` down to a HornetStudio composition layer over the reusable MQTT remote client facade.
- Changed ItemBroker MQTT item topics so the main item topic carries `meta` JSON, `/read` carries `Item.Value`, direct child topics carry properties, and an empty `BaseTopic` removes the prefix.
- Changed ItemBroker system data publishing to use `$SYS/status`, `$SYS/metrics`, and `$SYS/mqtt/status` instead of the old heartbeat/runtime health hierarchy.

## 2026.04.28.0110

- Generate first-start default layouts from the folder template.
- Create `Assets` and `Scripts` directories for first-start default layouts.

## 2026.04.28.0046

- Renamed the item and UDL client projects to `Amium.Item` and `Amium.UdlClient`.
- Renamed the solution, projects, namespaces, resource URIs, and documentation references to HornetStudio.
- Added numbered default widget names with validation for allowed characters.
- Set new widget text defaults to the generated widget name.
- Keep default widget text synchronized with the generated name after target changes.
- Moved editor dialog validation errors above the tab content.
- Start Windows camera devices lazily only when a camera widget subscribes to frames.
