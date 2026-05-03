# Amium.ItemBroker Architecture

## Purpose

`Amium.ItemBroker` is the transport-neutral runtime core for distributed `Amium.Item` communication. It routes item snapshots, value deltas, parameter deltas, subscriptions, removals, writes, retained state, policy decisions, and health data.

It does not replace the HornetStudio `DataRegistry`. The registry remains the local live item store inside HornetStudio. The broker is the shared communication layer for embedded use, service-hosted use, and future external inspection transports.

## Boundaries

- `Amium.Item` defines items, child items, values, and parameters.
- `HornetStudio.Host.DataRegistry` stores live local Host items for one HornetStudio process.
- `Amium.ItemBroker` keeps a slim operation-based broker contract, routed message contracts, retained state, routing, and central broker policies.
- `Amium.ItemBroker.Client` provides application-facing convenience APIs for publishing, writing, subscribing, local diffing, and future throttling or batching.
- `Amium.ItemBroker.Service` starts the broker core as a standalone process.
- `Amium.ItemBroker.Mqtt` maps MQTT topics to broker messages for local inspection and basic write testing.
- Future transport adapters can map additional external protocols such as HTTP or TCP to broker messages.

`Amium.ItemBroker` is not MQTT-specific. MQTT is an adapter concern that exposes broker paths as topics for inspection or external integration.

## Runtime Modes

- Embedded mode: HornetStudio creates the broker in-process and bridges local `DataRegistry` changes to broker messages.
- Local service mode: HornetStudio connects to a broker service on the same machine through a future transport adapter.
- Network broker mode: multiple publishers, clients, and adapters connect to a broker service over transport adapters.
- Inspection mode: external tools connect to a transport adapter that maps broker data to protocol-specific topics or endpoints.

## Dependency Direction

- `Amium.ItemBroker` depends on `Amium.Item`.
- `Amium.ItemBroker.Client` depends on `Amium.ItemBroker`.
- `Amium.ItemBroker.Service` depends on `Amium.ItemBroker`.
- `Amium.ItemBroker.Mqtt` depends on `Amium.ItemBroker` and MQTTnet.
- `HornetStudio.Host` may later depend on `Amium.ItemBroker` for a Host bridge.
- `Amium.ItemBroker` must not depend on `HornetStudio.Host`, Editor, UI, widgets, MQTT, HTTP, or TCP libraries.

## Addressing

Broker paths are canonical item paths. The broker normalizes `.`, `/`, and `\` to dot-separated paths and compares paths case-insensitively. Transport topics are mapping inputs only; adapters translate them into item path plus parameter.

Standard item parameters remain regular item parameters: `Value`, `Unit`, `Format`, `Kind`, `Writable`, `WritePath`, and `WriteMode`.

## Performance Goals

The broker must support high-frequency publishers without forcing every update to become a full item snapshot. A 100 Hz measurement should normally publish value deltas, throttled latest-value updates, batched updates, or cyclic latest-value updates according to policy.

Slow external clients must not block high-frequency publishers. The subscription model therefore includes options for max update rate, batch interval, keep-latest behavior, and slow-client drop policy. The first runtime implementation scaffolds these contracts; deeper queue behavior belongs in later transport and broker work.

## Retained State

The broker is the central authority for retained state. Publishers and the client SDK may optimize what they send, but final retention behavior is decided by broker policy.

Default behavior:

- Normal snapshots and deltas update latest retained state.
- Remove messages clear the retained root and descendants.
- Write requests and acknowledgements are not retained.
- Health values should usually be retained with a short TTL.

Retained data lets new subscribers request an immediate snapshot of the latest known state before live updates arrive.

## Publish Policy

Client-side publish policy decides how a client sends data:

- Full snapshot for first publish or explicit snapshot publishing.
- Value or parameter deltas when state already exists.
- Latest-only publishing for high-frequency values.
- Throttled latest publishing for consumers that need bounded update rates.
- Cyclic publishing for periodic signals.

The default client policy publishes a snapshot when no previous state exists and deltas afterwards.

## Subscription Options

Subscriptions support exact or recursive paths. The options model is prepared for:

- Recursive subscriptions.
- Retained snapshot delivery.
- Maximum update rate.
- Batch interval.
- Keep-latest behavior.
- Slow-client drop policy.

Wildcard syntax is intentionally not part of the first version.

## Slow-Client Handling

The broker and transport adapters should protect publishers from slow consumers. For future queue-backed transports, slow-client behavior should be explicit:

- `None` for reliable in-process delivery.
- `DropOldestKeepLatest` for measurement streams where the newest value matters most.
- `DropNewest` when preserving already queued data is more important than accepting new updates.

External adapters should report dropped-message counts through health items.

## Writes

Writes target item path plus parameter. The default parameter is `Value`. Write requests carry source client id, timestamp, correlation id, and optional reply target. The current implementation routes writes to the client that last published the exact path or nearest retained ancestor.

## External Transports

Transport adapters translate external protocol messages into broker operations and broker messages back into protocol-specific output. Adapters must not move protocol semantics into the core broker.

Potential adapters:

- MQTT for external inspection and lightweight integrations.
- HTTP for request/response tools.
- TCP or WebSocket for custom high-throughput clients.

## MQTT Inspection

MQTT is the first external inspection transport. `Amium.ItemBroker.Mqtt` hosts a local MQTTnet server by default on `127.0.0.1:1883` and exposes an external MQTT view instead of mirroring canonical broker paths directly.

Topic mapping:

- Broker-owned state: `hornet/broker/...`
- Shared item values: `hornet/{item path}`
- Shared item parameters: `hornet/{item path}/params/{parameter}`
- Example: `Runtime.Health.ItemBroker.Heartbeat` parameter `Value` maps to `hornet/broker/heartbeat`.
- Example: `Runtime.Device.Read` parameter `Value` maps to `hornet/Runtime/Device/Read`.
- Example: `Runtime.Device.Read` parameter `Unit` maps to `hornet/Runtime/Device/Read/params/Unit`.

The MQTT item topic shape is shared and does not include the MQTT client id.

Parameter names such as `Value`, `Unit`, `Format`, `Kind`, `Writable`, `WritePath`, and `WriteMode` remain item parameters, not child items. Incoming MQTT publishes are translated back to broker path plus parameter. Publishes on writable shared item topics become broker write requests and are confirmed only when the local owner republishes the accepted value. Non-writable shared-topic writes are rejected by the MQTT adapter. Rejection diagnostics are opt-in through `Amium.ItemBroker.Mqtt.WriteDiagnostics`.

The broker core remains transport-neutral and must not depend on MQTTnet.

## Health Publishing

Broker health should be published as normal broker messages under standard paths:

- `Runtime.Health.ItemBroker.Heartbeat`
- `Runtime.Health.ItemBroker.Uptime`
- `Runtime.Health.ItemBroker.ClientCount`
- `Runtime.Health.ItemBroker.SubscriptionCount`
- `Runtime.Health.ItemBroker.RetainedItemCount`
- `Runtime.Health.ItemBroker.MessagesPerSecond`
- `Runtime.Health.ItemBroker.DroppedMessages`
- `Runtime.Health.ItemBroker.Transport.Mqtt.Status`

Health values should generally be retained with TTL so inspectors can see the latest status while stale health data expires conceptually.

MQTT exposes these health paths as:

- `Runtime.Health.ItemBroker.Heartbeat` -> `hornet/broker/heartbeat`
- `Runtime.Health.ItemBroker.Uptime` -> `hornet/broker/uptime`
- `Runtime.Health.ItemBroker.ClientCount` -> `hornet/broker/clients/count`
- `Runtime.Health.ItemBroker.SubscriptionCount` -> `hornet/broker/subscriptions/count`
- `Runtime.Health.ItemBroker.RetainedItemCount` -> `hornet/broker/retained/count`
- `Runtime.Health.ItemBroker.MessagesPerSecond` -> `hornet/broker/messages/per-second`
- `Runtime.Health.ItemBroker.DroppedMessages` -> `hornet/broker/messages/dropped`
- `Runtime.Health.ItemBroker.Transport.Mqtt.Status` -> `hornet/broker/mqtt/status`

## Client SDK Responsibilities

`Amium.ItemBroker.Client` is the application-facing SDK. It should stay dependency-light and NuGet-ready.

Responsibilities:

- Normalize paths before publishing.
- Provide `PublishSnapshotAsync`, `PublishValueAsync`, `PublishParameterAsync`, `WriteValueAsync`, `WriteParameterAsync`, and `SubscribeAsync`.
- Track local published item state where useful.
- Prefer deltas after an initial snapshot.
- Support future batching, throttling, latest-only, reconnect, and subscription convenience behavior.

The SDK can optimize network traffic, but the broker remains responsible for final retained-state policy.

## Host Bridge Plan

A future `DataRegistryItemBrokerBridge` belongs in `HornetStudio.Host`. It should map `DataRegistry.ItemChanged` events to broker messages and map broker snapshots, deltas, and writes back to `DataRegistry.UpsertSnapshot`, `UpdateValue`, or `UpdateParameter`. Echo loops should be prevented through source ids or bridge-local suppression.
