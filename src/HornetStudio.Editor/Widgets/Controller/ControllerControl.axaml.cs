using System;
using System.Collections.ObjectModel;
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
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Displays and edits PID controller definitions for a controller widget.
/// </summary>
public partial class ControllerControl : EditorTemplateControl
{
    private const string DiagnosticsSourceName = nameof(ControllerControl);

    /// <summary>
    /// Indicates whether the widget currently has no configured controllers.
    /// </summary>
    public static readonly DirectProperty<ControllerControl, bool> HasNoControllersProperty =
        AvaloniaProperty.RegisterDirect<ControllerControl, bool>(nameof(HasNoControllers), control => control.HasNoControllers);

    /// <summary>
    /// Overrides the view model resolved from the visual tree with an explicitly injected one.
    /// </summary>
    public static readonly StyledProperty<MainWindowViewModel?> EditorViewModelProperty =
        AvaloniaProperty.Register<ControllerControl, MainWindowViewModel?>(nameof(EditorViewModel));

    private readonly ControllerDefinitionFileCodec _fileCodec = new();
    private FolderItemModel? _observedItem;
    private bool _hasNoControllers = true;
    private int _runtimeRefreshQueued;
    private int _suppressObservedItemRebuild;
    private volatile PidControllerRuntime[]? _runtimeSnapshot;
    private ScopedRegistryItemChangedSubscription? _registrySubscription;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerControl"/> class.
    /// </summary>
    public ControllerControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        Controllers.CollectionChanged += OnControllersCollectionChanged;
    }

    /// <summary>
    /// Gets the currently displayed controller rows.
    /// </summary>
    public ObservableCollection<ControllerRow> Controllers { get; } = [];

    /// <summary>
    /// Gets a value indicating whether no controllers are configured.
    /// </summary>
    public bool HasNoControllers
    {
        get => _hasNoControllers;
        private set => SetAndRaise(HasNoControllersProperty, ref _hasNoControllers, value);
    }

    /// <summary>
    /// Gets or sets an explicitly injected view model that takes precedence over the visual-tree resolved one.
    /// </summary>
    public MainWindowViewModel? EditorViewModel
    {
        get => GetValue(EditorViewModelProperty);
        set => SetValue(EditorViewModelProperty, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => EditorViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private string DiagnosticsSource => BuildBrowserDiagnosticsSource(DiagnosticsSourceName, _observedItem ?? Item);

    private static bool IsBrowserHost(FolderItemModel? item)
        => item is not null
            && item.IsControllerWidget
            && string.Equals(item.Name, "ControllersBrowser", StringComparison.OrdinalIgnoreCase);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorViewModelProperty)
        {
            RebuildControllers();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RefreshBrowserActivityState();
        EnsureRegistrySubscription();
        RebuildControllers();
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
        RebuildControllers();
    }

    protected override void OnBrowserRefreshActivated()
    {
        if (HasPendingBrowserRefresh)
        {
            RebuildControllers();
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
            if (RequiresControllerRebuild(propertyName) && !IsBrowserRefreshActive)
            {
                MarkBrowserRefreshDirty();
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "ObservedItemPropertyChanged");
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.ControllerDefinitions)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.Path)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            if (Volatile.Read(ref _suppressObservedItemRebuild) == 0)
            {
                RebuildControllers();
            }

            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            foreach (var row in Controllers)
            {
                row.RefreshTheme();
            }
        }
    }

    private void OnControllersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasNoControllers = Controllers.Count == 0;
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
        foreach (var row in Controllers)
        {
            if (row.Runtime?.MatchesPath(e.Key) == true)
            {
                accepted = true;
                row.RefreshRuntime();
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
            foreach (var row in Controllers)
            {
                row.RefreshRuntime();
            }
        }, DispatcherPriority.Background);
    }

    private static bool IsRelevantRuntimePath(PidControllerRuntime[] snapshot, string key)
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

    private void RebuildControllers()
    {
        if (!TryRunBrowserRefresh(() =>
            {
                using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(
                    TopLevel.GetTopLevel(this) as Window,
                    DiagnosticsSource,
                    nameof(RebuildControllers));
                var item = _observedItem;
                Controllers.Clear();
                if (item is null)
                {
                    _runtimeSnapshot = [];
                    UpdateRegistrySubscriptionPrefixes();
                    return;
                }

                var runtimeByName = (ViewModel?.GetControllerRuntimes(item, forceRecreate: false) ?? Array.Empty<PidControllerRuntime>())
                    .ToDictionary(runtime => runtime.Definition.Name, StringComparer.OrdinalIgnoreCase);

                IReadOnlyList<ControllerDefinition> definitions;
                if (IsBrowserHost(item))
                {
                    definitions = LoadFileEntries(item);
                }
                else
                {
                    definitions = ControllerDefinitionCodec.ParseDefinitions(item.ControllerDefinitions);
                }

                foreach (var definition in definitions)
                {
                    runtimeByName.TryGetValue(definition.Name, out var runtime);
                    Controllers.Add(new ControllerRow(item, definition, runtime));
                }

                _runtimeSnapshot = Controllers
                    .Select(static row => row.Runtime)
                    .Where(static r => r is not null)
                    .ToArray()!;
                UpdateRegistrySubscriptionPrefixes();
                UpdateFooter(item, Controllers.Count);
            }))
        {
            return;
        }
    }

    private static bool RequiresControllerRebuild(string? propertyName)
        => string.IsNullOrWhiteSpace(propertyName)
            || propertyName == nameof(FolderItemModel.ControllerDefinitions)
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
        _registrySubscription?.UpdatePrefixes(EnumerateRegistryScopePrefixes());
    }

    private IEnumerable<string> EnumerateRegistryScopePrefixes()
    {
        var item = _observedItem;
        if (item is null || string.IsNullOrWhiteSpace(item.FolderName))
        {
            yield break;
        }

        foreach (var row in Controllers)
        {
            yield return PidControllerRuntime.BuildRegistryPath(item.FolderName, row.Definition);

            foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(row.Definition.SourcePath, item.FolderName))
            {
                yield return candidate;
            }

            foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(row.Definition.OutputPath, item.FolderName))
            {
                yield return candidate;
            }
        }
    }

    private static void UpdateFooter(FolderItemModel ownerItem, int count)
    {
        ownerItem.Footer = count == 0
            ? "No PID controllers configured"
            : $"{count} PID controller{(count == 1 ? string.Empty : "s")} configured";
    }

    private void ApplyControllerDefinitions(FolderItemModel ownerItem, string rawDefinitions, bool queuePersist)
    {
        Interlocked.Increment(ref _suppressObservedItemRebuild);
        try
        {
            ownerItem.ControllerDefinitions = rawDefinitions;
        }
        finally
        {
            Interlocked.Decrement(ref _suppressObservedItemRebuild);
        }

        RebuildControllers();
        if (!queuePersist || ViewModel is not { } viewModel)
        {
            return;
        }

        if (!viewModel.TrySaveOwningPageYaml(ownerItem, out _))
        {
            viewModel.QueueSaveOwningPageYaml(ownerItem);
        }
    }

    private async void OnAddControllerClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        var ownerItem = _observedItem;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var viewModel = ViewModel;
        var dialogOwner = CreateDialogOwnerItem(ownerItem);
        var definition = await ControllerEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, null, GetSourceOptions());
        if (definition is null)
        {
            return;
        }

        if (IsBrowserHost(ownerItem))
        {
            if (Controllers.Any(row => string.Equals(row.Definition.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            SaveBrowserDefinition(ownerItem, definition, existingName: null);
        }
        else
        {
            var definitions = ControllerDefinitionCodec.ParseDefinitions(ownerItem.ControllerDefinitions).ToList();
            definitions.Add(definition);
            ApplyControllerDefinitions(ownerItem, ControllerDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
    }

    private async void OnEditControllerClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!TryResolveControllerRow(sender, out var row))
        {
            return;
        }

        var ownerItem = _observedItem;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var viewModel = ViewModel;
        var dialogOwner = CreateDialogOwnerItem(ownerItem);

        ControllerDefinition? currentDefinition;
        if (IsBrowserHost(ownerItem))
        {
            currentDefinition = LoadFileEntries(ownerItem)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
                ?.Clone();
        }
        else
        {
            currentDefinition = ControllerDefinitionCodec.ParseDefinitions(ownerItem.ControllerDefinitions)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (currentDefinition is null)
        {
            return;
        }

        var updated = await ControllerEditorDialogWindow.ShowAsync(owner, viewModel, dialogOwner, currentDefinition, GetSourceOptions());
        if (updated is null)
        {
            return;
        }

        if (IsBrowserHost(ownerItem))
        {
            if (Controllers.Any(candidate => !ReferenceEquals(candidate, row) && string.Equals(candidate.Definition.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            SaveBrowserDefinition(ownerItem, updated, existingName: row.Definition.Name);
        }
        else
        {
            var definitions = ControllerDefinitionCodec.ParseDefinitions(ownerItem.ControllerDefinitions).ToList();
            var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            definitions[index] = updated;
            ApplyControllerDefinitions(ownerItem, ControllerDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
    }

    private async void OnDeleteControllerClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!TryResolveControllerRow(sender, out var row))
        {
            return;
        }

        var ownerItem = _observedItem;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, "Delete controller", $"Delete PID controller '{row.Name}'?");
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
            var definitions = ControllerDefinitionCodec.ParseDefinitions(ownerItem.ControllerDefinitions)
                .Where(definition => !string.Equals(definition.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ApplyControllerDefinitions(ownerItem, ControllerDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }
    }

    private static bool TryResolveControllerRow(object? sender, out ControllerRow row)
    {
        if (sender is Button { CommandParameter: ControllerRow commandRow })
        {
            row = commandRow;
            return true;
        }

        if (sender is Button { Tag: ControllerRow tagRow })
        {
            row = tagRow;
            return true;
        }

        if (sender is Control { DataContext: ControllerRow contextRow })
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
            ControllerDefinitions = ControllerDefinitionCodec.SerializeDefinitions(LoadFileEntries(ownerItem))
        };
        dialogOwner.SetHierarchy(ownerItem.FolderName, null, ownerItem.ActiveViewId);
        dialogOwner.SetLayoutFilePath(ownerItem.FolderLayoutPath);
        return dialogOwner;
    }

    private IReadOnlyList<ControllerDefinition> LoadFileEntries(FolderItemModel ownerItem)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            return Array.Empty<ControllerDefinition>();
        }

        return _fileCodec.LoadFolder(folderDirectory)
            .Select(static entry => entry.Definition)
            .ToArray();
    }

    private void SaveBrowserDefinition(FolderItemModel ownerItem, ControllerDefinition definition, string? existingName)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return;
        }

        var existingFile = string.IsNullOrWhiteSpace(existingName)
            ? null
            : _fileCodec.LoadFolder(folderDirectory)
                .FirstOrDefault(entry => string.Equals(entry.Definition.Name, existingName, StringComparison.OrdinalIgnoreCase));
        _fileCodec.SaveDefinition(folderDirectory, definition, existingFile?.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        RebuildControllers();
    }

    private void DeleteBrowserDefinition(FolderItemModel ownerItem, string definitionName)
    {
        var folderDirectory = GetFolderDirectory(ownerItem);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return;
        }

        var existingFile = _fileCodec.LoadFolder(folderDirectory)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Name, definitionName, StringComparison.OrdinalIgnoreCase));
        if (existingFile is null)
        {
            ownerItem.Footer = $"Controller '{definitionName}' is legacy-only and must be removed from the legacy widget.";
            return;
        }

        _fileCodec.DeleteDefinition(existingFile.FilePath);
        ViewModel?.RefreshFolderBindings(ownerItem.FolderName);
        RebuildControllers();
    }

    private static string? GetFolderDirectory(FolderItemModel ownerItem)
    {
        if (string.IsNullOrWhiteSpace(ownerItem.FolderLayoutPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(ownerItem.FolderLayoutPath));
    }

    private static System.Collections.Generic.IEnumerable<string> GetSourceOptions()
    {
        return MainWindowViewModel.EnumerateSignalSourceOptions();
    }
}

/// <summary>
/// Represents a single controller row in the widget UI.
/// </summary>
public sealed class ControllerRow : ObservableObject
{
    private readonly FolderItemModel _ownerItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerRow"/> class.
    /// </summary>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="definition">The controller definition.</param>
    public ControllerRow(FolderItemModel ownerItem, ControllerDefinition definition, PidControllerRuntime? runtime)
    {
        _ownerItem = ownerItem;
        Definition = definition.Clone().Normalize();
        Runtime = runtime;
    }

    /// <summary>
    /// Gets the normalized controller definition.
    /// </summary>
    public ControllerDefinition Definition { get; }

    /// <summary>
    /// Gets the runtime when available.
    /// </summary>
    public PidControllerRuntime? Runtime { get; }

    /// <summary>
    /// Gets the controller display name.
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// Gets the controller type text.
    /// </summary>
    public string TypeText => $"Type: {Definition.Type}";

    /// <summary>
    /// Gets the compact controller type label shown in the row.
    /// </summary>
    public string TypeDisplayText => Definition.Type.ToString().ToUpperInvariant();

    /// <summary>
    /// Gets the configured path summary.
    /// </summary>
    public string PathSummary => $"PV: {ValueOrPlaceholder(Definition.SourcePath)} | SET: {GetOwnedSetPath()} | OUT: {ValueOrPlaceholder(Definition.OutputPath)}";

    private string GetOwnedSetPath()
    {
        var folderName = string.IsNullOrWhiteSpace(_ownerItem.FolderName)
            ? _ownerItem.Name
            : _ownerItem.FolderName;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "-";
        }

        return PidControllerRuntime.BuildRegistryPath(
            folderName: folderName,
            definition: Definition) + ".set";
    }

    /// <summary>
    /// Gets the tuning and timing summary.
    /// </summary>
    public string ParameterSummary => string.Format(
        CultureInfo.InvariantCulture,
        "Ks {0:0.###} | Tu {1:0.###} | Tg {2:0.###} | compute {3} ms | output {4} ms",
        Definition.Pid.Ks,
        Definition.Pid.Tu,
        Definition.Pid.Tg,
        Definition.Pid.ComputeIntervalMs,
        Definition.Pid.OutputIntervalMs);

    /// <summary>
    /// Gets the runtime state summary.
    /// </summary>
    public string StateText => Runtime is null
        ? "State: pending runtime synchronization"
        : $"State: {Runtime.CurrentStateValue} | Run: {Runtime.IsRunning}";

    /// <summary>
    /// Gets the runtime alert summary.
    /// </summary>
    public string AlertText => HasAlert ? "Fault active" : string.Empty;

    /// <summary>
    /// Gets a value indicating whether an alert is present.
    /// </summary>
    public bool HasAlert => Runtime?.CurrentAlertValue ?? false;

    /// <summary>
    /// Gets the row tooltip with the hidden controller details.
    /// </summary>
    public string DetailToolTip => HasAlert
        ? string.Join(Environment.NewLine, [TypeText, PathSummary, ParameterSummary, StateText, $"Alert: {AlertText}"])
        : string.Join(Environment.NewLine, [TypeText, PathSummary, ParameterSummary, StateText]);

    /// <summary>
    /// Gets the row background color.
    /// </summary>
    public string RowBackground => _ownerItem.EffectiveBodyBackground;

    /// <summary>
    /// Gets the row border color.
    /// </summary>
    public string RowBorderBrush => _ownerItem.EffectiveBodyBorder;

    /// <summary>
    /// Gets the primary foreground color.
    /// </summary>
    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    /// <summary>
    /// Gets the secondary foreground color.
    /// </summary>
    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    /// <summary>
    /// Refreshes the row theme bindings.
    /// </summary>
    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
    }

    /// <summary>
    /// Refreshes runtime-dependent row state.
    /// </summary>
    public void RefreshRuntime()
    {
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(AlertText));
        RaisePropertyChanged(nameof(HasAlert));
        RaisePropertyChanged(nameof(DetailToolTip));
    }

    private static string ValueOrPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) ? "n/a" : value;
}

public partial class EditorControllerWidget : ControllerControl
{
}
