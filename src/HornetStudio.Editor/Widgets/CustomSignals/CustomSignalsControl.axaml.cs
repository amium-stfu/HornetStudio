using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class CustomSignalsControl : EditorTemplateControl
{
    public static readonly DirectProperty<CustomSignalsControl, bool> HasNoSignalsProperty =
        AvaloniaProperty.RegisterDirect<CustomSignalsControl, bool>(nameof(HasNoSignals), control => control.HasNoSignals);

    private FolderItemModel? _observedItem;
    private bool _isPublishing;
    private bool _hasNoSignals = true;
    private HashSet<string> _publishedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastComputedPublishTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _manualTriggerPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingManualEvaluations = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<CustomSignalRow> Signals { get; } = [];

    public bool HasNoSignals
    {
        get => _hasNoSignals;
        private set => SetAndRaise(HasNoSignalsProperty, ref _hasNoSignals, value);
    }

    private FolderItemModel? ItemModel => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    public CustomSignalsControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        Signals.CollectionChanged += OnSignalsCollectionChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
        RebuildSignalRows();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        UnhookObservedItem();
        RemovePublishedSignals();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RebuildSignalRows();
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, ItemModel))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = ItemModel;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
        }
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is null)
        {
            return;
        }

        _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        _observedItem = null;
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.CustomSignalDefinitions)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.Path)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            RebuildSignalRows();
            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            foreach (var row in Signals)
            {
                row.RefreshTheme();
            }
        }
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (_isPublishing)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnRegistryItemChanged(sender, e));
            return;
        }

        if (_manualTriggerPaths.TryGetValue(e.Key, out var manualRegistryPath))
        {
            HandleManualTriggerChange(e.Key, manualRegistryPath);
            return;
        }

        if (_publishedPaths.Contains(e.Key))
        {
            UpdateRowsFromRegistry();
            return;
        }

        if (Signals.Any(row => row.Definition.Mode == CustomSignalMode.Computed && row.Definition.Trigger == CustomSignalComputationTrigger.OnSourceChange))
        {
            PublishSignals(preserveInputValues: true);
        }
    }

    private void OnSignalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var row in e.NewItems.OfType<CustomSignalRow>())
            {
                row.RefreshTheme();
            }
        }

        HasNoSignals = Signals.Count == 0;
    }

    private void RebuildSignalRows()
    {
        var item = _observedItem;
        if (item is null)
        {
            Signals.Clear();
            RemovePublishedSignals();
            return;
        }

        var definitions = CustomSignalDefinitionCodec.ParseDefinitions(item.CustomSignalDefinitions);
        Signals.Clear();
        foreach (var definition in definitions)
        {
            Signals.Add(new CustomSignalRow(item, definition.Clone(), BuildRegistryPath(item, definition)));
        }

        UpdateFooter(item, definitions.Count);
        PublishSignals(preserveInputValues: true);
    }

    private void ApplyCustomSignalDefinitions(FolderItemModel ownerItem, string rawDefinitions, bool queuePersist)
    {
        ownerItem.CustomSignalDefinitions = rawDefinitions;

        if (!queuePersist)
        {
            return;
        }

        if (ViewModel is { } viewModel && !viewModel.TrySaveOwningPageYaml(ownerItem, out _))
        {
            viewModel.QueueSaveOwningPageYaml(ownerItem);
        }
    }

    private void PublishSignals(bool preserveInputValues)
    {
        var item = _observedItem;
        if (item is null)
        {
            return;
        }

        var definitions = Signals.Select(row => row.Definition).ToArray();
        var nextSignalPaths = definitions
            .Select(definition => BuildRegistryPath(item, definition))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextManualTriggerPaths = definitions
            .Where(static definition => definition.Mode == CustomSignalMode.Computed && definition.Trigger == CustomSignalComputationTrigger.Manual)
            .Select(definition => BuildManualTriggerPath(item, definition))
            .ToDictionary(path => path, path => path[..path.LastIndexOf('.')], StringComparer.OrdinalIgnoreCase);
        var nextPaths = nextSignalPaths
            .Concat(nextManualTriggerPaths.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stalePath in _publishedPaths.Where(path => !nextPaths.Contains(path)).ToArray())
        {
            HostRegistries.Data.Remove(stalePath);
            _lastComputedPublishTimes.Remove(stalePath);
            _manualTriggerPaths.Remove(stalePath);
            _pendingManualEvaluations.Remove(stalePath);
        }

        _publishedPaths = nextPaths;
        _manualTriggerPaths.Clear();
        foreach (var entry in nextManualTriggerPaths)
        {
            _manualTriggerPaths[entry.Key] = entry.Value;
        }

        _isPublishing = true;
        try
        {
            foreach (var row in Signals)
            {
                var value = EvaluateValue(item, row.Definition, row.RegistryPath, preserveInputValues);
                PublishSignalSnapshot(item, row.Definition, row.RegistryPath, value);
                PublishManualTriggerSnapshot(item, row.Definition, row.RegistryPath);
                row.CurrentValue = value;
            }
        }
        finally
        {
            _isPublishing = false;
        }
    }

    private void PublishSignalSnapshot(FolderItemModel ownerItem, CustomSignalDefinition definition, string registryPath, object? value)
    {
        var segments = TargetPathHelper.SplitPathSegments(registryPath);
        var name = segments.LastOrDefault() ?? definition.Name;
        var parentPath = segments.Count > 1 ? string.Join('.', segments.Take(segments.Count - 1)) : null;

        var item = new ItemModel(name, value, parentPath);
        item.Properties["kind"].Value = "CustomSignal";
        item.Properties["title"].Value = definition.Name;
        item.Properties["text"].Value = definition.Name;
        item.Properties["unit"].Value = definition.Unit;
        item.Properties["format"].Value = definition.Format;
        item.Properties["mode"].Value = definition.Mode.ToString();
        item.Properties["writable"].Value = definition.Mode == CustomSignalMode.Input && definition.IsWritable;
        item.Properties["write_path"].Value = definition.Mode == CustomSignalMode.Input ? definition.WritePath : string.Empty;
        item.Properties["write_mode"].Value = definition.WriteMode.ToString();
        item.Properties["owner"].Value = ownerItem.Name ?? string.Empty;
        item.Properties["value"].Value = value ?? string.Empty;
        HostRegistries.Data.UpsertSnapshot(registryPath, item, DataRegistryItemMetadata.PublicData());
    }

    private void PublishManualTriggerSnapshot(FolderItemModel ownerItem, CustomSignalDefinition definition, string registryPath)
    {
        if (definition.Mode != CustomSignalMode.Computed || definition.Trigger != CustomSignalComputationTrigger.Manual)
        {
            return;
        }

        var triggerPath = BuildManualTriggerPath(registryPath);
        var item = new ItemModel("trigger", false, registryPath);
        item.Properties["kind"].Value = "CustomSignalManualTrigger";
        item.Properties["title"].Value = $"{definition.Name} Trigger";
        item.Properties["text"].Value = "Trigger";
        item.Properties["mode"].Value = definition.Mode.ToString();
        item.Properties["writable"].Value = true;
        item.Properties["owner"].Value = ownerItem.Name ?? string.Empty;
        item.Properties["value"].Value = false;
        HostRegistries.Data.UpsertSnapshot(triggerPath, item, DataRegistryItemMetadata.PublicCommand());
    }

    private object? EvaluateValue(FolderItemModel ownerItem, CustomSignalDefinition definition, string registryPath, bool preserveInputValues)
    {
        if (definition.Mode == CustomSignalMode.Input)
        {
            if (preserveInputValues && HostRegistries.Data.TryGet(registryPath, out var existing) && existing is not null)
            {
                return ConvertToDataType(existing.Properties.Has("value") ? existing.Properties["value"].Value : existing.Value, definition.DataType);
            }

            return ParseLiteral(definition.ValueText, definition.DataType);
        }

        if (!ShouldEvaluateComputed(definition, registryPath))
        {
            if (HostRegistries.Data.TryGet(registryPath, out var existing) && existing is not null)
            {
                return ConvertToDataType(existing.Properties.Has("value") ? existing.Properties["value"].Value : existing.Value, definition.DataType);
            }

            return definition.DataType == CustomSignalDataType.Boolean ? false : 0d;
        }

        var value = EvaluateComputedValue(ownerItem, definition);
        _lastComputedPublishTimes[registryPath] = DateTimeOffset.UtcNow;
        return value;
    }

    private object? EvaluateComputedValue(FolderItemModel ownerItem, CustomSignalDefinition definition)
    {
        if (CustomSignalFormulaEngine.TryEvaluate(definition, variableName => ResolveVariableValue(ownerItem, definition, variableName), out var value, out _))
        {
            return value;
        }

        return definition.DataType switch
        {
            CustomSignalDataType.Boolean => false,
            CustomSignalDataType.Text => string.Empty,
            _ => 0d
        };
    }

    private bool ShouldEvaluateComputed(CustomSignalDefinition definition, string registryPath)
    {
        if (definition.Trigger == CustomSignalComputationTrigger.Manual)
        {
            return _pendingManualEvaluations.Remove(registryPath);
        }

        if (definition.Trigger != CustomSignalComputationTrigger.Timer)
        {
            return true;
        }

        var interval = Math.Max(1, definition.TriggerIntervalSeconds);
        if (!_lastComputedPublishTimes.TryGetValue(registryPath, out var lastPublish))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastPublish >= TimeSpan.FromSeconds(interval);
    }

    private object? ResolveVariableValue(FolderItemModel ownerItem, CustomSignalDefinition definition, string variableName)
    {
        var variable = definition.Variables.FirstOrDefault(candidate => string.Equals(candidate.Name, variableName, StringComparison.OrdinalIgnoreCase));
        if (variable is not null && !string.IsNullOrWhiteSpace(variable.SourcePath))
        {
            return ResolveSourceValue(ownerItem, variable.SourcePath);
        }

        return variableName.ToUpperInvariant() switch
        {
            "A" => ResolveSourceValue(ownerItem, definition.SourcePath),
            "B" => ResolveSourceValue(ownerItem, definition.SourcePath2),
            "C" => ResolveSourceValue(ownerItem, definition.SourcePath3),
            _ => null
        };
    }

    private object? ResolveSourceValue(FolderItemModel ownerItem, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(sourcePath, ownerItem.FolderName))
        {
            if (!HostRegistries.Data.TryGet(candidate, out var item) || item is null)
            {
                continue;
            }

            return item.Properties.Has("value") ? item.Properties["value"].Value : item.Value;
        }

        return null;
    }

    private void HandleManualTriggerChange(string triggerPath, string registryPath)
    {
        if (!HostRegistries.Data.TryGet(triggerPath, out var triggerItem) || triggerItem is null)
        {
            return;
        }

        var shouldTrigger = ToBool(triggerItem.Properties.Has("value") ? triggerItem.Properties["value"].Value : triggerItem.Value);
        if (!shouldTrigger)
        {
            return;
        }

        _pendingManualEvaluations.Add(registryPath);
        PublishSignals(preserveInputValues: true);

        _isPublishing = true;
        try
        {
            HostRegistries.Data.UpdateValue(triggerPath, false);
        }
        finally
        {
            _isPublishing = false;
        }
    }

    private void UpdateRowsFromRegistry()
    {
        foreach (var row in Signals)
        {
            if (!HostRegistries.Data.TryGet(row.RegistryPath, out var item) || item is null)
            {
                continue;
            }

            row.CurrentValue = item.Properties.Has("value") ? item.Properties["value"].Value : item.Value;
        }
    }

    private void RemovePublishedSignals()
    {
        foreach (var path in _publishedPaths)
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedPaths.Clear();
    }

    private async void OnAddSignalClicked(object? sender, RoutedEventArgs e)
    {
        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || viewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var definition = await CustomSignalEditorDialogWindow.ShowAsync(owner, viewModel, ownerItem, null, GetSourceOptions());
        if (definition is null)
        {
            return;
        }

        if (Signals.Any(row => string.Equals(row.Definition.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions).ToList();
        definitions.Add(definition);
        ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        e.Handled = true;
    }

    private async void OnEditSignalClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomSignalRow row })
        {
            return;
        }

        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || viewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var updated = await CustomSignalEditorDialogWindow.ShowAsync(owner, viewModel, ownerItem, row.Definition, GetSourceOptions());
        if (updated is null)
        {
            return;
        }

        if (Signals.Any(candidate => !ReferenceEquals(candidate, row) && string.Equals(candidate.Definition.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions).ToList();
        var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            definitions[index] = updated;
            ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }

        e.Handled = true;
    }

    private async void OnDeleteSignalClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomSignalRow row })
        {
            return;
        }

        var ownerItem = _observedItem;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, $"Delete signal '{row.Name}'?", "The custom signal definition will be removed.", confirmText: "Delete", cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions)
            .Where(definition => !string.Equals(definition.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        e.Handled = true;
    }

    private async void OnSetValueClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomSignalRow row })
        {
            return;
        }

        if (!row.CanEditValue || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        object? nextValue = row.Definition.DataType switch
        {
            CustomSignalDataType.Number => await EditNumericValueAsync(owner, row),
            CustomSignalDataType.Boolean => !ToBool(row.CurrentValue),
            _ => await EditorInputDialogs.EditTextAsync(owner, row.Name, row.RegistryPath, row.CurrentValue?.ToString() ?? string.Empty)
        };

        if (nextValue is null && row.Definition.DataType != CustomSignalDataType.Text)
        {
            return;
        }

        _isPublishing = true;
        try
        {
            var convertedValue = ConvertToDataType(nextValue, row.Definition.DataType);
            var configuredWritePath = string.IsNullOrWhiteSpace(row.Definition.WritePath)
                ? row.RegistryPath
                : row.Definition.WritePath.Trim();

            if (TryResolveWriteTarget(configuredWritePath, _observedItem?.FolderName, out var writeTarget))
            {
                var writeTargetPath = writeTarget.Path ?? configuredWritePath;
                if (writeTarget.Properties.Has("write"))
                {
                    HostRegistries.Data.TryUpdateUserProperty(writeTargetPath, "write", convertedValue);
                }
                else
                {
                    HostRegistries.Data.UpdateValue(writeTargetPath, convertedValue);
                }
            }
            else if (!string.IsNullOrWhiteSpace(configuredWritePath))
            {
                HostRegistries.Data.UpdateValue(configuredWritePath, convertedValue);
            }

            HostRegistries.Data.UpdateValue(row.RegistryPath, convertedValue);
            row.CurrentValue = convertedValue;
        }
        finally
        {
            _isPublishing = false;
        }

        e.Handled = true;
    }

    private IEnumerable<string> GetSourceOptions()
    {
        return MainWindowViewModel.EnumerateSignalSourceOptions();
    }

    private static async System.Threading.Tasks.Task<object?> EditNumericValueAsync(Window owner, CustomSignalRow row)
    {
        var initial = ToNullableDouble(row.CurrentValue);
        var result = await EditorInputDialogs.EditNumericAsync(owner, row.Name, row.RegistryPath, "0.###", initial);
        return result;
    }

    private static void UpdateFooter(FolderItemModel ownerItem, int count)
    {
        ownerItem.Footer = count == 0
            ? "No custom signals configured"
            : $"{count} custom signal{(count == 1 ? string.Empty : "s")} published";
    }

    internal static string BuildRegistryPath(FolderItemModel ownerItem, CustomSignalDefinition definition)
    {
        var folderName = string.IsNullOrWhiteSpace(ownerItem.FolderName)
            ? "folder"
            : TargetPathHelper.NormalizeConfiguredTargetPath(ownerItem.FolderName);
        var widgetName = TargetPathHelper.NormalizePathSegment(ownerItem.Name, "custom_signals");
        var signalName = TargetPathHelper.NormalizePathSegment(definition.Name, "signal");
        return $"studio.{folderName}.{widgetName}.{signalName}";
    }

    internal static string BuildManualTriggerPath(FolderItemModel ownerItem, CustomSignalDefinition definition)
        => BuildManualTriggerPath(BuildRegistryPath(ownerItem, definition));

    internal static string BuildManualTriggerPath(string registryPath)
        => $"{registryPath}.trigger";

    internal static object? ParseLiteral(string? valueText, CustomSignalDataType dataType)
    {
        return dataType switch
        {
            CustomSignalDataType.Boolean => ToBool(valueText),
            CustomSignalDataType.Number => ToNullableDouble(valueText) ?? 0d,
            _ => valueText ?? string.Empty
        };
    }

    internal static object? ConvertToDataType(object? value, CustomSignalDataType dataType)
    {
        return dataType switch
        {
            CustomSignalDataType.Boolean => ToBool(value),
            CustomSignalDataType.Number => ToDouble(value),
            _ => value?.ToString() ?? string.Empty
        };
    }

    internal static double ToDouble(object? value)
    {
        return ToNullableDouble(value) ?? 0d;
    }

    internal static double? ToNullableDouble(object? value)
    {
        return value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            bool boolValue => boolValue ? 1d : 0d,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) => parsed,
            IConvertible convertible => TryConvertToDouble(convertible),
            _ => null
        };
    }

    internal static bool ToBool(object? value)
    {
        return value switch
        {
            null => false,
            bool boolValue => boolValue,
            string text when string.IsNullOrWhiteSpace(text) => false,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedNumber) => Math.Abs(parsedNumber) > double.Epsilon,
            IConvertible convertible => Math.Abs(TryConvertToDouble(convertible) ?? 0d) > double.Epsilon,
            _ => false
        };
    }

    private static double? TryConvertToDouble(IConvertible convertible)
    {
        try
        {
            return convertible.ToDouble(CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool TryResolveWriteTarget(string configuredPath, string? folderName, out ItemModel item)
    {
        foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(configuredPath, folderName))
        {
            ItemModel? resolvedItem;
            if (HostRegistries.Data.TryGet(candidate, out resolvedItem) && resolvedItem is not null)
            {
                item = resolvedItem;
                return true;
            }
        }

        item = null!;
        return false;
    }

}

public sealed class CustomSignalRow : ObservableObject
{
    private readonly FolderItemModel _ownerItem;
    private object? _currentValue;

    public CustomSignalRow(FolderItemModel ownerItem, CustomSignalDefinition definition, string registryPath)
    {
        _ownerItem = ownerItem;
        Definition = definition;
        RegistryPath = registryPath;
    }

    public CustomSignalDefinition Definition { get; }

    public string RegistryPath { get; }

    public string Name => Definition.Name;

    public bool CanEditValue => Definition.Mode == CustomSignalMode.Input && Definition.IsWritable;

    public object? CurrentValue
    {
        get => _currentValue;
        set
        {
            if (SetProperty(ref _currentValue, value))
            {
                RaisePropertyChanged(nameof(ValueDisplay));
            }
        }
    }

    public string SummaryText => $"Mode: {Definition.Mode} � Type: {Definition.DataType} � Write: {Definition.WriteMode}";

    public string ValueDisplay
    {
        get
        {
            if (CurrentValue is null)
            {
                return "Value: n/a";
            }

            var text = Definition.DataType == CustomSignalDataType.Number && !string.IsNullOrWhiteSpace(Definition.Format)
                ? CustomSignalsControl.ToDouble(CurrentValue).ToString(Definition.Format, CultureInfo.InvariantCulture)
                : CurrentValue.ToString() ?? string.Empty;

            return string.IsNullOrWhiteSpace(Definition.Unit)
                ? $"Value: {text}"
                : $"Value: {text} {Definition.Unit}";
        }
    }

    public string RowBackground => _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
    }

    private string BuildSourceSummary()
    {
        var parts = new[] { Definition.SourcePath, Definition.SourcePath2, Definition.SourcePath3 }
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" | ", parts);
    }
}

public partial class EditorCustomSignalsWidget : CustomSignalsControl
{
}
