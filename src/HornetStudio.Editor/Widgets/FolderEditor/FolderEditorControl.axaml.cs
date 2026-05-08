using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class FolderEditorControl : UserControl
{
    public static readonly StyledProperty<string> GridLineBrushProperty =
        AvaloniaProperty.Register<FolderEditorControl, string>(nameof(GridLineBrush), "#E9EEF5");

    public static readonly StyledProperty<FolderModel?> FolderProperty =
        AvaloniaProperty.Register<FolderEditorControl, FolderModel?>(nameof(Folder));

    private const double EdgeSnapDistance = 8;

    private Point? _dragStart;
    private Point? _resizeStart;
    private Point? _selectionStart;
    private bool _addToSelection;
    private FolderItemModel? _dragItem;
    private readonly Dictionary<FolderItemModel, Point> _dragOrigins = [];
    private double _dragGroupMinX;
    private double _dragGroupMinY;
    private double _dragGroupWidth;
    private double _dragGroupHeight;
    private double _resizeOriginWidth;
    private double _resizeOriginHeight;
    private readonly Dictionary<FolderItemModel, Size> _resizeOrigins = [];
    private EditorPropertyDialogWindow? _editorDialogWindow;

    public FolderEditorControl()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (this.FindControl<ScrollViewer>("EditorScrollViewer") is { } sv)
            {
                sv.PropertyChanged += OnScrollViewerPropertyChanged;
            }
        };
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            CloseEditorDialogWindow();
            AttachToViewModel(null);
            if (this.FindControl<ScrollViewer>("EditorScrollViewer") is { } sv)
            {
                sv.PropertyChanged -= OnScrollViewerPropertyChanged;
            }
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private MainWindowViewModel? _subscribedViewModel;

    public string GridLineBrush
    {
        get => GetValue(GridLineBrushProperty);
        private set => SetValue(GridLineBrushProperty, value);
    }

    public FolderModel? Folder
    {
        get => GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    private FolderModel? CurrentFolder => Folder ?? ViewModel?.SelectedFolder;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachToViewModel(ViewModel);
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
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

        UpdateThemeBindings();
        SyncEditorDialogWindow();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.GridLineBrush))
        {
            UpdateThemeBindings();
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.IsEditorDialogOpen))
        {
            SyncEditorDialogWindow();
        }
    }

    private void UpdateThemeBindings()
    {
        GridLineBrush = ViewModel?.GridLineBrush ?? "#E9EEF5";
    }

    private void SyncEditorDialogWindow()
    {
        if (ViewModel?.IsEditorDialogOpen == true)
        {
            EnsureEditorDialogWindow();
            return;
        }

        CloseEditorDialogWindow();
    }

    private void EnsureEditorDialogWindow()
    {
        if (_editorDialogWindow is not null)
        {
            _editorDialogWindow.Activate();
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        _editorDialogWindow = EditorPropertyDialogWindow.ShowOrActivate(owner, ViewModel);
        _editorDialogWindow.Closed -= OnEditorDialogWindowClosed;
        _editorDialogWindow.Closed += OnEditorDialogWindowClosed;
    }

    private void CloseEditorDialogWindow()
    {
        if (_editorDialogWindow is null)
        {
            return;
        }

        _editorDialogWindow.Closed -= OnEditorDialogWindowClosed;
        _editorDialogWindow.Close();
        _editorDialogWindow = null;
    }

    private void OnEditorDialogWindowClosed(object? sender, EventArgs e)
    {
        if (_editorDialogWindow is not null)
        {
            _editorDialogWindow.Closed -= OnEditorDialogWindowClosed;
            _editorDialogWindow = null;
        }
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (ViewModel is null || e.Property != ScrollViewer.ViewportProperty || sender is not ScrollViewer sv)
        {
            return;
        }

        var viewport = sv.Viewport;
        if (viewport.Width > 0 && viewport.Height > 0)
        {
            ViewModel.UpdateCanvasSize(viewport.Width, viewport.Height);
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (!ViewModel.IsEditMode)
        {
            ViewModel.CancelSelection();
            ViewModel.CancelEditorDialog();
            ViewModel.CancelValueInput();
            ViewModel.ClearItemSelection();
            return;
        }

        // Im Body-Interaktionsmodus: Klicks auf die freie Flaeche sollen die
        // aktuelle Auswahl nicht aufheben, damit der Body-Toggle erreichbar bleibt.
        if (ViewModel.IsShiftInteractionMode)
        {
            return;
        }

        ViewModel.CancelSelection();
        ViewModel.CancelEditorDialog();
        ViewModel.CancelValueInput();

        _addToSelection = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!_addToSelection)
        {
            ViewModel.ClearItemSelection();
        }

        var position = e.GetPosition(EditorCanvas);
        _selectionStart = position;
        e.Pointer.Capture(EditorCanvas);
        ViewModel.StartSelection(position.X, position.Y);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var position = e.GetPosition(EditorCanvas);

        if (_dragOrigins.Count > 0 && _dragStart is not null)
        {
            var deltaX = position.X - _dragStart.Value.X;
            var deltaY = position.Y - _dragStart.Value.Y;
            var targetGroupX = Clamp(ViewModel.SnapCoordinate(_dragGroupMinX + deltaX, _dragGroupWidth, EditorCanvas.Bounds.Width), 0, MaxCanvasX(_dragGroupWidth));
            var targetGroupY = Clamp(ViewModel.SnapCoordinate(_dragGroupMinY + deltaY, _dragGroupHeight, EditorCanvas.Bounds.Height), 0, MaxCanvasY(_dragGroupHeight));

            if (ViewModel.SnapToEdges)
            {
                (targetGroupX, targetGroupY) = SnapGroupToEdges(targetGroupX, targetGroupY, _dragGroupWidth, _dragGroupHeight);
            }

            var appliedDeltaX = targetGroupX - _dragGroupMinX;
            var appliedDeltaY = targetGroupY - _dragGroupMinY;

            foreach (var pair in _dragOrigins)
            {
                pair.Key.X = Clamp(pair.Value.X + appliedDeltaX, 0, MaxCanvasX(pair.Key.Width));
                pair.Key.Y = Clamp(pair.Value.Y + appliedDeltaY, 0, MaxCanvasY(pair.Key.Height));
            }

            return;
        }

        if (_dragItem is not null && _resizeStart is not null)
        {
            var masterWidth = ViewModel.SnapLength(_resizeOriginWidth + (position.X - _resizeStart.Value.X), _dragItem.MinWidth, MaxCanvasWidth(_dragItem.X));
            var masterHeight = ViewModel.SnapLength(_resizeOriginHeight + (position.Y - _resizeStart.Value.Y), _dragItem.MinHeight, MaxCanvasHeight(_dragItem.Y));

            if (ViewModel.SnapToEdges)
            {
                (masterWidth, masterHeight) = SnapResizeToEdges(_dragItem, masterWidth, masterHeight);
            }

            _dragItem.Width = masterWidth;
            _dragItem.Height = masterHeight;

            var widthDelta = masterWidth - _resizeOriginWidth;
            var heightDelta = masterHeight - _resizeOriginHeight;

            foreach (var item in ViewModel.GetSelectedItems().Where(item => !ReferenceEquals(item, _dragItem)))
            {
                if (!_resizeOrigins.TryGetValue(item, out var origin))
                {
                    continue;
                }

                item.Width = ViewModel.SnapLength(origin.Width + widthDelta, item.MinWidth, MaxCanvasWidth(item.X));
                item.Height = ViewModel.SnapLength(origin.Height + heightDelta, item.MinHeight, MaxCanvasHeight(item.Y));
            }
            return;
        }

        if (_selectionStart is null || !ViewModel.IsEditMode)
        {
            return;
        }

        ViewModel.UpdateSelection(position.X, position.Y);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (_dragOrigins.Count > 0 || _resizeStart is not null)
        {
            if (_dragOrigins.Count == 1 && _dragItem is not null)
            {
                var targetList = FindDropListTarget(_dragItem);
                if (targetList is not null)
                {
                    ViewModel.MoveItemIntoList(_dragItem, targetList);
                }
            }

            e.Pointer.Capture(null);
            _dragItem = null;
            _dragStart = null;
            _resizeStart = null;
            _dragOrigins.Clear();
            _resizeOrigins.Clear();
            return;
        }

        if (_selectionStart is null || !ViewModel.IsEditMode)
        {
            return;
        }

        var position = e.GetPosition(EditorCanvas);
        var shouldOpenPicker = ViewModel.FinishSelection(position.X, position.Y, _addToSelection);
        _selectionStart = null;
        _addToSelection = false;
        e.Pointer.Capture(null);

        if (shouldOpenPicker)
        {
            _ = ShowCanvasWidgetSelectionDialogAsync();
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderItemModel item } || ViewModel is null)
        {
            return;
        }

        // In View-Mode (nicht EditMode) duerfen keine Widgets
        // selektiert oder verschoben werden.
        if (!ViewModel.IsEditMode)
        {
            return;
        }

        // Wenn der Body-Interaktionsmodus aktiv ist, kein Drag/Resize,
        // sondern das Ereignis an den Widget-Body durchreichen.
        if (ViewModel.IsShiftInteractionMode)
        {
            return;
        }

        ViewModel.CancelSelection();

        var point = e.GetCurrentPoint(EditorCanvas);
        if (point.Properties.IsRightButtonPressed && item.IsWidgetList)
        {
            _ = ShowListWidgetSelectionDialogAsync(item);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.ToggleItemSelection(item);
            e.Handled = true;
            return;
        }

        if (ViewModel.IsItemSelected(item) && ViewModel.SelectedItemsCount > 1)
        {
            ViewModel.SetMasterItem(item);
        }
        else
        {
            ViewModel.SelectItem(item);
        }

        StartDragSelection(item, e.GetPosition(EditorCanvas));
        e.Pointer.Capture(EditorCanvas);
        e.Handled = true;
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderItemModel item } || ViewModel?.IsEditMode != true)
        {
            return;
        }

        ViewModel.CancelSelection();

        if (!ViewModel.IsItemSelected(item))
        {
            ViewModel.SelectItem(item);
        }
        else
        {
            ViewModel.SetMasterItem(item);
        }

        _dragOrigins.Clear();
        _resizeOrigins.Clear();
        foreach (var selected in ViewModel.GetSelectedItems())
        {
            _resizeOrigins[selected] = new Size(selected.Width, selected.Height);
        }

        _dragItem = item;
        _resizeStart = e.GetPosition(EditorCanvas);
        _resizeOriginWidth = item.Width;
        _resizeOriginHeight = item.Height;
        _dragStart = null;
        e.Pointer.Capture(EditorCanvas);
        e.Handled = true;
    }

    private void OnItemSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderItemModel item } || ViewModel is null)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var anchorTarget = owner as Visual ?? this;
        var localPoint = new Point(item.X + item.Width + 8, item.Y);
        var translated = EditorCanvas.TranslatePoint(localPoint, anchorTarget) ?? new Point(24, 24);

        ViewModel.OpenItemEditor(item, translated.X, translated.Y);
        e.Handled = true;
    }

    private async void OnItemDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderItemModel item } || ViewModel is null)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }

        var confirmed = await ShowDeleteDialogAsync(owner);
        if (!confirmed)
        {
            e.Handled = true;
            return;
        }

        ViewModel.DeleteItem(item);
        e.Handled = true;
    }

    private static async Task<bool> ShowDeleteDialogAsync(Window owner)
    {
        var dialog = new Window
        {
            Title = "Delete",
            Width = 320,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.Full
        };

        var text = new TextBlock
        {
            Text = "Really delete?",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var yesButton = new Button
        {
            Content = "Yes",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var noButton = new Button
        {
            Content = "No",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        yesButton.Click += (_, _) => dialog.Close(true);
        noButton.Click += (_, _) => dialog.Close(false);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { noButton, yesButton }
        };

        dialog.Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                text,
                buttonPanel
            }
        };

        Grid.SetRow(text, 0);
        Grid.SetRow(buttonPanel, 1);

        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnEditorCanvasSizeChanged(object? sender, SizeChangedEventArgs e) => ViewModel?.UpdateCanvasSize(e.NewSize.Width, e.NewSize.Height);

    private async Task ShowCanvasWidgetSelectionDialogAsync()
    {
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var widgets = CreateCanvasWidgetSelectionItems();
        var selected = await WidgetSelectionDialogWindow.ShowAsync(owner, ViewModel, widgets, "Select");
        if (selected is null)
        {
            ViewModel.CancelSelection();
            return;
        }

        ViewModel.BeginSelectionAdd(selected.Kind);
    }

    private async Task ShowListWidgetSelectionDialogAsync(FolderItemModel listControl)
    {
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        ViewModel.OpenListPopup(listControl, 0, 0);
        var widgets = CreateListWidgetSelectionItems();
        var selected = await WidgetSelectionDialogWindow.ShowAsync(owner, ViewModel, widgets, "Insert");
        if (selected is null)
        {
            ViewModel.CancelListPopup();
            return;
        }

        ViewModel.BeginListAdd(selected.Kind);
    }

    private IReadOnlyList<WidgetSelectionItem> CreateCanvasWidgetSelectionItems()
    {
        var items = new List<WidgetSelectionItem>
        {
            CreateWidgetSelectionItem(ControlKind.Button, "Button", "Action button with text or icon.", "Button.md", "Button.help.md"),
            CreateWidgetSelectionItem(ControlKind.Signal, "Signal", "Value display and parameter interaction widget.", "Signal.md", "Signal.help.md"),
            CreateWidgetSelectionItem(ControlKind.WidgetList, "WidgetList", "Vertical container with scrollable child widgets.", "WidgetList.md", "WidgetList.help.md"),
            CreateWidgetSelectionItem(ControlKind.TableControl, "WidgetTable", "Grid-based layout container for child widgets.", "TableControl.md", "TableControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.CircleDisplay, "CircleDisplay", "Circular matrix and progress display widget.", "CircleDisplay.md", "CircleDisplay.help.md"),
            CreateWidgetSelectionItem(ControlKind.LogControl, "Log", "Process log viewer with filter controls.", "LogControl.md", "LogControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.CsvLoggerControl, "CsvLogger", "CSV logger with runtime recording controls.", "CsvLoggerControl.md", "CsvLoggerControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.SqlLoggerControl, "SqlLogger", "SQL logger with runtime recording controls.", "SqlLoggerControl.md", "SqlLoggerControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.ChartControl, "RealtimeChart", "Live chart for numeric signal history.", "ChartControl.md", "ChartControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.CameraControl, "Camera", "Camera stream widget with snapshot support.", "CameraControl.md", "CameraControl.help.md"),
            CreateWidgetSelectionItem(ControlKind.BrokerWidget, "BrokerWidget", "MQTT ItemBroker connection and attach widget.", "BrokerWidget.md", "BrokerWidget.help.md"),
            CreateWidgetSelectionItem(ControlKind.ApplicationExplorer, "ApplicationExplorer", "Application launcher and runtime overview.", "ApplicationExplorer.md", "ApplicationExplorer.help.md"),
            CreateWidgetSelectionItem(ControlKind.CustomSignals, "CustomSignals", "Calculated and manual custom signal definitions.", "CustomSignals.md", "CustomSignals.help.md"),
            CreateWidgetSelectionItem(ControlKind.EnhancedSignals, "EnhancedSignals", "Extended signal processing and mapping widget.", "EnhancedSignals.md", "EnhancedSignals.help.md")
        };

        if (ViewModel?.SupportsUdlClientControl == true)
        {
            items.Insert(10, CreateWidgetSelectionItem(ControlKind.UdlClientControl, "UdlClient", "UDL client connection and attach widget.", "UdlClientControl.md", "UdlClientControl.help.md"));
        }

        return items;
    }

    private static IReadOnlyList<WidgetSelectionItem> CreateListWidgetSelectionItems()
    {
        return
        [
            CreateWidgetSelectionItem(ControlKind.Button, "Button", "Action button as list entry.", "Button.md", "Button.help.md", configureListChild: true),
            CreateWidgetSelectionItem(ControlKind.Signal, "Signal", "Signal value widget as list entry.", "Signal.md", "Signal.help.md", configureListChild: true)
        ];
    }

    private static WidgetSelectionItem CreateWidgetSelectionItem(ControlKind kind, string displayName, string summary, string descriptionFileName, string helpFileName, bool configureListChild = false)
    {
        return new WidgetSelectionItem(
            kind,
            displayName,
            summary,
            descriptionFileName,
            helpFileName,
            viewModel => CreatePreviewItem(viewModel, kind, configureListChild));
    }

    private static FolderItemModel? CreatePreviewItem(MainWindowViewModel? viewModel, ControlKind kind, bool configureListChild)
    {
        viewModel ??= new MainWindowViewModel();

        if (kind is ControlKind.Signal or ControlKind.ItemModel)
        {
            return CreateSignalPreviewHost(viewModel);
        }

        var preview = viewModel.CreateItem(kind, 0, 0, GetPreviewWidth(kind, configureListChild), GetPreviewHeight(kind, configureListChild));
        preview.Name = $"Preview{kind}";
        preview.Id = Guid.NewGuid().ToString("N");
        preview.SetLayoutFilePath(string.Empty);
        preview.SetHierarchy("Preview", null);
        ApplyPreviewPreset(preview, kind);

        if (configureListChild)
        {
            return CreateListChildPreviewHost(viewModel, preview);
        }

        if (kind == ControlKind.WidgetList)
        {
            PopulateListPreview(viewModel, preview);
        }
        else if (kind == ControlKind.CircleDisplay)
        {
            PopulateCircleDisplayPreview(viewModel, preview);
        }
        else if (kind == ControlKind.TableControl)
        {
            PopulateTablePreview(viewModel, preview);
        }

        preview.ApplyTheme(viewModel.IsDarkTheme);
        return preview;
    }

    private static FolderItemModel CreateSignalPreviewHost(MainWindowViewModel viewModel)
    {
        var host = viewModel.CreateItem(kind: ControlKind.TableControl, x: 0, y: 0, width: 640, height: 176);
        host.Name = "PreviewSignalVariants";
        host.Id = Guid.NewGuid().ToString("N");
        host.SetLayoutFilePath(string.Empty);
        host.SetHierarchy("Preview", null);
        host.SyncText = false;
        host.ControlCaption = "Signal variants";
        host.BodyCaption = "Numeric, bool, bitmask, text, and hex";
        host.Footer = "Five common signal modes";
        host.ShowFooter = true;
        host.BorderWidth = 0;
        host.HeaderBorderWidth = 0;
        host.BodyBorderWidth = 0;
        host.FooterBorderWidth = 0;
        host.TableRows = 2;
        host.TableColumns = 3;
        host.Items.Clear();

        var numeric = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewTemperature",
            headerCaption: "Temperature",
            bodyCaption: "Value",
            value: "23.4",
            unit: "°C",
            format: "numeric:0.0",
            width: 180,
            height: 64);
        numeric.TableCellRow = 1;
        numeric.TableCellColumn = 1;

        var boolean = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewPumpState",
            headerCaption: "Pump enabled",
            bodyCaption: "State",
            value: "true",
            unit: string.Empty,
            format: "bool:Off,On",
            width: 180,
            height: 64);
        boolean.TableCellRow = 1;
        boolean.TableCellColumn = 2;

        var bitmask = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewInputs",
            headerCaption: "Inputs",
            bodyCaption: "DI",
            value: "5",
            unit: string.Empty,
            format: "b4",
            width: 180,
            height: 64);

        bitmask.TableCellRow = 1;
        bitmask.TableCellColumn = 3;

        var text = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewSerialText",
            headerCaption: "Serial",
            bodyCaption: "Text",
            value: "AX-2048",
            unit: string.Empty,
            format: string.Empty,
            width: 180,
            height: 64);
        text.TableCellRow = 2;
        text.TableCellColumn = 1;

        var hex = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewStatusWord",
            headerCaption: "Status word",
           
            bodyCaption: "Hex",
            value: "255",
            unit: string.Empty,
            format: "hex:4",
            width: 180,
            height: 64);
        hex.TableCellRow = 2;
        hex.TableCellColumn = 2;
        hex.HeaderBackColor = "#FF1F92";

        foreach (var child in new[] { numeric, boolean, bitmask, text, hex })
        {
            child.SetHierarchy("Preview", host);
            child.ApplyTheme(viewModel.IsDarkTheme);
            host.Items.Add(child);
        }

        host.UpdateTableCellContentFromChildren();
        host.ApplyTheme(viewModel.IsDarkTheme);
        return host;
    }

    private static FolderItemModel CreateListChildPreviewHost(MainWindowViewModel viewModel, FolderItemModel childPreview)
    {
        var listHost = viewModel.CreateItem(ControlKind.WidgetList, 0, 0, 320, 220);
        listHost.Name = "PreviewList";
        listHost.Id = Guid.NewGuid().ToString("N");
        listHost.SetLayoutFilePath(string.Empty);
        listHost.SetHierarchy("Preview", null);
        listHost.ControlCaption = "Signal list";
        listHost.BodyCaption = "Operator favorites";
        listHost.Footer = "Scroll for more";
        listHost.ShowFooter = true;
        listHost.ListItemHeight = Math.Max(72, childPreview.Height);
        listHost.ApplyWidgetListDefaultsToChild(childPreview);
        childPreview.SetHierarchy("Preview", listHost);
        listHost.Items.Add(childPreview);

        var secondaryItem = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewListFlow",
            headerCaption: "Flow",
            bodyCaption: "Value",
            value: "128",
            unit: "l/min",
            format: "numeric:0");
        listHost.ApplyWidgetListDefaultsToChild(secondaryItem);
        secondaryItem.SetHierarchy("Preview", listHost);
        listHost.Items.Add(secondaryItem);

        listHost.ApplyTheme(viewModel.IsDarkTheme);
        foreach (var item in listHost.Items)
        {
            item.ApplyTheme(viewModel.IsDarkTheme);
        }

        return listHost;
    }

    private static void PopulateListPreview(MainWindowViewModel viewModel, FolderItemModel listPreview)
    {
        listPreview.ControlCaption = "Signal list";
        listPreview.BodyCaption = "Machine overview";
        listPreview.Footer = "3 live widgets";
        listPreview.ShowFooter = true;
        listPreview.IsAutoHeight = false;
        listPreview.ListItemHeight = 72;
        listPreview.Items.Clear();

        var temperature = CreatePreviewChild(viewModel, ControlKind.Signal, "PreviewTemperature", "Temperature", "Value", "23.4", "°C", "numeric:0.0");
        var pressure = CreatePreviewChild(viewModel, ControlKind.Signal, "PreviewPressure", "Pressure", "Value", "6.2", "bar", "numeric:0.0");
        var startButton = CreatePreviewChild(viewModel, ControlKind.Button, "PreviewStart", "Action", "Start line", string.Empty, string.Empty, string.Empty);
        startButton.ButtonText = "Start line";
        startButton.BodyCaption = "Start line";
        startButton.ShowFooter = false;

        foreach (var child in new[] { temperature, pressure, startButton })
        {
            listPreview.ApplyWidgetListDefaultsToChild(child);
            child.SetHierarchy("Preview", listPreview);
            child.ApplyTheme(viewModel.IsDarkTheme);
            listPreview.Items.Add(child);
        }
    }

    private static void PopulateTablePreview(MainWindowViewModel viewModel, FolderItemModel tablePreview)
    {
        tablePreview.ControlCaption = "Widget table";
        tablePreview.BodyCaption = "Production cells";
        tablePreview.Footer = "Signals, action, and chart";
        tablePreview.ShowFooter = true;
        tablePreview.TableRows = 4;
        tablePreview.TableColumns = 2;
        tablePreview.Items.Clear();

        var runtimeSignal = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewRuntime",
            headerCaption: "Runtime",
            bodyCaption: "Value",
            value: "8.7",
            unit: "h",
            format: "numeric:0.0");
        runtimeSignal.TableCellRow = 1;
        runtimeSignal.TableCellColumn = 1;

        var statusSignal = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewStatus",
            headerCaption: "Status",
            bodyCaption: "State",
            value: "true",
            unit: string.Empty,
            format: "bool:Idle,Ready");
        statusSignal.TableCellRow = 1;
        statusSignal.TableCellColumn = 2;

        var startButton = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Button,
            name: "PreviewCycleStart",
            headerCaption: "Action",
            bodyCaption: "Start cycle",
            value: string.Empty,
            unit: string.Empty,
            format: string.Empty);
        startButton.ButtonText = "Start cycle";
        startButton.BodyCaption = "Start cycle";
        startButton.ShowFooter = false;
        startButton.TableCellRow = 2;
        startButton.TableCellColumn = 1;
        startButton.TableCellColumnSpan = 2;

        var chart = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.ChartControl,
            name: "PreviewTableChart",
            headerCaption: "Trend",
            bodyCaption: "RealtimeChart",
            value: string.Empty,
            unit: string.Empty,
            format: string.Empty,
            width: 320,
            height: 120);
        chart.TableCellRow = 3;
        chart.TableCellColumn = 1;
        chart.TableCellRowSpan = 2;
        chart.TableCellColumnSpan = 2;

        foreach (var child in new[] { runtimeSignal, statusSignal, startButton, chart })
        {
            child.SetHierarchy("Preview", tablePreview);
            child.ApplyTheme(viewModel.IsDarkTheme);
            tablePreview.Items.Add(child);
        }

        tablePreview.UpdateTableCellContentFromChildren();
    }

    private static void PopulateCircleDisplayPreview(MainWindowViewModel viewModel, FolderItemModel circlePreview)
    {
        circlePreview.ControlCaption = "Circle display";
        circlePreview.BodyCaption = "Motor load";
        circlePreview.Footer = "Signal + progress + controls";
        circlePreview.ShowFooter = true;
        circlePreview.ProgressBar = true;
        circlePreview.ProgressState = 74;
        circlePreview.SignalRun = true;
        circlePreview.SignalColor = "#22C55E";
        circlePreview.ProgressBarColor = "#0EA5E9";
        circlePreview.TableRows = 9;
        circlePreview.TableColumns = 9;
        circlePreview.Items.Clear();

        var temperatureSignal = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewTemperature",
            headerCaption: "Temperature",
            bodyCaption: "Value",
            value: "23.4",
            unit: "°C",
            format: "numeric:0.0",
            width: 120,
            height: 56);
        temperatureSignal.TableCellRow = 4;
        temperatureSignal.TableCellColumn = 3;
        temperatureSignal.TableCellRowSpan = 2;
        temperatureSignal.TableCellColumnSpan = 5;
        temperatureSignal.ShowBodyCaption = false;

        var stateSignal = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Signal,
            name: "PreviewCircleState",
            headerCaption: "Pump",
            bodyCaption: "State",
            value: "true",
            unit: string.Empty,
            format: "bool:Off,On",
            width: 96,
            height: 56);
        stateSignal.TableCellRow = 2;
        stateSignal.TableCellColumn = 3;
        stateSignal.TableCellRowSpan = 2;
        stateSignal.TableCellColumnSpan = 5;

        var commandButton = CreatePreviewChild(
            viewModel: viewModel,
            kind: ControlKind.Button,
            name: "PreviewCircleStart",
            headerCaption: "Action",
            bodyCaption: "Start",
            value: string.Empty,
            unit: string.Empty,
            format: string.Empty,
            width: 104,
            height: 48);
        commandButton.TableCellRow = 7;
        commandButton.TableCellColumn = 3;
        commandButton.TableCellRowSpan = 2;
        commandButton.TableCellColumnSpan = 5;
        commandButton.ButtonOnlyIcon = true;
        commandButton.ButtonIcon = "avares://HornetStudio.Editor/EditorIcons/play.svg";

        foreach (var child in new[] { temperatureSignal, stateSignal, commandButton })
        {
            child.SetHierarchy("Preview", circlePreview);
            child.ApplyTheme(viewModel.IsDarkTheme);
            circlePreview.Items.Add(child);
        }

        circlePreview.UpdateTableCellContentFromChildren();
    }

    private static FolderItemModel CreatePreviewChild(
        MainWindowViewModel viewModel,
        ControlKind kind,
        string name,
        string headerCaption,
        string bodyCaption,
        string value,
        string unit,
        string format,
        double width = 200,
        double height = 72)
    {
        var child = viewModel.CreateItem(kind, 0, 0, width, height);
        child.Name = name;
        child.Id = Guid.NewGuid().ToString("N");
        child.SetLayoutFilePath(string.Empty);
        child.SetHierarchy("Preview", null);
        ApplyPreviewPreset(child, kind, headerCaption, bodyCaption, value, unit, format);
        return child;
    }

    private static void ApplyPreviewPreset(
        FolderItemModel item,
        ControlKind kind,
        string? headerCaptionOverride = null,
        string? bodyCaptionOverride = null,
        string? valueOverride = null,
        string? unitOverride = null,
        string? formatOverride = null)
    {
        switch (kind)
        {
            case ControlKind.Button:
                item.SyncText = false;
                item.ControlCaption = "Action";
                item.BodyCaption = bodyCaptionOverride ?? "Start line";
                item.Title = item.BodyCaption;
                item.ButtonText = bodyCaptionOverride ?? "Start line";
                item.Footer = "Manual command";
                item.ShowFooter = false;
                item.ShowBodyCaption = false;
                item.ShowCaption = false;
                break;

            case ControlKind.Signal:
                item.SyncText = false;
                item.ControlCaption = headerCaptionOverride ?? "Temperature";
                item.BodyCaption = bodyCaptionOverride ?? "Value";
                item.Title = valueOverride ?? "23.4";
                item.Unit = unitOverride ?? "°C";
                item.TargetPropertyFormat = string.IsNullOrWhiteSpace(formatOverride) ? "numeric:0.00" : formatOverride;
                item.ShowFooter = false;
                item.BodyCaptionVisible = true;
                break;

            case ControlKind.ItemModel:
                item.SyncText = false;
                item.ControlCaption = headerCaptionOverride ?? "Temperature";
                item.BodyCaption = bodyCaptionOverride ?? "Value";
                item.Title = valueOverride ?? "23.4";
                item.Unit = unitOverride ?? "°C";
                item.TargetPropertyFormat = string.IsNullOrWhiteSpace(formatOverride) ? "numeric:0.00" : formatOverride;
                item.ShowFooter = false;
                item.BodyCaptionVisible = true;
                break;

            case ControlKind.WidgetList:
                item.ControlCaption = "Signal list";
                item.BodyCaption = "Machine overview";
                item.Footer = "Scrollable list";
                item.ShowFooter = true;
                break;

            case ControlKind.TableControl:
                item.ControlCaption = "Widget table";
                item.BodyCaption = "Production cells";
                item.Footer = "Structured layout";
                item.ShowFooter = true;
                break;

            case ControlKind.CircleDisplay:
                item.ControlCaption = "Circle display";
                item.BodyCaption = "Motor load";
                item.Footer = "74 % progress";
                item.ShowFooter = true;
                item.ProgressBar = true;
                item.ProgressState = 74;
                item.SignalRun = true;
                item.SignalColor = "#FFC107";
                item.ProgressBarColor = "#0E21E9";
                break;

            case ControlKind.LogControl:
                item.ControlCaption = "Log";
                item.BodyCaption = "Runtime messages";
                item.Footer = "Info | Warning | Error";
                item.ShowFooter = true;
                item.TargetLog = "Logs.Host";
                break;

            case ControlKind.ChartControl:
                item.ControlCaption = "Trend";
                item.BodyCaption = "RealtimeChart";
                item.Footer = "3 series | 30 s";
                item.ShowFooter = true;
                break;

            case ControlKind.CsvLoggerControl:
                item.Title = "CSV Logger";
                item.ControlCaption = "Csv logger";
                item.BodyCaption = "production_log.csv";
                item.Footer = "3 signals | 1000 ms";
                item.ShowFooter = true;
                break;

            case ControlKind.SqlLoggerControl:
                item.Title = "SQL Logger";
                item.ControlCaption = "Sql logger";
                item.BodyCaption = "production_log.db";
                item.Footer = "3 signals | buffered";
                item.ShowFooter = true;
                break;

            case ControlKind.CameraControl:
                item.ControlCaption = "Camera";
                item.BodyCaption = "Line camera";
                item.Footer = "1920x1080 | Snapshot";
                item.ShowFooter = true;
                break;

            case ControlKind.UdlClientControl:
                item.ControlCaption = "UDL client";
                item.BodyCaption = "Connected endpoint";
                item.Footer = "192.168.178.151:9001";
                item.ShowFooter = true;
                break;

            case ControlKind.BrokerWidget:
                item.ControlCaption = "Broker";
                item.BodyCaption = "MQTT ItemBroker";
                item.Footer = "127.0.0.1:1883 | hornet";
                item.ShowFooter = true;
                break;

            case ControlKind.ApplicationExplorer:
                item.ControlCaption = "Application explorer";
                item.BodyCaption = "Utilities";
                item.Footer = "2 configured applications";
                item.ShowFooter = true;
                break;

            case ControlKind.CustomSignals:
                item.ControlCaption = "Custom signals";
                item.BodyCaption = "Derived values";
                item.Footer = "3 custom signals";
                item.ShowFooter = true;
                break;

            case ControlKind.EnhancedSignals:
                item.ControlCaption = "Enhanced signals";
                item.BodyCaption = "Mapped values";
                item.Footer = "2 transformed signals";
                item.ShowFooter = true;
                break;
        }
    }

    private static double GetPreviewWidth(ControlKind kind, bool configureListChild)
    {
        if (configureListChild)
        {
            return kind == ControlKind.Button ? 236 : 220;
        }

        return kind switch
        {
            ControlKind.Button => 260,
            ControlKind.Signal or ControlKind.ItemModel => 220,
            ControlKind.WidgetList => 340,
            ControlKind.TableControl => 340,
            ControlKind.CircleDisplay => 300,
            ControlKind.LogControl => 420,
            ControlKind.ChartControl => 460,
            ControlKind.CsvLoggerControl or ControlKind.SqlLoggerControl => 320,
            ControlKind.CameraControl => 340,
            ControlKind.UdlClientControl => 420,
            ControlKind.BrokerWidget => 420,
            ControlKind.ApplicationExplorer => 420,
            ControlKind.CustomSignals or ControlKind.EnhancedSignals => 420,
            _ => 260
        };
    }

    private static double GetPreviewHeight(ControlKind kind, bool configureListChild)
    {
        if (configureListChild)
        {
            return kind == ControlKind.Button ? 56 : 72;
        }

        return kind switch
        {
            ControlKind.Button => 72,
            ControlKind.Signal or ControlKind.ItemModel => 96,
            ControlKind.WidgetList => 280,
            ControlKind.TableControl => 260,
            ControlKind.CircleDisplay => 300,
            ControlKind.LogControl => 260,
            ControlKind.ChartControl => 260,
            ControlKind.CsvLoggerControl or ControlKind.SqlLoggerControl => 150,
            ControlKind.CameraControl => 220,
            ControlKind.UdlClientControl => 190,
            ControlKind.BrokerWidget => 190,
            ControlKind.ApplicationExplorer => 220,
            ControlKind.CustomSignals or ControlKind.EnhancedSignals => 240,
            _ => 120
        };
    }

    private void StartDragSelection(FolderItemModel anchorItem, Point startPosition)
    {
        var items = ViewModel?.GetSelectedItems() ?? [];
        if (items.Count == 0)
        {
            items = [anchorItem];
        }

        _dragOrigins.Clear();
        foreach (var item in items)
        {
            _dragOrigins[item] = new Point(item.X, item.Y);
        }

        _dragItem = anchorItem;
        _dragStart = startPosition;
        _resizeStart = null;
        _dragGroupMinX = items.Min(item => item.X);
        _dragGroupMinY = items.Min(item => item.Y);
        _dragGroupWidth = items.Max(item => item.X + item.Width) - _dragGroupMinX;
        _dragGroupHeight = items.Max(item => item.Y + item.Height) - _dragGroupMinY;
    }

    private FolderItemModel? FindDropListTarget(FolderItemModel draggedItem)
    {
        var centerX = draggedItem.X + draggedItem.Width / 2;
        var centerY = draggedItem.Y + draggedItem.Height / 2;

        return CurrentFolder?.Items
            .Where(item => item.IsWidgetList && !ReferenceEquals(item, draggedItem))
            .FirstOrDefault(item => centerX >= item.X && centerX <= item.X + item.Width && centerY >= item.Y && centerY <= item.Y + item.Height);
    }
    private (double X, double Y) SnapGroupToEdges(double x, double y, double width, double height)
    {
        var snappedX = x;
        var snappedY = y;
        var right = x + width;
        var bottom = y + height;
        var horizontalCandidates = new List<double> { 0, EditorCanvas.Bounds.Width };
        var verticalCandidates = new List<double> { 0, EditorCanvas.Bounds.Height };

        foreach (var item in CurrentFolder?.Items ?? [])
        {
            if (_dragOrigins.ContainsKey(item))
            {
                continue;
            }

            horizontalCandidates.Add(item.X);
            horizontalCandidates.Add(item.X + item.Width);
            verticalCandidates.Add(item.Y);
            verticalCandidates.Add(item.Y + item.Height);
        }

        foreach (var candidate in horizontalCandidates)
        {
            if (System.Math.Abs(x - candidate) <= EdgeSnapDistance)
            {
                snappedX = candidate;
            }
            else if (System.Math.Abs(right - candidate) <= EdgeSnapDistance)
            {
                snappedX = candidate - width;
            }
        }

        foreach (var candidate in verticalCandidates)
        {
            if (System.Math.Abs(y - candidate) <= EdgeSnapDistance)
            {
                snappedY = candidate;
            }
            else if (System.Math.Abs(bottom - candidate) <= EdgeSnapDistance)
            {
                snappedY = candidate - height;
            }
        }

        return (Clamp(snappedX, 0, MaxCanvasX(width)), Clamp(snappedY, 0, MaxCanvasY(height)));
    }

    private (double Width, double Height) SnapResizeToEdges(FolderItemModel item, double width, double height)
    {
        var snappedWidth = width;
        var snappedHeight = height;
        var right = item.X + width;
        var bottom = item.Y + height;
        var horizontalCandidates = new List<double> { EditorCanvas.Bounds.Width };
        var verticalCandidates = new List<double> { EditorCanvas.Bounds.Height };

        foreach (var other in CurrentFolder?.Items ?? [])
        {
            if (ReferenceEquals(other, item))
            {
                continue;
            }

            horizontalCandidates.Add(other.X);
            horizontalCandidates.Add(other.X + other.Width);
            verticalCandidates.Add(other.Y);
            verticalCandidates.Add(other.Y + other.Height);
        }

        foreach (var candidate in horizontalCandidates)
        {
            if (System.Math.Abs(right - candidate) <= EdgeSnapDistance)
            {
                snappedWidth = candidate - item.X;
            }
        }

        foreach (var candidate in verticalCandidates)
        {
            if (System.Math.Abs(bottom - candidate) <= EdgeSnapDistance)
            {
                snappedHeight = candidate - item.Y;
            }
        }

        snappedWidth = ViewModel?.SnapLength(snappedWidth, item.MinWidth, MaxCanvasWidth(item.X)) ?? snappedWidth;
        snappedHeight = ViewModel?.SnapLength(snappedHeight, item.MinHeight, MaxCanvasHeight(item.Y)) ?? snappedHeight;
        return (snappedWidth, snappedHeight);
    }

    private double MaxCanvasX(double width) => System.Math.Max(0, EditorCanvas.Bounds.Width - width);
    private double MaxCanvasY(double height) => System.Math.Max(0, EditorCanvas.Bounds.Height - height);
    private double MaxCanvasWidth(double x) => System.Math.Max(150, EditorCanvas.Bounds.Width - x);
    private double MaxCanvasHeight(double y) => System.Math.Max(72, EditorCanvas.Bounds.Height - y);

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            max = min;
        }

        return System.Math.Max(min, System.Math.Min(max, value));
    }
}

public partial class FolderEditorWidget : FolderEditorControl
{
}




