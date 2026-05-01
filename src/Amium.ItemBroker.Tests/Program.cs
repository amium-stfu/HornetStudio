using Amium.Item;
using Amium.ItemBroker;
using Amium.ItemBroker.Client;
using Amium.ItemBroker.Mqtt;
using MQTTnet.Server;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MqttClientOptions = Amium.ItemBroker.Mqtt.Client.MqttItemBrokerClientOptions;
using MqttClientSession = Amium.ItemBroker.Mqtt.Client.MqttItemBrokerClientSession;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Path normalization", PathNormalization),
    ("Snapshot publish and retained state", SnapshotPublishAndRetainedState),
    ("Value update routing", ValueUpdateRouting),
    ("Retained value update converts numeric payloads", RetainedValueUpdateConvertsNumericPayloads),
    ("Parameter update routing", ParameterUpdateRouting),
    ("Retained parameter update converts numeric payloads", RetainedParameterUpdateConvertsNumericPayloads),
    ("Recursive subscription", RecursiveSubscription),
    ("Remove clears descendants", RemoveClearsDescendants),
    ("Write request routing", WriteRequestRouting),
    ("Retention policy decisions", RetentionPolicyDecisions),
    ("Publish policy snapshot and delta decisions", PublishPolicySnapshotAndDeltaDecisions),
    ("Client publish path normalization", ClientPublishPathNormalization),
    ("Client publish item value", ClientPublishItemValue),
    ("Client publish item parameter", ClientPublishItemParameter),
    ("High frequency latest retained value", HighFrequencyLatestRetainedValue),
    ("MQTT topic mapping", MqttTopicMapping),
    ("MQTT topic mapping with base topic", MqttTopicMappingWithBaseTopic),
    ("MQTT shared topic mapping ignores client id", MqttSharedTopicMappingIgnoresClientId),
    ("MQTT remote registry rebuild", MqttRemoteRegistryRebuild),
    ("MQTT remote registry converts numeric payloads", MqttRemoteRegistryConvertsNumericPayloads),
    ("MQTT retained publish behavior", MqttRetainedPublishBehavior),
    ("MQTT incoming write mapping", MqttIncomingWriteMapping),
    ("MQTT client recursive item publish", MqttClientRecursiveItemPublish),
    ("MQTT client live subscription rebuild", MqttClientLiveSubscriptionRebuild),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Item broker tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"Item broker tests passed: {tests.Length}");
return 0;

static Task PathNormalization()
{
    AssertEqual("Runtime.Device.Read", ItemBrokerPath.Normalize(@"Runtime/Device\Read"));
    AssertTrue(ItemBrokerPath.Equals("runtime.device.read", @"Runtime/Device\Read"));
    AssertTrue(ItemBrokerPath.Matches("runtime.device", "Runtime.Device.Read", recursive: true));
    AssertFalse(ItemBrokerPath.Matches("runtime.device", "Runtime.Device.Read", recursive: false));
    return Task.CompletedTask;
}

static async Task SnapshotPublishAndRetainedState()
{
    var broker = new InMemoryItemBroker();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(publisher, Subscribe("Runtime.Device", recursive: true, publisher.ClientId));

    var item = new Item("Device", 1).Repath("Runtime.Device");
    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device", item, publisher.ClientId, null, DateTimeOffset.UtcNow));

    await broker.SubscribeAsync(subscriber, Subscribe(@"runtime/device", recursive: false, subscriber.ClientId));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual("Runtime.Device", retained.Path);
    AssertEqual(1, retained.Item.Value);
}

static async Task ValueUpdateRouting()
{
    var broker = new InMemoryItemBroker();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    await broker.PublishValueChangedAsync(new ItemValueChangedMessage(@"runtime/device/read", 7, "publisher", null, DateTimeOffset.UtcNow));

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("runtime.device.read", message.Path);
    AssertEqual(7, message.Value);
}

static async Task RetainedValueUpdateConvertsNumericPayloads()
{
    var broker = new InMemoryItemBroker();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    var item = new Item("Read", 1.5).Repath("Runtime.Device.Read");

    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device.Read", item, publisher.ClientId, null, DateTimeOffset.UtcNow));
    await broker.PublishValueChangedAsync(new ItemValueChangedMessage("Runtime.Device.Read", 2L, "mqtt", null, DateTimeOffset.UtcNow));
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(2.0, retained.Item.Value);
}

static async Task ParameterUpdateRouting()
{
    var broker = new InMemoryItemBroker();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    await broker.PublishParameterChangedAsync(new ItemParameterChangedMessage("Runtime.Device.Read", "Unit", "V", "publisher", null, DateTimeOffset.UtcNow));

    var message = AssertSingle<ItemParameterChangedMessage>(subscriber.Messages);
    AssertEqual("Unit", message.ParameterName);
    AssertEqual("V", message.Value);
}

static async Task RetainedParameterUpdateConvertsNumericPayloads()
{
    var broker = new InMemoryItemBroker();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    var item = new Item("Read", 0).Repath("Runtime.Device.Read");
    item.Params["Scale"].Value = 1.5;

    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device.Read", item, publisher.ClientId, null, DateTimeOffset.UtcNow));
    await broker.PublishParameterChangedAsync(new ItemParameterChangedMessage("Runtime.Device.Read", "Scale", 2L, "mqtt", null, DateTimeOffset.UtcNow));
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(2.0, retained.Item.Params["Scale"].Value);
}

static async Task RecursiveSubscription()
{
    var broker = new InMemoryItemBroker();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device", recursive: true, subscriber.ClientId));

    await broker.PublishValueChangedAsync(new ItemValueChangedMessage("Runtime.Device.Read", 3, "publisher", null, DateTimeOffset.UtcNow));

    AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
}

static async Task RemoveClearsDescendants()
{
    var broker = new InMemoryItemBroker();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(publisher, Subscribe("Runtime.Device", recursive: true, publisher.ClientId));

    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device.Read", new Item("Read", 2).Repath("Runtime.Device.Read"), publisher.ClientId, null, DateTimeOffset.UtcNow));
    await broker.RemoveAsync(new ItemRemoveMessage("Runtime.Device", publisher.ClientId, null, DateTimeOffset.UtcNow));
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device", recursive: true, subscriber.ClientId));

    AssertEqual(0, subscriber.Messages.Count);
}

static async Task WriteRequestRouting()
{
    var broker = new InMemoryItemBroker();
    var owner = new RecordingClient("owner");
    await broker.SubscribeAsync(owner, Subscribe("Runtime.Device", recursive: true, owner.ClientId));
    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device", new Item("Device").Repath("Runtime.Device"), owner.ClientId, null, DateTimeOffset.UtcNow));

    var ack = await broker.WriteAsync(new ItemWriteRequestMessage("Runtime.Device.Read", "", 9, null, "writer", "c1", DateTimeOffset.UtcNow));

    AssertTrue(ack.Accepted);
    var write = AssertSingle<ItemWriteRequestMessage>(owner.Messages.OfType<ItemWriteRequestMessage>().ToList());
    AssertEqual("Runtime.Device.Read", write.Path);
    AssertEqual("Value", write.ParameterName);
    AssertEqual(9, write.Value);
}

static Task RetentionPolicyDecisions()
{
    var resolver = new DefaultItemRetentionPolicyResolver();
    var timestamp = DateTimeOffset.UtcNow;

    var valueDecision = resolver.Resolve(new ItemValueChangedMessage("Runtime.Device.Read", 1, "publisher", null, timestamp), timestamp);
    AssertTrue(valueDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.LatestOnly, valueDecision.Mode);

    var healthDecision = resolver.Resolve(new ItemValueChangedMessage(ItemBrokerHealthPaths.Heartbeat, true, "publisher", null, timestamp), timestamp);
    AssertTrue(healthDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.TimeToLive, healthDecision.Mode);
    AssertTrue(healthDecision.ExpiresAt > timestamp);

    var writeDecision = resolver.Resolve(new ItemWriteRequestMessage("Runtime.Device.Read", "Value", 2, null, "writer", null, timestamp), timestamp);
    AssertFalse(writeDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.NotRetained, writeDecision.Mode);

    return Task.CompletedTask;
}

static Task PublishPolicySnapshotAndDeltaDecisions()
{
    var resolver = new DefaultItemPublishPolicyResolver();

    var snapshot = resolver.Resolve("Runtime.Device.Read", isSnapshotRequired: true);
    AssertTrue(snapshot.ShouldPublish);
    AssertEqual(ItemPublishMode.Snapshot, snapshot.Mode);

    var delta = resolver.Resolve("Runtime.Device.Read", isSnapshotRequired: false);
    AssertTrue(delta.ShouldPublish);
    AssertEqual(ItemPublishMode.Delta, delta.Mode);

    return Task.CompletedTask;
}

static async Task ClientPublishPathNormalization()
{
    var broker = new InMemoryItemBroker();
    var session = new ItemBrokerClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    await session.PublishValueAsync(@"Runtime/Device\Read", 5);

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("Runtime.Device.Read", message.Path);
    AssertEqual(5, message.Value);
}

static async Task ClientPublishItemValue()
{
    var broker = new InMemoryItemBroker();
    var session = new ItemBrokerClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    var item = new Item("Read", 5, "Runtime.Device");
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    item.Value = 6;
    await session.PublishValueAsync(item);

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("Runtime.Device.Read", message.Path);
    AssertEqual(6, message.Value);
}

static async Task ClientPublishItemParameter()
{
    var broker = new InMemoryItemBroker();
    var session = new ItemBrokerClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    var item = new Item("Read", 5, "Runtime.Device");
    item.Params["Unit"].Value = "V";
    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Read", recursive: false, subscriber.ClientId));

    await session.PublishParameterAsync(item, "Unit");

    var message = AssertSingle<ItemParameterChangedMessage>(subscriber.Messages);
    AssertEqual("Runtime.Device.Read", message.Path);
    AssertEqual("Unit", message.ParameterName);
    AssertEqual("V", message.Value);
}

static async Task HighFrequencyLatestRetainedValue()
{
    var broker = new InMemoryItemBroker();
    var publisher = new ItemBrokerClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");

    for (var value = 0; value < 25; value++)
    {
        await publisher.PublishValueAsync("Runtime.Device.Fast", value);
    }

    await broker.SubscribeAsync(subscriber, Subscribe("Runtime.Device.Fast", recursive: false, subscriber.ClientId));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(24, retained.Item.Value);
}

static Task MqttTopicMapping()
{
    var mapper = new MqttItemTopicMapper("hornet");
    var healthTopic = mapper.ToTopic("Runtime.Health.ItemBroker.Heartbeat", "Value", "itembroker");

    AssertEqual("hornet/broker/heartbeat", healthTopic);
    AssertTrue(mapper.TryMapTopic(healthTopic, out var healthMapping));
    AssertEqual("Runtime.Health.ItemBroker.Heartbeat", healthMapping.Path);
    AssertEqual("Value", healthMapping.ParameterName);
    AssertEqual(null, healthMapping.ClientId);

    var valueTopic = mapper.ToTopic("Runtime.Device.Read", "Value", "device-client");
    AssertEqual("hornet/Runtime/Device/Read", valueTopic);
    AssertTrue(mapper.TryMapTopic(valueTopic, out var valueMapping));
    AssertEqual("Runtime.Device.Read", valueMapping.Path);
    AssertEqual("Value", valueMapping.ParameterName);
    AssertEqual("shared", valueMapping.ClientId);

    var parameterTopic = mapper.ToTopic("Runtime.Device.Read", "Unit", "device-client");
    AssertEqual("hornet/Runtime/Device/Read/params/Unit", parameterTopic);
    AssertTrue(mapper.TryMapTopic(parameterTopic, out var parameterMapping));
    AssertEqual("Runtime.Device.Read", parameterMapping.Path);
    AssertEqual("Unit", parameterMapping.ParameterName);
    AssertEqual("shared", parameterMapping.ClientId);

    var fallbackTopic = mapper.ToTopic("Runtime.Device.Read", "Value");
    AssertEqual("hornet/Runtime/Device/Read", fallbackTopic);
    AssertFalse(mapper.TryMapTopic("other/items/Runtime/value", out _));
    AssertFalse(mapper.TryMapTopic("hornet/Runtime/Device/Read/params", out _));
    return Task.CompletedTask;
}

static Task MqttTopicMappingWithBaseTopic()
{
    var mapper = new Amium.ItemBroker.Mqtt.Client.MqttItemTopicMapper("/plant/hornet/");
    var topic = mapper.ToTopic("Runtime.Device.Read", "Value", "client a");

    AssertEqual("plant/hornet/Runtime/Device/Read", topic);
    AssertTrue(mapper.TryMapTopic(topic, out var mapping));
    AssertEqual("Runtime.Device.Read", mapping.Path);
    AssertEqual("Value", mapping.ParameterName);
    AssertEqual("shared", mapping.ClientId);
    AssertFalse(mapper.TryMapTopic("hornet/Runtime/Device/Read", out _));
    return Task.CompletedTask;
}

static Task MqttSharedTopicMappingIgnoresClientId()
{
    var mapper = new MqttItemTopicMapper("hornet");

    AssertEqual(
        mapper.ToTopic("Studio.Project.DefaultLayout.UdlClient1.m400.Set.Request", "Value", "client-a"),
        mapper.ToTopic("Studio.Project.DefaultLayout.UdlClient1.m400.Set.Request", "Value", "client-b"));
    AssertEqual("hornet/Studio/Project/DefaultLayout/UdlClient1/m400/Set/Request", mapper.ToTopic("Studio.Project.DefaultLayout.UdlClient1.m400.Set.Request", "Value", "client-a"));
    AssertEqual("hornet/Studio/Project/DefaultLayout/UdlClient1/m400/Set/Request/params/Unit", mapper.ToTopic("Studio.Project.DefaultLayout.UdlClient1.m400.Set.Request", "Unit", "client-a"));
    return Task.CompletedTask;
}

static Task MqttRemoteRegistryRebuild()
{
    var mapper = new Amium.ItemBroker.Mqtt.Client.MqttItemTopicMapper("hornet");
    var registry = new Amium.ItemBroker.Mqtt.Client.MqttRemoteItemRegistry();

    AssertTrue(mapper.TryMapTopic("hornet/Plant/Line1/Temperature", out var valueMapping));
    registry.Apply(valueMapping, "21.5");
    AssertTrue(mapper.TryMapTopic("hornet/Plant/Line1/Temperature/params/Unit", out var parameterMapping));
    registry.Apply(parameterMapping, "degC");

    var roots = registry.GetClientRoots();
    AssertTrue(roots.TryGetValue("shared", out var root));
    AssertEqual(21.5, root!["Plant"]["Line1"]["Temperature"].Value);
    AssertEqual("degC", root["Plant"]["Line1"]["Temperature"].Params["Unit"].Value);
    AssertEqual("online", root.Params["ConnectionStatus"].Value);

    registry.MarkOffline("shared");
    var offlineRoots = registry.GetClientRoots();
    AssertTrue(offlineRoots.TryGetValue("shared", out var offlineRoot));
    AssertEqual("offline", offlineRoot!.Params["ConnectionStatus"].Value);
    AssertEqual(true, offlineRoot.Params["Stale"].Value);
    return Task.CompletedTask;
}

static Task MqttRemoteRegistryConvertsNumericPayloads()
{
    var mapper = new Amium.ItemBroker.Mqtt.Client.MqttItemTopicMapper("hornet");
    var registry = new Amium.ItemBroker.Mqtt.Client.MqttRemoteItemRegistry();

    AssertTrue(mapper.TryMapTopic("hornet/Plant/Line1/Temperature", out var valueMapping));
    registry.Apply(valueMapping, "21.5");
    registry.Apply(valueMapping, "22");
    AssertTrue(mapper.TryMapTopic("hornet/Plant/Line1/Temperature/params/Scale", out var parameterMapping));
    registry.Apply(parameterMapping, "1.5");
    registry.Apply(parameterMapping, "2");

    var roots = registry.GetClientRoots();
    AssertTrue(roots.TryGetValue("shared", out var root));
    AssertEqual(22.0, root!["Plant"]["Line1"]["Temperature"].Value);
    AssertEqual(2.0, root["Plant"]["Line1"]["Temperature"].Params["Scale"].Value);
    return Task.CompletedTask;
}

static async Task MqttRetainedPublishBehavior()
{
    var publisher = new RecordingMqttPublisher();
    var adapter = new MqttItemBrokerAdapter(new MqttItemBrokerOptions { Enabled = false }, publisher);

    await adapter.ReceiveAsync(new ItemValueChangedMessage("Runtime.Device.Read", 42, "publisher", null, DateTimeOffset.UtcNow));
    await adapter.ReceiveAsync(new ItemValueChangedMessage("Runtime.Device.Temperature", 42.0, "publisher", null, DateTimeOffset.UtcNow));

    AssertContainsMessage(publisher.Messages, "hornet/Runtime/Device/Read", "42");
    AssertContainsMessage(publisher.Messages, "hornet/Runtime/Device/Temperature", "42.0");
    AssertTrue(publisher.Messages.All(message => message.Retain));
}

static async Task MqttIncomingWriteMapping()
{
    var broker = new InMemoryItemBroker();
    var owner = new RecordingClient("owner");
    var adapter = new MqttItemBrokerAdapter(new MqttItemBrokerOptions { Enabled = false });
    await broker.SubscribeAsync(owner, Subscribe("Runtime.Device", recursive: true, owner.ClientId));
    await broker.PublishSnapshotAsync(new ItemSnapshotMessage("Runtime.Device", new Item("Device").Repath("Runtime.Device"), owner.ClientId, null, DateTimeOffset.UtcNow));
    await adapter.StartAsync(broker);
    await adapter.ReceiveAsync(new ItemParameterChangedMessage("Runtime.Device", "Writable", true, "publisher", null, DateTimeOffset.UtcNow));

    await adapter.HandleIncomingPublishAsync("hornet/Runtime/Device", "13");

    var write = AssertSingle<ItemWriteRequestMessage>(owner.Messages.OfType<ItemWriteRequestMessage>().ToList());
    AssertEqual("Runtime.Device", write.Path);
    AssertEqual("Value", write.ParameterName);
    AssertEqual(13L, write.Value);
    AssertEqual("shared", write.SourceClientId);
}

static async Task MqttClientRecursiveItemPublish()
{
    var port = GetAvailableTcpPort();
    var publishedMessages = new List<RecordedMqttMessage>();
    var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
        .WithDefaultEndpoint()
        .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
        .WithDefaultEndpointPort(port)
        .Build());

    server.InterceptingPublishAsync += args =>
    {
        var payload = args.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : Encoding.UTF8.GetString(args.ApplicationMessage.Payload);
        lock (publishedMessages)
        {
            publishedMessages.Add(new RecordedMqttMessage(args.ApplicationMessage.Topic, payload, args.ApplicationMessage.Retain));
        }

        return Task.CompletedTask;
    };

    await server.StartAsync().ConfigureAwait(false);
    try
    {
        await using var session = new MqttClientSession(new MqttClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "demo-client",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });
        var item = new Item("Temperature", 22.0, "Demo");
        item.Params["Unit"].Value = "degC";
        item["Raw"].Value = "0.0";

        await session.PublishItemAsync(item).ConfigureAwait(false);

        await WaitForMqttMessageAsync(publishedMessages, "hornet/Demo/Temperature", "22.0").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/Demo/Temperature/params/Unit", "degC").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/Demo/Temperature/Raw", "0.0").ConfigureAwait(false);
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task MqttClientLiveSubscriptionRebuild()
{
    var port = GetAvailableTcpPort();
    var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
        .WithDefaultEndpoint()
        .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
        .WithDefaultEndpointPort(port)
        .Build());

    await server.StartAsync().ConfigureAwait(false);
    try
    {
        await using var subscriber = new MqttClientSession(new MqttClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "subscriber",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });
        await subscriber.ConnectAsync().ConfigureAwait(false);

        await using var publisher = new MqttClientSession(new MqttClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "live-client",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.PublishValueAsync("Edm1.Temperature", 23.5).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var roots = subscriber.RemoteItems.GetClientRoots();
            if (roots.TryGetValue("shared", out var root)
                && object.Equals(23.5, root["Edm1"]["Temperature"].Value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Subscriber did not rebuild remote live item tree.");
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static ItemSubscribeMessage Subscribe(string path, bool recursive, string clientId)
    => new(path, recursive, IncludeRetained: true, clientId, null, DateTimeOffset.UtcNow);

static int GetAvailableTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, port: 0);
    listener.Start();
    try
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}

static T AssertSingle<T>(IReadOnlyList<ItemBrokerMessage> messages)
    where T : ItemBrokerMessage
{
    var typed = messages.OfType<T>().ToArray();
    if (typed.Length != 1)
    {
        throw new InvalidOperationException($"Expected one {typeof(T).Name}, actual {typed.Length}.");
    }

    return typed[0];
}

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertEqual(object? expected, object? actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }
}

static void AssertContainsMessage(IReadOnlyList<RecordedMqttMessage> messages, string topic, string payload)
{
    RecordedMqttMessage[] snapshot;
    lock (messages)
    {
        snapshot = messages.ToArray();
    }

    if (!snapshot.Any(message => string.Equals(message.Topic, topic, StringComparison.Ordinal) && string.Equals(message.Payload, payload, StringComparison.Ordinal)))
    {
        var actualMessages = string.Join(", ", snapshot.Select(message => $"{message.Topic}={message.Payload}"));
        throw new InvalidOperationException($"Expected MQTT message '{topic}' with payload '{payload}'. Actual messages: {actualMessages}");
    }
}

static async Task WaitForMqttMessageAsync(IReadOnlyList<RecordedMqttMessage> messages, string topic, string payload)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            AssertContainsMessage(messages, topic, payload);
            return;
        }
        catch (InvalidOperationException)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }
    }

    AssertContainsMessage(messages, topic, payload);
}

sealed class RecordingClient : IItemBrokerClient
{
    public RecordingClient(string clientId)
    {
        ClientId = clientId;
    }

    public string ClientId { get; }

    public List<ItemBrokerMessage> Messages { get; } = new();

    public Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

sealed class RecordingMqttPublisher : IMqttMessagePublisher
{
    public List<RecordedMqttMessage> Messages { get; } = new();

    public Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken = default)
    {
        Messages.Add(new RecordedMqttMessage(topic, payload, retain));
        return Task.CompletedTask;
    }
}

sealed record RecordedMqttMessage(string Topic, string Payload, bool Retain);
