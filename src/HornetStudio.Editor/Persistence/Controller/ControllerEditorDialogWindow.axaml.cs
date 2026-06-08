using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Edits a PID controller definition for the controller widget.
/// </summary>
public partial class ControllerEditorDialogWindow : Window
{
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private readonly FolderItemModel _ownerItem = new();
    private IReadOnlyList<string> _sourceOptions = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerEditorDialogWindow"/> class.
    /// </summary>
    public ControllerEditorDialogWindow()
    {
        ViewModel = new ControllerEditorDialogViewModel(null, new FolderItemModel(), null);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private ControllerEditorDialogWindow(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, ControllerDefinition? definition, IEnumerable<string> sourceOptions)
        : this()
    {
        _mainWindowViewModel = mainWindowViewModel;
        _ownerItem = ownerItem;
        _sourceOptions = sourceOptions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        ViewModel = new ControllerEditorDialogViewModel(mainWindowViewModel, ownerItem, definition);
        DataContext = ViewModel;
    }

    /// <summary>
    /// Gets the dialog view model.
    /// </summary>
    public ControllerEditorDialogViewModel ViewModel { get; private set; }

    /// <summary>
    /// Shows the controller editor dialog.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="mainWindowViewModel">The main window view model.</param>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="definition">The definition to edit, or <see langword="null"/> for a new definition.</param>
    /// <param name="sourceOptions">The available registry paths.</param>
    /// <returns>The saved definition, or <see langword="null"/> when canceled.</returns>
    public static Task<ControllerDefinition?> ShowAsync(Window owner, MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, ControllerDefinition? definition, IEnumerable<string> sourceOptions)
    {
        var dialog = new ControllerEditorDialogWindow(mainWindowViewModel, ownerItem, definition, sourceOptions);
        return dialog.ShowDialog<ControllerDefinition?>(owner);
    }

    private async void OnPickSourceClicked(object? sender, RoutedEventArgs e)
    {
        await PickPathAsync(currentValue: ViewModel.SourcePath, apply: value => ViewModel.SourcePath = value);
        e.Handled = true;
    }

    private async void OnPickOutputClicked(object? sender, RoutedEventArgs e)
    {
        await PickPathAsync(currentValue: ViewModel.OutputPath, apply: value => ViewModel.OutputPath = value);
        e.Handled = true;
    }

    private async Task PickPathAsync(string currentValue, Action<string> apply)
    {
        var dialog = new TargetTreeSelectionDialogWindow(_mainWindowViewModel, _sourceOptions, currentValue, _ownerItem.FolderName);
        await dialog.ShowDialog(this);
        if (!string.IsNullOrWhiteSpace(dialog.CommittedSelection))
        {
            apply(dialog.CommittedSelection);
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
        Close((ControllerDefinition?)null);
        e.Handled = true;
    }
}

/// <summary>
/// Provides dialog state for editing a PID controller definition.
/// </summary>
public sealed class ControllerEditorDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> SupportedTypeOptions = [ControllerType.PID.ToString()];
    private readonly FolderItemModel _ownerItem;
    private readonly string _originalName;
    private string _selectedType = ControllerType.PID.ToString();
    private string _name = string.Empty;
    private string _sourcePath = string.Empty;
    private string _outputPath = string.Empty;
    private string _ksText = "1.0";
    private string _tuText = "0.0";
    private string _tgText = "0.0";
    private string _dFilterTauMsText = "0.0";
    private string _setMinText = "0.0";
    private string _setMaxText = "100.0";
    private string _outMinText = "0.0";
    private string _outMaxText = "100.0";
    private string _computeIntervalMsText = "100";
    private string _outputIntervalMsText = "100";
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerEditorDialogViewModel"/> class.
    /// </summary>
    /// <param name="mainWindowViewModel">The main window view model.</param>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="definition">The definition to edit, or <see langword="null"/>.</param>
    public ControllerEditorDialogViewModel(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, ControllerDefinition? definition)
    {
        _ownerItem = ownerItem;
        _originalName = definition?.Name ?? string.Empty;
        DialogBackground = mainWindowViewModel?.DialogBackground ?? "#E3E5EE";
        SectionBackground = mainWindowViewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
        BorderColor = mainWindowViewModel?.CardBorderBrush ?? "#CBD5E1";
        PrimaryTextBrush = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = mainWindowViewModel?.SecondaryTextBrush ?? "#5E6777";
        EditorBackground = mainWindowViewModel?.ParameterEditBackgrundColor ?? "#FFFFFF";
        EditorForeground = mainWindowViewModel?.ParameterEditForeColor ?? "#111827";
        ButtonBackground = mainWindowViewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = mainWindowViewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";

        if (definition is null)
        {
            Name = GenerateNextControllerName();
            return;
        }

        Name = definition.Name;
        SourcePath = definition.SourcePath;
        OutputPath = definition.OutputPath;
        SelectedType = definition.Type.ToString();
        KsText = definition.Pid.Ks.ToString(CultureInfo.InvariantCulture);
        TuText = definition.Pid.Tu.ToString(CultureInfo.InvariantCulture);
        TgText = definition.Pid.Tg.ToString(CultureInfo.InvariantCulture);
        DFilterTauMsText = definition.Pid.DFilterTauMs.ToString(CultureInfo.InvariantCulture);
        SetMinText = definition.Pid.SetMin.ToString(CultureInfo.InvariantCulture);
        SetMaxText = definition.Pid.SetMax.ToString(CultureInfo.InvariantCulture);
        OutMinText = definition.Pid.OutMin.ToString(CultureInfo.InvariantCulture);
        OutMaxText = definition.Pid.OutMax.ToString(CultureInfo.InvariantCulture);
        ComputeIntervalMsText = definition.Pid.ComputeIntervalMs.ToString(CultureInfo.InvariantCulture);
        OutputIntervalMsText = definition.Pid.OutputIntervalMs.ToString(CultureInfo.InvariantCulture);
    }

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

    public IReadOnlyList<string> TypeOptions => SupportedTypeOptions;

    public string SelectedType
    {
        get => _selectedType;
        set => SetProperty(ref _selectedType, value ?? ControllerType.PID.ToString());
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

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value ?? string.Empty);
    }

    public string KsText
    {
        get => _ksText;
        set => SetProperty(ref _ksText, value ?? string.Empty);
    }

    public string TuText
    {
        get => _tuText;
        set => SetProperty(ref _tuText, value ?? string.Empty);
    }

    public string TgText
    {
        get => _tgText;
        set => SetProperty(ref _tgText, value ?? string.Empty);
    }

    public string DFilterTauMsText
    {
        get => _dFilterTauMsText;
        set => SetProperty(ref _dFilterTauMsText, value ?? string.Empty);
    }

    public string SetMinText
    {
        get => _setMinText;
        set => SetProperty(ref _setMinText, value ?? string.Empty);
    }

    public string SetMaxText
    {
        get => _setMaxText;
        set => SetProperty(ref _setMaxText, value ?? string.Empty);
    }

    public string OutMinText
    {
        get => _outMinText;
        set => SetProperty(ref _outMinText, value ?? string.Empty);
    }

    public string OutMaxText
    {
        get => _outMaxText;
        set => SetProperty(ref _outMaxText, value ?? string.Empty);
    }

    public string ComputeIntervalMsText
    {
        get => _computeIntervalMsText;
        set => SetProperty(ref _computeIntervalMsText, value ?? string.Empty);
    }

    public string OutputIntervalMsText
    {
        get => _outputIntervalMsText;
        set => SetProperty(ref _outputIntervalMsText, value ?? string.Empty);
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

    /// <summary>
    /// Builds a validated controller definition from the current dialog state.
    /// </summary>
    /// <param name="definition">The resulting definition.</param>
    /// <param name="errorMessage">The validation error message.</param>
    /// <returns><see langword="true"/> when validation succeeded; otherwise <see langword="false"/>.</returns>
    public bool TryBuildDefinition(out ControllerDefinition? definition, out string errorMessage)
    {
        definition = null;
        errorMessage = string.Empty;

        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Name is required.";
            return false;
        }

        var existingNames = ControllerDefinitionCodec.ParseDefinitions(_ownerItem.ControllerDefinitions)
            .Select(controller => controller.Name)
            .Where(static controllerName => !string.IsNullOrWhiteSpace(controllerName))
            .Where(controllerName => !string.Equals(controllerName, _originalName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existingNames.Contains(name))
        {
            errorMessage = "Name must be unique within the controller widget.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            errorMessage = "Source path is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            errorMessage = "Output path is required.";
            return false;
        }

        if (!TryParseDouble(KsText, out var ks)
            || !TryParseDouble(TuText, out var tu)
            || !TryParseDouble(TgText, out var tg)
            || !TryParseDouble(DFilterTauMsText, out var dFilterTauMs)
            || !TryParseDouble(SetMinText, out var setMin)
            || !TryParseDouble(SetMaxText, out var setMax)
            || !TryParseDouble(OutMinText, out var outMin)
            || !TryParseDouble(OutMaxText, out var outMax))
        {
            errorMessage = "All PID numeric fields must contain valid numbers.";
            return false;
        }

        if (!TryParseInt(ComputeIntervalMsText, out var computeIntervalMs)
            || !TryParseInt(OutputIntervalMsText, out var outputIntervalMs))
        {
            errorMessage = "Compute and output intervals must be valid integers.";
            return false;
        }

        definition = new ControllerDefinition
        {
            Name = name,
            Enabled = true,
            Type = ControllerType.PID,
            SourcePath = SourcePath,
            OutputPath = OutputPath,
            Pid = new PidControllerDefinition
            {
                Ks = ks,
                Tu = tu,
                Tg = tg,
                DFilterTauMs = dFilterTauMs,
                SetMin = setMin,
                SetMax = setMax,
                OutMin = outMin,
                OutMax = outMax,
                ComputeIntervalMs = computeIntervalMs,
                OutputIntervalMs = outputIntervalMs
            }
        }.Normalize();

        return true;
    }

    private string GenerateNextControllerName()
    {
        var existingNames = ControllerDefinitionCodec.ParseDefinitions(_ownerItem.ControllerDefinitions)
            .Select(controller => controller.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = 1;
        while (true)
        {
            var candidate = $"pid_controller_{index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static bool TryParseDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1;
}