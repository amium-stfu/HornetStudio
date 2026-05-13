using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Client;
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
    ("Enhanced signal runtime path uses snake_case segments", EnhancedSignalRuntimePathUsesSnakeCaseSegments),
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
    ("UDL received rows stay visible when attached", UdlReceivedRowsStayVisibleWhenAttached),
    ("UDL attached items resolve via runtime registry", UdlAttachedItemsResolveViaRuntimeRegistry),
    ("UDL set-driven demo writes feedback to read only", UdlSetDrivenDemoWritesFeedbackToReadOnly),
    ("UDL runtime channels include registry items", UdlRuntimeChannelsIncludeRegistryItems),
    ("UDL runtime exposure bits use snake_case paths", UdlRuntimeExposureBitsUseSnakeCasePaths),
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
    ("Broker write-back normalizes legacy request mode", BrokerWriteBackNormalizesLegacyRequestMode),
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
    ("Signal write updates write property when present", SignalWriteUpdatesWritePropertyWhenPresent),
    ("Signal source options include descendants and skip status roots", SignalSourceOptionsIncludeDescendantsAndSkipStatusRoots),
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

static void EnhancedSignalRuntimePathUsesSnakeCaseSegments()
{
    var definition = new ExtendedSignalDefinition
    {
        Name = "enhanced_signal_1",
        SourcePath = "studio.default_layout.signal_1"
    };

    AssertEqual(
        "studio.default_layout.enhanced_signals.enhanced_signal_1",
        EnhancedSignalRuntime.BuildRegistryPath("default_layout", definition));
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
    var item = ItemExtension.CreateWithPath("runtime.policy_picker.device");
    item.Properties["read"].Value = 1;
    item.Properties["unit"].Value = "V";
    item.Properties["write"].Value = 1;
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = "runtime.policy_picker.device.request";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.device", item);

    var method = typeof(MainWindowViewModel).GetMethod("GetTargetPropertyOptions", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetTargetPropertyOptions was not found.");
    }

    var options = ((IEnumerable<string>)method.Invoke(null, ["runtime.policy_picker.device", string.Empty])!)
        .ToArray();

    AssertTrue(options.Contains("read", StringComparer.OrdinalIgnoreCase));
    AssertEqual("read", options[0]);
    AssertTrue(options.Contains("Unit", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("write", StringComparer.OrdinalIgnoreCase));
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
    var target = ItemExtension.CreateWithPath("runtime.policy_picker.default_device");
    target.Properties["read"].Value = 42;
    target.Properties["unit"].Value = "V";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.default_device", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.policy_picker.default_device";

    AssertEqual("read", item.TargetPropertyPath);
}

static void TargetPropertyProtectedFallbackUsesValue()
{
    var target = ItemExtension.CreateWithPath("runtime.policy_picker.protected_device");
    target.Properties["read"].Value = 7;
    target.Properties["write_path"].Value = "runtime.policy_picker.protected_device.request";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.protected_device", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.policy_picker.protected_device";
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
        HostRegistries.Data.UpsertSnapshot(publicPath, ItemExtension.CreateWithPath(publicPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(customSignalPath, ItemExtension.CreateWithPath(customSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(enhancedSignalPath, ItemExtension.CreateWithPath(enhancedSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(internalPath, ItemExtension.CreateWithPath(internalPath), DataRegistryItemMetadata.WidgetInternal());

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
        HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(receivedPath, ItemExtension.CreateWithPath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

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
    var legacyPath = "project.metadata_publish.deduplicated_signal";
    var canonicalPath = "studio.metadata_publish.deduplicated_signal";
    HostRegistries.Data.Remove(legacyPath);
    HostRegistries.Data.Remove(canonicalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(legacyPath, ItemExtension.CreateWithPath(legacyPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(canonicalPath, ItemExtension.CreateWithPath(canonicalPath), DataRegistryItemMetadata.PublicData());

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
        HostRegistries.Data.UpsertSnapshot(internalPath, ItemExtension.CreateWithPath(internalPath), DataRegistryItemMetadata.WidgetInternal());
        HostRegistries.Data.UpsertSnapshot(receivedPath, ItemExtension.CreateWithPath(receivedPath, 21.5), DataRegistryItemMetadata.BrokerReceivedData());

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
        HostRegistries.Data.UpsertSnapshot(attachOptionPath, ItemExtension.CreateWithPath(attachOptionPath), DataRegistryItemMetadata.WidgetInternal());

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

static void UdlReceivedRowsStayVisibleWhenAttached()
{
    var method = typeof(UdlClientControl).GetMethod("BuildReceivedAttachSectionRows", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedAttachSectionRows was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);
    var detachedRows = (IReadOnlyList<UdlClientAttachSectionRow>)method.Invoke(null, [ownerItem, new[] { "m310" }, new HashSet<string>(StringComparer.OrdinalIgnoreCase)])!;

    AssertEqual(1, detachedRows.Count);
    AssertEqual("m310", detachedRows[0].RelativePath);
    AssertEqual("Attach", detachedRows[0].ActionText);
    AssertEqual(true, detachedRows[0].CanExecuteAction);

    var attachedRows = (IReadOnlyList<UdlClientAttachSectionRow>)method.Invoke(null, [ownerItem, new[] { "m310" }, new HashSet<string>(["m310"], StringComparer.OrdinalIgnoreCase)])!;

    AssertEqual(1, attachedRows.Count);
    AssertEqual("m310", attachedRows[0].RelativePath);
    AssertEqual("Attached", attachedRows[0].ActionText);
    AssertEqual(false, attachedRows[0].CanExecuteAction);
}

static void UdlAttachedItemsResolveViaRuntimeRegistry()
{
    var method = typeof(UdlClientControl).GetMethod("ResolveRuntimeItemFromSources", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ResolveRuntimeItemFromSources was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);

    var runtimePath = "runtime.udl_client.udl_client_control.m310";
    HostRegistries.Data.Remove(runtimePath);

    try
    {
        var runtimeItem = ItemExtension.CreateWithPath(runtimePath);
        HostRegistries.Data.UpsertSnapshot(runtimePath, runtimeItem, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        var resolved = (ItemModel?)method.Invoke(null, [ownerItem, Array.Empty<ItemModel>(), "m310"]);
        AssertTrue(resolved is not null);
        AssertEqual(runtimePath, resolved!.Path);
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
    }
}

static void UdlSetDrivenDemoWritesFeedbackToReadOnly()
{
    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0,
        SetScale = 1,
        SetOffset = 0,
        SetTauSeconds = 0
    };

    using var client = new SimulatedHostUdlClient(
        name: "udl_client_control",
        host: "demo",
        port: 9001,
        definitions: [definition]);

    client.ConnectAsync().GetAwaiter().GetResult();
    try
    {
        var module = client.Items["m001"];
        module["set"].Properties["write"].Value = 20d;

        Thread.Sleep(250);

        AssertEqual(20d, module["read"].Properties["read"].Value);
        AssertEqual(20d, module["set"].Properties["write"].Value);
        AssertEqual(20d, module["set"].Properties["read"].Value);
    }
    finally
    {
        client.DisconnectAsync().GetAwaiter().GetResult();
    }
}

static void UdlRuntimeChannelsIncludeRegistryItems()
{
    var method = typeof(UdlClientControl).GetMethod("BuildRuntimeChannelDescriptors", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildRuntimeChannelDescriptors was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);

    var runtimeChannelPath = "runtime.udl_client.udl_client_control.m310.read";
    HostRegistries.Data.Remove("runtime.udl_client.udl_client_control.m310");

    try
    {
        var runtimeChannel = ItemExtension.CreateWithPath(runtimeChannelPath);
        runtimeChannel.Properties["format"].Value = "b16";
        runtimeChannel.Properties["unit"].Value = "raw";
        HostRegistries.Data.UpsertSnapshot(runtimeChannelPath, runtimeChannel, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        var descriptors = ((IReadOnlyList<UdlRuntimeModuleChannelDescriptor>)method.Invoke(null, [ownerItem, Array.Empty<ItemModel>()])!).ToArray();
        var descriptor = descriptors.FirstOrDefault(candidate => string.Equals(candidate.ModuleName, "m310", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ChannelName, "read", StringComparison.OrdinalIgnoreCase));

        AssertTrue(descriptor is not null);
        AssertEqual("b16", descriptor!.Format);
        AssertEqual("raw", descriptor.Unit);
        AssertEqual(16, descriptor.BitCount);
    }
    finally
    {
        HostRegistries.Data.Remove("runtime.udl_client.udl_client_control.m310");
    }
}

static void UdlRuntimeExposureBitsUseSnakeCasePaths()
{
    var method = typeof(UdlClientControl).GetMethod("UpsertRuntimeExposureBits", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("UpsertRuntimeExposureBits was not found.");
    }

    var control = (UdlClientControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UdlClientControl));
    var runtimeChannel = new ItemModel("read", path: "runtime.udl_client.udl_client_control.m002");
    runtimeChannel.Properties["read"].Value = 5;
    runtimeChannel.Properties["format"].Value = "b4";

    var definition = new UdlModuleExposureDefinition
    {
        ModuleName = "m002",
        ChannelName = "read",
        ExposeBits = true,
        BitCount = 4,
        BitLabels = "Bit0=Ready\nBit2=Fault"
    };

    method.Invoke(control, [runtimeChannel, definition, 4]);

    AssertTrue(runtimeChannel.Has("bits"));
    AssertTrue(runtimeChannel["bits"].Has("bit0"));
    AssertTrue(runtimeChannel.GetDictionary().ContainsKey("bits"));
    AssertTrue(runtimeChannel["bits"].GetDictionary().ContainsKey("bit0"));
    AssertEqual(true, runtimeChannel["bits"]["bit0"].Value);
    AssertEqual(false, runtimeChannel["bits"]["bit1"].Value);
    AssertEqual(true, runtimeChannel["bits"]["bit2"].Value);
    AssertEqual("Ready", runtimeChannel["bits"]["bit0"].Properties["title"].Value);
    AssertEqual("m002", runtimeChannel["bits"]["bit0"].Properties["module_name"].Value);
    AssertEqual("read", runtimeChannel["bits"]["bit0"].Properties["channel_name"].Value);
    AssertEqual(0, runtimeChannel["bits"]["bit0"].Properties["bit_index"].Value);
}

static void SignalSourceOptionsIncludeDescendantsAndSkipStatusRoots()
{
    var attachedRootPath = "studio.default_layout.udl_client_control.m310";
    var statusRootPath = "studio.default_layout.udl_client_control.status";
    HostRegistries.Data.Remove(attachedRootPath);
    HostRegistries.Data.Remove(statusRootPath);

    try
    {
        var attachedRoot = ItemExtension.CreateWithPath(attachedRootPath);
        attachedRoot["read"].Value = 1;
        attachedRoot["read"]["bits"]["bit0"].Value = true;

        var statusRoot = ItemExtension.CreateWithPath(statusRootPath);
        statusRoot["connection"].Value = "Connected";

        HostRegistries.Data.UpsertSnapshot(attachedRootPath, attachedRoot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        HostRegistries.Data.UpsertSnapshot(statusRootPath, statusRoot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);

        var method = typeof(MainWindowViewModel).GetMethod("EnumerateSignalSourceOptions", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("EnumerateSignalSourceOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [])!).ToArray();

        AssertTrue(options.Contains(attachedRootPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains("studio.default_layout.udl_client_control.m310.read", StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains("studio.default_layout.udl_client_control.m310.read.bits.bit0", StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(statusRootPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains("studio.default_layout.udl_client_control.status.connection", StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(attachedRootPath);
        HostRegistries.Data.Remove(statusRootPath);
    }
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
        LocalPath = "studio.default_layout.edm1",
        BrokerPath = "studio.default_layout.edm1",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var childDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "studio.default_layout.edm1.pressure",
        BrokerPath = "studio.default_layout.edm1.pressure",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var rootItem = ItemExtension.CreateWithPath("studio.default_layout.edm1");
    rootItem["pressure"].Value = 12.5;

    ItemModel? Resolve(string path)
    {
        if (string.Equals(path, "studio.default_layout.edm1", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem;
        }

        if (string.Equals(path, "studio.default_layout.edm1.pressure", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem["pressure"];
        }

        return null;
    }

    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        rootDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1", rootItem, DataChangeKind.SnapshotUpserted),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1", rootItem, DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm2.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
}

static void BrokerPublisherSendsValueUpdateForUnregisteredValueChange()
{
    var localPath = "runtime.broker_publisher.set.request";
    var brokerPath = "studio.folder1.udl_client1.m300.set.request";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

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
    var target = ItemExtension.CreateWithPath(targetPath, 80d);
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

static void SignalWriteUpdatesWritePropertyWhenPresent()
{
    var targetPath = "studio.editor_tests.signal_write.demo_write_property";
    var target = ItemExtension.CreateWithPath(targetPath, 80d);
    target.Properties["read"].Value = 80d;
    target.Properties["write"].Value = 80d;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "DemoWriteProperty",
        TargetPath = targetPath,
    };

    AssertEqual("read", signal.TargetPropertyPath);
    AssertTrue(signal.TryUpdateTargetPropertyValue(10d, out var error));
    AssertEqual(string.Empty, error);
    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var resolved));
    AssertEqual(80d, resolved?.Value);
    AssertEqual(80d, resolved?.Properties["read"].Value);
    AssertEqual(10d, resolved?.Properties["write"].Value);
}

static void BrokerWriteBackIgnoresNonWritableEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "runtime.broker_write_back.non_writable",
        "studio.runtime.broker_write_back.non_writable",
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
        "runtime.broker_write_back.inactive",
        "studio.runtime.broker_write_back.inactive",
        active: false,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackUpdatesWritableValue()
{
    var localPath = "runtime.broker_write_back.value";
    var brokerPath = "studio.runtime.broker_write_back.value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
}

static void BrokerWriteBackNormalizesLegacyRequestMode()
{
    var localPath = "runtime.broker_write_back.request_value";
    var brokerPath = "studio.runtime.broker_write_back.request_value";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.AddItem("request");
    item["request"].Value = 1;
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = localPath;
    item.Properties["write_mode"].Value = SignalWriteMode.Request.ToString();
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
    AssertTrue(HostRegistries.Data.TryResolve($"{localPath}.request", out var request));
    AssertEqual(1, request?.Value);
}

static void BrokerWriteBackConvertsNumericValueToLocalType()
{
    var localPath = "runtime.broker_write_back.double_value";
    var brokerPath = "studio.runtime.broker_write_back.double_value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1.5));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 2L, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void BrokerWriteBackBlocksProtectedProperties()
{
    var localPath = "runtime.broker_write_back.protected_parameter";
    var brokerPath = "studio.runtime.broker_write_back.protected_parameter";
    var item = ItemExtension.CreateWithPath(localPath, 1);
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
    var localPath = "runtime.broker_write_back.same_value";
    var brokerPath = "studio.runtime.broker_write_back.same_value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

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
        "runtime.broker_write_back.cleanup",
        "studio.runtime.broker_write_back.cleanup",
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
    AssertTrue(item.Has("bits"));
    AssertTrue(item["bits"].Has("bit0"));
    AssertTrue(item["bits"].Has("bit2"));
    AssertEqual(true, item["bits"]["bit0"].Value);
    AssertEqual(false, item["bits"]["bit1"].Value);
    AssertEqual(true, item["bits"]["bit2"].Value);
    AssertEqual("Ready", item["bits"]["bit0"].Properties["title"].Value);
    AssertEqual("Fault", item["bits"]["bit2"].Properties["title"].Value);
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

    public Task PublishReadAsync(
        ItemModel item,
        bool publishEpoch = true,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        ValueUpdates.Add(item.Clone());
        return Task.CompletedTask;
    }

    public Task PublishPropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        ParameterUpdates.Add((item.Clone(), parameterName));
        return Task.CompletedTask;
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
