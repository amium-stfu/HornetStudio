# BrokerWidget

`BrokerWidget` connects to an MQTT ItemBroker bus and exposes attached remote MQTT items under `Studio.<FolderName>.{WidgetName}.Mqtt.{ItemPath}`. The internal MQTT shared root marker is not shown in the visible path, so a shared broker item such as `Edm1.Pressure` appears as `BrokerWidget1.Mqtt.Edm1.Pressure`.

`BrokerMode` controls where the bus comes from. `External` connects to the configured `BrokerHost` and `BrokerPort` without starting anything locally. `Own` starts an in-process `InMemoryItemBroker` with the MQTT adapter on the configured endpoint, then connects the widget client to that local endpoint.

Widgets use a generated local MQTT client id such as `hornet-studio-1a2b3c4d`. The id is stored in the persisted `BrokerClientId` property and displayed as readonly `LocalMqttClientId` in the property dialog so it cannot accidentally be replaced with a remote client id.

The widget body is split by direction. `Attached To UI` lists remote broker items that are exposed into HornetStudio. `Published Items` lists local registry roots that may publish from HornetStudio to the broker.

The `AttachToUi` editor displays remote paths as a compact transport-aware tree and keeps the selected broker item identities in `BrokerAttachedItemPaths`. Saved legacy paths below `Runtime.ItemBroker...shared...` are still recognized, but live received items are registered below the widget's `Mqtt` branch.

The `PublishItems` editor selects local registry roots and stores structured local publish definitions in `BrokerPublishedItemPaths`. New selections appear in `Published Items` but are inactive by default. The body row is only a grouping and navigation row for that local root; the actual publish units are the individual active definitions configured through `Edit`. Use the row `Edit` action to enable `Active` for the root or any subitem, choose `PublishMode`, set `PublishIntervalMs`, and store the future-facing `Writable` flag.

`Status.AttachOptions` is an internal discovery branch used by the attach dialog and is hidden from normal item-tree/runtime data display. `Published Items` are managed through the Broker widget publish UI and are not duplicated as normal received item-tree entries.

Active local entries are published one-way to the MQTT ItemBroker when the widget is connected. New definitions default to broker path `Studio.<LocalPath>`, `Active` `false`, `PublishMode` `OnChanged`, `PublishIntervalMs` `1000`, and `Writable` `false`.

Published values use flat shared MQTT topics. With `BrokerBaseTopic=hornet`, the broker path `Studio.DefaultLayout.UdlClient1.m400.Set.Request` publishes to `hornet/Studio/DefaultLayout/UdlClient1/m400/Set/Request`. Parameters stay below the item topic, for example `hornet/Studio/DefaultLayout/UdlClient1/m400/Set/Request/params/Unit`. The topic does not contain the MQTT client id, so every client reads and writes the same shared item topic in MQTT Explorer.

Snapshots are scoped to active definitions. Connect publishes all active entries once. Saving a `Published Items` root publishes retained snapshots only for active definitions under that saved root. `Interval` publishes retained snapshots only for active interval entries. `OnChanged` publishes the exact active entry that changed, an active subtree root when one of its descendants changes, or an active descendant after an ancestor snapshot/upsert refreshes it. Ancestor value and parameter updates do not publish unrelated active descendants.

`PublishMode` supports `OnChanged` for registry change publishing and `Interval` for periodic snapshot publishing. `Writable=false` is the safe default. When a connected active definition has `Writable=true`, external broker updates on its exact flat `BrokerPath` topic are written back to the configured `LocalPath`. `Value` updates write the local item value. Parameter updates use the central `HostRegistryParameterPolicy`, so protected system metadata such as `Writable`, `WritePath`, `BrokerPath`, `LocalPath`, `Active`, `PublishMode`, and `PublishIntervalMs` is blocked. Incoming writes that already match the local value are ignored to prevent shared-topic echoes. Inactive or non-writable definitions are ignored for write-back and do not publish. Definitions are not removed from retained broker state automatically.

## Properties

- `BrokerHost`
- `BrokerPort`
- `BrokerBaseTopic`
- `BrokerClientId` (generated local MQTT client id, shown as readonly `LocalMqttClientId`)
- `BrokerMode` (`External` or `Own`)
- `BrokerAutoConnect`
- `BrokerAttachedItemPaths`
- `BrokerPublishedItemPaths` (JSON definitions configured through `PublishItems` and `Published Items`: `LocalRootPath`, `LocalPath`, `BrokerPath`, `Active`, `PublishMode`, `PublishIntervalMs`, `Writable`)
