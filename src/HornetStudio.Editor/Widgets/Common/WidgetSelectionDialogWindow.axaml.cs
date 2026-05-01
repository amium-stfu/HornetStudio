using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;
using HornetStudio.EditorUi;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public sealed partial class WidgetSelectionDialogWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly IReadOnlyList<WidgetSelectionItem> _availableWidgets;
    private readonly PreviewHost _previewHost = new();
    private MainWindowViewModel? _subscribedViewModel;
    private string _filterText = string.Empty;
    private WidgetSelectionItem? _selectedWidget;
    private string _dialogBackground = "#FFFFFF";
    private string _panelBackground = "#F8FAFC";
    private string _borderColor = "#CBD5E1";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";
    private string _editorBackground = "#FFFFFF";
    private string _editorForeground = "#111827";
    private string _buttonBackground = "#F8FAFC";
    private string _buttonBorderBrush = "#CBD5E1";
    private string _buttonForeground = "#111827";
    private string _descriptionText = string.Empty;
    private object? _previewContent;
    private string _confirmButtonText = "Select";

    public WidgetSelectionDialogWindow()
        : this(null, Array.Empty<WidgetSelectionItem>(), "Select")
    {
    }

    public WidgetSelectionDialogWindow(MainWindowViewModel? viewModel, IReadOnlyList<WidgetSelectionItem> widgets, string confirmButtonText)
    {
        _viewModel = viewModel;
        _availableWidgets = widgets;
        Widgets = [];
        _confirmButtonText = string.IsNullOrWhiteSpace(confirmButtonText) ? "Select" : confirmButtonText.Trim();
        InitializeComponent();
        DataContext = this;
        AttachToViewModel(_viewModel);
        RefreshWidgetList();
        Closed += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WidgetSelectionItem> Widgets { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_filterText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _filterText = normalized;
            OnPropertyChanged(nameof(FilterText));
            RefreshWidgetList();
        }
    }

    public WidgetSelectionItem? SelectedWidget
    {
        get => _selectedWidget;
        set
        {
            if (Equals(_selectedWidget, value))
            {
                return;
            }

            _selectedWidget = value;
            OnPropertyChanged(nameof(SelectedWidget));
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(SelectedWidgetTitle));
            OnPropertyChanged(nameof(SelectedWidgetSummary));
            UpdatePreview();
            UpdateDescriptionText();
        }
    }

    public bool CanSelect => SelectedWidget is not null;

    public string SelectedWidgetTitle => SelectedWidget?.DisplayName ?? "No widget selected";

    public string SelectedWidgetSummary => SelectedWidget?.Summary ?? "Select a widget type on the left to preview it here.";

    public string DescriptionText
    {
        get => _descriptionText;
        private set => SetAndRaise(ref _descriptionText, value, nameof(DescriptionText));
    }

    public object? PreviewContent
    {
        get => _previewContent;
        private set => SetAndRaise(ref _previewContent, value, nameof(PreviewContent));
    }

    public string ConfirmButtonText
    {
        get => _confirmButtonText;
        private set => SetAndRaise(ref _confirmButtonText, value, nameof(ConfirmButtonText));
    }

    public string DialogBackground
    {
        get => _dialogBackground;
        private set => SetAndRaise(ref _dialogBackground, value, nameof(DialogBackground));
    }

    public string PanelBackground
    {
        get => _panelBackground;
        private set => SetAndRaise(ref _panelBackground, value, nameof(PanelBackground));
    }

    public string BorderColor
    {
        get => _borderColor;
        private set => SetAndRaise(ref _borderColor, value, nameof(BorderColor));
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetAndRaise(ref _primaryTextBrush, value, nameof(PrimaryTextBrush));
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetAndRaise(ref _secondaryTextBrush, value, nameof(SecondaryTextBrush));
    }

    public string EditorBackground
    {
        get => _editorBackground;
        private set => SetAndRaise(ref _editorBackground, value, nameof(EditorBackground));
    }

    public string EditorForeground
    {
        get => _editorForeground;
        private set => SetAndRaise(ref _editorForeground, value, nameof(EditorForeground));
    }

    public string ButtonBackground
    {
        get => _buttonBackground;
        private set => SetAndRaise(ref _buttonBackground, value, nameof(ButtonBackground));
    }

    public string ButtonBorderBrush
    {
        get => _buttonBorderBrush;
        private set => SetAndRaise(ref _buttonBorderBrush, value, nameof(ButtonBorderBrush));
    }

    public string ButtonForeground
    {
        get => _buttonForeground;
        private set => SetAndRaise(ref _buttonForeground, value, nameof(ButtonForeground));
    }

    public static async System.Threading.Tasks.Task<WidgetSelectionItem?> ShowAsync(Window owner, MainWindowViewModel? viewModel, IReadOnlyList<WidgetSelectionItem> widgets, string confirmButtonText)
    {
        var dialog = new WidgetSelectionDialogWindow(viewModel, widgets, confirmButtonText)
        {
            Owner = owner
        };

        return await dialog.ShowDialog<WidgetSelectionItem?>(owner);
    }

    private void RefreshWidgetList()
    {
        Widgets.Clear();

        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _availableWidgets
            : _availableWidgets.Where(widget => widget.MatchesFilter(FilterText)).ToArray();

        foreach (var widget in filtered)
        {
            Widgets.Add(widget);
        }

        if (Widgets.Count == 0)
        {
            SelectedWidget = null;
            DescriptionText = "No widget matches the current filter.";
            PreviewContent = BuildEmptyPreview();
            return;
        }

        if (SelectedWidget is not null)
        {
            var preserved = Widgets.FirstOrDefault(widget => widget.Kind == SelectedWidget.Kind);
            if (preserved is not null)
            {
                SelectedWidget = preserved;
                return;
            }
        }

        SelectedWidget = Widgets[0];
    }

    private void UpdatePreview()
    {
        if (SelectedWidget is null)
        {
            PreviewContent = BuildEmptyPreview();
            return;
        }

        var previewItem = SelectedWidget.CreatePreviewItem(_viewModel);
        if (previewItem is null)
        {
            PreviewContent = BuildEmptyPreview();
            return;
        }

        PreviewContent = _previewHost.BuildPreview(previewItem);
    }

    private void UpdateDescriptionText()
    {
        if (SelectedWidget is null)
        {
            DescriptionText = "Select a widget to view its description.";
            return;
        }

        var descriptionPath = SelectedWidget.ResolveDescriptionFilePath();
        if (!string.IsNullOrWhiteSpace(descriptionPath) && File.Exists(descriptionPath))
        {
            try
            {
                DescriptionText = File.ReadAllText(descriptionPath);
                return;
            }
            catch
            {
            }
        }

        var helpPath = SelectedWidget.ResolveHelpFilePath();
        if (!string.IsNullOrWhiteSpace(helpPath) && File.Exists(helpPath))
        {
            try
            {
                DescriptionText = File.ReadAllText(helpPath);
                return;
            }
            catch
            {
            }
        }

        DescriptionText = "No widget description is available for this widget yet.";
    }

    private Control BuildEmptyPreview()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = ParseBrush(BorderColor),
            Background = ParseBrush(PanelBackground),
            Child = new TextBlock
            {
                Text = "No preview available.",
                Foreground = ParseBrush(SecondaryTextBrush),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            }
        };
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            ApplyTheme(viewModel);
            UpdateWindowIcon(viewModel);
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

        ApplyTheme(viewModel);
        UpdateWindowIcon(viewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.IsDarkTheme)
            or nameof(MainWindowViewModel.DialogBackground)
            or nameof(MainWindowViewModel.CardBorderBrush)
            or nameof(MainWindowViewModel.PrimaryTextBrush)
            or nameof(MainWindowViewModel.SecondaryTextBrush)
            or nameof(MainWindowViewModel.ParameterEditBackgrundColor)
            or nameof(MainWindowViewModel.ParameterEditForeColor)
            or nameof(MainWindowViewModel.EditPanelButtonBackground)
            or nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            or nameof(MainWindowViewModel.EditorDialogSectionContentBackground))
        {
            ApplyTheme(vm);
            UpdateWindowIcon(vm);
            UpdatePreview();
        }
    }

    private void ApplyTheme(MainWindowViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        DialogBackground = viewModel.DialogBackground;
        PanelBackground = viewModel.EditorDialogSectionContentBackground;
        BorderColor = viewModel.CardBorderBrush;
        PrimaryTextBrush = viewModel.PrimaryTextBrush;
        SecondaryTextBrush = viewModel.SecondaryTextBrush;
        EditorBackground = viewModel.ParameterEditBackgrundColor;
        EditorForeground = viewModel.ParameterEditForeColor;
        ButtonBackground = viewModel.EditPanelButtonBackground;
        ButtonBorderBrush = viewModel.EditPanelButtonBorderBrush;
        ButtonForeground = viewModel.PrimaryTextBrush;
    }

    private void UpdateWindowIcon(MainWindowViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        var iconName = viewModel.IsDarkTheme ? "cogDark.png" : "cogLight.png";
        var uri = new Uri($"avares://HornetStudio.Editor/EditorIcons/{iconName}");

        try
        {
            using var stream = AssetLoader.Open(uri);
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch
        {
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AttachToViewModel(null);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close((WidgetSelectionItem?)null);
        e.Handled = true;
    }

    private void OnSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedWidget is null)
        {
            return;
        }

        Close(SelectedWidget);
        e.Handled = true;
    }

    private void OnWidgetListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedWidget is null)
        {
            return;
        }

        Close(SelectedWidget);
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedWidget is not null)
        {
            Close(SelectedWidget);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close((WidgetSelectionItem?)null);
            e.Handled = true;
        }
    }

    private static IBrush ParseBrush(string? color)
    {
        try
        {
            return string.IsNullOrWhiteSpace(color)
                ? Brushes.Transparent
                : Brush.Parse(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetAndRaise<T>(ref T field, T value, string propertyName)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private sealed class PreviewHost : IEditorUiHost
    {
        public bool IsEditMode => false;

        public string? PrimaryTextBrush => "#111827";

        public void OpenItemEditor(object item, double x, double y)
        {
        }

        public bool DeleteItem(object item) => false;

        public void RefreshFolderBindings(string pageName)
        {
        }

        public Control BuildPreview(FolderItemModel item)
        {
            return CreatePreviewControl(item);
        }

        private Control CreatePreviewControl(FolderItemModel item)
        {
            Control content = item.Kind switch
            {
                ControlKind.Button => new EditorButtonControl(),
                ControlKind.Signal or ControlKind.Item => new EditorSignalControl(),
                ControlKind.WidgetList => new EditorListControl(),
                ControlKind.TableControl => new EditorTableControl(),
                ControlKind.CircleDisplay => new EditorCircleDisplayControl(),
                ControlKind.LogControl => new EditorLogControl
                {
                    PageIsActive = false
                },
                ControlKind.ChartControl => new RealtimeChartControl
                {
                    PageIsActive = true
                },
                ControlKind.UdlClientControl => CreatePlaceholderPreview(item, "UDL client preview"),
                ControlKind.BrokerWidget => CreatePlaceholderPreview(item, "MQTT ItemBroker preview"),
                ControlKind.CsvLoggerControl => CreatePlaceholderPreview(item, "CSV logger preview"),
                ControlKind.SqlLoggerControl => CreatePlaceholderPreview(item, "SQL logger preview"),
                ControlKind.CameraControl => CreatePlaceholderPreview(item, "Camera preview"),
                ControlKind.ApplicationExplorer => CreatePlaceholderPreview(item, "Application overview preview"),
                ControlKind.CustomSignals => CreatePlaceholderPreview(item, "Custom signals preview"),
                ControlKind.EnhancedSignals => CreatePlaceholderPreview(item, "Enhanced signals preview"),
                _ => new EditorSignalControl()
            };

            content.DataContext = item;
            content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            content.Width = item.Width;
            content.Height = item.Height;
            return content;
        }

        private static Control CreatePlaceholderPreview(FolderItemModel item, string subtitle)
        {
            return new EditorTemplateControl
            {
                DataContext = item,
                Width = item.Width,
                Height = item.Height,
                HeaderContent = new TextBlock
                {
                    Text = item.ControlCaption,
                    FontSize = item.ItemControlCaptionFontSize,
                    FontWeight = FontWeight.Medium,
                    Foreground = item.EffectiveHeaderForegroundBrush,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                },
                BodyContent = new Border
                {
                    Background = item.EffectiveBodyBackgroundBrush,
                    BorderBrush = item.EffectiveBodyBorderBrush,
                    BorderThickness = item.EffectiveBodyBorderThickness,
                    CornerRadius = item.EffectiveBodyCornerRadius,
                    Padding = new Thickness(16, 12),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = item.BodyCaption,
                                FontSize = 18,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = item.EffectiveBodyForegroundBrush,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = subtitle,
                                FontSize = 13,
                                Foreground = item.EffectiveMutedForegroundBrush,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                },
                FooterContent = string.IsNullOrWhiteSpace(item.Footer)
                    ? null
                    : new TextBlock
                    {
                        Text = item.Footer,
                        Margin = new Thickness(8, 4, 8, 4),
                        Foreground = item.EffectiveFooterForegroundBrush,
                        TextWrapping = TextWrapping.Wrap
                    }
            };
        }
    }
}

public sealed class WidgetSelectionItem
{
    private readonly Func<MainWindowViewModel?, FolderItemModel?> _previewFactory;

    public WidgetSelectionItem(ControlKind kind, string displayName, string summary, string descriptionFileName, string helpFileName, Func<MainWindowViewModel?, FolderItemModel?> previewFactory)
    {
        Kind = kind;
        DisplayName = displayName;
        Summary = summary;
        DescriptionFileName = descriptionFileName;
        HelpFileName = helpFileName;
        _previewFactory = previewFactory;
    }

    public ControlKind Kind { get; }

    public string DisplayName { get; }

    public string Summary { get; }

    public string DescriptionFileName { get; }

    public string HelpFileName { get; }

    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Kind.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public FolderItemModel? CreatePreviewItem(MainWindowViewModel? viewModel)
        => _previewFactory(viewModel);

    public string ResolveDescriptionFilePath()
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "widgets", "descriptions", DescriptionFileName),
            Path.Combine(AppContext.BaseDirectory, "HornetStudio", "docs", "widgets", "descriptions", DescriptionFileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "HornetStudio", "docs", "widgets", "descriptions", DescriptionFileName),
            Path.Combine(AppContext.BaseDirectory, "..", "HornetStudio", "docs", "widgets", "descriptions", DescriptionFileName)
        };

        foreach (var candidatePath in candidatePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidatePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    public string ResolveHelpFilePath()
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "widgets", "help", HelpFileName),
            Path.Combine(AppContext.BaseDirectory, "HornetStudio", "docs", "widgets", "help", HelpFileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "HornetStudio", "docs", "widgets", "help", HelpFileName),
            Path.Combine(AppContext.BaseDirectory, "..", "HornetStudio", "docs", "widgets", "help", HelpFileName)
        };

        foreach (var candidatePath in candidatePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidatePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }
}
