# Amium.Item.Server Usage

## Current State

The current broker implementation can be used in-process through `Amium.Item.Server`. It supports:

- Publishing item snapshots.
- Updating values and parameters through one public update model.
- Subscribing to exact or recursive paths.
- Delivering retained snapshots to new subscribers.
- Removing retained paths.
- Protecting existing item owners from implicit overwrite.
- Routing non-owner updates to the registered owner.

`Amium.Item.Server` starts a reusable selfhosted MQTT ItemBroker host on `127.0.0.1:1883` by default. The host publishes broker-owned health state below `hornet/broker/...` and accepts shared item participation below `hornet/...`.

## In-Process Broker

Applications can create an in-memory broker directly:

```csharp
var broker = new InMemoryItemServer();
```

Publishers can register ownership with a snapshot and then send later updates without constructing transport-style messages:

```csharp
await broker.PublishSnapshotAsync(
    item: item,
    retained: true,
    sourceClientId: "device-client");

await broker.UpdateValueAsync(
    item: new Item("Read", 42).Repath("runtime.Device.Read"),
    sourceClientId: "device-client");
```

Ownership is explicit:

- The first snapshot for a path registers the owner.
- The same owner can publish the snapshot again to refresh it.
- A different owner cannot claim an already owned path implicitly.
- Later value and parameter changes should use `UpdateValueAsync(...)` and `UpdateParameterAsync(...)`.

Subscribers register an `IItemServerClient`:

```csharp
await broker.SubscribeAsync(
    client: client,
    path: "runtime.Device",
    options: new ItemSubscriptionOptions
    {
        Recursive = true,
        IncludeRetained = true,
    });
```

## Client SDK

`Amium.Item.Client` adds a convenience session over the core broker:

```csharp
var broker = new InMemoryItemServer();
var session = new ItemClientSession(
    clientId: "device-client",
    broker: broker);

await session.PublishSnapshotAsync(item: item);
await session.UpdateValueAsync(item: new Item("Read", 42).Repath("runtime.Device.Read"));
await session.UpdateParameterAsync(item: item, parameterName: "Unit");
```

Subscriptions can be registered with options:

```csharp
await session.SubscribeAsync(
    path: "runtime.Device",
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

Start `Amium.Item.Server` first, then run the demo client to publish two demo items to the service MQTT broker at 10 Hz:

```powershell
dotnet run --project src/Amium.Item.Server.DemoClient/Amium.Item.Server.DemoClient.csproj
```

The demo publishes `runtime.Demo.Temperature` and `runtime.Demo.Pressure` under `hornet/Runtime/Demo/...`.

## Selfhosted MQTT Host

Applications that want to host the broker and MQTT surface without HornetStudio can use `Amium.Item.Server.Mqtt` directly:

```csharp
var host = new MqttItemServerHost(new MqttItemServerOptions
{
    Host = "127.0.0.1",
    Port = 1883,
    BaseTopic = "hornet",
    ClientId = "device-broker",
});

await host.StartAsync();
```

The host owns an `IItemServer` instance through `host.Broker` and publishes health items automatically unless `PublishHealth` is disabled.

Low-level `ItemServerMessage` records still exist for routed broker events, retained snapshots, acknowledgements, and transport adapters, but normal in-process broker usage should prefer the slim operation-based API above.

## Remote MQTT Client

Applications that want to join an existing MQTT-backed ItemBroker can use the lower-level session or the higher-level remote client facade from the `Amium.Item.Client` assembly under the `Amium.Item.Client.Mqtt` namespace:

```csharp
await using var client = new MqttRemoteItemClient(new MqttItemClientOptions
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

The first flattening step intentionally keeps the existing `Amium.Item.Client.Mqtt` namespace for source compatibility, but the project/assembly dependency is now `Amium.Item.Client` only.

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
dotnet run --project src/Amium.Item.Server/Amium.Item.Server.csproj
```

Connect MQTT Explorer to:

- Host: `127.0.0.1`
- Port: `1883`
- Protocol: plain MQTT
- Authentication: none for local development defaults

Inspect system and item topics:

```text
$SYS/#
hornet/#
```

Broker-owned system output uses compact `$SYS/...` topics:

```text
sys.status.state + read
$SYS/status/state
```

Shared client-owned items use the item topic for `meta` JSON, `/read` for the item value, and direct child topics for properties:

```text
runtime.Device.Read + meta from source client device-client
hornet/runtime/device/read

runtime.Device.Read + read from source client device-client
hornet/runtime/device/read/read

runtime.Device.Read + unit from source client device-client
hornet/runtime/device/read/unit
```

The service publishes retained system values immediately so MQTT Explorer shows visible data under `$SYS/#`.

Incoming MQTT publishes under `hornet/#` are mapped back to broker item path plus property. A publish on the same shared topic is treated as a write attempt only when the item is marked with `writable=true`; the local owner must then accept the change and republish the confirmed value. Non-writable item-topic publishes are rejected instead of being applied as competing state. Rejection diagnostics can be enabled with the `Amium.Item.Server.Mqtt.WriteDiagnostics` AppContext switch.

## Health Data

Broker health should be published as system item values under:

- `sys.status.state`
- `sys.status.uptime_seconds`
- `sys.status.started_at_utc`
- `sys.status.last_updated_utc`
- `sys.metrics.item_count`
- `sys.metrics.memory_working_set_mb`
- `sys.metrics.memory_managed_heap_mb`
- `sys.metrics.cpu_usage_percent`
- `sys.mqtt.status.state`
- `sys.mqtt.status.client_count`
- `sys.mqtt.status.endpoint`
- `sys.mqtt.status.last_error`

Health values should generally be retained with TTL so tools can inspect the latest status without mistaking stale data for live status.

MQTT health topics:

- `$SYS/status/state`
- `$SYS/status/uptime_seconds`
- `$SYS/status/started_at_utc`
- `$SYS/status/last_updated_utc`
- `$SYS/metrics/item_count`
- `$SYS/metrics/memory_working_set_mb`
- `$SYS/metrics/memory_managed_heap_mb`
- `$SYS/metrics/cpu_usage_percent`
- `$SYS/mqtt/status/state`
- `$SYS/mqtt/status/client_count`
- `$SYS/mqtt/status/endpoint`
- `$SYS/mqtt/status/last_error`
