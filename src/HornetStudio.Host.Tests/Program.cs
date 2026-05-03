using Amium.Items;
using Amium.ItemBroker;
using Amium.ItemBroker.Mqtt;
using Amium.ItemBroker.Mqtt.Client;
using HornetStudio.Contracts;
using HornetStudio.Host;
using MQTTnet.Server;
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
    ("Host item broker client receives live items", HostItemBrokerClientReceivesLiveItems),
    ("Host item broker client hides self-published items", HostItemBrokerClientHidesSelfPublishedItems),
    ("Host item broker client snapshots are detached", HostItemBrokerClientSnapshotsAreDetached),
    ("Host item broker client publishes local snapshots", HostItemBrokerClientPublishesLocalSnapshots),
    ("Owned item broker adapter starts and disposes endpoint", OwnedItemBrokerAdapterStartsAndDisposesEndpoint),
    ("Owned item broker adapter fails on occupied endpoint", OwnedItemBrokerAdapterFailsOnOccupiedEndpoint),
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
    registry.UpsertSnapshot("Runtime.Device", root);

    AssertTrue(registry.TryResolve("Runtime.Device", out var resolved));
    AssertSame(root, resolved);
}

static void DescendantResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(1, resolved?.Value);
}

static void MixedSeparatorsResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(2));

    AssertTrue(registry.TryResolve(@"Runtime/Device\Read", out var resolved));
    AssertEqual(2, resolved?.Value);
}

static void CaseInsensitiveResolve()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(3));

    AssertTrue(registry.TryResolve("runtime.device.read", out var resolved));
    AssertEqual(3, resolved?.Value);
}

static void LegacyProjectPathResolvesToStudioItem()
{
    var registry = new DataRegistry();
    var item = new Item("Signal", 7).Repath("Studio.DefaultLayout.Signal");
    registry.UpsertSnapshot("Studio.DefaultLayout.Signal", item);

    AssertTrue(registry.TryResolve("Project.DefaultLayout.Signal", out var resolved));
    AssertSame(item, resolved);
}

static void LongestRootWins()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));
    registry.UpsertSnapshot("Runtime.Device.Read", new Item("Read", 2).Repath("Runtime.Device.Read"));

    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(2, resolved?.Value);
}

static void MissingChildReturnsFalse()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertFalse(registry.TryResolve("Runtime.Device.Missing", out _));
}

static void UpdateValueUpdatesDescendantItem()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateValue("Runtime.Device.Read", 4));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(4, resolved?.Value);
}

static void UpdateValueIgnoresUnchangedDescendantItem()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));
    var changeCount = 0;
    registry.ItemChanged += (_, e) =>
    {
        if (e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            changeCount++;
        }
    };

    AssertTrue(registry.UpdateValue("Runtime.Device.Read", 1));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(1, resolved?.Value);
    AssertEqual(0, changeCount);
}

static void UpdateValueConvertsNumericPayloadsToExistingType()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", new Item("Device").Repath("Runtime.Device"));
    registry.UpdateValue("Runtime.Device", 1.5);

    AssertTrue(registry.UpdateValue("Runtime.Device", 2L));
    AssertTrue(registry.TryResolve("Runtime.Device", out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void UpdateParameterUpdatesDescendantParameter()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateParameter("Runtime.Device.Read", "Unit", "bar"));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual("bar", resolved?.Params["Unit"].Value);
}

static void UpdateParameterIgnoresUnchangedDescendantParameter()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));
    var changeCount = 0;
    registry.ItemChanged += (_, e) =>
    {
        if (e.ChangeKind == DataChangeKind.ParameterUpdated)
        {
            changeCount++;
        }
    };

    AssertTrue(registry.UpdateParameter("Runtime.Device.Read", "Unit", "V"));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual("V", resolved?.Params["Unit"].Value);
    AssertEqual(0, changeCount);
}

static void UpdateParameterConvertsNumericPayloadsToExistingType()
{
    var registry = new DataRegistry();
    var item = new Item("Device").Repath("Runtime.Device");
    item.Params["Scale"].Value = 1.5;
    registry.UpsertSnapshot("Runtime.Device", item);

    AssertTrue(registry.UpdateParameter("Runtime.Device", "Scale", 2L));
    AssertTrue(registry.TryResolve("Runtime.Device", out var resolved));
    AssertEqual(2.0, resolved?.Params["Scale"].Value);
}

static void ProtectedParameterPolicyDetectsProtectedNames()
{
    AssertTrue(HostRegistryParameterPolicy.IsProtectedParameter("Writable"));
    AssertTrue(HostRegistryParameterPolicy.IsProtectedParameter("writepath"));
    AssertFalse(HostRegistryParameterPolicy.IsProtectedParameter("Value"));
    AssertFalse(HostRegistryParameterPolicy.IsProtectedParameter("Unit"));
}

static void GuardedUserParameterWriteRejectsProtectedNames()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertFalse(registry.TryUpdateUserParameter("Runtime.Device.Read", "Writable", false));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(true, resolved?.Params["Writable"].Value);
}

static void InternalParameterUpdateAllowsProtectedNames()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(registry.UpdateParameter("Runtime.Device.Read", "Writable", false));
    AssertTrue(registry.TryResolve("Runtime.Device.Read", out var resolved));
    AssertEqual(false, resolved?.Params["Writable"].Value);
}

static void MetadataDefaultsExcludeBrokerPublish()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Metadata.Default", new Item("Default").Repath("Runtime.Metadata.Default"));

    AssertTrue(registry.TryGetMetadata("Runtime.Metadata.Default", out var metadata));
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
    registry.UpsertSnapshot("Runtime.ItemBroker.Broker1.Client1.Device", new Item("Device").Repath("Runtime.ItemBroker.Broker1.Client1.Device"), metadata);

    AssertTrue(registry.TryGetMetadata("Runtime.ItemBroker.Broker1.Client1.Device", out var storedMetadata));
    AssertEqual(metadata, storedMetadata);
    AssertFalse(registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish).Contains("Runtime.ItemBroker.Broker1.Client1.Device", StringComparer.OrdinalIgnoreCase));
    AssertTrue(registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerAttach).Contains("Runtime.ItemBroker.Broker1.Client1.Device", StringComparer.OrdinalIgnoreCase));
    AssertTrue(registry.GetKeysByCapability(DataRegistryItemCapabilities.DebugInspect).Contains("Runtime.ItemBroker.Broker1.Client1.Device", StringComparer.OrdinalIgnoreCase));
}

static void MetadataCapabilityQueryReturnsPublishableKeys()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Metadata.Public", new Item("Public").Repath("Runtime.Metadata.Public"), DataRegistryItemMetadata.PublicData());
    registry.UpsertSnapshot("Runtime.Metadata.Internal", new Item("Internal").Repath("Runtime.Metadata.Internal"), DataRegistryItemMetadata.WidgetInternal());

    var keys = registry.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish);

    AssertTrue(keys.Contains("Runtime.Metadata.Public", StringComparer.OrdinalIgnoreCase));
    AssertFalse(keys.Contains("Runtime.Metadata.Internal", StringComparer.OrdinalIgnoreCase));
}

static void RemoveClearsIndexedDescendants()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(registry.Remove("Runtime.Device"));
    AssertFalse(registry.TryResolve("Runtime.Device.Read", out _));
}

static void PruneClearsStaleDescendants()
{
    var registry = new DataRegistry();
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));
    registry.UpsertSnapshot("Runtime.Device", new Item("Device").Repath("Runtime.Device"), pruneMissingMembers: true);

    AssertFalse(registry.TryResolve("Runtime.Device.Read", out _));
}

static void SignalLookupWorksForDescendants()
{
    var registry = new DataRegistry();
    var signals = new SignalRegistry(registry);
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));

    AssertTrue(signals.TryGetBySourcePath(@"runtime/device\read", out var signal));
    AssertEqual("Runtime.Device.Read", signal?.Descriptor.SourcePath);
}

static void SignalUpdateFiresForDescendantUpdates()
{
    var registry = new DataRegistry();
    var signals = new SignalRegistry(registry);
    registry.UpsertSnapshot("Runtime.Device", CreateDeviceSnapshot(1));
    AssertTrue(signals.TryGetBySourcePath("Runtime.Device.Read", out var signal));

    object? changedValue = null;
    signal!.ValueChanged += (_, e) => changedValue = e.NewValue;

    AssertTrue(registry.UpdateValue(@"runtime/device\read", 5));
    AssertEqual(5, changedValue);
}

static void UiFolderChildSourceUpdatePreservesRequestChild()
{
    var source = new Item("m300", 0).Repath("Runtime.UiFolderMirror.m300");
    source["Read"].Value = 0;
    source["Set"]["Request"].Value = 0;
    using var context = new UiFolderContext("MirrorTest");
    var attached = context.Attach(source, "m300");
    HostRegistries.Data.UpsertSnapshot(attached.Path!, attached);

    AssertTrue(HostRegistries.Data.UpdateValue("Studio.MirrorTest.m300.Set.Request", 42));
    source["Read"].Value = 1;

    AssertTrue(HostRegistries.Data.TryResolve("Studio.MirrorTest.m300.Read", out var read));
    AssertEqual(1, read?.Value);
    AssertTrue(HostRegistries.Data.TryResolve("Studio.MirrorTest.m300.Set.Request", out var request));
    AssertEqual(42, request?.Value);
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

        await using var publisher = new MqttItemBrokerClientSession(new MqttItemBrokerClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClient1",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.UpdateValueAsync(new Item("Temperature", 23.5).Repath("Edm1.Temperature")).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (hostClient.Items.GetDictionary().TryGetValue("shared", out var root)
                && object.Equals(23.5, root["Edm1"]["Temperature"].Value))
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

        await hostClient.PublishSnapshotAsync(new Item("Pressure", 12.5).Repath("Studio.SelfEcho.Pressure")).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

        AssertFalse(hostClient.GetItemSnapshots().Values.Any(root => ContainsItemPath(root, "Studio.SelfEcho.Pressure")));
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

        await using var publisher = new MqttItemBrokerClientSession(new MqttItemBrokerClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClient2",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.UpdateValueAsync(new Item("Temperature", 23.5).Repath("Edm1.Temperature")).ConfigureAwait(false);
        await WaitForSnapshotAsync(hostClient, "shared").ConfigureAwait(false);

        var snapshot = hostClient.GetItemSnapshots();
        snapshot["shared"]["Edm1"]["Temperature"].Value = 99.0;

        var nextSnapshot = hostClient.GetItemSnapshots();
        AssertEqual(23.5, nextSnapshot["shared"]["Edm1"]["Temperature"].Value);
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

        await using var receiver = new MqttItemBrokerClientSession(new MqttItemBrokerClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "SnapshotReceiver",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });
        await receiver.ConnectAsync().ConfigureAwait(false);

        await publisher.PublishSnapshotAsync(new Item("Pressure", 12.5).Repath("Studio.DefaultLayout.Edm1.Pressure")).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (receiver.RemoteItems.GetClientRoots().TryGetValue("shared", out var root)
                && object.Equals(12.5, root["Studio"]["DefaultLayout"]["Edm1"]["Pressure"].Value))
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

static async Task OwnedItemBrokerAdapterStartsAndDisposesEndpoint()
{
    var port = GetAvailableTcpPort();
    var adapter = new MqttItemBrokerAdapter(new MqttItemBrokerOptions
    {
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        BaseTopic = "hornet",
        ClientId = "owned-broker-test",
    });

    await adapter.StartAsync(new InMemoryItemBroker()).ConfigureAwait(false);
    await using (var hostClient = new HostItemBrokerClient("BrokerWidget1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-owned-test"))
    {
        await hostClient.ConnectAsync().ConfigureAwait(false);
        AssertTrue(hostClient.IsConnected);
    }

    await adapter.DisposeAsync().ConfigureAwait(false);

    var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
        .WithDefaultEndpoint()
        .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
        .WithDefaultEndpointPort(port)
        .Build());
    await server.StartAsync().ConfigureAwait(false);
    await server.StopAsync().ConfigureAwait(false);
    server.Dispose();
}

static async Task OwnedItemBrokerAdapterFailsOnOccupiedEndpoint()
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
        var adapter = new MqttItemBrokerAdapter(new MqttItemBrokerOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            BaseTopic = "hornet",
            ClientId = "owned-broker-failure-test",
        });

        var failed = false;
        try
        {
            await adapter.StartAsync(new InMemoryItemBroker()).ConfigureAwait(false);
        }
        catch
        {
            failed = true;
        }
        finally
        {
            await adapter.DisposeAsync().ConfigureAwait(false);
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
            && object.Equals(23.5, root["Edm1"]["Temperature"].Value))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
    }

    throw new InvalidOperationException("Host client did not expose snapshot items.");
}

static Item CreateDeviceSnapshot(int readValue)
{
    var root = new Item("Device");
    root["Read"].Value = readValue;
    root["Read"].Params["Unit"].Value = "V";
    root["Read"].Params["Writable"].Value = true;
    return root.Repath("Runtime.Device");
}

static bool ContainsItemPath(Item root, string path)
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
