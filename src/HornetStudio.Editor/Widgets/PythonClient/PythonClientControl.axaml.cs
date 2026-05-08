using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Widgets;

public partial class PythonClientControl : EditorTemplateControl
{
    public static readonly DirectProperty<PythonClientControl, string> RuntimeStatusTextProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, string>(nameof(RuntimeStatusText), control => control.RuntimeStatusText);

    public static readonly DirectProperty<PythonClientControl, string> RuntimeDetailTextProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, string>(nameof(RuntimeDetailText), control => control.RuntimeDetailText);

    public static readonly DirectProperty<PythonClientControl, bool> CanToggleRuntimeProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, bool>(nameof(CanToggleRuntime), control => control.CanToggleRuntime);

    public static readonly DirectProperty<PythonClientControl, string> RuntimeToggleTextProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, string>(nameof(RuntimeToggleText), control => control.RuntimeToggleText);

    public static readonly DirectProperty<PythonClientControl, IBrush> RuntimeStatusBackgroundProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, IBrush>(nameof(RuntimeStatusBackground), control => control.RuntimeStatusBackground);

    public static readonly DirectProperty<PythonClientControl, IBrush> RuntimeStatusForegroundProperty =
        AvaloniaProperty.RegisterDirect<PythonClientControl, IBrush>(nameof(RuntimeStatusForeground), control => control.RuntimeStatusForeground);

    private static readonly IBrush RunningBackgroundBrush = new SolidColorBrush(Color.Parse("#14532D"));
    private static readonly IBrush RunningForegroundBrush = new SolidColorBrush(Color.Parse("#DCFCE7"));
    private static readonly IBrush StoppedBackgroundBrush = new SolidColorBrush(Color.Parse("#1F2937"));
    private static readonly IBrush StoppedForegroundBrush = new SolidColorBrush(Color.Parse("#F9FAFB"));
    private static readonly IBrush ErrorBackgroundBrush = new SolidColorBrush(Color.Parse("#991B1B"));
    private static readonly IBrush ErrorForegroundBrush = new SolidColorBrush(Color.Parse("#FEE2E2"));

    private FolderItemModel? _observedItem;
    private ATask? _runtimeTask;
    private bool _isStopping;
    private long _runtimeInstanceId;
    private RuntimeState _runtimeState = RuntimeState.Stopped;
    private string _runtimeStatusText = "Stopped";
    private string _runtimeDetailText = "Configure a Python client script.";
    private bool _canToggleRuntime;
    private string _runtimeToggleText = "Start";
    private IBrush _runtimeStatusBackground = StoppedBackgroundBrush;
    private IBrush _runtimeStatusForeground = StoppedForegroundBrush;

    private FolderItemModel? Item => DataContext as FolderItemModel;

    public PythonClientControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private enum RuntimeState
    {
        Stopped,
        Running,
        Stopping,
        Error
    }

    public string RuntimeStatusText
    {
        get => _runtimeStatusText;
        private set => SetAndRaise(RuntimeStatusTextProperty, ref _runtimeStatusText, value);
    }

    public string RuntimeDetailText
    {
        get => _runtimeDetailText;
        private set => SetAndRaise(RuntimeDetailTextProperty, ref _runtimeDetailText, value);
    }

    public bool CanToggleRuntime
    {
        get => _canToggleRuntime;
        private set => SetAndRaise(CanToggleRuntimeProperty, ref _canToggleRuntime, value);
    }

    public string RuntimeToggleText
    {
        get => _runtimeToggleText;
        private set => SetAndRaise(RuntimeToggleTextProperty, ref _runtimeToggleText, value);
    }

    public IBrush RuntimeStatusBackground
    {
        get => _runtimeStatusBackground;
        private set => SetAndRaise(RuntimeStatusBackgroundProperty, ref _runtimeStatusBackground, value);
    }

    public IBrush RuntimeStatusForeground
    {
        get => _runtimeStatusForeground;
        private set => SetAndRaise(RuntimeStatusForegroundProperty, ref _runtimeStatusForeground, value);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RefreshPresentation();
    }

    private async void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        await StopRuntimeAsync("Stopped");
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RefreshPresentation();
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, Item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = Item;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
        }
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is null)
        {
            return;
        }

        _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        _observedItem = null;
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName is nameof(FolderItemModel.Name)
            or nameof(FolderItemModel.PythonScriptPath)
            or nameof(FolderItemModel.Footer))
        {
            RefreshPresentation();
        }

        if (_runtimeState == RuntimeState.Running
            && (e.PropertyName is nameof(FolderItemModel.Name)
                || !HasValidConfiguration(_observedItem, out _)))
        {
            _ = StopRuntimeAsync("Configuration changed");
        }
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private void OnToggleRuntimeClicked(object? sender, RoutedEventArgs e)
    {
        if (_isStopping)
        {
            e.Handled = true;
            return;
        }

        if (_runtimeState == RuntimeState.Running)
        {
            _ = StopRuntimeAsync("Stopped");
        }
        else
        {
            _ = StartRuntimeAsync();
        }

        e.Handled = true;
    }

    private async Task StartRuntimeAsync()
    {
        if (_runtimeState == RuntimeState.Running || _isStopping)
        {
            return;
        }

        var item = _observedItem;
        if (!HasValidConfiguration(item, out var scriptPath))
        {
            SetStoppedState("Configure a Python client script.");
            return;
        }

        var previousTask = _runtimeTask;
        _runtimeTask = null;
        if (previousTask is not null)
        {
            await Task.Run(previousTask.Stop);
        }

        var runtimeInstanceId = Interlocked.Increment(ref _runtimeInstanceId);
        SetRunningState($"Starting {Path.GetFileName(scriptPath)}");

        var instanceName = BuildRuntimeInstanceName(item!, runtimeInstanceId);
        _runtimeTask = new ATask(instanceName, async token => await RunRuntimeAsync(item!, scriptPath, token, runtimeInstanceId));
        await Task.CompletedTask;
    }

    private async Task StopRuntimeAsync(string detail)
    {
        if (_isStopping)
        {
            return;
        }

        _isStopping = true;
        Interlocked.Increment(ref _runtimeInstanceId);
        var task = _runtimeTask;
        _runtimeTask = null;

        SetStoppingState(detail);

        if (task is not null)
        {
            try
            {
                await Task.Run(task.Stop);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                Core.LogError("Python client stop failed.", ex);
            }
        }

        try
        {
            if (_runtimeState != RuntimeState.Error)
            {
                SetStoppedState(detail);
            }
        }
        finally
        {
            _isStopping = false;
            RefreshPresentation();
        }
    }

    private async Task RunRuntimeAsync(FolderItemModel item, string scriptPath, CancellationToken cancellationToken, long runtimeInstanceId)
    {
        var completedNormally = false;
        try
        {
            var options = new PythonClientOptions
            {
                Name = BuildRuntimeClientName(item),
                ClientType = "UiPythonClient",
                ScriptPath = scriptPath,
                RegistryRootPath = BuildRegistryRootPath(item),
                AllowedCapabilities = new[] { "functions", "host_log" }
            };

            await using var client = new PythonClient(options);

            await client.StartAsync(cancellationToken);

            var runtimeTargetPath = BuildRuntimeTargetPath(item);
            PythonClientRuntimeRegistry.Register(runtimeTargetPath, item.Name, client);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (runtimeInstanceId != Interlocked.Read(ref _runtimeInstanceId) || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                SetRunningState($"Connected to {Path.GetFileName(scriptPath)}");
            });

            // Wait until cancellation is requested (stop).
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // expected on stop
            }

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                Core.LogError("Python client soft stop failed.", ex);
                await client.HardStopAsync();
            }

            completedNormally = true;
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        catch (Exception ex)
        {
            Core.LogError($"Python client failed for '{item.Name}'.", ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (runtimeInstanceId != Interlocked.Read(ref _runtimeInstanceId) || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                SetErrorState(ex.Message);
            });
        }
        finally
        {
            PythonClientRuntimeRegistry.Unregister(BuildRuntimeTargetPath(item));
            if (!cancellationToken.IsCancellationRequested
                && runtimeInstanceId == Interlocked.Read(ref _runtimeInstanceId)
                && _runtimeState != RuntimeState.Error)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (runtimeInstanceId != Interlocked.Read(ref _runtimeInstanceId) || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!completedNormally)
                    {
                        SetErrorState($"Python client aborted for {Path.GetFileName(scriptPath)}");
                        return;
                    }

                    SetErrorState($"Python client stopped: {Path.GetFileName(scriptPath)}");
                });
            }
        }
    }

    private void RefreshPresentation()
    {
        var item = _observedItem;
        var hasConfiguration = HasValidConfiguration(item, out var scriptPath);
        CanToggleRuntime = !_isStopping && hasConfiguration;
        RuntimeToggleText = _runtimeState == RuntimeState.Running ? "Stop" : "Start";

        if (_runtimeState == RuntimeState.Running)
        {
            RuntimeStatusText = "Running";
            RuntimeDetailText = string.IsNullOrWhiteSpace(scriptPath)
                ? "Python client running"
                : $"Running {Path.GetFileName(scriptPath)}";
            RuntimeStatusBackground = RunningBackgroundBrush;
            RuntimeStatusForeground = RunningForegroundBrush;
            return;
        }

        if (_runtimeState == RuntimeState.Stopping)
        {
            RuntimeStatusText = "Stopping";
            RuntimeDetailText = string.IsNullOrWhiteSpace(scriptPath)
                ? "Stopping Python client"
                : $"Stopping {Path.GetFileName(scriptPath)}";
            RuntimeStatusBackground = StoppedBackgroundBrush;
            RuntimeStatusForeground = StoppedForegroundBrush;
            return;
        }

        if (_runtimeState == RuntimeState.Error)
        {
            RuntimeStatusText = "Error";
            RuntimeStatusBackground = ErrorBackgroundBrush;
            RuntimeStatusForeground = ErrorForegroundBrush;
            return;
        }

        RuntimeStatusText = "Stopped";
        RuntimeStatusBackground = StoppedBackgroundBrush;
        RuntimeStatusForeground = StoppedForegroundBrush;
        RuntimeDetailText = hasConfiguration
            ? $"Ready: PythonClient via {Path.GetFileName(scriptPath)}"
            : "Configure a Python client script.";
    }

    private void SetRunningState(string detail)
    {
        _runtimeState = RuntimeState.Running;
        RuntimeStatusText = "Running";
        RuntimeDetailText = detail;
        RuntimeToggleText = "Stop";
        RuntimeStatusBackground = RunningBackgroundBrush;
        RuntimeStatusForeground = RunningForegroundBrush;
        CanToggleRuntime = true;
    }

    private void SetStoppedState(string detail)
    {
        _runtimeState = RuntimeState.Stopped;
        RuntimeStatusText = "Stopped";
        RuntimeDetailText = string.IsNullOrWhiteSpace(detail) ? "Stopped" : detail;
        RuntimeToggleText = "Start";
        RuntimeStatusBackground = StoppedBackgroundBrush;
        RuntimeStatusForeground = StoppedForegroundBrush;
        CanToggleRuntime = !_isStopping && HasValidConfiguration(_observedItem, out _);
    }

    private void SetStoppingState(string detail)
    {
        _runtimeState = RuntimeState.Stopping;
        RuntimeStatusText = "Stopping";
        RuntimeDetailText = string.IsNullOrWhiteSpace(detail) ? "Stopping Python client" : detail;
        RuntimeToggleText = "Stop";
        RuntimeStatusBackground = StoppedBackgroundBrush;
        RuntimeStatusForeground = StoppedForegroundBrush;
        CanToggleRuntime = false;
    }

    private void SetErrorState(string detail)
    {
        _runtimeState = RuntimeState.Error;
        RuntimeStatusText = "Error";
        RuntimeDetailText = string.IsNullOrWhiteSpace(detail) ? "Python client failed." : detail;
        RuntimeToggleText = "Start";
        RuntimeStatusBackground = ErrorBackgroundBrush;
        RuntimeStatusForeground = ErrorForegroundBrush;
        CanToggleRuntime = !_isStopping && HasValidConfiguration(_observedItem, out _);
    }

    private static bool HasValidConfiguration(FolderItemModel? item, out string scriptPath)
    {
        scriptPath = item?.ResolveConfiguredScriptPath()?.Trim() ?? string.Empty;
        return item is not null && !string.IsNullOrWhiteSpace(scriptPath);
    }

    private static string BuildRuntimeInstanceName(FolderItemModel item, long runtimeInstanceId)
    {
        var folderName = string.IsNullOrWhiteSpace(item.FolderName) ? "Page" : item.FolderName.Trim();
        var widgetName = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name.Trim();
        return $"PythonClient-{folderName}-{widgetName}-{runtimeInstanceId}";
    }

    private static string BuildRuntimeClientName(FolderItemModel item)
    {
        var folderName = string.IsNullOrWhiteSpace(item.FolderName) ? "Page" : item.FolderName.Trim();
        var widgetName = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name.Trim();
        return $"PythonClient-{folderName}-{widgetName}";
    }

    private static string BuildRuntimeTargetPath(FolderItemModel item)
        => string.IsNullOrWhiteSpace(item.Path)
            ? $"{item.FolderName}.{(string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name)}"
            : item.Path;

    private static string BuildRegistryRootPath(FolderItemModel item)
    {
        var folderName = string.IsNullOrWhiteSpace(item.FolderName) ? "Page" : item.FolderName.Trim();
        var widgetName = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name.Trim();
        return $"studio.{folderName}.{widgetName}";
    }
}

