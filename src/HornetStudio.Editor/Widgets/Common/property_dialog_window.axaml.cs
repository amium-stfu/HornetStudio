using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class PropertyDialogWindow : Window
{
    private static PropertyDialogWindow? _openInstance;
    private MainWindowViewModel.PropertyDialogSessionViewModel? _session;

    public PropertyDialogWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
    }

    public static PropertyDialogWindow ShowOrActivate(Window? owner, MainWindowViewModel.PropertyDialogSessionViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_openInstance is not null)
        {
            _openInstance.DataContext = session;
            _openInstance.Activate();
            return _openInstance;
        }

        var window = new PropertyDialogWindow
        {
            DataContext = session
        };

        _openInstance = window;
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        return window;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachSession();
        if (ReferenceEquals(_openInstance, this))
        {
            _openInstance = null;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachSession();

        if (DataContext is not MainWindowViewModel.PropertyDialogSessionViewModel session)
        {
            return;
        }

        _session = session;
        _session.CloseRequested += OnSessionCloseRequested;
        _session.PropertyChanged += OnSessionPropertyChanged;
        UpdateWindowIcon(session.OwnerViewModel);
    }

    private void OnSessionCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        {
            UpdateWindowIcon(_session?.OwnerViewModel);
        }
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.CloseRequested -= OnSessionCloseRequested;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.Dispose();
        _session = null;
    }

    private void UpdateWindowIcon(MainWindowViewModel? viewModel)
    {
        var iconName = viewModel?.IsDarkTheme == true ? "cogDark.png" : "cogLight.png";
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
}