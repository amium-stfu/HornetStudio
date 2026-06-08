using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence.CustomControls;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets.Common;

/// <summary>
/// Represents one editable variable entry for a boolean condition.
/// </summary>
public sealed class ConditionVariableEntryViewModel : ObservableObject
{
    private string _name;
    private string _sourcePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionVariableEntryViewModel"/> class.
    /// </summary>
    /// <param name="name">The configured variable name.</param>
    /// <param name="sourcePath">The configured source path.</param>
    public ConditionVariableEntryViewModel(string name, string sourcePath)
    {
        _name = name ?? string.Empty;
        _sourcePath = sourcePath ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the variable name.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the source path used to resolve the variable value.
    /// </summary>
    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value ?? string.Empty);
    }
}

/// <summary>
/// Describes one token insertion button for the condition editor.
/// </summary>
public sealed class FormulaInsertButtonDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FormulaInsertButtonDefinition"/> class.
    /// </summary>
    /// <param name="label">The visible button label.</param>
    /// <param name="token">The inserted token text.</param>
    /// <param name="caretBacktrack">The optional caret offset after insertion.</param>
    /// <param name="tooltip">The optional tooltip text.</param>
    public FormulaInsertButtonDefinition(string label, string token, int caretBacktrack = 0, string? tooltip = null)
    {
        Label = label;
        Token = token;
        CaretBacktrack = caretBacktrack;
        ToolTip = tooltip ?? label;
    }

    /// <summary>
    /// Gets the visible button label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the inserted token text.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Gets the caret offset that is applied after insertion.
    /// </summary>
    public int CaretBacktrack { get; }

    /// <summary>
    /// Gets the tooltip shown for the button.
    /// </summary>
    public string ToolTip { get; }
}

/// <summary>
/// Provides shared editing, token insertion, and validation state for boolean condition formulas.
/// </summary>
public sealed class BooleanConditionEditorViewModel : ObservableObject
{
    private static readonly IReadOnlyList<FormulaInsertButtonDefinition> SharedOperatorButtons =
    [
        new FormulaInsertButtonDefinition("AND", " && ", tooltip: "Logical AND"),
        new FormulaInsertButtonDefinition("OR", " || ", tooltip: "Logical OR"),
        new FormulaInsertButtonDefinition("NOT", "!", tooltip: "Logical NOT"),
        new FormulaInsertButtonDefinition("=", " == ", tooltip: "Equal"),
        new FormulaInsertButtonDefinition("!=", " != ", tooltip: "Not equal"),
        new FormulaInsertButtonDefinition(">", " > ", tooltip: "Greater than"),
        new FormulaInsertButtonDefinition("<", " < ", tooltip: "Less than"),
        new FormulaInsertButtonDefinition(">=", " >= ", tooltip: "Greater or equal"),
        new FormulaInsertButtonDefinition("<=", " <= ", tooltip: "Less or equal"),
        new FormulaInsertButtonDefinition("(", "(", tooltip: "Open bracket"),
        new FormulaInsertButtonDefinition(")", ")", tooltip: "Close bracket")
    ];

    private readonly Dictionary<string, object?> _reservedPreviewVariables;
    private readonly HashSet<string> _reservedVariableNames;
    private string _formulaText;
    private string _formulaStatusMessage = string.Empty;
    private string _formulaStatusBrush = "#5E6777";

    /// <summary>
    /// Initializes a new instance of the <see cref="BooleanConditionEditorViewModel"/> class.
    /// </summary>
    /// <param name="formulaText">The initial formula text.</param>
    /// <param name="variables">The initial variables.</param>
    /// <param name="reservedVariableNames">Reserved variable names that cannot be used by user-defined entries.</param>
    /// <param name="reservedPreviewVariables">Reserved preview variables available during validation.</param>
    public BooleanConditionEditorViewModel(
        string? formulaText = null,
        IEnumerable<BooleanConditionVariableDefinition>? variables = null,
        IEnumerable<string>? reservedVariableNames = null,
        IReadOnlyDictionary<string, object?>? reservedPreviewVariables = null)
    {
        _formulaText = formulaText ?? string.Empty;
        _reservedVariableNames = new HashSet<string>(reservedVariableNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _reservedPreviewVariables = reservedPreviewVariables is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(reservedPreviewVariables, StringComparer.OrdinalIgnoreCase);

        Variables.CollectionChanged += OnVariablesCollectionChanged;
        foreach (var variable in variables ?? Array.Empty<BooleanConditionVariableDefinition>())
        {
            var entry = new ConditionVariableEntryViewModel(variable.Name, variable.SourcePath);
            entry.PropertyChanged += OnVariablePropertyChanged;
            Variables.Add(entry);
        }

        UpdateValidation();
    }

    /// <summary>
    /// Gets the editable condition variables.
    /// </summary>
    public ObservableCollection<ConditionVariableEntryViewModel> Variables { get; } = [];

    /// <summary>
    /// Gets the reusable operator buttons.
    /// </summary>
    public IReadOnlyList<FormulaInsertButtonDefinition> OperatorButtons => SharedOperatorButtons;

    /// <summary>
    /// Gets the variable token buttons for all configured variables.
    /// </summary>
    public IReadOnlyList<FormulaInsertButtonDefinition> VariableButtons =>
    [
        .. Variables
            .Where(static variable => !string.IsNullOrWhiteSpace(variable.Name))
            .Select(variable => new FormulaInsertButtonDefinition(variable.Name.Trim(), $"{{{variable.Name.Trim()}}}"))
    ];

    /// <summary>
    /// Gets the example text shown below the editor.
    /// </summary>
    public string ExampleText => "Examples: ({A} > 10) && {Enabled}   or   !{Fault}";

    /// <summary>
    /// Gets or sets the condition formula text.
    /// </summary>
    public string FormulaText
    {
        get => _formulaText;
        set
        {
            if (!SetProperty(ref _formulaText, value ?? string.Empty))
            {
                return;
            }

            UpdateValidation();
        }
    }

    /// <summary>
    /// Gets the current validation status message.
    /// </summary>
    public string FormulaStatusMessage
    {
        get => _formulaStatusMessage;
        private set => SetProperty(ref _formulaStatusMessage, value ?? string.Empty);
    }

    /// <summary>
    /// Gets the current validation status brush value.
    /// </summary>
    public string FormulaStatusBrush
    {
        get => _formulaStatusBrush;
        private set => SetProperty(ref _formulaStatusBrush, value ?? "#5E6777");
    }

    /// <summary>
    /// Adds one new variable with an automatically generated name.
    /// </summary>
    public void AddVariable()
    {
        AddVariable(GenerateNextVariableName(), string.Empty);
    }

    /// <summary>
    /// Adds one variable entry.
    /// </summary>
    /// <param name="name">The initial variable name.</param>
    /// <param name="sourcePath">The initial source path.</param>
    public void AddVariable(string? name, string? sourcePath)
    {
        var entry = new ConditionVariableEntryViewModel(name ?? string.Empty, sourcePath ?? string.Empty);
        entry.PropertyChanged += OnVariablePropertyChanged;
        Variables.Add(entry);
        RaisePropertyChanged(nameof(VariableButtons));
        UpdateValidation();
    }

    /// <summary>
    /// Removes one variable entry.
    /// </summary>
    /// <param name="variable">The variable to remove.</param>
    public void RemoveVariable(ConditionVariableEntryViewModel variable)
    {
        if (variable is null)
        {
            return;
        }

        variable.PropertyChanged -= OnVariablePropertyChanged;
        if (Variables.Remove(variable))
        {
            RaisePropertyChanged(nameof(VariableButtons));
            UpdateValidation();
        }
    }

    /// <summary>
    /// Inserts one token into the formula text.
    /// </summary>
    /// <param name="token">The token to insert.</param>
    /// <param name="caretIndex">The insertion caret index.</param>
    /// <param name="caretBacktrack">The caret offset that is applied after insertion.</param>
    /// <returns>The resulting caret index.</returns>
    public int InsertFormulaToken(string token, int caretIndex, int caretBacktrack = 0)
    {
        token ??= string.Empty;
        caretIndex = Math.Clamp(caretIndex, 0, FormulaText.Length);
        FormulaText = FormulaText.Insert(caretIndex, token);
        return Math.Clamp(caretIndex + token.Length + caretBacktrack, 0, FormulaText.Length);
    }

    /// <summary>
    /// Builds the persisted variable definitions after validation.
    /// </summary>
    /// <param name="variables">The created variable list.</param>
    /// <param name="errorMessage">The validation error message when building fails.</param>
    /// <returns><see langword="true"/> when the variables are valid.</returns>
    public bool TryBuildVariables(out List<BooleanConditionVariableDefinition> variables, out string errorMessage)
    {
        variables = [];
        errorMessage = string.Empty;

        var usedNames = new HashSet<string>(_reservedVariableNames, StringComparer.OrdinalIgnoreCase);
        foreach (var variable in Variables)
        {
            var name = (variable.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                errorMessage = "Variable name is required.";
                return false;
            }

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

            variables.Add(new BooleanConditionVariableDefinition
            {
                Name = name,
                SourcePath = TargetPathHelper.NormalizeConfiguredTargetPath(variable.SourcePath)
            });
        }

        return true;
    }

    /// <summary>
    /// Validates the current variables and formula.
    /// </summary>
    /// <param name="errorMessage">The validation error message when validation fails.</param>
    /// <returns><see langword="true"/> when the formula is valid.</returns>
    public bool TryValidate(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryBuildVariables(out var variables, out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FormulaText))
        {
            errorMessage = "Formula is required.";
            return false;
        }

        if (!CustomSignalFormulaEngine.TryEvaluateBooleanExpression(FormulaText.Trim(), CreatePreviewVariables(variables, _reservedPreviewVariables), out _, out errorMessage))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates a condition variable name.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]))
        {
            return false;
        }

        return name.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    /// <summary>
    /// Creates preview values for condition validation.
    /// </summary>
    /// <param name="variables">The configured variable definitions.</param>
    /// <param name="reservedPreviewVariables">The reserved preview values that should already exist.</param>
    /// <returns>The created preview dictionary.</returns>
    public static Dictionary<string, object?> CreatePreviewVariables(
        IEnumerable<BooleanConditionVariableDefinition> variables,
        IReadOnlyDictionary<string, object?>? reservedPreviewVariables = null)
    {
        var dictionary = reservedPreviewVariables is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(reservedPreviewVariables, StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            dictionary[variable.Name] = 1d;
        }

        return dictionary;
    }

    private string GenerateNextVariableName()
    {
        for (var offset = 0; offset < 26; offset++)
        {
            var candidate = ((char)('A' + offset)).ToString();
            if (Variables.All(variable => !string.Equals(variable.Name, candidate, StringComparison.OrdinalIgnoreCase))
                && !_reservedVariableNames.Contains(candidate))
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
                if (Variables.All(variable => !string.Equals(variable.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    && !_reservedVariableNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            suffix++;
        }
    }

    private void OnVariablesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ConditionVariableEntryViewModel>())
            {
                item.PropertyChanged += OnVariablePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ConditionVariableEntryViewModel>())
            {
                item.PropertyChanged -= OnVariablePropertyChanged;
            }
        }

        RaisePropertyChanged(nameof(VariableButtons));
        UpdateValidation();
    }

    private void OnVariablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(VariableButtons));
        UpdateValidation();
    }

    private void UpdateValidation()
    {
        if (TryValidate(out var errorMessage))
        {
            FormulaStatusMessage = "Formula looks valid.";
            FormulaStatusBrush = "#027A48";
        }
        else
        {
            FormulaStatusMessage = errorMessage;
            FormulaStatusBrush = "#B42318";
        }
    }
}
