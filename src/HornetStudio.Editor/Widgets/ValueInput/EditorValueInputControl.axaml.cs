using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EditorValueInputControl : UserControl
{
    public static readonly StyledProperty<string> CardBorderBrushProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(CardBorderBrush), "#D5D9E0");

    public static readonly StyledProperty<string> ParameterEditBackgrundColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ParameterEditBackgrundColor), "#FFFFFF");

    public static readonly StyledProperty<string> ParameterEditForeColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ParameterEditForeColor), "#111827");

    public static readonly StyledProperty<string> ParameterHoverColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ParameterHoverColor), "#BDBDBD");

    public static readonly StyledProperty<string> ButtonBackColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ButtonBackColor), "#CFDBE7");

    public static readonly StyledProperty<string> EditPanelButtonBorderBrushProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(EditPanelButtonBorderBrush), "#CBD5E1");

    public static readonly StyledProperty<string> ButtonForeColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ButtonForeColor), "#111827");

    public static readonly StyledProperty<string> ButtonHoverColorProperty =
        AvaloniaProperty.Register<EditorValueInputControl, string>(nameof(ButtonHoverColor), "#E2E8F0");

    private enum ValueInputMode
    {
        None,
        Text,
        Numeric,
        Hex,
        Bits
    }

    private const double KeyButtonWidth = 112;
    private const double ActionButtonWidth = 112;
    private const double ButtonSpacing = 12;

    private ValueInputMode _mode;
    private FolderItemModel? _item;
    private int _maxDecimalDigits;
    private int _maxHexDigits = 16;
    private MainWindowViewModel? _subscribedViewModel;
    private bool _replaceOnNextOnscreenInput;
    private readonly List<(ToggleButton Button, int BitIndex)> _bitButtons = [];

    public string CardBorderBrush
    {
        get => GetValue(CardBorderBrushProperty);
        private set => SetValue(CardBorderBrushProperty, value);
    }

    public string ParameterEditBackgrundColor
    {
        get => GetValue(ParameterEditBackgrundColorProperty);
        private set => SetValue(ParameterEditBackgrundColorProperty, value);
    }

    public string ParameterEditForeColor
    {
        get => GetValue(ParameterEditForeColorProperty);
        private set => SetValue(ParameterEditForeColorProperty, value);
    }

    public string ParameterHoverColor
    {
        get => GetValue(ParameterHoverColorProperty);
        private set => SetValue(ParameterHoverColorProperty, value);
    }

    public string ButtonBackColor
    {
        get => GetValue(ButtonBackColorProperty);
        private set => SetValue(ButtonBackColorProperty, value);
    }

    public string EditPanelButtonBorderBrush
    {
        get => GetValue(EditPanelButtonBorderBrushProperty);
        private set => SetValue(EditPanelButtonBorderBrushProperty, value);
    }

    public string ButtonForeColor
    {
        get => GetValue(ButtonForeColorProperty);
        private set => SetValue(ButtonForeColorProperty, value);
    }

    public string ButtonHoverColor
    {
        get => GetValue(ButtonHoverColorProperty);
        private set => SetValue(ButtonHoverColorProperty, value);
    }

    public EditorValueInputControl()
    {
        InitializeComponent();
        this.FindControl<EditorTextInputPad>("TextInputPad")!.KeyInvoked += OnTextPadKeyInvoked;
        this.FindControl<EditorTextInputPad>("TextInputPad")!.ActionInvoked += OnTextPadActionInvoked;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            AttachToViewModel(ViewModel);
            ApplyInputTheme();
            RefreshFromViewModel();
        };
        DetachedFromVisualTree += (_, _) => AttachToViewModel(null);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachToViewModel(ViewModel);
        UpdateThemeBindings();
        ApplyInputTheme();
        RefreshFromViewModel();
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsValueInputOpen)
            || e.PropertyName == nameof(MainWindowViewModel.ActiveValueInputItem))
        {
            RefreshFromViewModel();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.EditPanelInputBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelInputForeground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterHoverColor)
            || e.PropertyName == nameof(MainWindowViewModel.ButtonBackColor)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.ButtonHoverColor)
            || e.PropertyName == nameof(MainWindowViewModel.ButtonForeColor))
        {
            UpdateThemeBindings();
            ApplyInputTheme();
            RefreshButtonChrome();
        }
    }

    private void UpdateThemeBindings()
    {
        var vm = ViewModel;
        CardBorderBrush = vm?.CardBorderBrush ?? "#D5D9E0";
        ParameterEditBackgrundColor = vm?.ParameterEditBackgrundColor ?? "#FFFFFF";
        ParameterEditForeColor = vm?.ParameterEditForeColor ?? "#111827";
        ParameterHoverColor = vm?.ParameterHoverColor ?? "#BDBDBD";
        ButtonBackColor = vm?.ButtonBackColor ?? "#CFDBE7";
        EditPanelButtonBorderBrush = vm?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeColor = vm?.ButtonForeColor ?? "#111827";
        ButtonHoverColor = vm?.ButtonHoverColor ?? "#E2E8F0";
    }

    private void RefreshFromViewModel()
    {
        var vm = ViewModel;
        if (vm?.IsValueInputOpen != true || vm.ActiveValueInputItem is null)
        {
            _item = null;
            ResetState();
            return;
        }

        if (ReferenceEquals(_item, vm.ActiveValueInputItem))
        {
            return;
        }

        _item = vm.ActiveValueInputItem;
        OpenFor(_item);
    }

    private void OpenFor(FolderItemModel item)
    {
        ResetState();
        ApplyInputTheme();
        var presentation = item.TargetPropertyView;
        var title = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.ValueEditorTitle;
        this.FindControl<TextBlock>("TitleTextBlock")!.Text = title;
        this.FindControl<TextBlock>("FormatTextBlock")!.Text = BuildFormatText(presentation);

        switch (presentation.Definition.Kind)
        {
            case PropertyVisualKind.Text:
                ConfigureText(presentation);
                break;
            case PropertyVisualKind.Numeric:
                ConfigureNumeric(presentation);
                break;
            case PropertyVisualKind.Hex:
                ConfigureHex(presentation);
                break;
            case PropertyVisualKind.Bits:
                ConfigureBits(presentation);
                break;
            default:
                ViewModel?.CancelValueInput();
                return;
        }

        FocusInputTextBox(selectAll: true);
    }

    private void ConfigureNumeric(PropertyDisplayModel presentation)
    {
        _mode = ValueInputMode.Numeric;
        _maxDecimalDigits = GetDecimalDigits(presentation.Definition.PatternOrOptionsText);
        var input = this.FindControl<TextBox>("InputTextBox")!;
        input.IsVisible = true;
        input.IsReadOnly = false;
        input.Text = GetNumericInputText(presentation.Property?.Value);
        var showSign = SupportsSign(presentation.Property?.Value?.GetType());

        var labels = new List<string>
        {
            "7", "8", "9",
            "4", "5", "6",
            "1", "2", "3",
            "0"
        };

        if (_maxDecimalDigits > 0)
        {
            labels.Add(".");
        }

        if (showSign)
        {
            labels.Add("+/-");
        }

        BuildInputButtons(labels, 3);
        BuildActionButtons(["DEL", "Clear", "Cancel", "OK"]);
    }

    private void ConfigureHex(PropertyDisplayModel presentation)
    {
        _mode = ValueInputMode.Hex;
        _maxHexDigits = GetHexDigits(presentation);
        var input = this.FindControl<TextBox>("InputTextBox")!;
        input.IsVisible = true;
        input.IsReadOnly = false;
        input.Text = GetHexInputText(presentation);
        BuildHexButtons();
    }

    private void ConfigureBits(PropertyDisplayModel presentation)
    {
        _mode = ValueInputMode.Bits;
        var input = this.FindControl<TextBox>("InputTextBox")!;
        input.IsVisible = true;
        input.IsReadOnly = true;
        input.Text = string.Empty;
        this.FindControl<WrapPanel>("BitButtonPanel")!.IsVisible = true;
        BuildActionButtons(["Clear", "Cancel", "OK"]);
        BuildBitButtons(presentation);
        UpdateBitPreview();
    }
    private void ConfigureText(PropertyDisplayModel presentation)
    {
        _mode = ValueInputMode.Text;
        var input = this.FindControl<TextBox>("InputTextBox")!;
        input.IsVisible = true;
        input.IsReadOnly = false;
        input.Text = presentation.Property?.Value?.ToString() ?? string.Empty;

        var textPad = this.FindControl<EditorTextInputPad>("TextInputPad")!;
        var textPadViewbox = this.FindControl<Viewbox>("TextInputPadViewbox")!;
        textPadViewbox.IsVisible = true;
        textPad.RefreshButtonChrome();
        this.FindControl<Border>("RootBorder")!.Width = double.NaN;
    }

    private void BuildInputButtons(IReadOnlyList<string> labels, int columns)
    {
        this.FindControl<Grid>("HexInputGrid")!.IsVisible = false;
        var grid = this.FindControl<UniformGrid>("InputButtonGrid")!;
        grid.Children.Clear();
        grid.Columns = columns;
        grid.IsVisible = true;
        grid.Width = columns * (KeyButtonWidth + ButtonSpacing);

        foreach (var label in labels)
        {
            var button = new Button
            {
                Content = label,
                Width = KeyButtonWidth,
                MinHeight = 48,
                Margin = new Thickness(0, 0, ButtonSpacing, ButtonSpacing),
                Tag = label,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("input-key");
            ApplyButtonChrome(button);
            button.Click += OnInputButtonClicked;
            button.PointerPressed += OnInteractivePointerPressed;
            button.PointerEntered += OnButtonPointerEntered;
            button.PointerExited += OnButtonPointerExited;
            grid.Children.Add(button);
        }

        UpdateDialogWidth(grid.Width, ActionButtonWidth);
    }

    private void BuildActionButtons(IReadOnlyList<string> labels)
    {
        var panel = this.FindControl<StackPanel>("ActionButtonPanel")!;
        panel.IsVisible = true;
        panel.Children.Clear();

        foreach (var label in labels)
        {
            var button = new Button
            {
                Content = label,
                Width = ActionButtonWidth,
                MinHeight = 48,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("action-key");
            ApplyButtonChrome(button);

            switch (label)
            {
                case "DEL":
                    button.Click += OnBackspaceClicked;
                    break;
                case "Clear":
                    button.Click += OnClearClicked;
                    break;
                case "Cancel":
                    button.Click += OnCancelClicked;
                    break;
                case "OK":
                    button.Click += OnApplyClicked;
                    break;
            }

            button.PointerPressed += OnInteractivePointerPressed;
            button.PointerEntered += OnButtonPointerEntered;
            button.PointerExited += OnButtonPointerExited;
            panel.Children.Add(button);
        }

        panel.Width = ActionButtonWidth;
        UpdateDialogWidth(this.FindControl<UniformGrid>("InputButtonGrid")!.IsVisible ? this.FindControl<UniformGrid>("InputButtonGrid")!.Width : 0, panel.Width);
    }

    private void BuildHexButtons()
    {
        var host = this.FindControl<Grid>("HexInputGrid")!;
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();
        host.IsVisible = true;
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        host.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(24)));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        host.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(24)));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var alphaGrid = new Grid();
        for (var i = 0; i < 4; i++)
        {
            alphaGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        alphaGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        alphaGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        AddHexButton(alphaGrid, "A", 0, 0);
        AddHexButton(alphaGrid, "E", 0, 1);
        AddHexButton(alphaGrid, "B", 1, 0);
        AddHexButton(alphaGrid, "F", 1, 1);
        AddHexButton(alphaGrid, "C", 2, 0);
        AddHexButton(alphaGrid, "D", 3, 0);
        Grid.SetColumn(alphaGrid, 0);
        host.Children.Add(alphaGrid);

        var numericGrid = new Grid();
        for (var i = 0; i < 4; i++)
        {
            numericGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        for (var i = 0; i < 3; i++)
        {
            numericGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }
        AddHexButton(numericGrid, "7", 0, 0);
        AddHexButton(numericGrid, "8", 0, 1);
        AddHexButton(numericGrid, "9", 0, 2);
        AddHexButton(numericGrid, "4", 1, 0);
        AddHexButton(numericGrid, "5", 1, 1);
        AddHexButton(numericGrid, "6", 1, 2);
        AddHexButton(numericGrid, "1", 2, 0);
        AddHexButton(numericGrid, "2", 2, 1);
        AddHexButton(numericGrid, "3", 2, 2);
        AddHexButton(numericGrid, "0", 3, 1);
        Grid.SetColumn(numericGrid, 2);
        host.Children.Add(numericGrid);

        var actionsGrid = new Grid();
        for (var i = 0; i < 4; i++)
        {
            actionsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        AddHexActionButton(actionsGrid, "DEL", 0);
        AddHexActionButton(actionsGrid, "Clear", 1, "CLR");
        AddHexActionButton(actionsGrid, "Cancel", 2, "X");
        AddHexActionButton(actionsGrid, "OK", 3);
        Grid.SetColumn(actionsGrid, 4);
        host.Children.Add(actionsGrid);

        this.FindControl<UniformGrid>("InputButtonGrid")!.IsVisible = false;
        this.FindControl<StackPanel>("ActionButtonPanel")!.IsVisible = false;
        host.Width = (2 * (KeyButtonWidth + ButtonSpacing)) + (3 * (KeyButtonWidth + ButtonSpacing)) + ActionButtonWidth + 48;
        UpdateDialogWidth(host.Width, 0);
    }

    private void AddHexButton(Grid grid, string label, int row, int column)
    {
        var button = new Button
        {
            Content = label,
            Width = KeyButtonWidth,
            MinHeight = 48,
            Margin = new Thickness(0, 0, ButtonSpacing, ButtonSpacing),
            Tag = label,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("input-key");
        ApplyButtonChrome(button);
        button.Click += OnInputButtonClicked;
        button.PointerPressed += OnInteractivePointerPressed;
        button.PointerEntered += OnButtonPointerEntered;
        button.PointerExited += OnButtonPointerExited;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private void AddHexActionButton(Grid grid, string action, int row, string? contentOverride = null)
    {
        var button = new Button
        {
            Content = contentOverride ?? action,
            Width = ActionButtonWidth,
            MinHeight = 48,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, ButtonSpacing)
        };
        button.Classes.Add("action-key");
        ApplyButtonChrome(button);
        switch (action)
        {
            case "DEL":
                button.Click += OnBackspaceClicked;
                break;
            case "Clear":
                button.Click += OnClearClicked;
                break;
            case "Cancel":
                button.Click += OnCancelClicked;
                break;
            case "OK":
                button.Click += OnApplyClicked;
                break;
        }
        button.PointerPressed += OnInteractivePointerPressed;
        button.PointerEntered += OnButtonPointerEntered;
        button.PointerExited += OnButtonPointerExited;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, 0);
        grid.Children.Add(button);
    }
    private void BuildBitButtons(PropertyDisplayModel presentation)
    {
        var panel = this.FindControl<WrapPanel>("BitButtonPanel")!;
        panel.Children.Clear();
        _bitButtons.Clear();
        var value = ToUInt64(presentation.Property?.Value);

        for (var bitIndex = presentation.Definition.BitCount - 1; bitIndex >= 0; bitIndex--)
        {
            var label = bitIndex < presentation.Definition.Options.Count && !string.IsNullOrWhiteSpace(presentation.Definition.Options[bitIndex])
                ? presentation.Definition.Options[bitIndex]
                : $"Bit {bitIndex + 1}";
            var button = new ToggleButton
            {
                Content = label,
                Margin = new Thickness(0, 0, ButtonSpacing, ButtonSpacing),
                MinWidth = 96,
                MinHeight = 48,
                Tag = bitIndex,
                IsChecked = ((value >> bitIndex) & 1UL) == 1UL,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Classes.Add("bit-key");
            ApplyBitButtonStyle(button);
            button.Click += OnBitButtonClicked;
            button.PointerPressed += OnInteractivePointerPressed;
            panel.Children.Add(button);
            _bitButtons.Add((button, bitIndex));
        }

        UpdateDialogWidth(Math.Max(360, panel.Bounds.Width), ActionButtonWidth);
    }

    private void ApplyInputTheme()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var input = this.FindControl<TextBox>("InputTextBox");
        if (input is null)
        {
            return;
        }

        input.Background = Brush.Parse(vm.ParameterEditBackgrundColor);
        input.Foreground = Brush.Parse(vm.ParameterEditForeColor);
        input.CaretBrush = Brush.Parse(vm.ParameterEditForeColor);
        input.BorderBrush = Brush.Parse(vm.CardBorderBrush);
        input.SelectionBrush = Brush.Parse(vm.ParameterHoverColor);
        input.SelectionForegroundBrush = Brush.Parse(vm.ParameterEditForeColor);
    }

    private void ApplyButtonChrome(Button button, bool isPointerOver = false)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        button.Background = Brush.Parse(isPointerOver ? vm.ButtonHoverColor : vm.ButtonBackColor);
        button.BorderBrush = Brush.Parse(vm.EditPanelButtonBorderBrush);
        button.Foreground = Brush.Parse(vm.ButtonForeColor);
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(8);
        button.Opacity = 1;
    }

    private void RefreshButtonChrome()
    {
        foreach (var button in this.FindControl<UniformGrid>("InputButtonGrid")!.Children.OfType<Button>())
        {
            ApplyButtonChrome(button);
        }

        foreach (var button in this.FindControl<StackPanel>("ActionButtonPanel")!.Children.OfType<Button>())
        {
            ApplyButtonChrome(button);
        }

        foreach (var (button, _) in _bitButtons)
        {
            ApplyBitButtonStyle(button);
        }

        this.FindControl<EditorTextInputPad>("TextInputPad")!.RefreshButtonChrome();
    }

    private void OnButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonChrome(button, true);
        }
    }

    private void OnButtonPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonChrome(button);
        }
    }

    private void OnBitPointerEntered(object? sender, PointerEventArgs e)
    {
        // Hover-Effekte fuer Bit-Buttons sind explizit deaktiviert.
    }

    private void OnBitPointerExited(object? sender, PointerEventArgs e)
    {
        // Hover-Effekte fuer Bit-Buttons sind explizit deaktiviert.
    }

    private void OnInputButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        if (key == "+/-")
        {
            ToggleSign();
            return;
        }

        var input = this.FindControl<TextBox>("InputTextBox")!;
        var current = GetEffectiveInputText(input, consumePendingReplace: true);

        if (_mode == ValueInputMode.Numeric)
        {
            input.Text = AppendNumericKey(current, key);
        }
        else if (_mode == ValueInputMode.Hex)
        {
            input.Text = AppendHexKey(current, key);
        }

        FocusInputTextBox();
        e.Handled = true;
    }

    private void OnBitButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            ApplyBitButtonStyle(button);
            UpdateBitPreview();
        }

        e.Handled = true;
    }

    private void ToggleSign()
    {
        if (_mode != ValueInputMode.Numeric)
        {
            return;
        }

        var input = this.FindControl<TextBox>("InputTextBox")!;
        var text = GetEffectiveInputText(input, consumePendingReplace: true);
        input.Text = text.StartsWith("-", StringComparison.Ordinal) ? text[1..] : "-" + text;
        FocusInputTextBox();
    }

    private void OnBackspaceClicked(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("InputTextBox")!;
        var text = input.Text ?? string.Empty;
        if (_inputIsReadOnly())
        {
            FocusInputTextBox();
            e.Handled = true;
            return;
        }

        if (_replaceOnNextOnscreenInput || HasActiveSelection(input))
        {
            _replaceOnNextOnscreenInput = false;
            input.Text = string.Empty;
        }
        else if (text.Length > 0)
        {
            input.Text = text[..^1];
        }

        FocusInputTextBox();
        e.Handled = true;
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        if (_mode == ValueInputMode.Bits)
        {
            foreach (var (button, _) in _bitButtons)
            {
                button.IsChecked = false;
                ApplyBitButtonStyle(button);
            }
            UpdateBitPreview();
        }
        else
        {
            _replaceOnNextOnscreenInput = false;
            this.FindControl<TextBox>("InputTextBox")!.Text = string.Empty;
        }

        FocusInputTextBox();
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelValueInput();
        e.Handled = true;
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        ApplyCurrentValue();
        e.Handled = true;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_mode == ValueInputMode.Hex && e.Key is >= Key.A and <= Key.F)
        {
            var value = ((char)('A' + (e.Key - Key.A))).ToString();
            var input = this.FindControl<TextBox>("InputTextBox")!;
            input.Text = AppendHexKey(GetEffectiveInputText(input, consumePendingReplace: true), value);
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ApplyCurrentValue();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            ViewModel?.CancelValueInput();
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_mode == ValueInputMode.Hex && sender is TextBox input)
        {
            var normalized = new string((input.Text ?? string.Empty).ToUpperInvariant().Where(IsHexChar).ToArray());
            if (normalized.Length > _maxHexDigits)
            {
                normalized = normalized[.._maxHexDigits];
            }
            if (!string.Equals(input.Text, normalized, StringComparison.Ordinal))
            {
                input.Text = normalized;
                input.CaretIndex = input.Text.Length;
            }
        }
    }

    private void OnTextPadKeyInvoked(object? sender, string key)
    {
        if (_mode != ValueInputMode.Text)
        {
            return;
        }

        var input = this.FindControl<TextBox>("InputTextBox")!;
        InsertTextAtCaret(input, key);
        input.Focus();
    }

    private void OnTextPadActionInvoked(object? sender, string action)
    {
        if (_mode != ValueInputMode.Text)
        {
            return;
        }

        var input = this.FindControl<TextBox>("InputTextBox")!;
        switch (action)
        {
            case "Backspace":
                input.Text = string.Empty;
                input.CaretIndex = 0;
                input.SelectionStart = 0;
                input.SelectionEnd = 0;
                input.Focus();
                break;
            case "MoveLeft":
                {
                    var text = input.Text ?? string.Empty;
                    if (text.Length > 0)
                    {
                        text = text[..^1];
                        input.Text = text;
                        var caret = text.Length;
                        input.CaretIndex = caret;
                        input.SelectionStart = caret;
                        input.SelectionEnd = caret;
                    }

                    input.Focus();
                }
                break;
            case "Apply":
                ApplyCurrentValue();
                break;
            case "Cancel":
                ViewModel?.CancelValueInput();
                break;
        }
    }

    private void InsertTextAtCaret(TextBox input, string value)
    {
        var currentText = input.Text ?? string.Empty;
        var replaceAll = _replaceOnNextOnscreenInput;
        _replaceOnNextOnscreenInput = false;

        var selectionStart = replaceAll ? 0 : System.Math.Min(input.SelectionStart, input.SelectionEnd);
        var selectionEnd = replaceAll ? currentText.Length : System.Math.Max(input.SelectionStart, input.SelectionEnd);
        selectionStart = System.Math.Clamp(selectionStart, 0, currentText.Length);
        selectionEnd = System.Math.Clamp(selectionEnd, selectionStart, currentText.Length);

        input.Text = currentText[..selectionStart] + value + currentText[selectionEnd..];
        input.CaretIndex = selectionStart + value.Length;
        input.SelectionStart = input.CaretIndex;
        input.SelectionEnd = input.CaretIndex;
    }

    private void BackspaceTextAtCaret(TextBox input)
    {
        var currentText = input.Text ?? string.Empty;
        if (_replaceOnNextOnscreenInput)
        {
            _replaceOnNextOnscreenInput = false;
            input.Text = string.Empty;
            input.CaretIndex = 0;
            input.SelectionStart = 0;
            input.SelectionEnd = 0;
            return;
        }

        var selectionStart = System.Math.Min(input.SelectionStart, input.SelectionEnd);
        var selectionEnd = System.Math.Max(input.SelectionStart, input.SelectionEnd);
        if (selectionEnd > selectionStart)
        {
            input.Text = currentText[..selectionStart] + currentText[selectionEnd..];
            input.CaretIndex = selectionStart;
            input.SelectionStart = selectionStart;
            input.SelectionEnd = selectionStart;
            return;
        }

        if (input.CaretIndex <= 0 || currentText.Length == 0)
        {
            return;
        }

        var removeIndex = input.CaretIndex - 1;
        input.Text = currentText[..removeIndex] + currentText[input.CaretIndex..];
        input.CaretIndex = removeIndex;
        input.SelectionStart = removeIndex;
        input.SelectionEnd = removeIndex;
    }

    private static void MoveCaret(TextBox input, int delta)
    {
        var target = System.Math.Clamp(input.CaretIndex + delta, 0, (input.Text ?? string.Empty).Length);
        input.CaretIndex = target;
        input.SelectionStart = target;
        input.SelectionEnd = target;
    }
    private void ApplyCurrentValue()
    {
        if (_item is null)
        {
            return;
        }

        object rawValue = _mode switch
        {
            ValueInputMode.Text => this.FindControl<TextBox>("InputTextBox")!.Text ?? string.Empty,
            ValueInputMode.Numeric => string.IsNullOrWhiteSpace(this.FindControl<TextBox>("InputTextBox")!.Text) ? "0" : this.FindControl<TextBox>("InputTextBox")!.Text!,
            ValueInputMode.Hex => ulong.Parse(string.IsNullOrWhiteSpace(this.FindControl<TextBox>("InputTextBox")!.Text) ? "0" : this.FindControl<TextBox>("InputTextBox")!.Text!, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ValueInputMode.Bits => (long)GetBitMaskValue(),
            _ => string.Empty
        };

        if (_item.TrySendInput(rawValue, out var error))
        {
            ViewModel?.CancelValueInput();
            return;
        }

        this.FindControl<TextBlock>("FormatTextBlock")!.Text = $"Fehler: {error}";
    }

    private void UpdateBitPreview()
    {
        if (_mode != ValueInputMode.Bits)
        {
            return;
        }

        var value = GetBitMaskValue();
        this.FindControl<TextBox>("InputTextBox")!.Text = $"{value} (0x{value:X})";
    }

    private string GetEffectiveInputText(TextBox input, bool consumePendingReplace = false)
    {
        var replace = _replaceOnNextOnscreenInput || HasActiveSelection(input);
        if (consumePendingReplace)
        {
            _replaceOnNextOnscreenInput = false;
        }

        return replace ? string.Empty : input.Text ?? string.Empty;
    }

    private static bool HasActiveSelection(TextBox input)
        => input.SelectionEnd > input.SelectionStart;

    private ulong GetBitMaskValue()
    {
        ulong value = 0;
        foreach (var (button, bitIndex) in _bitButtons)
        {
            if (button.IsChecked == true)
            {
                value |= 1UL << bitIndex;
            }
        }
        return value;
    }

    private bool _inputIsReadOnly() => _mode == ValueInputMode.Bits;

    private void ResetState()
    {
        _mode = ValueInputMode.None;
        _maxDecimalDigits = 0;
        _maxHexDigits = 16;
        _replaceOnNextOnscreenInput = false;
        _bitButtons.Clear();
        this.FindControl<TextBlock>("TitleTextBlock")!.Text = string.Empty;
        this.FindControl<TextBlock>("FormatTextBlock")!.Text = string.Empty;
        this.FindControl<TextBox>("InputTextBox")!.Text = string.Empty;
        this.FindControl<TextBox>("InputTextBox")!.IsVisible = false;
        this.FindControl<Viewbox>("TextInputPadViewbox")!.IsVisible = false;
        this.FindControl<UniformGrid>("InputButtonGrid")!.Children.Clear();
        this.FindControl<UniformGrid>("InputButtonGrid")!.IsVisible = false;
        this.FindControl<UniformGrid>("InputButtonGrid")!.Width = 0;
        this.FindControl<Grid>("HexInputGrid")!.Children.Clear();
        this.FindControl<Grid>("HexInputGrid")!.RowDefinitions.Clear();
        this.FindControl<Grid>("HexInputGrid")!.ColumnDefinitions.Clear();
        this.FindControl<Grid>("HexInputGrid")!.IsVisible = false;
        this.FindControl<WrapPanel>("BitButtonPanel")!.Children.Clear();
        this.FindControl<WrapPanel>("BitButtonPanel")!.IsVisible = false;
        this.FindControl<StackPanel>("ActionButtonPanel")!.Children.Clear();
        this.FindControl<StackPanel>("ActionButtonPanel")!.Width = double.NaN;
        this.FindControl<Border>("RootBorder")!.Width = double.NaN;
    }

    private void FocusInputTextBox(bool selectAll = false)
    {
        if (this.FindControl<TextBox>("InputTextBox") is { IsVisible: true } input)
        {
            if (selectAll)
            {
                _replaceOnNextOnscreenInput = true;
            }

            ApplyInputTheme();
            Dispatcher.UIThread.Post(() =>
            {
                input.Focus();
                if (selectAll)
                {
                    input.SelectAll();
                }
                else
                {
                    input.CaretIndex = (input.Text ?? string.Empty).Length;
                }
            }, DispatcherPriority.Input);
        }
    }

    private void ApplyBitButtonStyle(ToggleButton button)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var isActive = button.IsChecked == true;
        var background = isActive ? vm.HeaderBadgeBackground : vm.ButtonBackColor;
        button.Background = Brush.Parse(background);
        button.BorderBrush = Brush.Parse(vm.EditPanelButtonBorderBrush);
        button.Foreground = Brush.Parse(vm.ButtonForeColor);
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(8);
        button.Opacity = 1;
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateDialogWidth(double inputWidth, double actionWidth)
    {
        var contentWidth = inputWidth > 0 ? inputWidth + 16 + actionWidth : actionWidth;
        if (contentWidth <= 0)
        {
            return;
        }

        this.FindControl<Border>("RootBorder")!.Width = contentWidth + 32;
    }

    private static string BuildFormatText(PropertyDisplayModel presentation)
        => presentation.Definition.Kind switch
        {
            PropertyVisualKind.Text => "Text",
            PropertyVisualKind.Numeric => $"Numeric {presentation.Definition.PatternOrOptionsText}",
            PropertyVisualKind.Hex => $"Hex {presentation.Definition.PatternOrOptionsText}",
            PropertyVisualKind.Bits => $"Bitmask {presentation.Definition.BitCount} Bit",
            _ => string.Empty
        };

    private static string GetNumericInputText(object? value)
        => value switch
        {
            null => "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "0",
            _ => value.ToString() ?? "0"
        };

    private static int GetDecimalDigits(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "0.##";
        }

        var decimalIndex = pattern.IndexOf('.', StringComparison.Ordinal);
        return decimalIndex < 0 ? 0 : pattern[(decimalIndex + 1)..].Count(ch => ch is '0' or '#');
    }

    private static bool SupportsSign(Type? type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type ?? typeof(double)) ?? type ?? typeof(double);
        return effectiveType == typeof(sbyte)
            || effectiveType == typeof(short)
            || effectiveType == typeof(int)
            || effectiveType == typeof(long)
            || effectiveType == typeof(float)
            || effectiveType == typeof(double)
            || effectiveType == typeof(decimal);
    }

    private string AppendNumericKey(string current, string key)
    {
        if (key == ".")
        {
            if (_maxDecimalDigits <= 0 || current.Contains('.', StringComparison.Ordinal))
            {
                return current;
            }
            return string.IsNullOrWhiteSpace(current) || current == "-" ? $"{current}0." : current + ".";
        }

        if (_maxDecimalDigits > 0 && current.Contains('.', StringComparison.Ordinal))
        {
            var decimalIndex = current.IndexOf('.', StringComparison.Ordinal);
            if (current.Length - decimalIndex - 1 >= _maxDecimalDigits)
            {
                return current;
            }
        }

        return current switch
        {
            "0" => key,
            "-0" => "-" + key,
            _ => current + key
        };
    }

    private string AppendHexKey(string current, string key)
    {
        if (current.Length >= _maxHexDigits)
        {
            return current;
        }

        return current + key.ToUpperInvariant();
    }

    private static bool IsHexChar(char ch)
        => ch is >= '0' and <= '9' or >= 'A' and <= 'F';

    private static string GetHexInputText(PropertyDisplayModel presentation)
    {
        var value = ToUInt64(presentation.Property?.Value);
        var digits = GetHexDigits(presentation);
        return value.ToString($"X{digits}", CultureInfo.InvariantCulture);
    }

    private static int GetHexDigits(PropertyDisplayModel presentation)
    {
        if (int.TryParse(presentation.Definition.PatternOrOptionsText, out var digits) && digits > 0)
        {
            return digits;
        }

        var type = Nullable.GetUnderlyingType(presentation.Property?.Value?.GetType() ?? typeof(ulong)) ?? presentation.Property?.Value?.GetType() ?? typeof(ulong);
        return type == typeof(byte) || type == typeof(sbyte) ? 2
            : type == typeof(short) || type == typeof(ushort) ? 4
            : type == typeof(int) || type == typeof(uint) ? 8
            : 16;
    }

    private static ulong ToUInt64(object? value)
        => value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => unchecked((ulong)sbyteValue),
            short shortValue => unchecked((ulong)shortValue),
            ushort ushortValue => ushortValue,
            int intValue => unchecked((ulong)intValue),
            uint uintValue => uintValue,
            long longValue => unchecked((ulong)longValue),
            ulong ulongValue => ulongValue,
            float floatValue => unchecked((ulong)floatValue),
            double doubleValue => unchecked((ulong)doubleValue),
            decimal decimalValue => unchecked((ulong)decimalValue),
            string text when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0UL
        };
}

public partial class EditorValueInputWidget : EditorValueInputControl
{
}








