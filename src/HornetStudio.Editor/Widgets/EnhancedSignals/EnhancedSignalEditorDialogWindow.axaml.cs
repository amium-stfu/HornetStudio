using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EnhancedSignalEditorDialogWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private FolderItemModel? _ownerItem;
    private IReadOnlyList<string> _sourceOptions = Array.Empty<string>();
    private ExtendedSignalDefinition? _result;

    public EnhancedSignalEditorDialogWindow()
    {
        ViewModel = new EnhancedSignalEditorDialogViewModel(null, new FolderItemModel(), null);
        DataContext = ViewModel;
        InitializeComponent();
    }

    public EnhancedSignalEditorDialogWindow(MainWindowViewModel? viewModel, FolderItemModel ownerItem, ExtendedSignalDefinition? definition, IEnumerable<string> sourceOptions)
        : this()
    {
        _viewModel = viewModel;
        _ownerItem = ownerItem;
        _sourceOptions = sourceOptions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        ViewModel = new EnhancedSignalEditorDialogViewModel(viewModel, ownerItem, definition);
        DataContext = ViewModel;
    }

    public EnhancedSignalEditorDialogViewModel ViewModel { get; private set; }

    public static async Task<ExtendedSignalDefinition?> ShowAsync(Window owner, MainWindowViewModel? viewModel, FolderItemModel ownerItem, ExtendedSignalDefinition? definition, IEnumerable<string> sourceOptions)
    {
        var dialog = new EnhancedSignalEditorDialogWindow(viewModel, ownerItem, definition, sourceOptions);
        return await dialog.ShowDialog<ExtendedSignalDefinition?>(owner);
    }

    private async void OnPickSourceClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.SourcePath = await PickTargetAsync(ViewModel.SourcePath) ?? ViewModel.SourcePath;
        e.Handled = true;
    }

    private async void OnPickWriteTargetClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.WritePath = await PickTargetAsync(ViewModel.WritePath) ?? ViewModel.WritePath;
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
        SyncAdjustmentInputsFromControls();

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
        Close((ExtendedSignalDefinition?)null);
        e.Handled = true;
    }

    private void OnSectionToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string sectionKey })
        {
            return;
        }

        switch (sectionKey)
        {
            case "Signal":
                ViewModel.IsSignalSectionExpanded = !ViewModel.IsSignalSectionExpanded;
                break;
            case "PeakFilter":
                ViewModel.IsPeakFilterSectionExpanded = !ViewModel.IsPeakFilterSectionExpanded;
                break;
            case "BaseFilters":
                ViewModel.IsBaseFiltersSectionExpanded = !ViewModel.IsBaseFiltersSectionExpanded;
                break;
            case "Adjustment":
                ViewModel.IsAdjustmentSectionExpanded = !ViewModel.IsAdjustmentSectionExpanded;
                break;
            case "KalmanFilter":
                ViewModel.IsKalmanSectionExpanded = !ViewModel.IsKalmanSectionExpanded;
                break;
            case "Statistics":
                ViewModel.IsStatisticsSectionExpanded = !ViewModel.IsStatisticsSectionExpanded;
                break;
        }

        e.Handled = true;
    }

    private void SyncAdjustmentInputsFromControls()
    {
        var adjustmentEnabledCheckBox = this.FindControl<CheckBox>("AdjustmentEnabledCheckBox");
        if (adjustmentEnabledCheckBox is not null)
        {
            ViewModel.AdjustmentEnabled = adjustmentEnabledCheckBox.IsChecked == true;
        }

        var adjustmentModeComboBox = this.FindControl<ComboBox>("AdjustmentModeComboBox");
        if (adjustmentModeComboBox?.SelectedItem is not null)
        {
            ViewModel.SelectedAdjustmentMode = adjustmentModeComboBox.SelectedItem?.ToString() ?? ViewModel.SelectedAdjustmentMode;
        }

        var adjustmentOffsetTextBox = this.FindControl<TextBox>("AdjustmentOffsetTextBox");
        if (adjustmentOffsetTextBox is not null)
        {
            ViewModel.AdjustmentOffsetText = adjustmentOffsetTextBox.Text ?? string.Empty;
        }

        var adjustmentGainTextBox = this.FindControl<TextBox>("AdjustmentGainTextBox");
        if (adjustmentGainTextBox is not null)
        {
            ViewModel.AdjustmentGainText = adjustmentGainTextBox.Text ?? string.Empty;
        }

        var inverseMappingCheckBox = this.FindControl<CheckBox>("InverseMappingCheckBox");
        if (inverseMappingCheckBox is not null)
        {
            ViewModel.SupportsInverseMapping = inverseMappingCheckBox.IsChecked != false;
        }
    }

    private async void OnEditAdjustmentCurveClicked(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryCreateCurveEditorState(out var state, out var errorMessage))
        {
            ViewModel.ErrorMessage = errorMessage;
            e.Handled = true;
            return;
        }

        var updatedState = await AdjustmentCurveEditorDialogWindow.ShowAsync(this, _viewModel, state!);
        if (updatedState is not null)
        {
            ViewModel.ApplyCurveEditorState(updatedState);
            ViewModel.ErrorMessage = string.Empty;
        }

        e.Handled = true;
    }
}

public sealed record AdjustmentCurveEditorState(
    double Offset,
    double Gain,
    IReadOnlyList<ExtendedSignalSplinePoint> SplinePoints,
    ExtendedSignalSplineInterpolationMode SplineInterpolationMode);

public sealed class EnhancedSignalEditorDialogViewModel : ObservableObject
{
    private readonly FolderItemModel _ownerItem;
    private readonly string _originalName;
    private string _name = string.Empty;
    private bool _enabled = true;
    private bool _isWritable;
    private string _sourcePath = string.Empty;
    private string _writePath = string.Empty;
    private string _selectedWriteMode = SignalWriteMode.Direct.ToString();
    private string _unit = string.Empty;
    private string _format = string.Empty;
    private string _selectedFilterMode = ExtendedSignalFilterMode.Raw.ToString();
    private string _scanIntervalMsText = "100";
    private string _recordIntervalText = "1";
    private string _filterTimeMsText = "0";
    private bool _fillMissingWithLastValue = true;
    private bool _kalmanEnabled;
    private string _kalmanMeasurementNoiseRText = "1";
    private string _kalmanProcessNoiseQText = "0.05";
    private string _kalmanInitialErrorCovariancePText = "1";
    private string _kalmanTeachWindowMsText = "5000";
    private bool _kalmanTeachPauseOnDynamic = true;
    private string _kalmanTeachQFactorText = "0.05";
    private bool _kalmanTeachAutoApply = true;
    private bool _kalmanDynamicQEnabled;
    private string _kalmanDynamicQMinText = "0.05";
    private string _kalmanDynamicQMaxText = "0.5";
    private string _kalmanDynamicQHoldMsText = "250";
    private string _kalmanDynamicDetectionWindowMsText = "1000";
    private string _kalmanDynamicAngleThresholdText = "5";
    private string _kalmanDynamicAngleMaxText = "45";
    private string _kalmanDynamicReferenceFloorText = "1";
    private string _selectedKalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
    private string _kalmanDynamicResidualWeightText = "0.35";
    private bool _kalmanDynamicQUseExistingDynamic = true;
    private bool _adjustmentEnabled;
    private string _selectedAdjustmentMode = ExtendedSignalAdjustmentMode.None.ToString();
    private string _adjustmentOffsetText = "0";
    private string _adjustmentGainText = "1";
    private string _selectedSplineInterpolationMode = ExtendedSignalSplineInterpolationMode.Linear.ToString();
    private bool _supportsInverseMapping = true;
    private bool _forwardChildWritesToSource;
    private string _splinePointsText = string.Empty;
    private bool _statisticsEnabled;
    private bool _publishMin;
    private bool _publishMax;
    private bool _publishAverage;
    private bool _publishStdDev;
    private bool _publishIntegral;
    private string _stdDevWindowMsText = "10000";
    private string _integralDivisorMsText = "1";
    private bool _peakFilterEnabled;
    private string _peakThresholdText = "10";
    private string _peakMaxLengthMsText = "200";
    private bool _dynamicFilterEnabled;
    private string _dynamicDetectionWindowMsText = "1000";
    private string _dynamicSlopeThresholdText = "2";
    private string _dynamicRelativeSlopeThresholdPercentText = "0";
    private string _dynamicFilterTimeMsText = "1000";
    private string _dynamicHoldTimeMsText = "1500";
    private bool _isSignalSectionExpanded = true;
    private bool _isPeakFilterSectionExpanded;
    private bool _isBaseFiltersSectionExpanded = true;
    private bool _isAdjustmentSectionExpanded;
    private bool _isKalmanSectionExpanded;
    private bool _isStatisticsSectionExpanded;
    private string _errorMessage = string.Empty;
    private int _cachedSplinePointCount;
    private bool _suspendCurveSummaryUpdates;

    public EnhancedSignalEditorDialogViewModel(MainWindowViewModel? mainWindowViewModel, FolderItemModel ownerItem, ExtendedSignalDefinition? definition)
    {
        _ownerItem = ownerItem;
        _originalName = definition?.Name ?? string.Empty;
        FilterModeOptions = Enum.GetNames<ExtendedSignalFilterMode>();
        AdjustmentModeOptions =
        [
            ExtendedSignalAdjustmentMode.None.ToString(),
            ExtendedSignalAdjustmentMode.Spline.ToString()
        ];
        WriteModeOptions = [SignalWriteMode.Direct.ToString()];
        KalmanDynamicNormalizationModeOptions = [KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString()];

        DialogBackground = mainWindowViewModel?.DialogBackground ?? "#E3E5EE";
        SectionBackground = mainWindowViewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
        BorderColor = mainWindowViewModel?.CardBorderBrush ?? "#D5D9E0";
        PrimaryTextBrush = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = mainWindowViewModel?.SecondaryTextBrush ?? "#5E6777";
        EditorBackground = mainWindowViewModel?.ParameterEditBackgrundColor ?? "#FFFFFF";
        EditorForeground = mainWindowViewModel?.ParameterEditForeColor ?? "#111827";
        ParameterHoverColor = mainWindowViewModel?.ParameterHoverColor ?? "#BDBDBD";
        ButtonBackground = mainWindowViewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = mainWindowViewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = mainWindowViewModel?.PrimaryTextBrush ?? "#111827";
        TabSelectBackColor = mainWindowViewModel?.TabSelectBackColor ?? "#FFF1C4";
        TabSelectForeColor = mainWindowViewModel?.TabSelectForeColor ?? "#000000";
        TabBackColor = mainWindowViewModel?.TabBackColor ?? "#E7E7E7";
        TabForeColor = mainWindowViewModel?.TabForeColor ?? "#111827";
        SectionHeaderBackground = mainWindowViewModel?.EditorDialogSectionHeaderBackground ?? "#E8EEF6";
        SectionHeaderForeground = mainWindowViewModel?.EditorDialogSectionHeaderForeground ?? "#111827";
        SectionHeaderBorderBrush = mainWindowViewModel?.EditorDialogSectionHeaderBorderBrush ?? "#CBD5E1";
        SectionContentBackground = mainWindowViewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";

        if (definition is null)
        {
            Name = GenerateNextSignalName();
            RefreshCurveSummaryCaches();
            return;
        }

        Name = definition.Name;
        Enabled = definition.Enabled;
        IsWritable = definition.IsWritable;
        SourcePath = definition.SourcePath;
        WritePath = definition.WritePath;
        SelectedWriteMode = definition.WriteMode.ToString();
        Unit = definition.Unit;
        Format = definition.Format;
        SelectedFilterMode = definition.FilterMode.ToString();
        ScanIntervalMsText = definition.ScanIntervalMs.ToString(CultureInfo.InvariantCulture);
        RecordIntervalText = definition.RecordInterval.ToString(CultureInfo.InvariantCulture);
        FilterTimeMsText = definition.FilterTimeMs.ToString(CultureInfo.InvariantCulture);
        FillMissingWithLastValue = definition.FillMissingWithLastValue;
        KalmanEnabled = definition.KalmanEnabled;
        KalmanMeasurementNoiseRText = definition.KalmanMeasurementNoiseR.ToString(CultureInfo.InvariantCulture);
        KalmanProcessNoiseQText = definition.KalmanProcessNoiseQ.ToString(CultureInfo.InvariantCulture);
        KalmanInitialErrorCovariancePText = definition.KalmanInitialErrorCovarianceP.ToString(CultureInfo.InvariantCulture);
        KalmanTeachWindowMsText = definition.KalmanTeachWindowMs.ToString(CultureInfo.InvariantCulture);
        KalmanTeachPauseOnDynamic = definition.KalmanTeachPauseOnDynamic;
        KalmanTeachQFactorText = definition.KalmanTeachQFactor.ToString(CultureInfo.InvariantCulture);
        KalmanTeachAutoApply = definition.KalmanTeachAutoApply;
        KalmanDynamicQEnabled = definition.KalmanDynamicQEnabled;
        KalmanDynamicQMinText = definition.KalmanDynamicQMin.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicQMaxText = definition.KalmanDynamicQMax.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicQHoldMsText = definition.KalmanDynamicQHoldMs.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicDetectionWindowMsText = definition.KalmanDynamicDetectionWindowMs.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicAngleThresholdText = definition.KalmanDynamicAngleThresholdDeg.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicAngleMaxText = definition.KalmanDynamicAngleMaxDeg.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicReferenceFloorText = definition.KalmanDynamicReferenceFloor.ToString(CultureInfo.InvariantCulture);
        SelectedKalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
        KalmanDynamicResidualWeightText = definition.KalmanDynamicResidualWeight.ToString(CultureInfo.InvariantCulture);
        KalmanDynamicQUseExistingDynamic = definition.KalmanDynamicQUseExistingDynamic;
        AdjustmentEnabled = definition.Adjustment.Enabled;
        SelectedAdjustmentMode = definition.Adjustment.MappingMode.ToString();
        AdjustmentOffsetText = definition.Adjustment.Offset.ToString(CultureInfo.InvariantCulture);
        AdjustmentGainText = definition.Adjustment.Gain.ToString(CultureInfo.InvariantCulture);
        SelectedSplineInterpolationMode = definition.Adjustment.SplineInterpolationMode.ToString();
        SupportsInverseMapping = definition.Adjustment.SupportsInverseMapping;
        ForwardChildWritesToSource = definition.ForwardChildWritesToSource;
        SplinePointsText = string.Join(Environment.NewLine, definition.Adjustment.SplinePoints.Select(point => $"{point.Input.ToString(CultureInfo.InvariantCulture)}:{point.Output.ToString(CultureInfo.InvariantCulture)}"));
        StatisticsEnabled = definition.Statistics.Enabled;
        PublishMin = definition.Statistics.PublishMin;
        PublishMax = definition.Statistics.PublishMax;
        PublishAverage = definition.Statistics.PublishAverage;
        PublishStdDev = definition.Statistics.PublishStdDev;
        PublishIntegral = definition.Statistics.PublishIntegral;
        StdDevWindowMsText = definition.Statistics.StdDevWindowMs.ToString(CultureInfo.InvariantCulture);
        IntegralDivisorMsText = definition.Statistics.IntegralDivisorMs.ToString(CultureInfo.InvariantCulture);
        PeakFilterEnabled = definition.PeakFilter.Enabled;
        PeakThresholdText = definition.PeakFilter.Threshold.ToString(CultureInfo.InvariantCulture);
        PeakMaxLengthMsText = definition.PeakFilter.MaxLengthMs.ToString(CultureInfo.InvariantCulture);
        DynamicFilterEnabled = definition.DynamicFilter.Enabled;
        DynamicDetectionWindowMsText = definition.DynamicFilter.DetectionWindowMs.ToString(CultureInfo.InvariantCulture);
        DynamicSlopeThresholdText = definition.DynamicFilter.SlopeThreshold.ToString(CultureInfo.InvariantCulture);
        DynamicRelativeSlopeThresholdPercentText = definition.DynamicFilter.RelativeSlopeThresholdPercent.ToString(CultureInfo.InvariantCulture);
        DynamicFilterTimeMsText = definition.DynamicFilter.DynamicFilterTimeMs.ToString(CultureInfo.InvariantCulture);
        DynamicHoldTimeMsText = definition.DynamicFilter.HoldTimeMs.ToString(CultureInfo.InvariantCulture);

        IsPeakFilterSectionExpanded = definition.PeakFilter.Enabled;
        IsBaseFiltersSectionExpanded = definition.FilterMode != ExtendedSignalFilterMode.Raw || definition.DynamicFilter.Enabled;
        IsAdjustmentSectionExpanded = definition.Adjustment.Enabled;
        IsKalmanSectionExpanded = definition.KalmanEnabled;
        IsStatisticsSectionExpanded = definition.Statistics.Enabled;
        RefreshCurveSummaryCaches();
    }

    public IReadOnlyList<string> FilterModeOptions { get; }

    public IReadOnlyList<string> AdjustmentModeOptions { get; }

    public IReadOnlyList<string> WriteModeOptions { get; }

    public IReadOnlyList<string> KalmanDynamicNormalizationModeOptions { get; }

    public string DialogBackground { get; }

    public string SectionBackground { get; }

    public string BorderColor { get; }

    public string PrimaryTextBrush { get; }

    public string SecondaryTextBrush { get; }

    public string EditorBackground { get; }

    public string EditorForeground { get; }

    public string ParameterHoverColor { get; }

    public string ButtonBackground { get; }

    public string ButtonBorderBrush { get; }

    public string ButtonForeground { get; }

    public string TabSelectBackColor { get; }

    public string TabSelectForeColor { get; }

    public string TabBackColor { get; }

    public string TabForeColor { get; }

    public string SectionHeaderBackground { get; }

    public string SectionHeaderForeground { get; }

    public string SectionHeaderBorderBrush { get; }

    public string SectionContentBackground { get; }

    public bool IsSignalSectionExpanded
    {
        get => _isSignalSectionExpanded;
        set
        {
            if (SetProperty(ref _isSignalSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(SignalSectionToggleGlyph));
            }
        }
    }

    public bool IsPeakFilterSectionExpanded
    {
        get => _isPeakFilterSectionExpanded;
        set
        {
            if (SetProperty(ref _isPeakFilterSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(PeakFilterSectionToggleGlyph));
            }
        }
    }

    public bool IsBaseFiltersSectionExpanded
    {
        get => _isBaseFiltersSectionExpanded;
        set
        {
            if (SetProperty(ref _isBaseFiltersSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(BaseFiltersSectionToggleGlyph));
            }
        }
    }

    public bool IsAdjustmentSectionExpanded
    {
        get => _isAdjustmentSectionExpanded;
        set
        {
            if (SetProperty(ref _isAdjustmentSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(AdjustmentSectionToggleGlyph));
            }
        }
    }

    public bool IsKalmanSectionExpanded
    {
        get => _isKalmanSectionExpanded;
        set
        {
            if (SetProperty(ref _isKalmanSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(KalmanSectionToggleGlyph));
            }
        }
    }

    public bool IsStatisticsSectionExpanded
    {
        get => _isStatisticsSectionExpanded;
        set
        {
            if (SetProperty(ref _isStatisticsSectionExpanded, value))
            {
                RaisePropertyChanged(nameof(StatisticsSectionToggleGlyph));
            }
        }
    }

    public string SignalSectionTitle => "Signal";

    public string SignalSectionToggleGlyph => IsSignalSectionExpanded ? "▼" : "▶";

    public string PeakFilterSectionTitle => "Peak Filter";

    public string PeakFilterSectionToggleGlyph => IsPeakFilterSectionExpanded ? "▼" : "▶";

    public string BaseFiltersSectionTitle => "Base Filters";

    public string BaseFiltersSectionToggleGlyph => IsBaseFiltersSectionExpanded ? "▼" : "▶";

    public string AdjustmentSectionTitle => "Adjustment";

    public string AdjustmentSectionToggleGlyph => IsAdjustmentSectionExpanded ? "▼" : "▶";

    public string KalmanSectionTitle => "Kalman Filter";

    public string KalmanSectionToggleGlyph => IsKalmanSectionExpanded ? "▼" : "▶";

    public string StatisticsSectionTitle => "Statistics";

    public string StatisticsSectionToggleGlyph => IsStatisticsSectionExpanded ? "▼" : "▶";

    public bool ShowAdjustmentSettings => AdjustmentEnabled;

    public bool ShowAffineAdjustmentSettings => AdjustmentEnabled;

    public bool ShowInverseMappingSetting => AdjustmentEnabled && IsNoAdjustmentMappingSelected;

    public bool ShowSplineAdjustmentSettings => AdjustmentEnabled && IsSplineAdjustmentModeSelected;

    public bool ShowAdjustmentCurveEditor => ShowSplineAdjustmentSettings;

    public bool ShowStatisticsSettings => StatisticsEnabled;

    public bool ShowPeakFilterSettings => PeakFilterEnabled;

    public bool ShowDynamicFilterSettings => DynamicFilterEnabled;

    public bool IsNoAdjustmentMappingSelected => string.Equals(SelectedAdjustmentMode, ExtendedSignalAdjustmentMode.None.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsSplineAdjustmentModeSelected => string.Equals(SelectedAdjustmentMode, ExtendedSignalAdjustmentMode.Spline.ToString(), StringComparison.OrdinalIgnoreCase);

    public string NameToolTip => "Display name of the enhanced signal. This name is also used in the published registry path. Change it when the function of the signal should be clearer or the path naming should change.";

    public string EnabledToolTip => "Enables or disables the enhanced signal definition. Disable it when you want to keep the configuration without publishing a live runtime.";

    public string SourceToolTip => "Registry path that provides the raw input value. When child forwarding is enabled, write operations on forwarded child paths are mapped relative to this source path.";

    public string IsWritableToolTip => "Enables user-facing write access for the published enhanced signal. Disable it when the signal should remain read-only in generic editors and interactions.";

    public string WritePathToolTip => "Optional target path for writes. Leave it empty to write to the configured target directly. When the target exposes a write property, writes update that property instead of the displayed read value.";

    public string WriteModeToolTip => "Writes target the configured path directly. When the target exposes a write property, writes update that property instead of the displayed read value.";

    public string ForwardChildWritesToSourceToolTip => "Forwards non-signal-owned child writes under the enhanced signal to matching child paths below the source path. Internal enhanced-signal runtime nodes such as Raw, Read, State, Alert, Config, Adjustment, and Kalman stay local.";

    public string UnitToolTip => "Engineering unit shown with the published signal. Change it when the enhanced value uses another physical unit or display label.";

    public string FormatToolTip => "Optional numeric display format for UI rendering. Change it when the published value should be shown with a specific precision or format string.";

    public string FilterModeToolTip => "Selects the default smoothing mode. RAW publishes the accepted raw value, AVG uses equal weights, WMA uses linear weights, EMA uses exponential tracking, and EMAWMA applies EMA first and WMA afterwards. Change it when you need a different smoothing characteristic.";

    public string PublishedPathToolTip => "Preview of the host-backed registry path that will be published for this enhanced signal. It changes automatically with the folder name and signal name.";

    public string ScanIntervalToolTip => "Base timer interval for the collection stage. Unit: milliseconds. Every timer tick reads, adjusts, peak-validates, and evaluates dynamic behavior. Reduce it for finer timing, increase it to reduce runtime overhead.";

    public string RecordIntervalToolTip => "Decimation factor for the stage-2 filter buffer. Unit: timer ticks. Every n-th accepted timer sample is written into the main filter buffer. Increase it to reduce buffer traffic, keep it at 1 when every scan should reach the filter buffer.";

    public string FilterTimeToolTip => "Primary smoothing horizon for stage 2. Unit: milliseconds. The effective raw window is derived from this value. Increase it for stronger smoothing, reduce it for faster tracking.";

    public string FillMissingWithLastValueToolTip => "Automatically pads missing stage-2 buffer samples with the last accepted value. Keep it enabled when the filter should start smoothly and preserve its effective time window even before enough real samples have been recorded.";

    public string KalmanEnabledToolTip => "Activates the Kalman runtime path as an additive alternative to the classic stage-2 filters. The existing filter modes remain unchanged and are only bypassed while Kalman is enabled for this enhanced signal.";

    public string KalmanMeasurementNoiseRToolTip => "Measurement noise variance R for the Kalman filter. Increase it when the raw measurement is noisy so the estimate trusts the measurement less.";

    public string KalmanProcessNoiseQToolTip => "Process noise variance Q for the Kalman filter. Increase it when the real signal is expected to move faster so the estimate reacts more quickly.";

    public string KalmanInitialErrorCovariancePToolTip => "Initial estimation uncertainty P for the Kalman state. Higher values make the filter adapt more quickly after activation or reset.";

    public string KalmanTeachWindowToolTip => "Teach duration in milliseconds for estimating the measurement noise R from a stable signal section.";

    public string KalmanTeachPauseOnDynamicToolTip => "Pauses or ignores teach sampling while the dynamic detector reports movement. Keep it enabled so ramps are not misinterpreted as noise.";

    public string KalmanTeachQFactorToolTip => "Derivation factor for Q after teach completion. The runtime sets Q to learned R multiplied by this factor in the first Kalman attempt.";

    public string KalmanTeachAutoApplyToolTip => "Automatically writes the taught Kalman values back into the stored enhanced-signal definition once the teach session finishes successfully.";

    public string KalmanDynamicQEnabledToolTip => "Enables a dedicated adaptive Kalman Q mode. The Kalman path uses its own movement analysis and does not depend on the classic dynamic filter of the normal stage-2 filters.";

    public string KalmanDynamicQMinToolTip => "Adaptive Q lower bound and rest value. When no relevant dynamics are detected, the runtime uses this Q value.";

    public string KalmanDynamicQMaxToolTip => "Adaptive Q upper bound for strong dynamics. The runtime approaches this value when the signal changes by about 100 percent over the configured detection window.";

    public string KalmanDynamicQHoldToolTip => "Hold time in milliseconds for softly keeping adaptive Q elevated after the lower dynamic threshold is left. This reduces hard on-off behavior around the threshold.";

    public string KalmanDynamicDetectionWindowToolTip => "Kalman-exclusive analysis window for adaptive Q. Unit: milliseconds. The runtime fits a regression line over accepted raw samples in this window and derives the Kalman movement angle from it.";

    public string KalmanDynamicAngleThresholdToolTip => "Kalman-exclusive lower movement threshold in degrees. A flat or purely noisy signal should stay near 0 degrees after trend-quality weighting. The adaptive Q range starts rising above this threshold.";

    public string KalmanDynamicAngleMaxToolTip => "Kalman-exclusive upper angle bound in degrees for normalized adaptive Q intensity. When the smoothed Kalman movement angle approaches this value, the adaptive Q approaches its configured maximum.";

    public string KalmanDynamicReferenceFloorToolTip => "Lower reference floor for the Kalman-only angle normalization. Unit: source value units. Increase it when values near zero still produce too much angle from noise; reduce it when small real movements should count more strongly.";

    public string KalmanDynamicNormalizationModeToolTip => "Kalman dynamic normalization is fixed to Hybrid Reference Floor. The editor keeps only the parameters that are still effective for that path.";

    public string KalmanDynamicResidualWeightToolTip => "Blend weight from 0 to 1 for Adaptive Residual Blend. 0 keeps the reference-floor behavior, 1 uses only the residual-based normalization. This value is ignored by the other normalization modes.";

    public bool ShowKalmanMainSettings => KalmanEnabled;

    public bool ShowStaticKalmanProcessNoiseQ => KalmanEnabled && !KalmanDynamicQEnabled;

    public bool ShowAdaptiveKalmanSettings => KalmanEnabled && KalmanDynamicQEnabled;

    public bool ShowKalmanReferenceFloor => ShowAdaptiveKalmanSettings;

    public bool ShowKalmanNormalizationMode => false;

    public bool ShowKalmanResidualWeight => false;

    public bool IsPureResidualModeSelected => false;

    public bool IsAdaptiveResidualBlendModeSelected => false;

    public string KalmanDynamicQUseExistingDynamicToolTip => "Uses the existing enhanced-signal dynamic analysis as the lower dynamic boundary for the adaptive Q range.";

    public string AdjustmentEnabledToolTip => "Enables stage-1 adjustment before peak handling and smoothing. Enable it when the source value needs calibration or engineering-unit conversion.";

    public string AdjustmentModeToolTip => "Selects the nonlinear mapping stage for adjustment: none or spline. Offset and gain remain available as the affine trim after this mapping stage.";

    public string OffsetToolTip => "Additive trim applied after the optional mapping stage. Formula: adjusted = mapped * gain + offset. Change it when the signal has a constant bias after mapping.";

    public string GainToolTip => "Multiplicative trim applied after the optional mapping stage. Formula: adjusted = mapped * gain + offset. Change it when the signal scale is wrong after mapping.";

    public string InverseMappingToolTip => "Allows forwarded set values to map adjusted engineering units back into source values. Spline mode is inverse-mapped before forwarding as well.";

    public string SplinePointsToolTip => "Piecewise interpolation points for stage-1 adjustment in the form input:output. Change them when calibration is best described by support points instead of a formula.";

    public string SplineInterpolationModeToolTip => "Selects how support points are interpolated in spline mode. Linear keeps straight segments, Catmull-Rom creates a smooth curve through the same points.";

    public string AdjustmentCurveSummary
        => IsSplineAdjustmentModeSelected
            ? $"{_cachedSplinePointCount} spline point(s) configured. Mode={SelectedSplineInterpolationMode}. Preview uses the current gain and offset."
            : "No curve editor required while mapping mode is None.";

    public string StatisticsEnabledToolTip => "Publishes additional statistics for the enhanced signal. Enable it when downstream consumers need min, max, average, standard deviation, or integral values.";

    public string PublishMinToolTip => "Publishes the minimum over the retained sample window. Enable it when lower excursions are relevant for monitoring or diagnostics.";

    public string PublishMaxToolTip => "Publishes the maximum over the retained sample window. Enable it when peaks should remain visible as a diagnostic value.";

    public string PublishAverageToolTip => "Publishes the arithmetic mean over the retained sample window. Enable it when consumers need a simple aggregate beside the main filtered output.";

    public string PublishStdDevToolTip => "Publishes the standard deviation over the retained sample window. Enable it when noise level or signal stability should be monitored.";

    public string PublishIntegralToolTip => "Publishes the running integral over the retained sample window. Enable it when accumulated quantity matters more than the instantaneous value.";

    public string StdDevWindowToolTip => "Time window in milliseconds used for the standard deviation calculation. Only samples inside this window contribute to the published StdDev value.";

    public string IntegralDivisorToolTip => "Divisor in milliseconds applied to the time-based integral. Use 60000 for values such as l/min when the integral should be published in l.";

    public string PeakFilterEnabledToolTip => "Enables spike suppression in stage 1 before smoothing. Use it when short spikes should be rejected before they influence the smoother.";

    public string PeakThresholdToolTip => "Difference threshold for peak detection. Unit: source value units. If the new preprocessed raw value differs more than this from the last accepted raw value, the spike filter can hold the old value. Increase it to allow larger jumps, reduce it for stricter spike suppression.";

    public string PeakMaxLengthToolTip => "Maximum duration for spike suppression. Unit: milliseconds. Shorter than this, a detected spike is held back; longer than this, the new value is accepted. Change it to match the expected spike duration.";

    public string DynamicFilterEnabledToolTip => "Enables classic dynamic mode switching for the normal stage-2 filters. Use it when Average, EMA, WMA, or EMAWMA should temporarily switch to a shorter filter time during ramps or fast transitions.";

    public string DynamicDetectionWindowToolTip => "Analysis window for the classic dynamic detector. Unit: milliseconds. A regression slope is calculated over the accepted raw samples in this window. Increase it for more robust detection, reduce it for faster reaction.";

    public string DynamicSlopeThresholdToolTip => "Absolute minimum threshold for the classic regression slope. Unit: value units per second. Increase it to ignore smaller absolute changes in the normal dynamic filter switching.";

    public string DynamicRelativeSlopeThresholdToolTip => "Relative minimum slope threshold for the classic dynamic detector. Unit: percent of the reference signal level per second. Increase it when the absolute slope threshold alone is too sensitive across different signal magnitudes.";

    public string DynamicFilterTimeToolTip => "Alternative stage-2 filter horizon used while dynamic mode is active. Unit: milliseconds. Usually this is shorter than the normal filter time so the output follows ramps faster.";

    public string DynamicHoldTimeToolTip => "Hold time after the classic regression slope falls below the threshold. Unit: milliseconds. This avoids rapid toggling between dynamic and normal mode during borderline movement.";

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

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public bool IsWritable
    {
        get => _isWritable;
        set => SetProperty(ref _isWritable, value);
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value ?? string.Empty);
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

    public string SelectedFilterMode
    {
        get => _selectedFilterMode;
        set => SetProperty(ref _selectedFilterMode, value ?? ExtendedSignalFilterMode.Raw.ToString());
    }

    public string ScanIntervalMsText
    {
        get => _scanIntervalMsText;
        set => SetProperty(ref _scanIntervalMsText, value ?? string.Empty);
    }

    public string RecordIntervalText
    {
        get => _recordIntervalText;
        set => SetProperty(ref _recordIntervalText, value ?? string.Empty);
    }

    public string FilterTimeMsText
    {
        get => _filterTimeMsText;
        set => SetProperty(ref _filterTimeMsText, value ?? string.Empty);
    }

    public bool FillMissingWithLastValue
    {
        get => _fillMissingWithLastValue;
        set => SetProperty(ref _fillMissingWithLastValue, value);
    }

    public bool KalmanEnabled
    {
        get => _kalmanEnabled;
        set
        {
            if (SetProperty(ref _kalmanEnabled, value))
            {
                IsKalmanSectionExpanded = value;
                RaiseKalmanVisibilityProperties();
                RaisePropertyChanged(nameof(KalmanSectionTitle));
            }
        }
    }

    public string KalmanMeasurementNoiseRText
    {
        get => _kalmanMeasurementNoiseRText;
        set => SetProperty(ref _kalmanMeasurementNoiseRText, value ?? string.Empty);
    }

    public string KalmanProcessNoiseQText
    {
        get => _kalmanProcessNoiseQText;
        set => SetProperty(ref _kalmanProcessNoiseQText, value ?? string.Empty);
    }

    public string KalmanInitialErrorCovariancePText
    {
        get => _kalmanInitialErrorCovariancePText;
        set => SetProperty(ref _kalmanInitialErrorCovariancePText, value ?? string.Empty);
    }

    public string KalmanTeachWindowMsText
    {
        get => _kalmanTeachWindowMsText;
        set => SetProperty(ref _kalmanTeachWindowMsText, value ?? string.Empty);
    }

    public bool KalmanTeachPauseOnDynamic
    {
        get => _kalmanTeachPauseOnDynamic;
        set => SetProperty(ref _kalmanTeachPauseOnDynamic, value);
    }

    public string KalmanTeachQFactorText
    {
        get => _kalmanTeachQFactorText;
        set => SetProperty(ref _kalmanTeachQFactorText, value ?? string.Empty);
    }

    public bool KalmanTeachAutoApply
    {
        get => _kalmanTeachAutoApply;
        set => SetProperty(ref _kalmanTeachAutoApply, value);
    }

    public bool KalmanDynamicQEnabled
    {
        get => _kalmanDynamicQEnabled;
        set
        {
            if (SetProperty(ref _kalmanDynamicQEnabled, value))
            {
                RaiseKalmanVisibilityProperties();
            }
        }
    }

    public string KalmanDynamicQMinText
    {
        get => _kalmanDynamicQMinText;
        set => SetProperty(ref _kalmanDynamicQMinText, value ?? string.Empty);
    }

    public string KalmanDynamicQMaxText
    {
        get => _kalmanDynamicQMaxText;
        set => SetProperty(ref _kalmanDynamicQMaxText, value ?? string.Empty);
    }

    public string KalmanDynamicQHoldMsText
    {
        get => _kalmanDynamicQHoldMsText;
        set => SetProperty(ref _kalmanDynamicQHoldMsText, value ?? string.Empty);
    }

    public string KalmanDynamicDetectionWindowMsText
    {
        get => _kalmanDynamicDetectionWindowMsText;
        set => SetProperty(ref _kalmanDynamicDetectionWindowMsText, value ?? string.Empty);
    }

    public string KalmanDynamicAngleThresholdText
    {
        get => _kalmanDynamicAngleThresholdText;
        set => SetProperty(ref _kalmanDynamicAngleThresholdText, value ?? string.Empty);
    }

    public string KalmanDynamicAngleMaxText
    {
        get => _kalmanDynamicAngleMaxText;
        set => SetProperty(ref _kalmanDynamicAngleMaxText, value ?? string.Empty);
    }

    public string KalmanDynamicReferenceFloorText
    {
        get => _kalmanDynamicReferenceFloorText;
        set => SetProperty(ref _kalmanDynamicReferenceFloorText, value ?? string.Empty);
    }

    public string SelectedKalmanDynamicNormalizationMode
    {
        get => _selectedKalmanDynamicNormalizationMode;
        set
        {
            if (SetProperty(ref _selectedKalmanDynamicNormalizationMode, KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString()))
            {
                RaiseKalmanVisibilityProperties();
            }
        }
    }

    public string KalmanDynamicResidualWeightText
    {
        get => _kalmanDynamicResidualWeightText;
        set => SetProperty(ref _kalmanDynamicResidualWeightText, value ?? string.Empty);
    }

    public bool KalmanDynamicQUseExistingDynamic
    {
        get => _kalmanDynamicQUseExistingDynamic;
        set => SetProperty(ref _kalmanDynamicQUseExistingDynamic, value);
    }

    public bool AdjustmentEnabled
    {
        get => _adjustmentEnabled;
        set
        {
            if (SetProperty(ref _adjustmentEnabled, value))
            {
                IsAdjustmentSectionExpanded = value;
                RaiseOptionalVisibilityProperties();
                RaisePropertyChanged(nameof(AdjustmentSectionTitle));
            }
        }
    }

    public string SelectedAdjustmentMode
    {
        get => _selectedAdjustmentMode;
        set
        {
            if (SetProperty(ref _selectedAdjustmentMode, value ?? ExtendedSignalAdjustmentMode.None.ToString()))
            {
                RaiseOptionalVisibilityProperties();
            }
        }
    }

    public string AdjustmentOffsetText
    {
        get => _adjustmentOffsetText;
        set => SetProperty(ref _adjustmentOffsetText, value ?? string.Empty);
    }

    public string AdjustmentGainText
    {
        get => _adjustmentGainText;
        set => SetProperty(ref _adjustmentGainText, value ?? string.Empty);
    }

    public string SelectedSplineInterpolationMode
    {
        get => _selectedSplineInterpolationMode;
        set
        {
            if (SetProperty(ref _selectedSplineInterpolationMode, value ?? ExtendedSignalSplineInterpolationMode.Linear.ToString()))
            {
                NotifyCurveSummaryChanged();
            }
        }
    }

    public bool SupportsInverseMapping
    {
        get => _supportsInverseMapping;
        set => SetProperty(ref _supportsInverseMapping, value);
    }

    public bool ForwardChildWritesToSource
    {
        get => _forwardChildWritesToSource;
        set => SetProperty(ref _forwardChildWritesToSource, value);
    }

    public string SplinePointsText
    {
        get => _splinePointsText;
        set
        {
            if (SetProperty(ref _splinePointsText, value ?? string.Empty))
            {
                RefreshSplinePointCount();
                NotifyCurveSummaryChanged();
            }
        }
    }

    public bool StatisticsEnabled
    {
        get => _statisticsEnabled;
        set
        {
            if (SetProperty(ref _statisticsEnabled, value))
            {
                IsStatisticsSectionExpanded = value;
                RaiseOptionalVisibilityProperties();
                RaisePropertyChanged(nameof(StatisticsSectionTitle));
            }
        }
    }

    public bool PublishMin
    {
        get => _publishMin;
        set => SetProperty(ref _publishMin, value);
    }

    public bool PublishMax
    {
        get => _publishMax;
        set => SetProperty(ref _publishMax, value);
    }

    public bool PublishAverage
    {
        get => _publishAverage;
        set => SetProperty(ref _publishAverage, value);
    }

    public bool PublishStdDev
    {
        get => _publishStdDev;
        set => SetProperty(ref _publishStdDev, value);
    }

    public bool PublishIntegral
    {
        get => _publishIntegral;
        set => SetProperty(ref _publishIntegral, value);
    }

    public string StdDevWindowMsText
    {
        get => _stdDevWindowMsText;
        set => SetProperty(ref _stdDevWindowMsText, value ?? string.Empty);
    }

    public string IntegralDivisorMsText
    {
        get => _integralDivisorMsText;
        set => SetProperty(ref _integralDivisorMsText, value ?? string.Empty);
    }

    public bool PeakFilterEnabled
    {
        get => _peakFilterEnabled;
        set
        {
            if (SetProperty(ref _peakFilterEnabled, value))
            {
                IsPeakFilterSectionExpanded = value;
                RaiseOptionalVisibilityProperties();
            }
        }
    }

    public string PeakThresholdText
    {
        get => _peakThresholdText;
        set => SetProperty(ref _peakThresholdText, value ?? string.Empty);
    }

    public string PeakMaxLengthMsText
    {
        get => _peakMaxLengthMsText;
        set => SetProperty(ref _peakMaxLengthMsText, value ?? string.Empty);
    }

    public bool DynamicFilterEnabled
    {
        get => _dynamicFilterEnabled;
        set
        {
            if (SetProperty(ref _dynamicFilterEnabled, value))
            {
                IsBaseFiltersSectionExpanded = true;
                RaiseOptionalVisibilityProperties();
            }
        }
    }

    public string DynamicDetectionWindowMsText
    {
        get => _dynamicDetectionWindowMsText;
        set => SetProperty(ref _dynamicDetectionWindowMsText, value ?? string.Empty);
    }

    public string DynamicSlopeThresholdText
    {
        get => _dynamicSlopeThresholdText;
        set => SetProperty(ref _dynamicSlopeThresholdText, value ?? string.Empty);
    }

    public string DynamicRelativeSlopeThresholdPercentText
    {
        get => _dynamicRelativeSlopeThresholdPercentText;
        set => SetProperty(ref _dynamicRelativeSlopeThresholdPercentText, value ?? string.Empty);
    }

    public string DynamicFilterTimeMsText
    {
        get => _dynamicFilterTimeMsText;
        set => SetProperty(ref _dynamicFilterTimeMsText, value ?? string.Empty);
    }

    public string DynamicHoldTimeMsText
    {
        get => _dynamicHoldTimeMsText;
        set => SetProperty(ref _dynamicHoldTimeMsText, value ?? string.Empty);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value ?? string.Empty);
    }

    public string PreviewPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_ownerItem.FolderName) || string.IsNullOrWhiteSpace(Name))
            {
                return string.Empty;
            }

            var definition = new ExtendedSignalDefinition { Name = Name };
            return EnhancedSignalRuntime.BuildRegistryPath(_ownerItem.FolderName, definition);
        }
    }

    private void RaiseKalmanVisibilityProperties()
    {
        RaisePropertyChanged(nameof(ShowKalmanMainSettings));
        RaisePropertyChanged(nameof(ShowStaticKalmanProcessNoiseQ));
        RaisePropertyChanged(nameof(ShowAdaptiveKalmanSettings));
        RaisePropertyChanged(nameof(ShowKalmanReferenceFloor));
        RaisePropertyChanged(nameof(ShowKalmanNormalizationMode));
        RaisePropertyChanged(nameof(ShowKalmanResidualWeight));
        RaisePropertyChanged(nameof(IsPureResidualModeSelected));
        RaisePropertyChanged(nameof(IsAdaptiveResidualBlendModeSelected));
    }

    private void RaiseOptionalVisibilityProperties()
    {
        RaisePropertyChanged(nameof(ShowAdjustmentSettings));
        RaisePropertyChanged(nameof(ShowAffineAdjustmentSettings));
        RaisePropertyChanged(nameof(ShowInverseMappingSetting));
        RaisePropertyChanged(nameof(ShowSplineAdjustmentSettings));
        RaisePropertyChanged(nameof(ShowAdjustmentCurveEditor));
        NotifyCurveSummaryChanged();
        RaisePropertyChanged(nameof(ShowStatisticsSettings));
        RaisePropertyChanged(nameof(ShowPeakFilterSettings));
        RaisePropertyChanged(nameof(ShowDynamicFilterSettings));
        RaisePropertyChanged(nameof(IsNoAdjustmentMappingSelected));
        RaisePropertyChanged(nameof(IsSplineAdjustmentModeSelected));
    }

    public bool TryBuildDefinition(out ExtendedSignalDefinition? definition, out string errorMessage)
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

        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            errorMessage = "Source is required.";
            return false;
        }

        if (!Enum.TryParse<SignalWriteMode>(SelectedWriteMode, true, out var writeMode))
        {
            errorMessage = "Write mode is invalid.";
            return false;
        }

        if (!Enum.TryParse<ExtendedSignalFilterMode>(SelectedFilterMode, true, out var filterMode))
        {
            errorMessage = "Filter mode is invalid.";
            return false;
        }

        if (!Enum.TryParse<ExtendedSignalAdjustmentMode>(SelectedAdjustmentMode, true, out var adjustmentMode))
        {
            errorMessage = "Adjustment mode is invalid.";
            return false;
        }

        if (!int.TryParse(ScanIntervalMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scanIntervalMs) || scanIntervalMs <= 0)
        {
            errorMessage = "Scan interval must be a positive integer.";
            return false;
        }

        if (!int.TryParse(RecordIntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordInterval) || recordInterval <= 0)
        {
            errorMessage = "Record interval must be a positive integer.";
            return false;
        }

        if (!int.TryParse(FilterTimeMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var filterTimeMs) || filterTimeMs < 0)
        {
            errorMessage = "Filter time must be a non-negative integer.";
            return false;
        }

        if (!double.TryParse(KalmanMeasurementNoiseRText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanMeasurementNoiseR) || kalmanMeasurementNoiseR <= 0)
        {
            errorMessage = "Kalman measurement noise R must be a positive number.";
            return false;
        }

        if (!double.TryParse(KalmanProcessNoiseQText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanProcessNoiseQ) || kalmanProcessNoiseQ <= 0)
        {
            errorMessage = "Kalman process noise Q must be a positive number.";
            return false;
        }

        if (!double.TryParse(KalmanInitialErrorCovariancePText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanInitialErrorCovarianceP) || kalmanInitialErrorCovarianceP <= 0)
        {
            errorMessage = "Kalman initial error covariance P must be a positive number.";
            return false;
        }

        if (!int.TryParse(KalmanTeachWindowMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kalmanTeachWindowMs) || kalmanTeachWindowMs <= 0)
        {
            errorMessage = "Kalman teach window must be a positive integer.";
            return false;
        }

        if (!double.TryParse(KalmanTeachQFactorText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanTeachQFactor) || kalmanTeachQFactor < 0)
        {
            errorMessage = "Kalman teach Q factor must be a non-negative number.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicQMinText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicQMin) || kalmanDynamicQMin <= 0)
        {
            errorMessage = "Kalman dynamic Q min must be a positive number.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicQMaxText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicQMax) || kalmanDynamicQMax <= 0)
        {
            errorMessage = "Kalman dynamic Q max must be a positive number.";
            return false;
        }

        if (kalmanDynamicQMax < kalmanDynamicQMin)
        {
            errorMessage = "Kalman dynamic Q max must be greater than or equal to Kalman dynamic Q min.";
            return false;
        }

        if (!int.TryParse(KalmanDynamicQHoldMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kalmanDynamicQHoldMs) || kalmanDynamicQHoldMs < 0)
        {
            errorMessage = "Kalman dynamic Q hold must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(KalmanDynamicDetectionWindowMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kalmanDynamicDetectionWindowMs) || kalmanDynamicDetectionWindowMs < 100)
        {
            errorMessage = "Kalman dynamic buffer must be an integer of at least 100 ms.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicAngleThresholdText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicAngleThreshold) || kalmanDynamicAngleThreshold < 0 || kalmanDynamicAngleThreshold >= 90)
        {
            errorMessage = "Kalman dynamic angle threshold must be a number from 0 to below 90 degrees.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicAngleMaxText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicAngleMax) || kalmanDynamicAngleMax <= 0 || kalmanDynamicAngleMax >= 90)
        {
            errorMessage = "Kalman dynamic max angle must be a number above 0 and below 90 degrees.";
            return false;
        }

        if (kalmanDynamicAngleMax < kalmanDynamicAngleThreshold)
        {
            errorMessage = "Kalman dynamic max angle must be greater than or equal to the Kalman dynamic angle threshold.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicReferenceFloorText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicReferenceFloor) || kalmanDynamicReferenceFloor < 0)
        {
            errorMessage = "Kalman dynamic reference floor must be a non-negative number.";
            return false;
        }

        if (!double.TryParse(KalmanDynamicResidualWeightText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var kalmanDynamicResidualWeight) || kalmanDynamicResidualWeight < 0 || kalmanDynamicResidualWeight > 1)
        {
            errorMessage = "Kalman dynamic residual weight must be a number from 0 to 1.";
            return false;
        }

        if (!double.TryParse(AdjustmentOffsetText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var offset))
        {
            errorMessage = "Adjustment offset must be numeric.";
            return false;
        }

        if (!double.TryParse(AdjustmentGainText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var gain))
        {
            errorMessage = "Adjustment gain must be numeric.";
            return false;
        }

        if (!double.TryParse(PeakThresholdText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var peakThreshold))
        {
            errorMessage = "Peak threshold must be numeric.";
            return false;
        }

        if (!int.TryParse(PeakMaxLengthMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var peakMaxLengthMs) || peakMaxLengthMs < 0)
        {
            errorMessage = "Peak max length must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(DynamicDetectionWindowMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dynamicDetectionWindowMs) || dynamicDetectionWindowMs < 0)
        {
            errorMessage = "Dynamic detection window must be a non-negative integer.";
            return false;
        }

        if (!double.TryParse(DynamicSlopeThresholdText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dynamicSlopeThreshold))
        {
            errorMessage = "Dynamic slope threshold must be numeric.";
            return false;
        }

        if (!double.TryParse(DynamicRelativeSlopeThresholdPercentText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dynamicRelativeSlopeThresholdPercent) || dynamicRelativeSlopeThresholdPercent < 0)
        {
            errorMessage = "Dynamic relative slope threshold must be a non-negative number.";
            return false;
        }

        if (!int.TryParse(DynamicFilterTimeMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dynamicFilterTimeMs) || dynamicFilterTimeMs < 0)
        {
            errorMessage = "Dynamic filter time must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(DynamicHoldTimeMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dynamicHoldTimeMs) || dynamicHoldTimeMs < 0)
        {
            errorMessage = "Dynamic hold time must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(StdDevWindowMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stdDevWindowMs) || stdDevWindowMs <= 0)
        {
            errorMessage = "StdDev window must be a positive integer.";
            return false;
        }

        if (!double.TryParse(IntegralDivisorMsText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var integralDivisorMs) || integralDivisorMs <= 0)
        {
            errorMessage = "Integral divisor must be a positive number.";
            return false;
        }

        if (!TryParseSplinePoints(SplinePointsText, out var splinePoints, out errorMessage))
        {
            return false;
        }

        definition = new ExtendedSignalDefinition
        {
            Name = normalizedName,
            Enabled = Enabled,
            IsWritable = IsWritable,
            SourcePath = SourcePath.Trim(),
            WritePath = WritePath.Trim(),
            WriteMode = writeMode,
            ForwardChildWritesToSource = ForwardChildWritesToSource,
            Unit = Unit.Trim(),
            Format = Format.Trim(),
            FilterMode = filterMode,
            ScanIntervalMs = scanIntervalMs,
            RecordInterval = recordInterval,
            FilterTimeMs = filterTimeMs,
            FillMissingWithLastValue = FillMissingWithLastValue,
            KalmanEnabled = KalmanEnabled,
            KalmanMeasurementNoiseR = kalmanMeasurementNoiseR,
            KalmanProcessNoiseQ = kalmanProcessNoiseQ,
            KalmanInitialErrorCovarianceP = kalmanInitialErrorCovarianceP,
            KalmanTeachWindowMs = kalmanTeachWindowMs,
            KalmanTeachPauseOnDynamic = KalmanTeachPauseOnDynamic,
            KalmanTeachQFactor = kalmanTeachQFactor,
            KalmanTeachAutoApply = KalmanTeachAutoApply,
            KalmanDynamicQEnabled = KalmanDynamicQEnabled,
            KalmanDynamicQMin = kalmanDynamicQMin,
            KalmanDynamicQMax = kalmanDynamicQMax,
            KalmanDynamicQHoldMs = kalmanDynamicQHoldMs,
            KalmanDynamicDetectionWindowMs = kalmanDynamicDetectionWindowMs,
            KalmanDynamicAngleThresholdDeg = kalmanDynamicAngleThreshold,
            KalmanDynamicAngleMaxDeg = kalmanDynamicAngleMax,
            KalmanDynamicReferenceFloor = kalmanDynamicReferenceFloor,
            KalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor,
            KalmanDynamicResidualWeight = kalmanDynamicResidualWeight,
            KalmanDynamicQUseExistingDynamic = KalmanDynamicQUseExistingDynamic,
            Adjustment = new ExtendedSignalAdjustmentDefinition
            {
                Enabled = AdjustmentEnabled,
                MappingMode = adjustmentMode,
                Offset = offset,
                Gain = gain,
                SplinePoints = splinePoints,
                SplineInterpolationMode = ParseSplineInterpolationMode(SelectedSplineInterpolationMode),
                SupportsInverseMapping = SupportsInverseMapping
            },
            PeakFilter = new ExtendedSignalPeakFilterDefinition
            {
                Enabled = PeakFilterEnabled,
                Threshold = peakThreshold,
                MaxLengthMs = peakMaxLengthMs
            },
            DynamicFilter = new ExtendedSignalDynamicFilterDefinition
            {
                Enabled = DynamicFilterEnabled,
                DetectionWindowMs = dynamicDetectionWindowMs,
                SlopeThreshold = dynamicSlopeThreshold,
                RelativeSlopeThresholdPercent = dynamicRelativeSlopeThresholdPercent,
                DynamicFilterTimeMs = dynamicFilterTimeMs,
                HoldTimeMs = dynamicHoldTimeMs
            },
            Statistics = new ExtendedSignalStatisticsDefinition
            {
                Enabled = StatisticsEnabled,
                PublishMin = PublishMin,
                PublishMax = PublishMax,
                PublishAverage = PublishAverage,
                PublishStdDev = PublishStdDev,
                PublishIntegral = PublishIntegral,
                StdDevWindowMs = stdDevWindowMs,
                IntegralDivisorMs = integralDivisorMs
            }
        };

        return true;
    }

    private string GenerateNextSignalName()
    {
        var existingNames = ExtendedSignalDefinitionCodec.ParseDefinitions(_ownerItem.EnhancedSignalDefinitions)
            .Select(static definition => definition.Name);
        return TargetPathHelper.GenerateIndexedPathIdentityName("enhanced_signal", existingNames, "enhanced_signal");
    }

    public bool TryCreateCurveEditorState(out AdjustmentCurveEditorState? state, out string errorMessage)
    {
        state = null;
        errorMessage = string.Empty;

        if (!Enum.TryParse<ExtendedSignalAdjustmentMode>(SelectedAdjustmentMode, true, out var mappingMode)
            || mappingMode != ExtendedSignalAdjustmentMode.Spline)
        {
            errorMessage = "Curve editor is only available for spline mapping.";
            return false;
        }

        if (!double.TryParse(AdjustmentOffsetText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var offset))
        {
            errorMessage = "Adjustment offset must be numeric.";
            return false;
        }

        if (!double.TryParse(AdjustmentGainText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var gain))
        {
            errorMessage = "Adjustment gain must be numeric.";
            return false;
        }

        if (!TryParseSplinePoints(SplinePointsText, out var splinePoints, out errorMessage))
        {
            return false;
        }

        state = new AdjustmentCurveEditorState(offset, gain, splinePoints, ParseSplineInterpolationMode(SelectedSplineInterpolationMode));
        return true;
    }

    public void ApplyCurveEditorState(AdjustmentCurveEditorState state)
    {
        _suspendCurveSummaryUpdates = true;
        try
        {
            SelectedAdjustmentMode = ExtendedSignalAdjustmentMode.Spline.ToString();
            SplinePointsText = FormatSplinePoints(state.SplinePoints);
            SelectedSplineInterpolationMode = state.SplineInterpolationMode.ToString();
        }
        finally
        {
            _suspendCurveSummaryUpdates = false;
        }

        RefreshCurveSummaryCaches();
        RaisePropertyChanged(nameof(AdjustmentCurveSummary));
    }

    private void NotifyCurveSummaryChanged()
    {
        if (_suspendCurveSummaryUpdates)
        {
            return;
        }

        RaisePropertyChanged(nameof(AdjustmentCurveSummary));
    }

    private void RefreshCurveSummaryCaches()
    {
        RefreshSplinePointCount();
    }

    private void RefreshSplinePointCount()
    {
        _ = TryParseSplinePoints(SplinePointsText, out var points, out _);
        _cachedSplinePointCount = points.Count;
    }

    private static bool TryParseSplinePoints(string text, out List<ExtendedSignalSplinePoint> points, out string errorMessage)
    {
        points = [];
        errorMessage = string.Empty;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split([':', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var input)
                || !double.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var output))
            {
                errorMessage = $"Invalid spline point '{rawLine}'. Use input:output.";
                return false;
            }

            points.Add(new ExtendedSignalSplinePoint { Input = input, Output = output });
        }

        var duplicateInput = points
            .GroupBy(static point => point.Input)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateInput is not null)
        {
            errorMessage = $"Spline input '{duplicateInput.Key.ToString(CultureInfo.InvariantCulture)}' is duplicated. Each input value must be unique.";
            return false;
        }

        return true;
    }

    private static ExtendedSignalSplineInterpolationMode ParseSplineInterpolationMode(string? text)
    {
        return Enum.TryParse<ExtendedSignalSplineInterpolationMode>(text, true, out var mode)
            ? mode
            : ExtendedSignalSplineInterpolationMode.Linear;
    }

    private static string FormatSplinePoints(IEnumerable<ExtendedSignalSplinePoint> points)
    {
        return string.Join(Environment.NewLine, points.Select(point => $"{point.Input.ToString(CultureInfo.InvariantCulture)}:{point.Output.ToString(CultureInfo.InvariantCulture)}"));
    }
}