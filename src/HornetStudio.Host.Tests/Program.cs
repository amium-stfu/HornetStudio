using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server;
using Amium.Item.Server.Mqtt;
using Amium.Item.Client;
using HornetStudio.Contracts;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using HornetStudio.Logging;
using HornetStudio.Host.Python.Client;
using MQTTnet.Server;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using HornetStudio.Host.Registries;
using HornetStudio.Host.Runtimes.Controller;
using HornetStudio.Host.Runtimes.EnhancedSignal;
using HornetStudio.Host.Logging.Values;

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
    ("Host item registry reads and writes memory reference", () => RunSync(HostItemRegistryReadsAndWritesMemoryReference)),
    ("Host item registry rejects duplicate paths", () => RunSync(HostItemRegistryRejectsDuplicatePaths)),
    ("Host item registry unregister blocks reads and writes", () => RunSync(HostItemRegistryUnregisterBlocksReadsAndWrites)),
    ("Value reference resolves source property without mutating public snapshot", () => RunSync(ValueReferenceResolvesSourcePropertyWithoutMutatingPublicSnapshot)),
    ("Value reference notification publishes public path", () => RunSync(ValueReferenceNotificationPublishesPublicPath)),
    ("Signal lookup works for descendants", () => RunSync(SignalLookupWorksForDescendants)),
    ("Signal update fires for descendant updates", () => RunSync(SignalUpdateFiresForDescendantUpdates)),
    ("UI folder child source update preserves write property", () => RunSync(UiFolderChildSourceUpdatePreservesWriteProperty)),
    ("UI folder coalesces runtime read updates to latest value", () => RunSync(UiFolderCoalescesRuntimeReadUpdatesToLatestValue)),
    ("UI folder initial publish preserves source runtime paths", () => RunSync(UiFolderInitialPublishPreservesSourceRuntimePaths)),
    ("UI folder dispose is safe during reentrant registry remove", () => RunSync(UiFolderDisposeIsSafeDuringReentrantRegistryRemove)),
    ("Host UDL client creates flat channels", () => RunSync(HostUdlClientCreatesFlatChannels)),
    ("Host UDL client queues non-state writeback", () => RunSync(HostUdlClientQueuesNonStateWriteback)),
    ("Host UDL client acknowledges matching writeback channel", () => RunSync(HostUdlClientAcknowledgesMatchingWritebackChannel)),
    ("Python client registry paths normalize to snake_case", PythonClientRegistryPathsNormalizeToSnakeCase),
    ("Enhanced signal runtime publishes snake_case write paths", () => RunSync(EnhancedSignalRuntimePublishesSnakeCaseWritePaths)),
    ("Enhanced signal runtime publishes type metadata", () => RunSync(EnhancedSignalRuntimePublishesTypeMetadata)),
    ("Enhanced signal set write forwards inverse adjustment", () => RunSync(EnhancedSignalSetWriteForwardsInverseAdjustment)),
    ("Enhanced signal prefers child read over source container text", () => RunSync(EnhancedSignalPrefersChildReadOverSourceContainerText)),
    ("Enhanced signal ignores nonnumeric source text for numeric conversion", () => RunSync(EnhancedSignalIgnoresNonnumericSourceTextForNumericConversion)),
    ("Enhanced signal manager keeps runtime when runtime definition mutates", () => RunSync(EnhancedSignalManagerKeepsRuntimeWhenRuntimeDefinitionMutates)),
    ("Registry direct update writes writable child channel", () => RunSync(RegistryDirectUpdateWritesWritableChildChannel)),
    ("PID controller runtime publishes stable paths", () => RunSync(PidControllerRuntimePublishesStablePaths)),
    ("PID controller stopped runtime stays quiet", () => RunSync(PidControllerStoppedRuntimeStaysQuiet)),
    ("PID controller stopped read mirrors available source", () => RunSync(PidControllerStoppedReadMirrorsAvailableSource)),
    ("PID controller stopped read updates on source change", () => RunSync(PidControllerStoppedReadUpdatesOnSourceChange)),
    ("PID controller run value starts and stops", () => RunSync(PidControllerRunValueStartsAndStops)),
    ("PID controller legacy write property still starts and stops", () => RunSync(PidControllerRunWritePropertyStartsAndStops)),
    ("Value log manager publishes control items", () => RunSync(ValueLogManagerPublishesControlItems)),
    ("Value log manager control items update output path", () => RunSync(ValueLogManagerControlItemsUpdateOutputPath)),
    ("Value log manager run value starts and stops", ValueLogManagerRunValueStartsAndStops),
    ("Value log manager run false returns immediately", ValueLogManagerRunFalseReturnsImmediately),
    ("Value log manager does not publish command items", () => RunSync(ValueLogManagerDoesNotPublishCommandItems)),
    ("CSV logger writes text header without name fallback", CsvLoggerWritesTextHeaderWithoutNameFallback),
    ("Value log CSV header uses current registry metadata", ValueLogCsvHeaderUsesCurrentRegistryMetadata),
    ("Value log CSV source parent samples read child", ValueLogCsvSourceParentSamplesReadChild),
    ("PID controller guards invalid numeric input", () => RunSync(PidControllerGuardsInvalidNumericInput)),
    ("PID controller rejects invalid owned setpoint", () => RunSync(PidControllerRejectsInvalidOwnedSetpoint)),
    ("PID controller owned setpoint write changes output", () => RunSync(PidControllerOwnedSetpointWriteChangesOutput)),
    ("PID controller publishes percent and scaled output", () => RunSync(PidControllerPublishesPercentAndScaledOutput)),
    ("PID controller publishes boolean alert state", () => RunSync(PidControllerPublishesBooleanAlertState)),
    ("PID controller does not bias output to setpoint", () => RunSync(PidControllerDoesNotBiasOutputToSetpoint)),
    ("PID controller clamps scaled output", () => RunSync(PidControllerClampsScaledOutput)),
    ("PID controller rejects invalid CHR parameters", () => RunSync(PidControllerRejectsInvalidChrParameters)),
    ("Process log runtime publishes level input items", () => RunSync(ProcessLogRuntimePublishesLevelInputItems)),
    ("Process log runtime updates log directory", () => RunSync(ProcessLogRuntimeUpdatesLogDirectory)),
    ("Host item broker client receives live items", HostItemBrokerClientReceivesLiveItems),
    ("Host item broker client exposes direct received roots", HostItemBrokerClientExposesDirectReceivedRoots),
    ("Host item broker client receives live items without base topic", HostItemBrokerClientReceivesLiveItemsWithoutBaseTopic),
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
    AssertFalse(HostRegistryPropertyPolicy.CanShowInUserPicker("write"));
    AssertTrue(HostRegistryPropertyPolicy.CanUserWriteProperty("write"));
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

static void HostItemRegistryReadsAndWritesMemoryReference()
{
    var registry = new HostItemRegistry();
    registry.Register("runtime.device.read", new TestHostItem(1));

    AssertTrue(registry.TryRead("runtime.device.read", out var initialValue));
    AssertEqual(1, initialValue);
    AssertTrue(registry.TryWrite("runtime.device.read", 2));
    AssertTrue(registry.TryRead("runtime.device.read", out var writtenValue));
    AssertEqual(2, writtenValue);
}

static void HostItemRegistryRejectsDuplicatePaths()
{
    var registry = new HostItemRegistry();
    registry.Register("runtime.device.read", new TestHostItem(1));

    var failed = false;
    try
    {
        registry.Register("Runtime.Device.Read", new TestHostItem(2));
    }
    catch (ArgumentException)
    {
        failed = true;
    }

    AssertTrue(failed);
}

static void HostItemRegistryUnregisterBlocksReadsAndWrites()
{
    var registry = new HostItemRegistry();
    registry.Register("runtime.device.read", new TestHostItem(1));

    AssertTrue(registry.Remove("runtime.device.read"));
    AssertFalse(registry.TryRead("runtime.device.read", out _));
    AssertFalse(registry.TryWrite("runtime.device.read", 2));
}

static void ValueReferenceResolvesSourcePropertyWithoutMutatingPublicSnapshot()
{
    var registry = new DataRegistry();
    var runtimeRead = ItemExtension.CreateWithPath("runtime.udl_client.client_a.m001.read");
    runtimeRead.Properties["read"].Value = 5d;
    registry.UpsertSnapshot(runtimeRead.Path!, runtimeRead);

    var publicRead = ItemExtension.CreateWithPath("studio.default_layout.client_a.m001.read");
    publicRead.Properties["read"].Value = 0d;
    registry.UpsertSnapshot(publicRead.Path!, publicRead);

    AssertTrue(registry.RegisterValueReference(new DataRegistryValueReference(
        PublicItemPath: publicRead.Path!,
        PublicParameterName: "read",
        SourceItemPath: runtimeRead.Path!,
        SourceParameterName: "read")));

    AssertTrue(registry.TryResolve(publicRead.Path!, out var resolved));
    AssertEqual(5d, Convert.ToDouble(resolved?.Properties["read"].Value ?? -1d));
    AssertTrue(registry.TryGet(publicRead.Path!, out var stored));
    AssertEqual(0d, Convert.ToDouble(stored?.Properties["read"].Value ?? -1d));
}

static void ValueReferenceNotificationPublishesPublicPath()
{
    var registry = new DataRegistry();
    var runtimeRead = ItemExtension.CreateWithPath("runtime.udl_client.client_a.m002.read");
    runtimeRead.Properties["read"].Value = 1d;
    registry.UpsertSnapshot(runtimeRead.Path!, runtimeRead);

    var publicRead = ItemExtension.CreateWithPath("studio.default_layout.client_a.m002.read");
    publicRead.Properties["read"].Value = 0d;
    registry.UpsertSnapshot(publicRead.Path!, publicRead);

    AssertTrue(registry.RegisterValueReference(new DataRegistryValueReference(
        PublicItemPath: publicRead.Path!,
        PublicParameterName: "read",
        SourceItemPath: runtimeRead.Path!,
        SourceParameterName: "read")));

    var changedKey = string.Empty;
    var changedParameter = string.Empty;
    var changedValue = double.NaN;
    registry.ItemChanged += (_, e) =>
    {
        if (!string.Equals(e.Key, publicRead.Path, StringComparison.OrdinalIgnoreCase)
            || e.ChangeKind != DataChangeKind.PropertyUpdated
            || !string.Equals(e.ParameterName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        changedKey = e.Key;
        changedParameter = e.ParameterName ?? string.Empty;
        changedValue = Convert.ToDouble(e.ItemModel.Properties["read"].Value ?? double.NaN);
    };

    runtimeRead.Properties["read"].Value = 9d;

    AssertTrue(registry.NotifyReferencedPropertyChanged(publicRead.Path!, "read"));
    AssertEqual(publicRead.Path, changedKey);
    AssertEqual("read", changedParameter);
    AssertEqual(9d, changedValue);
    AssertTrue(registry.TryGet(publicRead.Path!, out var stored));
    AssertEqual(0d, Convert.ToDouble(stored?.Properties["read"].Value ?? -1d));
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

static void UiFolderChildSourceUpdatePreservesWriteProperty()
{
    var source = ItemExtension.CreateWithPath("runtime.ui_folder_mirror.m300", 0);
    source["read"].Value = 0;
    source["set"].Properties["write"].Value = 0;
    using var context = new UiFolderContext("MirrorTest");
    var attached = context.Attach(source, "m300");
    HostRegistries.Data.UpsertSnapshot(attached.Path!, attached);

    AssertTrue(HostRegistries.Data.TryUpdateUserProperty("studio.mirror_test.m300.set", "write", 42));
    source["read"].Value = 1;

    AssertTrue(HostRegistries.Data.TryResolve("studio.mirror_test.m300.read", out var read));
    AssertEqual(1, read?.Value);
    AssertTrue(HostRegistries.Data.TryResolve("studio.mirror_test.m300.set", out var set));
    AssertEqual(42, set?.Properties["write"].Value);
}

static void UiFolderCoalescesRuntimeReadUpdatesToLatestValue()
{
    const string targetPath = "studio.mirror_test.m301.read";
    HostRegistries.Data.Remove("studio.mirror_test.m301");

    var source = ItemExtension.CreateWithPath("runtime.udl_client.runtime_projection.m301", 0);
    source["read"].Properties["read"].Value = 0d;

    using var context = new UiFolderContext("MirrorTest");
    var attached = context.Attach(source, "m301");
    HostRegistries.Data.UpsertSnapshot(attached.Path!, attached.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

    var publishCount = 0;
    void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (string.Equals(e.Key, targetPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ParameterName, "read", StringComparison.OrdinalIgnoreCase)
            && e.ChangeKind == DataChangeKind.PropertyUpdated)
        {
            Interlocked.Increment(ref publishCount);
        }
    }

    HostRegistries.Data.ItemChanged += OnDataChanged;
    try
    {
        source["read"].Properties["read"].Value = 1d;
        source["read"].Properties["read"].Value = 2d;
        source["read"].Properties["read"].Value = 3d;

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(targetPath, out var resolved)
                && Convert.ToDouble(resolved?.Properties["read"].Value ?? -1d) == 3d,
            TimeSpan.FromSeconds(2)));
        if (Volatile.Read(ref publishCount) >= 3)
        {
            throw new InvalidOperationException($"Expected coalesced publish count < 3 but was {publishCount}.");
        }
    }
    finally
    {
        HostRegistries.Data.ItemChanged -= OnDataChanged;
        HostRegistries.Data.Remove("studio.mirror_test.m301");
    }
}

static void UiFolderInitialPublishPreservesSourceRuntimePaths()
{
    var source = ItemExtension.CreateWithPath("runtime.ui_folder_mirror.m002", 0);
    source["read"].Value = 1;

    using var context = new UiFolderContext("MirrorTest");
    var attached = context.Attach(source, "m002");
    HostRegistries.Data.UpsertSnapshot(attached.Path!, attached.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

    AssertEqual("runtime.ui_folder_mirror.m002", source.Path);
    AssertEqual("runtime.ui_folder_mirror.m002.read", source["read"].Path);
}

static void UiFolderDisposeIsSafeDuringReentrantRegistryRemove()
{
    for (var iteration = 0; iteration < 16; iteration++)
    {
        HostRegistries.Data.Remove("studio.mirror_test.m302");
        HostRegistries.Data.Remove("studio.mirror_test.m303");

        var sourceA = ItemExtension.CreateWithPath($"runtime.udl_client.dispose_test_{iteration}.m302", 0);
        var sourceB = ItemExtension.CreateWithPath($"runtime.udl_client.dispose_test_{iteration}.m303", 0);
        using var context = new UiFolderContext("MirrorTest");
        var attachedA = context.Attach(sourceA, "m302");
        var attachedB = context.Attach(sourceB, "m303");
        HostRegistries.Data.UpsertSnapshot(attachedA.Path!, attachedA.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        HostRegistries.Data.UpsertSnapshot(attachedB.Path!, attachedB.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        using var start = new ManualResetEventSlim(false);
        var disposeA = Task.Run(() =>
        {
            start.Wait();
            context.Dispose();
        });
        var disposeB = Task.Run(() =>
        {
            start.Wait();
            context.Dispose();
        });

        start.Set();
        Task.WaitAll(disposeA, disposeB);

        HostRegistries.Data.Remove("studio.mirror_test.m302");
        HostRegistries.Data.Remove("studio.mirror_test.m303");
    }
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

static void HostUdlClientQueuesNonStateWriteback()
{
    var client = new HostUdlClient("test", "127.0.0.1", 9001);
    var module = CreateHostUdlModule(client, moduleId: 1u);
    module["set"].Properties["read"].Value = 10f;
    module["set"].Properties["write"].Value = 42f;

    QueueHostUdlWrite(client, moduleId: 1u, module, module["set"]);

    AssertEqual(1, GetHostUdlPendingWriteCount(client));

    ProcessHostUdlFrame(client, moduleId: 1u, function: 3, value: 42f);

    AssertEqual(1, GetHostUdlPendingWriteCount(client));
}

static void HostUdlClientAcknowledgesMatchingWritebackChannel()
{
    var client = new HostUdlClient("test", "127.0.0.1", 9001);
    var module = CreateHostUdlModule(client, moduleId: 1u);
    module["set"].Properties["read"].Value = 10f;
    module["set"].Properties["write"].Value = 42f;

    QueueHostUdlWrite(client, moduleId: 1u, module, module["set"]);
    ProcessHostUdlFrame(client, moduleId: 1u, function: 4, value: 42f);

    AssertEqual(0, GetHostUdlPendingWriteCount(client));
    AssertEqual("ok", module.Properties["SendStatus"].Value);
    AssertEqual(42f, module["set"].Properties["read"].Value);
}

static ItemModel CreateHostUdlModule(HostUdlClient client, uint moduleId)
{
    var createModuleMethod = typeof(HostUdlClient).GetMethod("GetOrCreateModule", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(createModuleMethod is not null);
    return (ItemModel)createModuleMethod!.Invoke(client, [moduleId])!;
}

static void QueueHostUdlWrite(HostUdlClient client, uint moduleId, ItemModel module, ItemModel item)
{
    var processRequestWriteMethod = typeof(HostUdlClient).GetMethod("ProcessRequestWrite", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(processRequestWriteMethod is not null);
    processRequestWriteMethod!.Invoke(client, [moduleId, module, item]);
}

static void ProcessHostUdlFrame(HostUdlClient client, uint moduleId, int function, float value)
{
    var processFrameMethod = typeof(HostUdlClient).GetMethod("ProcessFrame", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(processFrameMethod is not null);

    var data = new byte[8];
    Array.Copy(BitConverter.GetBytes(value), sourceIndex: 0, data, destinationIndex: 0, length: 4);
    data[6] = (byte)function;
    data[7] = (byte)(moduleId & 0x0F);
    var id = 0x480u | ((moduleId >> 4) & 0x7Fu);

    processFrameMethod!.Invoke(client, [id, (byte)data.Length, data]);
}

static int GetHostUdlPendingWriteCount(HostUdlClient client)
{
    var pendingWritesField = typeof(HostUdlClient).GetField("_pendingWrites", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(pendingWritesField is not null);
    var pendingWrites = pendingWritesField!.GetValue(client);
    var countProperty = pendingWrites?.GetType().GetProperty("Count");
    AssertTrue(countProperty is not null);
    return (int)countProperty!.GetValue(pendingWrites)!;
}

static async Task PythonClientRegistryPathsNormalizeToSnakeCase()
{
    await using var client = new PythonClient(new PythonClientOptions
    {
        Name = "RawClient42",
        RegistryRootPath = "studio.Main.Applications.Python.RawTest"
    });

    var method = typeof(PythonClient).GetMethod("ResolveRegistryValuePath", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("ResolveRegistryValuePath was not found.");
    }

    var normalizedPath = method.Invoke(client, ["RawValueA", null]);
    AssertEqual("studio.main.applications.python.raw_test.raw_value_a", normalizedPath);

    var explicitPath = method.Invoke(client, ["IgnoredName", "studio/Main/Applications/Python/RawTest/Value-B"]);
    AssertEqual("studio.main.applications.python.raw_test.value_b", explicitPath);

    await using var fallbackClient = new PythonClient(new PythonClientOptions
    {
        Name = "MixedClient"
    });

    var fallbackPath = method.Invoke(fallbackClient, ["ValueB", null]);
    AssertEqual("python_clients.mixed_client.value_b", fallbackPath);
}

static void EnhancedSignalRuntimePublishesSnakeCaseWritePaths()
{
    var definition = new ExtendedSignalDefinition
    {
        Name = "enhanced_signal_test",
        SourcePath = "runtime.enhanced_signal_source.read",
        KalmanEnabled = true,
        KalmanDynamicQEnabled = true,
        DynamicFilter = new ExtendedSignalDynamicFilterDefinition
        {
            Enabled = true
        },
        PeakFilter = new ExtendedSignalPeakFilterDefinition
        {
            Enabled = true
        },
        Statistics = new ExtendedSignalStatisticsDefinition
        {
            Enabled = true,
            PublishMin = true,
            PublishMax = true,
            PublishAverage = true,
            PublishStdDev = true,
            PublishIntegral = true
        },
        Adjustment = new ExtendedSignalAdjustmentDefinition
        {
            Enabled = true,
            MappingMode = ExtendedSignalAdjustmentMode.Spline
        }
    };

    using var runtime = new EnhancedSignalRuntime("enhanced_signal_runtime_test", definition);
    var rootPath = runtime.RegistryPath;

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.kalman.write", out _));
    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.adjustment.write", out _));
    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.adjustment.mapping_mode", out _));
    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.statistics.reset", out _));
    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.read", out var read));
    AssertTrue(read!.Properties.Has("read"));
    AssertTrue(read.Properties.Has("write"));
    AssertFalse(read.Has("write"));

    AssertTrue(HostRegistries.Data.TryGetMetadata(rootPath, out var metadata));
    AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Display));
    AssertTrue(HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display).Contains(rootPath, StringComparer.OrdinalIgnoreCase));

    AssertTrue(HostRegistries.Data.TryGet(rootPath, out var root));
    AssertFalse(EnumerateItemPaths(root!).Any(static path => path.Split('.').Any(static segment => !IsSnakeCaseSegment(segment))));
}

static void EnhancedSignalRuntimePublishesTypeMetadata()
{
    var definition = new ExtendedSignalDefinition
    {
        Name = "enhanced_signal_types",
        SourcePath = "runtime.enhanced_signal_source_types.read",
        KalmanEnabled = true,
        KalmanDynamicQEnabled = true,
        DynamicFilter = new ExtendedSignalDynamicFilterDefinition
        {
            Enabled = true
        },
        Statistics = new ExtendedSignalStatisticsDefinition
        {
            Enabled = true,
            PublishMin = true,
            PublishAverage = true
        },
        Adjustment = new ExtendedSignalAdjustmentDefinition
        {
            Enabled = true,
            SupportsInverseMapping = true
        }
    };

    using var runtime = new EnhancedSignalRuntime("enhanced_signal_runtime_test", definition);
    var rootPath = runtime.RegistryPath;

    AssertTrue(HostRegistries.Data.TryResolve(rootPath, out var root));
    AssertEqual("float", root?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.read", out var read));
    AssertEqual("float", read?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.state", out var state));
    AssertEqual("string", state?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.command", out var command));
    AssertEqual("bool", command?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.dynamic.active", out var dynamicActive));
    AssertEqual("bool", dynamicActive?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.dynamic.normalization_mode", out var dynamicNormalizationMode));
    AssertEqual("string", dynamicNormalizationMode?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.dynamic.remaining_hold_ms", out var dynamicRemainingHold));
    AssertEqual("int", dynamicRemainingHold?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.kalman.dynamic_trigger_active", out var kalmanDynamicTrigger));
    AssertEqual("bool", kalmanDynamicTrigger?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.kalman.dynamic_normalization_mode", out var kalmanDynamicNormalization));
    AssertEqual("string", kalmanDynamicNormalization?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.adjustment.offset", out var adjustmentOffset));
    AssertEqual("float", adjustmentOffset?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.adjustment.enabled", out var adjustmentEnabled));
    AssertEqual("bool", adjustmentEnabled?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.adjustment.mapping_mode", out var adjustmentMappingMode));
    AssertEqual("string", adjustmentMappingMode?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.statistics.average", out var statisticsAverage));
    AssertEqual("float", statisticsAverage?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.statistics.min.timestamp", out var statisticsTimestamp));
    AssertEqual("int", statisticsTimestamp?.Properties["type"].Value);

    AssertTrue(HostRegistries.Data.TryResolve($"{rootPath}.statistics.reset", out var statisticsReset));
    AssertEqual("bool", statisticsReset?.Properties["type"].Value);
}

static void EnhancedSignalSetWriteForwardsInverseAdjustment()
{
    const string sourcePath = "runtime.enhanced_signal_inverse_source.m001";
    var source = CreateFlatSourceModule("m001", initialValue: 10d);
    HostRegistries.Data.UpsertSnapshot(sourcePath, source, pruneMissingMembers: true);

    var definition = new ExtendedSignalDefinition
    {
        Name = "inverse_adjustment_test",
        SourcePath = $"{sourcePath}.read",
        ForwardChildWritesToSource = true,
        Adjustment = new ExtendedSignalAdjustmentDefinition
        {
            Enabled = true,
            Gain = 2d,
            Offset = 5d,
            SupportsInverseMapping = true
        }
    };

    using var runtime = new EnhancedSignalRuntime("enhanced_signal_runtime_test", definition);

    AssertTrue(HostRegistries.Data.UpdateProperty($"{runtime.RegistryPath}.set", "write", 25d));
    AssertTrue(HostRegistries.Data.TryResolve($"{sourcePath}.set", out var sourceSet));
    AssertEqual(10d, sourceSet?.Properties["write"].Value);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.set", out var enhancedSet));
    AssertEqual(25d, enhancedSet?.Properties["read"].Value);
    AssertEqual(25d, enhancedSet?.Properties["write"].Value);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 45d));
    AssertEqual(20d, sourceSet?.Properties["write"].Value);
    AssertEqual(45d, enhancedSet?.Properties["read"].Value);
    AssertEqual(45d, enhancedSet?.Properties["write"].Value);

    const string fallbackSourcePath = "runtime.enhanced_signal_inverse_source.m003";
    var fallbackSource = CreateFlatSourceModule("m003", initialValue: 10d, setHasWriteChannel: false);
    HostRegistries.Data.UpsertSnapshot(fallbackSourcePath, fallbackSource, pruneMissingMembers: true);

    var fallbackDefinition = new ExtendedSignalDefinition
    {
        Name = "inverse_adjustment_fallback_test",
        SourcePath = $"{fallbackSourcePath}.read",
        ForwardChildWritesToSource = true,
        Adjustment = new ExtendedSignalAdjustmentDefinition
        {
            Enabled = true,
            Gain = 2d,
            Offset = 5d,
            SupportsInverseMapping = true
        }
    };

    using var fallbackRuntime = new EnhancedSignalRuntime("enhanced_signal_runtime_test", fallbackDefinition);

    AssertTrue(HostRegistries.Data.UpdateValue($"{fallbackRuntime.RegistryPath}.set", 25d));
    AssertTrue(HostRegistries.Data.TryResolve($"{fallbackSourcePath}.set", out var fallbackSourceSet));
    AssertFalse(fallbackSourceSet!.Properties.Has("write"));
    AssertEqual(10d, fallbackSourceSet.Value);
}

static void EnhancedSignalPrefersChildReadOverSourceContainerText()
{
    const string sourcePath = "runtime.enhanced_signal_inverse_source.m002";
    const double expectedValue = -0.485d;
    var source = CreateFlatSourceModule("m002", expectedValue);
    source.Value = "Noise Jitter -0,485";
    HostRegistries.Data.UpsertSnapshot(sourcePath, source, pruneMissingMembers: true);

    var definition = new ExtendedSignalDefinition
    {
        Name = "container_text_test",
        SourcePath = sourcePath
    };

    using var runtime = new EnhancedSignalRuntime("enhanced_signal_runtime_test", definition);

    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.raw", out var raw));
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.read", out var read));
    AssertEqual(expectedValue, raw?.Value);
    AssertEqual(expectedValue, raw?.Properties["read"].Value);
    AssertEqual(expectedValue, read?.Value);
    AssertEqual(expectedValue, read?.Properties["read"].Value);
}

static void EnhancedSignalIgnoresNonnumericSourceTextForNumericConversion()
{
    var method = typeof(EnhancedSignalRuntime).GetMethod("ToNullableDouble", BindingFlags.Static | BindingFlags.NonPublic);
    AssertTrue(method is not null);

    var value = method!.Invoke(null, ["Noise Jitter -0,348"]);
    AssertEqual(null, value);
}

static void EnhancedSignalManagerKeepsRuntimeWhenRuntimeDefinitionMutates()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"enhanced_signal_manager_{suffix}";
    var sourcePath = $"runtime.enhanced_signal_manager.{suffix}.source";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);

    var definition = new ExtendedSignalDefinition
    {
        Name = "managed_signal",
        SourcePath = sourcePath,
        Adjustment = new ExtendedSignalAdjustmentDefinition
        {
            Gain = 1d
        }
    };
    var rawDefinitions = ExtendedSignalDefinitionJsonCodec.SerializeDefinitions([definition]);
    var runtimes = EnhancedSignalRuntimeManager.SyncDefinitions(folderName, rawDefinitions);
    var runtime = runtimes.Single();
    var registryPath = runtime.RegistryPath;

    runtime.Definition.Adjustment.Gain = 2d;
    var syncedRuntimes = EnhancedSignalRuntimeManager.SyncDefinitions(folderName, rawDefinitions);

    AssertEqual(1, syncedRuntimes.Count);
    AssertTrue(ReferenceEquals(runtime, syncedRuntimes[0]));
    AssertTrue(EnhancedSignalRuntimeManager.TryGetRuntime(registryPath, out var found));
    AssertTrue(ReferenceEquals(runtime, found));

    EnhancedSignalRuntimeManager.ReleaseFolder(folderName);
}

static void PidControllerRuntimePublishesStablePaths()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_a", sourcePath, outputPath));

    AssertEqual($"studio.{folderName}.controller.loop_a", runtime.RegistryPath);
    AssertTrue(HostRegistries.Data.TryResolve(runtime.RegistryPath, out var root));
    AssertEqual(runtime.RegistryPath, root?.Path);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.run", out var runItem));
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.set", out var setItem));
    AssertFalse(runItem?.Properties.Has("writable") ?? true);
    AssertFalse(runItem?.Properties.Has("write") ?? true);
    AssertFalse(runItem?.Properties.Has("write_path") ?? true);
    AssertFalse(runItem?.Properties.Has("write_mode") ?? true);
    AssertFalse(setItem?.Properties.Has("writable") ?? true);
    AssertFalse(setItem?.Properties.Has("write") ?? true);
    AssertFalse(setItem?.Properties.Has("write_path") ?? true);
    AssertFalse(setItem?.Properties.Has("write_mode") ?? true);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.parameters.kr", out var krItem));
    AssertTrue(krItem?.Value is double);
}

static void PidControllerStoppedRuntimeStaysQuiet()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_quiet_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_quiet_test.{suffix}.source";
    var outputPath = $"runtime.pid_quiet_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_a", sourcePath, outputPath));
    var changedCount = 0;
    HostRegistries.Data.ItemChanged += OnItemChanged;
    try
    {
        Thread.Sleep(350);
    }
    finally
    {
        HostRegistries.Data.ItemChanged -= OnItemChanged;
    }

    AssertEqual(0, changedCount);

    void OnItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.Key.StartsWith(runtime.RegistryPath, StringComparison.OrdinalIgnoreCase))
        {
            changedCount++;
        }
    }
}

static void PidControllerStoppedReadMirrorsAvailableSource()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_stop_read_{suffix}";
    var sourcePath = $"runtime.pid_stop_read.{suffix}.source";
    var outputPath = $"runtime.pid_stop_read.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 42.5d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_stop_read", sourcePath, outputPath));

    AssertFalse(runtime.IsRunning);

    if (!runtime.CurrentSourceValue.HasValue || runtime.CurrentSourceValue.Value != 42.5d)
    {
        throw new InvalidOperationException($"Stopped PID CurrentSourceValue should be 42.5 but was '{runtime.CurrentSourceValue?.ToString() ?? "<null>"}'.");
    }

    if (!TryResolveNumericItemValue($"{runtime.RegistryPath}.read", out var publishedRead) || publishedRead != 42.5d)
    {
        throw new InvalidOperationException($"Stopped PID controller.read should mirror source 42.5 but registry value was '{publishedRead}'.");
    }
}

static void PidControllerStoppedReadUpdatesOnSourceChange()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_stop_update_{suffix}";
    var sourcePath = $"runtime.pid_stop_update.{suffix}.source";
    var outputPath = $"runtime.pid_stop_update.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_stop_update", sourcePath, outputPath));
    AssertFalse(runtime.IsRunning);

    HostRegistries.Data.UpdateValue(sourcePath, 75d);

    if (!runtime.CurrentSourceValue.HasValue || runtime.CurrentSourceValue.Value != 75d)
    {
        throw new InvalidOperationException($"Stopped PID CurrentSourceValue should update to 75 after source change but was '{runtime.CurrentSourceValue?.ToString() ?? "<null>"}'.");
    }

    if (!TryResolveNumericItemValue($"{runtime.RegistryPath}.read", out var publishedRead) || publishedRead != 75d)
    {
        throw new InvalidOperationException($"Stopped PID controller.read should update to 75 after source change but registry value was '{publishedRead}'.");
    }

    AssertEqual("Stopped", runtime.CurrentStateValue);
}

static void RegistryDirectUpdateWritesWritableChildChannel()
{
    var path = $"studio.pid_registry_test_{Guid.NewGuid():N}.controller.loop";
    var snapshot = ItemExtension.CreateWithPath(path, false);
    snapshot["run"].Value = false;
    snapshot["run"].Properties["writable"].Value = true;
    snapshot["run"].Properties["write"].Value = false;
    HostRegistries.Data.UpsertSnapshot(path, snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

    AssertTrue(HostRegistries.Data.UpdateValue($"{path}.run", true));
    AssertTrue(HostRegistries.Data.TryResolve($"{path}.run", out var runItem));
    AssertEqual(true, runItem?.Properties["write"].Value);
}

static void PidControllerRunValueStartsAndStops()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_b", sourcePath, outputPath));
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 90d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    AssertPidRunState(runtime, expectedValue: true, context: "after direct run update");
    InvokePidEvaluation(runtime);
    if (!TryResolveNumericItemValue(outputPath, out var outputValue))
    {
        throw new InvalidOperationException($"PID controller did not write a numeric output. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}', RuntimeOutput='{runtime.CurrentOutputValue}'.");
    }

    if (outputValue <= 0.0)
    {
        throw new InvalidOperationException($"PID controller output should be positive after start, actual '{outputValue}'. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}'. {DescribePidRunItem(runtime)}");
    }

    AssertEqual("Running", runtime.CurrentStateValue);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", false));
    InvokePidEvaluation(runtime);
    AssertEqual("Stopped", runtime.CurrentStateValue);
    AssertFalse(runtime.IsRunning);
}

static void PidControllerRunWritePropertyStartsAndStops()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(
        folderName: folderName,
        definition: CreatePidDefinition(name: "loop_e", sourcePath: sourcePath, outputPath: outputPath));
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.run", out var initialRunItem));
    AssertFalse(initialRunItem?.Properties.Has("write") ?? true);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.set", out var initialSetItem));
    AssertFalse(initialSetItem?.Properties.Has("write") ?? true);

    initialRunItem!.Properties["write"].Value = false;
    initialSetItem!.Properties["write"].Value = 0d;

    AssertTrue(HostRegistries.Data.TryUpdateUserProperty($"{runtime.RegistryPath}.set", "write", 90d));
    AssertTrue(HostRegistries.Data.TryUpdateUserProperty($"{runtime.RegistryPath}.run", "write", true));
    AssertPidRunState(runtime, expectedValue: true, context: "after run.write update");
    InvokePidEvaluation(runtime);
    if (!TryResolveNumericItemValue(outputPath, out var outputValue))
    {
        throw new InvalidOperationException($"PID controller did not write a numeric output through run.write. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}', RuntimeOutput='{runtime.CurrentOutputValue}'.");
    }

    if (outputValue <= 0.0)
    {
        throw new InvalidOperationException($"PID controller run.write output should be positive after start, actual '{outputValue}'. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}'. {DescribePidRunItem(runtime)}");
    }

    AssertEqual("Running", runtime.CurrentStateValue);

    AssertTrue(HostRegistries.Data.TryUpdateUserProperty($"{runtime.RegistryPath}.run", "write", false));
    InvokePidEvaluation(runtime);
    AssertEqual("Stopped", runtime.CurrentStateValue);
    AssertFalse(runtime.IsRunning);
}

static void ValueLogManagerPublishesControlItems()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_control_{suffix}";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "initial.csv");

    try
    {
        var status = LogManager.SyncDefinitions(folderName, [CreateValueLogDefinition("csv_log", outputPath)]).Single();

        AssertTrue(HostRegistries.Data.TryResolve($"{status.RuntimePath}.run", out var runItem));
        AssertEqual(false, runItem?.Value);
        AssertTrue(HostRegistries.Data.TryResolve($"{status.RuntimePath}.output_directory", out var outputDirectoryItem));
        AssertEqual(Path.GetDirectoryName(outputPath) ?? string.Empty, outputDirectoryItem?.Value?.ToString());
        AssertTrue(HostRegistries.Data.TryResolve($"{status.RuntimePath}.filename", out var filenameItem));
        AssertEqual("initial.csv", filenameItem?.Value?.ToString());
    }
    finally
    {
        LogManager.ReleaseFolder(folderName);
    }
}

static void ValueLogManagerControlItemsUpdateOutputPath()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_output_{suffix}";
    var initialDirectory = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "initial");
    var updatedDirectory = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "updated");
    var initialOutputPath = Path.Combine(initialDirectory, "initial.csv");
    var updatedOutputPath = Path.Combine(updatedDirectory, "changed.csv");

    try
    {
        var status = LogManager.SyncDefinitions(
            folderName,
            [CreateValueLogDefinition("csv_log", initialOutputPath)]).Single();

        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.output_directory", updatedDirectory));
        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.filename", "changed.csv"));

        AssertTrue(LogManager.TryGetStatus(folderName, "csv_log", out var updatedStatus));
        AssertEqual(updatedOutputPath, updatedStatus.Definition.OutputPath);
        AssertTrue(HostRegistries.Data.TryResolve(status.RuntimePath, out var outputPathItem));
        AssertEqual(updatedOutputPath, outputPathItem?.Properties["output_path"].Value?.ToString());
    }
    finally
    {
        LogManager.ReleaseFolder(folderName);
    }
}

static async Task ValueLogManagerRunValueStartsAndStops()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_run_{suffix}";
    var sourcePath = $"runtime.value_log_test.{suffix}.source";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "run.csv");
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);

    try
    {
        var status = LogManager.SyncDefinitions(
            folderName,
            [CreateValueLogDefinition("csv_log", outputPath, sourcePath)]).Single();

        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.run", true));
        await WaitForConditionAsync(
            () => LogManager.TryGetStatus(folderName, "csv_log", out var currentStatus) && currentStatus.IsRunning,
            "ValueLog manager did not start after run=true.").ConfigureAwait(false);

        AssertTrue(HostRegistries.Data.TryResolve($"{status.RuntimePath}.run", out var runningItem));
        AssertEqual(true, runningItem?.Value);

        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.run", false));
        await WaitForConditionAsync(
            () => LogManager.TryGetStatus(folderName, "csv_log", out var currentStatus) && !currentStatus.IsRunning,
            "ValueLog manager did not stop after run=false.").ConfigureAwait(false);

        AssertTrue(HostRegistries.Data.TryResolve($"{status.RuntimePath}.run", out var stoppedItem));
        AssertEqual(false, stoppedItem?.Value);
    }
    finally
    {
        LogManager.ReleaseFolder(folderName);
    }
}

static async Task ValueLogManagerRunFalseReturnsImmediately()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_stop_queue_{suffix}";
    var sourcePath = $"runtime.value_log_test.{suffix}.source";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "run_false.csv");
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);

    try
    {
        var status = LogManager.SyncDefinitions(
            folderName,
            [CreateValueLogDefinition("csv_log", outputPath, sourcePath)]).Single();

        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.run", true));
        await WaitForConditionAsync(
            () => LogManager.TryGetStatus(folderName, "csv_log", out var runningStatus) && runningStatus.IsRunning,
            "ValueLog manager did not start after queued run=true.").ConfigureAwait(false);

        AssertTrue(LogManager.TryGetStatus(folderName, "csv_log", out var runningStatus) && runningStatus.IsRunning);

        var startedAt = DateTime.UtcNow;
        AssertTrue(HostRegistries.Data.UpdateValue($"{status.RuntimePath}.run", false));
        var elapsed = DateTime.UtcNow - startedAt;

        if (elapsed > TimeSpan.FromMilliseconds(100))
        {
            throw new InvalidOperationException($"ValueLog run=false update took {elapsed.TotalMilliseconds:0.0} ms and appears to execute stop work synchronously.");
        }

        await WaitForConditionAsync(
            () => LogManager.TryGetStatus(folderName, "csv_log", out var currentStatus) && !currentStatus.IsRunning,
            "ValueLog manager did not stop after queued run=false.").ConfigureAwait(false);
    }
    finally
    {
        LogManager.ReleaseFolder(folderName);
    }
}

static void ValueLogManagerDoesNotPublishCommandItems()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_commands_{suffix}";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "initial.csv");

    try
    {
        var status = LogManager.SyncDefinitions(
            folderName,
            [CreateValueLogDefinition("csv_log", outputPath)]).Single();

        AssertFalse(HostRegistries.Data.TryResolve($"{status.RuntimePath}.commands.start", out _));
        AssertFalse(HostRegistries.Data.TryResolve($"{status.RuntimePath}.commands.stop", out _));
    }
    finally
    {
        LogManager.ReleaseFolder(folderName);
    }
}

static async Task CsvLoggerWritesTextHeaderWithoutNameFallback()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "headers.csv");
    var logger = new CsvLogger($"csv_header_{suffix}");
    var firstValue = 10d;
    var secondValue = 20d;

    logger.Add(
        name: "signal_m001",
        unit: "ppm",
        format: string.Empty,
        value: () => firstValue,
        caption: string.Empty);
    logger.Add(
        name: "signal_m006",
        unit: string.Empty,
        format: string.Empty,
        value: () => secondValue,
        caption: "Display text");

    try
    {
        logger.Start(outputPath, interval: 10);
        await WaitForConditionAsync(
            () => File.Exists(outputPath),
            "CSV logger did not write header lines.").ConfigureAwait(false);
    }
    finally
    {
        await logger.Stop().ConfigureAwait(false);
    }

    var lines = File.ReadAllLines(outputPath);
    AssertEqual("\"datetime\";\"timeRel\";\"signal_m001\";\"signal_m006\"", lines[0]);
    AssertEqual("\"DateTime\";\"Time relativ\";\"\";\"Display text\"", lines[1]);
    AssertEqual("\"\";\"s\";\"ppm\";\"\"", lines[2]);
}
static async Task ValueLogCsvHeaderUsesCurrentRegistryMetadata()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_header_{suffix}";
    var sourcePath = $"runtime.value_log_test.{suffix}.signal_m001.read";
    var metadataPath = $"runtime.value_log_test.{suffix}.signal_m001";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "runtime_headers.csv");
    string runtimePath = string.Empty;
    var sourceItem = ItemExtension.CreateWithPath(metadataPath, 0d);
    sourceItem.Properties["text"].Value = "DummyTest";
    sourceItem.Properties["unit"].Value = "ppm";
    sourceItem.Properties["format"].Value = "0.0";
    sourceItem["read"].Value = 10d;
    sourceItem["read"].Properties["read"].Value = 10d;
    sourceItem["read"].Properties["text"].Value = "m001 Read";
    HostRegistries.Data.UpsertSnapshot(metadataPath, sourceItem, pruneMissingMembers: true);

    try
    {
        runtimePath = LogManager.SyncDefinitions(
            folderName,
            [
                new ValueLogDefinition
                {
                    Id = "csv_log",
                    Text = "csv_log",
                    Kind = ValueLogKind.Csv,
                    Enabled = true,
                    OutputPath = outputPath,
                    IntervalMs = 10,
                    Sources =
                    [
                        new ValueLogSourceDefinition
                        {
                            TargetPath = sourcePath
                        }
                    ]
                }
            ]).Single().RuntimePath;

        AssertTrue(HostRegistries.Data.UpdateValue($"{runtimePath}.run", true));
        await WaitForConditionAsync(
            () => File.Exists(outputPath),
            "ValueLog CSV logger did not write header lines.").ConfigureAwait(false);
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(runtimePath) && HostRegistries.Data.UpdateValue($"{runtimePath}.run", false))
        {
            await WaitForConditionAsync(
                () => LogManager.TryGetStatus(folderName, "csv_log", out var currentStatus) && !currentStatus.IsRunning,
                "ValueLog CSV logger did not stop after queued run=false.").ConfigureAwait(false);
        }

        LogManager.ReleaseFolder(folderName);
    }

    var lines = File.ReadAllLines(outputPath);
    AssertEqual("\"datetime\";\"timeRel\";\"signal_m001\"", lines[0]);
    AssertEqual("\"DateTime\";\"Time relativ\";\"DummyTest\"", lines[1]);
    AssertEqual("\"\";\"s\";\"ppm\"", lines[2]);
}
static async Task ValueLogCsvSourceParentSamplesReadChild()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"value_log_parent_source_{suffix}";
    var metadataPath = $"runtime.value_log_test.{suffix}.signal_m001";
    var outputPath = Path.Combine(Path.GetTempPath(), "hornet_studio_value_log_tests", suffix, "parent_source.csv");
    string runtimePath = string.Empty;
    var sourceItem = ItemExtension.CreateWithPath(metadataPath, 0d);
    string[] lines = [];
    sourceItem.Properties["text"].Value = "DummyTest";
    sourceItem.Properties["unit"].Value = "ppm";
    sourceItem["read"].Value = 42d;
    sourceItem["read"].Properties["read"].Value = 42d;
    HostRegistries.Data.UpsertSnapshot(metadataPath, sourceItem, pruneMissingMembers: true);

    try
    {
        runtimePath = LogManager.SyncDefinitions(
            folderName,
            [
                new ValueLogDefinition
                {
                    Id = "csv_log",
                    Text = "csv_log",
                    Kind = ValueLogKind.Csv,
                    Enabled = true,
                    OutputPath = outputPath,
                    IntervalMs = 10,
                    Sources =
                    [
                        new ValueLogSourceDefinition
                        {
                            TargetPath = metadataPath
                        }
                    ]
                }
            ]).Single().RuntimePath;

        AssertTrue(HostRegistries.Data.UpdateValue($"{runtimePath}.run", true));
        await WaitForConditionAsync(
            () => File.Exists(outputPath) && new FileInfo(outputPath).Length > 0,
            "ValueLog CSV logger did not write sampled parent source values.").ConfigureAwait(false);
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(runtimePath) && HostRegistries.Data.UpdateValue($"{runtimePath}.run", false))
        {
            await WaitForConditionAsync(
                () => LogManager.TryGetStatus(folderName, "csv_log", out var currentStatus) && !currentStatus.IsRunning,
                "ValueLog CSV logger did not stop after queued run=false.").ConfigureAwait(false);
        }

        LogManager.ReleaseFolder(folderName);
    }

    await WaitForConditionAsync(
        () => TryReadAllLines(outputPath, out lines) && lines.Length > 3,
        "ValueLog CSV logger file was not readable after stop.").ConfigureAwait(false);
    AssertEqual("\"datetime\";\"timeRel\";\"signal_m001\"", lines[0]);
    AssertTrue(lines.Skip(3).Any(static line => line.EndsWith(";42", StringComparison.Ordinal)));
}

static void PidControllerGuardsInvalidNumericInput()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, "invalid"), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_c", sourcePath, outputPath));
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 30d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    AssertPidRunState(runtime, expectedValue: true, context: "after invalid-input run update");
    InvokePidEvaluation(runtime);
    AssertTrue(runtime.CurrentAlertValue);
    AssertEqual("Waiting for source: Source value must be numeric and readable.", runtime.CurrentStateValue);
}

static void PidControllerRejectsInvalidOwnedSetpoint()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 20d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_owned_invalid", sourcePath, outputPath));
    StopPidTimer(runtime);

    var ownedSetpointField = typeof(PidControllerRuntime).GetField("_ownedSetpointRequest", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(ownedSetpointField is not null);
    ownedSetpointField!.SetValue(runtime, "invalid");
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);

    AssertTrue(runtime.CurrentAlertValue);
    AssertEqual("Waiting for setpoint: Setpoint value must be numeric.", runtime.CurrentStateValue);
}

static void PidControllerOwnedSetpointWriteChangesOutput()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    var definition = CreatePidDefinition("loop_owned_set", sourcePath, outputPath);
    definition.Pid.Ks = 100d;
    definition.Pid.Tg = 1d;

    using var runtime = new PidControllerRuntime(folderName, definition);
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 20d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);
    var lowOutput = runtime.CurrentOutputValue;

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 90d));
    InvokePidEvaluation(runtime);
    var highOutput = runtime.CurrentOutputValue;

    if (!lowOutput.HasValue || !highOutput.HasValue)
    {
        throw new InvalidOperationException($"PID controller did not calculate owned-setpoint output values. Low='{lowOutput}', High='{highOutput}', State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}'.");
    }

    if (highOutput.Value <= lowOutput.Value)
    {
        throw new InvalidOperationException($"PID controller output should increase after owned setpoint write. Low='{lowOutput.Value}', High='{highOutput.Value}', State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}'.");
    }
}

static void PidControllerPublishesPercentAndScaledOutput()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_percent_scaled_{suffix}";
    var sourcePath = $"runtime.pid_percent_scaled.{suffix}.source";
    var outputPath = $"runtime.pid_percent_scaled.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_percent_scaled", sourcePath, outputPath));
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 90d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);

    AssertFalse(runtime.CurrentAlertValue);
    AssertEqual("Running", runtime.CurrentStateValue);
    var currentScaledOutput = runtime.CurrentOutputValue;
    AssertTrue(currentScaledOutput.HasValue);
    AssertTrue(TryResolveNumericItemValue(outputPath, out var writtenOutput));
    AssertTrue(TryResolveNumericItemValue($"{runtime.RegistryPath}.out", out var publishedPercent));
    AssertTrue(TryResolveNumericItemValue($"{runtime.RegistryPath}.out.scaled", out var publishedScaledOutput));
    AssertEqual(currentScaledOutput.GetValueOrDefault(), writtenOutput);
    AssertEqual(writtenOutput, publishedScaledOutput);
    AssertTrue(publishedPercent > 0d);
    AssertTrue(publishedPercent <= 100d);
}

static void PidControllerPublishesBooleanAlertState()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_alert_bool_{suffix}";
    var sourcePath = $"runtime.pid_alert_bool.{suffix}.source";
    var outputPath = $"runtime.pid_alert_bool.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, "invalid"), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    using var runtime = new PidControllerRuntime(folderName, CreatePidDefinition("loop_alert_bool", sourcePath, outputPath));
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 30d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);

    AssertTrue(runtime.CurrentAlertValue);
    AssertEqual("Waiting for source: Source value must be numeric and readable.", runtime.CurrentStateValue);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.alert", out var alertItem));
    AssertEqual(true, alertItem?.Value);

    AssertTrue(HostRegistries.Data.UpdateValue(sourcePath, 15d));
    InvokePidEvaluation(runtime);

    AssertFalse(runtime.CurrentAlertValue);
    AssertEqual("Running", runtime.CurrentStateValue);
    AssertTrue(HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.alert", out alertItem));
    AssertEqual(false, alertItem?.Value);
}

static void PidControllerDoesNotBiasOutputToSetpoint()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 50d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    var definition = CreatePidDefinition("loop_no_bias", sourcePath, outputPath);
    definition.Pid.Ks = 100d;
    definition.Pid.Tg = 1d;

    using var runtime = new PidControllerRuntime(folderName, definition);
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 50d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);

    if (!TryResolveNumericItemValue(outputPath, out var outputValue))
    {
        throw new InvalidOperationException($"PID controller did not write a numeric no-bias output. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}', RuntimeOutput='{runtime.CurrentOutputValue}'.");
    }

    if (Math.Abs(outputValue) >= 0.000001)
    {
        throw new InvalidOperationException($"PID controller should not bias output to the setpoint when error is zero. Actual output '{outputValue}'. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}'.");
    }
}

static void PidControllerClampsScaledOutput()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 0d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    var definition = CreatePidDefinition("loop_d", sourcePath, outputPath);
    definition.Pid.SetMin = 0d;
    definition.Pid.SetMax = 100d;
    definition.Pid.OutMin = -10d;
    definition.Pid.OutMax = 10d;

    using var runtime = new PidControllerRuntime(folderName, definition);
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 200d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    AssertPidRunState(runtime, expectedValue: true, context: "after clamp run update");
    InvokePidEvaluation(runtime);
    if (!TryResolveNumericItemValue(outputPath, out var outputValue))
    {
        throw new InvalidOperationException($"PID controller did not write a numeric clamped output. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}', RuntimeOutput='{runtime.CurrentOutputValue}'.");
    }

    if (Math.Abs(outputValue - 10d) >= 0.000001)
    {
        throw new InvalidOperationException($"PID controller output should clamp to 10, actual '{outputValue}'. State='{runtime.CurrentStateValue}', Alert='{runtime.CurrentAlertValue}', RuntimeOutput='{runtime.CurrentOutputValue}'. {DescribePidRunItem(runtime)}");
    }

    AssertEqual("Running", runtime.CurrentStateValue);
}

static void PidControllerRejectsInvalidChrParameters()
{
    var suffix = $"id_{Guid.NewGuid():N}";
    var folderName = $"pid_test_folder_{suffix}";
    var sourcePath = $"runtime.pid_test.{suffix}.source";
    var outputPath = $"runtime.pid_test.{suffix}.output";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d), pruneMissingMembers: true);
    HostRegistries.Data.UpsertSnapshot(outputPath, ItemExtension.CreateWithPath(outputPath, 0d), pruneMissingMembers: true);

    var definition = CreatePidDefinition("loop_invalid_chr", sourcePath, outputPath);
    definition.Pid.Ks = -1d;

    using var runtime = new PidControllerRuntime(folderName, definition);
    StopPidTimer(runtime);

    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.set", 50d));
    AssertTrue(HostRegistries.Data.UpdateValue($"{runtime.RegistryPath}.run", true));
    InvokePidEvaluation(runtime);

    AssertTrue(runtime.CurrentAlertValue);
    AssertEqual("Invalid parameters: Ks must be greater than zero.", runtime.CurrentStateValue);
}

static void ProcessLogRuntimePublishesLevelInputItems()
{
    var logPath = $"studio.process_log_runtime_{Guid.NewGuid():N}.logs.log_control";
    var logDirectory = Path.Combine(Path.GetTempPath(), "HornetStudioProcessLogRuntimeTests", Guid.NewGuid().ToString("N"));
    var normalizedPath = ProcessLogRuntime.EnsurePublished(logPath, "Runtime Log", logDirectory);

    AssertTrue(HostRegistries.Data.TryResolve(normalizedPath, out var logItem));
    AssertTrue(logItem?.Value is ProcessLog);

    foreach (var level in new[] { "debug", "info", "warning", "error", "fatal" })
    {
        var inputPath = $"{normalizedPath}.{level}";
        AssertTrue(HostRegistries.Data.TryResolve(inputPath, out var inputItem));
        AssertEqual(inputPath, inputItem?.Path);
        AssertEqual(string.Empty, inputItem?.Value);
        AssertEqual(true, inputItem?.Properties["writable"].Value);
        AssertEqual(string.Empty, inputItem?.Properties["write"].Value);
        AssertEqual(inputPath, inputItem?.Properties["write_path"].Value);
    }

    string? changedKey = null;
    HostRegistries.Data.ItemChanged += (_, e) =>
    {
        if (string.Equals(e.Key, $"{normalizedPath}.info", StringComparison.OrdinalIgnoreCase))
        {
            changedKey = e.Key;
        }
    };

    AssertTrue(HostRegistries.Data.UpdateValue($"{normalizedPath}.info", "Created from item change"));
    AssertEqual($"{normalizedPath}.info", changedKey);
    AssertTrue(HostRegistries.Data.TryResolve($"{normalizedPath}.info", out var afterFirstUpdateItem));
    AssertEqual(string.Empty, afterFirstUpdateItem?.Value);

    var processLog = (ProcessLog)logItem!.Value!;
    var logsField = typeof(ProcessLogRuntime).GetField("Logs", BindingFlags.Static | BindingFlags.NonPublic);
    var logs = logsField?.GetValue(null);
    var logFromRuntime = logs?.GetType().GetProperty("Item")?.GetValue(logs, [normalizedPath]);
    AssertTrue(ReferenceEquals(processLog, logFromRuntime));
    var entry = processLog.GetEntries().LastOrDefault();
    AssertEqual("Information", entry?.Level);
    AssertEqual("Created from item change", entry?.Message);
    AssertTrue(HostRegistries.Data.TryUpdateUserProperty($"{normalizedPath}.warning", "write", "Created from write property"));
    var warningEntry = processLog.GetEntries(levelFilter: "Warning").LastOrDefault();
    AssertEqual("Warning", warningEntry?.Level);
    AssertEqual("Created from write property", warningEntry?.Message);
    AssertTrue(Directory.GetFiles(logDirectory, "process-*.log").Length > 0);
}

static void ProcessLogRuntimeUpdatesLogDirectory()
{
    var logPath = $"studio.process_log_runtime_{Guid.NewGuid():N}.logs.log_control";
    var rootDirectory = Path.Combine(Path.GetTempPath(), "HornetStudioProcessLogRuntimeTests", Guid.NewGuid().ToString("N"));
    var firstLogDirectory = Path.Combine(rootDirectory, "First");
    var secondLogDirectory = Path.Combine(rootDirectory, "Second");

    try
    {
        var normalizedPath = ProcessLogRuntime.EnsurePublished(logPath, "Runtime Log", firstLogDirectory);
        AssertTrue(HostRegistries.Data.TryResolve(normalizedPath, out var logItem));
        AssertTrue(logItem?.Value is ProcessLog);

        var processLog = (ProcessLog)logItem!.Value!;
        AssertEqual(firstLogDirectory, processLog.LogDirectory);

        ProcessLogRuntime.EnsurePublished(logPath, "Runtime Log", secondLogDirectory);
        AssertEqual(secondLogDirectory, processLog.LogDirectory);
    }
    finally
    {
        if (Directory.Exists(rootDirectory))
        {
            try
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
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
        await using var hostClient = new HostItemBrokerClient("ItemClient1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-test");
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

static async Task HostItemBrokerClientExposesDirectReceivedRoots()
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
        await using var hostClient = new HostItemBrokerClient(
            name: "ItemClient1",
            host: IPAddress.Loopback.ToString(),
            port: port,
            baseTopic: "hornet",
            clientId: "hornet-studio-received-roots-test");
        await hostClient.ConnectAsync().ConfigureAwait(false);

        await using var publisher = new MqttItemClientSession(new MqttItemClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClientReceivedRoots",
            BaseTopic = "hornet",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.PublishReadAsync(ItemExtension.CreateWithPath("edm1.pressure", 12.5), retained: true).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var roots = hostClient.GetReceivedItemRootSnapshots();
            if (roots.TryGetValue("edm1", out var root)
                && object.Equals(12.5f, root["pressure"].Value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Host client did not expose direct received MQTT roots.");
    }
    finally
    {
        await server.StopAsync().ConfigureAwait(false);
        server.Dispose();
    }
}

static async Task HostItemBrokerClientReceivesLiveItemsWithoutBaseTopic()
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
        await using var hostClient = new HostItemBrokerClient("ItemClient1", IPAddress.Loopback.ToString(), port, string.Empty, "hornet-studio-test-empty-base");
        await hostClient.ConnectAsync().ConfigureAwait(false);

        await using var publisher = new MqttItemClientSession(new MqttItemClientOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ClientId = "DummyClientEmptyBase",
            BaseTopic = string.Empty,
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
        });

        await publisher.PublishReadAsync(ItemExtension.CreateWithPath("edm1.temperature", 24.5), retained: true).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (hostClient.Items.GetDictionary().TryGetValue("shared", out var root)
                && object.Equals(24.5f, root["edm1"]["temperature"].Value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Host client did not mirror live MQTT items without a base topic.");
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
        await using var hostClient = new HostItemBrokerClient("ItemClientSelfEcho", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-self-echo-test");
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
        await using var hostClient = new HostItemBrokerClient("ItemClient1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-snapshot-test");
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
        await using var publisher = new HostItemBrokerClient("ItemClient1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-publish-test");
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
        await using (var hostClient = new HostItemBrokerClient("ItemClient1", IPAddress.Loopback.ToString(), port, "hornet", "hornet-studio-owned-test"))
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

static ItemModel CreateFlatSourceModule(string name, double initialValue, bool setHasWriteChannel = true)
{
    var root = new ItemModel(name);
    AddFlatSourceChannel(root, "read", initialValue, hasWriteChannel: true);
    AddFlatSourceChannel(root, "set", initialValue, hasWriteChannel: setHasWriteChannel);
    AddFlatSourceChannel(root, "out", initialValue, hasWriteChannel: true);
    return ItemExtension.CloneWithPath(root, $"runtime.enhanced_signal_inverse_source.{name}");
}

static ControllerDefinition CreatePidDefinition(string name, string sourcePath, string outputPath, string setpointPath = "")
{
    return new ControllerDefinition
    {
        Name = name,
        SourcePath = sourcePath,
        OutputPath = outputPath,
        SetpointPath = setpointPath,
        Pid = new PidControllerDefinition
        {
            Ks = 1d,
            Tu = 1d,
            Tg = 2d,
            DFilterTauMs = 10d,
            SetMin = 0d,
            SetMax = 100d,
            OutMin = 0d,
            OutMax = 100d,
            ComputeIntervalMs = 20,
            OutputIntervalMs = 20
        }
    };
}

static ValueLogDefinition CreateValueLogDefinition(string id, string outputPath, string sourcePath = "")
{
    var sources = string.IsNullOrWhiteSpace(sourcePath)
        ? Array.Empty<ValueLogSourceDefinition>()
        :
        [
            new ValueLogSourceDefinition
            {
                Name = "source",
                TargetPath = sourcePath,
            }
        ];

    return new ValueLogDefinition
    {
        Id = id,
        Text = id,
        Kind = ValueLogKind.Csv,
        Enabled = true,
        OutputPath = outputPath,
        IntervalMs = 100,
        Sources = sources
    };
}

static async Task WaitForConditionAsync(Func<bool> condition, string failureMessage)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
    }

    throw new InvalidOperationException(failureMessage);
}

static bool TryReadAllLines(string path, out string[] lines)
{
    try
    {
        lines = File.ReadAllLines(path);
        return true;
    }
    catch (IOException)
    {
        lines = [];
        return false;
    }
    catch (UnauthorizedAccessException)
    {
        lines = [];
        return false;
    }
}

static void AddFlatSourceChannel(ItemModel root, string channelName, double initialValue, bool hasWriteChannel)
{
    root[channelName] = new ItemModel(channelName, hasWriteChannel: hasWriteChannel);
    root[channelName].Properties["read"].Value = initialValue;
    if (hasWriteChannel)
    {
        root[channelName].Properties["write"].Value = initialValue;
    }
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

static IEnumerable<string> EnumerateItemPaths(ItemModel item)
{
    if (!string.IsNullOrWhiteSpace(item.Path))
    {
        yield return item.Path!;
    }

    foreach (var child in item.GetDictionary().Values)
    {
        foreach (var childPath in EnumerateItemPaths(child))
        {
            yield return childPath;
        }
    }
}

static bool IsSnakeCaseSegment(string segment)
{
    if (string.IsNullOrWhiteSpace(segment) || !char.IsLetter(segment[0]) || segment[^1] == '_')
    {
        return false;
    }

    var previousWasSeparator = false;
    foreach (var character in segment)
    {
        if (character == '_')
        {
            if (previousWasSeparator)
            {
                return false;
            }

            previousWasSeparator = true;
            continue;
        }

        if (!char.IsLower(character) && !char.IsDigit(character))
        {
            return false;
        }

        previousWasSeparator = false;
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

static bool TryResolveNumericItemValue(string path, out double value)
{
    value = 0;
    if (!HostRegistries.Data.TryResolve(path, out var item) || item is null || item.Value is null)
    {
        return false;
    }

    try
    {
        value = Convert.ToDouble(item.Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
    catch (InvalidCastException)
    {
        return false;
    }
}

static void InvokePidEvaluation(PidControllerRuntime runtime)
{
    var method = typeof(PidControllerRuntime).GetMethod("EvaluateController", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(method is not null);
    method!.Invoke(runtime, null);
}

static void StopPidTimer(PidControllerRuntime runtime)
{
    var field = typeof(PidControllerRuntime).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic);
    if (field?.GetValue(runtime) is not ATimer timer)
    {
        throw new InvalidOperationException("PID controller timer field was not found.");
    }

    timer.Stop();
}

static string DescribePidRunItem(PidControllerRuntime runtime)
{
    if (!HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.run", out var runItem) || runItem is null)
    {
        return "Run item was not resolved.";
    }

    var writeValue = runItem.Properties.Has("write")
        ? runItem.Properties["write"].Value?.ToString() ?? "<null>"
        : "<missing>";
    var rootValue = HostRegistries.Data.TryResolve(runtime.RegistryPath, out var rootItem) && rootItem is not null
        ? rootItem.Value?.ToString() ?? "<null>"
        : "<missing>";
    return $"RunItemPath='{runItem.Path ?? "<null>"}', RunItemValue='{runItem.Value ?? "<null>"}', RunWrite='{writeValue}', RootValue='{rootValue}', IsRunning='{runtime.IsRunning}'.";
}

static void AssertPidRunState(PidControllerRuntime runtime, bool expectedValue, string context)
{
    if (!HostRegistries.Data.TryResolve($"{runtime.RegistryPath}.run", out var runItem) || runItem is null)
    {
        throw new InvalidOperationException($"PID controller run item was not resolved {context}.");
    }

    if (!object.Equals(expectedValue, runItem.Value))
    {
        throw new InvalidOperationException($"PID controller run item value mismatch {context}. Expected '{expectedValue}', actual '{runItem.Value ?? "<null>"}'. {DescribePidRunItem(runtime)}");
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

sealed class TestHostItem : IHostItem
{
    private object? _value;

    public TestHostItem(object? value)
    {
        _value = value;
    }

    public bool TryRead(out object? value)
    {
        value = _value;
        return true;
    }

    public bool TryWrite(object? value)
    {
        _value = value;
        return true;
    }
}
