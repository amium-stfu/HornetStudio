using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host;

namespace HornetStudio.Editor.Widgets;

public partial class MonitorViewControl : EditorTemplateControl
{
    private const string DiagnosticsSourceName = nameof(MonitorViewControl);

    public static readonly DirectProperty<MonitorViewControl, bool> HasNoRowsProperty =
        AvaloniaProperty.RegisterDirect<MonitorViewControl, bool>(nameof(HasNoRows), control => control.HasNoRows);

    private FolderItemModel? _observedItem;
    private bool _hasNoRows = true;
    private MainWindowViewModel? _observedViewModel;
    private long _observedProjectRuntimeGeneration = -1;

    public MonitorViewControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        DisplayRows.CollectionChanged += OnRowsCollectionChanged;
    }

    public ObservableCollection<AlertWidgetRow> DisplayRows { get; } = [];

    public bool HasNoRows
    {
        get => _hasNoRows;
        private set => SetAndRaise(HasNoRowsProperty, ref _hasNoRows, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private string DiagnosticsSource => BuildBrowserDiagnosticsSource(DiagnosticsSourceName, _observedItem ?? Item);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookViewModel();
        HookObservedItem();
        RefreshBrowserActivityState();
        RebuildRows();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookViewModel();
        UnhookObservedItem();
        ClearRows();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookViewModel();
        HookObservedItem();
        RefreshBrowserActivityState();
        RebuildRows();
    }

    protected override void OnBrowserRefreshActivated()
    {
        if (HasPendingBrowserRefresh)
        {
            RebuildRows();
        }
    }

    private void HookObservedItem()
    {
        var item = Item;
        if (ReferenceEquals(_observedItem, item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = item;
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

    private void HookViewModel()
    {
        var viewModel = ViewModel;
        if (ReferenceEquals(_observedViewModel, viewModel))
        {
            return;
        }

        UnhookViewModel();
        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
        {
            _observedProjectRuntimeGeneration = _observedViewModel.ProjectRuntimeGeneration;
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnhookViewModel()
    {
        if (_observedViewModel is null)
        {
            return;
        }

        _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = null;
        _observedProjectRuntimeGeneration = -1;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ProjectRuntimeGeneration))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "ProjectRuntimeGenerationChanged");
            Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        var generation = _observedViewModel?.ProjectRuntimeGeneration ?? -1;
        if (generation == _observedProjectRuntimeGeneration)
        {
            return;
        }

        _observedProjectRuntimeGeneration = generation;
        RebuildRows();
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            if (RequiresRowRebuild(propertyName) && !IsBrowserRefreshActive)
            {
                MarkBrowserRefreshDirty();
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "ObservedItemPropertyChanged");
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.SelectedMonitorIds)
            || e.PropertyName == nameof(FolderItemModel.FolderName)
            || e.PropertyName == nameof(FolderItemModel.Path))
        {
            RebuildRows();
            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground)
            or nameof(FolderItemModel.OnActiveColor))
        {
            foreach (var row in DisplayRows)
            {
                row.RefreshTheme();
            }
        }
    }

    private void RebuildRows()
    {
        if (!TryRunBrowserRefresh(() =>
            {
                using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(
                    TopLevel.GetTopLevel(this) as Window,
                    DiagnosticsSource,
                    nameof(RebuildRows));
                ClearRows();
                var item = _observedItem;
                if (item is null)
                {
                    UpdateFooter();
                    return;
                }

                var entries = (_observedViewModel ?? ViewModel)?.GetMonitorRegistryEntries(item)
                    ?? [];
                var selectedEntries = ResolveSelectedEntries(item.SelectedMonitorIds, entries);
                foreach (var entry in selectedEntries)
                {
                    DisplayRows.Add(new AlertWidgetRow(item, entry.Definition));
                }

                UpdateFooter();
            }))
        {
            return;
        }
    }

    internal static IReadOnlyList<MonitorRegistryEntry> ResolveSelectedEntries(string? selectedMonitorIds, IEnumerable<MonitorRegistryEntry>? entries)
    {
        var selectedIds = MonitorRegistry.ParseSelectedIds(selectedMonitorIds);
        if (selectedIds.Count == 0)
        {
            return [];
        }

        var availableEntries = (entries ?? []).ToArray();
        var selectedEntries = new List<MonitorRegistryEntry>();
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedId in selectedIds)
        {
            var entry = availableEntries.FirstOrDefault(candidate => MonitorRegistry.SelectionMatchesEntry(selectedId, candidate));
            if (entry is null || !addedNames.Add(entry.Name))
            {
                continue;
            }

            selectedEntries.Add(entry);
        }

        return selectedEntries;
    }

    private static bool RequiresRowRebuild(string? propertyName)
        => string.IsNullOrWhiteSpace(propertyName)
            || propertyName == nameof(FolderItemModel.SelectedMonitorIds)
            || propertyName == nameof(FolderItemModel.FolderName)
            || propertyName == nameof(FolderItemModel.Path);

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is AlertWidgetRow row)
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is AlertWidgetRow row)
                {
                    row.PropertyChanged += OnRowPropertyChanged;
                }
            }
        }

        HasNoRows = DisplayRows.Count == 0;
        UpdateFooter();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlertWidgetRow.IsActive))
        {
            UpdateFooter();
        }
    }

    private void ClearRows()
    {
        var rows = DisplayRows.ToArray();
        DisplayRows.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            row.Dispose();
        }
    }

    private void UpdateFooter()
    {
        var item = _observedItem;
        if (item is null)
        {
            return;
        }

        var activeCount = DisplayRows.Count(row => row.IsActive);
        item.Footer = DisplayRows.Count == 0
            ? "No monitor rules selected"
            : $"{DisplayRows.Count} monitor rule{(DisplayRows.Count == 1 ? string.Empty : "s")}, {activeCount} active";
    }
}

public class AlertWidgetRow : ObservableObject, IDisposable
{
    private readonly FolderItemModel _ownerItem;
    private readonly ScopedRegistryItemChangedSubscription _registrySubscription;
    private readonly string _diagnosticsSource;
    private readonly object _stateGate = new();
    private readonly string _runtimePath;
    private readonly int _eventId;
    private readonly string _fallbackText;
    private readonly MonitorLogLevel _logLevel;
    private bool _isActive;
    private int _disposed;

    public AlertWidgetRow(FolderItemModel ownerItem, MonitorDefinition definition)
    {
        _ownerItem = ownerItem;
        Definition = definition.Clone();
        _runtimePath = MonitorRegistry.BuildRulePath(ownerItem.FolderName, definition.Name);
        _diagnosticsSource = BuildDiagnosticsSource(ownerItem, definition.Name);
        _eventId = definition.EventId;
        _fallbackText = string.IsNullOrWhiteSpace(definition.EventText) ? definition.Name : definition.EventText;
        _logLevel = definition.LogLevel;
        _registrySubscription = new ScopedRegistryItemChangedSubscription(
            OnRegistryItemChanged,
            diagnosticsSource: _diagnosticsSource,
            includeAncestorMatches: false);
        _registrySubscription.UpdatePrefixes([_runtimePath]);
        RefreshRuntime();
    }

    public MonitorDefinition Definition { get; }

    public string RuntimePath => _runtimePath;

    public bool IsActive => _isActive;

    public string EventIdText => _eventId.ToString(CultureInfo.InvariantCulture);

    public string EventDisplayText => _fallbackText;

    public string RowTooltip => EventDisplayText;

    public string RowBackground => _isActive ? GetActiveRowBackground() : _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => _isActive ? GetSeverityForeground() : _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _registrySubscription.Dispose();
    }

    public bool MatchesRuntimeBinding(string? changedPath)
        => !string.IsNullOrWhiteSpace(changedPath)
           && TargetPathHelper.PathsEqual(changedPath, _runtimePath);

    public bool TryApplyRuntimeChange(string? changedPath)
    {
        if (!MatchesRuntimeBinding(changedPath))
        {
            return false;
        }

        return ApplyRuntimeState(AlertWidgetRuntimeState.Read(_runtimePath));
    }

    public void RefreshRuntime()
    {
        ApplyRuntimeState(AlertWidgetRuntimeState.Read(_runtimePath));
    }

    internal bool ApplyRuntimeState(AlertWidgetRuntimeState state)
    {
        lock (_stateGate)
        {
            var previousState = new AlertWidgetRuntimeState(_isActive);
            if (previousState == state)
            {
                return false;
            }

            _isActive = state.IsActive;
        }

        RaisePropertyChanged(nameof(IsActive));
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        return true;
    }

    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var previousActive = GetCurrentActiveState();
        if (!MatchesRuntimeBinding(e.Key))
        {
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(_diagnosticsSource, e.Key, accepted: false);
            LogDispatchDecision(
                changedPath: e.Key,
                previousActive: previousActive,
                nextActive: previousActive,
                visibleStateChanged: false,
                accepted: false,
                dispatcherPost: false,
                reason: "KeyMismatch");
            return;
        }

        var nextState = AlertWidgetRuntimeState.Read(_runtimePath);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (!HasVisibleStateChange(nextState))
            {
                UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(_diagnosticsSource, e.Key, accepted: false);
                LogDispatchDecision(
                    changedPath: e.Key,
                    previousActive: previousActive,
                    nextActive: nextState.IsActive,
                    visibleStateChanged: false,
                    accepted: false,
                    dispatcherPost: false,
                    reason: "NoVisibleStateChange");
                return;
            }

            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(_diagnosticsSource, e.Key, accepted: true);
            LogDispatchDecision(
                changedPath: e.Key,
                previousActive: previousActive,
                nextActive: nextState.IsActive,
                visibleStateChanged: true,
                accepted: true,
                dispatcherPost: true,
                reason: "AlertWidgetRuntimeRefresh");
            UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(_diagnosticsSource, "AlertWidgetRuntimeRefresh");
            Dispatcher.UIThread.Post(() => ApplyRegistryUpdate(e.Key, nextState, recordDiagnostics: false));
            return;
        }

        ApplyRegistryUpdate(e.Key, nextState, recordDiagnostics: true);
    }

    private void ApplyRegistryUpdate(string? changedPath, AlertWidgetRuntimeState nextState, bool recordDiagnostics)
    {
        var previousActive = GetCurrentActiveState();
        var changed = ApplyRuntimeState(nextState);
        if (recordDiagnostics)
        {
            UiResponsivenessDiagnostics.RecordBrowserRegistryEvent(_diagnosticsSource, changedPath, accepted: changed);
        }

        LogDispatchDecision(
            changedPath: changedPath,
            previousActive: previousActive,
            nextActive: nextState.IsActive,
            visibleStateChanged: changed,
            accepted: changed,
            dispatcherPost: false,
            reason: changed
                ? recordDiagnostics ? "AppliedInlineRuntimeState" : "AppliedPostedRuntimeState"
                : recordDiagnostics ? "InlineNoVisibleStateChange" : "PostedNoVisibleStateChange");
    }

    private bool HasVisibleStateChange(AlertWidgetRuntimeState nextState)
    {
        lock (_stateGate)
        {
            return _isActive != nextState.IsActive;
        }
    }

    private bool GetCurrentActiveState()
    {
        lock (_stateGate)
        {
            return _isActive;
        }
    }

    private void LogDispatchDecision(
        string? changedPath,
        bool previousActive,
        bool nextActive,
        bool visibleStateChanged,
        bool accepted,
        bool dispatcherPost,
        string reason)
    {
        Core.LogInfo(
            $"[AlertWidgetDispatch] row={Definition.Name} source={_diagnosticsSource} changed_key={FormatDiagnosticValue(changedPath)} runtime_path={_runtimePath} previous_active={previousActive} next_active={nextActive} visible_state_changed={visibleStateChanged} accepted={accepted} dispatcher_post={dispatcherPost} reason={reason}");
    }

    private static string FormatDiagnosticValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private static string BuildDiagnosticsSource(FolderItemModel ownerItem, string ruleName)
    {
        var folderName = string.IsNullOrWhiteSpace(ownerItem.FolderName) ? ownerItem.Name : ownerItem.FolderName;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return $"AlertWidgetRow[{ruleName}]";
        }

        return $"AlertWidgetRow[{folderName}.{ruleName}]";
    }

    private string GetActiveRowBackground()
        => string.IsNullOrWhiteSpace(_ownerItem.OnActiveColor)
            ? _ownerItem.EffectiveAccentBackground
            : _ownerItem.OnActiveColor;

    private string GetSeverityForeground()
    {
        var theme = IsDarkColor(_ownerItem.EffectiveBodyBackground) ? ThemePalette.Dark : ThemePalette.Light;
        return _logLevel switch
        {
            MonitorLogLevel.Debug => theme.LogDebugForeground,
            MonitorLogLevel.Info => theme.LogInfoForeground,
            MonitorLogLevel.Warning => theme.LogWarningForeground,
            MonitorLogLevel.Error => theme.LogErrorForeground,
            MonitorLogLevel.Fatal => theme.LogFatalForeground,
            _ => _ownerItem.EffectiveMutedForeground
        };
    }

    private static bool IsDarkColor(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText) || !Avalonia.Media.Color.TryParse(colorText, out var color))
        {
            return false;
        }

        var brightness = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return brightness < 140;
    }
}

public sealed class MonitorViewRow : AlertWidgetRow
{
    public MonitorViewRow(FolderItemModel ownerItem, MonitorDefinition definition)
        : base(ownerItem, definition)
    {
    }
}

internal readonly record struct AlertWidgetRuntimeState(bool IsActive)
{
    public static AlertWidgetRuntimeState Read(string runtimePath)
    {
        var isActive = TryGetRuntimeValue(runtimePath, out var activeValue) && activeValue;
        return new AlertWidgetRuntimeState(isActive);
    }

    private static bool TryGetRuntimeValue(string path, out bool value)
    {
        value = false;
        if (!HostRegistries.Data.TryResolve(path, out var item) || item is null)
        {
            return false;
        }

        switch (item.Value)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case string text when bool.TryParse(text, out var parsedBool):
                value = parsedBool;
                return true;
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong):
                value = parsedLong != 0;
                return true;
            case byte numeric:
                value = numeric != 0;
                return true;
            case sbyte numeric:
                value = numeric != 0;
                return true;
            case short numeric:
                value = numeric != 0;
                return true;
            case ushort numeric:
                value = numeric != 0;
                return true;
            case int numeric:
                value = numeric != 0;
                return true;
            case uint numeric:
                value = numeric != 0;
                return true;
            case long numeric:
                value = numeric != 0;
                return true;
            case ulong numeric:
                value = numeric != 0;
                return true;
            case float numeric:
                value = Math.Abs(numeric) > float.Epsilon;
                return true;
            case double numeric:
                value = Math.Abs(numeric) > double.Epsilon;
                return true;
            case decimal numeric:
                value = numeric != 0;
                return true;
            default:
                return false;
        }
    }
}
