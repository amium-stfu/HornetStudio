using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets.Common;
using HornetStudio.Host.Python.Client;

namespace HornetStudio.Editor.Widgets;

public partial class MonitorEditorDialogWindow : Window
{
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private readonly FolderItemModel _ownerItem = new();
    private IReadOnlyList<string> _sourceOptions = Array.Empty<string>();
    private IReadOnlyList<string> _targetLogOptions = Array.Empty<string>();

    public MonitorEditorDialogWindow()
    {
        ViewModel = new MonitorEditorDialogViewModel(null, new FolderItemModel(), null, Array.Empty<string>());
        DataContext = ViewModel;
        InitializeComponent();
    }

    public MonitorEditorDialogWindow(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, MonitorDefinition? definition, IEnumerable<string> sourceOptions, IEnumerable<string> targetLogOptions)
        : this()
    {
        _mainWindowViewModel = mainWindowViewModel;
        _ownerItem = ownerItem;
        _sourceOptions = sourceOptions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        _targetLogOptions = targetLogOptions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).Prepend(string.Empty).ToArray();
        ViewModel = new MonitorEditorDialogViewModel(mainWindowViewModel, ownerItem, definition, _targetLogOptions);
        DataContext = ViewModel;
    }

    public MonitorEditorDialogViewModel ViewModel { get; private set; }

    public static Task<MonitorDefinition?> ShowAsync(Window owner, MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, MonitorDefinition? definition, IEnumerable<string> sourceOptions, IEnumerable<string> targetLogOptions)
    {
        var dialog = new MonitorEditorDialogWindow(mainWindowViewModel, ownerItem, definition, sourceOptions, targetLogOptions);
        return dialog.ShowDialog<MonitorDefinition?>(owner);
    }

    private async void OnPickSourceClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new TargetTreeSelectionDialogWindow(_mainWindowViewModel, _sourceOptions, ViewModel.SourcePath, _ownerItem.FolderName);
        await dialog.ShowDialog(this);
        if (!string.IsNullOrWhiteSpace(dialog.CommittedSelection))
        {
            ViewModel.SourcePath = dialog.CommittedSelection;
        }

        e.Handled = true;
    }

    private async void OnPickTargetLogClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new TargetTreeSelectionDialogWindow(_mainWindowViewModel, _targetLogOptions, string.Empty, _ownerItem.FolderName);
        await dialog.ShowDialog(this);
        if (dialog.CommittedSelection is not null)
        {
            ViewModel.AddAction(dialog.CommittedSelection);
        }

        e.Handled = true;
    }

    private async void OnPickActionTargetLogClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: MonitorActionEntryViewModel action })
        {
            return;
        }

        var options = GetActionTargetOptions(action);
        var selectedValue = action.IsWriteLogAction ? action.TargetLog : action.TargetPath;
        var dialog = new TargetTreeSelectionDialogWindow(_mainWindowViewModel, options, selectedValue, _ownerItem.FolderName);
        await dialog.ShowDialog(this);
        if (dialog.CommittedSelection is not null)
        {
            if (action.IsWriteLogAction)
            {
                action.TargetLog = dialog.CommittedSelection;
            }
            else
            {
                action.TargetPath = dialog.CommittedSelection;
            }
        }

        e.Handled = true;
    }

    private IReadOnlyList<string> GetActionTargetOptions(MonitorActionEntryViewModel action)
    {
        return action.SelectedActionType switch
        {
            nameof(MonitorActionType.WriteLog) => _targetLogOptions,
            nameof(MonitorActionType.InvokeFunction) => PythonClientRuntimeRegistry.GetRegisteredTargetPaths()
                .Select(ApplicationExplorerRuntime.ToPersistedInteractionTargetPath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => _sourceOptions
        };
    }

    private void OnAddActionClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddAction();
        e.Handled = true;
    }

    private void OnRemoveActionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: MonitorActionEntryViewModel action })
        {
            return;
        }

        ViewModel.RemoveAction(action);
        e.Handled = true;
    }

    private async void OnConditionVariableSourcePickRequested(object? sender, ConditionVariablePickerRequestedEventArgs e)
    {
        var dialog = new TargetTreeSelectionDialogWindow(_mainWindowViewModel, _sourceOptions, e.Variable.SourcePath, _ownerItem.FolderName);
        await dialog.ShowDialog(this);
        if (!string.IsNullOrWhiteSpace(dialog.CommittedSelection))
        {
            e.Variable.SourcePath = dialog.CommittedSelection;
        }
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildDefinition(out var definition, out var errorMessage))
        {
            ViewModel.ErrorMessage = errorMessage;
            return;
        }

        Close(definition);
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close((MonitorDefinition?)null);
        e.Handled = true;
    }
}

public sealed class MonitorEditorDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> ModeOptions = Enum.GetNames<MonitorRuleMode>();
    private static readonly IReadOnlyList<string> LogLevelOptions = Enum.GetNames<MonitorLogLevel>();
    private readonly Func<IReadOnlyList<MonitorDefinition>> _registryDefinitionsProvider;
    private readonly FolderItemModel _ownerItem;
    private readonly string _originalName;
    private string _selectedMode = MonitorRuleMode.Default.ToString();
    private string _name = string.Empty;
    private string _sourcePath = string.Empty;
    private string _refreshRateMsText = "1000";
    private string _timeoutMsText = string.Empty;
    private string _lowerLimit = string.Empty;
    private string _upperLimit = string.Empty;
    private string _inhibitMsText = string.Empty;
    private string _eventIdText = "0";
    private string _eventText = string.Empty;
    private string _selectedLogLevel = MonitorLogLevel.Warning.ToString();
    private string _errorMessage = string.Empty;

    public MonitorEditorDialogViewModel(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, MonitorDefinition? definition, IEnumerable<string> targetLogOptions)
    {
        _ownerItem = ownerItem;
        _originalName = definition?.Name ?? string.Empty;
        _registryDefinitionsProvider = () => EnumerateRegistryDefinitions(mainWindowViewModel, _ownerItem);
        var useOwnerTheme = IsMonitorBrowserOwner(ownerItem) || mainWindowViewModel is null;
        DialogBackground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveBackground, "#E3E5EE") : mainWindowViewModel!.DialogBackground;
        SectionBackground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveInnerBackground, "#EEF3F8") : mainWindowViewModel!.EditorDialogSectionContentBackground;
        BorderColor = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveBorderBrush, "#CBD5E1") : mainWindowViewModel!.CardBorderBrush;
        ParameterHoverColor = mainWindowViewModel?.ParameterHoverColor ?? ownerItem.EffectiveAccentBackground;
        PrimaryTextBrush = useOwnerTheme ? GetThemeValue(ownerItem.EffectivePrimaryForeground, "#111827") : mainWindowViewModel!.PrimaryTextBrush;
        SecondaryTextBrush = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveSecondaryForeground, "#5E6777") : mainWindowViewModel!.SecondaryTextBrush;
        SectionContentBackground = SectionBackground;
        EditorBackground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveBodyBackground, SectionBackground) : mainWindowViewModel!.ParameterEditBackgrundColor;
        EditorForeground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveBodyForeground, PrimaryTextBrush) : mainWindowViewModel!.ParameterEditForeColor;
        ButtonBackground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveAccentBackground, SectionBackground) : mainWindowViewModel!.EditPanelButtonBackground;
        ButtonBorderBrush = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveBodyBorder, BorderColor) : mainWindowViewModel!.EditPanelButtonBorderBrush;
        ButtonForeground = useOwnerTheme ? GetThemeValue(ownerItem.EffectiveAccentForeground, PrimaryTextBrush) : mainWindowViewModel!.PrimaryTextBrush;
        ConditionEditor = new BooleanConditionEditorViewModel(
            formulaText: definition?.CustomFormula,
            variables: definition?.CustomVariables.Select(static variable => new BooleanConditionVariableDefinition
            {
                Name = variable.Name,
                SourcePath = variable.SourcePath
            }),
            reservedVariableNames: ["value", "source"],
            reservedPreviewVariables: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = 1d,
                ["source"] = 1d
            });
        ConditionEditor.PropertyChanged += OnConditionEditorPropertyChanged;
        TargetLogOptions = targetLogOptions.ToArray();
        Actions.CollectionChanged += OnActionsCollectionChanged;

        if (definition is null)
        {
            Name = GenerateNextRuleName();
            EventIdText = GenerateNextEventId().ToString(CultureInfo.InvariantCulture);
            return;
        }

        Name = definition.Name;
        SourcePath = definition.SourcePath;
        RefreshRateMsText = definition.RefreshRateMs.ToString(CultureInfo.InvariantCulture);
        TimeoutMsText = definition.TimeoutMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SelectedMode = definition.Mode.ToString();
        LowerLimit = definition.LowerLimit;
        UpperLimit = definition.UpperLimit;
        InhibitMsText = definition.InhibitMs > 0 ? definition.InhibitMs.ToString(CultureInfo.InvariantCulture) : string.Empty;
        EventIdText = definition.EventId.ToString(CultureInfo.InvariantCulture);
        EventText = definition.EventText;
        SelectedLogLevel = definition.LogLevel.ToString();

        foreach (var action in definition.Actions)
        {
            AddAction(action.Trigger.ToString(), action.ActionType.ToString(), action.TargetLog, action.TargetPath, action.FunctionName, action.Argument);
        }

        if (Actions.Count == 0 && !string.IsNullOrWhiteSpace(definition.TargetLog))
        {
            AddAction(definition.TargetLog);
        }
    }

    public string DialogBackground { get; }
    
    public string SectionBackground { get; }

    public string BorderColor { get; }

    public string ParameterHoverColor { get; }

    public string PrimaryTextBrush { get; }

    public string SecondaryTextBrush { get; }

    public string SectionContentBackground { get; }
    
    public string EditorBackground { get; }
    
    public string EditorForeground { get; }
    
    public string ButtonBackground { get; }
    
    public string ButtonBorderBrush { get; }
    
    public string ButtonForeground { get; }
    
    public string ValidationErrorBrush => "#B42318";

    private static bool IsMonitorBrowserOwner(FolderItemModel ownerItem)
        => ownerItem.Kind == ControlKind.Monitor
            && string.Equals(ownerItem.Name, "MonitorBrowser", StringComparison.OrdinalIgnoreCase);

    private static string GetThemeValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, "Transparent", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : value;

    public IReadOnlyList<string> AvailableModeOptions => ModeOptions;

    public IReadOnlyList<string> TargetLogOptions { get; }

    public IReadOnlyList<string> AvailableLogLevelOptions => LogLevelOptions;

    public BooleanConditionEditorViewModel ConditionEditor { get; }

    public ObservableCollection<ConditionVariableEntryViewModel> Variables => ConditionEditor.Variables;

    public ObservableCollection<MonitorActionEntryViewModel> Actions { get; } = [];

    public string FormulaPanelBackground => SectionContentBackground;

    public string AddVariableIconPath => SvgIconCache.ResolvePath("avares://HornetStudio.Editor/EditorIcons/circle-plus-solid-full.svg", ButtonForeground)
        ?? "avares://HornetStudio.Editor/EditorIcons/circle-plus-solid-full.svg";

    public string TokenButtonForeground => ButtonForeground;

    public string TokenButtonBackground => ButtonBackground;

    public string TokenButtonBorderBrush => ButtonBorderBrush;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                RaisePropertyChanged(nameof(PreviewPath));
            }
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value ?? MonitorRuleMode.Default.ToString()))
            {
                RaisePropertyChanged(nameof(IsDefaultMode));
                RaisePropertyChanged(nameof(IsCustomMode));
                RaisePropertyChanged(nameof(ShowSourcePicker));
                RaisePropertyChanged(nameof(ShowFormulaStatus));
                UpdateFormulaValidation();
            }
        }
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value ?? string.Empty);
    }

    public string RefreshRateMsText
    {
        get => _refreshRateMsText;
        set => SetProperty(ref _refreshRateMsText, value ?? string.Empty);
    }

    public string TimeoutMsText
    {
        get => _timeoutMsText;
        set => SetProperty(ref _timeoutMsText, value ?? string.Empty);
    }

    public string LowerLimit
    {
        get => _lowerLimit;
        set => SetProperty(ref _lowerLimit, value ?? string.Empty);
    }

    public string UpperLimit
    {
        get => _upperLimit;
        set => SetProperty(ref _upperLimit, value ?? string.Empty);
    }

    public string InhibitMsText
    {
        get => _inhibitMsText;
        set => SetProperty(ref _inhibitMsText, value ?? string.Empty);
    }

    public string FormulaText
    {
        get => ConditionEditor.FormulaText;
        set => ConditionEditor.FormulaText = value ?? string.Empty;
    }

    public string EventIdText
    {
        get => _eventIdText;
        set => SetProperty(ref _eventIdText, value ?? string.Empty);
    }

    public string EventText
    {
        get => _eventText;
        set => SetProperty(ref _eventText, value ?? string.Empty);
    }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set => SetProperty(ref _selectedLogLevel, value ?? MonitorLogLevel.Warning.ToString());
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasActions => Actions.Count > 0;

    public bool HasNoActions => !HasActions;

    public bool IsDefaultMode => string.Equals(SelectedMode, MonitorRuleMode.Default.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsCustomMode => !IsDefaultMode;
    
    public bool ShowSourcePicker => IsDefaultMode;

    public string FormulaStatusMessage
    {
        get => ConditionEditor.FormulaStatusMessage;
        private set { }
    }

    public string FormulaStatusBrush
    {
        get => ConditionEditor.FormulaStatusBrush;
        private set { }
    }

    public bool ShowFormulaStatus => IsCustomMode && !string.IsNullOrWhiteSpace(FormulaStatusMessage);

    public string PreviewPath => MonitorRegistry.BuildRulePath(_ownerItem.FolderName, Name);

    public string AggregatePreviewPath => MonitorRegistry.BuildAggregatePath(_ownerItem.FolderName);

    public void AddVariable()
    {
        ConditionEditor.AddVariable();
    }

    public void AddVariable(string? name, string? sourcePath)
    {
        ConditionEditor.AddVariable(name, sourcePath);
    }

    public void RemoveVariable(ConditionVariableEntryViewModel variable)
    {
        ConditionEditor.RemoveVariable(variable);
    }

    public void AddAction()
    {
        AddAction(MonitorActionTrigger.OnActivated.ToString(), MonitorActionType.WriteLog.ToString(), string.Empty, string.Empty, string.Empty, string.Empty);
    }

    public void AddAction(string? targetLog)
    {
        AddAction(MonitorActionTrigger.OnActivated.ToString(), MonitorActionType.WriteLog.ToString(), targetLog, string.Empty, string.Empty, string.Empty);
    }

    public void AddAction(string? trigger, string? actionType, string? targetLog)
    {
        AddAction(trigger, actionType, targetLog, string.Empty, string.Empty, string.Empty);
    }

    public void AddAction(string? trigger, string? actionType, string? targetLog, string? targetPath, string? functionName, string? argument)
    {
        var entry = new MonitorActionEntryViewModel(trigger, actionType, targetLog, targetPath, functionName, argument);
        entry.PropertyChanged += OnActionPropertyChanged;
        RefreshActionFunctionOptions(entry);
        Actions.Add(entry);
    }

    public void RemoveAction(MonitorActionEntryViewModel action)
    {
        if (!Actions.Remove(action))
        {
            return;
        }

        action.PropertyChanged -= OnActionPropertyChanged;
    }

    public int InsertFormulaToken(string token, int caretIndex, int caretBacktrack = 0)
        => ConditionEditor.InsertFormulaToken(token, caretIndex, caretBacktrack);

    public bool TryBuildDefinition(out MonitorDefinition definition, out string errorMessage)
    {
        definition = new MonitorDefinition();
        errorMessage = string.Empty;

        var normalizedName = TargetPathHelper.NormalizeIdentityName(Name);
        if (!TargetPathHelper.IsValidPathIdentityName(normalizedName))
        {
            errorMessage = "Name must use snake_case and start with a lowercase letter.";
            return false;
        }

        var existingNames = EnumerateRegistryDefinitions()
            .Select(candidate => candidate.Name)
            .Where(candidate => !string.Equals(candidate, _originalName, StringComparison.OrdinalIgnoreCase));
        if (existingNames.Contains(normalizedName, StringComparer.OrdinalIgnoreCase))
        {
            errorMessage = "Name must be unique within the folder monitor registry.";
            return false;
        }

        if (!TryParseRequiredPositiveInt(RefreshRateMsText, 100, out var refreshRateMs, out errorMessage))
        {
            return false;
        }

        if (!TryParseOptionalPositiveInt(TimeoutMsText, out var timeoutMs, out errorMessage))
        {
            return false;
        }

        if (!TryParseOptionalPositiveInt(InhibitMsText, out var inhibitMs, out errorMessage))
        {
            return false;
        }

        if (!TryValidateOptionalNumber(LowerLimit, "Lower limit", out errorMessage)
            || !TryValidateOptionalNumber(UpperLimit, "Upper limit", out errorMessage))
        {
            return false;
        }

        if (!Enum.TryParse<MonitorRuleMode>(SelectedMode, true, out var mode))
        {
            errorMessage = "Mode is invalid.";
            return false;
        }

        var normalizedSourcePath = mode == MonitorRuleMode.Default
            ? TargetPathHelper.NormalizeConfiguredTargetPath(SourcePath)
            : string.Empty;
        if (mode == MonitorRuleMode.Default && string.IsNullOrWhiteSpace(normalizedSourcePath))
        {
            errorMessage = "Source path is required.";
            return false;
        }

        if (!Enum.TryParse<MonitorLogLevel>(SelectedLogLevel, true, out var logLevel))
        {
            errorMessage = "Log level is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(EventIdText))
        {
            errorMessage = "Event Id is required.";
            return false;
        }

        if (!int.TryParse(EventIdText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId))
        {
            errorMessage = "Event Id must be a valid integer.";
            return false;
        }

        if (eventId <= 0)
        {
            errorMessage = "Event Id must be greater than 0.";
            return false;
        }

        if (EnumerateSiblingDefinitions()
            .Any(candidate => candidate.EventId == eventId))
        {
            errorMessage = "Event Id must be unique within the folder monitor registry.";
            return false;
        }

        if (!ConditionEditor.TryBuildVariables(out var conditionVariables, out errorMessage))
        {
            return false;
        }

        var variables = conditionVariables.Select(static variable => new MonitorVariableDefinition
        {
            Name = variable.Name,
            SourcePath = variable.SourcePath
        }).ToList();

        if (!TryBuildActions(out var actions, out errorMessage))
        {
            return false;
        }

        var customFormula = FormulaText?.Trim() ?? string.Empty;
        if (mode == MonitorRuleMode.Custom)
        {
            if (string.IsNullOrWhiteSpace(customFormula))
            {
                errorMessage = "Formula is required in Custom mode.";
                return false;
            }

            if (!ConditionEditor.TryValidate(out var formulaError))
            {
                errorMessage = formulaError;
                return false;
            }
        }

        definition = new MonitorDefinition
        {
            Name = normalizedName,
            SourcePath = normalizedSourcePath,
            RefreshRateMs = refreshRateMs,
            TimeoutMs = timeoutMs,
            Mode = mode,
            LowerLimit = mode == MonitorRuleMode.Default ? LowerLimit?.Trim() ?? string.Empty : LowerLimit?.Trim() ?? string.Empty,
            UpperLimit = mode == MonitorRuleMode.Default ? UpperLimit?.Trim() ?? string.Empty : UpperLimit?.Trim() ?? string.Empty,
            InhibitMs = inhibitMs ?? 0,
            CustomFormula = customFormula,
            CustomVariables = variables,
            EventId = eventId,
            EventText = EventText?.Trim() ?? string.Empty,
            Actions = actions,
            TargetLog = string.Empty,
            LogLevel = logLevel
        };
        return true;
    }

    private string GenerateNextRuleName()
    {
        var existingNames = EnumerateRegistryDefinitions()
            .Select(definition => definition.Name);
        return TargetPathHelper.GenerateIndexedPathIdentityName("monitor_rule", existingNames, "monitor_rule");
    }

    private int GenerateNextEventId()
    {
        var usedEventIds = EnumerateRegistryDefinitions()
            .Select(static definition => definition.EventId)
            .Where(static eventId => eventId > 0)
            .ToHashSet();

        var candidate = 1;
        while (usedEventIds.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private IEnumerable<MonitorDefinition> EnumerateSiblingDefinitions()
    {
        return EnumerateRegistryDefinitions()
            .Where(candidate => !string.Equals(candidate.Name, _originalName, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<MonitorDefinition> EnumerateRegistryDefinitions()
        => _registryDefinitionsProvider();

    private static IReadOnlyList<MonitorDefinition> EnumerateRegistryDefinitions(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem)
    {
        if (TryLoadCentralDefinitions(ownerItem, out var centralDefinitions))
        {
            return centralDefinitions;
        }

        if (mainWindowViewModel is null)
        {
            return MonitorDefinitionCodec.ParseDefinitions(ownerItem.MonitorDefinitions)
                .Select(static candidate => candidate.Clone())
                .ToArray();
        }

        return mainWindowViewModel.GetMonitorRegistryEntries(ownerItem)
            .Select(static entry => entry.Definition.Clone())
            .ToArray();
    }

    private static bool TryLoadCentralDefinitions(FolderItemModel ownerItem, out IReadOnlyList<MonitorDefinition> definitions)
    {
        definitions = [];
        if (string.IsNullOrWhiteSpace(ownerItem.FolderLayoutPath)
            || string.IsNullOrWhiteSpace(ownerItem.FolderName))
        {
            return false;
        }

        var folderDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ownerItem.FolderLayoutPath));
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return false;
        }

        var monitorFilePath = MonitorDefinitionFileCodec.GetMonitorFilePath(folderDirectory);
        if (!System.IO.File.Exists(monitorFilePath))
        {
            return false;
        }

        var codec = new MonitorDefinitionFileCodec();
        definitions = codec.LoadDefinitions(folderDirectory, ownerItem.FolderName)
            .Select(static definition => definition.Clone())
            .ToArray();
        return true;
    }

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasActions));
        RaisePropertyChanged(nameof(HasNoActions));
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MonitorActionEntryViewModel action)
        {
            return;
        }

        if (e.PropertyName is nameof(MonitorActionEntryViewModel.SelectedActionType)
            or nameof(MonitorActionEntryViewModel.TargetPath))
        {
            RefreshActionFunctionOptions(action);
        }
    }

    private void RefreshActionFunctionOptions(MonitorActionEntryViewModel action)
    {
        if (!action.IsInvokeFunctionAction)
        {
            action.SetFunctionOptions(Array.Empty<string>());
            return;
        }

        var resolvedTargetPath = ApplicationExplorerRuntime.ResolveInteractionTargetPath(_ownerItem, action.TargetPath);
        action.SetFunctionOptions(PythonClientRuntimeRegistry.GetFunctionNames(resolvedTargetPath));
    }

    private bool TryBuildActions(out List<MonitorActionDefinition> actions, out string errorMessage)
    {
        actions = [];
        errorMessage = string.Empty;

        for (var index = 0; index < Actions.Count; index++)
        {
            var action = Actions[index];
            if (!Enum.TryParse<MonitorActionTrigger>(action.SelectedTrigger, true, out var trigger))
            {
                errorMessage = $"Action {index + 1} has an invalid trigger.";
                return false;
            }

            if (!Enum.TryParse<MonitorActionType>(action.SelectedActionType, true, out var actionType))
            {
                errorMessage = $"Action {index + 1} has an invalid action type.";
                return false;
            }

            var targetLog = TargetPathHelper.NormalizeConfiguredTargetPath(action.TargetLog);
            var targetPath = actionType == MonitorActionType.InvokeFunction
                ? action.TargetPath?.Trim() ?? string.Empty
                : TargetPathHelper.NormalizeConfiguredTargetPath(action.TargetPath);
            var functionName = action.FunctionName?.Trim() ?? string.Empty;
            var argument = action.Argument?.Trim() ?? string.Empty;
            if (actionType == MonitorActionType.WriteLog && string.IsNullOrWhiteSpace(targetLog))
            {
                errorMessage = $"Action {index + 1} requires a target log for WriteLog.";
                return false;
            }

            if (actionType == MonitorActionType.SetValue && string.IsNullOrWhiteSpace(targetPath))
            {
                errorMessage = $"Action {index + 1} requires a target path for SetValue.";
                return false;
            }

            if (actionType == MonitorActionType.InvokeFunction)
            {
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    errorMessage = $"Action {index + 1} requires a target path for InvokeFunction.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(functionName))
                {
                    errorMessage = $"Action {index + 1} requires a function name for InvokeFunction.";
                    return false;
                }
            }

            actions.Add(new MonitorActionDefinition
            {
                Trigger = trigger,
                ActionType = actionType,
                TargetLog = targetLog,
                TargetPath = targetPath,
                FunctionName = functionName,
                Argument = argument
            });
        }

        return true;
    }

    private void UpdateFormulaValidation()
    {
        RaisePropertyChanged(nameof(FormulaText));
        RaisePropertyChanged(nameof(Variables));
        RaisePropertyChanged(nameof(FormulaStatusMessage));
        RaisePropertyChanged(nameof(FormulaStatusBrush));
        RaisePropertyChanged(nameof(ShowFormulaStatus));
    }

    private void OnConditionEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BooleanConditionEditorViewModel.FormulaText)
            or nameof(BooleanConditionEditorViewModel.FormulaStatusMessage)
            or nameof(BooleanConditionEditorViewModel.FormulaStatusBrush)
            or nameof(BooleanConditionEditorViewModel.VariableButtons))
        {
            UpdateFormulaValidation();
        }
    }

    private static bool TryParseRequiredPositiveInt(string raw, int minValue, out int value, out string errorMessage)
    {
        errorMessage = string.Empty;
        value = 0;
        if (!int.TryParse(string.IsNullOrWhiteSpace(raw) ? "0" : raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < minValue)
        {
            errorMessage = $"Value must be a whole number greater than or equal to {minValue}.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseOptionalPositiveInt(string raw, out int? value, out string errorMessage)
    {
        errorMessage = string.Empty;
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            errorMessage = "Numeric values must be empty or a non-negative integer.";
            return false;
        }

        value = parsed > 0 ? parsed : null;
        return true;
    }

    private static bool TryValidateOptionalNumber(string raw, string label, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }
        errorMessage = $"{label} must be empty or a valid number.";
        return false;
    }
}

public sealed class MonitorActionEntryViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> TriggerOptions = Enum.GetNames<MonitorActionTrigger>();
    private static readonly IReadOnlyList<string> ActionTypeOptions = Enum.GetNames<MonitorActionType>();
    private string _selectedTrigger = MonitorActionTrigger.OnActivated.ToString();
    private string _selectedActionType = MonitorActionType.WriteLog.ToString();
    private string _targetLog = string.Empty;
    private string _targetPath = string.Empty;
    private string _functionName = string.Empty;
    private string _argument = string.Empty;

    public MonitorActionEntryViewModel(string? trigger, string? actionType, string? targetLog, string? targetPath, string? functionName, string? argument)
    {
        SelectedTrigger = trigger ?? MonitorActionTrigger.OnActivated.ToString();
        SelectedActionType = actionType ?? MonitorActionType.WriteLog.ToString();
        TargetLog = targetLog ?? string.Empty;
        TargetPath = targetPath ?? string.Empty;
        FunctionName = functionName ?? string.Empty;
        Argument = argument ?? string.Empty;
    }

    public IReadOnlyList<string> AvailableTriggerOptions => TriggerOptions;

    public IReadOnlyList<string> AvailableActionTypeOptions => ActionTypeOptions;

    public string SelectedTrigger
    {
        get => _selectedTrigger;
        set => SetProperty(ref _selectedTrigger, value ?? MonitorActionTrigger.OnActivated.ToString());
    }

    public string SelectedActionType
    {
        get => _selectedActionType;
        set
        {
            if (SetProperty(ref _selectedActionType, value ?? MonitorActionType.WriteLog.ToString()))
            {
                RaisePropertyChanged(nameof(IsWriteLogAction));
                RaisePropertyChanged(nameof(IsSetValueAction));
                RaisePropertyChanged(nameof(IsInvokeFunctionAction));
                RaisePropertyChanged(nameof(ShowArgument));
                RaisePropertyChanged(nameof(ShowFunction));
                RaisePropertyChanged(nameof(DisplayTarget));
            }
        }
    }

    public string TargetLog
    {
        get => _targetLog;
        set
        {
            if (SetProperty(ref _targetLog, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTarget));
            }
        }
    }

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetProperty(ref _targetPath, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTarget));
            }
        }
    }

    public string FunctionName
    {
        get => _functionName;
        set => SetProperty(ref _functionName, value ?? string.Empty);
    }

    public string Argument
    {
        get => _argument;
        set => SetProperty(ref _argument, value ?? string.Empty);
    }

    public string DisplayTarget
    {
        get => IsWriteLogAction ? TargetLog : TargetPath;
        set
        {
            if (IsWriteLogAction)
            {
                TargetLog = value;
            }
            else
            {
                TargetPath = value;
            }
        }
    }

    public bool IsWriteLogAction => string.Equals(SelectedActionType, nameof(MonitorActionType.WriteLog), StringComparison.OrdinalIgnoreCase);

    public bool IsSetValueAction => string.Equals(SelectedActionType, nameof(MonitorActionType.SetValue), StringComparison.OrdinalIgnoreCase);

    public bool IsInvokeFunctionAction => string.Equals(SelectedActionType, nameof(MonitorActionType.InvokeFunction), StringComparison.OrdinalIgnoreCase);

    public bool ShowArgument => IsSetValueAction || IsInvokeFunctionAction;

    public bool ShowFunction => IsInvokeFunctionAction;

    public ObservableCollection<string> FunctionOptions { get; } = [];

    public void SetFunctionOptions(IEnumerable<string> options)
    {
        FunctionOptions.Clear();
        foreach (var option in options.Where(static option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            FunctionOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(FunctionName)
            && !FunctionOptions.Contains(FunctionName, StringComparer.OrdinalIgnoreCase))
        {
            FunctionOptions.Add(FunctionName);
        }
    }
}
