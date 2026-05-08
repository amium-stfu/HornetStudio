using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class CustomSignalEditorDialogWindow : Window
{
    private TextBox? _formulaEditorTextBox;
    private readonly MainWindowViewModel? _viewModel;
    private FolderItemModel? _ownerItem;
    private IReadOnlyList<string> _sourceOptions = Array.Empty<string>();
    private CustomSignalDefinition? _result;

    public CustomSignalEditorDialogWindow()
    {
        ViewModel = new CustomSignalEditorDialogViewModel(null, new FolderItemModel(), null);
        DataContext = ViewModel;
        InitializeComponent();
        _formulaEditorTextBox = this.FindControl<TextBox>("FormulaEditorTextBox");
    }

    public CustomSignalEditorDialogWindow(MainWindowViewModel? viewModel, FolderItemModel ownerItem, CustomSignalDefinition? definition, IEnumerable<string> sourceOptions)
        : this()
    {
        _viewModel = viewModel;
        _ownerItem = ownerItem;
        _sourceOptions = sourceOptions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        ViewModel = new CustomSignalEditorDialogViewModel(viewModel, ownerItem, definition);
        DataContext = ViewModel;
    }

    public CustomSignalEditorDialogViewModel ViewModel { get; }

    public static async Task<CustomSignalDefinition?> ShowAsync(Window owner, MainWindowViewModel? viewModel, FolderItemModel ownerItem, CustomSignalDefinition? definition, IEnumerable<string> sourceOptions)
    {
        var dialog = new CustomSignalEditorDialogWindow(viewModel, ownerItem, definition, sourceOptions);
        return await dialog.ShowDialog<CustomSignalDefinition?>(owner);
    }

    private async void OnPickVariableSourceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomSignalVariableEntryViewModel variable })
        {
            return;
        }

        variable.SourcePath = await PickTargetAsync(variable.SourcePath) ?? variable.SourcePath;
        e.Handled = true;
    }

    private async void OnPickWriteTargetClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.WritePath = await PickTargetAsync(ViewModel.WritePath) ?? ViewModel.WritePath;
        e.Handled = true;
    }

    private void OnAddVariableClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddVariable();
        e.Handled = true;
    }

    private void OnDeleteVariableClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomSignalVariableEntryViewModel variable })
        {
            return;
        }

        ViewModel.RemoveVariable(variable);
        e.Handled = true;
    }

    private void OnInsertFormulaTokenClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: FormulaInsertButtonDefinition token })
        {
            return;
        }

        var caretIndex = _formulaEditorTextBox?.CaretIndex ?? ViewModel.FormulaText.Length;
        var nextCaretIndex = ViewModel.InsertFormulaToken(token.Token, caretIndex, token.CaretBacktrack);

        if (_formulaEditorTextBox is not null)
        {
            _formulaEditorTextBox.Focus();
            _formulaEditorTextBox.CaretIndex = Math.Clamp(nextCaretIndex, 0, _formulaEditorTextBox.Text?.Length ?? 0);
        }

        e.Handled = true;
    }

    private async Task<string?> PickTargetAsync(string currentSelection)
    {
        var dialog = new TargetTreeSelectionDialogWindow(_viewModel, _sourceOptions, currentSelection, _ownerItem?.FolderName ?? string.Empty);
        await dialog.ShowDialog(this);
        return string.IsNullOrWhiteSpace(dialog.CommittedSelection) ? currentSelection : dialog.CommittedSelection;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildDefinition(out var definition, out var errorMessage))
        {
            ViewModel.ErrorMessage = errorMessage;
            return;
        }

        _result = definition;
        Close(_result);
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close((CustomSignalDefinition?)null);
        e.Handled = true;
    }
}

public sealed class CustomSignalEditorDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> ComputedDataTypeOptions = [nameof(CustomSignalDataType.Number), nameof(CustomSignalDataType.Boolean)];

    private readonly FolderItemModel _ownerItem;
    private readonly string _originalName;
    private readonly IReadOnlyList<string> _allDataTypeOptions = Enum.GetNames<CustomSignalDataType>();
    private static readonly IReadOnlyList<string> ReadOnlyOptions = ["True", "False"];
    private string _name = string.Empty;
    private string _selectedMode = CustomSignalMode.Input.ToString();
    private string _selectedDataType = CustomSignalDataType.Number.ToString();
    private bool _isWritable = true;
    private string _writePath = string.Empty;
    private string _selectedWriteMode = SignalWriteMode.Direct.ToString();
    private string _unit = string.Empty;
    private string _format = string.Empty;
    private string _valueText = string.Empty;
    private string _formulaText = string.Empty;
    private string _selectedTrigger = CustomSignalComputationTrigger.OnSourceChange.ToString();
    private string _triggerIntervalText = "1";
    private string _errorMessage = string.Empty;
    private string _formulaStatusMessage = string.Empty;
    private string _formulaStatusBrush = "#5E6777";
    private IReadOnlyList<string> _availableDataTypeOptions;
    private bool _isInitializing;

    public CustomSignalEditorDialogViewModel(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, CustomSignalDefinition? definition)
    {
        _isInitializing = true;
        _ownerItem = ownerItem;
        _originalName = definition?.Name ?? string.Empty;
        _availableDataTypeOptions = _allDataTypeOptions;
        ModeOptions = Enum.GetNames<CustomSignalMode>();
        TriggerOptions = Enum.GetNames<CustomSignalComputationTrigger>();
        WriteModeOptions = Enum.GetNames<SignalWriteMode>();
        OperatorButtons = CreateOperatorButtons();
        Variables.CollectionChanged += OnVariablesCollectionChanged;

        DialogBackground = mainWindowViewModel?.DialogBackground ?? "#E3E5EE";
        SectionBackground = mainWindowViewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
        BorderColor = mainWindowViewModel?.CardBorderBrush ?? "#D5D9E0";
        PrimaryTextBrush = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = mainWindowViewModel?.SecondaryTextBrush ?? "#5E6777";
        EditorBackground = mainWindowViewModel?.ParameterEditBackgrundColor ?? "#FFFFFF";
        EditorForeground = mainWindowViewModel?.ParameterEditForeColor ?? "#111827";
        ButtonBackground = mainWindowViewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = mainWindowViewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";

        if (definition is not null)
        {
            Name = definition.Name;
            SelectedMode = definition.Mode.ToString();
            SelectedDataType = definition.DataType.ToString();
            IsWritable = definition.IsWritable;
            WritePath = definition.WritePath;
            SelectedWriteMode = definition.WriteMode.ToString();
            Unit = definition.Unit;
            Format = definition.Format;
            ValueText = definition.ValueText;
            FormulaText = definition.Formula;
            SelectedTrigger = definition.Trigger.ToString();
            TriggerIntervalText = Math.Max(1, definition.TriggerIntervalSeconds).ToString();

            foreach (var variable in definition.Variables.Where(static variable => variable is not null))
            {
                AddVariable(variable.Name, variable.SourcePath);
            }
        }
        else
        {
            Name = GenerateNextSignalName();
        }

        if (IsComputed && Variables.Count == 0)
        {
            AddVariable();
        }

        UpdateAvailableDataTypeOptions();
        UpdateFormulaValidation();
        _isInitializing = false;
    }

    public IReadOnlyList<string> ModeOptions { get; }

    public IReadOnlyList<string> TriggerOptions { get; }

    public IReadOnlyList<string> WriteModeOptions { get; }

    public IReadOnlyList<string> ReadOnlyValueOptions => ReadOnlyOptions;

    public ObservableCollection<CustomSignalVariableEntryViewModel> Variables { get; } = [];

    public IReadOnlyList<string> AvailableDataTypeOptions => _availableDataTypeOptions;

    public IReadOnlyList<FormulaInsertButtonDefinition> OperatorButtons { get; }

    public string ExampleText => string.Equals(SelectedDataType, nameof(CustomSignalDataType.Boolean), StringComparison.OrdinalIgnoreCase)
        ? "Examples: ({A} > {B}) && {Ready}   or   !{Fault}"
        : "Examples: ({A} + {B}) / {C}   or   sqrt({A}) + max({B}, {C})";

    public IReadOnlyList<FormulaInsertButtonDefinition> VariableButtons => Variables
        .Where(static variable => !string.IsNullOrWhiteSpace(variable.Name))
        .Select(variable => new FormulaInsertButtonDefinition(variable.Name.Trim(), $"{{{variable.Name.Trim()}}}"))
        .ToArray();

    public string DialogBackground { get; }

    public string SectionBackground { get; }

    public string BorderColor { get; }

    public string PrimaryTextBrush { get; }

    public string SecondaryTextBrush { get; }

    public string EditorBackground { get; }

    public string EditorForeground { get; }

    public string ButtonBackground { get; }

    public string ButtonBorderBrush { get; }

    public string ButtonForeground { get; }

    public string AddVariableIconPath => SvgIconCache.ResolvePath("avares://HornetStudio.Editor/EditorIcons/circle-plus-solid-full.svg", ButtonForeground)
        ?? "avares://HornetStudio.Editor/EditorIcons/circle-plus-solid-full.svg";

    public string TokenButtonForeground => ButtonForeground;

    public string TokenButtonBackground => ButtonBackground;

    public string TokenButtonBorderBrush => ButtonBorderBrush;

    public string FormulaPanelBackground => EditorBackground;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
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
            if (SetProperty(ref _selectedMode, value ?? CustomSignalMode.Input.ToString()))
            {
                if (!_isInitializing && IsComputed && Variables.Count == 0)
                {
                    AddVariable();
                }

                UpdateAvailableDataTypeOptions();
                RaiseComputedVisibilityChanged();
                RaisePropertyChanged(nameof(PreviewPath));
                UpdateFormulaValidation();
            }
        }
    }

    public string SelectedDataType
    {
        get => _selectedDataType;
        set
        {
            if (SetProperty(ref _selectedDataType, value ?? CustomSignalDataType.Number.ToString()))
            {
                RaisePropertyChanged(nameof(ExampleText));
                UpdateFormulaValidation();
            }
        }
    }

    public bool IsWritable
    {
        get => _isWritable;
        set => SetProperty(ref _isWritable, value);
    }

    public string WritePath
    {
        get => _writePath;
        set => SetProperty(ref _writePath, value ?? string.Empty);
    }

    public string SelectedWriteMode
    {
        get => _selectedWriteMode;
        set => SetProperty(ref _selectedWriteMode, value ?? SignalWriteMode.Direct.ToString());
    }

    public string SelectedReadOnlyValue
    {
        get => IsWritable ? "False" : "True";
        set
        {
            var nextIsWritable = !string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
            if (IsWritable != nextIsWritable)
            {
                IsWritable = nextIsWritable;
            }

            RaisePropertyChanged(nameof(SelectedReadOnlyValue));
        }
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value ?? string.Empty);
    }

    public string Format
    {
        get => _format;
        set => SetProperty(ref _format, value ?? string.Empty);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value ?? string.Empty);
    }

    public string FormulaText
    {
        get => _formulaText;
        set
        {
            if (SetProperty(ref _formulaText, value ?? string.Empty))
            {
                UpdateFormulaValidation();
            }
        }
    }

    public string SelectedTrigger
    {
        get => _selectedTrigger;
        set
        {
            if (SetProperty(ref _selectedTrigger, value ?? CustomSignalComputationTrigger.OnSourceChange.ToString()))
            {
                RaisePropertyChanged(nameof(IsTimerTrigger));
                UpdateFormulaValidation();
            }
        }
    }

    public string TriggerIntervalText
    {
        get => _triggerIntervalText;
        set
        {
            if (SetProperty(ref _triggerIntervalText, value ?? "1"))
            {
                UpdateFormulaValidation();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value ?? string.Empty);
    }

    public string FormulaStatusMessage
    {
        get => _formulaStatusMessage;
        private set => SetProperty(ref _formulaStatusMessage, value ?? string.Empty);
    }

    public string FormulaStatusBrush
    {
        get => _formulaStatusBrush;
        private set => SetProperty(ref _formulaStatusBrush, value ?? SecondaryTextBrush);
    }

    public string ValidationErrorBrush => "#B42318";

    public bool IsComputed => string.Equals(SelectedMode, CustomSignalMode.Computed.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsInput => !IsComputed;

    public bool IsValueVisible => IsInput;

    public bool IsWritableVisible => IsInput;

    public bool IsWriteConfigurationVisible => IsInput;

    public bool IsTriggerVisible => IsComputed;

    public bool IsTimerTrigger => IsComputed && string.Equals(SelectedTrigger, CustomSignalComputationTrigger.Timer.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool ShowComputedEditor => IsComputed;

    public bool ShowFormulaStatus => IsComputed && !string.IsNullOrWhiteSpace(FormulaStatusMessage);

    public string PreviewPath
    {
        get
        {
            var definition = new CustomSignalDefinition { Name = Name };
            return CustomSignalsControl.BuildRegistryPath(_ownerItem, definition);
        }
    }

    public void AddVariable()
    {
        AddVariable(GenerateNextVariableName(), string.Empty);
    }

    public void AddVariable(string? name, string? sourcePath)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName)
            && Variables.Any(variable => string.Equals(variable.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var variable = new CustomSignalVariableEntryViewModel(name ?? string.Empty, sourcePath ?? string.Empty);
        variable.PropertyChanged += OnVariablePropertyChanged;
        Variables.Add(variable);
    }

    public void RemoveVariable(CustomSignalVariableEntryViewModel variable)
    {
        if (!Variables.Remove(variable))
        {
            return;
        }

        variable.PropertyChanged -= OnVariablePropertyChanged;
        RaisePropertyChanged(nameof(VariableButtons));
        UpdateFormulaValidation();
    }

    public int InsertFormulaToken(string token, int caretIndex, int caretBacktrack = 0)
    {
        var formula = FormulaText ?? string.Empty;
        var insertIndex = Math.Clamp(caretIndex, 0, formula.Length);
        FormulaText = formula.Insert(insertIndex, token);
        return Math.Clamp(insertIndex + token.Length + caretBacktrack, 0, FormulaText.Length);
    }

    public bool TryBuildDefinition(out CustomSignalDefinition? definition, out string errorMessage)
    {
        definition = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            errorMessage = "Name is required.";
            return false;
        }

        var normalizedName = TargetPathHelper.NormalizeIdentityName(Name);
        var isUnchangedExistingName = !string.IsNullOrWhiteSpace(_originalName)
            && string.Equals(_originalName, normalizedName, StringComparison.Ordinal);
        if (!isUnchangedExistingName && !TargetPathHelper.IsValidPathIdentityName(normalizedName))
        {
            errorMessage = "Name must use snake_case: lowercase letters, numbers and '_' only.";
            return false;
        }

        if (!Enum.TryParse<CustomSignalMode>(SelectedMode, true, out var mode))
        {
            errorMessage = "Mode is invalid.";
            return false;
        }

        if (!Enum.TryParse<CustomSignalDataType>(SelectedDataType, true, out var dataType))
        {
            errorMessage = "Type is invalid.";
            return false;
        }

        if (mode == CustomSignalMode.Computed)
        {
            if (dataType == CustomSignalDataType.Text)
            {
                errorMessage = "Computed signals support only Number or Boolean type.";
                return false;
            }

            if (!Enum.TryParse<CustomSignalComputationTrigger>(SelectedTrigger, true, out var trigger))
            {
                errorMessage = "Trigger is invalid.";
                return false;
            }

            if (!TryBuildVariables(out var variables, out errorMessage))
            {
                return false;
            }

            if (!int.TryParse(TriggerIntervalText, out var triggerIntervalSeconds) || triggerIntervalSeconds < 1)
            {
                errorMessage = "Timer interval must be a positive whole number of seconds.";
                return false;
            }

            definition = new CustomSignalDefinition
            {
                Name = normalizedName,
                Mode = mode,
                DataType = dataType,
                IsWritable = false,
                WritePath = string.Empty,
                WriteMode = SignalWriteMode.Direct,
                Unit = Unit.Trim(),
                Format = Format.Trim(),
                Formula = FormulaText.Trim(),
                Trigger = trigger,
                TriggerIntervalSeconds = Math.Max(1, triggerIntervalSeconds),
                Variables = variables,
                Operation = CustomSignalOperation.Copy,
                SourcePath = variables.ElementAtOrDefault(0)?.SourcePath ?? string.Empty,
                SourcePath2 = variables.ElementAtOrDefault(1)?.SourcePath ?? string.Empty,
                SourcePath3 = variables.ElementAtOrDefault(2)?.SourcePath ?? string.Empty
            };

            if (!CustomSignalFormulaEngine.TryValidate(definition, out errorMessage))
            {
                return false;
            }

            return true;
        }

        if (!Enum.TryParse<SignalWriteMode>(SelectedWriteMode, true, out var writeMode))
        {
            errorMessage = "Write mode is invalid.";
            return false;
        }

        definition = new CustomSignalDefinition
        {
            Name = normalizedName,
            Mode = mode,
            DataType = dataType,
            IsWritable = IsWritable,
            WritePath = WritePath.Trim(),
            WriteMode = writeMode,
            Unit = Unit.Trim(),
            Format = Format.Trim(),
            ValueText = ValueText,
            Trigger = CustomSignalComputationTrigger.OnSourceChange,
            TriggerIntervalSeconds = 1,
            Variables = []
        };

        return true;
    }

    private void UpdateAvailableDataTypeOptions()
    {
        _availableDataTypeOptions = IsComputed ? ComputedDataTypeOptions : _allDataTypeOptions;
        if (!_availableDataTypeOptions.Contains(SelectedDataType, StringComparer.OrdinalIgnoreCase))
        {
            SelectedDataType = _availableDataTypeOptions[0];
        }

        RaisePropertyChanged(nameof(AvailableDataTypeOptions));
    }

    private void RaiseComputedVisibilityChanged()
    {
        RaisePropertyChanged(nameof(IsComputed));
        RaisePropertyChanged(nameof(IsInput));
        RaisePropertyChanged(nameof(IsValueVisible));
        RaisePropertyChanged(nameof(IsWritableVisible));
        RaisePropertyChanged(nameof(IsWriteConfigurationVisible));
        RaisePropertyChanged(nameof(IsTriggerVisible));
        RaisePropertyChanged(nameof(IsTimerTrigger));
        RaisePropertyChanged(nameof(ShowComputedEditor));
        RaisePropertyChanged(nameof(ShowFormulaStatus));
        RaisePropertyChanged(nameof(VariableButtons));
        RaisePropertyChanged(nameof(ExampleText));
    }

    private void OnVariablesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(VariableButtons));
        UpdateFormulaValidation();
    }

    private void OnVariablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(VariableButtons));
        UpdateFormulaValidation();
    }

    private bool TryBuildVariables(out List<CustomSignalVariableDefinition> variables, out string errorMessage)
    {
        variables = [];
        errorMessage = string.Empty;

        if (Variables.Count == 0)
        {
            errorMessage = "At least one variable is required for computed signals.";
            return false;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in Variables)
        {
            var name = (variable.Name ?? string.Empty).Trim();
            if (!IsValidVariableName(name))
            {
                errorMessage = $"Variable name '{name}' is invalid.";
                return false;
            }

            if (!usedNames.Add(name))
            {
                errorMessage = $"Variable name '{name}' is used more than once.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(variable.SourcePath))
            {
                errorMessage = $"Variable '{name}' requires a source path.";
                return false;
            }

            variables.Add(new CustomSignalVariableDefinition
            {
                Name = name,
                SourcePath = variable.SourcePath.Trim()
            });
        }

        return true;
    }

    private void UpdateFormulaValidation()
    {
        if (!IsComputed)
        {
            FormulaStatusMessage = string.Empty;
            FormulaStatusBrush = SecondaryTextBrush;
            RaisePropertyChanged(nameof(ShowFormulaStatus));
            return;
        }

        if (!Enum.TryParse<CustomSignalDataType>(SelectedDataType, true, out var dataType))
        {
            FormulaStatusMessage = "Select a valid type.";
            FormulaStatusBrush = "#B42318";
            RaisePropertyChanged(nameof(ShowFormulaStatus));
            return;
        }

        if (!TryBuildVariables(out var variables, out var variableError))
        {
            FormulaStatusMessage = variableError;
            FormulaStatusBrush = "#B42318";
            RaisePropertyChanged(nameof(ShowFormulaStatus));
            return;
        }

        if (!Enum.TryParse<CustomSignalComputationTrigger>(SelectedTrigger, true, out var trigger))
        {
            trigger = CustomSignalComputationTrigger.OnSourceChange;
        }

        var previewDefinition = new CustomSignalDefinition
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Preview" : Name.Trim(),
            Mode = CustomSignalMode.Computed,
            DataType = dataType,
            Formula = FormulaText.Trim(),
            Trigger = trigger,
            TriggerIntervalSeconds = int.TryParse(TriggerIntervalText, out var seconds) ? Math.Max(1, seconds) : 1,
            Variables = variables
        };

        if (CustomSignalFormulaEngine.TryValidate(previewDefinition, out var errorMessage))
        {
            FormulaStatusMessage = "Formula looks valid.";
            FormulaStatusBrush = "#027A48";
        }
        else
        {
            FormulaStatusMessage = errorMessage;
            FormulaStatusBrush = "#B42318";
        }

        RaisePropertyChanged(nameof(ShowFormulaStatus));
    }

    private string GenerateNextSignalName()
    {
        var existingNames = CustomSignalDefinitionCodec.ParseDefinitions(_ownerItem.CustomSignalDefinitions)
            .Select(static definition => definition.Name);
        return TargetPathHelper.GenerateIndexedPathIdentityName("signal", existingNames, "signal");
    }

    private string GenerateNextVariableName()
    {
        for (var offset = 0; offset < 26; offset++)
        {
            var candidate = ((char)('A' + offset)).ToString();
            if (Variables.All(variable => !string.Equals(variable.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        var suffix = 1;
        while (true)
        {
            for (var offset = 0; offset < 26; offset++)
            {
                var candidate = $"{(char)('A' + offset)}{suffix}";
                if (Variables.All(variable => !string.Equals(variable.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }

            suffix++;
        }
    }

    private static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]))
        {
            return false;
        }

        return name.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static IReadOnlyList<FormulaInsertButtonDefinition> CreateOperatorButtons()
    {
        return
        [
            new FormulaInsertButtonDefinition("+", " + ", tooltip: "Plus"),
            new FormulaInsertButtonDefinition("−", " - ", tooltip: "Minus"),
            new FormulaInsertButtonDefinition("×", " * ", tooltip: "Mal"),
            new FormulaInsertButtonDefinition("÷", " / ", tooltip: "Geteilt"),
            new FormulaInsertButtonDefinition("xʸ", " ^ ", tooltip: "Potenz"),
            new FormulaInsertButtonDefinition("√x", "sqrt()", -1, "Wurzel"),
            new FormulaInsertButtonDefinition("min", "min(, )", -3, "Minimum"),
            new FormulaInsertButtonDefinition("max", "max(, )", -3, "Maximum"),
            new FormulaInsertButtonDefinition("|x|", "abs()", -1, "Absolutwert"),
            new FormulaInsertButtonDefinition("if", "if(, , )", -5, "Bedingung"),
            new FormulaInsertButtonDefinition("∧", " && ", tooltip: "Und"),
            new FormulaInsertButtonDefinition("∨", " || ", tooltip: "Oder"),
            new FormulaInsertButtonDefinition("¬", "!", tooltip: "Nicht"),
            new FormulaInsertButtonDefinition("=", " == ", tooltip: "Gleich"),
            new FormulaInsertButtonDefinition("≠", " != ", tooltip: "Ungleich"),
            new FormulaInsertButtonDefinition(">", " > ", tooltip: "Größer"),
            new FormulaInsertButtonDefinition("<", " < ", tooltip: "Kleiner"),
            new FormulaInsertButtonDefinition("(", "(", tooltip: "Klammer auf"),
            new FormulaInsertButtonDefinition(")", ")", tooltip: "Klammer zu")
        ];
    }
}

public sealed class CustomSignalVariableEntryViewModel : ObservableObject
{
    private string _name;
    private string _sourcePath;

    public CustomSignalVariableEntryViewModel(string name, string sourcePath)
    {
        _name = name ?? string.Empty;
        _sourcePath = sourcePath ?? string.Empty;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value ?? string.Empty);
    }
}

public sealed class FormulaInsertButtonDefinition
{
    public FormulaInsertButtonDefinition(string label, string token, int caretBacktrack = 0, string? tooltip = null)
    {
        Label = label;
        Token = token;
        CaretBacktrack = caretBacktrack;
        ToolTip = tooltip ?? label;
    }

    public string Label { get; }

    public string Token { get; }

    public int CaretBacktrack { get; }

    public string ToolTip { get; }
}