using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence.ValueLog;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host.Logging.Values;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Provides one shared browser/editor for folder-local CSV and SQL value logs.
/// </summary>
public sealed partial class ValueLogsBrowserDialogWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly ValueLogDefinitionFileCodec _codec = new();
    private MainWindowViewModel? _subscribedViewModel;
    private FolderModel _folder;
    private ValueLogBrowserEntry? _selectedValueLog;
    private bool _isCreatingNewValueLog;
    private bool _isBusy;
    private string _dialogBackground = "#FFFFFF";
    private string _panelBackground = "#F8FAFC";
    private string _sectionBackground = "#1F2937";
    private string _editorBackground = "#111827";
    private string _editorForeground = "#F9FAFB";
    private string _editorDialogSectionContentBackground = "#1F2937";
    private string _borderColor = "#CBD5E1";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";
    private string _buttonBackground = "#F8FAFC";
    private string _buttonBorderBrush = "#CBD5E1";
    private string _buttonForeground = "#111827";
    private string _valueLogsSummaryText = "0 value logs";
    private string _selectedHeaderText = "No value log selected";
    private string _footerText = "Edit one folder-local CSV or SQL value log and save it back to ValueLog/csv or ValueLog/sql.";
    private string _runtimeDetailsText = "Runtime: -";
    private string _editableId = string.Empty;
    private string _editableText = string.Empty;
    private string _editableKind = "Csv";
    private string _editableOutputDirectory = string.Empty;
    private string _editableOutputFileName = string.Empty;
    private string _editableIntervalMs = "1000";
    private bool _editableEnabled = true;
    private bool _editableAutoStart;
    private bool _editableSplitDaily;
    private string _editableSplitDailyTime = "00:00:00";
    private string _editableSplitMaxFileSizeMb = "0";

    public ValueLogsBrowserDialogWindow()
        : this(null, new FolderModel())
    {
    }

    public ValueLogsBrowserDialogWindow(MainWindowViewModel? viewModel, FolderModel folder)
    {
        _viewModel = viewModel;
        _folder = folder;
        ValueLogs = [];
        SourceRows = [];
        KindOptions = ["Csv", "Sql"];
        InitializeComponent();
        DataContext = this;
        AttachToViewModel(viewModel);
        ApplyFolderContext(folder);
        Closed += OnClosed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ValueLogBrowserEntry> ValueLogs { get; }

    public ObservableCollection<AttachItemEditorRow> SourceRows { get; }

    public IReadOnlyList<string> KindOptions { get; }

    public string DialogBackground
    {
        get => _dialogBackground;
        private set => SetAndRaise(ref _dialogBackground, value);
    }

    public string PanelBackground
    {
        get => _panelBackground;
        private set => SetAndRaise(ref _panelBackground, value);
    }

    public string SectionBackground
    {
        get => _sectionBackground;
        private set => SetAndRaise(ref _sectionBackground, value);
    }

    public string EditorBackground
    {
        get => _editorBackground;
        private set => SetAndRaise(ref _editorBackground, value);
    }

    public string EditorForeground
    {
        get => _editorForeground;
        private set => SetAndRaise(ref _editorForeground, value);
    }

    public string EditorDialogSectionContentBackground
    {
        get => _editorDialogSectionContentBackground;
        private set => SetAndRaise(ref _editorDialogSectionContentBackground, value);
    }

    public string BorderColor
    {
        get => _borderColor;
        private set => SetAndRaise(ref _borderColor, value);
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetAndRaise(ref _primaryTextBrush, value);
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetAndRaise(ref _secondaryTextBrush, value);
    }

    public string ButtonBackground
    {
        get => _buttonBackground;
        private set => SetAndRaise(ref _buttonBackground, value);
    }

    public string ButtonBorderBrush
    {
        get => _buttonBorderBrush;
        private set => SetAndRaise(ref _buttonBorderBrush, value);
    }

    public string ButtonForeground
    {
        get => _buttonForeground;
        private set => SetAndRaise(ref _buttonForeground, value);
    }

    public string ValueLogsSummaryText
    {
        get => _valueLogsSummaryText;
        private set => SetAndRaise(ref _valueLogsSummaryText, value);
    }

    public ValueLogBrowserEntry? SelectedValueLog
    {
        get => _selectedValueLog;
        set
        {
            if (!SetAndRaise(ref _selectedValueLog, value))
            {
                return;
            }

            _isCreatingNewValueLog = false;
            ApplySelectedValueLog(value);
            RaiseEditorStateProperties();
        }
    }

    public bool HasSelectedValueLog => SelectedValueLog is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetAndRaise(ref _isBusy, value))
            {
                RaiseEditorStateProperties();
            }
        }
    }

    public bool CanPersistValueLog => !IsBusy && (HasSelectedValueLog || _isCreatingNewValueLog);

    public bool CanChangeSelection => !IsBusy;

    public bool CanDeleteSelected => !IsBusy && HasSelectedValueLog;

    public bool CanStopSelected => !IsBusy && HasSelectedValueLog;

    public bool IsSqlKind => string.Equals(EditableKind, ValueLogKind.Sql.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool ShowDefinitionInterval => !IsSqlKind;

    public bool ShowSourceIntervalColumn => IsSqlKind;

    public bool ShowEmptySourcesMessage => SourceRows.Count == 0;

    public string SourcesSummaryText
    {
        get
        {
            var selectedCount = SourceRows.Count(static row => row.IsAttached && !row.IsRemoved);
            var availableCount = SourceRows.Count(static row => !row.IsRemoved);
            if (availableCount == 0)
            {
                return "No signal sources available in this folder.";
            }

            if (IsSqlKind)
            {
                return $"{selectedCount} of {availableCount} sources selected. SQL stores IntervalMs per source.";
            }

            return $"{selectedCount} of {availableCount} sources selected. CSV uses the shared IntervalMs setting.";
        }
    }

    public string SelectedHeaderText
    {
        get => _selectedHeaderText;
        private set => SetAndRaise(ref _selectedHeaderText, value);
    }

    public string FooterText
    {
        get => _footerText;
        private set => SetAndRaise(ref _footerText, value);
    }

    public string RuntimeDetailsText
    {
        get => _runtimeDetailsText;
        private set => SetAndRaise(ref _runtimeDetailsText, value);
    }

    public string EditableId
    {
        get => _editableId;
        set => SetAndRaise(ref _editableId, value);
    }

    public string EditableText
    {
        get => _editableText;
        set => SetAndRaise(ref _editableText, value);
    }

    public string EditableKind
    {
        get => _editableKind;
        set
        {
            if (!SetAndRaise(ref _editableKind, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsSqlKind));
            RaisePropertyChanged(nameof(ShowDefinitionInterval));
            RaisePropertyChanged(nameof(ShowSourceIntervalColumn));
            RaisePropertyChanged(nameof(SourcesSummaryText));
        }
    }

    public string EditableOutputDirectory
    {
        get => _editableOutputDirectory;
        set => SetAndRaise(ref _editableOutputDirectory, value);
    }

    public string EditableOutputFileName
    {
        get => _editableOutputFileName;
        set => SetAndRaise(ref _editableOutputFileName, value);
    }

    public string EditableIntervalMs
    {
        get => _editableIntervalMs;
        set => SetAndRaise(ref _editableIntervalMs, value);
    }

    public bool EditableEnabled
    {
        get => _editableEnabled;
        set => SetAndRaise(ref _editableEnabled, value);
    }

    public bool EditableAutoStart
    {
        get => _editableAutoStart;
        set => SetAndRaise(ref _editableAutoStart, value);
    }

    public bool EditableSplitDaily
    {
        get => _editableSplitDaily;
        set => SetAndRaise(ref _editableSplitDaily, value);
    }

    public string EditableSplitDailyTime
    {
        get => _editableSplitDailyTime;
        set => SetAndRaise(ref _editableSplitDailyTime, value);
    }

    public string EditableSplitMaxFileSizeMb
    {
        get => _editableSplitMaxFileSizeMb;
        set => SetAndRaise(ref _editableSplitMaxFileSizeMb, value);
    }

    public static ValueLogsBrowserDialogWindow ShowOrActivate(Window? owner, MainWindowViewModel? viewModel, FolderModel folder)
    {
        var dialog = new ValueLogsBrowserDialogWindow(viewModel, folder)
        {
            Owner = owner
        };

        if (owner is null)
        {
            dialog.Show();
        }
        else
        {
            dialog.Show(owner);
        }

        return dialog;
    }

    public void UpdateFolderContext(FolderModel folder)
    {
        if (folder is null)
        {
            return;
        }

        _folder = folder;
        ApplyFolderContext(folder);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            RefreshThemeBindings();
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshThemeBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder))
        {
            UpdateFolderContext(_viewModel?.SelectedFolder ?? _folder);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.DialogBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelInputBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelInputForeground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionContentBackground))
        {
            RefreshThemeBindings();
        }
    }

    private void RefreshThemeBindings()
    {
        DialogBackground = _viewModel?.DialogBackground ?? "#FFFFFF";
        PanelBackground = _viewModel?.CardBackground ?? "#F8FAFC";
        SectionBackground = _viewModel?.CardBackground ?? "#F8FAFC";
        EditorBackground = _viewModel?.EditPanelInputBackground ?? "#111827";
        EditorForeground = _viewModel?.EditPanelInputForeground ?? "#F9FAFB";
        EditorDialogSectionContentBackground = _viewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
        BorderColor = _viewModel?.CardBorderBrush ?? "#CBD5E1";
        PrimaryTextBrush = _viewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = _viewModel?.SecondaryTextBrush ?? "#5E6777";
        ButtonBackground = _viewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = _viewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = _viewModel?.PrimaryTextBrush ?? "#111827";
    }

    private void ApplyFolderContext(FolderModel folder)
    {
        Title = $"Value Logs - {folder.Name}";
        ReloadValueLogs();
    }

    private void ReloadValueLogs()
    {
        ValueLogs.Clear();

        var folderDirectory = TryGetFolderDirectory(_folder);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            ValueLogsSummaryText = "0 value logs";
            SourceRows.Clear();
            RaiseSourceProperties();
            SelectedValueLog = null;
            return;
        }

        foreach (var entry in _codec.LoadFolder(folderDirectory))
        {
            var isRunning = LogManager.TryGetStatus(_folder.Name, entry.Definition.Id, out var status) && status.IsRunning;
            ValueLogs.Add(new ValueLogBrowserEntry(entry, isRunning));
        }

        ValueLogsSummaryText = ValueLogs.Count == 1 ? "1 value log" : $"{ValueLogs.Count} value logs";
        if (ValueLogs.Count == 0)
        {
            if (!_isCreatingNewValueLog)
            {
                SelectedValueLog = null;
            }

            return;
        }

        if (_isCreatingNewValueLog)
        {
            BuildSourceRows(Array.Empty<ValueLogSourceDefinition>(), preserveSelection: true);
            return;
        }

        var previousId = SelectedValueLog?.Definition.Id;
        SelectedValueLog = ValueLogs.FirstOrDefault(candidate => string.Equals(candidate.Definition.Id, previousId, StringComparison.OrdinalIgnoreCase))
            ?? ValueLogs[0];
    }

    private void ApplySelectedValueLog(ValueLogBrowserEntry? entry)
    {
        if (entry is null)
        {
            ResetEditorForNewDefinition(ValueLogKind.Csv);
            SelectedHeaderText = "No value log selected";
            RuntimeDetailsText = "Runtime: -";
            FooterText = "Choose Add CSV or Add SQL to create a new value log.";
            return;
        }

        SelectedHeaderText = entry.DisplayName;
        EditableId = entry.Definition.Id;
        EditableText = entry.Definition.Text;
        EditableKind = entry.Definition.Kind.ToString();
        ApplyOutputParts(entry.Definition.OutputPath, entry.Definition.Kind);
        EditableIntervalMs = entry.Definition.IntervalMs.ToString();
        EditableEnabled = entry.Definition.Enabled;
        EditableAutoStart = entry.Definition.AutoStart;
        EditableSplitDaily = entry.Definition.SplitDaily;
        EditableSplitDailyTime = entry.Definition.SplitDailyTime;
        EditableSplitMaxFileSizeMb = entry.Definition.SplitMaxFileSizeMb.ToString();
        BuildSourceRows(entry.Definition.Sources);
        RuntimeDetailsText = entry.IsRunning
            ? $"Runtime: Running | Output: {entry.Definition.OutputPath}"
            : $"Runtime: Stopped | Output: {entry.Definition.OutputPath}";
        FooterText = "Edit one folder-local CSV or SQL value log and save it back to ValueLog/csv or ValueLog/sql.";
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        ReloadValueLogs();
        e.Handled = true;
    }

    private void OnRefreshSourcesClicked(object? sender, RoutedEventArgs e)
    {
        BuildSourceRows(GetCurrentSourceDefinitions(), preserveSelection: true);
        FooterText = "Refreshed available signal sources for this folder.";
        e.Handled = true;
    }

    private async void OnPickOutputDirectoryClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        IStorageFolder? startFolder = null;
        var initialDirectory = ResolveInitialOutputDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            startFolder = await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        var selectedPath = TryGetStoragePath(result.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            EditableOutputDirectory = selectedPath;
            FooterText = "Updated output directory.";
        }

        e.Handled = true;
    }

    private void OnAddCsvClicked(object? sender, RoutedEventArgs e)
    {
        StartNewDefinition(ValueLogKind.Csv);
        FooterText = "Creating a new CSV value log.";
        e.Handled = true;
    }

    private void OnAddSqlClicked(object? sender, RoutedEventArgs e)
    {
        StartNewDefinition(ValueLogKind.Sql);
        FooterText = "Creating a new SQL value log.";
        e.Handled = true;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folderDirectory = TryGetFolderDirectory(_folder);
            if (string.IsNullOrWhiteSpace(folderDirectory))
            {
                throw new InvalidOperationException("Could not resolve folder directory.");
            }

            var definition = BuildEditedDefinition();
            var savedFilePath = _codec.SaveDefinition(folderDirectory, definition, SelectedValueLog?.FilePath);
            IsBusy = true;
            FooterText = $"Saving '{definition.Id}'...";
            _ = CompleteSaveAsync(folderDirectory, definition, savedFilePath);
        }
        catch (Exception ex)
        {
            FooterText = $"Save failed: {ex.Message}";
        }

        e.Handled = true;
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedValueLog is null || IsBusy)
        {
            return;
        }

        var selected = SelectedValueLog;
        IsBusy = true;
        FooterText = $"Deleting '{selected.Definition.Id}'...";
        _ = DeleteValueLogAsync(selected);
        e.Handled = true;
    }

    private void OnStartClicked(object? sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        var definition = BuildEditedDefinition();
        try
        {
            var folderDirectory = TryGetFolderDirectory(_folder);
            if (string.IsNullOrWhiteSpace(folderDirectory))
            {
                throw new InvalidOperationException("Could not resolve folder directory.");
            }

            _codec.SaveDefinition(folderDirectory, definition, SelectedValueLog?.FilePath);
            IsBusy = true;
            FooterText = $"Starting '{definition.Id}'...";
            _ = StartValueLogAsync(folderDirectory, definition);
        }
        catch (Exception ex)
        {
            FooterText = $"Start failed: {ex.Message}";
        }

        e.Handled = true;
    }

    private void OnStopClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedValueLog is null || IsBusy)
        {
            return;
        }

        var stoppedId = SelectedValueLog.Definition.Id;
        IsBusy = true;
        FooterText = $"Stopping '{stoppedId}'...";
        _ = StopValueLogAsync(stoppedId);
        e.Handled = true;
    }

    private void OnRemoveMissingSourceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AttachItemEditorRow row })
        {
            row.IsRemoved = true;
            row.PropertyChanged -= OnSourceRowPropertyChanged;
            SourceRows.Remove(row);
            RaiseSourceProperties();
        }

        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        Closed -= OnClosed;
    }

    private void StartNewDefinition(ValueLogKind kind)
    {
        _isCreatingNewValueLog = true;
        _selectedValueLog = null;
        ResetEditorForNewDefinition(kind);
        SelectedHeaderText = $"New {kind} value log";
        RuntimeDetailsText = "Runtime: Draft";
        RaisePropertyChanged(nameof(SelectedValueLog));
        RaiseEditorStateProperties();
    }

    private void ResetEditorForNewDefinition(ValueLogKind kind)
    {
        EditableId = string.Empty;
        EditableText = string.Empty;
        EditableKind = kind.ToString();
        EditableOutputDirectory = ResolveInitialOutputDirectory();
        EditableOutputFileName = kind == ValueLogKind.Sql ? "value_log.db" : "value_log.csv";
        EditableIntervalMs = "1000";
        EditableEnabled = true;
        EditableAutoStart = false;
        EditableSplitDaily = false;
        EditableSplitDailyTime = "00:00:00";
        EditableSplitMaxFileSizeMb = "0";
        BuildSourceRows(Array.Empty<ValueLogSourceDefinition>(), preserveSelection: false);
    }

    private ValueLogDefinition BuildEditedDefinition()
    {
        var kind = Enum.TryParse<ValueLogKind>(EditableKind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : ValueLogKind.Csv;
        var intervalMs = ParsePositiveInt(EditableIntervalMs, fallback: 1000);

        var outputPath = BuildOutputPath(kind);

        return new ValueLogDefinition
        {
            Id = EditableId?.Trim() ?? string.Empty,
            Text = EditableText?.Trim() ?? string.Empty,
            Kind = kind,
            Enabled = EditableEnabled,
            AutoStart = EditableAutoStart,
            OutputPath = outputPath,
            IntervalMs = intervalMs,
            SplitDaily = EditableSplitDaily,
            SplitDailyTime = string.IsNullOrWhiteSpace(EditableSplitDailyTime) ? "00:00:00" : EditableSplitDailyTime.Trim(),
            SplitMaxFileSizeMb = ParseNonNegativeInt(EditableSplitMaxFileSizeMb),
            PersistenceMode = "Balanced",
            FlushIntervalMs = 0,
            FlushBatchSize = 0,
            Sources = BuildSelectedSources(kind, intervalMs)
        };
    }

    private IReadOnlyList<ValueLogSourceDefinition> BuildSelectedSources(ValueLogKind kind, int definitionIntervalMs)
    {
        var selectedRows = SourceRows
            .Where(static row => row.IsAttached && !row.IsRemoved)
            .ToArray();
        var sources = new List<ValueLogSourceDefinition>(selectedRows.Length);
        foreach (var row in selectedRows)
        {
            var targetPath = row.Source.Trim();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                continue;
            }

            sources.Add(new ValueLogSourceDefinition
            {
                TargetPath = targetPath,
                IntervalMs = kind == ValueLogKind.Sql
                    ? Math.Max(1, row.IntervalMs > 0 ? row.IntervalMs : definitionIntervalMs)
                    : null
            });
        }

        return sources;
    }

    private IReadOnlyList<ValueLogSourceDefinition> GetCurrentSourceDefinitions()
        => BuildSelectedSources(IsSqlKind ? ValueLogKind.Sql : ValueLogKind.Csv, ParsePositiveInt(EditableIntervalMs, 1000));

    private void BuildSourceRows(IEnumerable<ValueLogSourceDefinition> sources, bool preserveSelection = false)
    {
        var sourceList = sources?.ToArray() ?? Array.Empty<ValueLogSourceDefinition>();
        var selectedByPath = new Dictionary<string, ValueLogSourceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceList)
        {
            if (!string.IsNullOrWhiteSpace(source.TargetPath))
            {
                selectedByPath[source.TargetPath.Trim()] = source;
            }
        }

        if (preserveSelection)
        {
            foreach (var row in SourceRows.Where(static row => row.IsAttached && !row.IsRemoved))
            {
                var path = row.Source.Trim();
                if (!selectedByPath.ContainsKey(path))
                {
                    selectedByPath[path] = new ValueLogSourceDefinition
                    {
                        TargetPath = path,
                        IntervalMs = row.IntervalMs > 0 ? row.IntervalMs : null
                    };
                }
            }
        }

        foreach (var existingRow in SourceRows)
        {
            existingRow.PropertyChanged -= OnSourceRowPropertyChanged;
        }

        SourceRows.Clear();

        var optionsByPath = GetSignalSourceOptions()
            .GroupBy(static option => ExtractOptionPath(option), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var option in optionsByPath.Values.OrderBy(static option => option, StringComparer.OrdinalIgnoreCase))
        {
            var optionPath = ExtractOptionPath(option);
            selectedByPath.TryGetValue(optionPath, out var existingSource);
            AddSourceRow(
                option,
                isAttached: existingSource is not null,
                isMissing: false,
                intervalMs: existingSource?.IntervalMs ?? ParsePositiveInt(EditableIntervalMs, 1000));
        }

        foreach (var missingSource in selectedByPath.Values.Where(source => !optionsByPath.ContainsKey(source.TargetPath)).OrderBy(static source => source.Name, StringComparer.OrdinalIgnoreCase))
        {
            AddSourceRow(
                missingSource.TargetPath,
                isAttached: true,
                isMissing: true,
                intervalMs: missingSource.IntervalMs ?? ParsePositiveInt(EditableIntervalMs, 1000),
                displayName: missingSource.TargetPath,
                displaySource: missingSource.TargetPath);
        }

        RaiseSourceProperties();
    }

    private void AddSourceRow(string option, bool isAttached, bool isMissing, int intervalMs, string? displayName = null, string? displaySource = null)
    {
        var row = new AttachItemEditorRow
        {
            RelativePath = option,
            IsAttached = isAttached,
            IsMissing = isMissing,
            DisplayName = displayName ?? string.Empty,
            DisplaySource = displaySource ?? string.Empty,
            IntervalMs = Math.Max(1, intervalMs)
        };

        row.PropertyChanged += OnSourceRowPropertyChanged;
        SourceRows.Add(row);
    }

    private void OnSourceRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(AttachItemEditorRow.IsAttached)
            and not nameof(AttachItemEditorRow.IsRemoved)
            and not nameof(AttachItemEditorRow.IntervalMs))
        {
            return;
        }

        RaiseSourceProperties();
    }

    private IReadOnlyList<string> GetSignalSourceOptions()
    {
        var folderName = TargetPathHelper.NormalizeConfiguredTargetPath(_folder.Name);
        return EnumeratePageItems(_folder.Items)
            .Where(static item => item.Kind == ControlKind.Signal)
            .Select(item =>
            {
                var targetPath = TargetPathHelper.ToPersistedLayoutTargetPath(item.TargetPath, folderName);
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return string.Empty;
                }

                var name = !string.IsNullOrWhiteSpace(item.Name)
                    ? item.Name.Trim()
                    : (!string.IsNullOrWhiteSpace(item.Title)
                        ? item.Title.Trim()
                        : (!string.IsNullOrWhiteSpace(item.BodyCaption)
                            ? item.BodyCaption.Trim()
                            : targetPath));
                var unit = item.Unit?.Trim() ?? string.Empty;

                return string.IsNullOrWhiteSpace(unit)
                    ? $"{name}|{targetPath}"
                    : $"{name}|{targetPath}|{unit}";
            })
            .Where(static option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static option => option, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<FolderItemModel> EnumeratePageItems(IEnumerable<FolderItemModel> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in EnumeratePageItems(item.Items))
            {
                yield return child;
            }
        }
    }

    private static string ExtractOptionPath(string option)
    {
        var parts = option.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[1].Trim() : option.Trim();
    }

    private static int ParsePositiveInt(string? raw, int fallback)
        => int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;

    private static int ParseNonNegativeInt(string? raw)
        => int.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : 0;

    private string BuildOutputPath(ValueLogKind kind)
    {
        var directory = EditableOutputDirectory?.Trim() ?? string.Empty;
        var fileName = EnsureOutputExtension(EditableOutputFileName?.Trim() ?? string.Empty, kind);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return directory;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return fileName;
        }

        return Path.Combine(directory, fileName);
    }

    private void ApplyOutputParts(string outputPath, ValueLogKind kind)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            EditableOutputDirectory = ResolveInitialOutputDirectory();
            EditableOutputFileName = kind == ValueLogKind.Sql ? "value_log.db" : "value_log.csv";
            return;
        }

        EditableOutputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileName = Path.GetFileName(outputPath);
        EditableOutputFileName = EnsureOutputExtension(fileName, kind);
    }

    private string ResolveInitialOutputDirectory()
    {
        var folderDirectory = TryGetFolderDirectory(_folder);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return string.Empty;
        }

        return Path.Combine(folderDirectory, "Logs");
    }

    private static string EnsureOutputExtension(string fileName, ValueLogKind kind)
    {
        var trimmed = fileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var expectedExtension = kind == ValueLogKind.Sql ? ".db" : ".csv";
        return string.Equals(Path.GetExtension(trimmed), expectedExtension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}{expectedExtension}";
    }

    private static string? TryGetStoragePath(IStorageItem? item)
    {
        if (item?.TryGetLocalPath() is { } localPath && !string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        return null;
    }

    private async Task CompleteSaveAsync(string folderDirectory, ValueLogDefinition definition, string savedFilePath)
    {
        try
        {
            await SyncValueLogsDirectAsync(folderDirectory).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isCreatingNewValueLog = false;
                ReloadValueLogs();
                SelectedValueLog = ValueLogs.FirstOrDefault(candidate => string.Equals(candidate.FilePath, savedFilePath, StringComparison.OrdinalIgnoreCase))
                    ?? ValueLogs.FirstOrDefault(candidate => string.Equals(candidate.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
                FooterText = $"Saved '{definition.Id}'.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FooterText = $"Save failed: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task StartValueLogAsync(string folderDirectory, ValueLogDefinition definition)
    {
        try
        {
            await SyncValueLogsDirectAsync(folderDirectory).ConfigureAwait(false);
            var runtimePath = TryGetRuntimePath(definition.Id);
            if (string.IsNullOrWhiteSpace(runtimePath) || !HostRegistries.Data.UpdateValue($"{runtimePath}.run", true))
            {
                throw new InvalidOperationException($"ValueLog '{definition.Id}' run item could not be updated.");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isCreatingNewValueLog = false;
                ReloadValueLogs();
                SelectedValueLog = ValueLogs.FirstOrDefault(candidate => string.Equals(candidate.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
                FooterText = $"Starting '{definition.Id}'.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FooterText = $"Start failed: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task StopValueLogAsync(string valueLogId)
    {
        try
        {
            var runtimePath = TryGetRuntimePath(valueLogId);
            if (string.IsNullOrWhiteSpace(runtimePath) || !HostRegistries.Data.UpdateValue($"{runtimePath}.run", false))
            {
                throw new InvalidOperationException($"ValueLog '{valueLogId}' run item could not be updated.");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReloadValueLogs();
                SelectedValueLog = ValueLogs.FirstOrDefault(candidate => string.Equals(candidate.Definition.Id, valueLogId, StringComparison.OrdinalIgnoreCase));
                FooterText = $"Stopping '{valueLogId}'.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FooterText = $"Stop failed: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task DeleteValueLogAsync(ValueLogBrowserEntry entry)
    {
        try
        {
            try
            {
                var runtimePath = TryGetRuntimePath(entry.Definition.Id);
                if (!string.IsNullOrWhiteSpace(runtimePath))
                {
                    HostRegistries.Data.UpdateValue($"{runtimePath}.run", false);
                }
            }
            catch
            {
            }

            _codec.DeleteDefinition(entry.FilePath);
            var folderDirectory = TryGetFolderDirectory(_folder);
            if (!string.IsNullOrWhiteSpace(folderDirectory))
            {
                await SyncValueLogsDirectAsync(folderDirectory).ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReloadValueLogs();
                FooterText = "Deleted selected value log.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FooterText = $"Delete failed: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private Task SyncValueLogsDirectAsync(string folderDirectory)
    {
        return Task.Run(() =>
        {
            var definitions = _codec.LoadFolder(folderDirectory)
                .Select(static entry => entry.Definition)
                .GroupBy(static definition => definition.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
            LogManager.SyncDefinitions(_folder.Name, definitions);
        });
    }

    private string? TryGetRuntimePath(string valueLogId)
    {
        return LogManager.TryGetStatus(_folder.Name, valueLogId, out var status)
            ? status.RuntimePath
            : null;
    }

    private static string TryGetFolderDirectory(FolderModel folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.UiFilePath))
        {
            return Path.GetDirectoryName(folder.UiFilePath) ?? string.Empty;
        }

        return string.Empty;
    }

    private void RaiseSourceProperties()
    {
        RaisePropertyChanged(nameof(ShowEmptySourcesMessage));
        RaisePropertyChanged(nameof(SourcesSummaryText));
    }

    private void RaiseEditorStateProperties()
    {
        RaisePropertyChanged(nameof(HasSelectedValueLog));
        RaisePropertyChanged(nameof(CanPersistValueLog));
        RaisePropertyChanged(nameof(CanChangeSelection));
        RaisePropertyChanged(nameof(CanDeleteSelected));
        RaisePropertyChanged(nameof(CanStopSelected));
        RaisePropertyChanged(nameof(IsBusy));
    }

    private bool SetAndRaise<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ValueLogBrowserEntry
{
    public ValueLogBrowserEntry(ValueLogFileEntry fileEntry, bool isRunning)
    {
        FilePath = fileEntry.FilePath;
        Definition = fileEntry.Definition;
        IsRunning = isRunning;
    }

    public string FilePath { get; }

    public ValueLogDefinition Definition { get; }

    public bool IsRunning { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Definition.Text) ? Definition.Id : $"{Definition.Text} ({Definition.Id})";

    public string SummaryText => $"{Definition.Kind} | {Definition.Sources.Count} sources | {Definition.OutputPath}";

    public string StatusText => IsRunning ? "Running" : "Stopped";

    public string RowBackground => "#00000000";

    public string RowBorderBrush => IsRunning ? "#16A34A" : "#CBD5E1";
}
