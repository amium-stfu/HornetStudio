using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class UdlDemoModulesDialogWindow : Window
{
    public UdlDemoModulesDialogWindow()
    {
        ViewModel = new UdlDemoModulesDialogViewModel(null, new FolderItemModel(), string.Empty);
        DataContext = ViewModel;
        InitializeComponent();
    }

    public UdlDemoModulesDialogWindow(MainWindowViewModel? viewModel, FolderItemModel ownerItem, string rawDefinitions)
        : this()
    {
        ViewModel = new UdlDemoModulesDialogViewModel(viewModel, ownerItem, rawDefinitions);
        DataContext = ViewModel;
    }

    public UdlDemoModulesDialogViewModel ViewModel { get; private set; }

    public static async Task<string?> ShowAsync(Window owner, MainWindowViewModel? viewModel, FolderItemModel ownerItem, string rawDefinitions)
    {
        var dialog = new UdlDemoModulesDialogWindow(viewModel, ownerItem, rawDefinitions);
        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddModule();
        e.Handled = true;
    }

    private void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.RemoveSelectedModule();
        e.Handled = true;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildSerializedDefinitions(out var serializedDefinitions, out var errorMessage))
        {
            ViewModel.ErrorMessage = errorMessage;
            return;
        }

        Close(serializedDefinitions);
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close((string?)null);
        e.Handled = true;
    }
}

public sealed class UdlDemoModulesDialogViewModel : ObservableObject
{
    private readonly FolderItemModel _ownerItem;
    private UdlDemoModuleEditorRow? _selectedRow;
    private string _errorMessage = string.Empty;

    public UdlDemoModulesDialogViewModel(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, string rawDefinitions)
    {
        _ownerItem = ownerItem;
        Rows = new ObservableCollection<UdlDemoModuleEditorRow>(UdlDemoModuleDefinitionCodec.ParseDefinitions(rawDefinitions).Select(UdlDemoModuleEditorRow.FromDefinition));
        SelectedRow = Rows.FirstOrDefault();

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

        KindOptions = Enum.GetNames<UdlDemoModuleKind>();
        GeneratorOptions = Enum.GetNames<UdlDemoGeneratorKind>();
        NoiseModeOptions = Enum.GetNames<UdlDemoNoiseMode>();
    }

    public ObservableCollection<UdlDemoModuleEditorRow> Rows { get; }

    public IReadOnlyList<string> KindOptions { get; }

    public IReadOnlyList<string> GeneratorOptions { get; }

    public IReadOnlyList<string> NoiseModeOptions { get; }

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

    public UdlDemoModuleEditorRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                RaisePropertyChanged(nameof(HasSelectedRow));
                RaisePropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool HasSelectedRow => SelectedRow is not null;

    public bool ShowEmptyState => !HasSelectedRow;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value ?? string.Empty);
    }

    public void AddModule()
    {
        var suffix = 1;
        string candidateName;
        do
        {
            candidateName = $"m{suffix:000}";
            suffix++;
        }
        while (Rows.Any(row => string.Equals(row.Name, candidateName, StringComparison.OrdinalIgnoreCase)));

        var row = new UdlDemoModuleEditorRow
        {
            Name = candidateName
        };

        Rows.Add(row);
        SelectedRow = row;
        ErrorMessage = string.Empty;
    }

    public void RemoveSelectedModule()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var index = Rows.IndexOf(SelectedRow);
        Rows.Remove(SelectedRow);
        SelectedRow = Rows.Count == 0
            ? null
            : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
        ErrorMessage = string.Empty;
    }

    public bool TryBuildSerializedDefinitions(out string serializedDefinitions, out string errorMessage)
    {
        serializedDefinitions = string.Empty;
        errorMessage = string.Empty;

        var definitions = new List<UdlDemoModuleDefinition>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            if (!row.TryBuildDefinition(out var definition, out errorMessage))
            {
                return false;
            }

            if (!usedNames.Add(definition.Name))
            {
                errorMessage = $"Duplicate module name '{definition.Name}'.";
                return false;
            }

            definitions.Add(definition);
        }

        _ = _ownerItem;
        serializedDefinitions = UdlDemoModuleDefinitionCodec.SerializeDefinitions(definitions);
        return true;
    }
}

public sealed class UdlDemoModuleEditorRow : ObservableObject
{
    private string _name = string.Empty;
    private string _selectedKind = UdlDemoModuleKind.Dynamic.ToString();
    private string _selectedGenerator = UdlDemoGeneratorKind.Sine.ToString();
    private string _unit = string.Empty;
    private string _format = string.Empty;
    private string _baseValueText = "0";
    private string _amplitudeText = "1";
    private string _periodSecondsText = "5";
    private string _initialValueText = "0";
    private string _setScaleText = "1";
    private string _setOffsetText = "0";
    private string _setTauSecondsText = "0";

    public UdlDemoModuleEditorRow()
    {
        NoiseFault = new UdlDemoFaultEditorRow(UdlDemoFaultKind.Noise)
        {
            AmountText = "0",
            UseJitter = true,
            PeriodSecondsText = "2",
            UpdateIntervalMsText = "250"
        };
        FreezeFault = new UdlDemoFaultEditorRow(UdlDemoFaultKind.Freeze)
        {
            IntervalSecondsText = "5",
            DurationMsText = "1000"
        };
    }

    public UdlDemoFaultEditorRow NoiseFault { get; }

    public UdlDemoFaultEditorRow FreezeFault { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(Summary));
            }
        }
    }

    public string SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetProperty(ref _selectedKind, value ?? UdlDemoModuleKind.Dynamic.ToString()))
            {
                RaisePropertyChanged(nameof(IsDynamic));
                RaisePropertyChanged(nameof(IsSetDriven));
                RaisePropertyChanged(nameof(Summary));
            }
        }
    }

    public string SelectedGenerator
    {
        get => _selectedGenerator;
        set
        {
            if (SetProperty(ref _selectedGenerator, value ?? UdlDemoGeneratorKind.Sine.ToString()))
            {
                RaisePropertyChanged(nameof(Summary));
            }
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

    public string BaseValueText
    {
        get => _baseValueText;
        set => SetProperty(ref _baseValueText, value ?? string.Empty);
    }

    public string AmplitudeText
    {
        get => _amplitudeText;
        set => SetProperty(ref _amplitudeText, value ?? string.Empty);
    }

    public string PeriodSecondsText
    {
        get => _periodSecondsText;
        set => SetProperty(ref _periodSecondsText, value ?? string.Empty);
    }

    public string InitialValueText
    {
        get => _initialValueText;
        set => SetProperty(ref _initialValueText, value ?? string.Empty);
    }

    public string SetScaleText
    {
        get => _setScaleText;
        set => SetProperty(ref _setScaleText, value ?? string.Empty);
    }

    public string SetOffsetText
    {
        get => _setOffsetText;
        set => SetProperty(ref _setOffsetText, value ?? string.Empty);
    }

    public string SetTauSecondsText
    {
        get => _setTauSecondsText;
        set => SetProperty(ref _setTauSecondsText, value ?? string.Empty);
    }

    public bool IsDynamic => string.Equals(SelectedKind, UdlDemoModuleKind.Dynamic.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsSetDriven => string.Equals(SelectedKind, UdlDemoModuleKind.SetDriven.ToString(), StringComparison.OrdinalIgnoreCase);

    public string Summary => IsDynamic
        ? $"{SelectedKind} | {SelectedGenerator}"
        : $"{SelectedKind} | Request-driven";

    public static UdlDemoModuleEditorRow FromDefinition(UdlDemoModuleDefinition definition)
    {
        var row = new UdlDemoModuleEditorRow
        {
            Name = definition.Name,
            SelectedKind = definition.Kind.ToString(),
            SelectedGenerator = definition.Generator.ToString(),
            Unit = definition.Unit,
            Format = definition.Format,
            BaseValueText = FormatNumber(definition.BaseValue),
            AmplitudeText = FormatNumber(definition.Amplitude),
            PeriodSecondsText = FormatNumber(definition.PeriodSeconds),
            InitialValueText = FormatNumber(definition.InitialValue),
            SetScaleText = FormatNumber(definition.SetScale),
            SetOffsetText = FormatNumber(definition.SetOffset),
            SetTauSecondsText = FormatNumber(definition.SetTauSeconds)
        };

        var noiseFault = definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Noise);
        if (noiseFault is not null)
        {
            row.NoiseFault.Enabled = noiseFault.Enabled;
            row.NoiseFault.AmountText = FormatNumber(noiseFault.Amount);
            row.NoiseFault.PeakAmountText = FormatNumber(noiseFault.PeakAmount);
            row.NoiseFault.UseJitter = noiseFault.UseJitter;
            row.NoiseFault.UseSine = noiseFault.UseSine;
            row.NoiseFault.UsePeak = noiseFault.UsePeak;
            row.NoiseFault.PeriodSecondsText = FormatNumber(noiseFault.PeriodSeconds);
            row.NoiseFault.UpdateIntervalMsText = noiseFault.UpdateIntervalMs.ToString(CultureInfo.InvariantCulture);
            row.NoiseFault.IntervalSecondsText = FormatNumber(noiseFault.IntervalSeconds);
            row.NoiseFault.DurationMsText = noiseFault.DurationMs.ToString(CultureInfo.InvariantCulture);
        }

        var freezeFault = definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Freeze);
        if (freezeFault is not null)
        {
            row.FreezeFault.Enabled = freezeFault.Enabled;
            row.FreezeFault.AmountText = FormatNumber(freezeFault.Amount);
            row.FreezeFault.IntervalSecondsText = FormatNumber(freezeFault.IntervalSeconds);
            row.FreezeFault.DurationMsText = freezeFault.DurationMs.ToString(CultureInfo.InvariantCulture);
        }

        return row;
    }

    public bool TryBuildDefinition(out UdlDemoModuleDefinition definition, out string errorMessage)
    {
        definition = new UdlDemoModuleDefinition();
        errorMessage = string.Empty;

        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Each module needs a name.";
            return false;
        }

        if (!IsCanonicalModuleName(name))
        {
            errorMessage = "Module name must use format m000: lowercase m followed by exactly three digits.";
            return false;
        }

        if (!TryParseDouble(BaseValueText, out var baseValue, out errorMessage, "BaseValue")
            || !TryParseDouble(AmplitudeText, out var amplitude, out errorMessage, "Amplitude")
            || !TryParseDouble(PeriodSecondsText, out var periodSeconds, out errorMessage, "PeriodSeconds")
            || !TryParseDouble(InitialValueText, out var initialValue, out errorMessage, "InitialValue")
            || !TryParseDouble(SetScaleText, out var setScale, out errorMessage, "SetScale")
            || !TryParseDouble(SetOffsetText, out var setOffset, out errorMessage, "SetOffset")
            || !TryParseDouble(SetTauSecondsText, out var setTauSeconds, out errorMessage, "SetTauSeconds"))
        {
            return false;
        }

        if (!NoiseFault.TryBuildDefinition(out var noiseFault, out errorMessage)
            || !FreezeFault.TryBuildDefinition(out var freezeFault, out errorMessage))
        {
            return false;
        }

        definition = new UdlDemoModuleDefinition
        {
            Name = name,
            Kind = Enum.TryParse<UdlDemoModuleKind>(SelectedKind, true, out var parsedKind) ? parsedKind : UdlDemoModuleKind.Dynamic,
            Generator = Enum.TryParse<UdlDemoGeneratorKind>(SelectedGenerator, true, out var parsedGenerator) ? parsedGenerator : UdlDemoGeneratorKind.Sine,
            Unit = Unit ?? string.Empty,
            Format = Format ?? string.Empty,
            BaseValue = baseValue,
            Amplitude = amplitude,
            PeriodSeconds = periodSeconds <= 0 ? 5 : periodSeconds,
            InitialValue = initialValue,
            SetScale = setScale,
            SetOffset = setOffset,
            SetTauSeconds = Math.Max(0, setTauSeconds),
            Faults = [noiseFault, freezeFault]
        };

        return true;
    }

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool IsCanonicalModuleName(string value)
        => value.Length == 4
           && value[0] == 'm'
           && char.IsDigit(value[1])
           && char.IsDigit(value[2])
           && char.IsDigit(value[3]);

    private static bool TryParseDouble(string rawValue, out double value, out string errorMessage, string label)
    {
        if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Invalid number in {label}. Use invariant decimal notation like 1.5.";
        return false;
    }
}

public sealed class UdlDemoFaultEditorRow : ObservableObject
{
    private bool _enabled;
    private string _amountText = "0";
    private string _peakAmountText = "0";
    private bool _useJitter = true;
    private bool _useSine;
    private bool _usePeak;
    private string _periodSecondsText = "2";
    private string _updateIntervalMsText = "250";
    private string _intervalSecondsText = "5";
    private string _durationMsText = "1000";

    public UdlDemoFaultEditorRow(UdlDemoFaultKind kind)
    {
        Kind = kind;
    }

    public UdlDemoFaultKind Kind { get; }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string AmountText
    {
        get => _amountText;
        set => SetProperty(ref _amountText, value ?? string.Empty);
    }

    public string PeakAmountText
    {
        get => _peakAmountText;
        set => SetProperty(ref _peakAmountText, value ?? string.Empty);
    }

    public bool UseJitter
    {
        get => _useJitter;
        set => SetProperty(ref _useJitter, value);
    }

    public bool UseSine
    {
        get => _useSine;
        set
        {
            if (SetProperty(ref _useSine, value))
            {
                RaisePropertyChanged(nameof(UsesSinePeriod));
            }
        }
    }

    public bool UsePeak
    {
        get => _usePeak;
        set
        {
            if (SetProperty(ref _usePeak, value))
            {
                RaisePropertyChanged(nameof(UsesUpdateInterval));
                RaisePropertyChanged(nameof(UsesPeakDuration));
            }
        }
    }

    public string PeriodSecondsText
    {
        get => _periodSecondsText;
        set => SetProperty(ref _periodSecondsText, value ?? string.Empty);
    }

    public string UpdateIntervalMsText
    {
        get => _updateIntervalMsText;
        set => SetProperty(ref _updateIntervalMsText, value ?? string.Empty);
    }

    public string IntervalSecondsText
    {
        get => _intervalSecondsText;
        set => SetProperty(ref _intervalSecondsText, value ?? string.Empty);
    }

    public string DurationMsText
    {
        get => _durationMsText;
        set => SetProperty(ref _durationMsText, value ?? string.Empty);
    }

    public bool IsNoise => Kind == UdlDemoFaultKind.Noise;

    public bool UsesSinePeriod => IsNoise && UseSine;

    public bool UsesUpdateInterval => IsNoise && UsePeak;

    public bool UsesPeakDuration => IsNoise && UsePeak;

    public bool TryBuildDefinition(out UdlDemoFaultDefinition definition, out string errorMessage)
    {
        definition = new UdlDemoFaultDefinition { Kind = Kind };
        errorMessage = string.Empty;

        if (!double.TryParse(AmountText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount))
        {
            errorMessage = $"Invalid fault amount for {Kind}.";
            return false;
        }

        if (!double.TryParse(PeakAmountText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var peakAmount))
        {
            errorMessage = $"Invalid peak amount for {Kind}.";
            return false;
        }

        if (!double.TryParse(PeriodSecondsText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var periodSeconds))
        {
            errorMessage = $"Invalid fault period for {Kind}.";
            return false;
        }

        if (!int.TryParse(UpdateIntervalMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var updateIntervalMs))
        {
            errorMessage = $"Invalid fault update interval for {Kind}.";
            return false;
        }

        if (!double.TryParse(IntervalSecondsText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var intervalSeconds))
        {
            errorMessage = $"Invalid fault interval for {Kind}.";
            return false;
        }

        if (!int.TryParse(DurationMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
        {
            errorMessage = $"Invalid fault duration for {Kind}.";
            return false;
        }

        definition = new UdlDemoFaultDefinition
        {
            Kind = Kind,
            Enabled = Enabled,
            Amount = amount,
            PeakAmount = peakAmount,
            UseJitter = UseJitter,
            UseSine = UseSine,
            UsePeak = UsePeak,
            NoiseMode = UsePeak
                ? UdlDemoNoiseMode.PeakJitter
                : UseSine && UseJitter
                    ? UdlDemoNoiseMode.SineWithJitter
                    : UseSine
                        ? UdlDemoNoiseMode.Sine
                        : UdlDemoNoiseMode.Jitter,
            PeriodSeconds = periodSeconds <= 0 ? 2 : periodSeconds,
            UpdateIntervalMs = updateIntervalMs <= 0 ? 250 : updateIntervalMs,
            IntervalSeconds = intervalSeconds <= 0 ? 5 : intervalSeconds,
            DurationMs = durationMs <= 0 ? 1000 : durationMs
        };

        return true;
    }
}

