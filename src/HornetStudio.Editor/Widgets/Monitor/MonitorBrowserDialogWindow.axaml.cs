using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public sealed partial class MonitorBrowserDialogWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private MainWindowViewModel? _subscribedViewModel;
    private readonly FolderItemModel _browserItem;
    private FolderModel _folder;
    private MonitorControl? _monitorBrowserControl;
    private string _dialogBackground = "#FFFFFF";
    private string _panelBackground = "#F8FAFC";
    private string _borderColor = "#CBD5E1";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";

    public MonitorBrowserDialogWindow()
        : this(null, new FolderModel())
    {
    }

    public MonitorBrowserDialogWindow(MainWindowViewModel? viewModel, FolderModel folder)
    {
        _viewModel = viewModel;
        _folder = folder;
        _browserItem = CreateBrowserItem(viewModel, folder);
        InitializeComponent();
        DataContext = this;
        AttachToViewModel(viewModel);
        ApplyFolderContext(folder, refreshCatalog: false);
        Closed += OnClosed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel? EditorViewModel => _viewModel;

    public FolderItemModel BrowserItem => _browserItem;

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

    public static MonitorBrowserDialogWindow ShowOrActivate(Window? owner, MainWindowViewModel? viewModel, FolderModel folder)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserDialogOpen(owner, nameof(MonitorBrowserDialogWindow), folder.Name);
        var dialog = new MonitorBrowserDialogWindow(viewModel, folder)
        {
            Owner = owner
        };

        if (owner is null)
        {
            dialog.Show();
        }
        else
        {
            dialog.Show(owner);
        }

        dialog._monitorBrowserControl?.RefreshCatalog();
        return dialog;
    }

    public void UpdateFolderContext(FolderModel folder)
    {
        if (folder is null)
        {
            return;
        }

        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(this, nameof(MonitorBrowserDialogWindow), nameof(UpdateFolderContext));
        _folder = folder;
        ApplyFolderContext(folder, refreshCatalog: true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _monitorBrowserControl = this.FindControl<MonitorControl>("MonitorBrowserControl");
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            RefreshThemeBindings();
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

        RefreshThemeBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder)
            || e.PropertyName == nameof(MainWindowViewModel.DialogBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        {
            RefreshThemeBindings();
            _browserItem.ApplyTheme(_viewModel?.IsDarkTheme == true);

            if (string.IsNullOrWhiteSpace(e.PropertyName)
                || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder))
            {
                UpdateFolderContext(_viewModel?.SelectedFolder ?? _folder);
            }
        }
    }

    private void RefreshThemeBindings()
    {
        DialogBackground = _viewModel?.DialogBackground ?? "#FFFFFF";
        PanelBackground = _viewModel?.CardBackground ?? "#F8FAFC";
        BorderColor = _viewModel?.CardBorderBrush ?? "#CBD5E1";
        PrimaryTextBrush = _viewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = _viewModel?.SecondaryTextBrush ?? "#5E6777";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        Closed -= OnClosed;
    }

    private static FolderItemModel CreateBrowserItem(MainWindowViewModel? viewModel, FolderModel folder)
    {
        var item = viewModel?.CreateItem(ControlKind.Monitor, 0, 0, 420, 240) ?? new FolderItemModel { Kind = ControlKind.Monitor };
        item.Name = "MonitorBrowser";
        item.ControlCaption = "Monitor";
        item.BodyCaption = "Folder monitor rules";
        item.Footer = string.Empty;
        item.ShowFooter = true;
        item.ShowBodyCaption = true;
        item.CaptionVisible = true;
        item.BodyCaptionVisible = true;
        item.SetHierarchy(folder.Name, parentItem: null, activeViewId: folder.ActualViewId);
        item.ApplyActiveView(folder.ActualViewId);
        item.SetLayoutFilePath(folder.UiFilePath);
        item.ApplyTheme(viewModel?.IsDarkTheme == true);
        return item;
    }

    private void ApplyFolderContext(FolderModel folder, bool refreshCatalog)
    {
        Title = $"Monitor Browser - {folder.Name}";
        _browserItem.Name = "MonitorBrowser";
        _browserItem.ControlCaption = "Monitor";
        _browserItem.BodyCaption = $"Folder monitor rules for {folder.Name}";
        _browserItem.Footer = string.Empty;
        _browserItem.ShowFooter = true;
        _browserItem.ShowBodyCaption = true;
        _browserItem.CaptionVisible = true;
        _browserItem.BodyCaptionVisible = true;
        _browserItem.SetHierarchy(folder.Name, parentItem: null, activeViewId: folder.ActualViewId);
        _browserItem.ApplyActiveView(folder.ActualViewId);
        _browserItem.SetLayoutFilePath(folder.UiFilePath);
        _browserItem.ApplyTheme(_viewModel?.IsDarkTheme == true);

        if (refreshCatalog)
        {
            _monitorBrowserControl?.RefreshCatalog();
        }
    }

    private bool SetAndRaise(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
