using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public sealed partial class FunctionBrowserDialogWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private MainWindowViewModel? _subscribedViewModel;
    private readonly FolderItemModel _browserItem;
    private FolderModel _folder;
    private FunctionsControl? _functionsBrowserControl;
    private string _dialogBackground = "#FFFFFF";
    private string _panelBackground = "#F8FAFC";
    private string _borderColor = "#CBD5E1";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";

    public FunctionBrowserDialogWindow()
        : this(null, new FolderModel())
    {
    }

    public FunctionBrowserDialogWindow(MainWindowViewModel? viewModel, FolderModel folder)
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

    public static FunctionBrowserDialogWindow ShowOrActivate(Window? owner, MainWindowViewModel? viewModel, FolderModel folder)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserDialogOpen(owner, nameof(FunctionBrowserDialogWindow), folder.Name);
        var dialog = new FunctionBrowserDialogWindow(viewModel, folder)
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
        dialog._functionsBrowserControl?.RefreshCatalog();
        return dialog;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _functionsBrowserControl = this.FindControl<FunctionsControl>("FunctionsBrowserControl");
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

    public void UpdateFolderContext(FolderModel folder)
    {
        if (folder is null)
        {
            return;
        }

        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(this, nameof(FunctionBrowserDialogWindow), nameof(UpdateFolderContext));
        _folder = folder;
        ApplyFolderContext(folder, refreshCatalog: true);
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
        var item = viewModel?.CreateItem(ControlKind.Functions, 0, 0, 420, 240) ?? new FolderItemModel { Kind = ControlKind.Functions };
        item.Name = "FunctionBrowser";
        item.ControlCaption = "Functions";
        item.BodyCaption = "Folder functions";
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
        Title = $"Function Browser - {folder.Name}";
        _browserItem.Name = "FunctionBrowser";
        _browserItem.ControlCaption = "Functions";
        _browserItem.BodyCaption = $"Folder functions for {folder.Name}";
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
            _functionsBrowserControl?.RefreshCatalog();
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
