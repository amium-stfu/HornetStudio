using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Functions;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets.Workflow;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Widgets;

public partial class FunctionsControl : EditorTemplateControl
{
    private const string DiagnosticsSourceName = nameof(FunctionsControl);

    public static readonly StyledProperty<MainWindowViewModel?> EditorViewModelProperty =
        AvaloniaProperty.Register<FunctionsControl, MainWindowViewModel?>(nameof(EditorViewModel));

    private readonly HashSet<string> _stopRequestedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _runningStateTimer;
    private FolderItemModel? _observedItem;

    public FunctionsControl()
    {
        InitializeComponent();
        _runningStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _runningStateTimer.Tick += OnRunningStateTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    public ObservableCollection<FunctionCatalogRow> FunctionEntries { get; } = [];

    public MainWindowViewModel? EditorViewModel
    {
        get => GetValue(EditorViewModelProperty);
        set => SetValue(EditorViewModelProperty, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel => EditorViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private string DiagnosticsSource => BuildBrowserDiagnosticsSource(DiagnosticsSourceName, _observedItem ?? Item);

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        FolderItemModel.CatalogFunctionExecutionStateChanged += OnCatalogFunctionExecutionStateChanged;
        RefreshBrowserActivityState();
        RefreshFunctions();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        FolderItemModel.CatalogFunctionExecutionStateChanged -= OnCatalogFunctionExecutionStateChanged;
        StopRunningStateTimer();
    }

    private void OnCatalogFunctionExecutionStateChanged(object? sender, EventArgs e)
    {
        if (!IsBrowserRefreshActive)
        {
            MarkBrowserRefreshDirty();
            return;
        }

        UiResponsivenessDiagnostics.RecordBrowserDispatcherPost(DiagnosticsSource, "CatalogFunctionExecutionStateChanged");
        Dispatcher.UIThread.Post(ApplyRunningState);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        StopRunningStateTimer();
        _stopRequestedReferences.Clear();

        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged -= OnItemPropertyChanged;
        }

        _observedItem = Item;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnItemPropertyChanged;
        }

        RefreshBrowserActivityState();
        RefreshFunctions();
    }

    protected override void OnBrowserRefreshActivated()
    {
        if (HasPendingBrowserRefresh)
        {
            RefreshFunctions();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            if (!IsBrowserRefreshActive)
            {
                MarkBrowserRefreshDirty();
                return;
            }

            RefreshFunctions();
        }
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshFunctions();
        e.Handled = true;
    }

    private void OnRunningStateTimerTick(object? sender, EventArgs e)
    {
        if (!IsBrowserRefreshActive)
        {
            MarkBrowserRefreshDirty();
            StopRunningStateTimer();
            return;
        }

        ApplyRunningState();
    }

    private async void OnAddFunctionClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        var item = Item;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (item is null || owner is null)
        {
            return;
        }

        var folderDirectory = GetFolderDirectory(item);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            UpdateFooter(item, "Folder layout path is not available");
            return;
        }

        EnsureOwningPageSaved(item);

        var workflowDirectory = FunctionDefinitionCodec.GetFunctionDirectory(folderDirectory);
        var result = await FunctionEditorDialogWindow.ShowAsync(
            owner,
            ViewModel,
            item,
            workflowDirectory,
            definition: new FunctionDefinition
            {
                Name = string.Empty,
                Steps = []
            },
            existingFilePath: null,
            targetOptions: MainWindowViewModel.EnumerateSignalSourceOptions(),
            logTargetOptions: GetProcessLogTargetOptions());
        if (result is null)
        {
            return;
        }

        try
        {
            FunctionDefinitionCodec.SaveToFile(filePath: result.FilePath, definition: result.Definition);
            UpdateFooter(item, $"Saved function file '{Path.GetFileName(result.FilePath)}'");
            RefreshFunctions();
        }
        catch (Exception ex)
        {
            UpdateFooter(item, $"Function file could not be saved: {ex.Message}");
        }
    }

    private async void OnEditFunctionClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetFunctionCatalogRowFromActionSource(sender, out var row))
        {
            return;
        }

        var item = Item;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (item is null || owner is null)
        {
            return;
        }

        if (!row.CanEdit)
        {
            UpdateFooter(item, $"Catalog entry '{row.DisplayName}' is read-only.");
            e.Handled = true;
            return;
        }

        if (!FunctionDefinitionCodec.TryLoadFromFile(row.SourceIdentifier, out var definition, out var validation))
        {
            UpdateFooter(item, validation.Errors.FirstOrDefault()?.Message ?? $"Function file '{row.FileName}' could not be loaded.");
            e.Handled = true;
            return;
        }

        var result = await FunctionEditorDialogWindow.ShowAsync(
            owner,
            ViewModel,
            item,
            functionDirectory: Path.GetDirectoryName(row.SourceIdentifier) ?? string.Empty,
            definition,
            existingFilePath: row.SourceIdentifier,
            targetOptions: MainWindowViewModel.EnumerateSignalSourceOptions(),
            logTargetOptions: GetProcessLogTargetOptions());
        if (result is null)
        {
            return;
        }

        try
        {
            FunctionDefinitionCodec.SaveToFile(filePath: result.FilePath, definition: result.Definition);
            UpdateFooter(item, $"Updated function file '{Path.GetFileName(result.FilePath)}'");
            RefreshFunctions();
        }
        catch (Exception ex)
        {
            UpdateFooter(item, $"Function file could not be saved: {ex.Message}");
        }

        e.Handled = true;
    }

    private async void OnDeleteFunctionClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetFunctionCatalogRowFromActionSource(sender, out var row))
        {
            return;
        }

        var item = Item;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (item is null || owner is null)
        {
            return;
        }

        if (!row.CanDelete)
        {
            UpdateFooter(item, $"Catalog entry '{row.DisplayName}' cannot be deleted from Functions.");
            e.Handled = true;
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, $"Delete function '{row.DisplayName}'?", $"The function file '{row.FileName}' will be removed.", confirmText: "Delete", cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        if (!File.Exists(row.SourceIdentifier))
        {
            UpdateFooter(item, $"Function file '{row.FileName}' was not found.");
            RefreshFunctions();
            e.Handled = true;
            return;
        }

        try
        {
            File.Delete(row.SourceIdentifier);
            UpdateFooter(item, $"Deleted function file '{row.FileName}'");
            RefreshFunctions();
        }
        catch (Exception ex)
        {
            UpdateFooter(item, $"Function file could not be deleted: {ex.Message}");
        }

        e.Handled = true;
    }

    private void OnRunStopFunctionClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetFunctionCatalogRowFromActionSource(sender, out var row))
        {
            return;
        }

        var item = Item;
        if (item is null)
        {
            return;
        }

        if (row.IsStopEnabled)
        {
            StopFunction(item, row);
            e.Handled = true;
            return;
        }

        if (!row.IsRunStopEnabled)
        {
            UpdateFooter(item, $"Function '{row.DisplayName}' is not runnable.");
            e.Handled = true;
            return;
        }

        if (!item.TryRunCatalogFunction(functionReference: row.Reference, argument: string.Empty, out var error))
        {
            UpdateFooter(item, error);
            e.Handled = true;
            return;
        }

        _stopRequestedReferences.Remove(FunctionRegistry.NormalizeReference(row.Reference));
        ApplyRunningState();
        UpdateFooter(item, $"Started function '{row.DisplayName}'.");
        e.Handled = true;
    }

    private void StopFunction(FolderItemModel item, FunctionCatalogRow row)
    {
        if (!item.TryStopCatalogFunction(functionReference: row.Reference, out var error))
        {
            UpdateFooter(item, error);
            return;
        }

        _stopRequestedReferences.Add(FunctionRegistry.NormalizeReference(row.Reference));
        ApplyRunningState();
        UpdateFooter(item, $"Stopping function '{row.DisplayName}'.");
    }

    private void RefreshFunctions()
    {
        if (!TryRunBrowserRefresh(() =>
            {
                using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(
                    TopLevel.GetTopLevel(this) as Window,
                    DiagnosticsSource,
                    nameof(RefreshFunctions));
                FunctionEntries.Clear();

                var item = Item;
                var folderDirectory = GetFolderDirectory(item);
                var workflowDirectory = FunctionDefinitionCodec.GetFunctionDirectory(folderDirectory);
                WorkflowDirectoryText.Text = string.IsNullOrWhiteSpace(workflowDirectory)
                    ? "Function directory: not available"
                    : $"Function directory: {workflowDirectory}";

                if (string.IsNullOrWhiteSpace(folderDirectory))
                {
                    UpdateFooter(item, "Folder layout path is not available");
                    UpdateVisibility();
                    return;
                }

                foreach (var entry in FunctionRegistry.EnumerateEntries(folderDirectory))
                {
                    FunctionEntries.Add(CreateCatalogRow(entry, item));
                }

                ApplyRunningState();
                UpdateFooter(item, FunctionEntries.Count == 0 ? "No functions discovered" : $"{FunctionEntries.Count} function registr{(FunctionEntries.Count == 1 ? "y entry" : "y entries")}");
                UpdateVisibility();
            }))
        {
            return;
        }
    }

    private void UpdateVisibility()
    {
        var hasNoWorkflows = FunctionEntries.Count == 0;
        EmptyStateText.IsVisible = hasNoWorkflows;
        WorkflowItemsControl.IsVisible = !hasNoWorkflows;
    }

    private FunctionCatalogRow CreateCatalogRow(FunctionCatalogEntry entry, FolderItemModel? item)
    {
        var sourceIdentifier = entry.SourceIdentifier;
        var fileName = string.IsNullOrWhiteSpace(sourceIdentifier)
            ? entry.Reference
            : Path.GetFileName(sourceIdentifier);
        var normalizedReference = FunctionRegistry.NormalizeReference(entry.Reference);
        var isRunning = item?.IsCatalogFunctionRunning(entry.Reference) == true;
        var isStopping = isRunning
            && (_stopRequestedReferences.Contains(normalizedReference) || item?.IsCatalogFunctionStopping(entry.Reference) == true);
        var kindBadgeBackground = entry.Kind == FunctionCatalogKind.Python
            ? item?.EffectiveBodyBackground ?? "#E5E7EB"
            : entry.IsValid
                ? item?.EffectiveAccentBackground ?? "#DBEAFE"
                : "#FEE2E2";
        var kindBadgeForeground = entry.Kind == FunctionCatalogKind.Python
            ? item?.EffectiveMutedForeground ?? "#667085"
            : entry.IsValid
                ? item?.EffectiveAccentForeground ?? "#1D4ED8"
                : "#B91C1C";

        return new FunctionCatalogRow(
            catalogEntry: entry,
            displayName: entry.Name,
            fileName: fileName,
            sourceText: $"Source: {entry.DisplaySource}",
            displayKindText: GetDisplayKindText(entry.Kind),
            compactStatusText: GetCompactStatusText(entry, isRunning, isStopping),
            compactStatusToolTipText: GetCompactStatusToolTipText(entry, isRunning, isStopping),
            toolTipText: BuildToolTipText(entry, fileName, isRunning, isStopping),
            statusText: entry.StatusText,
            isRunning: isRunning,
            isStopping: isStopping,
            titleBrush: item?.EffectiveBodyForeground ?? "#111827",
            subtitleBrush: item?.EffectiveMutedForeground ?? "#667085",
            readyStatusBrush: item?.EffectiveMutedForeground ?? "#667085",
            runningStatusBrush: entry.IsValid ? item?.EffectiveAccentForeground ?? "#1D4ED8" : "#B91C1C",
            invalidStatusBrush: "#B91C1C",
            borderBrush: item?.EffectiveBodyBorder ?? "#30343A",
            badgeBackground: kindBadgeBackground,
            badgeForeground: kindBadgeForeground);
    }

    public void RefreshCatalog()
    {
        RefreshFunctions();
    }

    private void ApplyRunningState()
    {
        var item = Item;
        var hasRunningEntry = false;

        foreach (var row in FunctionEntries)
        {
            var normalizedReference = FunctionRegistry.NormalizeReference(row.Reference);
            var isRunning = item?.IsCatalogFunctionRunning(row.Reference) == true;
            if (!isRunning)
            {
                _stopRequestedReferences.Remove(normalizedReference);
            }

            var isStopping = isRunning
                && (_stopRequestedReferences.Contains(normalizedReference) || item?.IsCatalogFunctionStopping(row.Reference) == true);
            row.UpdateExecutionState(isRunning: isRunning, isStopping: isStopping);
            hasRunningEntry |= isRunning;
        }

        if (hasRunningEntry)
        {
            if (!_runningStateTimer.IsEnabled)
            {
                _runningStateTimer.Start();
            }
        }
        else
        {
            StopRunningStateTimer();
        }
    }

    private void StopRunningStateTimer()
    {
        if (_runningStateTimer.IsEnabled)
        {
            _runningStateTimer.Stop();
        }
    }

    private static string GetDisplayKindText(FunctionCatalogKind kind)
        => kind switch
        {
            FunctionCatalogKind.Declarative => "YAML",
            FunctionCatalogKind.Python => "Python",
            _ => kind.ToString()
        };

    private static string GetCompactStatusText(FunctionCatalogEntry entry, bool isRunning, bool isStopping)
    {
        if (isStopping)
        {
            return "Stopping";
        }

        if (isRunning)
        {
            return "Running";
        }

        return entry.IsValid
            ? "Ready"
            : "Invalid";
    }

    private static string GetCompactStatusToolTipText(FunctionCatalogEntry entry, bool isRunning, bool isStopping)
    {
        if (isStopping)
        {
            return $"Stopping {GetDisplayKindText(entry.Kind)} function '{entry.Name}'.";
        }

        if (isRunning)
        {
            return $"Running {GetDisplayKindText(entry.Kind)} function '{entry.Name}'.";
        }

        if (entry.IsValid)
        {
            return $"{GetDisplayKindText(entry.Kind)} function '{entry.Name}' is ready.";
        }

        return string.IsNullOrWhiteSpace(entry.StatusText)
            ? $"{GetDisplayKindText(entry.Kind)} function '{entry.Name}' is invalid."
            : entry.StatusText;
    }

    private static string BuildToolTipText(FunctionCatalogEntry entry, string fileName, bool isRunning, bool isStopping)
    {
        var details = new List<string>
        {
            entry.Name,
            $"Type: {GetDisplayKindText(entry.Kind)}",
            $"File: {fileName}",
            $"Source: {entry.DisplaySource}",
            $"Reference: {entry.Reference}"
        };

        details.Add($"State: {GetCompactStatusText(entry, isRunning, isStopping)}");

        if (!string.IsNullOrWhiteSpace(entry.StatusText))
        {
            details.Add(entry.StatusText);
        }

        return string.Join(Environment.NewLine, details.Where(static detail => !string.IsNullOrWhiteSpace(detail)));
    }

    private static void UpdateFooter(FolderItemModel? item, string footer)
    {
        if (item is not null)
        {
            item.Footer = footer;
        }
    }

    private static string GetFolderDirectory(FolderItemModel? item)
    {
        var layoutPath = item?.FolderLayoutPath;
        return string.IsNullOrWhiteSpace(layoutPath)
            ? string.Empty
            : (Path.GetDirectoryName(layoutPath) ?? string.Empty);
    }

    private void EnsureOwningPageSaved(FolderItemModel item)
    {
        if (ViewModel is { } viewModel && !viewModel.TrySaveOwningPageYaml(item, out _))
        {
            viewModel.QueueSaveOwningPageYaml(item);
        }
    }

    private static bool TryGetFunctionCatalogRowFromActionSource(object? source, out FunctionCatalogRow row)
    {
        row = source switch
        {
            Button { CommandParameter: FunctionCatalogRow commandRow } => commandRow,
            Button { Tag: FunctionCatalogRow tagRow } => tagRow,
            Button { DataContext: FunctionCatalogRow contextRow } => contextRow,
            _ => null!
        };

        return row is not null;
    }

    private static IEnumerable<string> GetProcessLogTargetOptions()
    {
        return HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display)
            .Select(key => HostRegistries.Data.TryGet(key, out var item) ? (Key: key, Item: item) : (Key: (string?)null, Item: null))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Item?.Value is HornetStudio.Logging.ProcessLog)
            .Select(entry => entry.Key!)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Provides display state for one function catalog row in the Functions widget.
/// </summary>
public sealed class FunctionCatalogRow : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionCatalogRow"/> class.
    /// </summary>
    /// <param name="catalogEntry">The backing catalog entry.</param>
    /// <param name="displayName">The display name shown in the row.</param>
    /// <param name="fileName">The file name or reference shown in diagnostics.</param>
    /// <param name="sourceText">The compact source text.</param>
    /// <param name="displayKindText">The display text for the type badge.</param>
    /// <param name="compactStatusText">The compact status text.</param>
    /// <param name="compactStatusToolTipText">The tooltip for the compact status text.</param>
    /// <param name="toolTipText">The row tooltip text.</param>
    /// <param name="statusText">The validation or status details from the catalog entry.</param>
    /// <param name="isRunning">Whether this function is currently running.</param>
    /// <param name="isStopping">Whether this function has a pending stop request.</param>
    /// <param name="titleBrush">The row title brush.</param>
    /// <param name="subtitleBrush">The row subtitle brush.</param>
    /// <param name="readyStatusBrush">The status brush for ready entries.</param>
    /// <param name="runningStatusBrush">The status brush for running or stopping entries.</param>
    /// <param name="invalidStatusBrush">The status brush for invalid entries.</param>
    /// <param name="borderBrush">The row border brush.</param>
    /// <param name="badgeBackground">The type badge background brush.</param>
    /// <param name="badgeForeground">The type badge foreground brush.</param>
    public FunctionCatalogRow(
        FunctionCatalogEntry catalogEntry,
        string displayName,
        string fileName,
        string sourceText,
        string displayKindText,
        string compactStatusText,
        string compactStatusToolTipText,
        string toolTipText,
        string statusText,
        bool isRunning,
        bool isStopping,
        string titleBrush,
        string subtitleBrush,
        string readyStatusBrush,
        string runningStatusBrush,
        string invalidStatusBrush,
        string borderBrush,
        string badgeBackground,
        string badgeForeground)
    {
        CatalogEntry = catalogEntry;
        DisplayName = displayName;
        FileName = fileName;
        SourceText = sourceText;
        DisplayKindText = displayKindText;
        CompactStatusText = compactStatusText;
        CompactStatusToolTipText = compactStatusToolTipText;
        ToolTipText = toolTipText;
        StatusText = statusText;
        _isRunning = isRunning;
        _isStopping = isStopping;
        _readyStatusBrush = Brush.Parse(readyStatusBrush);
        _runningStatusBrush = Brush.Parse(runningStatusBrush);
        _invalidStatusBrush = Brush.Parse(invalidStatusBrush);
        TitleBrush = Brush.Parse(titleBrush);
        SubtitleBrush = Brush.Parse(subtitleBrush);
        _statusBrush = GetStatusBrush(isRunning, isStopping);
        BorderBrush = Brush.Parse(borderBrush);
        BadgeBackground = Brush.Parse(badgeBackground);
        BadgeForeground = Brush.Parse(badgeForeground);
    }

    public FunctionCatalogEntry CatalogEntry { get; }

    public string SourceIdentifier => CatalogEntry.SourceIdentifier;

    public string Reference => CatalogEntry.Reference;

    public string DisplayName { get; }

    public string FileName { get; }

    public string SourceText { get; }

    public string DisplayKindText { get; }

    public string CompactStatusText { get; private set; }

    public string CompactStatusToolTipText { get; private set; }

    public string ToolTipText { get; private set; }

    public string StatusText { get; }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsValid => CatalogEntry.IsValid;

    public bool CanEdit => CatalogEntry.CanEdit;

    public bool CanDelete => CatalogEntry.CanDelete;

    public bool CanRun => CatalogEntry.CanRun;

    public bool SupportsStop => CatalogEntry.Kind == FunctionCatalogKind.Declarative && CatalogEntry.CanRun;

    public bool HasCompactStatus => !string.IsNullOrWhiteSpace(CompactStatusText);

    public bool IsStatusHighlighted => !CatalogEntry.IsValid || _isRunning || _isStopping;

    public string RunStopActionText => SupportsStop && (_isRunning || _isStopping) ? "Stop" : "Run";

    public bool IsRunStopVisible => CanRun || IsStopVisible;

    public bool IsRunStopEnabled => IsStopEnabled || (CanRun && IsValid && !_isRunning && !_isStopping);

    public bool IsStopVisible => SupportsStop && (_isRunning || _isStopping);

    public bool IsStopEnabled => SupportsStop && _isRunning && !_isStopping;

    public bool IsEditVisible => CanEdit;

    public bool IsDeleteVisible => CanDelete;

    public IBrush TitleBrush { get; }

    public IBrush SubtitleBrush { get; }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public IBrush BorderBrush { get; }

    public IBrush BadgeBackground { get; }

    public IBrush BadgeForeground { get; }

    private bool _isRunning;

    private bool _isStopping;

    private readonly IBrush _readyStatusBrush;

    private readonly IBrush _runningStatusBrush;

    private readonly IBrush _invalidStatusBrush;

    private IBrush _statusBrush;

    /// <summary>
    /// Updates the row execution state and raises notifications for dependent UI properties.
    /// </summary>
    /// <param name="isRunning">Whether this function is currently running.</param>
    /// <param name="isStopping">Whether this function has a pending stop request.</param>
    public void UpdateExecutionState(bool isRunning, bool isStopping)
    {
        var compactStatusText = isStopping
            ? "Stopping"
            : isRunning
                ? "Running"
                : CatalogEntry.IsValid
                    ? "Ready"
                    : "Invalid";
        var compactStatusToolTipText = isStopping
            ? $"Stopping {DisplayKindText} function '{DisplayName}'."
            : isRunning
                ? $"Running {DisplayKindText} function '{DisplayName}'."
                : CatalogEntry.IsValid
                    ? $"{DisplayKindText} function '{DisplayName}' is ready."
                    : string.IsNullOrWhiteSpace(StatusText)
                        ? $"{DisplayKindText} function '{DisplayName}' is invalid."
                        : StatusText;

        var changed = false;
        if (_isRunning != isRunning)
        {
            _isRunning = isRunning;
            changed = true;
        }

        if (_isStopping != isStopping)
        {
            _isStopping = isStopping;
            changed = true;
        }

        if (!string.Equals(CompactStatusText, compactStatusText, StringComparison.Ordinal))
        {
            CompactStatusText = compactStatusText;
            RaisePropertyChanged(nameof(CompactStatusText));
            RaisePropertyChanged(nameof(HasCompactStatus));
            changed = true;
        }

        if (!string.Equals(CompactStatusToolTipText, compactStatusToolTipText, StringComparison.Ordinal))
        {
            CompactStatusToolTipText = compactStatusToolTipText;
            RaisePropertyChanged(nameof(CompactStatusToolTipText));
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        StatusBrush = GetStatusBrush(isRunning, isStopping);
        ToolTipText = string.Join(Environment.NewLine, new[]
        {
            DisplayName,
            $"Type: {DisplayKindText}",
            $"File: {FileName}",
            SourceText,
            $"Reference: {Reference}",
            $"State: {CompactStatusText}",
            StatusText
        }.Where(static detail => !string.IsNullOrWhiteSpace(detail)));
        RaisePropertyChanged(nameof(ToolTipText));
        RaisePropertyChanged(nameof(IsStatusHighlighted));
        RaisePropertyChanged(nameof(RunStopActionText));
        RaisePropertyChanged(nameof(IsRunStopVisible));
        RaisePropertyChanged(nameof(IsRunStopEnabled));
        RaisePropertyChanged(nameof(IsStopVisible));
        RaisePropertyChanged(nameof(IsStopEnabled));
    }

    private IBrush GetStatusBrush(bool isRunning, bool isStopping)
    {
        if (!CatalogEntry.IsValid)
        {
            return _invalidStatusBrush;
        }

        return isRunning || isStopping
            ? _runningStatusBrush
            : _readyStatusBrush;
    }
}
