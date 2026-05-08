using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets;
using HornetStudio.Host;
using System.Reflection;

var tests = new (string Name, Action Run)[]
{
    ("Custom signal codec parses YAML style nodes", CustomSignalCodecParsesYamlStyleNodes),
    ("Path identity validation accepts only snake_case", PathIdentityValidationAcceptsOnlySnakeCase),
    ("Folder identity validation accepts only snake_case", FolderIdentityValidationAcceptsOnlySnakeCase),
    ("Folder identity defaults use snake_case", FolderIdentityDefaultsUseSnakeCase),
    ("Custom signal editor defaults to snake_case name", CustomSignalEditorDefaultsToSnakeCaseName),
    ("Custom signal editor rejects uppercase name", CustomSignalEditorRejectsUppercaseName),
    ("Custom signal manual trigger path uses lowercase suffix", CustomSignalManualTriggerPathUsesLowercaseSuffix),
    ("Enhanced signal editor defaults to snake_case name", EnhancedSignalEditorDefaultsToSnakeCaseName),
    ("Enhanced signal editor rejects uppercase name", EnhancedSignalEditorRejectsUppercaseName),
    ("Broker widget mode defaults to external", BrokerWidgetModeDefaultsToExternal),
    ("Broker widget mode normalizes values", BrokerWidgetModeNormalizesValues),
    ("Broker widget layout document defaults to external mode", BrokerWidgetLayoutDocumentDefaultsToExternalMode),
    ("Broker widget publish items default empty", BrokerWidgetPublishItemsDefaultEmpty),
    ("Broker widget layout publish items default empty", BrokerWidgetLayoutPublishItemsDefaultEmpty),
    ("Broker widget publish options use metadata", BrokerWidgetPublishOptionsUseMetadata),
    ("Broker widget publish options exclude broker received items", BrokerWidgetPublishOptionsExcludeBrokerReceivedItems),
    ("Broker widget publish options de-duplicate legacy roots", BrokerWidgetPublishOptionsDeduplicateLegacyRoots),
    ("ItemModel tree visibility uses display metadata", ItemTreeVisibilityUsesDisplayMetadata),
    ("Broker attach options use internal discovery", BrokerAttachOptionsUseInternalDiscovery),
    ("Broker widget publish items renders flat attach rows", BrokerWidgetPublishItemsRendersFlatAttachRows),
    ("Broker widget received path uses MQTT branch", BrokerWidgetReceivedPathUsesMqttBranch),
    ("Broker widget received path collapses nested MQTT identity", BrokerWidgetReceivedPathCollapsesNestedMqttIdentity),
    ("Broker widget attach identity collapses nested MQTT identity", BrokerWidgetAttachIdentityCollapsesNestedMqttIdentity),
    ("Broker attach normalization strips prefix before MQTT identity", BrokerAttachNormalizationStripsPrefixBeforeMqttIdentity),
    ("Broker widget attach selection normalizes legacy shared path", BrokerWidgetAttachSelectionNormalizesLegacySharedPath),
    ("UDL attach add normalizes and de-duplicates paths", UdlAttachAddNormalizesAndDeduplicatesPaths),
    ("UDL attach remove clears selected path", UdlAttachRemoveClearsSelectedPath),
    ("Target path normalization uses Studio root", TargetPathNormalizationUsesStudioRoot),
    ("Broker published item codec migrates legacy paths", BrokerPublishedItemCodecMigratesLegacyPaths),
    ("Broker published item codec keeps explicit Studio broker paths", BrokerPublishedItemCodecKeepsExplicitStudioBrokerPaths),
    ("Broker published item codec keeps explicit HornetStudio broker paths", BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths),
    ("Broker published item codec roundtrip", BrokerPublishedItemCodecRoundtrip),
    ("Broker published item codec filters active root definitions", BrokerPublishedItemCodecFiltersActiveRootDefinitions),
    ("Broker published item change matcher scopes changes", BrokerPublishedItemChangeMatcherScopesChanges),
    ("Broker publisher sends value update for unregistered value change", BrokerPublisherSendsValueUpdateForUnregisteredValueChange),
    ("Broker write-back ignores non-writable entries", BrokerWriteBackIgnoresNonWritableEntries),
    ("Broker write-back ignores inactive entries", BrokerWriteBackIgnoresInactiveEntries),
    ("Broker write-back updates writable value", BrokerWriteBackUpdatesWritableValue),
    ("Broker write-back uses request write target", BrokerWriteBackUsesRequestWriteTarget),
    ("Broker write-back converts numeric value to local type", BrokerWriteBackConvertsNumericValueToLocalType),
    ("Broker write-back blocks protected properties", BrokerWriteBackBlocksProtectedProperties),
    ("Broker write-back ignores same-value self echoes", BrokerWriteBackIgnoresSameValueSelfEchoes),
    ("Broker write-back cleanup disposes subscriptions", BrokerWriteBackCleanupDisposesSubscriptions),
    ("ItemModel exposure codec roundtrip", ItemExposureCodecRoundtrip),
    ("ItemModel exposure codec upsert and remove", ItemExposureCodecUpsertAndRemove),
    ("ItemModel exposure codec normalizes runtime broker paths", ItemExposureCodecNormalizesRuntimeBrokerPaths),
    ("ItemModel exposure publisher applies bit helpers", ItemExposurePublisherAppliesBitHelpers),
    ("Target property options hide protected properties", TargetPropertyOptionsHideProtectedProperties),
    ("Target property field hidden for normal widgets", TargetPropertyFieldHiddenForNormalWidgets),
    ("Target property defaults to value", TargetPropertyDefaultsToValue),
    ("Target property protected fallback uses value", TargetPropertyProtectedFallbackUsesValue),
    ("Signal write emits registry value update", SignalWriteEmitsRegistryValueUpdate),
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

static void CustomSignalCodecParsesYamlStyleNodes()
{
        var raw = """
[
    {
        "name": "DummyValue",
        "mode": "Input",
        "dataType": "Number",
        "isWritable": true,
        "writePath": "",
        "writeMode": "Direct",
        "unit": "",
        "format": "",
        "valueText": "123",
        "formula": "",
        "trigger": "OnSourceChange",
        "triggerIntervalSeconds": 1,
        "variables": [],
        "operation": "Copy",
        "sourcePath": "",
        "sourcePath2": "",
        "sourcePath3": ""
    },
    {
        "name": 42
    }
]
""";

        var parsed = CustomSignalDefinitionCodec.ParseDefinitions(raw);
        AssertEqual(1, parsed.Count);
        AssertEqual("DummyValue", parsed[0].Name);
        AssertEqual("123", parsed[0].ValueText);
        AssertEqual(CustomSignalMode.Input, parsed[0].Mode);
}

static void PathIdentityValidationAcceptsOnlySnakeCase()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("IsValidPathIdentityName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("IsValidPathIdentityName was not found.");
    }

    AssertEqual(true, method.Invoke(null, ["custom_signal_1"]));
    AssertEqual(true, method.Invoke(null, ["signal1"]));
    AssertEqual(false, method.Invoke(null, ["CustomSignal1"]));
    AssertEqual(false, method.Invoke(null, ["custom-signal-1"]));
}

static void FolderIdentityValidationAcceptsOnlySnakeCase()
{
    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("TryValidateFolderIdentityName", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("TryValidateFolderIdentityName was not found.");
    }

    var arguments = new object?[] { "main", null, null };
    AssertEqual(true, method.Invoke(null, arguments));
    AssertEqual("main", arguments[1]);

    arguments = ["page_1", null, null];
    AssertEqual(true, method.Invoke(null, arguments));
    AssertEqual("page_1", arguments[1]);

    arguments = ["Folder1", null, null];
    AssertEqual(false, method.Invoke(null, arguments));
    AssertTrue(((string)arguments[2]!).Contains("snake_case", StringComparison.Ordinal));

    arguments = ["main-page", null, null];
    AssertEqual(false, method.Invoke(null, arguments));
    AssertTrue(((string)arguments[2]!).Contains("snake_case", StringComparison.Ordinal));
}

static void FolderIdentityDefaultsUseSnakeCase()
{
    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("GetDefaultFolderIdentityName", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetDefaultFolderIdentityName was not found.");
    }

    AssertEqual("folder_1", method.Invoke(null, [1]));
    AssertEqual("folder_2", method.Invoke(null, [2]));
}

static void CustomSignalEditorDefaultsToSnakeCaseName()
{
    var ownerItem = new FolderItemModel
    {
        CustomSignalDefinitions = CustomSignalDefinitionCodec.SerializeDefinitions(
        [
            new CustomSignalDefinition { Name = "signal_1" },
            new CustomSignalDefinition { Name = "signal_2" }
        ])
    };

    var viewModel = new CustomSignalEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null);

    AssertEqual("signal_3", viewModel.Name);
}

static void CustomSignalEditorRejectsUppercaseName()
{
    var viewModel = new CustomSignalEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null)
    {
        Name = "CustomSignal1"
    };

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("snake_case", StringComparison.Ordinal));
}

static void CustomSignalManualTriggerPathUsesLowercaseSuffix()
{
    var method = typeof(CustomSignalsControl).GetMethod("BuildManualTriggerPath", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(string)], null);
    if (method is null)
    {
        throw new InvalidOperationException("BuildManualTriggerPath was not found.");
    }

    AssertEqual("studio.default_layout.custom_signals.signal_1.trigger", method.Invoke(null, ["studio.default_layout.custom_signals.signal_1"]));
}

static void EnhancedSignalEditorDefaultsToSnakeCaseName()
{
    var ownerItem = new FolderItemModel
    {
        EnhancedSignalDefinitions = ExtendedSignalDefinitionCodec.SerializeDefinitions(
        [
            new ExtendedSignalDefinition
            {
                Name = "enhanced_signal_1",
                SourcePath = "studio.default_layout.signal_1"
            }
        ])
    };

    var viewModel = new EnhancedSignalEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null);

    AssertEqual("enhanced_signal_2", viewModel.Name);
}

static void EnhancedSignalEditorRejectsUppercaseName()
{
    var viewModel = new EnhancedSignalEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null)
    {
        Name = "EnhancedSignal1",
        SourcePath = "studio.default_layout.signal_1"
    };

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("snake_case", StringComparison.Ordinal));
}

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

static void TargetPropertyOptionsHideProtectedProperties()
{
    var item = new ItemModel("Device").Repath("runtime.PolicyPicker.Device");
    item.Properties["read"].Value = 1;
    item.Properties["unit"].Value = "V";
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = "runtime.PolicyPicker.Device.Request";
    HostRegistries.Data.UpsertSnapshot("runtime.PolicyPicker.Device", item);

    var method = typeof(MainWindowViewModel).GetMethod("GetTargetPropertyOptions", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetTargetPropertyOptions was not found.");
    }

    var options = ((IEnumerable<string>)method.Invoke(null, ["runtime.PolicyPicker.Device", string.Empty])!)
        .ToArray();

    AssertTrue(options.Contains("read", StringComparer.OrdinalIgnoreCase));
    AssertEqual("read", options[0]);
    AssertTrue(options.Contains("Unit", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("Writable", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("WritePath", StringComparer.OrdinalIgnoreCase));
}

static void TargetPropertyFieldHiddenForNormalWidgets()
{
    var method = typeof(MainWindowViewModel).GetMethod("ShouldShowEditorDialogField", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ShouldShowEditorDialogField was not found.");
    }

    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemModel }, "TargetPropertyPath"])!);
    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.Signal }, "TargetPropertyPath"])!);
    AssertTrue((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemModel }, "TargetPath"])!);
}

static void TargetPropertyDefaultsToValue()
{
    var target = new ItemModel("Device").Repath("runtime.PolicyPicker.DefaultDevice");
    target.Properties["read"].Value = 42;
    target.Properties["unit"].Value = "V";
    HostRegistries.Data.UpsertSnapshot("runtime.PolicyPicker.DefaultDevice", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.PolicyPicker.DefaultDevice";

    AssertEqual("read", item.TargetPropertyPath);
}

static void TargetPropertyProtectedFallbackUsesValue()
{
    var target = new ItemModel("Device").Repath("runtime.PolicyPicker.ProtectedDevice");
    target.Properties["read"].Value = 7;
    target.Properties["write_path"].Value = "runtime.PolicyPicker.ProtectedDevice.Request";
    HostRegistries.Data.UpsertSnapshot("runtime.PolicyPicker.ProtectedDevice", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.PolicyPicker.ProtectedDevice";
    item.TargetPropertyPath = "WritePath";

    AssertEqual("read", item.TargetPropertyPath);
    AssertEqual("read", item.TargetPropertyView.Property?.Name);
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
    var publicPath = "studio.metadata_publish.public_signal";
    var customSignalPath = "studio.metadata_publish.custom_signals.signal1";
    var enhancedSignalPath = "studio.metadata_publish.enhanced_signals.signal1";
    var internalPath = "studio.metadata_publish.broker.status.attach_options";
    HostRegistries.Data.Remove(publicPath);
    HostRegistries.Data.Remove(customSignalPath);
    HostRegistries.Data.Remove(enhancedSignalPath);
    HostRegistries.Data.Remove(internalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(publicPath, new ItemModel("PublicSignal").Repath(publicPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(customSignalPath, new ItemModel("Signal1").Repath(customSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(enhancedSignalPath, new ItemModel("Signal1").Repath(enhancedSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(internalPath, new ItemModel("AttachOptions").Repath(internalPath), DataRegistryItemMetadata.WidgetInternal());

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
    var localPath = "studio.metadata_publish.local_signal";
    var receivedPath = "studio.metadata_publish.broker_widget1.mqtt.device.temperature";
    HostRegistries.Data.Remove(localPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(localPath, new ItemModel("LocalSignal").Repath(localPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(receivedPath, new ItemModel("Temperature").Repath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

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
    var legacyPath = "project.MetadataPublish.DeduplicatedSignal";
    var canonicalPath = "studio.metadata_publish.deduplicated_signal";
    HostRegistries.Data.Remove(legacyPath);
    HostRegistries.Data.Remove(canonicalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(legacyPath, new ItemModel("DeduplicatedSignal").Repath(legacyPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(canonicalPath, new ItemModel("DeduplicatedSignal").Repath(canonicalPath), DataRegistryItemMetadata.PublicData());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.BrokerWidget }])!)
            .Where(path => path.EndsWith(".metadata_publish.deduplicated_signal", StringComparison.OrdinalIgnoreCase))
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
    var internalPath = "studio.registry_visibility.broker_widget1.status.attach_options.broker_widget1.mqtt.edm1.temperature";
    var receivedPath = "studio.registry_visibility.broker_widget1.mqtt.edm1.temperature";
    HostRegistries.Data.Remove(internalPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(internalPath, new ItemModel("Temperature").Repath(internalPath), DataRegistryItemMetadata.WidgetInternal());
        HostRegistries.Data.UpsertSnapshot(receivedPath, new ItemModel("Temperature", 21.5).Repath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

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
    var attachOptionPath = "studio.registry_visibility.broker_widget1.status.attach_options.broker_widget1.mqtt.edm1.temperature";
    HostRegistries.Data.Remove(attachOptionPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(attachOptionPath, new ItemModel("broker_widget1.mqtt.edm1.temperature").Repath(attachOptionPath), DataRegistryItemMetadata.WidgetInternal());

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

        AssertTrue(options.Contains("broker_widget1.mqtt.edm1.temperature", StringComparer.OrdinalIgnoreCase));
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
        BrokerPublishedItemPaths = "studio.default_layout.Edm1.Pressure"
    };
    var definition = new EditorDialogBindingDefinition(
        "BrokerPublishedItemPaths",
        "PublishItems",
        EditorPropertyType.AttachItemList,
        current => current.BrokerPublishedItemPaths,
        optionsFactory: _ =>
        [
            "studio.default_layout.Edm1.Pressure",
            "studio.default_layout.Edm1.Temperature"
        ]);

    var field = definition.CreateField(item);

    AssertEqual(2, field.AttachItemEntries.Count);
    AssertTrue(field.AttachItemEntries.All(static row => !row.IsGroup));
    AssertEqual("pressure", field.AttachItemEntries[0].DisplayName);
    AssertEqual("studio.default_layout.edm1", field.AttachItemEntries[0].DisplaySource);
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
    item.SetHierarchy("default_layout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "shared", "Edm1.Pressure"])!;

    AssertEqual("studio.default_layout.broker_widget1.mqtt.edm1.pressure", path);
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
    item.SetHierarchy("default_layout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "Edm1.Pressure", "broker_widget1.mqtt.edm1.pressure"])!;

    AssertEqual("studio.default_layout.broker_widget1.mqtt.edm1.pressure", path);
    AssertFalse(path.Contains("Edm1.Pressure.BrokerWidget1", StringComparison.OrdinalIgnoreCase));
}

static void BrokerWidgetAttachIdentityCollapsesNestedMqttIdentity()
{
    var method = typeof(BrokerClientControl).GetMethod("BuildBrokerAttachIdentity", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrokerAttachIdentity was not found.");
    }

    var path = (string)method.Invoke(null, ["broker_widget1", "Edm1.Pressure", "broker_widget1.mqtt.edm1.pressure"])!;

    AssertEqual("broker_widget1.mqtt.edm1.pressure", path);
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

    AssertEqual("broker_widget1.mqtt.edm1.pressure", method.Invoke(null, ["Edm1.Pressure.BrokerWidget1.Mqtt.Edm1.Pressure"]));
    AssertEqual("broker_widget1.mqtt.edm1.pressure", method.Invoke(null, ["studio.Folder1.BrokerWidget1.Mqtt.Edm1.Pressure"]));
}

static void BrokerWidgetAttachSelectionNormalizesLegacySharedPath()
{
    var item = new FolderItemModel { Kind = ControlKind.BrokerWidget };
    var definition = new EditorDialogBindingDefinition(
        "BrokerAttachedItemPaths",
        "AttachToUi",
        EditorPropertyType.AttachItemList,
        _ => "runtime.item_broker.BrokerWidget1.shared.Edm1.Pressure",
        optionsFactory: _ => ["broker_widget1.mqtt.edm1.pressure"]);

    var field = definition.CreateField(item);

    AssertEqual(1, field.AttachItemEntries.Count);
    AssertEqual("broker_widget1.mqtt.edm1.pressure", field.AttachItemEntries[0].RelativePath);
    AssertEqual(true, field.AttachItemEntries[0].IsAttached);
    AssertEqual(false, field.AttachItemEntries[0].IsMissing);
}

static void UdlAttachAddNormalizesAndDeduplicatesPaths()
{
    var method = typeof(UdlClientControl).GetMethod("AddAttachedPath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("AddAttachedPath was not found.");
    }

    var updated = (string)method.Invoke(null, ["project.default_layout.ModuleA", "studio.default_layout.ModuleA"])!;
    AssertEqual("studio.default_layout.module_a", updated);

    updated = (string)method.Invoke(null, [updated, "studio.default_layout.ModuleA.SubItem"])!;
    AssertEqual("studio.default_layout.module_a", updated);
}

static void UdlAttachRemoveClearsSelectedPath()
{
    var method = typeof(UdlClientControl).GetMethod("RemoveAttachedPath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("RemoveAttachedPath was not found.");
    }

    var updated = (string)method.Invoke(null, ["studio.default_layout.ModuleA\r\nstudio.default_layout.ModuleB", "project.default_layout.ModuleA"])!;
    AssertEqual("studio.default_layout.module_b", updated);

    updated = (string)method.Invoke(null, [updated, "studio.default_layout.ModuleB"])!;
    AssertEqual(string.Empty, updated);
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

    AssertEqual("studio.folder.x", method.Invoke(null, ["project.Folder.X"]));
    AssertEqual("studio.folder.x", method.Invoke(null, ["studio.Folder.X"]));
    AssertEqual("studio.folder.x", method.Invoke(null, ["studio.project.Folder.X"]));
}

static void BrokerPublishedItemCodecMigratesLegacyPaths()
{
    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions("project.default_layout.Edm1.Pressure");

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalRootPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].BrokerPath);
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
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "studio.Custom.Path",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.custom.path", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "HornetStudio.project.default_layout.Edm1.Pressure",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("hornet_studio.project.default_layout.edm1.pressure", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecRoundtrip()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "studio.project.default_layout.Edm1.Pressure",
            Active = true,
            PublishMode = BrokerPublishedItemPublishModes.Interval,
            PublishIntervalMs = 250,
            Writable = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalPath);
    AssertEqual("studio.default_layout.edm1", parsed[0].LocalRootPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].BrokerPath);
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
            LocalRootPath = "project.default_layout.Edm1",
            LocalPath = "project.default_layout.Edm1.Pressure",
            BrokerPath = "studio.project.default_layout.Edm1.Pressure",
            Active = true
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "project.default_layout.Edm1",
            LocalPath = "project.default_layout.Edm1.Temperature",
            BrokerPath = "studio.project.default_layout.Edm1.Temperature",
            Active = false
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "project.default_layout.Edm2",
            LocalPath = "project.default_layout.Edm2.Pressure",
            BrokerPath = "studio.project.default_layout.Edm2.Pressure",
            Active = true
        }
    };

    var filtered = BrokerPublishedItemDefinitionCodec.GetActiveDefinitionsForRoot(definitions, "project.default_layout.edm1");

    AssertEqual(1, filtered.Count);
    AssertEqual("studio.default_layout.edm1.pressure", filtered[0].LocalPath);
}

static void BrokerPublishedItemChangeMatcherScopesChanges()
{
    var rootDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "studio.default_layout.Edm1",
        BrokerPath = "studio.default_layout.Edm1",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var childDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "studio.default_layout.Edm1.Pressure",
        BrokerPath = "studio.default_layout.Edm1.Pressure",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var rootItem = new ItemModel("Edm1").Repath("studio.default_layout.Edm1");
    rootItem["Pressure"].Value = 12.5;

    ItemModel? Resolve(string path)
    {
        if (string.Equals(path, "studio.default_layout.Edm1", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem;
        }

        if (string.Equals(path, "studio.default_layout.Edm1.Pressure", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem["Pressure"];
        }

        return null;
    }

    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.Edm1.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        rootDefinition,
        new DataChangedEventArgs("studio.default_layout.Edm1.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.Edm1", rootItem, DataChangeKind.SnapshotUpserted),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.Edm1", rootItem, DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.Edm2.Pressure", rootItem["Pressure"], DataChangeKind.ValueUpdated),
        Resolve));
}

static void BrokerPublisherSendsValueUpdateForUnregisteredValueChange()
{
    var localPath = "runtime.BrokerPublisher.set.request";
    var brokerPath = "studio.folder1.udl_client1.m300.set.request";
    HostRegistries.Data.UpsertSnapshot(localPath, new ItemModel("Request", 1).Repath(localPath));

    var widget = new FolderItemModel
    {
        Kind = ControlKind.BrokerWidget,
        Name = "BrokerPublisher",
        BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
        [
            new BrokerPublishedItemDefinition
            {
                LocalRootPath = localPath,
                LocalPath = localPath,
                BrokerPath = brokerPath,
                PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                Active = true,
                Writable = true,
            }
        ])
    };

    var client = new FakeHostItemBrokerClient();
    using var publisher = CreateBrokerPublisher(widget, client);
    StartBrokerPublisher(publisher, publishInitialSnapshots: false);

    AssertTrue(HostRegistries.Data.UpdateValue(localPath, 42));

    AssertEqual(0, client.PublishedSnapshots.Count);
    AssertEqual(1, client.ValueUpdates.Count);
    AssertEqual(brokerPath, client.ValueUpdates[0].Path);
    AssertEqual(42, client.ValueUpdates[0].Value);
}

static void SignalWriteEmitsRegistryValueUpdate()
{
    var targetPath = "studio.editor_tests.signal_write.demo1";
    var target = new ItemModel("Demo1", 80d).Repath(targetPath);
    target.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "Demo1",
        TargetPath = targetPath,
    };

    var valueUpdateCount = 0;
    HostRegistries.Data.ItemChanged += OnItemChanged;
    try
    {
        AssertTrue(signal.TryUpdateTargetPropertyValue(10d, out var error));
        AssertEqual(string.Empty, error);
        AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var resolved));
        AssertEqual(10d, resolved?.Value);
        AssertEqual(1, valueUpdateCount);
    }
    finally
    {
        HostRegistries.Data.ItemChanged -= OnItemChanged;
    }

    void OnItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.ValueUpdated
            && string.Equals(e.Key, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            valueUpdateCount++;
        }
    }
}

static void BrokerWriteBackIgnoresNonWritableEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "runtime.BrokerWriteBack.NonWritable",
        "studio.runtime.BrokerWriteBack.NonWritable",
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
        "runtime.BrokerWriteBack.Inactive",
        "studio.runtime.BrokerWriteBack.Inactive",
        active: false,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackUpdatesWritableValue()
{
    var localPath = "runtime.BrokerWriteBack.Value";
    var brokerPath = "studio.runtime.broker_write_back.value";
    HostRegistries.Data.UpsertSnapshot(localPath, new ItemModel("read", 1).Repath(localPath));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
}

static void BrokerWriteBackUsesRequestWriteTarget()
{
    var localPath = "runtime.BrokerWriteBack.RequestValue";
    var brokerPath = "studio.runtime.broker_write_back.request_value";
    var item = new ItemModel("RequestValue", 1).Repath(localPath);
    item.AddItem("Request");
    item["Request"].Value = 1;
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = localPath;
    item.Properties["write_mode"].Value = SignalWriteMode.Request.ToString();
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(1, resolved?.Value);
    AssertTrue(HostRegistries.Data.TryResolve($"{localPath}.Request", out var request));
    AssertEqual(42, request?.Value);
}

static void BrokerWriteBackConvertsNumericValueToLocalType()
{
    var localPath = "runtime.BrokerWriteBack.DoubleValue";
    var brokerPath = "studio.runtime.broker_write_back.double_value";
    HostRegistries.Data.UpsertSnapshot(localPath, new ItemModel("DoubleValue", 1.5).Repath(localPath));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 2L, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void BrokerWriteBackBlocksProtectedProperties()
{
    var localPath = "runtime.BrokerWriteBack.ProtectedParameter";
    var brokerPath = "studio.runtime.BrokerWriteBack.ProtectedParameter";
    var item = new ItemModel("ProtectedParameter", 1).Repath(localPath);
    item.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemPropertyChangedMessage(brokerPath, "Writable", false, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(true, resolved?.Properties["writable"].Value);
}

static void BrokerWriteBackIgnoresSameValueSelfEchoes()
{
    var localPath = "runtime.BrokerWriteBack.SameValue";
    var brokerPath = "studio.runtime.BrokerWriteBack.SameValue";
    HostRegistries.Data.UpsertSnapshot(localPath, new ItemModel("OwnSource", 1).Repath(localPath));

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
        "runtime.BrokerWriteBack.Cleanup",
        "studio.runtime.BrokerWriteBack.Cleanup",
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

static IDisposable CreateBrokerPublisher(FolderItemModel item, IHostItemBrokerClient client)
{
    var publisherType = typeof(BrokerClientControl).GetNestedType("HostItemBrokerPublisher", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Broker publisher type was not found.");
    var constructor = publisherType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(FolderItemModel), typeof(IHostItemBrokerClient)],
        modifiers: null)
        ?? throw new InvalidOperationException("Broker publisher constructor was not found.");

    return (IDisposable)constructor.Invoke([item, client]);
}

static void StartBrokerPublisher(IDisposable publisher, bool publishInitialSnapshots)
{
    var method = publisher.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Broker publisher start method was not found.");
    method.Invoke(publisher, [publishInitialSnapshots]);
}

static void ItemExposurePublisherAppliesBitHelpers()
{
    var item = new ItemModel("mask", 5, "runtime.item_broker.broker1.client1.device");
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

    AssertEqual("b4", item.Properties["format"].Value);
    AssertEqual("flags", item.Properties["unit"].Value);
    AssertTrue(item.Has("Bits"));
    AssertTrue(item["Bits"].Has("Bit0"));
    AssertTrue(item["Bits"].Has("Bit2"));
    AssertEqual(true, item["Bits"]["Bit0"].Value);
    AssertEqual(false, item["Bits"]["Bit1"].Value);
    AssertEqual(true, item["Bits"]["Bit2"].Value);
    AssertEqual("Ready", item["Bits"]["Bit0"].Properties["title"].Value);
    AssertEqual("Fault", item["Bits"]["Bit2"].Properties["title"].Value);
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
        "runtime.item_broker.broker1.client1.device.mask",
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
    private Action<string>? _diagnostic;
    private Action? _itemsChanged;

    public string ClientIdValue { get; set; } = "broker-widget-test";

    public string Name => "BrokerWidgetTest";

    public string Host => "127.0.0.1";

    public int Port => 1883;

    public string BaseTopic => "hornet";

    public string ClientId => ClientIdValue;

    public bool IsConnected => true;

    public ItemDictionary Items { get; } = new("runtime.item_broker.BrokerWidgetTest");

    public List<FakeItemSubscription> Subscriptions { get; } = [];

    public List<ItemModel> PublishedSnapshots { get; } = [];

    public List<ItemModel> ValueUpdates { get; } = [];

    public List<(ItemModel ItemModel, string ParameterName)> ParameterUpdates { get; } = [];

    public event Action<string>? Diagnostic
    {
        add => _diagnostic += value;
        remove => _diagnostic -= value;
    }

    public event Action? ItemsChanged
    {
        add => _itemsChanged += value;
        remove => _itemsChanged -= value;
    }

    public IReadOnlyDictionary<string, ItemModel> GetItemSnapshots() => new Dictionary<string, ItemModel>();

    public Task PublishSnapshotAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        PublishedSnapshots.Add(item.Clone());
        return Task.CompletedTask;
    }

    public Task<ItemServerAckMessage> UpdateValueAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        ValueUpdates.Add(item.Clone());
        return Task.FromResult(CreateAcknowledgement(item));
    }

    public Task<ItemServerAckMessage> UpdateParameterAsync(ItemModel item, string parameterName, CancellationToken cancellationToken = default)
    {
        ParameterUpdates.Add((item.Clone(), parameterName));
        return Task.FromResult(CreateAcknowledgement(item));
    }

    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
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

    public void PublishToSubscription(ItemServerMessage message)
    {
        foreach (var subscription in Subscriptions.Where(subscription => string.Equals(subscription.Path, message.Path, StringComparison.OrdinalIgnoreCase)))
        {
            subscription.HandleAsync(message).GetAwaiter().GetResult();
        }
    }

    private ItemServerAckMessage CreateAcknowledgement(ItemModel item)
        => new(
            Path: item.Path ?? string.Empty,
            Accepted: true,
            Reason: null,
            SourceClientId: ClientId,
            CorrelationId: null,
            Timestamp: DateTimeOffset.UtcNow);
}

sealed class FakeItemSubscription : IItemSubscription
{
    private readonly Func<ItemServerMessage, CancellationToken, Task> _handler;

    public FakeItemSubscription(string path, bool recursive, Func<ItemServerMessage, CancellationToken, Task> handler)
    {
        Path = path;
        Recursive = recursive;
        _handler = handler;
    }

    public string SubscriptionId { get; } = Guid.NewGuid().ToString("N");

    public IItemServerClient Client { get; } = new FakeItemBrokerClient();

    public string Path { get; }

    public bool Recursive { get; }

    public bool Disposed { get; private set; }

    public Task HandleAsync(ItemServerMessage message)
        => _handler(message, CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

sealed class FakeItemBrokerClient : IItemServerClient
{
    public string ClientId => "fake";

    public Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
