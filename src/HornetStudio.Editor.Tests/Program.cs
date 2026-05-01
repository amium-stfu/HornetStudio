using Amium.Item;
using Amium.ItemBroker;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets;
using HornetStudio.Host;
using System.Reflection;

var tests = new (string Name, Action Run)[]
{
    ("Broker widget mode defaults to external", BrokerWidgetModeDefaultsToExternal),
    ("Broker widget mode normalizes values", BrokerWidgetModeNormalizesValues),
    ("Broker widget layout document defaults to external mode", BrokerWidgetLayoutDocumentDefaultsToExternalMode),
    ("Broker widget publish items default empty", BrokerWidgetPublishItemsDefaultEmpty),
    ("Broker widget layout publish items default empty", BrokerWidgetLayoutPublishItemsDefaultEmpty),
    ("Broker widget publish options use metadata", BrokerWidgetPublishOptionsUseMetadata),
    ("Broker widget publish options exclude broker received items", BrokerWidgetPublishOptionsExcludeBrokerReceivedItems),
    ("Broker widget publish options de-duplicate legacy roots", BrokerWidgetPublishOptionsDeduplicateLegacyRoots),
    ("Item tree visibility uses display metadata", ItemTreeVisibilityUsesDisplayMetadata),
    ("Broker attach options use internal discovery", BrokerAttachOptionsUseInternalDiscovery),
    ("Broker widget publish items renders flat attach rows", BrokerWidgetPublishItemsRendersFlatAttachRows),
    ("Broker widget received path uses MQTT branch", BrokerWidgetReceivedPathUsesMqttBranch),
    ("Broker widget received path collapses nested MQTT identity", BrokerWidgetReceivedPathCollapsesNestedMqttIdentity),
    ("Broker widget attach identity collapses nested MQTT identity", BrokerWidgetAttachIdentityCollapsesNestedMqttIdentity),
    ("Broker attach normalization strips prefix before MQTT identity", BrokerAttachNormalizationStripsPrefixBeforeMqttIdentity),
    ("Broker widget attach selection normalizes legacy shared path", BrokerWidgetAttachSelectionNormalizesLegacySharedPath),
    ("Target path normalization uses Studio root", TargetPathNormalizationUsesStudioRoot),
    ("Broker published item codec migrates legacy paths", BrokerPublishedItemCodecMigratesLegacyPaths),
    ("Broker published item codec keeps explicit Studio broker paths", BrokerPublishedItemCodecKeepsExplicitStudioBrokerPaths),
    ("Broker published item codec keeps explicit HornetStudio broker paths", BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths),
    ("Broker published item codec roundtrip", BrokerPublishedItemCodecRoundtrip),
    ("Broker published item codec filters active root definitions", BrokerPublishedItemCodecFiltersActiveRootDefinitions),
    ("Broker published item change matcher scopes changes", BrokerPublishedItemChangeMatcherScopesChanges),
    ("Broker write-back ignores non-writable entries", BrokerWriteBackIgnoresNonWritableEntries),
    ("Broker write-back ignores inactive entries", BrokerWriteBackIgnoresInactiveEntries),
    ("Broker write-back updates writable value", BrokerWriteBackUpdatesWritableValue),
    ("Broker write-back converts numeric value to local type", BrokerWriteBackConvertsNumericValueToLocalType),
    ("Broker write-back blocks protected parameters", BrokerWriteBackBlocksProtectedParameters),
    ("Broker write-back ignores same-value self echoes", BrokerWriteBackIgnoresSameValueSelfEchoes),
    ("Broker write-back cleanup disposes subscriptions", BrokerWriteBackCleanupDisposesSubscriptions),
    ("Item exposure codec roundtrip", ItemExposureCodecRoundtrip),
    ("Item exposure codec upsert and remove", ItemExposureCodecUpsertAndRemove),
    ("Item exposure codec normalizes runtime broker paths", ItemExposureCodecNormalizesRuntimeBrokerPaths),
    ("Item exposure publisher applies bit helpers", ItemExposurePublisherAppliesBitHelpers),
    ("Target parameter options hide protected parameters", TargetParameterOptionsHideProtectedParameters),
    ("Target parameter field hidden for normal widgets", TargetParameterFieldHiddenForNormalWidgets),
    ("Target parameter defaults to value", TargetParameterDefaultsToValue),
    ("Target parameter protected fallback uses value", TargetParameterProtectedFallbackUsesValue),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Editor tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"Editor tests passed: {tests.Length}");
return 0;

static void ItemExposureCodecRoundtrip()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b4",
            Unit = "flags",
            ExposeBits = true,
            BitCount = 4,
            BitLabels = "Bit0=Ready"
        }
    ]);

    var parsed = ItemExposureDefinitionCodec.ParseDefinitions(serialized);
    AssertEqual(1, parsed.Count);
    AssertEqual("broker1.client1.device.mask", parsed[0].ItemPath);
    AssertEqual("b4", parsed[0].Format);
    AssertEqual("flags", parsed[0].Unit);
    AssertEqual(true, parsed[0].ExposeBits);
    AssertEqual(4, parsed[0].BitCount);
    AssertEqual("Bit0=Ready", parsed[0].BitLabels);
}

static void TargetParameterOptionsHideProtectedParameters()
{
    var item = new Item("Device").Repath("Runtime.PolicyPicker.Device");
    item.Params["Value"].Value = 1;
    item.Params["Unit"].Value = "V";
    item.Params["Writable"].Value = true;
    item.Params["WritePath"].Value = "Runtime.PolicyPicker.Device.Request";
    HostRegistries.Data.UpsertSnapshot("Runtime.PolicyPicker.Device", item);

    var method = typeof(MainWindowViewModel).GetMethod("GetTargetParameterOptions", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetTargetParameterOptions was not found.");
    }

    var options = ((IEnumerable<string>)method.Invoke(null, ["Runtime.PolicyPicker.Device", string.Empty])!)
        .ToArray();

    AssertTrue(options.Contains("Value", StringComparer.OrdinalIgnoreCase));
    AssertEqual("Value", options[0]);
    AssertTrue(options.Contains("Unit", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("Writable", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("WritePath", StringComparer.OrdinalIgnoreCase));
}

static void TargetParameterFieldHiddenForNormalWidgets()
{
    var method = typeof(MainWindowViewModel).GetMethod("ShouldShowEditorDialogField", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ShouldShowEditorDialogField was not found.");
    }

    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.Item }, "TargetParameterPath"])!);
    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.Signal }, "TargetParameterPath"])!);
    AssertTrue((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.Item }, "TargetPath"])!);
}

static void TargetParameterDefaultsToValue()
{
    var target = new Item("Device").Repath("Runtime.PolicyPicker.DefaultDevice");
    target.Params["Value"].Value = 42;
    target.Params["Unit"].Value = "V";
    HostRegistries.Data.UpsertSnapshot("Runtime.PolicyPicker.DefaultDevice", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "Runtime.PolicyPicker.DefaultDevice";

    AssertEqual("Value", item.TargetParameterPath);
}

static void TargetParameterProtectedFallbackUsesValue()
{
    var target = new Item("Device").Repath("Runtime.PolicyPicker.ProtectedDevice");
    target.Params["Value"].Value = 7;
    target.Params["WritePath"].Value = "Runtime.PolicyPicker.ProtectedDevice.Request";
    HostRegistries.Data.UpsertSnapshot("Runtime.PolicyPicker.ProtectedDevice", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "Runtime.PolicyPicker.ProtectedDevice";
    item.TargetParameterPath = "WritePath";

    AssertEqual("Value", item.TargetParameterPath);
    AssertEqual("Value", item.TargetParameterView.Parameter?.Name);
}

static void BrokerWidgetModeDefaultsToExternal()
{
    var item = new FolderItemModel { Kind = ControlKind.BrokerWidget };

    AssertEqual(BrokerWidgetModes.External, item.BrokerMode);
}

static void BrokerWidgetModeNormalizesValues()
{
    var item = new FolderItemModel { Kind = ControlKind.BrokerWidget };

    item.BrokerMode = "Own";
    AssertEqual(BrokerWidgetModes.Own, item.BrokerMode);

    item.BrokerMode = "unexpected";
    AssertEqual(BrokerWidgetModes.External, item.BrokerMode);
}

static void BrokerWidgetLayoutDocumentDefaultsToExternalMode()
{
    var document = new FolderItemDocument();

    AssertEqual(BrokerWidgetModes.External, document.BrokerMode);
}

static void BrokerWidgetPublishItemsDefaultEmpty()
{
    var item = new FolderItemModel { Kind = ControlKind.BrokerWidget };

    AssertEqual(string.Empty, item.BrokerPublishedItemPaths);
}

static void BrokerWidgetLayoutPublishItemsDefaultEmpty()
{
    var document = new FolderItemDocument();

    AssertEqual(string.Empty, document.BrokerPublishedItemPaths);
}

static void BrokerWidgetPublishOptionsUseMetadata()
{
    var publicPath = "Studio.MetadataPublish.PublicSignal";
    var customSignalPath = "Studio.MetadataPublish.CustomSignals.Signal1";
    var enhancedSignalPath = "Studio.MetadataPublish.EnhancedSignals.Signal1";
    var internalPath = "Studio.MetadataPublish.Broker.Status.AttachOptions";
    HostRegistries.Data.Remove(publicPath);
    HostRegistries.Data.Remove(customSignalPath);
    HostRegistries.Data.Remove(enhancedSignalPath);
    HostRegistries.Data.Remove(internalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(publicPath, new Item("PublicSignal").Repath(publicPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(customSignalPath, new Item("Signal1").Repath(customSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(enhancedSignalPath, new Item("Signal1").Repath(enhancedSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(internalPath, new Item("AttachOptions").Repath(internalPath), DataRegistryItemMetadata.WidgetInternal());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.BrokerWidget }])!).ToArray();

        AssertTrue(options.Contains(publicPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains(customSignalPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains(enhancedSignalPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(internalPath, StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(publicPath);
        HostRegistries.Data.Remove(customSignalPath);
        HostRegistries.Data.Remove(enhancedSignalPath);
        HostRegistries.Data.Remove(internalPath);
    }
}

static void BrokerWidgetPublishOptionsExcludeBrokerReceivedItems()
{
    var localPath = "Studio.MetadataPublish.LocalSignal";
    var receivedPath = "Studio.MetadataPublish.BrokerWidget1.Mqtt.Device.Temperature";
    HostRegistries.Data.Remove(localPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(localPath, new Item("LocalSignal").Repath(localPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(receivedPath, new Item("Temperature").Repath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

        AssertTrue(HostRegistries.Data.TryGetMetadata(receivedPath, out var metadata));
        AssertFalse(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerPublish));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Display));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerAttach));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.DebugInspect));

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.BrokerWidget }])!).ToArray();

        AssertTrue(options.Contains(localPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(receivedPath, StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
        HostRegistries.Data.Remove(receivedPath);
    }
}

static void BrokerWidgetPublishOptionsDeduplicateLegacyRoots()
{
    var legacyPath = "Project.MetadataPublish.DeduplicatedSignal";
    var canonicalPath = "Studio.MetadataPublish.DeduplicatedSignal";
    HostRegistries.Data.Remove(legacyPath);
    HostRegistries.Data.Remove(canonicalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(legacyPath, new Item("DeduplicatedSignal").Repath(legacyPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(canonicalPath, new Item("DeduplicatedSignal").Repath(canonicalPath), DataRegistryItemMetadata.PublicData());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.BrokerWidget }])!)
            .Where(path => path.EndsWith(".MetadataPublish.DeduplicatedSignal", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        AssertEqual(1, options.Length);
        AssertEqual(canonicalPath, options[0]);
    }
    finally
    {
        HostRegistries.Data.Remove(legacyPath);
        HostRegistries.Data.Remove(canonicalPath);
    }
}

static void ItemTreeVisibilityUsesDisplayMetadata()
{
    var internalPath = "Studio.RegistryVisibility.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Temperature";
    var receivedPath = "Studio.RegistryVisibility.BrokerWidget1.Mqtt.Edm1.Temperature";
    HostRegistries.Data.Remove(internalPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(internalPath, new Item("Temperature").Repath(internalPath), DataRegistryItemMetadata.WidgetInternal());
        HostRegistries.Data.UpsertSnapshot(receivedPath, new Item("Temperature", 21.5).Repath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

        using var viewModel = new HornetStudio.ViewModels.ItemTreeWindowViewModel();
        var refreshMethod = typeof(HornetStudio.ViewModels.ItemTreeWindowViewModel).GetMethod("RefreshTree", BindingFlags.NonPublic | BindingFlags.Instance);
        if (refreshMethod is null)
        {
            throw new InvalidOperationException("RefreshTree was not found.");
        }

        refreshMethod.Invoke(viewModel, []);

        AssertTrue(ContainsTreePath(viewModel.RootNodes, receivedPath));
        AssertFalse(ContainsTreePath(viewModel.RootNodes, internalPath));
    }
    finally
    {
        HostRegistries.Data.Remove(internalPath);
        HostRegistries.Data.Remove(receivedPath);
    }
}

static void BrokerAttachOptionsUseInternalDiscovery()
{
    var attachOptionPath = "Studio.RegistryVisibility.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Temperature";
    HostRegistries.Data.Remove(attachOptionPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(attachOptionPath, new Item("BrokerWidget1.Mqtt.Edm1.Temperature").Repath(attachOptionPath), DataRegistryItemMetadata.WidgetInternal());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerAttachItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerAttachItemOptions was not found.");
        }

        var item = new FolderItemModel
        {
            Kind = ControlKind.BrokerWidget,
            Name = "BrokerWidget1"
        };
        item.SetHierarchy("RegistryVisibility", parentItem: null);

        var options = ((IEnumerable<string>)method.Invoke(null, [item])!).ToArray();

        AssertTrue(options.Contains("BrokerWidget1.Mqtt.Edm1.Temperature", StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(attachOptionPath);
    }
}

static void BrokerWidgetPublishItemsRendersFlatAttachRows()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.BrokerWidget,
        BrokerPublishedItemPaths = "Studio.DefaultLayout.Edm1.Pressure"
    };
    var definition = new EditorDialogBindingDefinition(
        "BrokerPublishedItemPaths",
        "PublishItems",
        EditorPropertyType.AttachItemList,
        current => current.BrokerPublishedItemPaths,
        optionsFactory: _ =>
        [
            "Studio.DefaultLayout.Edm1.Pressure",
            "Studio.DefaultLayout.Edm1.Temperature"
        ]);

    var field = definition.CreateField(item);

    AssertEqual(2, field.AttachItemEntries.Count);
    AssertTrue(field.AttachItemEntries.All(static row => !row.IsGroup));
    AssertEqual("Pressure", field.AttachItemEntries[0].DisplayName);
    AssertEqual("Studio.DefaultLayout.Edm1", field.AttachItemEntries[0].DisplaySource);
    AssertEqual(true, field.AttachItemEntries[0].IsAttached);
    AssertEqual(false, field.AttachItemEntries[1].IsAttached);
}

static void BrokerWidgetReceivedPathUsesMqttBranch()
{
    var method = typeof(BrokerClientControl).GetMethod("BuildReceivedMqttRuntimePath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedMqttRuntimePath was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.BrokerWidget,
        Name = "BrokerWidget1"
    };
    item.SetHierarchy("DefaultLayout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "shared", "Edm1.Pressure"])!;

    AssertEqual("Studio.DefaultLayout.BrokerWidget1.Mqtt.Edm1.Pressure", path);
    AssertFalse(path.Contains("shared", StringComparison.OrdinalIgnoreCase));
    AssertFalse(path.Contains("Status.AttachOptions", StringComparison.OrdinalIgnoreCase));
}

static void BrokerWidgetReceivedPathCollapsesNestedMqttIdentity()
{
    var method = typeof(BrokerClientControl).GetMethod("BuildReceivedMqttRuntimePath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedMqttRuntimePath was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.BrokerWidget,
        Name = "BrokerWidget1"
    };
    item.SetHierarchy("DefaultLayout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "Edm1.Pressure", "BrokerWidget1.Mqtt.Edm1.Pressure"])!;

    AssertEqual("Studio.DefaultLayout.BrokerWidget1.Mqtt.Edm1.Pressure", path);
    AssertFalse(path.Contains("Edm1.Pressure.BrokerWidget1", StringComparison.OrdinalIgnoreCase));
}

static void BrokerWidgetAttachIdentityCollapsesNestedMqttIdentity()
{
    var method = typeof(BrokerClientControl).GetMethod("BuildBrokerAttachIdentity", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrokerAttachIdentity was not found.");
    }

    var path = (string)method.Invoke(null, ["BrokerWidget1", "Edm1.Pressure", "BrokerWidget1.Mqtt.Edm1.Pressure"])!;

    AssertEqual("BrokerWidget1.Mqtt.Edm1.Pressure", path);
    AssertFalse(path.Contains("Edm1.Pressure.BrokerWidget1", StringComparison.OrdinalIgnoreCase));
}

static void BrokerAttachNormalizationStripsPrefixBeforeMqttIdentity()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("ToBrokerReceivedAttachIdentity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ToBrokerReceivedAttachIdentity was not found.");
    }

    AssertEqual("BrokerWidget1.Mqtt.Edm1.Pressure", method.Invoke(null, ["Edm1.Pressure.BrokerWidget1.Mqtt.Edm1.Pressure"]));
    AssertEqual("BrokerWidget1.Mqtt.Edm1.Pressure", method.Invoke(null, ["Studio.Folder1.BrokerWidget1.Mqtt.Edm1.Pressure"]));
}

static void BrokerWidgetAttachSelectionNormalizesLegacySharedPath()
{
    var item = new FolderItemModel { Kind = ControlKind.BrokerWidget };
    var definition = new EditorDialogBindingDefinition(
        "BrokerAttachedItemPaths",
        "AttachToUi",
        EditorPropertyType.AttachItemList,
        _ => "Runtime.ItemBroker.BrokerWidget1.shared.Edm1.Pressure",
        optionsFactory: _ => ["BrokerWidget1.Mqtt.Edm1.Pressure"]);

    var field = definition.CreateField(item);

    AssertEqual(1, field.AttachItemEntries.Count);
    AssertEqual("BrokerWidget1.Mqtt.Edm1.Pressure", field.AttachItemEntries[0].RelativePath);
    AssertEqual(true, field.AttachItemEntries[0].IsAttached);
    AssertEqual(false, field.AttachItemEntries[0].IsMissing);
}

static void TargetPathNormalizationUsesStudioRoot()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("NormalizeConfiguredTargetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("NormalizeConfiguredTargetPath was not found.");
    }

    AssertEqual("Studio.Folder.X", method.Invoke(null, ["Project.Folder.X"]));
    AssertEqual("Studio.Folder.X", method.Invoke(null, ["Studio.Folder.X"]));
    AssertEqual("Studio.Folder.X", method.Invoke(null, ["Studio.Project.Folder.X"]));
}

static void BrokerPublishedItemCodecMigratesLegacyPaths()
{
    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions("Project.DefaultLayout.Edm1.Pressure");

    AssertEqual(1, parsed.Count);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", parsed[0].LocalPath);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", parsed[0].LocalRootPath);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", parsed[0].BrokerPath);
    AssertEqual(false, parsed[0].Active);
    AssertEqual(BrokerPublishedItemPublishModes.OnChanged, parsed[0].PublishMode);
    AssertEqual(1000, parsed[0].PublishIntervalMs);
    AssertEqual(false, parsed[0].Writable);
}

static void BrokerPublishedItemCodecKeepsExplicitStudioBrokerPaths()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "Project.DefaultLayout.Edm1.Pressure",
            LocalRootPath = "Project.DefaultLayout.Edm1",
            BrokerPath = "Studio.Custom.Path",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("Studio.Custom.Path", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "Project.DefaultLayout.Edm1.Pressure",
            LocalRootPath = "Project.DefaultLayout.Edm1",
            BrokerPath = "HornetStudio.Project.DefaultLayout.Edm1.Pressure",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("HornetStudio.Project.DefaultLayout.Edm1.Pressure", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecRoundtrip()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "Project.DefaultLayout.Edm1.Pressure",
            LocalRootPath = "Project.DefaultLayout.Edm1",
            BrokerPath = "Studio.Project.DefaultLayout.Edm1.Pressure",
            Active = true,
            PublishMode = BrokerPublishedItemPublishModes.Interval,
            PublishIntervalMs = 250,
            Writable = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", parsed[0].LocalPath);
    AssertEqual("Studio.DefaultLayout.Edm1", parsed[0].LocalRootPath);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", parsed[0].BrokerPath);
    AssertEqual(true, parsed[0].Active);
    AssertEqual(BrokerPublishedItemPublishModes.Interval, parsed[0].PublishMode);
    AssertEqual(250, parsed[0].PublishIntervalMs);
    AssertEqual(true, parsed[0].Writable);
}

static void BrokerPublishedItemCodecFiltersActiveRootDefinitions()
{
    var definitions = new[]
    {
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "Project.DefaultLayout.Edm1",
            LocalPath = "Project.DefaultLayout.Edm1.Pressure",
            BrokerPath = "Studio.Project.DefaultLayout.Edm1.Pressure",
            Active = true
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "Project.DefaultLayout.Edm1",
            LocalPath = "Project.DefaultLayout.Edm1.Temperature",
            BrokerPath = "Studio.Project.DefaultLayout.Edm1.Temperature",
            Active = false
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "Project.DefaultLayout.Edm2",
            LocalPath = "Project.DefaultLayout.Edm2.Pressure",
            BrokerPath = "Studio.Project.DefaultLayout.Edm2.Pressure",
            Active = true
        }
    };

    var filtered = BrokerPublishedItemDefinitionCodec.GetActiveDefinitionsForRoot(definitions, "project.defaultlayout.edm1");

    AssertEqual(1, filtered.Count);
    AssertEqual("Studio.DefaultLayout.Edm1.Pressure", filtered[0].LocalPath);
}

static void BrokerPublishedItemChangeMatcherScopesChanges()
{
    var rootDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "Studio.DefaultLayout.Edm1",
        BrokerPath = "Studio.DefaultLayout.Edm1",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var childDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "Studio.DefaultLayout.Edm1.Pressure",
        BrokerPath = "Studio.DefaultLayout.Edm1.Pressure",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var rootItem = new Item("Edm1").Repath("Studio.DefaultLayout.Edm1");
    rootItem["Pressure"].Value = 12.5;

    Item? Resolve(string path)
    {
        if (string.Equals(path, "Studio.DefaultLayout.Edm1", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem;
        }

        if (string.Equals(path, "Studio.DefaultLayout.Edm1.Pressure", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem["Pressure"];
        }

        return null;
    }

    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("Studio.DefaultLayout.Edm1.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        rootDefinition,
        new DataChangedEventArgs("Studio.DefaultLayout.Edm1.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("Studio.DefaultLayout.Edm1", rootItem, DataChangeKind.SnapshotUpserted),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("Studio.DefaultLayout.Edm1", rootItem, DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("Studio.DefaultLayout.Edm2.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
}

static void BrokerWriteBackIgnoresNonWritableEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "Runtime.BrokerWriteBack.NonWritable",
        "Studio.Runtime.BrokerWriteBack.NonWritable",
        active: true,
        writable: false);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackIgnoresInactiveEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "Runtime.BrokerWriteBack.Inactive",
        "Studio.Runtime.BrokerWriteBack.Inactive",
        active: false,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackUpdatesWritableValue()
{
    var localPath = "Runtime.BrokerWriteBack.Value";
    var brokerPath = "Studio.Runtime.BrokerWriteBack.Value";
    HostRegistries.Data.UpsertSnapshot(localPath, new Item("Value", 1).Repath(localPath));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
}

static void BrokerWriteBackConvertsNumericValueToLocalType()
{
    var localPath = "Runtime.BrokerWriteBack.DoubleValue";
    var brokerPath = "Studio.Runtime.BrokerWriteBack.DoubleValue";
    HostRegistries.Data.UpsertSnapshot(localPath, new Item("DoubleValue", 1.5).Repath(localPath));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 2L, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void BrokerWriteBackBlocksProtectedParameters()
{
    var localPath = "Runtime.BrokerWriteBack.ProtectedParameter";
    var brokerPath = "Studio.Runtime.BrokerWriteBack.ProtectedParameter";
    var item = new Item("ProtectedParameter", 1).Repath(localPath);
    item.Params["Writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemParameterChangedMessage(brokerPath, "Writable", false, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(true, resolved?.Params["Writable"].Value);
}

static void BrokerWriteBackIgnoresSameValueSelfEchoes()
{
    var localPath = "Runtime.BrokerWriteBack.SameValue";
    var brokerPath = "Studio.Runtime.BrokerWriteBack.SameValue";
    HostRegistries.Data.UpsertSnapshot(localPath, new Item("OwnSource", 1).Repath(localPath));

    var client = new FakeHostItemBrokerClient { ClientIdValue = "own-client" };
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 1L, "own-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(1, resolved?.Value);
}

static void BrokerWriteBackCleanupDisposesSubscriptions()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "Runtime.BrokerWriteBack.Cleanup",
        "Studio.Runtime.BrokerWriteBack.Cleanup",
        active: true,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();
    AssertEqual(1, client.Subscriptions.Count);

    writeBack.DisposeAsync().AsTask().GetAwaiter().GetResult();

    AssertTrue(client.Subscriptions[0].Disposed);
}

static HostItemBrokerWriteBackClient CreateWriteBackClient(
    FakeHostItemBrokerClient client,
    string localPath,
    string brokerPath,
    bool active,
    bool writable)
    => new(client,
    [
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = localPath,
            LocalPath = localPath,
            BrokerPath = brokerPath,
            Active = active,
            Writable = writable,
        }
    ]);

static void ItemExposurePublisherAppliesBitHelpers()
{
    var item = new Item("mask", 5, "Runtime.ItemBroker.broker1.client1.device");
    var definition = new ItemExposureDefinition
    {
        ItemPath = "broker1.client1.device.mask",
        Format = "b4",
        Unit = "flags",
        ExposeBits = true,
        BitCount = 4,
        BitLabels = "Bit0=Ready\nBit2=Fault"
    };

    ItemExposurePublisher.Apply(item, definition);

    AssertEqual("b4", item.Params["Format"].Value);
    AssertEqual("flags", item.Params["Unit"].Value);
    AssertTrue(item.Has("Bits"));
    AssertTrue(item["Bits"].Has("Bit0"));
    AssertTrue(item["Bits"].Has("Bit2"));
    AssertEqual(true, item["Bits"]["Bit0"].Value);
    AssertEqual(false, item["Bits"]["Bit1"].Value);
    AssertEqual(true, item["Bits"]["Bit2"].Value);
    AssertEqual("Ready", item["Bits"]["Bit0"].Params["Title"].Value);
    AssertEqual("Fault", item["Bits"]["Bit2"].Params["Title"].Value);
}

static void ItemExposureCodecUpsertAndRemove()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "device.mask",
            Format = "b4",
            ExposeBits = true,
            BitCount = 4
        },
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.value",
            Unit = "V"
        }
    ]);

    var upserted = ItemExposureDefinitionCodec.UpsertDefinition(
        serialized,
        "broker1.client1.device.mask",
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b8",
            ExposeBits = true,
            BitCount = 8
        });
    var parsedUpserted = ItemExposureDefinitionCodec.ParseDefinitions(upserted);
    AssertEqual(2, parsedUpserted.Count);
    AssertEqual("b8", parsedUpserted.Single(definition => definition.ItemPath.EndsWith("mask", StringComparison.OrdinalIgnoreCase)).Format);

    var removed = ItemExposureDefinitionCodec.RemoveDefinition(upserted, "broker1.client1.device.mask");
    var parsedRemoved = ItemExposureDefinitionCodec.ParseDefinitions(removed);
    AssertEqual(1, parsedRemoved.Count);
    AssertEqual("broker1.client1.device.value", parsedRemoved[0].ItemPath);
}

static void ItemExposureCodecNormalizesRuntimeBrokerPaths()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "device.mask",
            Format = "b4",
            ExposeBits = true,
            BitCount = 4
        }
    ]);

    var upserted = ItemExposureDefinitionCodec.UpsertDefinition(
        serialized,
        "Runtime.ItemBroker.broker1.client1.device.mask",
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b8",
            ExposeBits = true,
            BitCount = 8
        });

    var parsed = ItemExposureDefinitionCodec.ParseDefinitions(upserted);
    AssertEqual(1, parsed.Count);
    AssertEqual("broker1.client1.device.mask", parsed[0].ItemPath);
    AssertEqual("b8", parsed[0].Format);
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

static bool ContainsTreePath(IEnumerable<HornetStudio.ViewModels.ItemTreeNodeViewModel> nodes, string path)
{
    foreach (var node in nodes)
    {
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase)
            || ContainsTreePath(node.Children, path))
        {
            return true;
        }
    }

    return false;
}

sealed class FakeHostItemBrokerClient : IHostItemBrokerClient
{
    public string ClientIdValue { get; set; } = "broker-widget-test";

    public string Name => "BrokerWidgetTest";

    public string Host => "127.0.0.1";

    public int Port => 1883;

    public string BaseTopic => "hornet";

    public string ClientId => ClientIdValue;

    public bool IsConnected => true;

    public ItemDictionary Items { get; } = new("Runtime.ItemBroker.BrokerWidgetTest");

    public List<FakeItemSubscription> Subscriptions { get; } = [];

    public event Action<string>? Diagnostic;

    public event Action? ItemsChanged;

    public IReadOnlyDictionary<string, Item> GetItemSnapshots() => new Dictionary<string, Item>();

    public Task PublishItemAsync(Item item, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = new FakeItemSubscription(path, options?.Recursive ?? true, handler);
        Subscriptions.Add(subscription);
        return Task.FromResult<IItemSubscription>(subscription);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void PublishToSubscription(ItemBrokerMessage message)
    {
        foreach (var subscription in Subscriptions.Where(subscription => string.Equals(subscription.Path, message.Path, StringComparison.OrdinalIgnoreCase)))
        {
            subscription.HandleAsync(message).GetAwaiter().GetResult();
        }
    }
}

sealed class FakeItemSubscription : IItemSubscription
{
    private readonly Func<ItemBrokerMessage, CancellationToken, Task> _handler;

    public FakeItemSubscription(string path, bool recursive, Func<ItemBrokerMessage, CancellationToken, Task> handler)
    {
        Path = path;
        Recursive = recursive;
        _handler = handler;
    }

    public string SubscriptionId { get; } = Guid.NewGuid().ToString("N");

    public IItemBrokerClient Client { get; } = new FakeItemBrokerClient();

    public string Path { get; }

    public bool Recursive { get; }

    public bool Disposed { get; private set; }

    public Task HandleAsync(ItemBrokerMessage message)
        => _handler(message, CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

sealed class FakeItemBrokerClient : IItemBrokerClient
{
    public string ClientId => "fake";

    public Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
