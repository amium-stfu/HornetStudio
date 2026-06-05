using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Controls;

public partial class EditorTemplateControl : EditorShellControl
{
    public static readonly StyledProperty<object?> HeaderActionsContentProperty =
        AvaloniaProperty.Register<EditorTemplateControl, object?>(nameof(HeaderActionsContent));

    public static readonly DirectProperty<EditorTemplateControl, bool> HostIsEditModeProperty =
        AvaloniaProperty.RegisterDirect<EditorTemplateControl, bool>(nameof(HostIsEditMode), control => control.HostIsEditMode);

    public static readonly DirectProperty<EditorTemplateControl, string?> HostPrimaryTextBrushProperty =
        AvaloniaProperty.RegisterDirect<EditorTemplateControl, string?>(nameof(HostPrimaryTextBrush), control => control.HostPrimaryTextBrush);

    public static readonly StyledProperty<int> ViewProperty =
        AvaloniaProperty.Register<EditorTemplateControl, int>(nameof(View), 1);

    private IEditorUiHost? _host;
    private INotifyPropertyChanged? _hostNotifier;
    private MainWindowViewModel? _browserActivityViewModel;
    private INotifyPropertyChanged? _browserActivityNotifier;
    private bool _isAttachedToVisualTree;
    private bool _isBrowserRefreshActive;
    private bool _isBrowserRefreshDirty;
    private bool _hostIsEditMode;
    private string? _hostPrimaryTextBrush = "#111827";

    public EditorTemplateControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public object? HeaderActionsContent
    {
        get => GetValue(HeaderActionsContentProperty);
        set => SetValue(HeaderActionsContentProperty, value);
    }

    public bool HostIsEditMode
    {
        get => _hostIsEditMode;
        private set => SetAndRaise(HostIsEditModeProperty, ref _hostIsEditMode, value);
    }

    public string? HostPrimaryTextBrush
    {
        get => _hostPrimaryTextBrush;
        private set => SetAndRaise(HostPrimaryTextBrushProperty, ref _hostPrimaryTextBrush, value);
    }

    public int View
    {
        get => GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    protected object? ItemContext => DataContext;

    protected IEditorUiHost? Host => _host;

    protected bool IsBrowserRefreshActive => _isBrowserRefreshActive;

    protected bool HasPendingBrowserRefresh => _isBrowserRefreshDirty;

    protected void HandleInteractivePointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    protected void HandleSettingsClicked(RoutedEventArgs e)
    {
        if (ItemContext is null || Host is null)
        {
            return;
        }

        var anchorTarget = this.GetVisualRoot() as Visual;
        var anchor = anchorTarget is null
            ? new Point(24, 24)
            : this.TranslatePoint(new Point(Bounds.Width + 8, 0), anchorTarget) ?? new Point(24, 24);

        Host.OpenItemEditor(ItemContext, anchor.X, anchor.Y);
        e.Handled = true;
    }

    protected bool TryRunBrowserRefresh(Action refreshAction)
    {
        if (!IsBrowserRefreshActive)
        {
            _isBrowserRefreshDirty = true;
            return false;
        }

        _isBrowserRefreshDirty = false;
        refreshAction();
        return true;
    }

    protected void MarkBrowserRefreshDirty()
    {
        _isBrowserRefreshDirty = true;
    }

    protected void RefreshBrowserActivityState()
    {
        UpdateBrowserActivityViewModel();
        UpdateBrowserRefreshActivity();
    }

    protected static string BuildBrowserDiagnosticsSource(string baseName, FolderItemModel? item)
    {
        if (item is null)
        {
            return baseName;
        }

        var scope = !string.IsNullOrWhiteSpace(item.FolderName)
            ? item.FolderName
            : !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : item.Path;

        return string.IsNullOrWhiteSpace(scope)
            ? baseName
            : $"{baseName}[{scope}]";
    }

    protected virtual void OnBrowserRefreshActivated()
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            UpdateBrowserRefreshActivity();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        ResolveHost();
        UpdateBrowserActivityViewModel();
        UpdateBrowserRefreshActivity();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        AttachToHost(null);
        AttachToBrowserActivityViewModel(null);
        UpdateBrowserRefreshActivity();
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        HandleSettingsClicked(e);
    }

    private async void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemContext is null || Host is null)
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

        Host.DeleteItem(ItemContext);
        e.Handled = true;
    }

    private void ResolveHost()
    {
        var resolved = this.GetVisualRoot() is TopLevel { DataContext: IEditorUiHost host }
            ? host
            : null;

        AttachToHost(resolved);
        RefreshHostBindings();
    }

    private void AttachToHost(IEditorUiHost? host)
    {
        if (ReferenceEquals(_host, host))
        {
            return;
        }

        if (_hostNotifier is not null)
        {
            _hostNotifier.PropertyChanged -= OnHostPropertyChanged;
        }

        _host = host;
        _hostNotifier = host as INotifyPropertyChanged;

        if (_hostNotifier is not null)
        {
            _hostNotifier.PropertyChanged += OnHostPropertyChanged;
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(IEditorUiHost.IsEditMode)
            || e.PropertyName == nameof(IEditorUiHost.PrimaryTextBrush))
        {
            RefreshHostBindings();
        }
    }

    private void RefreshHostBindings()
    {
        HostIsEditMode = _host?.IsEditMode ?? false;
        HostPrimaryTextBrush = string.IsNullOrWhiteSpace(_host?.PrimaryTextBrush) ? "#111827" : _host.PrimaryTextBrush;
    }

    private void UpdateBrowserActivityViewModel()
    {
        var viewModel = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        AttachToBrowserActivityViewModel(viewModel);
    }

    private void AttachToBrowserActivityViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_browserActivityViewModel, viewModel))
        {
            return;
        }

        if (_browserActivityNotifier is not null)
        {
            _browserActivityNotifier.PropertyChanged -= OnBrowserActivityViewModelPropertyChanged;
        }

        _browserActivityViewModel = viewModel;
        _browserActivityNotifier = viewModel as INotifyPropertyChanged;

        if (_browserActivityNotifier is not null)
        {
            _browserActivityNotifier.PropertyChanged += OnBrowserActivityViewModelPropertyChanged;
        }
    }

    private void OnBrowserActivityViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder))
        {
            UpdateBrowserRefreshActivity();
        }
    }

    private void UpdateBrowserRefreshActivity()
    {
        var isActive = ComputeBrowserRefreshActive();
        if (_isBrowserRefreshActive == isActive)
        {
            return;
        }

        var wasActive = _isBrowserRefreshActive;
        _isBrowserRefreshActive = isActive;
        if (!wasActive && isActive && _isBrowserRefreshDirty)
        {
            OnBrowserRefreshActivated();
        }
    }

    private bool ComputeBrowserRefreshActive()
    {
        if (!_isAttachedToVisualTree || !IsVisible)
        {
            return false;
        }

        var folderHost = this.FindAncestorOfType<HornetStudio.Editor.Widgets.FolderEditorControl>();
        if (folderHost?.Folder is null)
        {
            return true;
        }

        var selectedFolder = _browserActivityViewModel?.SelectedFolder;
        return selectedFolder is null || ReferenceEquals(folderHost.Folder, selectedFolder);
    }

    private static async Task<bool> ShowDeleteDialogAsync(Window owner)
    {
        var dialog = new Window
        {
            Title = "L�schen",
            Width = 320,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.Full
        };

        var text = new TextBlock
        {
            Text = "Wirklich l�schen?",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var yesButton = new Button
        {
            Content = "Ja",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var noButton = new Button
        {
            Content = "Nein",
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
}

// Alias class so existing code can refer to EditorTemplateWidget while XAML and
// property registrations continue to use EditorTemplateControl as the owner type.
public partial class EditorTemplateWidget : EditorTemplateControl
{
}
