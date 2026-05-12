# Changelog

## Unreleased

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
- Added a HornetStudio Broker widget that exposes MQTT ItemBroker runtime items under `Runtime.ItemBroker.{WidgetName}.{RemoteClientId}.{ItemPath}`.
- Added generated readonly local MQTT client ids for Broker widgets and normalized older remote-client values on load.
- Fixed Broker widget retained MQTT item loading during connection and regenerated widget ids on layout load.
- Removed legacy incoming MQTT value topic handling with trailing `/value`; the primary channel now uses `/read`.
- Added MQTT subscribe and receive diagnostics for Broker widget debugging.
- Added central `IDataRegistry.TryResolve` item path resolving for root and descendant items.
- Updated signal, chart, logger, custom signal, UDL exposure, and UI target lookups to use the central resolver.
- Added indexed descendant item resolving, canonical data-change keys, and focused host registry tests.
- Added explicit Broker widget `BrokerMode` support for external endpoints or widget-owned in-process MQTT ItemBroker instances.
- Added protected host registry parameter policy with picker filtering and guarded user parameter writes.
- Hid the normal widget `Parameter` property in editor dialogs and defaulted invalid target parameter paths to `Value`.
- Added Broker widget write-back for active published definitions with `Writable=true`, protected by the central host registry parameter policy.
- Changed Broker widget MQTT publishing and write-back to use shared flat item topics such as `hornet/Studio/.../Request`.
- Fixed broker write-back numeric payloads so MQTT integers can update existing floating-point target values.
- Added host data registry item roles/capabilities and changed Broker widget publish selection to show only explicitly publishable registry items.
- Hid Broker widget internal status/options registry items from publish selection and filtered self-published broker items from received remote items.
- Prevented Broker widget received broker items from being offered again in `PublishItems` while keeping them visible and attachable.
- Changed new Broker widget published item defaults and documentation examples to use `Studio.<LocalPath>` broker paths while preserving existing explicit `HornetStudio.*` paths.
- Normalized project/runtime item paths to the canonical `Studio.<Folder>...` root while preserving legacy `Project.<Folder>...` resolution.
- Changed Broker widget received MQTT item registration to use `Studio.<Folder>.<BrokerWidget>.Mqtt...` paths while preserving legacy shared attach selections.
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
