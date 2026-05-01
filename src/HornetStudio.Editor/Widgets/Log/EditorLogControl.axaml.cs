using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using HornetStudio.Logging;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EditorLogControl : UserControl
{
    public static readonly StyledProperty<bool> PageIsActiveProperty =
        AvaloniaProperty.Register<EditorLogControl, bool>(nameof(PageIsActive), true);

    private static readonly IBrush ActiveButtonBackground = new SolidColorBrush(Color.Parse("#916afd"));
    private static readonly IBrush ActiveButtonBorderBrush = new SolidColorBrush(Color.Parse("#916afd"));
    private static readonly IBrush ActiveButtonForeground = Brushes.White;
    private static readonly IBrush InactiveButtonBackground = new SolidColorBrush(Color.Parse("#F3F4F6"));
    private static readonly IBrush InactiveButtonBorderBrush = new SolidColorBrush(Color.Parse("#C7CDD8"));
    private static readonly IBrush InactiveButtonForeground = new SolidColorBrush(Color.Parse("#111827"));

    private ListBox? _logListBox;
    private ScrollViewer? _scrollViewer;
    private ProcessLog? _processLog;
    private FolderItemModel? _observedLogItem;
    private MainWindowViewModel? _observedViewModel;
    private Button? _debugButton;
    private Button? _infoButton;
    private Button? _warningButton;
    private Button? _errorButton;
    private Button? _fatalButton;
    private Button? _pauseButton;
    private Button? _openFolderButton;
    private ThemeSvgIcon? _pauseIcon;
    private ThemeSvgIcon? _openFolderIcon;

    public EditorLogControl()
    {
        LogEntries = [];
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    public ObservableCollection<LogDisplayEntry> LogEntries { get; }

    public bool PageIsActive
    {
        get => GetValue(PageIsActiveProperty);
        set => SetValue(PageIsActiveProperty, value);
    }

    private FolderItemModel? LogItem => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel } ? viewModel : null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageIsActiveProperty && PageIsActive)
        {
            ReloadEntries();
            UpdateFilterButtons();
        }
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private async void OnLogListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        await CopySelectedEntriesToClipboardAsync();
        e.Handled = true;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _logListBox = this.FindControl<ListBox>("LogListBox");
        _debugButton = this.FindControl<Button>("DebugButton");
        _infoButton = this.FindControl<Button>("InfoButton");
        _warningButton = this.FindControl<Button>("WarningButton");
        _errorButton = this.FindControl<Button>("ErrorButton");
        _fatalButton = this.FindControl<Button>("FatalButton");
        _pauseButton = this.FindControl<Button>("PauseButton");
        _openFolderButton = this.FindControl<Button>("OpenFolderButton");
        _pauseIcon = this.FindControl<ThemeSvgIcon>("PauseIcon");
        _openFolderIcon = this.FindControl<ThemeSvgIcon>("OpenFolderIcon");
        HookObservedViewModel();
        HookObservedLogItem();
        ResolveProcessLog();
        ReloadEntries();
        ResolveScrollViewer();
        Dispatcher.UIThread.Post(ResolveScrollViewer, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(UpdateFilterButtons, DispatcherPriority.Loaded);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookObservedViewModel();
        UnhookObservedLogItem();
        UnhookProcessLog();
        _debugButton = null;
        _infoButton = null;
        _warningButton = null;
        _errorButton = null;
        _fatalButton = null;
        _pauseButton = null;
        _openFolderButton = null;
        _pauseIcon = null;
        _openFolderIcon = null;
        _scrollViewer = null;
        _logListBox = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedViewModel();
        HookObservedLogItem();
        ResolveProcessLog();
        ReloadEntries();
        UpdateFilterButtons();
    }

    private void ResolveProcessLog()
    {
        var resolved = ResolveProcessLogFromTarget(LogItem?.TargetLog);
        if (ReferenceEquals(_processLog, resolved))
        {
            return;
        }

        UnhookProcessLog();
        _processLog = resolved;
        if (_processLog is not null)
        {
            _processLog.EntryAdded += OnLogEntryAdded;
            _processLog.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        UpdateFilterButtons();
    }

    private void HookObservedLogItem()
    {
        if (ReferenceEquals(_observedLogItem, LogItem))
        {
            return;
        }

        UnhookObservedLogItem();
        _observedLogItem = LogItem;
        if (_observedLogItem is not null)
        {
            _observedLogItem.PropertyChanged += OnLogItemPropertyChanged;
        }
    }

    private void HookObservedViewModel()
    {
        if (ReferenceEquals(_observedViewModel, ViewModel))
        {
            return;
        }

        UnhookObservedViewModel();
        _observedViewModel = ViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnhookObservedLogItem()
    {
        if (_observedLogItem is null)
        {
            return;
        }

        _observedLogItem.PropertyChanged -= OnLogItemPropertyChanged;
        _observedLogItem = null;
    }

    private void UnhookObservedViewModel()
    {
        if (_observedViewModel is null)
        {
            return;
        }

        _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = null;
    }

    private void UnhookProcessLog()
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.EntryAdded -= OnLogEntryAdded;
        _processLog.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _processLog = null;
    }

    private void OnLogItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderItemModel.TargetLog))
        {
            ResolveProcessLog();
            ReloadEntries();
            UpdateFilterButtons();
            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBackground)
            or nameof(FolderItemModel.EffectiveBorderBrush)
            or nameof(FolderItemModel.EffectivePrimaryForeground)
            or nameof(FolderItemModel.EffectiveSecondaryForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground)
            or nameof(FolderItemModel.EffectiveContainerBackground)
            or nameof(FolderItemModel.EffectiveContainerBorderBrush))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshEntryColors();
                UpdateFilterButtons();
            });
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsDarkTheme)
            or nameof(MainWindowViewModel.IsEditMode)
            or nameof(MainWindowViewModel.EditPanelButtonBackground)
            or nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            or nameof(MainWindowViewModel.EditPanelInputForeground))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshEntryColors();
                UpdateFilterButtons();
            });
        }
    }

    private void OnDisplaySettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFilterButtons();
            ReloadEntries();
        });
    }

    private void OnDebugFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.ShowDebug = !_processLog.ShowDebug;
        e.Handled = true;
    }

    private void OnInfoFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.ShowInfo = !_processLog.ShowInfo;
        e.Handled = true;
    }

    private void OnWarningFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.ShowWarning = !_processLog.ShowWarning;
        e.Handled = true;
    }

    private void OnErrorFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.ShowError = !_processLog.ShowError;
        e.Handled = true;
    }

    private void OnFatalFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.ShowFatal = !_processLog.ShowFatal;
        e.Handled = true;
    }

    private void OnPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (_processLog is null)
        {
            return;
        }

        _processLog.Pause = !_processLog.Pause;
        e.Handled = true;
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        _processLog?.OpenLogDirectory();
        e.Handled = true;
    }

    private void OnLogEntryAdded(ProcessLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!PageIsActive)
            {
                return;
            }

            LogEntries.Add(CreateDisplayEntry(entry));
            ScrollToEnd();
        });
    }

    private void ReloadEntries()
    {
        LogEntries.Clear();
        foreach (var entry in _processLog?.GetEntries() ?? [])
        {
            LogEntries.Add(CreateDisplayEntry(entry));
        }

        ScrollToEnd();
    }

    private void UpdateFilterButtons()
    {
        SetFilterButtonState(_debugButton, _processLog?.ShowDebug ?? true);
        SetFilterButtonState(_infoButton, _processLog?.ShowInfo ?? true);
        SetFilterButtonState(_warningButton, _processLog?.ShowWarning ?? true);
        SetFilterButtonState(_errorButton, _processLog?.ShowError ?? true);
        SetFilterButtonState(_fatalButton, _processLog?.ShowFatal ?? true);
        UpdatePauseButton();
        UpdateChromeButtons();
    }

    private static void SetFilterButtonState(Button? button, bool isActive)
    {
        if (button is null)
        {
            return;
        }

        button.Background = isActive ? ActiveButtonBackground : InactiveButtonBackground;
        button.BorderBrush = isActive ? ActiveButtonBorderBrush : InactiveButtonBorderBrush;
        button.Foreground = isActive ? ActiveButtonForeground : InactiveButtonForeground;
        button.Opacity = isActive ? 1 : 0.55;
    }

    private void UpdatePauseButton()
    {
        if (_pauseButton is null || _pauseIcon is null)
        {
            return;
        }

        var isPaused = _processLog?.Pause ?? false;
        ApplyThemeButtonState(_pauseButton);
        _pauseButton.Opacity = 1;
        _pauseIcon.TintColor = GetThemeIconTint();
        _pauseIcon.IconPath = isPaused
            ? "avares://HornetStudio.Editor/EditorIcons/play.svg"
            : "avares://HornetStudio.Editor/EditorIcons/pause.svg";
    }

    private void UpdateChromeButtons()
    {
        ApplyThemeButtonState(_openFolderButton);
        if (_openFolderIcon is not null)
        {
            _openFolderIcon.TintColor = GetThemeIconTint();
        }
    }

    private void ApplyThemeButtonState(Button? button)
    {
        if (button is null)
        {
            return;
        }

        button.Background = GetThemeButtonBackground();
        button.BorderBrush = GetThemeButtonBorderBrush();
        button.Foreground = InactiveButtonForeground;
    }

    private IBrush GetThemeButtonBackground()
    {
        return TryParseThemeBrush(ViewModel?.EditPanelButtonBackground) ?? InactiveButtonBackground;
    }

    private IBrush GetThemeButtonBorderBrush()
    {
        return TryParseThemeBrush(ViewModel?.EditPanelButtonBorderBrush) ?? InactiveButtonBorderBrush;
    }

    private string GetThemeIconTint()
    {
        return string.IsNullOrWhiteSpace(ViewModel?.EditPanelInputForeground)
            ? "#111827"
            : ViewModel!.EditPanelInputForeground;
    }

    private static IBrush? TryParseThemeBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Brush.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private void RefreshEntryColors()
    {
        foreach (var entry in LogEntries)
        {
            entry.Foreground = GetLogForeground(entry.Level);
        }
    }

    private LogDisplayEntry CreateDisplayEntry(ProcessLogEntry entry)
    {
        return new LogDisplayEntry(FormatEntry(entry), entry.Level, GetLogForeground(entry.Level));
    }

    private IBrush GetLogForeground(string level)
    {
        var theme = ViewModel?.IsDarkTheme == true ? ThemePalette.Dark : ThemePalette.Light;
        var color = level switch
        {
            "Debug" => theme.LogDebugForeground,
            "Information" => theme.LogInfoForeground,
            "Warning" => theme.LogWarningForeground,
            "Error" => theme.LogErrorForeground,
            "Fatal" => theme.LogFatalForeground,
            _ => LogItem?.EffectiveSecondaryForeground ?? theme.LogInfoForeground
        };

        return TryParseThemeBrush(color) ?? InactiveButtonForeground;
    }

    private void ResolveScrollViewer()
    {
        if (_logListBox is null)
        {
            return;
        }

        _scrollViewer = _logListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        _scrollViewer?.ScrollToEnd();
    }

    private async Task CopySelectedEntriesToClipboardAsync()
    {
        if (_logListBox?.SelectedItems is not { Count: > 0 } selectedItems)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, selectedItems
            .OfType<LogDisplayEntry>()
            .Select(entry => entry.Text));

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }

    private static ProcessLog? ResolveProcessLogFromTarget(string? targetLog)
    {
        if (TryResolveProcessLog(targetLog, out var resolved))
        {
            return resolved;
        }

        return TryResolveProcessLog("Logs.Host", out resolved) ? resolved : null;
    }

    private static bool TryResolveProcessLog(string? targetLog, out ProcessLog? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(targetLog))
        {
            return false;
        }

        var normalized = HornetStudio.Editor.Helpers.TargetPathHelper.NormalizeConfiguredTargetPath(targetLog);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (HostRegistries.Data.TryResolve(normalized, out var item) && item?.Value is ProcessLog processLog)
        {
            resolved = processLog;
            return true;
        }

        if (!normalized.Contains('.', StringComparison.Ordinal)
            && HostRegistries.Data.TryResolve($"Logs.{normalized}", out item)
            && item?.Value is ProcessLog legacyProcessLog)
        {
            resolved = legacyProcessLog;
            return true;
        }

        return false;
    }

    private static string FormatEntry(ProcessLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Exception))
        {
            return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}";
        }

        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}{Environment.NewLine}{entry.Exception}";
    }
}

public partial class EditorLogWidget : EditorLogControl
{
}

public sealed class LogDisplayEntry : INotifyPropertyChanged
{
    private IBrush _foreground;

    public LogDisplayEntry(string text, string level, IBrush foreground)
    {
        Text = text;
        Level = level;
        _foreground = foreground;
    }

    public string Text { get; }

    public string Level { get; }

    public IBrush Foreground
    {
        get => _foreground;
        set
        {
            if (ReferenceEquals(_foreground, value))
            {
                return;
            }

            _foreground = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

