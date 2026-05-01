using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Controls;

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
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            CloseEditorDialogWindow();
            AttachToViewModel(null);
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

    private FolderModel? CurrentPage => Folder ?? ViewModel?.SelectedFolder;

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

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.CancelSelection();
        ViewModel.CancelEditorDialog();
        ViewModel.CancelValueInput();

        if (!ViewModel.IsEditMode)
        {
            ViewModel.ClearItemSelection();
            return;
        }

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
        ViewModel.FinishSelection(position.X, position.Y, _addToSelection);
        _selectionStart = null;
        _addToSelection = false;
        e.Pointer.Capture(null);
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderItemModel item } || ViewModel is null)
        {
            return;
        }

        ViewModel.CancelSelection();

        var point = e.GetCurrentPoint(EditorCanvas);
        if (point.Properties.IsRightButtonPressed && item.IsWidgetList)
        {
            var position = e.GetPosition(EditorCanvas);
            ViewModel.OpenListPopup(item, position.X + 8, position.Y + 8);
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

        if (ViewModel.IsEditMode)
        {
            StartDragSelection(item, e.GetPosition(EditorCanvas));
            e.Pointer.Capture(EditorCanvas);
        }

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

    private void OnBeginAddButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.Button);
    private void OnBeginAddSignalClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.Signal);
    private void OnBeginAddWidgetListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.WidgetList);
    private void OnBeginAddCircleDisplayClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.CircleDisplay);
    private void OnBeginAddLogControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.LogControl);
    private void OnBeginAddCsvLoggerControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.CsvLoggerControl);
    private void OnBeginAddSqlLoggerControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.SqlLoggerControl);
    private void OnBeginAddChartControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.ChartControl);
    private void OnBeginAddCameraControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.CameraControl);
    private void OnBeginAddUdlClientControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.UdlClientControl);
    private void OnBeginAddBrokerWidgetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.BrokerWidget);
    private void OnBeginAddCustomSignalsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.CustomSignals);
    private void OnBeginAddEnhancedSignalsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginSelectionAdd(ControlKind.EnhancedSignals);
    private void OnBeginAddButtonToListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginListAdd(ControlKind.Button);
    private void OnBeginAddSignalToListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.BeginListAdd(ControlKind.Signal);
    private void OnCancelSelectionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.CancelSelection();
    private void OnCancelListSelectionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.CancelListPopup();
    private void OnEditorCanvasSizeChanged(object? sender, SizeChangedEventArgs e) => ViewModel?.UpdateCanvasSize(e.NewSize.Width, e.NewSize.Height);

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

        return CurrentPage?.Items
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

        foreach (var item in CurrentPage?.Items ?? [])
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

        foreach (var other in CurrentPage?.Items ?? [])
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




