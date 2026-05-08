using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Item.Server.Monitor.ViewModels;

namespace Item.Server.Monitor;

public partial class MainWindow : Window
{
    private MonitorTreeRowViewModel? _hoveredRow;

    public MainWindow()
    {
        InitializeComponent();
        if (this.FindControl<ItemsControl>("MonitorTree") is { } monitorTree)
        {
            monitorTree.PointerMoved += OnMonitorTreePointerMoved;
            monitorTree.PointerExited += OnMonitorTreePointerExited;
            monitorTree.PointerPressed += OnMonitorTreePointerPressed;
        }

        Closed += OnClosed;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnMonitorTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Source is not Control sourceControl)
        {
            SetHoveredRow(null);
            return;
        }

        SetHoveredRow(GetRowFromSource(sourceControl));
    }

    private void OnMonitorTreePointerExited(object? sender, PointerEventArgs e)
        => SetHoveredRow(null);

    private void OnMonitorTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || e.Source is not Control sourceControl)
        {
            return;
        }

        var row = GetRowFromSource(sourceControl);
        if (row is not null)
        {
            viewModel.SelectedRow = row;
        }
    }

    private static MonitorTreeRowViewModel? GetRowFromSource(Control sourceControl)
    {
        if (sourceControl.DataContext is MonitorTreeRowViewModel sourceRow)
        {
            return sourceRow;
        }

        return sourceControl
            .GetVisualAncestors()
            .OfType<Control>()
            .Select(static control => control.DataContext)
            .OfType<MonitorTreeRowViewModel>()
            .FirstOrDefault();
    }

    private void SetHoveredRow(MonitorTreeRowViewModel? row)
    {
        if (ReferenceEquals(_hoveredRow, row))
        {
            return;
        }

        if (_hoveredRow is not null)
        {
            _hoveredRow.IsHovered = false;
        }

        _hoveredRow = row;
        if (_hoveredRow is not null)
        {
            _hoveredRow.IsHovered = true;
        }
    }

    private async void OnClosed(object? sender, System.EventArgs e)
    {
        SetHoveredRow(null);

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
    }
}
