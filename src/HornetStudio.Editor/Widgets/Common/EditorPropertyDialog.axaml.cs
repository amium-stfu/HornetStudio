using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EditorPropertyDialog : UserControl
{
    public static readonly StyledProperty<string> CardBorderBrushProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(CardBorderBrush), "#D5D9E0");

    public static readonly StyledProperty<string> ParameterEditBackgrundColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(ParameterEditBackgrundColor), "#FFFFFF");

    public static readonly StyledProperty<string> ParameterEditForeColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(ParameterEditForeColor), "#111827");

    public static readonly StyledProperty<string> ParameterHoverColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(ParameterHoverColor), "#BDBDBD");

    public static readonly StyledProperty<string> EditPanelButtonBackgroundProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditPanelButtonBackground), "#F8FAFC");

    public static readonly StyledProperty<string> EditPanelButtonBorderBrushProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditPanelButtonBorderBrush), "#CBD5E1");

    public static readonly StyledProperty<string> PrimaryTextBrushProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(PrimaryTextBrush), "#111827");

    public static readonly StyledProperty<string> SecondaryTextBrushProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(SecondaryTextBrush), "#5E6777");

    public static readonly StyledProperty<string> TabSelectBackColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(TabSelectBackColor), "#FFF1C4");

    public static readonly StyledProperty<string> TabSelectForeColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(TabSelectForeColor), "#000000");

    public static readonly StyledProperty<string> TabBackColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(TabBackColor), "#E7E7E7");

    public static readonly StyledProperty<string> TabForeColorProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(TabForeColor), "#111827");

    public static readonly StyledProperty<string> EditorDialogSectionHeaderBackgroundProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditorDialogSectionHeaderBackground), "#E8EEF6");

    public static readonly StyledProperty<string> EditorDialogSectionHeaderForegroundProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditorDialogSectionHeaderForeground), "#111827");

    public static readonly StyledProperty<string> EditorDialogSectionHeaderBorderBrushProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditorDialogSectionHeaderBorderBrush), "#CBD5E1");

    public static readonly StyledProperty<string> EditorDialogSectionContentBackgroundProperty =
        AvaloniaProperty.Register<EditorPropertyDialog, string>(nameof(EditorDialogSectionContentBackground), "#EEF3F8");

    private static readonly IReadOnlyList<Color> StandardColors =
    [
        Color.Parse("#1B19D8"),
        Color.Parse("#C62828"),
        Color.Parse("#118C22"),
        Color.Parse("#7E3CCB"),
        Color.Parse("#FFFFFF"),
        Color.Parse("#3A8EE6"),
        Color.Parse("#5F7433"),
        Color.Parse("#4F477D"),
        Color.Parse("#A55B12"),
        Color.Parse("#FFAF1A"),
        Color.Parse("#111111"),
        Color.Parse("#FF1F92"),
        Color.Parse("#6B63C8"),
        Color.Parse("#D46BD4"),
        Color.Parse("#B8860B"),
        Color.Parse("#8FAFB1"),
        Color.Parse("#FF1A1A"),
        Color.Parse("#FFFF00"),
        Color.Parse("#BDBDBD"),
        Color.Parse("#EAEAEA")
    ];

    private EditorDialogField? _activeColorField;
    private EditorDialogField? _activeIconField;
    private MainWindowViewModel? _subscribedViewModel;

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

    public string EditPanelButtonBackground
    {
        get => GetValue(EditPanelButtonBackgroundProperty);
        private set => SetValue(EditPanelButtonBackgroundProperty, value);
    }

    public string EditPanelButtonBorderBrush
    {
        get => GetValue(EditPanelButtonBorderBrushProperty);
        private set => SetValue(EditPanelButtonBorderBrushProperty, value);
    }

    public string PrimaryTextBrush
    {
        get => GetValue(PrimaryTextBrushProperty);
        private set => SetValue(PrimaryTextBrushProperty, value);
    }

    public string SecondaryTextBrush
    {
        get => GetValue(SecondaryTextBrushProperty);
        private set => SetValue(SecondaryTextBrushProperty, value);
    }

    public string TabSelectBackColor
    {
        get => GetValue(TabSelectBackColorProperty);
        private set => SetValue(TabSelectBackColorProperty, value);
    }

    public string TabSelectForeColor
    {
        get => GetValue(TabSelectForeColorProperty);
        private set => SetValue(TabSelectForeColorProperty, value);
    }

    public string TabBackColor
    {
        get => GetValue(TabBackColorProperty);
        private set => SetValue(TabBackColorProperty, value);
    }

    public string TabForeColor
    {
        get => GetValue(TabForeColorProperty);
        private set => SetValue(TabForeColorProperty, value);
    }

    public string EditorDialogSectionHeaderBackground
    {
        get => GetValue(EditorDialogSectionHeaderBackgroundProperty);
        private set => SetValue(EditorDialogSectionHeaderBackgroundProperty, value);
    }

    public string EditorDialogSectionHeaderForeground
    {
        get => GetValue(EditorDialogSectionHeaderForegroundProperty);
        private set => SetValue(EditorDialogSectionHeaderForegroundProperty, value);
    }

    public string EditorDialogSectionHeaderBorderBrush
    {
        get => GetValue(EditorDialogSectionHeaderBorderBrushProperty);
        private set => SetValue(EditorDialogSectionHeaderBorderBrushProperty, value);
    }

    public string EditorDialogSectionContentBackground
    {
        get => GetValue(EditorDialogSectionContentBackgroundProperty);
        private set => SetValue(EditorDialogSectionContentBackgroundProperty, value);
    }

    public EditorPropertyDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => BuildColorPalette();
        DetachedFromVisualTree += (_, _) => AttachToViewModel(null);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterEditBackgrundColor)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterEditForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterHoverColor)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectBackColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabBackColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionHeaderBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionHeaderForeground)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionHeaderBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionContentBackground))
        {
            UpdateThemeBindings();
        }
    }

    private void UpdateThemeBindings()
    {
        var vm = ViewModel;
        CardBorderBrush = vm?.CardBorderBrush ?? "#D5D9E0";
        ParameterEditBackgrundColor = vm?.ParameterEditBackgrundColor ?? "#FFFFFF";
        ParameterEditForeColor = vm?.ParameterEditForeColor ?? "#111827";
        ParameterHoverColor = vm?.ParameterHoverColor ?? "#BDBDBD";
        EditPanelButtonBackground = vm?.EditPanelButtonBackground ?? "#F8FAFC";
        EditPanelButtonBorderBrush = vm?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        PrimaryTextBrush = vm?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = vm?.SecondaryTextBrush ?? "#5E6777";
        TabSelectBackColor = vm?.TabSelectBackColor ?? "#FFF1C4";
        TabSelectForeColor = vm?.TabSelectForeColor ?? "#000000";
        TabBackColor = vm?.TabBackColor ?? "#E7E7E7";
        TabForeColor = vm?.TabForeColor ?? "#111827";
        EditorDialogSectionHeaderBackground = vm?.EditorDialogSectionHeaderBackground ?? "#E8EEF6";
        EditorDialogSectionHeaderForeground = vm?.EditorDialogSectionHeaderForeground ?? "#111827";
        EditorDialogSectionHeaderBorderBrush = vm?.EditorDialogSectionHeaderBorderBrush ?? "#CBD5E1";
        EditorDialogSectionContentBackground = vm?.EditorDialogSectionContentBackground ?? "#EEF3F8";
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnOpenColorPickerClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field } control)
        {
            return;
        }

        _activeColorField = field;
        if (this.FindControl<TextBox>("ColorHexTextBox") is { } hexBox)
        {
            hexBox.Text = field.Value;
        }

        if (this.FindControl<Popup>("ColorPopup") is { } popup)
        {
            popup.PlacementTarget = control;
            popup.IsOpen = true;
        }

        e.Handled = true;
    }

    private async void OnOpenIconPickerClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        _activeIconField = field;

        var window = new IconPickerDialogWindow(ViewModel, field.Value);
        var result = await window.ShowDialog<IconPickerResult?>(owner);
        if (result is null)
        {
            return;
        }

        if (_activeIconField is not null)
        {
            _activeIconField.Value = result.IconPath ?? string.Empty;
        }

        if (ViewModel is not null && result.IconColor is not null)
        {
            var iconColorField = ViewModel.EditorDialogSections
                .SelectMany(section => section.Fields)
                .FirstOrDefault(f => string.Equals(f.Key, "ButtonIconColor", StringComparison.Ordinal));

            if (iconColorField is not null)
            {
                iconColorField.Value = result.IconColor;
            }
        }

        e.Handled = true;
    }

    private async void OnCreatePythonScriptClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var targetPath = ViewModel?.GetPythonScriptTargetPath(field, ".py");
        var overwriteExisting = false;
        if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
        {
            overwriteExisting = await EditorInputDialogs.ConfirmAsync(
                owner,
                "Python script already exists",
                $"The file '{Path.GetFileName(targetPath)}' already exists. Overwrite it?",
                confirmText: "Overwrite",
                cancelText: "Cancel");

            if (!overwriteExisting)
            {
                e.Handled = true;
                return;
            }
        }

        ViewModel?.CreatePythonScriptForField(field, overwriteExisting);
        e.Handled = true;
    }

    private async void OnCopyPythonTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        IStorageFolder? startFolder = null;
        var initialDirectory = ViewModel?.GetPythonTemplatesDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            startFolder = await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Python template",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter =
            [
                new FilePickerFileType("Python Files")
                {
                    Patterns = ["*.py"]
                }
            ]
        });

        var selectedPath = TryGetStoragePath(result.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            var targetPath = ViewModel?.GetPythonScriptTargetPath(field, Path.GetExtension(selectedPath));
            var overwriteExisting = false;
            if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
            {
                overwriteExisting = await EditorInputDialogs.ConfirmAsync(
                    owner,
                    "Python script already exists",
                    $"The file '{Path.GetFileName(targetPath)}' already exists. Overwrite it with the selected template?",
                    confirmText: "Overwrite",
                    cancelText: "Cancel");

                if (!overwriteExisting)
                {
                    e.Handled = true;
                    return;
                }
            }

            ViewModel?.CopyPythonTemplateForField(field, selectedPath, overwriteExisting);
        }

        e.Handled = true;
    }

    // Legacy ApplicationExplorer picker handler retained for compatibility; no longer used now that the
    // application list is embedded inline in the properties panel via
    // ApplicationExplorerSettingsDialogWindow UserControl.
    private void OnOpenApplicationPickerClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnPaletteColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorText } || _activeColorField is null)
        {
            return;
        }

        _activeColorField.Value = colorText;
        CloseColorPopup();
        e.Handled = true;
    }

    private void OnTransparentColorClicked(object? sender, RoutedEventArgs e)
    {
        if (_activeColorField is not null)
        {
            _activeColorField.Value = "Transparent";
        }

        CloseColorPopup();
        e.Handled = true;
    }

    private void OnDefaultColorClicked(object? sender, RoutedEventArgs e)
    {
        if (_activeColorField is not null)
        {
            _activeColorField.Value = string.Empty;
        }

        CloseColorPopup();
        e.Handled = true;
    }

    private void OnApplyHexColorClicked(object? sender, RoutedEventArgs e)
    {
        ApplyHexColorFromEditor();
        e.Handled = true;
    }

    private void OnColorHexTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyHexColorFromEditor();
        e.Handled = true;
    }

    private async void OnEditStructuredFieldClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (ViewModel is null && field.PropertyType is EditorPropertyType.UdlModuleExposureList)
        {
            return;
        }

        Window? editorWindow = field.PropertyType switch
        {
            EditorPropertyType.TargetTree => new TargetTreeSelectionDialogWindow(ViewModel, field),
            EditorPropertyType.ChartSeriesList => new ChartSeriesEditorDialogWindow(ViewModel, field),
            EditorPropertyType.AttachItemList => new AttachItemsEditorDialogWindow(ViewModel, field),
            EditorPropertyType.InteractionRuleList => new InteractionRulesEditorDialogWindow(ViewModel, field),
            EditorPropertyType.UdlModuleExposureList => new UdlModuleExposureDialogWindow(ViewModel!, field),
            _ => null
        };

        if (editorWindow is null)
        {
            return;
        }

        await editorWindow.ShowDialog(owner);
        e.Handled = true;
    }

    private async void OnOpenFolderPickerClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorDialogField field })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        IStorageFolder? startFolder = null;
        if (!string.IsNullOrWhiteSpace(field.Value))
        {
            startFolder = await owner.StorageProvider.TryGetFolderFromPathAsync(field.Value);
        }

        var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        var selectedPath = TryGetStoragePath(result.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            field.Value = selectedPath;
        }

        e.Handled = true;
    }

    private static string? TryGetStoragePath(IStorageItem? item)
    {
        if (item?.TryGetLocalPath() is { } localPath && !string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var path = item?.Path;
        if (path is null)
        {
            return null;
        }

        if (path.IsAbsoluteUri)
        {
            return path.LocalPath;
        }

        return Uri.UnescapeDataString(path.OriginalString);
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CommitEditorDialog();
        e.Handled = true;
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ApplyEditorDialog();
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelEditorDialog();
        e.Handled = true;
    }

    private void OnSectionToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: EditorDialogSection section })
        {
            section.IsExpanded = !section.IsExpanded;
        }

        e.Handled = true;
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ViewModel?.CommitEditorDialog();
        e.Handled = true;
    }

    private void BuildColorPalette()
    {
        if (this.FindControl<WrapPanel>("ColorPalettePanel") is not { } panel || panel.Children.Count > 0)
        {
            return;
        }

        foreach (var color in StandardColors)
        {
            var colorText = ToHex(color);
            var button = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 6),
                Tag = colorText,
                Content = new Border
                {
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6)
                },
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            button.Click += OnPaletteColorClicked;
            button.PointerPressed += OnInteractivePointerPressed;
            panel.Children.Add(button);
        }
    }

    private void ApplyHexColorFromEditor()
    {
        if (_activeColorField is null || this.FindControl<TextBox>("ColorHexTextBox") is not { } hexBox)
        {
            return;
        }

        _activeColorField.Value = NormalizeColorText(hexBox.Text);
        CloseColorPopup();
    }

    private void CloseColorPopup()
    {
        if (this.FindControl<Popup>("ColorPopup") is { } popup)
        {
            popup.IsOpen = false;
        }
    }

    private static string NormalizeColorText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "Transparent";
        }

        return Color.TryParse(trimmed, out var color) ? ToHex(color) : trimmed;
    }

    private static string ToHex(Color color)
        => color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
