# Amium.ItemBroker Usage

## Current State

The current broker implementation can be used in-process through `Amium.ItemBroker`. It supports:

- Publishing item snapshots.
- Publishing value and parameter deltas.
- Subscribing to exact or recursive paths.
- Delivering retained snapshots to new subscribers.
- Removing retained paths.
- Routing write requests to the last publisher of a path or nearest ancestor.

`Amium.ItemBroker.Service` starts the broker core and enables the local MQTT inspection transport by default on `127.0.0.1:1883` with broker state below `hornet/broker/...` and client-owned item data below `clients/...`.

## In-Process Broker

Applications can create an in-memory broker directly:

```csharp
var broker = new InMemoryItemBroker();
```

Publishers can send snapshots and deltas:

```csharp
await broker.PublishSnapshotAsync(
    message: new ItemSnapshotMessage(
        Path: "Runtime.Device.Read",
        Item: item,
        SourceClientId: "device-client",
        CorrelationId: null,
        Timestamp: DateTimeOffset.UtcNow));

await broker.PublishValueChangedAsync(
    message: new ItemValueChangedMessage(
        Path: "Runtime.Device.Read",
        Value: 42,
        SourceClientId: "device-client",
        CorrelationId: null,
        Timestamp: DateTimeOffset.UtcNow));
```

Subscribers register an `IItemBrokerClient`:

```csharp
await broker.SubscribeAsync(
    client: client,
    message: new ItemSubscribeMessage(
        Path: "Runtime.Device",
        Recursive: true,
        IncludeRetained: true,
        SourceClientId: client.ClientId,
        CorrelationId: null,
        Timestamp: DateTimeOffset.UtcNow));
```

## Client SDK

`Amium.ItemBroker.Client` adds a convenience session over the core broker:

```csharp
var broker = new InMemoryItemBroker();
var session = new ItemBrokerClientSession(
    clientId: "device-client",
    broker: broker);

await session.PublishItemAsync(item: item);
await session.PublishValueAsync(path: "Runtime.Device.Read", value: 42);
await session.PublishParameterAsync(path: "Runtime.Device.Read", parameterName: "Unit", value: "V");
await session.WriteAsync(path: "Runtime.Device.Write", value: true);
```

Subscriptions can be registered with options:

```csharp
await session.SubscribeAsync(
    path: "Runtime.Device",
    handler: (message, cancellationToken) => Task.CompletedTask,
    options: new ItemSubscriptionOptions
    {
        Recursive = true,
        IncludeRetained = true,
        MaxUpdateRate = 20,
        KeepLatest = true,
        SlowClientDropPolicy = SlowClientDropPolicy.DropOldestKeepLatest,
    });
```

The current SDK publishes a snapshot the first time an item path is sent and value or parameter deltas on later changes. Future work can add transport reconnect handling, batching, and throttling.

## Demo Client

Start `Amium.ItemBroker.Service` first, then run the demo client to publish two demo items to the service MQTT broker at 10 Hz:

```powershell
dotnet run --project src/Amium.ItemBroker.DemoClient/Amium.ItemBroker.DemoClient.csproj
```

The demo publishes `Runtime.Demo.Temperature` and `Runtime.Demo.Pressure` under `clients/demo-client/...`.

## Retained vs Non-Retained Data

Use retained data for state that new subscribers should see immediately:

- Current values.
- Device status.
- Configuration.
- Health status with a TTL.

Use non-retained data for transient events:

- Write requests.
- Acknowledgements.
- One-shot notifications.
- High-volume event streams where only live subscribers should receive events.

The broker is the central authority for retained state. A client SDK may choose efficient message shapes, but broker policy decides whether a message updates retained state.

## High-Frequency Values

For 100 Hz measurements, avoid publishing a full item snapshot on every update. Prefer:

- `PublishValueAsync(...)` for value deltas.
- `PublishParameterAsync(...)` only when metadata changes.
- Latest-only behavior for displays that only need the newest value.
- Throttled latest behavior for external tools.
- Batch intervals for transports that benefit from grouped updates.

## MQTT Explorer

Run the service:

```powershell
dotnet run --project src/Amium.ItemBroker.Service/Amium.ItemBroker.Service.csproj
```

Connect MQTT Explorer to:

- Host: `127.0.0.1`
- Port: `1883`
- Protocol: plain MQTT
- Authentication: none for local development defaults

Subscribe to:

```text
hornet/broker/#
clients/#
```

Broker-owned health output uses compact `hornet/broker/...` topics:

```text
Runtime.Health.ItemBroker.Heartbeat + Value
hornet/broker/heartbeat
```

Client-owned values use the broker message source id as MQTT `clientId`:

```text
Runtime.Device.Read + Value from source client device-client
clients/device-client/Runtime/Device/Read

Runtime.Device.Read + Unit from source client device-client
clients/device-client/Runtime/Device/Read/params/Unit
```

If a broker message has no source id, the MQTT adapter uses `unknown` as the topic client id. The service publishes retained health values immediately so MQTT Explorer shows visible data under `hornet/broker/#`.

Incoming MQTT publishes under `clients/{clientId}/#` are mapped back to broker item path plus parameter. If the item is marked with `Writable=true`, the adapter routes the publish as a broker write request. Otherwise, item-topic updates become value changes and `params/{parameter}` updates become parameter changes.

## Health Data

Broker health should be published as normal item values under:

- `Runtime.Health.ItemBroker.Heartbeat`
- `Runtime.Health.ItemBroker.Uptime`
- `Runtime.Health.ItemBroker.ClientCount`
- `Runtime.Health.ItemBroker.SubscriptionCount`
- `Runtime.Health.ItemBroker.RetainedItemCount`
- `Runtime.Health.ItemBroker.MessagesPerSecond`
- `Runtime.Health.ItemBroker.DroppedMessages`
- `Runtime.Health.ItemBroker.Transport.Mqtt.Status`

Health values should generally be retained with TTL so tools can inspect the latest status without mistaking stale data for live status.

MQTT health topics:

- `hornet/broker/heartbeat`
- `hornet/broker/uptime`
- `hornet/broker/clients/count`
- `hornet/broker/subscriptions/count`
- `hornet/broker/retained/count`
- `hornet/broker/messages/per-second`
- `hornet/broker/messages/dropped`
- `hornet/broker/mqtt/status`
