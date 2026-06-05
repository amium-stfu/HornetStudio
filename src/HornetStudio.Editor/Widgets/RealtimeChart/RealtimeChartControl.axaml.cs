using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.ViewModels;
using ScottPlot;
using ScottPlot.Avalonia;
using ChartSeriesConfiguration = HornetStudio.Editor.Widgets.RealtimeChartRuntimeManager.ChartSeriesConfiguration;

namespace HornetStudio.Editor.Widgets;

public partial class RealtimeChartControl : EditorTemplateWidget
{
    public static readonly StyledProperty<bool> PageIsActiveProperty =
        AvaloniaProperty.Register<RealtimeChartControl, bool>(nameof(PageIsActive), true);

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<RealtimeChartControl, bool>(nameof(IsPaused));

    private static readonly ScottPlot.Color[] SeriesColors =
    [
        Colors.DodgerBlue,
        Colors.Orange,
        Colors.LimeGreen,
        Colors.DeepPink,
        Colors.Gold,
        Colors.Cyan,
        Colors.Violet,
        Colors.Tomato
    ];

    private DispatcherTimer? _renderTimer;
    private FolderItemModel? _chartItem;
    private RealtimeChartRuntimeManager.RealtimeChartRuntimeState? _chartState;
    private AvaPlot? _avaPlot;
    private Grid? _plotHost;
    private Canvas? _crosshairOverlay;
    private Border? _crosshairVerticalLine;
    private Border? _crosshairHorizontalLine;
    private Border? _crosshairInfoBorder;
    private Border? _emptyStateBorder;
    private TextBlock? _crosshairInfoTextBlock;
    private TextBlock? _emptyStateTextBlock;
    private IYAxis? _yAxis2;
    private IYAxis? _yAxis3;
    private IYAxis? _yAxis4;
    private bool _hasConfiguredAxes;
    private bool _isAttachedToVisualTree;
    private readonly Dictionary<int, AxisScaleOverride> _axisOverrides = new();

    private MainWindowViewModel? ViewModel
        => this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel } ? viewModel : null;

    public bool PageIsActive
    {
        get => GetValue(PageIsActiveProperty);
        set => SetValue(PageIsActiveProperty, value);
    }

    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        private set => SetValue(IsPausedProperty, value);
    }

    public RealtimeChartControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private object? PlotSyncRoot => _avaPlot?.Plot.Sync;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        _avaPlot = this.FindControl<AvaPlot>("ChartPlot");
        _plotHost = this.FindControl<Grid>("PlotHost");
        _crosshairOverlay = this.FindControl<Canvas>("CrosshairOverlay");
        _crosshairVerticalLine = this.FindControl<Border>("CrosshairVerticalLine");
        _crosshairHorizontalLine = this.FindControl<Border>("CrosshairHorizontalLine");
        _crosshairInfoBorder = this.FindControl<Border>("CrosshairInfoBorder");
        _crosshairInfoTextBlock = this.FindControl<TextBlock>("CrosshairInfoTextBlock");
        _emptyStateBorder = this.FindControl<Border>("EmptyStateBorder");
        _emptyStateTextBlock = this.FindControl<TextBlock>("EmptyStateTextBlock");

        ConfigurePlot();
        HookChartItem(DataContext as FolderItemModel);
        UpdateRenderActivity();
        if (PageIsActive && IsVisible)
        {
            RequestSnapshotRefresh();
            RenderPlot();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        StopRenderTimer();
        HookChartItem(null);
        _avaPlot = null;
        _plotHost = null;
        _crosshairOverlay = null;
        _crosshairVerticalLine = null;
        _crosshairHorizontalLine = null;
        _crosshairInfoBorder = null;
        _crosshairInfoTextBlock = null;
        _emptyStateBorder = null;
        _emptyStateTextBlock = null;
        _yAxis2 = null;
        _yAxis3 = null;
        _yAxis4 = null;
        _hasConfiguredAxes = false;
        _chartState = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookChartItem(DataContext as FolderItemModel);
        if (PageIsActive && IsVisible)
        {
            RenderPlot();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PageIsActiveProperty || change.Property == IsVisibleProperty)
        {
            UpdateRenderActivity();
            if (PageIsActive && IsVisible)
            {
                RequestSnapshotRefresh();
                RenderPlot();
            }
            else
            {
                HideCrosshair();
            }
        }

        if (change.Property == IsPausedProperty)
        {
            UpdateInteractionState();
            UpdateStatusText();
            if (!IsPaused && PageIsActive && IsVisible)
            {
                RenderPlot();
            }
        }
    }

    private void HookChartItem(FolderItemModel? nextItem)
    {
        if (ReferenceEquals(_chartItem, nextItem))
        {
            if (_chartItem is not null)
            {
                RebindChartState(reason: "RefreshCurrentChartItem");
            }

            return;
        }

        if (_chartItem is not null)
        {
            _chartItem.PropertyChanged -= OnChartItemPropertyChanged;
        }

        _chartItem = nextItem;

        if (_chartItem is not null)
        {
            _chartItem.PropertyChanged += OnChartItemPropertyChanged;
        }

        RebindChartState(reason: "HookChartItem");

        UpdateStatusText();
    }

    private void RebindChartState(string reason)
    {
        var previousState = _chartState;
        var nextState = RealtimeChartRuntimeManager.GetOrCreate(_chartItem);

        if (!ReferenceEquals(previousState, nextState) && previousState is not null)
        {
            previousState.SnapshotUpdated -= OnChartSnapshotUpdated;
        }

        _chartState = nextState;
        if (!ReferenceEquals(previousState, nextState) && nextState is not null)
        {
            nextState.SnapshotUpdated += OnChartSnapshotUpdated;
        }

        if (PageIsActive && IsVisible)
        {
            RequestSnapshotRefresh();
        }
    }

    private void OnChartSnapshotUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (PageIsActive && IsVisible)
            {
                RenderPlot();
            }
        }, DispatcherPriority.Background);
    }

    private void RequestSnapshotRefresh()
    {
        if (!PageIsActive || !IsVisible || _chartItem is null || _chartState is null)
        {
            return;
        }

        var plotWidth = _plotHost?.Bounds.Width ?? Bounds.Width;
        var maxRenderPoints = Math.Max(128, (int)Math.Ceiling(Math.Max(1d, plotWidth) * 2d));
        _chartState.RequestSnapshotRefresh(
            viewSeconds: Math.Max(1, _chartItem.ViewSeconds),
            maxRenderPoints: maxRenderPoints);
    }

    private void OnChartItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderItemModel.TargetPath)
            or nameof(FolderItemModel.ChartSeriesDefinitions)
            or nameof(FolderItemModel.Id))
        {
            if (_chartItem is not null)
            {
                RebindChartState(reason: $"ChartItemPropertyChanged:{e.PropertyName}");
            }

            UpdateStatusText();
            HideCrosshair();
            if (PageIsActive && IsVisible)
            {
                RequestSnapshotRefresh();
                RenderPlot();
            }
        }

        if (e.PropertyName is nameof(FolderItemModel.ViewSeconds))
        {
            RequestSnapshotRefresh();
            if (PageIsActive && IsVisible)
            {
                RenderPlot();
            }
        }

        if (e.PropertyName is nameof(FolderItemModel.RefreshRateMs))
        {
            StartRenderTimer();
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBackground)
            or nameof(FolderItemModel.EffectiveContainerBackground)
            or nameof(FolderItemModel.EffectivePrimaryForeground)
            or nameof(FolderItemModel.EffectiveSecondaryForeground)
            or nameof(FolderItemModel.EffectiveContainerBorderBrush)
            or nameof(FolderItemModel.Title)
            or nameof(FolderItemModel.Footer))
        {
            UpdateStatusText();
            if (PageIsActive && IsVisible)
            {
                RenderPlot();
            }
        }
    }

    private void StartRenderTimer()
    {
        StopRenderTimer();

        var interval = _chartItem?.RefreshRateMs ?? 1000;
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(30, interval <= 0 ? 30 : interval))
        };
        _renderTimer.Tick += OnRenderTimerTick;
        _renderTimer.Start();
    }

    private void UpdateRenderActivity()
    {
        if (PageIsActive && IsVisible)
        {
            StartRenderTimer();
            return;
        }

        StopRenderTimer();
    }

    private void StopRenderTimer()
    {
        if (_renderTimer is null)
        {
            return;
        }

        _renderTimer.Stop();
        _renderTimer.Tick -= OnRenderTimerTick;
        _renderTimer = null;
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        if (IsPaused)
        {
            return;
        }

        RequestSnapshotRefresh();
        RenderPlot();
    }

    private void OnAdjustAxesClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Open();
        e.Handled = true;
    }

    private void ConfigurePlot()
    {
        if (_avaPlot is null || PlotSyncRoot is not { } syncRoot)
        {
            return;
        }

        lock (syncRoot)
        {
            _avaPlot.Plot.ShowLegend(Alignment.UpperLeft);
            EnsureAxesCreated();
            ApplyPlotTheme();
            UpdateInteractionState();
        }
    }

    private void EnsureAxesCreated()
    {
        if (_avaPlot is null || PlotSyncRoot is not { } syncRoot)
        {
            return;
        }

        lock (syncRoot)
        {
            var plot = _avaPlot.Plot;
            if (!_hasConfiguredAxes)
            {
                plot.Axes.DateTimeTicksBottom();
                _hasConfiguredAxes = true;
            }

            plot.Axes.Left.Label.Text = "Y1";
            plot.Axes.Left.IsVisible = true;

            _yAxis2 ??= plot.Axes.AddLeftAxis();
            _yAxis3 ??= plot.Axes.AddLeftAxis();
            _yAxis4 ??= plot.Axes.AddLeftAxis();

            _yAxis2.Label.Text = "Y2";
            _yAxis3.Label.Text = "Y3";
            _yAxis4.Label.Text = "Y4";
            _yAxis2.IsVisible = false;
            _yAxis3.IsVisible = false;
            _yAxis4.IsVisible = false;
        }
    }

    private void ApplyPlotTheme()
    {
        if (_avaPlot is null || PlotSyncRoot is not { } syncRoot)
        {
            return;
        }

        lock (syncRoot)
        {
            var plot = _avaPlot.Plot;
            var darkMode = ViewModel?.IsDarkTheme ?? IsDarkColor(_chartItem?.EffectiveBackground);
            var figureBackground = ParseScottPlotColor(_chartItem?.EffectiveContainerBackground) ?? (darkMode ? Colors.Black : Colors.White);
            var axesColor = ParseScottPlotColor(_chartItem?.EffectivePrimaryForeground) ?? (darkMode ? Colors.White : Colors.Black);
            var gridColor = ParseScottPlotColor(ViewModel?.GridLineBrush) ?? (darkMode ? Colors.DimGray : Colors.LightGray);
            var legendBackground = ParseScottPlotColor(_chartItem?.EffectiveBackground) ?? figureBackground;

            plot.FigureBackground.Color = figureBackground;
            plot.DataBackground.Color = figureBackground;
            plot.Grid.MajorLineColor = gridColor;
            plot.Grid.MinorLineColor = gridColor;
            plot.Axes.Color(axesColor);
            plot.Legend.BackgroundColor = legendBackground;
            plot.Legend.FontColor = axesColor;
            plot.Legend.Alignment = Alignment.UpperLeft;
        }
    }

    private void UpdateInteractionState()
    {
        if (_avaPlot?.UserInputProcessor is { } userInputProcessor && PlotSyncRoot is { } syncRoot)
        {
            lock (syncRoot)
            {
                if (IsPaused)
                {
                    userInputProcessor.Enable();
                }
                else
                {
                    userInputProcessor.Disable();
                }
            }
        }

        if (!IsPaused)
        {
            HideCrosshair();
        }
    }

    private void RenderPlot()
    {
        if (!PageIsActive || !IsVisible || _avaPlot is null || PlotSyncRoot is not { } syncRoot)
        {
            return;
        }

        var chartSnapshot = _chartState?.GetRenderSnapshot() ?? ChartRenderSnapshot.Empty;
        var seriesSnapshots = chartSnapshot.SeriesSnapshots;
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackChartRender(
            owner: this.GetVisualRoot() as Window,
            chartName: _chartItem?.Name ?? _chartItem?.Id ?? nameof(RealtimeChartControl),
            seriesCount: seriesSnapshots.Count);
        var hasSeries = seriesSnapshots.Count > 0;
        var hasData = false;
        var activeAxisIndexes = seriesSnapshots
            .Where(snapshot => snapshot.Values.Length > 0)
            .Select(snapshot => snapshot.Configuration.AxisIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        lock (syncRoot)
        {
            var plot = _avaPlot.Plot;
            plot.Clear();
            EnsureAxesCreated();
            ApplyPlotTheme();

            var axisMap = CreateAxisMap(plot, activeAxisIndexes);
            for (var i = 0; i < seriesSnapshots.Count; i++)
            {
                var snapshot = seriesSnapshots[i];
                if (snapshot.Values.Length == 0)
                {
                    continue;
                }

                hasData = true;
                var scatter = plot.Add.Scatter(snapshot.Timestamps, snapshot.Values);
                scatter.LegendText = GetSeriesLabel(snapshot.Configuration);
                scatter.LineWidth = 2;
                scatter.MarkerSize = 0;
                scatter.Color = SeriesColors[i % SeriesColors.Length];
                scatter.ConnectStyle = snapshot.Configuration.ConnectStyle;
                scatter.Axes.YAxis = axisMap[snapshot.Configuration.AxisIndex];
            }

            plot.Axes.SetLimitsX(chartSnapshot.VisibleFrom.ToOADate(), chartSnapshot.VisibleTo.ToOADate());

            if (hasData)
            {
                foreach (var axisIndex in activeAxisIndexes)
                {
                    if (axisMap.TryGetValue(axisIndex, out var axis))
                    {
                        plot.Axes.AutoScaleY(axis);

                        if (_axisOverrides.TryGetValue(axisIndex, out var overrideConfig)
                            && overrideConfig.Min.HasValue && overrideConfig.Max.HasValue)
                        {
                            plot.Axes.SetLimitsY(overrideConfig.Min.Value, overrideConfig.Max.Value, axis);
                        }
                    }
                }
            }
        }

        _avaPlot.Refresh();
        UpdateEmptyStateIndicator(hasSeries: hasSeries, hasData: hasData);
        UpdateStatusText();
    }

    private void UpdateEmptyStateIndicator(bool hasSeries, bool hasData)
    {
        if (_emptyStateBorder is null || _emptyStateTextBlock is null)
        {
            return;
        }

        if (hasData)
        {
            _emptyStateBorder.IsVisible = false;
            _emptyStateTextBlock.Text = string.Empty;
            return;
        }

        _emptyStateTextBlock.Text = hasSeries
            ? "No data available"
            : "No series configured";
        _emptyStateBorder.IsVisible = true;
    }

    private Dictionary<int, IYAxis> CreateAxisMap(Plot plot, IReadOnlyCollection<int> activeAxisIndexes)
    {
        var axisMap = new Dictionary<int, IYAxis>
        {
            [1] = plot.Axes.Left
        };

        plot.Axes.Left.Label.Text = "Y1";
        plot.Axes.Left.IsVisible = true;

        if (_yAxis2 is not null)
        {
            axisMap[2] = _yAxis2;
            _yAxis2.IsVisible = activeAxisIndexes.Contains(2);
        }

        if (_yAxis3 is not null)
        {
            axisMap[3] = _yAxis3;
            _yAxis3.IsVisible = activeAxisIndexes.Contains(3);
        }

        if (_yAxis4 is not null)
        {
            axisMap[4] = _yAxis4;
            _yAxis4.IsVisible = activeAxisIndexes.Contains(4);
        }

        return axisMap;
    }

    private List<ChartSeriesConfiguration> GetSeriesConfigurations()
    {
        return RealtimeChartRuntimeManager.GetSeriesConfigurations(_chartItem);
    }

    private void UpdateStatusText()
    {
        return;
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsPaused || _plotHost is null)
        {
            HideCrosshair();
            return;
        }

        var position = e.GetPosition(_plotHost);
        if (position.X < 0 || position.Y < 0 || position.X > _plotHost.Bounds.Width || position.Y > _plotHost.Bounds.Height)
        {
            HideCrosshair();
            return;
        }

        UpdateCrosshair(position);
    }

    private void OnPlotPointerExited(object? sender, PointerEventArgs e)
    {
        HideCrosshair();
    }

    private void UpdateCrosshair(Point position)
    {
        if (_plotHost is null || _crosshairOverlay is null || _crosshairVerticalLine is null || _crosshairHorizontalLine is null)
        {
            return;
        }

        var width = Math.Max(0, _plotHost.Bounds.Width);
        var height = Math.Max(0, _plotHost.Bounds.Height);

        _crosshairOverlay.Width = width;
        _crosshairOverlay.Height = height;

        _crosshairVerticalLine.Height = height;
        Canvas.SetLeft(_crosshairVerticalLine, position.X);
        Canvas.SetTop(_crosshairVerticalLine, 0);
        _crosshairVerticalLine.IsVisible = true;

        _crosshairHorizontalLine.Width = width;
        Canvas.SetLeft(_crosshairHorizontalLine, 0);
        Canvas.SetTop(_crosshairHorizontalLine, position.Y);
        _crosshairHorizontalLine.IsVisible = true;

        if (_crosshairInfoBorder is null || _crosshairInfoTextBlock is null)
        {
            return;
        }

        if (TryBuildCrosshairText(position, out var text))
        {
            _crosshairInfoTextBlock.Text = text;
            _crosshairInfoBorder.IsVisible = true;
        }
        else
        {
            _crosshairInfoTextBlock.Text = string.Empty;
            _crosshairInfoBorder.IsVisible = false;
        }
    }

    private bool TryBuildCrosshairText(Point position, out string text)
    {
        text = string.Empty;
        if (_avaPlot is null || PlotSyncRoot is not { } syncRoot)
        {
            return false;
        }

        Coordinates coordinates;
        lock (syncRoot)
        {
            var plot = _avaPlot.Plot;
            coordinates = plot.GetCoordinates((float)position.X, (float)position.Y, plot.Axes.Bottom, plot.Axes.Left);
        }

        if (double.IsNaN(coordinates.X) || double.IsInfinity(coordinates.X))
        {
            return false;
        }

        DateTime cursorTime;
        try
        {
            cursorTime = DateTime.FromOADate(coordinates.X);
        }
        catch
        {
            return false;
        }

        var lines = new List<string>
        {
            cursorTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)
        };

        foreach (var config in GetSeriesConfigurations())
        {
            var label = GetSeriesLabel(config);
            if (TryGetNearestPoint(config.Key, coordinates.X, out var point))
            {
                lines.Add($"{label}: {FormatValue(point.Value)}");
            }
            else
            {
                lines.Add($"{label}: n/a");
            }
        }

        text = string.Join(Environment.NewLine, lines);
        return true;
    }

    private bool TryGetNearestPoint(string key, double xPosition, out ChartNearestPoint point)
    {
        if (_chartState is null)
        {
            point = default;
            return false;
        }

        return _chartState.TryGetNearestPoint(key, xPosition, out point);
    }

    private void HideCrosshair()
    {
        if (_crosshairVerticalLine is not null)
        {
            _crosshairVerticalLine.IsVisible = false;
        }

        if (_crosshairHorizontalLine is not null)
        {
            _crosshairHorizontalLine.IsVisible = false;
        }

        if (_crosshairInfoBorder is not null)
        {
            _crosshairInfoBorder.IsVisible = false;
        }

        if (_crosshairInfoTextBlock is not null)
        {
            _crosshairInfoTextBlock.Text = string.Empty;
        }
    }

    private static string FormatValue(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsDarkColor(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return false;
        }

        if (!Avalonia.Media.Color.TryParse(colorText, out var color))
        {
            return false;
        }

        var brightness = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return brightness < 140;
    }

    private static ScottPlot.Color? ParseScottPlotColor(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText) || !Avalonia.Media.Color.TryParse(colorText, out var color))
        {
            return null;
        }

        return new ScottPlot.Color(color.R, color.G, color.B, color.A);
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private void OnPauseClicked(object? sender, RoutedEventArgs e)
    {
        IsPaused = !IsPaused;
        e.Handled = true;
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        _chartState?.Clear();
        RequestSnapshotRefresh();
        HideCrosshair();
        RenderPlot();
        e.Handled = true;
    }

    private async void OnYAxisAutoClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string tag || !int.TryParse(tag, out var axisIndex))
        {
            return;
        }

        _axisOverrides.Remove(axisIndex);
        RenderPlot();
        e.Handled = true;
    }

    private async void OnYAxisMinClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string tag || !int.TryParse(tag, out var axisIndex))
        {
            return;
        }

        if (this.GetVisualRoot() is not Window owner)
        {
            return;
        }

        _axisOverrides.TryGetValue(axisIndex, out var existing);
        var currentMin = existing?.Min;

        var result = await EditorInputDialogs.EditNumericAsync(owner, $"Y{axisIndex} Minimum", "Minimaler Y-Wert", "0.###", currentMin);
        if (result is null)
        {
            return;
        }

        var next = existing is null
            ? new AxisScaleOverride(result.Value, null)
            : existing with { Min = result.Value };

        _axisOverrides[axisIndex] = next;
        RenderPlot();
        e.Handled = true;
    }

    private async void OnYAxisMaxClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string tag || !int.TryParse(tag, out var axisIndex))
        {
            return;
        }

        if (this.GetVisualRoot() is not Window owner)
        {
            return;
        }

        _axisOverrides.TryGetValue(axisIndex, out var existing);
        var currentMax = existing?.Max;

        var result = await EditorInputDialogs.EditNumericAsync(owner, $"Y{axisIndex} Maximum", "Maximaler Y-Wert", "0.###", currentMax);
        if (result is null)
        {
            return;
        }

        var next = existing is null
            ? new AxisScaleOverride(null, result.Value)
            : existing with { Max = result.Value };

        _axisOverrides[axisIndex] = next;
        RenderPlot();
        e.Handled = true;
    }

    private async void OnXRefreshRateClicked(object? sender, RoutedEventArgs e)
    {
        if (_chartItem is null || this.GetVisualRoot() is not Window owner)
        {
            return;
        }

        var current = _chartItem.RefreshRateMs;
        var result = await EditorInputDialogs.EditNumericAsync(owner, "RefreshRate", "Abtastrate in ms", "0", current);
        if (result is null)
        {
            return;
        }

        _chartItem.RefreshRateMs = (int)Math.Max(1, Math.Round(result.Value));
        e.Handled = true;
    }

    private async void OnXViewSecondsClicked(object? sender, RoutedEventArgs e)
    {
        if (_chartItem is null || this.GetVisualRoot() is not Window owner)
        {
            return;
        }

        var current = _chartItem.ViewSeconds;
        var result = await EditorInputDialogs.EditNumericAsync(owner, "View", "Angezeigtes Zeitfenster in Sekunden", "0", current);
        if (result is null)
        {
            return;
        }

        _chartItem.ViewSeconds = (int)Math.Max(1, Math.Round(result.Value));
        e.Handled = true;
    }

    private string GetSeriesLabel(ChartSeriesConfiguration configuration)
    {
        var axisText = $"Y{Math.Clamp(configuration.AxisIndex, 1, 4)}";

        var widgetName = ResolveSeriesWidgetName(configuration.TargetPath, configuration.PageName);
        if (string.IsNullOrWhiteSpace(widgetName))
        {
            // Fallback: use existing display name or target path
            var fallback = !string.IsNullOrWhiteSpace(configuration.DisplayName)
                ? configuration.DisplayName
                : TargetPathHelper.SplitPathSegments(configuration.TargetPath).LastOrDefault() ?? configuration.TargetPath;
            return $"{axisText} {fallback}";
        }

        return $"{axisText} {widgetName.Trim()}";
    }

    private string? ResolveSeriesWidgetName(string targetPath, string? pageName)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return null;
        }

        var effectivePageName = !string.IsNullOrWhiteSpace(pageName)
            ? pageName
            : _chartItem?.FolderName;

        if (string.IsNullOrWhiteSpace(effectivePageName))
        {
            return null;
        }

        var page = viewModel.Folders.FirstOrDefault(p => string.Equals(p.Name, effectivePageName, StringComparison.Ordinal));
        if (page is null)
        {
            return null;
        }

        var comparableSeriesPath = TargetPathHelper.NormalizeComparablePath(targetPath);

        foreach (var item in EnumeratePageItems(page.Items))
        {
            if (item.Kind != ControlKind.Signal)
            {
                continue;
            }

            var itemPath = TargetPathHelper.ToPersistedLayoutTargetPath(item.TargetPath, effectivePageName);
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                continue;
            }

            if (!string.Equals(TargetPathHelper.NormalizeComparablePath(itemPath), comparableSeriesPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return item.Name;
        }

        return null;
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

    private sealed record AxisScaleOverride(double? Min, double? Max);
}

