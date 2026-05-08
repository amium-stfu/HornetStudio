using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using TableCellSlot = HornetStudio.Editor.Models.FolderItemModel.TableCellSlot;

namespace HornetStudio.Editor.Widgets;

public partial class EditorTableControl : EditorTemplateWidget
{
    private Grid? _tableRoot;
    private Grid? _itemContentGrid;
    private Grid? _itemOverlayGrid;
    private INotifyCollectionChanged? _itemsCollection;
    private bool _isSelecting;
    private int _selectionAnchorRow;
    private int _selectionAnchorColumn;
    private int _selectionCurrentRow;
    private int _selectionCurrentColumn;
    private bool _hasRangeAnchor;
    private int _rangeAnchorRow;
    private int _rangeAnchorColumn;
    private Border? _selectionOverlay;
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

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel } ? viewModel : null;

    public EditorTableControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _tableRoot = this.FindControl<Grid>("TableRoot");
        _itemContentGrid = this.FindControl<Grid>("ItemContentGrid");
        _itemOverlayGrid = this.FindControl<Grid>("ItemOverlayGrid");
        _selectionOverlay = this.FindControl<Border>("SelectionOverlay");
        HookItemsCollection();
        RefreshItemOverlay();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookItemsCollection();
        if (_selectionOverlay is not null)
        {
            _selectionOverlay.IsVisible = false;
        }
        _tableRoot = null;
        _itemContentGrid = null;
        _itemOverlayGrid = null;
        _selectionOverlay = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookItemsCollection();
        RefreshItemOverlay();
    }

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

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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

        for (var r = 0; r < Item.TableRows; r++)
        {
            _itemContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _itemOverlayGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        for (var c = 0; c < Item.TableColumns; c++)
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

            // Inhaltliches Widget (immer sichtbar, nicht interaktiv) –
            // gleiche spezialisierten Controls wie im FolderEditor verwenden.
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
                // Fallback: nicht erwarteter Typ – ueberspringen.
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

            // Overlay fuer Edit-Controls (nur im EditMode sichtbar/aktiv)
            var overlay = new Grid
            {
                DataContext = child,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            overlay.Bind(IsVisibleProperty, new Binding(nameof(FolderItemModel.IsVisibleInActiveView)));

            var moveSurface = new Border
            {
                Background = Avalonia.Media.Brushes.Transparent,
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
                Background = Avalonia.Media.Brushes.Orange,
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
                Background = Avalonia.Media.Brushes.Transparent,
                BorderBrush = Avalonia.Media.Brushes.Transparent,
                Tag = child
            };
            settingsButton.Classes.Add("toolicon");
            ToolTip.SetTip(settingsButton, "Settings");
            settingsButton.Click += OnChildSettingsClicked;

            var settingsIcon = new ThemeSvgIcon
            {
                Width = 10,
                Height = 10,
                IconPath = "avares://HornetStudio.Editor/EditorIcons/cog.svg"
            };
            settingsButton.Content = settingsIcon;

            var deleteButton = new Button
            {
                Width = 16,
                Height = 16,
                MinWidth = 16,
                Padding = new Thickness(0),
                Background = Avalonia.Media.Brushes.Transparent,
                BorderBrush = Avalonia.Media.Brushes.Transparent,
                Tag = child
            };
            deleteButton.Classes.Add("toolicon");
            ToolTip.SetTip(deleteButton, "Delete");
            deleteButton.Click += OnChildDeleteClicked;

            var deleteIcon = new ThemeSvgIcon
            {
                Width = 10,
                Height = 10,
                IconPath = "avares://HornetStudio.Editor/EditorIcons/remove.svg"
            };
            deleteButton.Content = deleteIcon;

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
                Background = Avalonia.Media.Brushes.Orange,
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = child,
                Opacity = 0.95
            };
            resizeGrip.PointerPressed += OnChildResizePointerPressed;
            overlay.Children.Add(resizeGrip);

            // Overlay nur anzeigen, wenn das Item selektiert ist.
            overlay.Bind(IsVisibleProperty, new Avalonia.Data.Binding("IsSelected"));

            Grid.SetRow(overlay, row);
            Grid.SetColumn(overlay, column);
            Grid.SetRowSpan(overlay, rowSpan);
            Grid.SetColumnSpan(overlay, columnSpan);

            _itemOverlayGrid.Children.Add(overlay);
        }
    }

    private void OnAddItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
            // Oeffnet das Kontextmenue zur Auswahl des Control-Typs.
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

        if (sender is not Control control || control.DataContext is not TableCellSlot slot)
        {
            return;
        }
        var point = e.GetCurrentPoint(control);

        // Klick auf eine Zelle mit Child-Item: nur das Widget ist selektierbar, nicht die Zelle.
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
            // Rechtsklick: Item fuer aktuelle Auswahl (oder nur diese Zelle) anlegen.
            ViewModel.ToggleTableCellSelection(Item, slot.Row, slot.Column, toggle: false);
            ViewModel.AddItemToSelectedTableCells(Item);
            RefreshItemOverlay();
            e.Handled = true;
            return;
        }
        var modifiers = e.KeyModifiers;

        // STRG + Linksklick: rechteckige Bereichsauswahl (Startpunkt -> Endpunkt).
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
            e.Pointer.Capture(_tableRoot);
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

        if (!TryGetTableSlotFromPoint(e.GetPosition(control), requireFree: true, out var slot))
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
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode || _tableRoot is null)
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

        if (!TryGetTableSlotFromPoint(e.GetPosition(_tableRoot), requireFree: false, out var slot))
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
        _previousCursor = _tableRoot.Cursor;
        _tableRoot.Cursor = new Cursor(StandardCursorType.Hand);
        e.Pointer.Capture(_tableRoot);
        UpdateSelectionOverlay(child.TableCellRow, child.TableCellColumn, child.TableCellRow + child.TableCellRowSpan - 1, child.TableCellColumn + child.TableCellColumnSpan - 1);
        e.Handled = true;
    }

    private void OnChildResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode || _tableRoot is null)
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
        _previousCursor = _tableRoot.Cursor;
        _tableRoot.Cursor = new Cursor(StandardCursorType.Hand);
        e.Pointer.Capture(_tableRoot);
        UpdateSelectionOverlay(child.TableCellRow, child.TableCellColumn, child.TableCellRow + child.TableCellRowSpan - 1, child.TableCellColumn + child.TableCellColumnSpan - 1);
        e.Handled = true;
    }

    private void HandleChildPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeChildItem is null || ViewModel is null || Item is null || _tableRoot is null)
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

        if (!TryGetTableSlotFromPoint(e.GetPosition(control), requireFree: false, out var slot))
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

        if (_tableRoot is not null)
        {
            _tableRoot.Cursor = _previousCursor;
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

        if (_tableRoot is not null)
        {
            _tableRoot.Cursor = _previousCursor;
        }

        if (_selectionOverlay is not null)
        {
            _selectionOverlay.IsVisible = false;
        }
    }

    private bool TryGetTableSlotFromPoint(Point point, bool requireFree, out TableCellSlot slot)
    {
        slot = null!;
        if (_tableRoot is null || Item is null)
        {
            return false;
        }

        var bounds = _tableRoot.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var normalizedX = Math.Clamp(point.X, 0, Math.Max(0, bounds.Width - 1));
        var normalizedY = Math.Clamp(point.Y, 0, Math.Max(0, bounds.Height - 1));
        var column = Math.Min(Item.TableColumns, Math.Max(1, (int)(normalizedX / (bounds.Width / Math.Max(1, Item.TableColumns))) + 1));
        var row = Math.Min(Item.TableRows, Math.Max(1, (int)(normalizedY / (bounds.Height / Math.Max(1, Item.TableRows))) + 1));
        var resolved = Item.TableCellSlots.FirstOrDefault(c => c.Row == row && c.Column == column);
        if (resolved is null)
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

    // STRG-Range-Selektion: erster Klick setzt Anker, zweiter Klick selektiert das Rechteck.
    private void HandleCtrlRangeClick(TableCellSlot slot)
    {
        if (ViewModel is null || Item is null || !ViewModel.IsEditMode)
        {
            return;
        }

        // Auf belegten Zellen keine Rechteckauswahl starten.
        if (slot.ChildItem is not null)
        {
            return;
        }

        if (!_hasRangeAnchor)
        {
            _rangeAnchorRow = slot.Row;
            _rangeAnchorColumn = slot.Column;
            _hasRangeAnchor = true;

            // Ankerzelle sichtbar machen.
            ViewModel.SelectTableRectangle(Item, slot.Row, slot.Column, slot.Row, slot.Column);
            return;
        }

        // Zweiter STRG-Klick: komplettes Rechteck selektieren.
        ViewModel.SelectTableRectangle(Item, _rangeAnchorRow, _rangeAnchorColumn, slot.Row, slot.Column);
        _hasRangeAnchor = false;
    }

    private void UpdateSelectionOverlay(int startRow, int startColumn, int endRow, int endColumn)
    {
        if (_selectionOverlay is null || _tableRoot is null || Item is null)
        {
            return;
        }

        var minRow = Math.Min(startRow, endRow);
        var maxRow = Math.Max(startRow, endRow);
        var minColumn = Math.Min(startColumn, endColumn);
        var maxColumn = Math.Max(startColumn, endColumn);

        var rootBounds = _tableRoot.Bounds;
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
        AddControlToTable(ControlKind.ItemModel);
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

        // Editor-Dialog fuer das Child-Item oeffnen; Position ist hier nachrangig.
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

