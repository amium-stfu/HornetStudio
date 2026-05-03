# Amium.ItemBroker Usage

## Current State

The current broker implementation can be used in-process through `Amium.ItemBroker`. It supports:

- Publishing item snapshots.
- Updating values and parameters through one public update model.
- Subscribing to exact or recursive paths.
- Delivering retained snapshots to new subscribers.
- Removing retained paths.
- Protecting existing item owners from implicit overwrite.
- Routing non-owner updates to the registered owner.

`Amium.ItemBroker.Service` starts a reusable selfhosted MQTT ItemBroker host on `127.0.0.1:1883` by default. The host publishes broker-owned health state below `hornet/broker/...` and accepts shared item participation below `hornet/...`.

## In-Process Broker

Applications can create an in-memory broker directly:

```csharp
var broker = new InMemoryItemBroker();
```

Publishers can register ownership with a snapshot and then send later updates without constructing transport-style messages:

```csharp
await broker.PublishSnapshotAsync(
    item: item,
    retained: true,
    sourceClientId: "device-client");

await broker.UpdateValueAsync(
    item: new Item("Read", 42).Repath("Runtime.Device.Read"),
    sourceClientId: "device-client");
```

Ownership is explicit:

- The first snapshot for a path registers the owner.
- The same owner can publish the snapshot again to refresh it.
- A different owner cannot claim an already owned path implicitly.
- Later value and parameter changes should use `UpdateValueAsync(...)` and `UpdateParameterAsync(...)`.

Subscribers register an `IItemBrokerClient`:

```csharp
await broker.SubscribeAsync(
    client: client,
    path: "Runtime.Device",
    options: new ItemSubscriptionOptions
    {
        Recursive = true,
        IncludeRetained = true,
    });
```

## Client SDK

`Amium.ItemBroker.Client` adds a convenience session over the core broker:

```csharp
var broker = new InMemoryItemBroker();
var session = new ItemBrokerClientSession(
    clientId: "device-client",
    broker: broker);

await session.PublishSnapshotAsync(item: item);
await session.UpdateValueAsync(item: new Item("Read", 42).Repath("Runtime.Device.Read"));
await session.UpdateParameterAsync(item: item, parameterName: "Unit");
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

If the session owns the target path, `Update...` applies the change locally in the broker. If another owner is registered, the broker turns the update into a routed write request for that owner. Future work can add transport reconnect handling, batching, and throttling.

## Demo Client

Start `Amium.ItemBroker.Service` first, then run the demo client to publish two demo items to the service MQTT broker at 10 Hz:

```powershell
dotnet run --project src/Amium.ItemBroker.DemoClient/Amium.ItemBroker.DemoClient.csproj
```

The demo publishes `Runtime.Demo.Temperature` and `Runtime.Demo.Pressure` under `hornet/Runtime/Demo/...`.

## Selfhosted MQTT Host

Applications that want to host the broker and MQTT surface without HornetStudio can use `Amium.ItemBroker.Mqtt` directly:

```csharp
var host = new MqttItemBrokerHost(new MqttItemBrokerOptions
{
    Host = "127.0.0.1",
    Port = 1883,
    BaseTopic = "hornet",
    ClientId = "device-broker",
});

await host.StartAsync();
```

The host owns an `IItemBroker` instance through `host.Broker` and publishes health items automatically unless `PublishHealth` is disabled.

Low-level `ItemBrokerMessage` records still exist for routed broker events, retained snapshots, acknowledgements, and transport adapters, but normal in-process broker usage should prefer the slim operation-based API above.

## Remote MQTT Client

Applications that want to join an existing MQTT-backed ItemBroker can use the lower-level session or the higher-level remote client facade from `Amium.ItemBroker.Mqtt.Client`:

```csharp
await using var client = new MqttRemoteItemClient(new MqttItemBrokerClientOptions
{
    Host = "127.0.0.1",
    Port = 1883,
    BaseTopic = "hornet",
    ClientId = "monitoring-app",
});

await client.ConnectAsync();
var roots = client.GetRemoteItemSnapshots();
```

`MqttRemoteItemClient` keeps remote item snapshots for external consumers, supports publishing local snapshots, and hides the same paths from its visible remote mirror so hybrid scenarios do not reflect their own published items back as remote data.

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

- `UpdateValueAsync(...)` for later value changes after snapshot registration.
- `UpdateParameterAsync(...)` only when metadata changes.
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
hornet/#
```

Broker-owned health output uses compact `hornet/broker/...` topics:

```text
Runtime.Health.ItemBroker.Heartbeat + Value
hornet/broker/heartbeat
```

Shared client-owned values use flat item topics:

```text
Runtime.Device.Read + Value from source client device-client
hornet/Runtime/Device/Read

Runtime.Device.Read + Unit from source client device-client
hornet/Runtime/Device/Read/params/Unit
```

The service publishes retained health values immediately so MQTT Explorer shows visible data under `hornet/broker/#`.

Incoming MQTT publishes under `hornet/#` are mapped back to broker item path plus parameter. A publish on the same shared topic is treated as a write attempt only when the item is marked with `Writable=true`; the local owner must then accept the change and republish the confirmed value. Non-writable item-topic publishes are rejected instead of being applied as competing state. Rejection diagnostics can be enabled with the `Amium.ItemBroker.Mqtt.WriteDiagnostics` AppContext switch.

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
