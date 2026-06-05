using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EnhancedSignalsControl : EditorTemplateControl
{
    private const string DiagnosticsSourceName = nameof(EnhancedSignalsControl);

    public static readonly StyledProperty<MainWindowViewModel?> EditorViewModelProperty =
        AvaloniaProperty.Register<EnhancedSignalsControl, MainWindowViewModel?>(nameof(EditorViewModel));

    public static readonly DirectProperty<EnhancedSignalsControl, bool> HasNoSignalsProperty =
        AvaloniaProperty.RegisterDirect<EnhancedSignalsControl, bool>(nameof(HasNoSignals), control => control.HasNoSignals);

    private FolderItemModel? _observedItem;
    private bool _hasNoSignals = true;
    private int _runtimeRefreshQueued;
    private int _runtimeRebuildQueued;
    private int _suppressObservedItemRebuild;
    private readonly EnhancedSignalDefinitionFileCodec _fileCodec = new();
    private volatile EnhancedSignalRuntime[]? _runtimeSnapshot;
    private ScopedRegistryItemChangedSubscription? _registrySubscription;

    public EnhancedSignalsControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        Signals.CollectionChanged += OnSignalsCollectionChanged;
    }

    public ObservableCollection<EnhancedSignalRow> Signals { get; } = [];

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

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => EditorViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private string DiagnosticsSource => BuildBrowserDiagnosticsSource(DiagnosticsSourceName, _observedItem ?? Item);

    private static bool IsBrowserHost(FolderItemModel? item)
        => item is not null
            && item.Kind == ControlKind.EnhancedSignals
            && string.Equals(item.Name, "EnhancedSignalsBrowser", StringComparison.OrdinalIgnoreCase);

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RefreshBrowserActivityState();
        EnsureRegistrySubscription();
        RebuildRuntimes();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorViewModelProperty)
        {
            QueueRuntimeRebuild();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DisposeRegistrySubscription();
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RefreshBrowserActivityState();
        RebuildRuntimes();
    }

    protected override void OnBrowserRefreshActivated()
    {
        if (HasPendingBrowserRefresh)
        {
            RebuildRuntimes();
        }
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, Item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = Item;
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
            if (RequiresRuntimeRebuild(propertyName) && !IsBrowserRefreshActive)
            {
                MarkBrowserRefreshDirty();
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "ObservedItemPropertyChanged");
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.EnhancedSignalDefinitions)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.Path)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            if (Volatile.Read(ref _suppressObservedItemRebuild) == 0)
            {
                QueueRuntimeRebuild();
            }

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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var snapshot = _runtimeSnapshot;
            if (snapshot is null || !IsRelevantRuntimePath(snapshot, e.Key))
            {
                UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: false);
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted: true);
            QueueRuntimeRefresh();
            return;
        }

        var accepted = false;
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
            owner: TopLevel.GetTopLevel(this) as Window,
            category: "SteadyStateEnhancedSignal",
            name: $"{DiagnosticsSource}.RegistryItemChangedUi",
            threshold: TimeSpan.FromMilliseconds(10),
            stateFactory: () => new Dictionary<string, object?>
            {
                ["Key"] = e.Key,
                ["SignalCount"] = Signals.Count
            });
        foreach (var row in Signals)
        {
            if (row.Runtime.MatchesPath(e.Key))
            {
                accepted = true;
                row.RefreshFromRuntime();
            }
        }

        UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(DiagnosticsSource, e.Key, accepted);
    }

    private void QueueRuntimeRefresh()
    {
        if (!IsBrowserRefreshActive)
        {
            MarkBrowserRefreshDirty();
            return;
        }

        if (Interlocked.Exchange(ref _runtimeRefreshQueued, 1) == 1)
        {
            return;
        }

        UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "RuntimeRefresh");
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _runtimeRefreshQueued, 0);
            using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
                owner: TopLevel.GetTopLevel(this) as Window,
                category: "SteadyStateEnhancedSignal",
                name: $"{DiagnosticsSource}.RuntimeRefresh",
                threshold: TimeSpan.FromMilliseconds(10),
                stateFactory: () => new Dictionary<string, object?>
                {
                    ["SignalCount"] = Signals.Count
                });
            foreach (var row in Signals)
            {
                row.RefreshFromRuntime();
            }
        }, DispatcherPriority.Background);
    }

    private static bool IsRelevantRuntimePath(EnhancedSignalRuntime[] snapshot, string key)
    {
        foreach (var runtime in snapshot)
        {
            if (runtime.MatchesPath(key))
            {
                return true;
            }
        }

        return false;
    }

    private void OnSignalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasNoSignals = Signals.Count == 0;
    }

    private void QueueRuntimeRebuild()
    {
        if (!IsBrowserRefreshActive)
        {
            MarkBrowserRefreshDirty();
            return;
        }

        if (Interlocked.Exchange(ref _runtimeRebuildQueued, 1) == 1)
        {
            return;
        }

        UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "RuntimeRebuild");
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _runtimeRebuildQueued, 0);
            RebuildRuntimes();
        }, DispatcherPriority.Background);
    }

    private void ApplyEnhancedSignalDefinitions(FolderItemModel ownerItem, string rawDefinitions, bool queuePersist)
    {
        Interlocked.Increment(ref _suppressObservedItemRebuild);
        try
        {
            ownerItem.EnhancedSignalDefinitions = rawDefinitions;
        }
        finally
        {
            Interlocked.Decrement(ref _suppressObservedItemRebuild);
        }

        QueueRuntimeRebuild();
        if (queuePersist)
        {
            if (ViewModel is { } viewModel)
            {
                if (!viewModel.TrySaveOwningPageYaml(ownerItem, out _))
                {
                    viewModel.QueueSaveOwningPageYaml(ownerItem);
                }
            }
        }
    }

    private static string SummarizeDefinitions(string? rawDefinitions)
    {
        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(rawDefinitions);
        if (definitions.Count == 0)
        {
            return "count=0";
        }

        return string.Join("; ", definitions.Select(definition =>
            $"name={definition.Name},mode={definition.Adjustment.MappingMode},enabled={definition.Adjustment.Enabled},offset={definition.Adjustment.Offset.ToString(CultureInfo.InvariantCulture)},gain={definition.Adjustment.Gain.ToString(CultureInfo.InvariantCulture)},spline={definition.Adjustment.SplinePoints.Count},inverse={definition.Adjustment.SupportsInverseMapping}"));
    }

    private void RebuildRuntimes()
    {
        if (!TryRunBrowserRefresh(() =>
            {
                using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(
                    TopLevel.GetTopLevel(this) as Window,
                    DiagnosticsSource,
                    nameof(RebuildRuntimes));
                var item = _observedItem;
                if (item is null)
                {
                    Signals.Clear();
                    _runtimeSnapshot = [];
                    UpdateRegistrySubscriptionPrefixes();
                    return;
                }

                var runtimes = ResolveRuntimes(item);

                Signals.Clear();
                foreach (var runtime in runtimes)
                {
                    Signals.Add(new EnhancedSignalRow(item, runtime));
                }

                _runtimeSnapshot = Signals.Select(static row => row.Runtime).ToArray();
                UpdateRegistrySubscriptionPrefixes();
                UpdateFooter(item, Signals.Count);
            }))
        {
            return;
        }
    }

    private static bool RequiresRuntimeRebuild(string? propertyName)
        => string.IsNullOrWhiteSpace(propertyName)
            || propertyName == nameof(FolderItemModel.EnhancedSignalDefinitions)
            || propertyName == nameof(FolderItemModel.Name)
            || propertyName == nameof(FolderItemModel.Path)
            || propertyName == nameof(FolderItemModel.FolderName);

    private void EnsureRegistrySubscription()
    {
        _registrySubscription ??= new ScopedRegistryItemChangedSubscription(OnRegistryItemChanged, DiagnosticsSource);
        UpdateRegistrySubscriptionPrefixes();
    }

    private void DisposeRegistrySubscription()
    {
        _registrySubscription?.Dispose();
        _registrySubscription = null;
    }

    private void UpdateRegistrySubscriptionPrefixes()
    {
        _registrySubscription?.UpdatePrefixes(EnumerateRegistryScopePrefixes());
    }

    private IEnumerable<string> EnumerateRegistryScopePrefixes()
    {
        var item = _observedItem;
        if (item is null || string.IsNullOrWhiteSpace(item.FolderName))
        {
            yield break;
        }

        foreach (var row in Signals)
        {
            yield return EnhancedSignalRuntime.BuildRegistryPath(item.FolderName, row.Definition);

            if (string.IsNullOrWhiteSpace(row.Definition.SourcePath))
            {
                continue;
            }

            foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(row.Definition.SourcePath, item.FolderName))
            {
                yield return candidate;
            }
        }
    }

    private IReadOnlyList<EnhancedSignalRuntime> ResolveRuntimes(FolderItemModel item)
    {
        if (ViewModel is { } viewModel)
        {
            return viewModel.GetEnhancedSignalRuntimes(item, forceRecreate: false);
        }

        if (IsBrowserHost(item))
        {
            return ResolveBrowserRuntimesWithoutViewModel(item);
        }

        return EnhancedSignalRuntimeManager.SyncDefinitions(
            item.FolderName,
            item.EnhancedSignalDefinitions,
            forceRecreate: false,
            rawDefinitionsGetter: () => item.EnhancedSignalDefinitions,
            rawDefinitionsSetter: rawDefinitions =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    item.EnhancedSignalDefinitions = rawDefinitions;
                    return;
                }

                Dispatcher.UIThread.Post(() => item.EnhancedSignalDefinitions = rawDefinitions);
            });
    }

    private IReadOnlyList<EnhancedSignalRuntime> ResolveBrowserRuntimesWithoutViewModel(FolderItemModel item)
    {
        var definitions = LoadFileEntries(item)
            .Select(static entry => entry.Definition)
            .ToArray();
        if (definitions.Length == 0)
        {
            return Array.Empty<EnhancedSignalRuntime>();
        }

        string BuildRawDefinitions() => ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions);

        return EnhancedSignalRuntimeManager.SyncDefinitions(
            item.FolderName,
            BuildRawDefinitions(),
            forceRecreate: false,
            rawDefinitionsGetter: BuildRawDefinitions,
            rawDefinitionsSetter: rawDefinitions =>
            {
                var updatedDefinitions = ExtendedSignalDefinitionCodec.ParseDefinitions(rawDefinitions);
                foreach (var definition in updatedDefinitions)
                {
                    SaveBrowserDefinition(item, definition, existingName: definition.Name);
                }
            });
    }

    private static void UpdateFooter(FolderItemModel ownerItem, int count)
    {
        ownerItem.Footer = count == 0
            ? "No enhanced signals configured"
            : $"{count} enhanced signal module{(count == 1 ? string.Empty : "s")} published";
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
        var definition = await EnhancedSignalEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, null, GetSourceOptions());
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
            var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions).ToList();
            definitions.Add(definition);
            ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
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

        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions).ToList();
        var currentDefinition = IsBrowserHost(ownerItem)
            ? row.Definition.Clone()
            : definitions.FirstOrDefault(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        if (currentDefinition is null)
        {
            return;
        }

        var dialogOwner = CreateDialogOwnerItem(ownerItem);
        var updated = await EnhancedSignalEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, currentDefinition, GetSourceOptions());
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
            var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                definitions[index] = updated;
                ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
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

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, $"Delete enhanced signal '{row.Name}'?", "The enhanced signal definition will be removed.", confirmText: "Delete", cancelText: "Cancel");
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
            var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions)
                .Where(definition => !string.Equals(definition.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
    }

    private static bool TryResolveSignalRow(object? sender, out EnhancedSignalRow row)
    {
        if (sender is Button { CommandParameter: EnhancedSignalRow commandRow })
        {
            row = commandRow;
            return true;
        }

        if (sender is Button { Tag: EnhancedSignalRow tagRow })
        {
            row = tagRow;
            return true;
        }

        if (sender is Control { DataContext: EnhancedSignalRow contextRow })
        {
            row = contextRow;
            return true;
        }

        row = null!;
        return false;
    }

    private static IEnumerable<string> GetSourceOptions()
    {
        return MainWindowViewModel.EnumerateSignalSourceOptions();
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
            EnhancedSignalDefinitions = ExtendedSignalDefinitionCodec.SerializeDefinitions(LoadFileEntries(ownerItem).Select(entry => entry.Definition))
        };
        dialogOwner.SetHierarchy(ownerItem.FolderName, null, ownerItem.ActiveViewId);
        dialogOwner.SetLayoutFilePath(ownerItem.FolderLayoutPath);
        return dialogOwner;
    }

    private IReadOnlyList<EnhancedSignalFileEntry> LoadFileEntries(FolderItemModel ownerItem)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            return Array.Empty<EnhancedSignalFileEntry>();
        }

        return _fileCodec.LoadFolder(folderDirectory, ownerItem.FolderName);
    }

    private void SaveBrowserDefinition(FolderItemModel ownerItem, ExtendedSignalDefinition definition, string? existingName)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return;
        }

        var existingFile = LoadFileEntries(ownerItem)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Name, existingName, StringComparison.OrdinalIgnoreCase));
        _fileCodec.SaveDefinition(folderDirectory, ownerItem.FolderName, definition, existingFile?.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        QueueRuntimeRebuild();
    }

    private void DeleteBrowserDefinition(FolderItemModel ownerItem, string definitionName)
    {
        var existingFile = LoadFileEntries(ownerItem)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Name, definitionName, StringComparison.OrdinalIgnoreCase));
        if (existingFile is null)
        {
            ownerItem.Footer = $"Enhanced signal '{definitionName}' is legacy-only and must be removed from the legacy widget.";
            return;
        }

        _fileCodec.DeleteDefinition(existingFile.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        QueueRuntimeRebuild();
    }

    private static string? GetFolderDirectory(FolderItemModel ownerItem)
    {
        if (string.IsNullOrWhiteSpace(ownerItem.FolderLayoutPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(ownerItem.FolderLayoutPath));
    }
}

public sealed class EnhancedSignalRow : ObservableObject
{
    private readonly FolderItemModel _ownerItem;

    public EnhancedSignalRow(FolderItemModel ownerItem, EnhancedSignalRuntime runtime)
    {
        _ownerItem = ownerItem;
        Runtime = runtime;
    }

    public EnhancedSignalRuntime Runtime { get; }

    public ExtendedSignalDefinition Definition => Runtime.Definition;

    public string Name => Definition.Name;

    public string SourceText => $"Source: {ValueOrPlaceholder(Definition.SourcePath)}";

    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    public string AlertText => FormatAlert(Runtime.CurrentAlertValue);

    public string RowBackground => _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void RefreshFromRuntime()
    {
        RaisePropertyChanged(nameof(AlertText));
        RaisePropertyChanged(nameof(HasAlert));
    }

    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
    }

    private static string ValueOrPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) ? "n/a" : value;

    private static string FormatAlert(object? value)
        => value?.ToString() ?? string.Empty;
}

public partial class EditorEnhancedSignalsWidget : EnhancedSignalsControl
{
}
