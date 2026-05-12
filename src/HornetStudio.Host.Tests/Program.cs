using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server;
using Amium.Item.Server.Mqtt;
using Amium.Item.Client;
using HornetStudio.Contracts;
using HornetStudio.Host;
using MQTTnet.Server;
using System.Reflection;
using System.Net;
using System.Net.Sockets;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Exact root resolve", () => RunSync(ExactRootResolve)),
    ("Descendant resolve", () => RunSync(DescendantResolve)),
    ("Mixed separators", () => RunSync(MixedSeparatorsResolve)),
    ("Case-insensitive resolve", () => RunSync(CaseInsensitiveResolve)),
    ("Legacy Project path resolves to Studio item", () => RunSync(LegacyProjectPathResolvesToStudioItem)),
    ("Longest root wins", () => RunSync(LongestRootWins)),
    ("Missing child returns false", () => RunSync(MissingChildReturnsFalse)),
    ("UpdateValue updates descendant item", () => RunSync(UpdateValueUpdatesDescendantItem)),
    ("UpdateValue applies explicit epoch", () => RunSync(UpdateValueAppliesExplicitEpoch)),
    ("UpdateValue ignores unchanged descendant item", () => RunSync(UpdateValueIgnoresUnchangedDescendantItem)),
    ("UpdateValue converts numeric payloads to existing type", () => RunSync(UpdateValueConvertsNumericPayloadsToExistingType)),
    ("UpdateParameter updates descendant parameter", () => RunSync(UpdateParameterUpdatesDescendantParameter)),
    ("UpdateParameter ignores unchanged descendant parameter", () => RunSync(UpdateParameterIgnoresUnchangedDescendantParameter)),
    ("UpdateParameter converts numeric payloads to existing type", () => RunSync(UpdateParameterConvertsNumericPayloadsToExistingType)),
    ("Protected parameter policy detects protected names", () => RunSync(ProtectedParameterPolicyDetectsProtectedNames)),
    ("Guarded user parameter write rejects protected names", () => RunSync(GuardedUserParameterWriteRejectsProtectedNames)),
    ("Internal parameter update allows protected names", () => RunSync(InternalParameterUpdateAllowsProtectedNames)),
    ("Metadata defaults exclude broker publish", () => RunSync(MetadataDefaultsExcludeBrokerPublish)),
    ("Broker received metadata excludes broker publish", () => RunSync(BrokerReceivedMetadataExcludesBrokerPublish)),
    ("Metadata capability query returns publishable keys", () => RunSync(MetadataCapabilityQueryReturnsPublishableKeys)),
    ("Remove clears indexed descendants", () => RunSync(RemoveClearsIndexedDescendants)),
    ("Prune clears stale descendants", () => RunSync(PruneClearsStaleDescendants)),
    ("Signal lookup works for descendants", () => RunSync(SignalLookupWorksForDescendants)),
    ("Signal update fires for descendant updates", () => RunSync(SignalUpdateFiresForDescendantUpdates)),
    ("UI folder child source update preserves request child", () => RunSync(UiFolderChildSourceUpdatePreservesRequestChild)),
    ("Host UDL client creates flat channels", () => RunSync(HostUdlClientCreatesFlatChannels)),
    ("Host item broker client receives live items", HostItemBrokerClientReceivesLiveItems),
    ("Host item broker client hides self-published items", HostItemBrokerClientHidesSelfPublishedItems),
    ("Host item broker client snapshots are detached", HostItemBrokerClientSnapshotsAreDetached),
    ("Host item broker client publishes local snapshots", HostItemBrokerClientPublishesLocalSnapshots),
    ("Owned item broker host starts and disposes endpoint", OwnedItemBrokerHostStartsAndDisposesEndpoint),
    ("Owned item broker host fails on occupied endpoint", OwnedItemBrokerHostFailsOnOccupiedEndpoint),
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
    Console.Error.WriteLine("Host registry tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"Host registry tests passed: {tests.Length}");
return 0;

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static void ExactRootResolve()
{
    var registry = new DataRegistry();
    var root = CreateDeviceSnapshot(1);
    registry.UpsertSnapshot("runtime.device", root);

    AssertTrue(registry.TryResolve("runtime.device", out var resolved));
    AssertSame(root, resolved);
}

static void DescendantResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(1, resolved?.Value);
}

static void MixedSeparatorsResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(2));

    AssertTrue(registry.TryResolve(@"runtime/device\read", out var resolved));
    AssertEqual(2, resolved?.Value);
}

static void CaseInsensitiveResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(3));

    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(3, resolved?.Value);
}

static void LegacyProjectPathResolvesToStudioItem()
{
    var registry = new DataRegistry();
    var item = ItemExtension.CreateWithPath("studio.default_layout.signal", 7);
    registry.UpsertSnapshot("studio.default_layout.signal", item);

    AssertTrue(registry.TryResolve("project.default_layout.signal", out var resolved));
    AssertSame(item, resolved);
}

static void LongestRootWins()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));
    registry.UpsertSnapshot("runtime.device.read", ItemExtension.CreateWithPath("runtime.device.read", 2));

    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(2, resolved?.Value);
}

static void MissingChildReturnsFalse()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertFalse(registry.TryResolve("runtime.device.missing", out _));
}

static void UpdateValueUpdatesDescendantItem()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateValue("runtime.device.read", 4));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(4, resolved?.Value);
}

static void UpdateValueAppliesExplicitEpoch()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateValue("runtime.device.read", 4, 123UL));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(123UL, resolved?.Properties["epoch"].Value);
}

static void UpdateValueIgnoresUnchangedDescendantItem()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));
    var changeCount = 0;
    registry.ItemChanged += (_, e) =>
    {
        if (e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            changeCount++;
        }
    };

    AssertTrue(registry.UpdateValue("runtime.device.read", 1));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(1, resolved?.Value);
    AssertEqual(0, changeCount);
}

static void UpdateValueConvertsNumericPayloadsToExistingType()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", ItemExtension.CreateWithPath("runtime.device"));
    registry.UpdateValue("runtime.device", 1.5);

    AssertTrue(registry.UpdateValue("runtime.device", 2L));
    AssertTrue(registry.TryResolve("runtime.device", out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void UpdateParameterUpdatesDescendantParameter()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateProperty("runtime.device.read", "Unit", "bar"));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual("bar", resolved?.Properties["unit"].Value);
}

static void UpdateParameterIgnoresUnchangedDescendantParameter()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));
    var changeCount = 0;
    registry.ItemChanged += (_, e) =>
    {
        if (e.ChangeKind == DataChangeKind.PropertyUpdated)
        {
            changeCount++;
        }
    };

    AssertTrue(registry.UpdateProperty("runtime.device.read", "Unit", "V"));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual("V", resolved?.Properties["unit"].Value);
    AssertEqual(0, changeCount);
}

static void UpdateParameterConvertsNumericPayloadsToExistingType()
{
    var registry = new DataRegistry();
    var item = ItemExtension.CreateWithPath("runtime.device");
    item.Properties["Scale"].Value = 1.5;
    registry.UpsertSnapshot("runtime.device", item);

    AssertTrue(registry.UpdateProperty("runtime.device", "Scale", 2L));
    AssertTrue(registry.TryResolve("runtime.device", out var resolved));
    AssertEqual(2.0, resolved?.Properties["Scale"].Value);
}

static void ProtectedParameterPolicyDetectsProtectedNames()
{
    AssertTrue(HostRegistryPropertyPolicy.IsProtectedProperty("Writable"));
    AssertTrue(HostRegistryPropertyPolicy.IsProtectedProperty("writepath"));
    AssertFalse(HostRegistryPropertyPolicy.IsProtectedProperty("Value"));
    AssertFalse(HostRegistryPropertyPolicy.IsProtectedProperty("Unit"));
}

static void GuardedUserParameterWriteRejectsProtectedNames()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertFalse(registry.TryUpdateUserProperty("runtime.device.read", "Writable", false));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(true, resolved?.Properties["writable"].Value);
}

static void InternalParameterUpdateAllowsProtectedNames()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateProperty("runtime.device.read", "Writable", false));
    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(false, resolved?.Properties["writable"].Value);
}

static void MetadataDefaultsExcludeBrokerPublish()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.metadata.default", ItemExtension.CreateWithPath("runtime.metadata.default"));

    AssertTrue(registry.TryGetMetadata("runtime.metadata.default", out var metadata));
    AssertFalse(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerPublish));
    AssertEqual(0, registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish).Count);
}

static void BrokerReceivedMetadataExcludesBrokerPublish()
{
    var metadata = DataRegistryItemMetadata.BrokerReceivedData();

    AssertEqual(DataRegistryItemRole.Data, metadata.Role);
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Display));
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerAttach));
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.UdlAttach));
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Log));
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.DebugInspect));
    AssertFalse(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerPublish));

    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.item_broker.broker1.client1.device", ItemExtension.CreateWithPath("runtime.item_broker.broker1.client1.device"), metadata);

    AssertTrue(registry.TryGetMetadata("runtime.item_broker.broker1.client1.device", out var storedMetadata));
    AssertEqual(metadata, storedMetadata);
    AssertFalse(registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish).Contains("runtime.item_broker.broker1.client1.device", StringComparer.OrdinalIgnoreCase));
    AssertTrue(registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerAttach).Contains("runtime.item_broker.broker1.client1.device", StringComparer.OrdinalIgnoreCase));
    AssertTrue(registry.GetKeysByCapability(DataRegistryItemCapabilities.DebugInspect).Contains("runtime.item_broker.broker1.client1.device", StringComparer.OrdinalIgnoreCase));
}

static void MetadataCapabilityQueryReturnsPublishableKeys()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.metadata.public", ItemExtension.CreateWithPath("runtime.metadata.public"), DataRegistryItemMetadata.PublicData());
    registry.UpsertSnapshot("runtime.metadata.internal", ItemExtension.CreateWithPath("runtime.metadata.internal"), DataRegistryItemMetadata.WidgetInternal());

    var keys = registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish);

    AssertTrue(keys.Contains("runtime.metadata.public", StringComparer.OrdinalIgnoreCase));
    AssertFalse(keys.Contains("runtime.metadata.internal", StringComparer.OrdinalIgnoreCase));
}

static void RemoveClearsIndexedDescendants()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(registry.Remove("runtime.device"));
    AssertFalse(registry.TryResolve("runtime.device.read", out _));
}

static void PruneClearsStaleDescendants()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));
    registry.UpsertSnapshot("runtime.device", ItemExtension.CreateWithPath("runtime.device"), pruneMissingMembers: true);

    AssertFalse(registry.TryResolve("runtime.device.read", out _));
}

static void SignalLookupWorksForDescendants()
{
    var registry = new DataRegistry();
    var signals = new SignalRegistry(registry);
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));

    AssertTrue(signals.TryGetBySourcePath(@"runtime/device\read", out var signal));
    AssertEqual("runtime.device.read", signal?.Descriptor.SourcePath);
}

static void SignalUpdateFiresForDescendantUpdates()
{
    var registry = new DataRegistry();
    var signals = new SignalRegistry(registry);
    registry.UpsertSnapshot("runtime.device", CreateDeviceSnapshot(1));
    AssertTrue(signals.TryGetBySourcePath("runtime.device.read", out var signal));

    object? changedValue = null;
    signal!.ValueChanged += (_, e) => changedValue = e.NewValue;

    AssertTrue(registry.UpdateValue(@"runtime/device\read", 5));
    AssertEqual(5, changedValue);
}

static void UiFolderChildSourceUpdatePreservesRequestChild()
{
    var source = ItemExtension.CreateWithPath("runtime.ui_folder_mirror.m300", 0);
    source["read"].Value = 0;
    source["set"]["request"].Value = 0;
    using var context = new UiFolderContext("MirrorTest");
    var attached = context.Attach(source, "m300");
    HostRegistries.Data.UpsertSnapshot(attached.Path!, attached);

    AssertTrue(HostRegistries.Data.UpdateValue("studio.mirror_test.m300.set.request", 42));
    source["read"].Value = 1;

    AssertTrue(HostRegistries.Data.TryResolve("studio.mirror_test.m300.read", out var read));
    AssertEqual(1, read?.Value);
    AssertTrue(HostRegistries.Data.TryResolve("studio.mirror_test.m300.set.request", out var request));
    AssertEqual(42, request?.Value);
}

static void HostUdlClientCreatesFlatChannels()
{
    var client = new HostUdlClient("test", "127.0.0.1", 9001);
    var createModuleMethod = typeof(HostUdlClient).GetMethod("GetOrCreateModule", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(createModuleMethod is not null);

    var module = (ItemModel?)createModuleMethod!.Invoke(client, [1u]);
    AssertTrue(module is not null);
    AssertFalse(module!.Has("Command"));

    AssertTrue(module.Has("read"));
    AssertTrue(module["read"].Properties.Has("read"));
    AssertTrue(module["read"].Properties.Has("write"));
    AssertFalse(module["read"].Has("request"));

    AssertTrue(module.Has("state"));
    AssertTrue(module["state"].Properties.Has("read"));
    AssertTrue(module["state"].Properties.Has("write"));

    AssertTrue(module.Has("alert"));
    AssertTrue(module["alert"].Properties.Has("read"));
    AssertFalse(module["alert"].Properties.Has("write"));
}

static async Task HostItemBrokerClientReceivesLiveItems()
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
        await using var hostClient = new HostItemBrokerClient("BrokerWidget1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-test");
        await hostClient.ConnectAsync().ConfigureAwait(false);

        await using var publisher = new MqttItemClientSession(new MqttItemClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClient1",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.PublishReadAsync(ItemExtension.CreateWithPath("edm1.temperature", 23.5), retained: true).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (hostClient.Items.GetDictionary().TryGetValue("shared", out var root)
                && object.Equals(23.5f, root["edm1"]["temperature"].Value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Host client did not mirror live MQTT items.");
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task HostItemBrokerClientHidesSelfPublishedItems()
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
        await using var hostClient = new HostItemBrokerClient("BrokerWidgetSelfEcho", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-self-echo-test");
        await hostClient.ConnectAsync().ConfigureAwait(false);

        await hostClient.PublishSnapshotAsync(ItemExtension.CreateWithPath("studio.self_echo.pressure", 12.5)).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

        AssertFalse(hostClient.GetItemSnapshots().Values.Any(root => ContainsItemPath(root, "studio.self_echo.pressure")));
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task HostItemBrokerClientSnapshotsAreDetached()
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
        await using var hostClient = new HostItemBrokerClient("BrokerWidget1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-snapshot-test");
        await hostClient.ConnectAsync().ConfigureAwait(false);

        await using var publisher = new MqttItemClientSession(new MqttItemClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClient2",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.PublishReadAsync(ItemExtension.CreateWithPath("edm1.temperature", 23.5), retained: true).ConfigureAwait(false);
        await WaitForSnapshotAsync(hostClient, "shared").ConfigureAwait(false);

        var snapshot = hostClient.GetItemSnapshots();
        snapshot["shared"]["edm1"]["temperature"].Value = 99.0f;

        var nextSnapshot = hostClient.GetItemSnapshots();
        AssertEqual(23.5f, nextSnapshot["shared"]["edm1"]["temperature"].Value);
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task HostItemBrokerClientPublishesLocalSnapshots()
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
        await using var publisher = new HostItemBrokerClient("BrokerWidget1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-publish-test");
        await publisher.ConnectAsync().ConfigureAwait(false);

        await using var receiver = new MqttItemClientSession(new MqttItemClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "SnapshotReceiver",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });
        await receiver.ConnectAsync().ConfigureAwait(false);

        await publisher.PublishSnapshotAsync(ItemExtension.CreateWithPath("studio.default_layout.edm1.pressure", 12.5)).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (receiver.RemoteItems.GetItemRoots().TryGetValue("studio", out var root)
                && object.Equals(12.5f, root["default_layout"]["edm1"]["pressure"].Value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Published local snapshot was not received.");
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task OwnedItemBrokerHostStartsAndDisposesEndpoint()
{
    var port = GetAvailableTcpPort();
    var host = new MqttItemServerHost(new MqttItemServerOptions
    {
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        BaseTopic = "hornet",
        ClientId = "owned-broker-test",
    });

    try
    {
        await host.StartAsync().ConfigureAwait(false);
        await using (var hostClient = new HostItemBrokerClient("BrokerWidget1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-owned-test"))
        {
            await hostClient.ConnectAsync().ConfigureAwait(false);
            AssertTrue(hostClient.IsConnected);
        }
    }
    finally
    {
        await host.DisposeAsync().ConfigureAwait(false);
    }
}

static async Task OwnedItemBrokerHostFailsOnOccupiedEndpoint()
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
        await using var host = new MqttItemServerHost(new MqttItemServerOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            BaseTopic = "hornet",
            ClientId = "owned-broker-failure-test",
        });

        var failed = false;
        try
        {
            await host.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            failed = true;
        }

        AssertTrue(failed);
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task WaitForSnapshotAsync(HostItemBrokerClient hostClient, string clientId)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (hostClient.GetItemSnapshots().TryGetValue(clientId, out var root)
            && object.Equals(23.5f, root["edm1"]["temperature"].Value))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
    }

    throw new InvalidOperationException("Host client did not expose snapshot items.");
}

static ItemModel CreateDeviceSnapshot(int readValue)
{
    var root = new ItemModel("device");
    root["read"].Value = readValue;
    root["read"].Properties["unit"].Value = "V";
    root["read"].Properties["writable"].Value = true;
    return ItemExtension.CloneWithPath(root, "runtime.device");
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

static void AssertSame(object expected, object? actual)
{
    if (!ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException($"Expected same instance as {expected}.");
    }
}

static void AssertEqual(object? expected, object? actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }
}
