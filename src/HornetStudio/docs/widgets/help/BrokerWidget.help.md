# BrokerWidget Help

`BrokerWidget`

Connects to an MQTT ItemBroker bus with `BrokerHost`, `BrokerPort`, `BrokerBaseTopic`, and the generated local MQTT client id stored in `BrokerClientId`.

`BrokerMode` is either `External` or `Own`. `External` keeps the previous behavior and connects to an already running broker endpoint. `Own` starts a local in-process ItemBroker with MQTT adapter on the configured endpoint and stops it again when the widget disconnects.

The local client id is generated for widgets, persisted with the layout, and shown as readonly `LocalMqttClientId` in the property dialog. Saved values that do not use the `hornet-studio-{shortGuid}` format are replaced with a generated local id when loaded.

Remote items are published under `Runtime.ItemBroker.{WidgetName}.shared.{ItemPath}` and can be attached to the UI through `BrokerAttachedItemPaths`. The widget body lists these rows under `Attached To UI`. The attach editor groups available paths by remote client and item tree, while saved paths that are no longer live are shown as missing so they can be removed.

`PublishItems` selects local registry roots and stores structured local publish definitions in `BrokerPublishedItemPaths`. The widget body lists these roots under `Published Items`. New selections are inactive by default and default to `Studio.<LocalPath>`, `OnChanged`, `1000` ms, and `Writable=false`.

Published entries use shared flat MQTT topics. For example, `Studio.DefaultLayout.UdlClient1.m400.Set.Request` is visible in MQTT Explorer as `hornet/Studio/DefaultLayout/UdlClient1/m400/Set/Request`, and `Unit` is visible as `hornet/Studio/DefaultLayout/UdlClient1/m400/Set/Request/params/Unit`.

Use `Edit` in `Published Items` to configure the selected root and its subitems. The visible root row is only a grouping and navigation row; only individual rows with `Active=true` publish while connected.

Connect publishes all active entries once. Saving a root publishes retained snapshots only for active entries under that root. `OnChanged` publishes exact active item changes, active subtree roots for descendant changes, and active descendants after ancestor snapshot/upsert refreshes. Ancestor value or parameter changes do not publish unrelated descendants. `Interval` publishes snapshots periodically for active interval entries.

When an active definition has `Writable=true`, external broker updates on the exact configured broker path are written back to the local registry path. `Value` updates are allowed; parameter updates are checked by the central protected-parameter policy, so system metadata such as `Writable`, `WritePath`, `BrokerPath`, `LocalPath`, `Active`, `PublishMode`, and `PublishIntervalMs` cannot be changed through broker write-back. Same-value writes are ignored so shared-topic echoes do not rewrite the local item.
