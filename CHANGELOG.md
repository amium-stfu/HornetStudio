# Changelog

## Unreleased

- Added `Amium.ItemBroker.MqttDemoWinForms`, a WinForms MQTT demo with local service start/stop, two live demo publishers, one writable demo item, and in-app status/log output.
- Reorganized `AGENTS.md` into a shorter priority-based structure and replaced the flat handoff/debug guidance with `docs/workitems/<timestamp>-<slug>/...` workitem rules triggered by `PLAN`.
- Added `TODO` mode guidance for creating standalone backlog entries under `docs/todos/`.
- Reduced remaining redundancy in `AGENTS.md` by consolidating overlapping scope and workflow guidance.
- Tightened `PLAN` and implementation handoff guidance so plans remain human-readable while handoffs are more execution-ready for other models.
- Added the initial `Amium.ItemBroker` project with transport-neutral broker contracts, message contracts, retained in-memory state, subscription routing, and write routing.
- Added `Amium.ItemBroker.Client` SDK scaffolding, broker publish/retention policy contracts, subscription options, and health path contracts.
- Added ItemBroker usage documentation covering current in-process usage, retained data guidance, and planned MQTT inspection.
- Added the standalone `Amium.ItemBroker.Service` scaffold and focused `Amium.ItemBroker.Tests`.
- Added `Amium.ItemBroker` architecture documentation.
- Added `Amium.ItemBroker.Mqtt` with MQTTnet-based local MQTT inspection, topic mapping, incoming publish mapping, service health publishing, and focused adapter tests.
- Added `Amium.ItemBroker.DemoClient` as a small 10 Hz in-process publishing template for two demo items.
- Added recursive MQTT client publishing for item snapshots and value updates with focused coverage for child item topics.
- Added MQTT ItemBroker client subscriptions, remote retained item reconstruction, direct retained writes, and BaseTopic-aware item topic mapping.
- Added a HornetStudio Broker widget that exposes MQTT ItemBroker runtime items under `Runtime.ItemBroker.{WidgetName}.{RemoteClientId}.{ItemPath}`.
- Added generated readonly local MQTT client ids for Broker widgets and normalized older remote-client values on load.
- Fixed Broker widget retained MQTT item loading during connection and regenerated widget ids on layout load.
- Accepted legacy incoming MQTT value topics with trailing `/value` for Broker widget remote item discovery.
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
- Added reusable `MqttItemBrokerHost` and `MqttRemoteItemClient` facades so selfhosted, remote, and hybrid MQTT ItemBroker scenarios are consumable without HornetStudio-specific classes.
- Slimmed `HostItemBrokerClient` down to a HornetStudio composition layer over the reusable MQTT remote client facade.

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
