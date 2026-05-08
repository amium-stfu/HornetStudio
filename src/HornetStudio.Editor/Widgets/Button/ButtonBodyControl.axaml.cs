using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace HornetStudio.Editor.Widgets;

public partial class ButtonBodyControl : UserControl
{
    public static readonly StyledProperty<bool> EnabledProperty =
        AvaloniaProperty.Register<ButtonBodyControl, bool>(nameof(Enabled), true);

    public static readonly StyledProperty<string?> BackColorProperty =
        AvaloniaProperty.Register<ButtonBodyControl, string?>(nameof(BackColor));

    public bool Enabled
    {
        get => GetValue(EnabledProperty);
        set => SetValue(EnabledProperty, value);
    }

    public string? BackColor
    {
        get => GetValue(BackColorProperty);
        set => SetValue(BackColorProperty, value);
    }

    public ButtonBodyControl()
    {
        InitializeComponent();
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        UpdateVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EnabledProperty || change.Property == BackColorProperty)
        {
            UpdateVisualState();
        }
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (!Enabled)
        {
            return;
        }

        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!Enabled)
        {
            return;
        }

        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Enabled)
        {
            e.Handled = true;
        }
    }

    private void UpdateVisualState()
    {
        var rootBorder = this.FindControl<Border>("RootBorder");
        if (rootBorder is null)
        {
            return;
        }

        // Test: always use a strong orange background so we can
        // clearly see whether ButtonBodyControl is active in udl_book.
        rootBorder.Background = Brush.Parse("#F59E0B");
        Cursor = Enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
    }
}