using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server;
using Amium.Item.Client;
using Amium.Item.Server.Mqtt;
using Amium.Item.Client.Mqtt;
using MQTTnet;
using MQTTnet.Server;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HostMqttItemTopicMapper = Amium.Item.Server.Mqtt.MqttItemTopicMapper;
using ClientMqttItemTopicMapper = Amium.Item.Client.Mqtt.MqttItemTopicMapper;
using MqttClientOptions = Amium.Item.Client.Mqtt.MqttItemClientOptions;
using MqttClientSession = Amium.Item.Client.Mqtt.MqttItemClientSession;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Path normalization", PathNormalization),
    ("ItemModel read/write channel", ItemReadWriteChannel),
    ("Snapshot publish and retained state", SnapshotPublishAndRetainedState),
    ("Value update routing", ValueUpdateRouting),
    ("Retained value update converts numeric payloads", RetainedValueUpdateConvertsNumericPayloads),
    ("Parameter update routing", ParameterUpdateRouting),
    ("Retained parameter update converts numeric payloads", RetainedParameterUpdateConvertsNumericPayloads),
    ("Recursive subscription", RecursiveSubscription),
    ("Remove clears descendants", RemoveClearsDescendants),
    ("Write request routing", WriteRequestRouting),
    ("Snapshot same owner refresh succeeds", SnapshotSameOwnerRefreshSucceeds),
    ("Snapshot different owner conflict is rejected", SnapshotDifferentOwnerConflictIsRejected),
    ("Retention policy decisions", RetentionPolicyDecisions),
    ("Publish policy snapshot and delta decisions", PublishPolicySnapshotAndDeltaDecisions),
    ("Client publish path normalization", ClientPublishPathNormalization),
    ("Client publish item value", ClientPublishItemValue),
    ("Client publish item parameter", ClientPublishItemParameter),
    ("High frequency latest retained value", HighFrequencyLatestRetainedValue),
    ("MQTT topic mapping", MqttTopicMappingBehavior),
    ("MQTT topic mapping with base topic", MqttTopicMappingWithBaseTopicBehavior),
    ("MQTT topic mapping without base topic", MqttTopicMappingWithoutBaseTopicBehavior),
    ("MQTT shared topic mapping ignores client id", MqttSharedTopicMappingIgnoresClientIdBehavior),
    ("MQTT remote registry rebuild", MqttRemoteRegistryRebuild),
    ("MQTT remote registry converts numeric payloads", MqttRemoteRegistryConvertsNumericPayloads),
    ("MQTT retained publish behavior", MqttRetainedPublishBehavior),
    ("MQTT incoming write mapping", MqttIncomingWriteMapping),
    ("MQTT incoming observed publish import", MqttIncomingObservedPublishImport),
    ("MQTT incoming observed publish import preserves value across parameters", MqttIncomingObservedPublishImportPreservesValueAcrossParameters),
    ("MQTT incoming non-writable write is blocked", MqttIncomingNonWritableWriteIsBlocked),
    ("MQTT client recursive item publish", MqttClientRecursiveItemPublish),
    ("MQTT client live subscription rebuild", MqttClientLiveSubscriptionRebuild),
    ("MQTT remote client value update refreshes retained state", MqttRemoteClientValueUpdateRefreshesRetainedState),
    ("Core health publisher publishes health", ItemServerHealthPublisherPublishesHealth),
    ("MQTT selfhosted host publishes health", MqttSelfhostedHostPublishesHealth),
    ("MQTT selfhosted host publishes runtime counts", MqttSelfhostedHostPublishesRuntimeCounts),
    ("MQTT remote client hides self-published items", MqttRemoteClientHidesSelfPublishedItems),
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
    Console.Error.WriteLine("Item server tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"Item server tests passed: {tests.Length}");
return 0;

static Task PathNormalization()
{
    AssertEqual("runtime.device.read", ItemServerPath.Normalize(@"runtime/device\Read"));
    AssertTrue(ItemServerPath.Equals("runtime.device.read", @"runtime/device\Read"));
    AssertTrue(ItemServerPath.Matches("runtime.device", "runtime.Device.Read", recursive: true));
    AssertFalse(ItemServerPath.Matches("runtime.device", "runtime.Device.Read", recursive: false));
    AssertTrue(ItemServerPath.Matches(ItemServerPath.GlobalSubscriptionPath, "runtime.Device.Read", recursive: true));
    return Task.CompletedTask;
}

static Task ItemReadWriteChannel()
{
    var readOnlyItem = new ItemModel("ReadOnly", 1);

    AssertTrue(readOnlyItem.Properties.Has("read"));
    AssertFalse(readOnlyItem.Properties.Has("value"));
    AssertFalse(readOnlyItem.Properties.Has("write"));
    AssertEqual(1, readOnlyItem.Value);
    readOnlyItem.Value = 2;
    AssertEqual(2, readOnlyItem.Properties["read"].Value);

    var writableItem = new ItemModel("Writable", 3, hasWriteChannel: true);

    AssertTrue(writableItem.Properties.Has("read"));
    AssertTrue(writableItem.Properties.Has("write"));
    AssertFalse(writableItem.Properties.Has("value"));
    AssertEqual(3, writableItem.Value);
    writableItem.Value = 4;
    AssertEqual(3, writableItem.Properties["read"].Value);
    AssertEqual(4, writableItem.Properties["write"].Value);

    var alertItem = new ItemModel("Alert", path: "runtime.Device", hasReadChannel: false);

    AssertFalse(alertItem.Properties.Has("read"));
    AssertFalse(alertItem.Properties.Has("write"));
    AssertFalse(alertItem.Properties.Has("value"));
    alertItem.Value = "fault";
    AssertFalse(alertItem.Properties.Has("read"));
    AssertEqual("fault", alertItem.Properties["value"].Value);
    AssertEqual("fault", alertItem.Value);

    return Task.CompletedTask;
}

static async Task SnapshotPublishAndRetainedState()
{
    var broker = new InMemoryItemServer();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: publisher,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));

    var item = new ItemModel("Device", 1).Repath("runtime.Device");
    await broker.PublishSnapshotAsync(
        item: item,
        sourceClientId: publisher.ClientId);

    await broker.SubscribeAsync(
        client: subscriber,
        path: @"runtime/device",
        options: SubscribeOptions(recursive: false));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual("runtime.device", retained.Path);
    AssertEqual(1, retained.ItemModel.Value);
}

static async Task ValueUpdateRouting()
{
    var broker = new InMemoryItemServer();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    await broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 0).Repath("runtime.Device.Read"),
        sourceClientId: "publisher");
    await broker.UpdateValueAsync(
        item: new ItemModel("Read", 7).Repath(@"runtime/device/read"),
        sourceClientId: "publisher");

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("runtime.device.read", message.Path);
    AssertEqual(7, message.Value);
}

static async Task RetainedValueUpdateConvertsNumericPayloads()
{
    var broker = new InMemoryItemServer();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    var item = new ItemModel("Read", 1.5).Repath("runtime.Device.Read");

    await broker.PublishSnapshotAsync(
        item: item,
        sourceClientId: publisher.ClientId);
    item.Value = 2.0;
    await broker.UpdateValueAsync(
        item: item,
        retained: true,
        sourceClientId: publisher.ClientId);
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(2.0, retained.ItemModel.Value);
}

static async Task ParameterUpdateRouting()
{
    var broker = new InMemoryItemServer();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    var item = new ItemModel("Read", 0).Repath("runtime.Device.Read");
    item.Properties["unit"].Value = "V";
    await broker.PublishSnapshotAsync(
        item: item,
        sourceClientId: "publisher");
    await broker.UpdatePropertyAsync(
        item: item,
        parameterName: "Unit",
        sourceClientId: "publisher");

    var message = AssertSingle<ItemPropertyChangedMessage>(subscriber.Messages);
    AssertEqual("unit", message.PropertyName);
    AssertEqual("V", message.Value);
}

static async Task RetainedParameterUpdateConvertsNumericPayloads()
{
    var broker = new InMemoryItemServer();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    var item = new ItemModel("Read", 0).Repath("runtime.Device.Read");
    item.Properties["Scale"].Value = 1.5;

    await broker.PublishSnapshotAsync(
        item: item,
        sourceClientId: publisher.ClientId);
    item.Properties["Scale"].Value = 2.0;
    await broker.UpdatePropertyAsync(
        item: item,
        parameterName: "Scale",
        retained: true,
        sourceClientId: publisher.ClientId);
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(2.0, retained.ItemModel.Properties["Scale"].Value);
}

static async Task RecursiveSubscription()
{
    var broker = new InMemoryItemServer();
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));

    await broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 0).Repath("runtime.Device.Read"),
        sourceClientId: "publisher");
    await broker.UpdateValueAsync(
        item: new ItemModel("Read", 3).Repath("runtime.Device.Read"),
        sourceClientId: "publisher");

    AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
}

static async Task RemoveClearsDescendants()
{
    var broker = new InMemoryItemServer();
    var publisher = new RecordingClient("publisher");
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: publisher,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));

    await broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 2).Repath("runtime.Device.Read"),
        sourceClientId: publisher.ClientId);
    await broker.RemoveAsync(
        item: new ItemModel("Device").Repath("runtime.Device"),
        sourceClientId: publisher.ClientId);
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));

    AssertEqual(0, subscriber.Messages.Count);
}

static async Task WriteRequestRouting()
{
    var broker = new InMemoryItemServer();
    var owner = new RecordingClient("owner");
    await broker.SubscribeAsync(
        client: owner,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));
    await broker.PublishSnapshotAsync(
        item: new ItemModel("Device").Repath("runtime.Device"),
        sourceClientId: owner.ClientId);

    var ack = await broker.UpdateValueAsync(
        item: new ItemModel("Read", 9).Repath("runtime.Device.Read"),
        sourceClientId: "writer",
        correlationId: "c1");

    AssertTrue(ack.Accepted);
    var write = AssertSingle<ItemWriteRequestMessage>(owner.Messages.OfType<ItemWriteRequestMessage>().ToList());
    AssertEqual("runtime.device.read", write.Path);
    AssertEqual("read", write.ParameterName);
    AssertEqual(9, write.Value);
}

static Task RetentionPolicyDecisions()
{
    var resolver = new DefaultItemRetentionPolicyResolver();
    var timestamp = DateTimeOffset.UtcNow;

    var valueDecision = resolver.Resolve(new ItemValueChangedMessage("runtime.Device.Read", 1, "publisher", null, timestamp), timestamp);
    AssertTrue(valueDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.LatestOnly, valueDecision.Mode);

    var healthDecision = resolver.Resolve(new ItemValueChangedMessage(ItemServerHealthPaths.StatusState, "Running", "publisher", null, timestamp), timestamp);
    AssertTrue(healthDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.TimeToLive, healthDecision.Mode);
    AssertTrue(healthDecision.ExpiresAt > timestamp);

    var writeDecision = resolver.Resolve(new ItemWriteRequestMessage("runtime.Device.Read", "read", 2, null, "writer", null, timestamp), timestamp);
    AssertFalse(writeDecision.ShouldRetain);
    AssertEqual(ItemRetentionMode.NotRetained, writeDecision.Mode);

    return Task.CompletedTask;
}

static async Task SnapshotSameOwnerRefreshSucceeds()
{
    var broker = new InMemoryItemServer();
    var first = new ItemModel("Read", 1).Repath("runtime.Device.Read");
    var second = new ItemModel("Read", 2).Repath("runtime.Device.Read");

    await broker.PublishSnapshotAsync(first, sourceClientId: "publisher");
    await broker.PublishSnapshotAsync(second, sourceClientId: "publisher");

    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(2, retained.ItemModel.Value);
}

static async Task SnapshotDifferentOwnerConflictIsRejected()
{
    var broker = new InMemoryItemServer();
    await broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 1).Repath("runtime.Device.Read"),
        sourceClientId: "owner-a");

    await AssertThrowsAsync<ItemOwnershipConflictException>(() => broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 2).Repath("runtime.Device.Read"),
        sourceClientId: "owner-b"));
}

static Task PublishPolicySnapshotAndDeltaDecisions()
{
    var resolver = new DefaultItemPublishPolicyResolver();

    var snapshot = resolver.Resolve("runtime.Device.Read", isSnapshotRequired: true);
    AssertTrue(snapshot.ShouldPublish);
    AssertEqual(ItemPublishMode.Snapshot, snapshot.Mode);

    var delta = resolver.Resolve("runtime.Device.Read", isSnapshotRequired: false);
    AssertTrue(delta.ShouldPublish);
    AssertEqual(ItemPublishMode.Delta, delta.Mode);

    return Task.CompletedTask;
}

static async Task ClientPublishPathNormalization()
{
    var broker = new InMemoryItemServer();
    var session = new ItemClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    await session.PublishSnapshotAsync(new ItemModel("Read", 0).Repath("runtime.Device.Read"));
    await session.UpdateValueAsync(new ItemModel("Read", 5).Repath(@"runtime/device\Read"));

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("runtime.device.read", message.Path);
    AssertEqual(5, message.Value);
}

static async Task ClientPublishItemValue()
{
    var broker = new InMemoryItemServer();
    var session = new ItemClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    var item = new ItemModel("Read", 5, "runtime.Device");
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    item.Value = 6;
    await session.PublishSnapshotAsync(item);
    await session.UpdateValueAsync(item);

    var message = AssertSingle<ItemValueChangedMessage>(subscriber.Messages);
    AssertEqual("runtime.device.read", message.Path);
    AssertEqual(6, message.Value);
}

static async Task ClientPublishItemParameter()
{
    var broker = new InMemoryItemServer();
    var session = new ItemClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    var item = new ItemModel("Read", 5, "runtime.Device");
    item.Properties["unit"].Value = "V";
    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Read",
        options: SubscribeOptions(recursive: false));

    await session.PublishSnapshotAsync(item);
    await session.UpdatePropertyAsync(item, "Unit");

    var message = AssertSingle<ItemPropertyChangedMessage>(subscriber.Messages);
    AssertEqual("runtime.device.read", message.Path);
    AssertEqual("unit", message.PropertyName);
    AssertEqual("V", message.Value);
}

static async Task HighFrequencyLatestRetainedValue()
{
    var broker = new InMemoryItemServer();
    var publisher = new ItemClientSession("publisher", broker);
    var subscriber = new RecordingClient("subscriber");
    await publisher.PublishSnapshotAsync(new ItemModel("Fast", -1).Repath("runtime.Device.Fast"));

    for (var value = 0; value < 25; value++)
    {
        await publisher.UpdateValueAsync(
            new ItemModel("Fast", value).Repath("runtime.Device.Fast"),
            retained: true);
    }

    await broker.SubscribeAsync(
        client: subscriber,
        path: "runtime.Device.Fast",
        options: SubscribeOptions(recursive: false));

    var retained = AssertSingle<ItemSnapshotMessage>(subscriber.Messages);
    AssertEqual(24, retained.ItemModel.Value);
}

static Task MqttTopicMappingBehavior()
{
    var mapper = new HostMqttItemTopicMapper("hornet");
    var healthTopic = mapper.ToTopic(ItemServerHealthPaths.StatusState, "read", "itemserver");

    AssertEqual("$SYS/status/state", healthTopic);
    AssertTrue(mapper.TryMapTopic(healthTopic, out var healthMapping));
    AssertEqual(ItemServerHealthPaths.StatusState, healthMapping.Path);
    AssertEqual("read", healthMapping.PropertyName);
    AssertEqual("shared", healthMapping.ClientId);

    var metaTopic = mapper.ToTopic("runtime.Device.Read", "Meta", "device-client");
    AssertEqual("hornet/runtime/device/read", metaTopic);
    AssertTrue(mapper.TryMapTopic(metaTopic, "{}", out var metaMapping));
    AssertEqual("runtime.device.read", metaMapping.Path);
    AssertEqual("meta", metaMapping.PropertyName);
    AssertEqual("shared", metaMapping.ClientId);

    var valueTopic = mapper.ToTopic("runtime.Device.Read", "read", "device-client");
    AssertEqual("hornet/runtime/device/read/read", valueTopic);
    AssertTrue(mapper.TryMapTopic(valueTopic, "13", out var valueMapping));
    AssertEqual("runtime.device.read", valueMapping.Path);
    AssertEqual("read", valueMapping.PropertyName);
    AssertEqual("shared", valueMapping.ClientId);

    var parameterTopic = mapper.ToTopic("runtime.Device.Read", "Unit", "device-client");
    AssertEqual("hornet/runtime/device/read/unit", parameterTopic);
    AssertTrue(mapper.TryMapTopic(parameterTopic, "V", out var parameterMapping));
    AssertEqual("runtime.device.read", parameterMapping.Path);
    AssertEqual("unit", parameterMapping.PropertyName);
    AssertEqual("shared", parameterMapping.ClientId);

    var fallbackTopic = mapper.ToTopic("runtime.Device.Read", "read");
    AssertEqual("hornet/runtime/device/read/read", fallbackTopic);
    AssertFalse(mapper.TryMapTopic("other/items/Runtime/read", out _));
    return Task.CompletedTask;
}

static Task MqttTopicMappingWithBaseTopicBehavior()
{
    var mapper = new ClientMqttItemTopicMapper("/plant/hornet/");
    var topic = mapper.ToTopic("runtime.Device.Read", "read", "client a");

    AssertEqual("plant/hornet/runtime/device/read/read", topic);
    AssertTrue(mapper.TryMapTopic(topic, "2", out var mapping));
    AssertEqual("runtime.device.read", mapping.Path);
    AssertEqual("read", mapping.PropertyName);
    AssertEqual("shared", mapping.ClientId);
    AssertEqual("plant/hornet/#", mapper.ItemSubscriptionTopic);
    AssertFalse(mapper.TryMapTopic("hornet/runtime/device/read/read", "2", out _));
    return Task.CompletedTask;
}

static Task MqttTopicMappingWithoutBaseTopicBehavior()
{
    var mapper = new ClientMqttItemTopicMapper(string.Empty);

    AssertEqual("#", mapper.ItemSubscriptionTopic);
    AssertEqual("edm1/pressure", mapper.ToTopic("edm1.Pressure", "Meta", null));
    AssertEqual("edm1/pressure/read", mapper.ToTopic("edm1.Pressure", "read", null));
    AssertEqual("edm1/pressure/unit", mapper.ToTopic("edm1.Pressure", "Unit", null));
    AssertTrue(mapper.TryMapTopic("edm1/pressure", "{}", out var metaMapping));
    AssertEqual("edm1.pressure", metaMapping.Path);
    AssertEqual("meta", metaMapping.PropertyName);
    AssertTrue(mapper.TryMapTopic("edm1/pressure/unit", "bar", out var propertyMapping));
    AssertEqual("edm1.pressure", propertyMapping.Path);
    AssertEqual("unit", propertyMapping.PropertyName);
    return Task.CompletedTask;
}

static Task MqttSharedTopicMappingIgnoresClientIdBehavior()
{
    var mapper = new HostMqttItemTopicMapper("hornet");

    AssertEqual(
        mapper.ToTopic("studio.project.default_layout.UdlClient1.m400.set.request", "read", "client-a"),
        mapper.ToTopic("studio.project.default_layout.UdlClient1.m400.set.request", "read", "client-b"));
    AssertEqual("hornet/studio/project/default_layout/udl_client1/m400/set/request/read", mapper.ToTopic("studio.project.default_layout.UdlClient1.m400.set.request", "read", "client-a"));
    AssertEqual("hornet/studio/project/default_layout/udl_client1/m400/set/request/unit", mapper.ToTopic("studio.project.default_layout.UdlClient1.m400.set.request", "Unit", "client-a"));
    return Task.CompletedTask;
}

static Task MqttRemoteRegistryRebuild()
{
    var mapper = new ClientMqttItemTopicMapper("hornet");
    var registry = new Amium.Item.Client.Mqtt.MqttRemoteItemRegistry();

    AssertTrue(mapper.TryMapTopic("hornet/plant/line1/temperature", "{}", out var metaMapping));
    registry.Apply(metaMapping, "{}");
    AssertTrue(mapper.TryMapTopic("hornet/plant/line1/temperature/read", "21.5", out var valueMapping));
    registry.Apply(valueMapping, "21.5");
    AssertTrue(mapper.TryMapTopic("hornet/plant/line1/temperature/unit", "degC", out var parameterMapping));
    registry.Apply(parameterMapping, "degC");

    var roots = registry.GetClientRoots();
    AssertTrue(roots.TryGetValue("shared", out var root));
    AssertEqual(21.5, root!["plant"]["line1"]["temperature"].Value);
    AssertEqual("degC", root["plant"]["line1"]["temperature"].Properties["unit"].Value);
    AssertEqual("online", root.Properties["connection_status"].Value);

    registry.MarkOffline("shared");
    var offlineRoots = registry.GetClientRoots();
    AssertTrue(offlineRoots.TryGetValue("shared", out var offlineRoot));
    AssertEqual("offline", offlineRoot!.Properties["connection_status"].Value);
    AssertEqual(true, offlineRoot.Properties["stale"].Value);
    return Task.CompletedTask;
}

static Task MqttRemoteRegistryConvertsNumericPayloads()
{
    var mapper = new ClientMqttItemTopicMapper("hornet");
    var registry = new Amium.Item.Client.Mqtt.MqttRemoteItemRegistry();

    AssertTrue(mapper.TryMapTopic("hornet/plant/line1/temperature/read", "21.5", out var valueMapping));
    registry.Apply(valueMapping, "21.5");
    registry.Apply(valueMapping, "22");
    AssertTrue(mapper.TryMapTopic("hornet/plant/line1/temperature/scale", "1.5", out var parameterMapping));
    registry.Apply(parameterMapping, "1.5");
    registry.Apply(parameterMapping, "2");

    var roots = registry.GetClientRoots();
    AssertTrue(roots.TryGetValue("shared", out var root));
    AssertEqual(22.0, root!["plant"]["line1"]["temperature"].Value);
    AssertEqual(2.0, root["plant"]["line1"]["temperature"].Properties["scale"].Value);
    return Task.CompletedTask;
}

static async Task MqttRetainedPublishBehavior()
{
    var publisher = new RecordingMqttPublisher();
    var adapter = new MqttItemServerAdapter(new MqttItemServerOptions { Enabled = false }, publisher);

    await adapter.ReceiveAsync(new ItemValueChangedMessage("runtime.Device.Read", 42, "publisher", null, DateTimeOffset.UtcNow));
    await adapter.ReceiveAsync(new ItemValueChangedMessage("runtime.Device.Temperature", 42.0, "publisher", null, DateTimeOffset.UtcNow));
    await adapter.ReceiveAsync(new ItemSnapshotMessage(
        ItemServerHealthPaths.StatusState,
        new ItemModel("State", "Running").Repath(ItemServerHealthPaths.StatusState),
        "publisher",
        null,
        DateTimeOffset.UtcNow));

    AssertContainsMessage(publisher.Messages, "hornet/runtime/device/read/read", "42");
    AssertContainsMessage(publisher.Messages, "hornet/runtime/device/temperature/read", "42.0");
    AssertContainsMessage(publisher.Messages, "$SYS/status/state", "Running");
    AssertFalse(publisher.Messages.Any(message => string.Equals(message.Topic, "hornet/sys/status/state", StringComparison.OrdinalIgnoreCase)));
    AssertTrue(publisher.Messages.All(message => message.Retain));
}

static async Task MqttIncomingWriteMapping()
{
    var broker = new InMemoryItemServer();
    var owner = new RecordingClient("owner");
    var adapter = new MqttItemServerAdapter(new MqttItemServerOptions { Enabled = false });
    await broker.SubscribeAsync(
        client: owner,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));
    await broker.PublishSnapshotAsync(
        item: new ItemModel("Device").Repath("runtime.Device"),
        sourceClientId: owner.ClientId);
    await adapter.StartAsync(broker);
    await adapter.ReceiveAsync(new ItemPropertyChangedMessage("runtime.Device", "Writable", true, "publisher", null, DateTimeOffset.UtcNow));

    await adapter.HandleIncomingPublishAsync("hornet/runtime/device/read", "13");

    var write = AssertSingle<ItemWriteRequestMessage>(owner.Messages.OfType<ItemWriteRequestMessage>().ToList());
    AssertEqual("runtime.device", write.Path);
    AssertEqual("read", write.ParameterName);
    AssertEqual(13L, write.Value);
    AssertEqual("shared", write.SourceClientId);
}

static async Task MqttIncomingObservedPublishImport()
{
    var broker = new InMemoryItemServer();
    var observer = new RecordingClient("observer");
    var adapter = new MqttItemServerAdapter(new MqttItemServerOptions
    {
        Enabled = false,
        AllowObservedInboundPublishes = true,
    });

    await broker.SubscribeAsync(
        client: observer,
        path: "edm1.temperature",
        options: SubscribeOptions(recursive: false));
    await adapter.StartAsync(broker);

    await adapter.HandleIncomingPublishAsync("hornet/Edm1/Temperature/read", "23.5", cancellationToken: default);

    var snapshot = AssertSingle<ItemSnapshotMessage>(observer.Messages);
    AssertEqual("edm1.temperature", snapshot.Path);
    AssertEqual(23.5d, snapshot.ItemModel.Value);
    AssertEqual("shared", snapshot.SourceClientId);
}

static async Task MqttIncomingObservedPublishImportPreservesValueAcrossParameters()
{
    var broker = new InMemoryItemServer();
    var adapter = new MqttItemServerAdapter(new MqttItemServerOptions
    {
        Enabled = false,
        AllowObservedInboundPublishes = true,
    });

    var observer = new RecordingClient("observer");
    await broker.SubscribeAsync(
        client: observer,
        path: "edm1.temperature",
        options: SubscribeOptions(recursive: false));
    await adapter.StartAsync(broker);

    await adapter.HandleIncomingPublishAsync("hornet/Edm1/Temperature/read", "23.5", cancellationToken: default);
    await adapter.HandleIncomingPublishAsync("hornet/Edm1/Temperature/unit", "degC", cancellationToken: default);

    var snapshot = AssertSingle<ItemSnapshotMessage>(observer.Messages);
    var parameterChanged = AssertSingle<ItemPropertyChangedMessage>(observer.Messages);
    AssertEqual("edm1.temperature", snapshot.Path);
    AssertEqual(23.5d, snapshot.ItemModel.Value);
    AssertEqual("shared", snapshot.SourceClientId);
    AssertEqual("edm1.temperature", parameterChanged.Path);
    AssertEqual("unit", parameterChanged.PropertyName);
    AssertEqual("degC", parameterChanged.Value);
    AssertEqual("shared", parameterChanged.SourceClientId);
}

static async Task MqttIncomingNonWritableWriteIsBlocked()
{
    var broker = new InMemoryItemServer();
    var owner = new RecordingClient("owner");
    var adapter = new MqttItemServerAdapter(new MqttItemServerOptions { Enabled = false });
    await broker.SubscribeAsync(
        client: owner,
        path: "runtime.Device",
        options: SubscribeOptions(recursive: true));
    await broker.PublishSnapshotAsync(
        item: new ItemModel("Device").Repath("runtime.Device"),
        sourceClientId: owner.ClientId);
    await adapter.StartAsync(broker);

    await adapter.HandleIncomingPublishAsync("hornet/runtime/device/read", "13");

    AssertEqual(0, owner.Messages.OfType<ItemWriteRequestMessage>().Count());
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
        var item = new ItemModel("Temperature", 22.0, "Demo");
        item.Properties["unit"].Value = "degC";
        item["Raw"].Value = "0.0";

        await session.PublishSnapshotAsync(item).ConfigureAwait(false);

        await WaitForMqttMessageAsync(publishedMessages, "hornet/demo/temperature", "{}").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/demo/temperature/read", "22.0").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/demo/temperature/unit", "degC").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/demo/temperature/raw", "{}").ConfigureAwait(false);
        await WaitForMqttMessageAsync(publishedMessages, "hornet/demo/temperature/raw/read", "0.0").ConfigureAwait(false);
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

        await publisher.UpdateValueAsync(new ItemModel("Temperature", 23.5).Repath("Edm1.Temperature")).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var roots = subscriber.RemoteItems.GetClientRoots();
            if (roots.TryGetValue("shared", out var root)
                && object.Equals(23.5, root["edm1"]["temperature"].Value))
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

static async Task MqttRemoteClientValueUpdateRefreshesRetainedState()
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
        await using var publisher = new MqttRemoteItemClient(new MqttClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "retained-update-publisher",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.ConnectAsync().ConfigureAwait(false);
        await publisher.PublishSnapshotAsync(new ItemModel("Request", 1).Repath("studio.Folder1.UdlClient1.m300.set.request")).ConfigureAwait(false);
        await publisher.UpdateValueAsync(new ItemModel("Request", 42).Repath("studio.Folder1.UdlClient1.m300.set.request")).ConfigureAwait(false);

        var retainedPayload = await ReadRetainedMqttPayloadAsync(
            host: IPAddress.Loopback.ToString(),
            port: port,
            topic: "hornet/studio/folder1/udl_client1/m300/set/request/read").ConfigureAwait(false);

        AssertEqual("42", retainedPayload);
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task MqttSelfhostedHostPublishesHealth()
{
    var port = GetAvailableTcpPort();
    await using var host = new MqttItemServerHost(new MqttItemServerOptions
    {
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        BaseTopic = "hornet",
        ClientId = "selfhosted-broker",
        HealthPublishInterval = TimeSpan.FromMilliseconds(100),
    });

    await host.StartAsync().ConfigureAwait(false);
    try
    {
        var retainedPayload = await ReadRetainedMqttPayloadAsync(
            host: IPAddress.Loopback.ToString(),
            port: port,
            topic: "$SYS/status/state").ConfigureAwait(false);

        AssertEqual("Running", retainedPayload);
    }
    finally
    {
        await host.StopAsync().ConfigureAwait(false);
    }
}

static async Task ItemServerHealthPublisherPublishesHealth()
{
    var broker = new InMemoryItemServer();
    await broker.PublishSnapshotAsync(
        item: new ItemModel("Read", 23.5).Repath("runtime.device.read"),
        sourceClientId: "publisher").ConfigureAwait(false);

    await using var publisher = new ItemServerHealthPublisher(
        server: broker,
        options: new ItemServerHealthOptions
        {
            ClientId = "core-health-test",
            PublishInterval = TimeSpan.FromSeconds(10),
        });

    await publisher.StartAsync().ConfigureAwait(false);
    try
    {
        var subscriber = new RecordingClient("health-subscriber");
        await broker.SubscribeAsync(
            client: subscriber,
            path: ItemServerHealthPaths.Root,
            options: SubscribeOptions(recursive: true)).ConfigureAwait(false);

        var retained = subscriber.Messages.OfType<ItemSnapshotMessage>().ToArray();
        var stateSnapshot = retained.Single(message => string.Equals(message.Path, ItemServerHealthPaths.StatusState, StringComparison.OrdinalIgnoreCase));
        var itemCountSnapshot = retained.Single(message => string.Equals(message.Path, ItemServerHealthPaths.MetricsItemCount, StringComparison.OrdinalIgnoreCase));
        var startedAtSnapshot = retained.Single(message => string.Equals(message.Path, ItemServerHealthPaths.StatusStartedAtUtc, StringComparison.OrdinalIgnoreCase));

        AssertEqual("Running", stateSnapshot.ItemModel.Value);
        AssertEqual(1, itemCountSnapshot.ItemModel.Value);
        AssertFalse(string.IsNullOrWhiteSpace(startedAtSnapshot.ItemModel.Value?.ToString()));
    }
    finally
    {
        await publisher.StopAsync().ConfigureAwait(false);
    }
}

static async Task MqttSelfhostedHostPublishesRuntimeCounts()
{
    var port = GetAvailableTcpPort();
    await using var host = new MqttItemServerHost(new MqttItemServerOptions
    {
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        BaseTopic = "hornet",
        ClientId = "selfhosted-broker",
        HealthPublishInterval = TimeSpan.FromMilliseconds(100),
    });

    await host.StartAsync().ConfigureAwait(false);
    using var client = new MqttClientFactory().CreateMqttClient();

    try
    {
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId("external-runtime-client")
            .WithTcpServer(IPAddress.Loopback.ToString(), port)
            .Build()).ConfigureAwait(false);

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic("hornet/edm1/pressure/read")
            .WithPayload("1011.4")
            .WithRetainFlag(true)
            .Build()).ConfigureAwait(false);
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic("hornet/edm1/pressure/unit")
            .WithPayload("hPa")
            .WithRetainFlag(true)
            .Build()).ConfigureAwait(false);
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic("hornet/edm1/temperature/read")
            .WithPayload("22.0")
            .WithRetainFlag(true)
            .Build()).ConfigureAwait(false);

        var clientCount = await WaitForRetainedMqttPayloadAsync(
            host: IPAddress.Loopback.ToString(),
            port: port,
            topic: "$SYS/mqtt/status/client_count",
            expectedPayload: "1.00").ConfigureAwait(false);
        var itemCount = await WaitForRetainedMqttPayloadAsync(
            host: IPAddress.Loopback.ToString(),
            port: port,
            topic: "$SYS/metrics/item_count",
            expectedPayload: "2.00").ConfigureAwait(false);

        AssertEqual("1.00", clientCount);
        AssertEqual("2.00", itemCount);
    }
    finally
    {
        if (client.IsConnected)
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }

        await host.StopAsync().ConfigureAwait(false);
    }
}

static async Task MqttRemoteClientHidesSelfPublishedItems()
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
        await using var client = new MqttRemoteItemClient(new MqttClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            BaseTopic = "hornet",
            ClientId = "self-hide-client",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await client.ConnectAsync().ConfigureAwait(false);
        await client.PublishSnapshotAsync(new ItemModel("Pressure", 12.5).Repath("studio.SelfEcho.Pressure")).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

        AssertFalse(client.GetRemoteItemSnapshots().Values.Any(root => ContainsItemPath(root, "studio.SelfEcho.Pressure")));
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static ItemSubscriptionOptions SubscribeOptions(bool recursive)
    => new()
    {
        Recursive = recursive,
        IncludeRetained = true,
    };

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

static async Task<string> ReadRetainedMqttPayloadAsync(string host, int port, string topic)
{
    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var client = new MqttClientFactory().CreateMqttClient();
    client.ApplicationMessageReceivedAsync += args =>
    {
        if (string.Equals(args.ApplicationMessage.Topic, topic, StringComparison.OrdinalIgnoreCase))
        {
            var payload = args.ApplicationMessage.Payload.IsEmpty
                ? string.Empty
                : Encoding.UTF8.GetString(args.ApplicationMessage.Payload);
            completion.TrySetResult(payload);
        }

        return Task.CompletedTask;
    };

    await client.ConnectAsync(new MqttClientOptionsBuilder()
        .WithClientId($"retained-reader-{Guid.NewGuid():N}")
        .WithTcpServer(host, port)
        .Build()).ConfigureAwait(false);

    try
    {
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic)
            .Build()).ConfigureAwait(false);

        var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        if (!ReferenceEquals(completed, completion.Task))
        {
            throw new InvalidOperationException($"No retained MQTT payload was received for topic '{topic}'.");
        }

        return await completion.Task.ConfigureAwait(false);
    }
    finally
    {
        if (client.IsConnected)
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }

        client.Dispose();
    }
}

static async Task<string> WaitForRetainedMqttPayloadAsync(string host, int port, string topic, string expectedPayload)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    var lastPayload = string.Empty;
    var lastError = string.Empty;

    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            lastPayload = await ReadRetainedMqttPayloadAsync(host, port, topic).ConfigureAwait(false);
            if (string.Equals(lastPayload, expectedPayload, StringComparison.Ordinal))
            {
                return lastPayload;
            }
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
    }

    throw new InvalidOperationException($"Expected retained MQTT payload '{expectedPayload}' for topic '{topic}', actual '{lastPayload}'. Last error: {lastError}");
}

static T AssertSingle<T>(IReadOnlyList<ItemServerMessage> messages)
    where T : ItemServerMessage
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

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action().ConfigureAwait(false);
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception '{typeof(TException).Name}'.");
}

static bool ContainsItemPath(ItemModel root, string path)
{
    var segments = path
        .Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var current = root;
    foreach (var segment in segments)
    {
        var matchingChildName = current.GetDictionary().Keys
            .FirstOrDefault(key => string.Equals(key, segment, StringComparison.OrdinalIgnoreCase));
        if (matchingChildName is null)
        {
            return false;
        }

        current = current.GetDictionary()[matchingChildName];
    }

    return true;
}

sealed class RecordingClient : IItemServerClient
{
    public RecordingClient(string clientId)
    {
        ClientId = clientId;
    }

    public string ClientId { get; }

    public List<ItemServerMessage> Messages { get; } = new();

    public Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default)
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
