using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using Amium.Items;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using TableCellSlot = HornetStudio.Editor.Models.FolderItemModel.TableCellSlot;

namespace HornetStudio.Editor.Widgets;

public partial class EditorCircleDisplayControl : EditorTemplateWidget
{
    private const string DefaultSignalColor = "#FFC107";
    private const string DefaultProgressBarColor = "#FFC107";
    private const string SignalColorItemName = "SignalColor";
    private const string SignalRunItemName = "SignalRun";
    private const string ProgressBarItemName = "ProgressBar";
    private const string ProgressStateItemName = "ProgressState";
    private const string ProgressBarColorItemName = "ProgressBarColor";
    private const double BeaconStep = 1.6d;
    private const double BeaconSweepDegrees = 34d;

    private Grid? _tableRoot;
    private Grid? _circleSurface;
    private Grid? _contentHost;
    private Grid? _itemContentGrid;
    private Grid? _itemOverlayGrid;
    private Border? _selectionOverlay;
    private Ellipse? _outerRing;
    private Ellipse? _innerRing;
    private Avalonia.Controls.Shapes.Path? _progressArc;
    private Avalonia.Controls.Shapes.Path? _beaconArc;
    private INotifyCollectionChanged? _itemsCollection;
    private INotifyPropertyChanged? _itemPropertySource;
    private readonly DispatcherTimer _beaconTimer;
    private bool _isSelecting;
    private int _selectionAnchorRow;
    private int _selectionAnchorColumn;
    private int _selectionCurrentRow;
    private int _selectionCurrentColumn;
    private bool _hasRangeAnchor;
    private int _rangeAnchorRow;
    private int _rangeAnchorColumn;
    private Cursor? _previousCursor;
    private bool _isMovingChild;
    private bool _isResizingChild;
    private FolderItemModel? _activeChildItem;
    private int _dragRowOffset;
    private int _dragColumnOffset;
    private int _previewRow;
    private int _previewColumn;
    private int _previewRowSpan = 1;
    private int _previewColumnSpan = 1;
    private bool _hasPreview;
    private double _beaconOffset;
    private bool _isBeaconRunning;
    private bool _isProgressBarActive;
    private double _currentProgressState;
    private string _registryPath = string.Empty;
    private string _signalColorRuntimePath = string.Empty;
    private string _signalRunRuntimePath = string.Empty;
    private string _progressBarRuntimePath = string.Empty;
    private string _progressStateRuntimePath = string.Empty;
    private string _progressBarColorRuntimePath = string.Empty;
    private FolderItemModel? _item;
    private bool _isUpdatingRuntimeSignals;
    private readonly Dictionary<string, string> _publishedRuntimeValues = new(StringComparer.OrdinalIgnoreCase);

    private FolderItemModel? Item => _item;

    private MainWindowViewModel? ViewModel
        => this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel } ? viewModel : null;

    public EditorCircleDisplayControl()
    {
        InitializeComponent();
        _beaconTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(24)
        };
        _beaconTimer.Tick += OnBeaconTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _item = DataContext as FolderItemModel;
        _tableRoot = this.FindControl<Grid>("TableRoot");
        _circleSurface = this.FindControl<Grid>("CircleSurface");
        _contentHost = this.FindControl<Grid>("ContentHost");
        _itemContentGrid = this.FindControl<Grid>("ItemContentGrid");
        _itemOverlayGrid = this.FindControl<Grid>("ItemOverlayGrid");
        _selectionOverlay = this.FindControl<Border>("SelectionOverlay");
        _outerRing = this.FindControl<Ellipse>("OuterRing");
        _innerRing = this.FindControl<Ellipse>("InnerRing");
        _progressArc = this.FindControl<Avalonia.Controls.Shapes.Path>("ProgressArc");
        _beaconArc = this.FindControl<Avalonia.Controls.Shapes.Path>("BeaconArc");

        if (_tableRoot is not null)
        {
            _tableRoot.SizeChanged += OnRootSizeChanged;
        }

        if (_contentHost is not null)
        {
            _contentHost.SizeChanged += OnContentHostSizeChanged;
        }

        HookItemProperties();
        HookItemsCollection();
        UpdateRuntimeSnapshot();
        RefreshSignalState();
        RefreshCircleGeometry();
        RefreshItemOverlay();
        Dispatcher.UIThread.Post(() =>
        {
            RefreshCircleGeometry();
            RefreshItemOverlay();
        }, DispatcherPriority.Loaded);
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        if (_tableRoot is not null)
        {
            _tableRoot.SizeChanged -= OnRootSizeChanged;
        }

        if (_contentHost is not null)
        {
            _contentHost.SizeChanged -= OnContentHostSizeChanged;
        }

        UnhookItemProperties();
        UnhookItemsCollection();
        RemovePublishedRuntimeItems();
        StopBeacon();
        _tableRoot = null;
        _circleSurface = null;
        _contentHost = null;
        _itemContentGrid = null;
        _itemOverlayGrid = null;
        _selectionOverlay = null;
        _outerRing = null;
        _innerRing = null;
        _progressArc = null;
        _beaconArc = null;
        _item = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _item = DataContext as FolderItemModel;
        HookItemProperties();
        HookItemsCollection();
        UpdateRuntimeSnapshot();
        RefreshSignalState();
        RefreshCircleGeometry();
        RefreshItemOverlay();
        Dispatcher.UIThread.Post(() =>
        {
            RefreshCircleGeometry();
            RefreshItemOverlay();
        }, DispatcherPriority.Loaded);
    }

    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs e) => RefreshCircleGeometry();

    private void OnContentHostSizeChanged(object? sender, SizeChangedEventArgs e) => RefreshCircleGeometry();

    private void HookItemsCollection()
    {
        UnhookItemsCollection();
        _itemsCollection = Item?.Items;
        if (_itemsCollection is not null)
        {
            _itemsCollection.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void UnhookItemsCollection()
    {
        if (_itemsCollection is null)
        {
            return;
        }

        _itemsCollection.CollectionChanged -= OnItemsCollectionChanged;
        _itemsCollection = null;
    }

    private void HookItemProperties()
    {
        UnhookItemProperties();
        _itemPropertySource = Item;
        if (_itemPropertySource is not null)
        {
            _itemPropertySource.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void UnhookItemProperties()
    {
        if (_itemPropertySource is null)
        {
            return;
        }

        _itemPropertySource.PropertyChanged -= OnItemPropertyChanged;
        _itemPropertySource = null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItemOverlay();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.TableRows), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.TableColumns), StringComparison.Ordinal))
        {
            RefreshItemOverlay();
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.Path), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.FolderName), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.Name), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.Title), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.SignalColor), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.SignalRun), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.ProgressBar), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.ProgressState), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.ProgressBarColor), StringComparison.Ordinal))
        {
            UpdateRuntimeSnapshot();
            RefreshSignalState();
        }
    }

    private void UpdateRuntimeSnapshot()
    {
        var previousRegistryPath = _registryPath;

        if (Item?.IsCircleDisplay != true)
        {
            _registryPath = string.Empty;
            _signalColorRuntimePath = string.Empty;
            _signalRunRuntimePath = string.Empty;
            _progressBarRuntimePath = string.Empty;
            _progressStateRuntimePath = string.Empty;
            _progressBarColorRuntimePath = string.Empty;
            RemovePublishedRuntimeItems();
            return;
        }

        _registryPath = Item.GetDisplayRuntimeBasePath();
        _signalColorRuntimePath = Item.GetDisplayRuntimePath(SignalColorItemName);
        _signalRunRuntimePath = Item.GetDisplayRuntimePath(SignalRunItemName);
        _progressBarRuntimePath = Item.GetDisplayRuntimePath(ProgressBarItemName);
        _progressStateRuntimePath = Item.GetDisplayRuntimePath(ProgressStateItemName);
        _progressBarColorRuntimePath = Item.GetDisplayRuntimePath(ProgressBarColorItemName);

        if (!string.Equals(previousRegistryPath, _registryPath, StringComparison.OrdinalIgnoreCase))
        {
            RemovePublishedRuntimeItems();
        }

        EnsureRuntimeSignals();
    }

    private void RefreshCircleGeometry()
    {
        if (_tableRoot is null || _circleSurface is null || _contentHost is null || _outerRing is null || _innerRing is null || _progressArc is null || _beaconArc is null)
        {
            return;
        }

        var size = Math.Max(0d, Math.Min(_tableRoot.Bounds.Width, _tableRoot.Bounds.Height));
        _circleSurface.Width = size;
        _circleSurface.Height = size;

        var ringThickness = Math.Max(6d, size * 0.035d);
        var outerMargin = ringThickness;
        var innerMargin = ringThickness * 2.1d;
        var contentMargin = ringThickness * 3.35d;
        var outerStrokeThickness = Math.Max(2d, ringThickness * 0.28d);
        var innerStrokeThickness = Math.Max(2d, ringThickness * 0.22d);
        var outerRadius = (size / 2d) - outerMargin - (outerStrokeThickness / 2d);
        var innerRadius = (size / 2d) - innerMargin - (innerStrokeThickness / 2d);
        var beaconBand = Math.Max(2d, outerRadius - innerRadius);

        _outerRing.Margin = new Thickness(outerMargin);
        _outerRing.StrokeThickness = outerStrokeThickness;

        _innerRing.Margin = new Thickness(innerMargin);
        _innerRing.StrokeThickness = innerStrokeThickness;

        _progressArc.Margin = default;
        _beaconArc.Margin = default;
        _progressArc.StrokeThickness = beaconBand * 0.58d;
        _beaconArc.StrokeThickness = beaconBand * 0.58d;

        _contentHost.Margin = new Thickness(contentMargin);
        _contentHost.Clip = new EllipseGeometry(new Rect(_contentHost.Bounds.Size));
        UpdateProgressVisual();
        UpdateActivityVisual();
        RefreshItemOverlay();
    }

    private void RefreshItemOverlay()
    {
        if (_itemContentGrid is null || _itemOverlayGrid is null || Item is null)
        {
            return;
        }

        _itemContentGrid.RowDefinitions.Clear();
        _itemContentGrid.ColumnDefinitions.Clear();
        _itemOverlayGrid.RowDefinitions.Clear();
        _itemOverlayGrid.ColumnDefinitions.Clear();

        for (var row = 0; row < Item.TableRows; row++)
        {
            _itemContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _itemOverlayGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        for (var column = 0; column < Item.TableColumns; column++)
        {
            _itemContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _itemOverlayGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        _itemContentGrid.Children.Clear();
        _itemOverlayGrid.Children.Clear();

        foreach (var child in Item.Items)
        {
            if (!child.IsTableChildControl)
            {
                continue;
            }

            var row = Math.Max(0, child.TableCellRow - 1);
            var column = Math.Max(0, child.TableCellColumn - 1);
            var rowSpan = Math.Max(1, child.TableCellRowSpan);
            var columnSpan = Math.Max(1, child.TableCellColumnSpan);

            Control content;
            if (child.IsButton)
            {
                content = new EditorButtonControl();
            }
            else if (child.IsItem)
            {
                content = new EditorSignalControl();
            }
            else if (child.IsLogControl)
            {
                content = new EditorLogControl
                {
                    PageIsActive = true
                };
            }
            else if (child.IsChartControl)
            {
                content = new RealtimeChartControl
                {
                    PageIsActive = true
                };
            }
            else if (child.IsUdlClientControl)
            {
                content = new UdlClientControl();
            }
            else
            {
                continue;
            }

            content.DataContext = child;
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.VerticalAlignment = VerticalAlignment.Stretch;
            content.Bind(Control.IsVisibleProperty, new Binding(nameof(FolderItemModel.IsVisibleInActiveView)));

            Grid.SetRow(content, row);
            Grid.SetColumn(content, column);
            Grid.SetRowSpan(content, rowSpan);
            Grid.SetColumnSpan(content, columnSpan);
            _itemContentGrid.Children.Add(content);

            var overlay = new Grid
            {
                DataContext = child,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            overlay.Bind(IsVisibleProperty, new Binding(nameof(FolderItemModel.IsSelected)));

            var moveSurface = new Border
            {
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = child
            };
            moveSurface.PointerPressed += OnChildMovePointerPressed;
            overlay.Children.Add(moveSurface);

            var toolbar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 4, 4, 0),
                Background = Brushes.Orange,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 1, 2, 1),
                Opacity = 0.95
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2
            };

            var settingsButton = new Button
            {
                Width = 16,
                Height = 16,
                MinWidth = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Tag = child
            };
            settingsButton.Classes.Add("toolicon");
            ToolTip.SetTip(settingsButton, "Settings");
            settingsButton.Click += OnChildSettingsClicked;
            settingsButton.Content = new ThemeSvgIcon
            {
                Width = 10,
                Height = 10,
                IconPath = "avares://HornetStudio.Editor/EditorIcons/cog.svg"
            };

            var deleteButton = new Button
            {
                Width = 16,
                Height = 16,
                MinWidth = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Tag = child
            };
            deleteButton.Classes.Add("toolicon");
            ToolTip.SetTip(deleteButton, "Delete");
            deleteButton.Click += OnChildDeleteClicked;
            deleteButton.Content = new ThemeSvgIcon
            {
                Width = 10,
                Height = 10,
                IconPath = "avares://HornetStudio.Editor/EditorIcons/remove.svg"
            };

            stack.Children.Add(settingsButton);
            stack.Children.Add(deleteButton);
            toolbar.Child = stack;
            overlay.Children.Add(toolbar);

            var resizeGrip = new Border
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 4),
                Background = Brushes.Orange,
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = child,
                Opacity = 0.95
            };
            resizeGrip.PointerPressed += OnChildResizePointerPressed;
            overlay.Children.Add(resizeGrip);

            Grid.SetRow(overlay, row);
            Grid.SetColumn(overlay, column);
            Grid.SetRowSpan(overlay, rowSpan);
            Grid.SetColumnSpan(overlay, columnSpan);
            _itemOverlayGrid.Children.Add(overlay);
        }
    }

    private void EnsureRuntimeSignals()
    {
        if (_isUpdatingRuntimeSignals)
        {
            return;
        }

        if (Item?.IsCircleDisplay != true || string.IsNullOrWhiteSpace(_registryPath))
        {
            return;
        }

        var signalColor = ReadRuntimeString(_signalColorRuntimePath, string.IsNullOrWhiteSpace(Item.SignalColor) ? DefaultSignalColor : Item.SignalColor);
        var signalRun = ReadRuntimeBoolean(_signalRunRuntimePath, Item.SignalRun);
        var progressBar = ReadRuntimeBoolean(_progressBarRuntimePath, Item.ProgressBar);
        var progressState = ReadRuntimeDouble(_progressStateRuntimePath, Math.Clamp(Item.ProgressState, 0d, 100d), 0d, 100d);
        var progressBarColor = ReadRuntimeString(_progressBarColorRuntimePath, string.IsNullOrWhiteSpace(Item.ProgressBarColor) ? DefaultProgressBarColor : Item.ProgressBarColor);

        _isUpdatingRuntimeSignals = true;
        try
        {
            PublishRuntimeValue(SignalColorItemName, signalColor, "Circle display signal color");
            PublishRuntimeValue(SignalRunItemName, signalRun, "Circle display signal state");
            PublishRuntimeValue(ProgressBarItemName, progressBar, "Circle display progress visibility");
            PublishRuntimeValue(ProgressStateItemName, progressState, "Circle display progress state");
            PublishRuntimeValue(ProgressBarColorItemName, progressBarColor, "Circle display progress color");
        }
        finally
        {
            _isUpdatingRuntimeSignals = false;
        }
    }

    private void PublishRuntimeValue(string itemName, object? value, string title)
    {
        if (Item is null)
        {
            return;
        }

        var path = Item.GetDisplayRuntimePath(itemName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var serializedValue = value?.ToString() ?? string.Empty;
        if (_publishedRuntimeValues.TryGetValue(path, out var previousValue)
            && string.Equals(previousValue, serializedValue, StringComparison.Ordinal))
        {
            return;
        }

        _publishedRuntimeValues[path] = serializedValue;

        var snapshot = new Item(itemName, value, _registryPath);
        snapshot.Params["Kind"].Value = "DisplayRuntime";
        snapshot.Params["Text"].Value = title;
        snapshot.Params["Title"].Value = title;
        HostRegistries.Data.UpsertSnapshot(snapshot.Path!, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
    }

    private void RemovePublishedRuntimeItems()
    {
        foreach (var path in _publishedRuntimeValues.Keys)
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedRuntimeValues.Clear();
    }

    private void RefreshSignalState()
    {
        if (Item is null)
        {
            ApplyBeaconColor(DefaultSignalColor);
            ApplyProgressColor(DefaultProgressBarColor);
            ApplyState(false, false, 0d);
            return;
        }

        var signalColor = ReadRuntimeString(_signalColorRuntimePath, string.IsNullOrWhiteSpace(Item.SignalColor) ? DefaultSignalColor : Item.SignalColor);
        var progressBarColor = ReadRuntimeString(_progressBarColorRuntimePath, string.IsNullOrWhiteSpace(Item.ProgressBarColor) ? DefaultProgressBarColor : Item.ProgressBarColor);
        var progressBar = ReadRuntimeBoolean(_progressBarRuntimePath, Item.ProgressBar);
        var progressState = ReadRuntimeDouble(_progressStateRuntimePath, Math.Clamp(Item.ProgressState, 0d, 100d), 0d, 100d);
        var signalRun = ReadRuntimeBoolean(_signalRunRuntimePath, Item.SignalRun);

        ApplyBeaconColor(signalColor);
        ApplyProgressColor(progressBarColor);
        ApplyState(progressBar, signalRun, progressState);
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (_isUpdatingRuntimeSignals)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_registryPath))
        {
            return;
        }

        if (!MatchesRuntimeChange(e.Key))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            EnsureRuntimeSignals();
            RefreshSignalState();
        });
    }

    private bool MatchesRuntimeChange(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return string.Equals(path, _registryPath, StringComparison.Ordinal)
            || string.Equals(path, _signalColorRuntimePath, StringComparison.Ordinal)
            || string.Equals(path, _signalRunRuntimePath, StringComparison.Ordinal)
            || string.Equals(path, _progressBarRuntimePath, StringComparison.Ordinal)
            || string.Equals(path, _progressStateRuntimePath, StringComparison.Ordinal)
            || string.Equals(path, _progressBarColorRuntimePath, StringComparison.Ordinal)
            || path.StartsWith(_registryPath + ".", StringComparison.Ordinal);
    }

    private string ReadRuntimeString(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !HostRegistries.Data.TryResolve(path, out var item)
            || item is null)
        {
            return fallback;
        }

        object? rawValue = item.Value;
        var value = rawValue?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private bool ReadRuntimeBoolean(string? path, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !HostRegistries.Data.TryResolve(path, out var item)
            || item is null)
        {
            return fallback;
        }

        return item.Value is null ? fallback : ToBoolean(item.Value);
    }

    private double ReadRuntimeDouble(string? path, double fallback, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !HostRegistries.Data.TryResolve(path, out var item)
            || item is null)
        {
            return Math.Clamp(fallback, min, max);
        }

        return Math.Clamp(ToDouble(item.Value), min, max);
    }

    private void ApplyBeaconColor(string? value)
    {
        if (_beaconArc is null)
        {
            return;
        }

        if (!Color.TryParse(string.IsNullOrWhiteSpace(value) ? DefaultSignalColor : value, out var color))
        {
            Color.TryParse(DefaultSignalColor, out color);
        }

        _beaconArc.Stroke = new SolidColorBrush(color);
    }

    private void ApplyProgressColor(string? value)
    {
        if (_progressArc is null)
        {
            return;
        }

        if (!Color.TryParse(string.IsNullOrWhiteSpace(value) ? DefaultProgressBarColor : value, out var color))
        {
            Color.TryParse(DefaultProgressBarColor, out color);
        }

        _progressArc.Stroke = new SolidColorBrush(color);
    }

    private void ApplyState(bool progressBar, bool signalRun, double progressState)
    {
        _isProgressBarActive = progressBar;
        _isBeaconRunning = signalRun;
        _currentProgressState = Math.Clamp(progressState, 0d, 100d);

        if (signalRun)
        {
            StartBeacon();
        }
        else
        {
            StopBeacon();
        }

        UpdateProgressVisual();
        UpdateActivityVisual();
    }

    private void StartBeacon()
    {
        if (!_beaconTimer.IsEnabled)
        {
            _beaconTimer.Start();
        }
    }

    private void StopBeacon()
    {
        _beaconTimer.Stop();
    }

    private void OnBeaconTimerTick(object? sender, EventArgs e)
    {
        _beaconOffset = (_beaconOffset + BeaconStep) % 360d;
        UpdateActivityVisual();
    }

    private void UpdateProgressVisual()
    {
        if (_progressArc is null || _circleSurface is null)
        {
            return;
        }

        if (!_isProgressBarActive || _currentProgressState <= 0d)
        {
            _progressArc.IsVisible = false;
            _progressArc.Data = null;
            return;
        }

        var size = Math.Min(_circleSurface.Bounds.Width, _circleSurface.Bounds.Height);
        var ringThickness = Math.Max(6d, size * 0.035d);
        var outerMargin = ringThickness;
        var innerMargin = ringThickness * 2.1d;
        var outerStrokeThickness = Math.Max(2d, ringThickness * 0.28d);
        var innerStrokeThickness = Math.Max(2d, ringThickness * 0.22d);
        var outerRadius = (size / 2d) - outerMargin - (outerStrokeThickness / 2d);
        var innerRadius = (size / 2d) - innerMargin - (innerStrokeThickness / 2d);
        var radius = Math.Max(1d, (outerRadius + innerRadius) / 2d);
        var center = new Point(_circleSurface.Bounds.Width / 2d, _circleSurface.Bounds.Height / 2d);
        _progressArc.Data = BuildArcGeometry(center, radius, -90d, 360d * (_currentProgressState / 100d));
        _progressArc.IsVisible = true;
        _progressArc.Opacity = 1d;
    }

    private void UpdateActivityVisual()
    {
        if (_beaconArc is null || _circleSurface is null)
        {
            return;
        }

        if (!_isBeaconRunning)
        {
            _beaconArc.IsVisible = false;
            _beaconArc.Data = null;
            return;
        }

        var size = Math.Min(_circleSurface.Bounds.Width, _circleSurface.Bounds.Height);
        var ringThickness = Math.Max(6d, size * 0.035d);
        var outerMargin = ringThickness;
        var innerMargin = ringThickness * 2.1d;
        var outerStrokeThickness = Math.Max(2d, ringThickness * 0.28d);
        var innerStrokeThickness = Math.Max(2d, ringThickness * 0.22d);
        var outerRadius = (size / 2d) - outerMargin - (outerStrokeThickness / 2d);
        var innerRadius = (size / 2d) - innerMargin - (innerStrokeThickness / 2d);
        var innerBand = Math.Max(2d, outerRadius - innerRadius);
        var radius = _isProgressBarActive
            ? Math.Max(1d, (size / 2d) - (ringThickness * 0.28d))
            : Math.Max(1d, (outerRadius + innerRadius) / 2d);
        var strokeThickness = _isProgressBarActive
            ? Math.Max(2d, ringThickness * 0.32d)
            : innerBand * 0.58d;
        var center = new Point(_circleSurface.Bounds.Width / 2d, _circleSurface.Bounds.Height / 2d);

        _beaconArc.StrokeThickness = strokeThickness;
        _beaconArc.Data = BuildArcGeometry(center, radius, _beaconOffset - 90d, BeaconSweepDegrees);
        _beaconArc.IsVisible = true;
        _beaconArc.Opacity = 1d;
    }

    private static PathGeometry? BuildArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var clampedSweep = Math.Clamp(sweepAngle, 0d, 360d);
        if (radius <= 0d || clampedSweep <= 0d)
        {
            return null;
        }

        var geometry = new PathGeometry
        {
            Figures = new PathFigures()
        };

        if (clampedSweep >= 359.999d)
        {
            var startPoint = PointOnCircle(center, radius, startAngle);
            var midpoint = PointOnCircle(center, radius, startAngle + 180d);
            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false,
                IsFilled = false,
                Segments = new PathSegments()
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = midpoint,
                Size = new Size(radius, radius),
                IsLargeArc = false,
                SweepDirection = SweepDirection.Clockwise
            });
            figure.Segments.Add(new ArcSegment
            {
                Point = startPoint,
                Size = new Size(radius, radius),
                IsLargeArc = false,
                SweepDirection = SweepDirection.Clockwise
            });
            geometry.Figures.Add(figure);
            return geometry;
        }

        var endAngle = startAngle + clampedSweep;
        var figureSingle = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, startAngle),
            IsClosed = false,
            IsFilled = false,
            Segments = new PathSegments()
        };
        figureSingle.Segments.Add(new ArcSegment
        {
            Point = PointOnCircle(center, radius, endAngle),
            Size = new Size(radius, radius),
            IsLargeArc = clampedSweep > 180d,
            SweepDirection = SweepDirection.Clockwise
        });
        geometry.Figures.Add(figureSingle);
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + (Math.Cos(angleRadians) * radius),
            center.Y + (Math.Sin(angleRadians) * radius));
    }

    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when long.TryParse(text, out var parsedLong) => parsedLong != 0,
            byte numeric => numeric != 0,
            sbyte numeric => numeric != 0,
            short numeric => numeric != 0,
            ushort numeric => numeric != 0,
            int numeric => numeric != 0,
            uint numeric => numeric != 0,
            long numeric => numeric != 0,
            ulong numeric => numeric != 0,
            float numeric => Math.Abs(numeric) > float.Epsilon,
            double numeric => Math.Abs(numeric) > double.Epsilon,
            decimal numeric => numeric != 0,
            _ => false
        };
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0d,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            short shortValue => shortValue,
            uint uintValue => uintValue,
            ulong ulongValue => ulongValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            string text when double.TryParse(text, out var parsed) => parsed,
            _ => 0d
        };
    }

    private void OnAddItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { ContextMenu: { } menu })
        {
            menu.Open();
            e.Handled = true;
        }
    }

    private void OnCellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode)
        {
            return;
        }

        if (sender is not Control control || control.DataContext is not TableCellSlot slot || !slot.IsVisibleInLayout)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (slot.ChildItem is FolderItemModel childItem
            && (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                || point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed))
        {
            ViewModel.SelectItem(childItem);
            e.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            ViewModel.ToggleTableCellSelection(Item, slot.Row, slot.Column, toggle: false);
            ViewModel.AddItemToSelectedTableCells(Item);
            RefreshItemOverlay();
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
            && modifiers.HasFlag(KeyModifiers.Control))
        {
            _isSelecting = false;
            if (_selectionOverlay is not null)
            {
                _selectionOverlay.IsVisible = false;
            }

            HandleCtrlRangeClick(slot);
            e.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _hasRangeAnchor = false;
            _isSelecting = true;
            _selectionAnchorRow = slot.Row;
            _selectionAnchorColumn = slot.Column;
            _selectionCurrentRow = slot.Row;
            _selectionCurrentColumn = slot.Column;
            _previousCursor = control.Cursor;
            control.Cursor = new Cursor(StandardCursorType.Cross);
            e.Pointer.Capture(_contentHost);
            ViewModel.SelectTableRectangle(Item, _selectionAnchorRow, _selectionAnchorColumn, _selectionCurrentRow, _selectionCurrentColumn);
            UpdateSelectionOverlay(_selectionAnchorRow, _selectionAnchorColumn, _selectionCurrentRow, _selectionCurrentColumn);
            e.Handled = true;
            return;
        }
    }

    private void OnSelectionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isMovingChild || _isResizingChild)
        {
            HandleChildPointerMoved(sender, e);
            return;
        }

        if (!_isSelecting || ViewModel is null || Item is null || !ViewModel.IsEditMode)
        {
            return;
        }

        if (sender is not Control control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _isSelecting = false;
            if (_selectionOverlay is not null)
            {
                _selectionOverlay.IsVisible = false;
            }
            ReleaseSelectionPointer(e.Pointer);
            return;
        }

        if (!TryGetCircleSlotFromPoint(e.GetPosition(control), requireFree: true, out var slot))
        {
            return;
        }

        _selectionCurrentRow = slot.Row;
        _selectionCurrentColumn = slot.Column;
        ViewModel.SelectTableRectangle(Item, _selectionAnchorRow, _selectionAnchorColumn, _selectionCurrentRow, _selectionCurrentColumn);
        UpdateSelectionOverlay(_selectionAnchorRow, _selectionAnchorColumn, _selectionCurrentRow, _selectionCurrentColumn);
        e.Handled = true;
    }

    private void OnSelectionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isMovingChild || _isResizingChild)
        {
            CommitChildInteraction(e);
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            if (_isSelecting && ViewModel is not null && Item is not null && ViewModel.IsEditMode)
            {
                ViewModel.SelectTableRectangle(Item, _selectionAnchorRow, _selectionAnchorColumn, _selectionCurrentRow, _selectionCurrentColumn);
            }

            ReleaseSelectionPointer(e.Pointer);
        }
    }

    private void OnChildMovePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode || _contentHost is null)
        {
            return;
        }

        if (sender is not Control { Tag: FolderItemModel child } control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (!TryGetCircleSlotFromPoint(e.GetPosition(_contentHost), requireFree: false, out var slot))
        {
            return;
        }

        _activeChildItem = child;
        _isMovingChild = true;
        _isResizingChild = false;
        _dragRowOffset = slot.Row - child.TableCellRow;
        _dragColumnOffset = slot.Column - child.TableCellColumn;
        _previewRow = child.TableCellRow;
        _previewColumn = child.TableCellColumn;
        _previewRowSpan = child.TableCellRowSpan;
        _previewColumnSpan = child.TableCellColumnSpan;
        _hasPreview = true;
        _previousCursor = _contentHost.Cursor;
        _contentHost.Cursor = new Cursor(StandardCursorType.Hand);
        e.Pointer.Capture(_contentHost);
        UpdateSelectionOverlay(child.TableCellRow, child.TableCellColumn, child.TableCellRow + child.TableCellRowSpan - 1, child.TableCellColumn + child.TableCellColumnSpan - 1);
        e.Handled = true;
    }

    private void OnChildResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode || _contentHost is null)
        {
            return;
        }

        if (sender is not Control { Tag: FolderItemModel child } control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        _activeChildItem = child;
        _isMovingChild = false;
        _isResizingChild = true;
        _previewRow = child.TableCellRow;
        _previewColumn = child.TableCellColumn;
        _previewRowSpan = child.TableCellRowSpan;
        _previewColumnSpan = child.TableCellColumnSpan;
        _hasPreview = true;
        _previousCursor = _contentHost.Cursor;
        _contentHost.Cursor = new Cursor(StandardCursorType.Hand);
        e.Pointer.Capture(_contentHost);
        UpdateSelectionOverlay(child.TableCellRow, child.TableCellColumn, child.TableCellRow + child.TableCellRowSpan - 1, child.TableCellColumn + child.TableCellColumnSpan - 1);
        e.Handled = true;
    }

    private void HandleChildPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeChildItem is null || ViewModel is null || Item is null || _contentHost is null)
        {
            return;
        }

        if (sender is not Control control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            ReleaseChildInteraction(e.Pointer);
            return;
        }

        if (!TryGetCircleSlotFromPoint(e.GetPosition(control), requireFree: false, out var slot))
        {
            e.Handled = true;
            return;
        }

        if (_isMovingChild)
        {
            var targetRow = slot.Row - _dragRowOffset;
            var targetColumn = slot.Column - _dragColumnOffset;
            if (ViewModel.CanOccupyTableRectangle(Item, targetRow, targetColumn, _activeChildItem.TableCellRowSpan, _activeChildItem.TableCellColumnSpan, _activeChildItem))
            {
                _previewRow = targetRow;
                _previewColumn = targetColumn;
                _previewRowSpan = _activeChildItem.TableCellRowSpan;
                _previewColumnSpan = _activeChildItem.TableCellColumnSpan;
                _hasPreview = true;
                UpdateSelectionOverlay(targetRow, targetColumn, targetRow + _previewRowSpan - 1, targetColumn + _previewColumnSpan - 1);
            }
        }
        else if (_isResizingChild)
        {
            var rowSpan = Math.Max(1, slot.Row - _activeChildItem.TableCellRow + 1);
            var columnSpan = Math.Max(1, slot.Column - _activeChildItem.TableCellColumn + 1);
            if (ViewModel.CanOccupyTableRectangle(Item, _activeChildItem.TableCellRow, _activeChildItem.TableCellColumn, rowSpan, columnSpan, _activeChildItem))
            {
                _previewRow = _activeChildItem.TableCellRow;
                _previewColumn = _activeChildItem.TableCellColumn;
                _previewRowSpan = rowSpan;
                _previewColumnSpan = columnSpan;
                _hasPreview = true;
                UpdateSelectionOverlay(_previewRow, _previewColumn, _previewRow + rowSpan - 1, _previewColumn + columnSpan - 1);
            }
        }

        e.Handled = true;
    }

    private void CommitChildInteraction(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && ViewModel is not null && Item is not null && _activeChildItem is not null && _hasPreview)
        {
            var changed = _isMovingChild
                ? ViewModel.TryMoveTableChild(Item, _activeChildItem, _previewRow, _previewColumn)
                : ViewModel.TryResizeTableChild(Item, _activeChildItem, _previewRowSpan, _previewColumnSpan);

            if (changed)
            {
                RefreshItemOverlay();
            }
        }

        ReleaseChildInteraction(e.Pointer);
        e.Handled = true;
    }

    private void ReleaseChildInteraction(IPointer pointer)
    {
        _isMovingChild = false;
        _isResizingChild = false;
        _activeChildItem = null;
        _hasPreview = false;
        pointer.Capture(null);

        if (_contentHost is not null)
        {
            _contentHost.Cursor = _previousCursor;
        }

        if (_selectionOverlay is not null)
        {
            _selectionOverlay.IsVisible = false;
        }
    }

    private void ReleaseSelectionPointer(IPointer pointer)
    {
        _isSelecting = false;
        pointer.Capture(null);

        if (_contentHost is not null)
        {
            _contentHost.Cursor = _previousCursor;
        }

        if (_selectionOverlay is not null)
        {
            _selectionOverlay.IsVisible = false;
        }
    }

    private bool TryGetCircleSlotFromPoint(Point point, bool requireFree, out TableCellSlot slot)
    {
        slot = null!;
        if (_contentHost is null || Item is null)
        {
            return false;
        }

        var bounds = _contentHost.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var normalizedX = Math.Clamp(point.X, 0, Math.Max(0, bounds.Width - 1));
        var normalizedY = Math.Clamp(point.Y, 0, Math.Max(0, bounds.Height - 1));
        var column = Math.Min(Item.TableColumns, Math.Max(1, (int)(normalizedX / (bounds.Width / Math.Max(1, Item.TableColumns))) + 1));
        var row = Math.Min(Item.TableRows, Math.Max(1, (int)(normalizedY / (bounds.Height / Math.Max(1, Item.TableRows))) + 1));
        var resolved = Item.TableCellSlots.FirstOrDefault(c => c.Row == row && c.Column == column);
        if (resolved is null || !resolved.IsVisibleInLayout)
        {
            return false;
        }

        if (requireFree && resolved.ChildItem is not null)
        {
            return false;
        }

        slot = resolved;
        return true;
    }

    private void HandleCtrlRangeClick(TableCellSlot slot)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode || !slot.IsVisibleInLayout)
        {
            return;
        }

        if (slot.ChildItem is not null)
        {
            return;
        }

        if (!_hasRangeAnchor)
        {
            _rangeAnchorRow = slot.Row;
            _rangeAnchorColumn = slot.Column;
            _hasRangeAnchor = true;
            ViewModel.SelectTableRectangle(Item, slot.Row, slot.Column, slot.Row, slot.Column);
            return;
        }

        ViewModel.SelectTableRectangle(Item, _rangeAnchorRow, _rangeAnchorColumn, slot.Row, slot.Column);
        _hasRangeAnchor = false;
    }

    private void UpdateSelectionOverlay(int startRow, int startColumn, int endRow, int endColumn)
    {
        if (_selectionOverlay is null || _contentHost is null || Item is null)
        {
            return;
        }

        var minRow = Math.Min(startRow, endRow);
        var maxRow = Math.Max(startRow, endRow);
        var minColumn = Math.Min(startColumn, endColumn);
        var maxColumn = Math.Max(startColumn, endColumn);
        var rootBounds = _contentHost.Bounds;
        if (rootBounds.Width <= 0 || rootBounds.Height <= 0)
        {
            return;
        }

        var cellWidth = rootBounds.Width / Math.Max(1, Item.TableColumns);
        var cellHeight = rootBounds.Height / Math.Max(1, Item.TableRows);
        var x = (minColumn - 1) * cellWidth;
        var y = (minRow - 1) * cellHeight;
        var width = Math.Max(cellWidth, (maxColumn - minColumn + 1) * cellWidth);
        var height = Math.Max(cellHeight, (maxRow - minRow + 1) * cellHeight);

        _selectionOverlay.Margin = new Thickness(x, y, 0, 0);
        _selectionOverlay.Width = width;
        _selectionOverlay.Height = height;
        _selectionOverlay.IsVisible = true;
    }

    private void AddControlToTable(ControlKind kind)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode)
        {
            return;
        }

        ViewModel.AddControlToSelectedTableCells(Item, kind);
        RefreshItemOverlay();
    }

    private void OnAddItemMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddControlToTable(ControlKind.Item);
        e.Handled = true;
    }

    private void OnAddButtonMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddControlToTable(ControlKind.Button);
        e.Handled = true;
    }

    private void OnAddChartMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddControlToTable(ControlKind.ChartControl);
        e.Handled = true;
    }

    private void OnAddLogMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddControlToTable(ControlKind.LogControl);
        e.Handled = true;
    }

    private void OnAddUdlClientMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.SupportsUdlClientControl)
        {
            return;
        }

        AddControlToTable(ControlKind.UdlClientControl);
        e.Handled = true;
    }

    private void OnChildSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Control { Tag: FolderItemModel child })
        {
            return;
        }

        ViewModel.OpenItemEditor(child, 0, 0);
        e.Handled = true;
    }

    private void OnChildDeleteClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Item is null || sender is not Control { Tag: FolderItemModel child })
        {
            return;
        }

        if (Item.Items.Contains(child))
        {
            Item.Items.Remove(child);
            Item.UpdateTableCellContentFromChildren();
            RefreshItemOverlay();
        }

        e.Handled = true;
    }
}
