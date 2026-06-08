using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Widgets;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Persistence.CustomControls;

public partial class CustomSignalsBrowserControl : EditorTemplateControl
{
    private const string DiagnosticsSourceName = nameof(CustomSignalsBrowserControl);
    private const string BoolTargetTypeName = "bool";
    private const string FloatTargetTypeName = "float";
    private const string StringTargetTypeName = "string";

    public static readonly StyledProperty<MainWindowViewModel?> EditorViewModelProperty =
        AvaloniaProperty.Register<CustomSignalsBrowserControl, MainWindowViewModel?>(nameof(EditorViewModel));

    public static readonly DirectProperty<CustomSignalsBrowserControl, bool> HasNoSignalsProperty =
        AvaloniaProperty.RegisterDirect<CustomSignalsBrowserControl, bool>(nameof(HasNoSignals), control => control.HasNoSignals);

    private FolderItemModel? _observedItem;
    private bool _isPublishing;
    private bool _hasNoSignals = true;
    private HashSet<string> _publishedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly CustomSignalDefinitionFileCodec _fileCodec = new();
    private readonly Dictionary<string, DateTimeOffset> _lastComputedPublishTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _manualTriggerPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingManualEvaluations = new(StringComparer.OrdinalIgnoreCase);
    private volatile string[]? _relevantPathsSnapshot;
    private ScopedRegistryItemChangedSubscription? _registrySubscription;

    public ObservableCollection<CustomSignalRow> Signals { get; } = [];

    public MainWindowViewModel? EditorViewModel
    {
        get => GetValue(EditorViewModelProperty);
        set => SetValue(EditorViewModelProperty, value);
    }

    public bool HasNoSignals
    {
        get => _hasNoSignals;
        private set => SetAndRaise(HasNoSignalsProperty, ref _hasNoSignals, value);
    }

    private FolderItemModel? ItemModel => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => EditorViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private string DiagnosticsSource => BuildBrowserDiagnosticsSource(DiagnosticsSourceName, _observedItem ?? ItemModel);

    private static bool IsBrowserHost(FolderItemModel? item)
        => item is not null
            && item.Kind == ControlKind.CustomSignals
            && string.Equals(item.Name, "CustomSignalsBrowser", StringComparison.OrdinalIgnoreCase);

    public CustomSignalsBrowserControl()
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
        RefreshBrowserActivityState();
        EnsureRegistrySubscription();
        RebuildSignalRows();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorViewModelProperty)
        {
            RebuildSignalRows();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DisposeRegistrySubscription();
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RefreshBrowserActivityState();
        RebuildSignalRows();
    }

    protected override void OnBrowserRefreshActivated()
    {
        if (HasPendingBrowserRefresh)
        {
            RebuildSignalRows();
        }
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
            if (RequiresSignalRowRebuild(propertyName) && !IsBrowserRefreshActive)
            {
                MarkBrowserRefreshDirty();
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "ObservedItemPropertyChanged");
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
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: false);
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (!IsRelevantPathOnBackground(e.Key))
            {
                UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: false);
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "RegistryItemChanged");
            Dispatcher.UIThread.Post(() => OnRegistryItemChanged(sender, e));
            return;
        }

        if (_manualTriggerPaths.TryGetValue(e.Key, out var manualRegistryPath))
        {
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: true);
            HandleManualTriggerChange(e.Key, manualRegistryPath);
            return;
        }

        if (_publishedPaths.Contains(e.Key))
        {
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: true);
            UpdateRowsFromRegistry();
            return;
        }

        if (IsRelevantSourceChange(e.Key))
        {
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: true);
            PublishSignals(preserveInputValues: true);
            return;
        }

        UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: false);
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
        if (!TryRunBrowserRefresh(() =>
            {
                using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(
                    TopLevel.GetTopLevel(this) as Window,
                    DiagnosticsSource,
                    nameof(RebuildSignalRows));
                var item = _observedItem;
                if (item is null)
                {
                    Signals.Clear();
                    _relevantPathsSnapshot = [];
                    UpdateRegistrySubscriptionPrefixes();
                    return;
                }

                IReadOnlyList<CustomSignalDefinition> definitions;
                if (IsBrowserHost(item))
                {
                    definitions = LoadFileEntries(item);
                }
                else
                {
                    definitions = ViewModel?.GetCustomSignalDefinitions(item)
                        ?? CustomSignalDefinitionCodec.ParseDefinitions(item.CustomSignalDefinitions);
                }

                Signals.Clear();
                foreach (var definition in definitions)
                {
                    Signals.Add(new CustomSignalRow(item, definition.Clone(), CustomSignalRuntimeHelper.BuildRegistryPath(item, definition)));
                }

                UpdateFooter(item, definitions.Count);
                PublishSignals(preserveInputValues: true);
            }))
        {
            return;
        }
    }

    private static bool RequiresSignalRowRebuild(string? propertyName)
        => string.IsNullOrWhiteSpace(propertyName)
            || propertyName == nameof(FolderItemModel.CustomSignalDefinitions)
            || propertyName == nameof(FolderItemModel.Name)
            || propertyName == nameof(FolderItemModel.Path)
            || propertyName == nameof(FolderItemModel.FolderName);

    private void EnsureRegistrySubscription()
    {
        _registrySubscription ??= new ScopedRegistryItemChangedSubscription(OnRegistryItemChanged);
        UpdateRegistrySubscriptionPrefixes();
    }

    private void DisposeRegistrySubscription()
    {
        _registrySubscription?.Dispose();
        _registrySubscription = null;
    }

    private void UpdateRegistrySubscriptionPrefixes()
    {
        _registrySubscription?.UpdatePrefixes(_relevantPathsSnapshot ?? []);
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
            .Select(definition => CustomSignalRuntimeHelper.BuildRegistryPath(item, definition))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextManualTriggerPaths = definitions
            .Where(static definition => definition.Mode == CustomSignalMode.Computed && definition.Trigger == CustomSignalComputationTrigger.Manual)
            .Select(definition => CustomSignalRuntimeHelper.BuildManualTriggerPath(item, definition))
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

        UpdateRelevantPathsSnapshot();
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
        item.Properties["type"].Value = GetRegistryType(definition.DataType);
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

        var triggerPath = CustomSignalRuntimeHelper.BuildManualTriggerPath(registryPath);
        var item = new ItemModel("trigger", false, registryPath);
        item.Properties["kind"].Value = "CustomSignalManualTrigger";
        item.Properties["title"].Value = $"{definition.Name} Trigger";
        item.Properties["text"].Value = "Trigger";
        item.Properties["mode"].Value = definition.Mode.ToString();
        item.Properties["type"].Value = BoolTargetTypeName;
        item.Properties["writable"].Value = true;
        item.Properties["owner"].Value = ownerItem.Name ?? string.Empty;
        item.Properties["value"].Value = false;
        HostRegistries.Data.UpsertSnapshot(triggerPath, item, DataRegistryItemMetadata.PublicCommand());
    }

    private static string GetRegistryType(CustomSignalDataType dataType)
    {
        return dataType switch
        {
            CustomSignalDataType.Boolean => BoolTargetTypeName,
            CustomSignalDataType.Text => StringTargetTypeName,
            _ => FloatTargetTypeName
        };
    }

    private object? EvaluateValue(FolderItemModel ownerItem, CustomSignalDefinition definition, string registryPath, bool preserveInputValues)
    {
        if (definition.Mode == CustomSignalMode.Input)
        {
            if (preserveInputValues && HostRegistries.Data.TryGet(registryPath, out var existing) && existing is not null)
            {
                return CustomSignalRuntimeHelper.ConvertToDataType(existing.Properties.Has("value") ? existing.Properties["value"].Value : existing.Value, definition.DataType);
            }

            return CustomSignalRuntimeHelper.ParseLiteral(definition.ValueText, definition.DataType);
        }

        if (!ShouldEvaluateComputed(definition, registryPath))
        {
            if (HostRegistries.Data.TryGet(registryPath, out var existing) && existing is not null)
            {
                return CustomSignalRuntimeHelper.ConvertToDataType(existing.Properties.Has("value") ? existing.Properties["value"].Value : existing.Value, definition.DataType);
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

        var shouldTrigger = CustomSignalRuntimeHelper.ToBool(triggerItem.Properties.Has("value") ? triggerItem.Properties["value"].Value : triggerItem.Value);
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

    private bool IsRelevantSourceChange(string registryPath)
    {
        var item = _observedItem;
        if (item is null)
        {
            return false;
        }

        foreach (var row in Signals)
        {
            if (row.Definition.Mode != CustomSignalMode.Computed
                || row.Definition.Trigger != CustomSignalComputationTrigger.OnSourceChange)
            {
                continue;
            }

            foreach (var sourcePath in EnumerateSourcePaths(row.Definition))
            {
                foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(sourcePath, item.FolderName))
                {
                    if (TargetPathHelper.PathsEqual(candidate, registryPath)
                        || TargetPathHelper.IsDescendantPath(candidate, registryPath)
                        || TargetPathHelper.IsDescendantPath(registryPath, candidate))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void UpdateRelevantPathsSnapshot()
    {
        var item = _observedItem;
        var paths = new List<string>(_publishedPaths);
        if (item is not null)
        {
            foreach (var row in Signals)
            {
                if (row.Definition.Mode == CustomSignalMode.Computed
                    && row.Definition.Trigger == CustomSignalComputationTrigger.OnSourceChange)
                {
                    foreach (var sourcePath in EnumerateSourcePaths(row.Definition))
                    {
                        foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(sourcePath, item.FolderName))
                        {
                            paths.Add(candidate);
                        }
                    }
                }
            }
        }

        _relevantPathsSnapshot = paths.ToArray();
        UpdateRegistrySubscriptionPrefixes();
    }

    private bool IsRelevantPathOnBackground(string key)
    {
        var snapshot = _relevantPathsSnapshot;
        if (snapshot is null)
        {
            return true;
        }

        foreach (var candidate in snapshot)
        {
            if (TargetPathHelper.PathsEqual(candidate, key)
                || TargetPathHelper.IsDescendantPath(candidate, key)
                || TargetPathHelper.IsDescendantPath(key, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSourcePaths(CustomSignalDefinition definition)
    {
        foreach (var variable in definition.Variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.SourcePath))
            {
                yield return variable.SourcePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.SourcePath))
        {
            yield return definition.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(definition.SourcePath2))
        {
            yield return definition.SourcePath2;
        }

        if (!string.IsNullOrWhiteSpace(definition.SourcePath3))
        {
            yield return definition.SourcePath3;
        }
    }

    private async void OnAddSignalClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialogOwner = CreateDialogOwnerItem(ownerItem);
        var definition = await CustomSignalEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, null, GetSourceOptions());
        if (definition is null)
        {
            return;
        }

        if (Signals.Any(row => string.Equals(row.Definition.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (IsBrowserHost(ownerItem))
        {
            SaveBrowserDefinition(ownerItem, definition, existingName: null);
        }
        else
        {
            var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions).ToList();
            definitions.Add(definition);
            ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
    }

    private async void OnEditSignalClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!TryResolveSignalRow(sender, out var row))
        {
            return;
        }

        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialogOwner = CreateDialogOwnerItem(ownerItem);
        var currentDefinition = IsBrowserHost(ownerItem)
            ? row.Definition.Clone()
            : CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        if (currentDefinition is null)
        {
            return;
        }

        var updated = await CustomSignalEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, currentDefinition, GetSourceOptions());
        if (updated is null)
        {
            return;
        }

        if (Signals.Any(candidate => !ReferenceEquals(candidate, row) && string.Equals(candidate.Definition.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (IsBrowserHost(ownerItem))
        {
            SaveBrowserDefinition(ownerItem, updated, existingName: row.Definition.Name);
        }
        else
        {
            var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions).ToList();
            var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                definitions[index] = updated;
                ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
            }
        }
    }

    private async void OnDeleteSignalClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!TryResolveSignalRow(sender, out var row))
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

        if (IsBrowserHost(ownerItem))
        {
            DeleteBrowserDefinition(ownerItem, row.Definition.Name);
        }
        else
        {
            var definitions = CustomSignalDefinitionCodec.ParseDefinitions(ownerItem.CustomSignalDefinitions)
                .Where(definition => !string.Equals(definition.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ApplyCustomSignalDefinitions(ownerItem, CustomSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
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
            CustomSignalDataType.Boolean => !CustomSignalRuntimeHelper.ToBool(row.CurrentValue),
            _ => await EditorInputDialogs.EditTextAsync(owner, row.Name, row.RegistryPath, row.CurrentValue?.ToString() ?? string.Empty)
        };

        if (nextValue is null && row.Definition.DataType != CustomSignalDataType.Text)
        {
            return;
        }

        _isPublishing = true;
        try
        {
            var convertedValue = CustomSignalRuntimeHelper.ConvertToDataType(nextValue, row.Definition.DataType);
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

    private static bool TryResolveSignalRow(object? sender, out CustomSignalRow row)
    {
        if (sender is Button { CommandParameter: CustomSignalRow commandRow })
        {
            row = commandRow;
            return true;
        }

        if (sender is Button { Tag: CustomSignalRow tagRow })
        {
            row = tagRow;
            return true;
        }

        if (sender is Control { DataContext: CustomSignalRow contextRow })
        {
            row = contextRow;
            return true;
        }

        row = null!;
        return false;
    }

    private FolderItemModel CreateDialogOwnerItem(FolderItemModel ownerItem)
    {
        if (!IsBrowserHost(ownerItem))
        {
            return ownerItem;
        }

        var dialogOwner = new FolderItemModel
        {
            Kind = ownerItem.Kind,
            Name = ownerItem.Name,
            ControlCaption = ownerItem.ControlCaption,
            BodyCaption = ownerItem.BodyCaption,
            Footer = ownerItem.Footer,
            CustomSignalDefinitions = CustomSignalDefinitionCodec.SerializeDefinitions(LoadFileEntries(ownerItem))
        };
        dialogOwner.SetHierarchy(ownerItem.FolderName, null, ownerItem.ActiveViewId);
        dialogOwner.SetLayoutFilePath(ownerItem.FolderLayoutPath);
        return dialogOwner;
    }

    private IReadOnlyList<CustomSignalDefinition> LoadFileEntries(FolderItemModel ownerItem)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            return Array.Empty<CustomSignalDefinition>();
        }

        return _fileCodec.LoadFolder(folderDirectory, ownerItem.FolderName)
            .Select(static entry => entry.Definition)
            .ToArray();
    }

    private void SaveBrowserDefinition(FolderItemModel ownerItem, CustomSignalDefinition definition, string? existingName)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return;
        }

        var existingFile = _fileCodec.LoadFolder(folderDirectory, ownerItem.FolderName)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Name, existingName, StringComparison.OrdinalIgnoreCase));
        _fileCodec.SaveDefinition(folderDirectory, ownerItem.FolderName, definition, existingFile?.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        RebuildSignalRows();
    }

    private void DeleteBrowserDefinition(FolderItemModel ownerItem, string definitionName)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return;
        }

        var existingFile = _fileCodec.LoadFolder(folderDirectory, ownerItem.FolderName)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Name, definitionName, StringComparison.OrdinalIgnoreCase));
        if (existingFile is null)
        {
            ownerItem.Footer = $"Custom signal '{definitionName}' is legacy-only and must be removed from the legacy widget.";
            return;
        }

        _fileCodec.DeleteDefinition(existingFile.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        RebuildSignalRows();
    }

    private static string? GetFolderDirectory(FolderItemModel ownerItem)
    {
        if (string.IsNullOrWhiteSpace(ownerItem.FolderLayoutPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(ownerItem.FolderLayoutPath));
    }

    private static async System.Threading.Tasks.Task<object?> EditNumericValueAsync(Window owner, CustomSignalRow row)
    {
        var initial = CustomSignalRuntimeHelper.ToNullableDouble(row.CurrentValue);
        var result = await EditorInputDialogs.EditNumericAsync(owner, row.Name, row.RegistryPath, "0.###", initial);
        return result;
    }

    private static void UpdateFooter(FolderItemModel ownerItem, int count)
    {
        ownerItem.Footer = count == 0
            ? "No custom signals configured"
            : $"{count} custom signal{(count == 1 ? string.Empty : "s")} published";
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
                ? CustomSignalRuntimeHelper.ToDouble(CurrentValue).ToString(Definition.Format, CultureInfo.InvariantCulture)
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
