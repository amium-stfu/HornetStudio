using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using Amium.Item;
using HornetStudio.Logging;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class UdlClientControl : EditorTemplateControl
{
    private static readonly string[] PublishedStatusNames = ["Endpoint", "Connection", "ItemCount", "MessageCounter", "AutoConnect"];
    public static readonly DirectProperty<UdlClientControl, string> SocketTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(SocketText), control => control.SocketText);

    public static readonly DirectProperty<UdlClientControl, string> ConnectionStateTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ConnectionStateText), control => control.ConnectionStateText);

    public static readonly DirectProperty<UdlClientControl, string> AutoConnectTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(AutoConnectText), control => control.AutoConnectText);

    public static readonly DirectProperty<UdlClientControl, string> ItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ItemCountText), control => control.ItemCountText);

    public static readonly DirectProperty<UdlClientControl, string> ModuleCountTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ModuleCountText), control => control.ModuleCountText);

    public static readonly DirectProperty<UdlClientControl, bool> HasNoModulesProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(HasNoModules), control => control.HasNoModules);

    public static readonly DirectProperty<UdlClientControl, bool> CanConnectProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(CanConnect), control => control.CanConnect);

    public static readonly DirectProperty<UdlClientControl, bool> CanDisconnectProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(CanDisconnect), control => control.CanDisconnect);

    public static readonly DirectProperty<UdlClientControl, IBrush> ConnectionStatusBackgroundProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, IBrush>(nameof(ConnectionStatusBackground), control => control.ConnectionStatusBackground);

    public static readonly DirectProperty<UdlClientControl, IBrush> ConnectionStatusForegroundProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, IBrush>(nameof(ConnectionStatusForeground), control => control.ConnectionStatusForeground);

    public static readonly DirectProperty<UdlClientControl, IBrush> ConnectionStatusHoverBackgroundProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, IBrush>(nameof(ConnectionStatusHoverBackground), control => control.ConnectionStatusHoverBackground);

    public static readonly DirectProperty<UdlClientControl, bool> CanToggleConnectionProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(CanToggleConnection), control => control.CanToggleConnection);

    public static readonly DirectProperty<UdlClientControl, string> ConnectionToggleTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ConnectionToggleText), control => control.ConnectionToggleText);

    private Popup? _attachPopup;
    private FolderItemModel? _observedItem;
    private UiFolderContext? _uiFolderContext;
    private IHostUdlClient? _client;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private DispatcherTimer? _attachedItemsRefreshTimer;
    private readonly Dictionary<string, string> _publishedStatusValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _publishedAttachOptionPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _clientItemsDirty = 1;
    private int _isConnecting;
    private int _lastPublishedClientItemCount = -1;
    private int _hasAttachedPaths;
    private ConnectionState _connectionState = ConnectionState.Disconnected;
    private bool _canConnect = true;
    private bool _canDisconnect;
    private long _messageCounter;
    private long _rxCounter;
    private long _txCounter;
    private long _lastLoggedRxCounter;
    private long _lastLoggedTxCounter;
    private long _monitorLoopCounter;
    private volatile bool _verboseDiagnosticsEnabled;
    private bool _loggedNoFramesWarning;
    private string _lastLoggedRuntimeRootsSignature = string.Empty;
    private string _lastSynchronizedAttachSignature = string.Empty;
    private string _lastModuleRowsSignature = string.Empty;
    private string _socketText = "192.168.178.151:9001";
    private string _connectionStateText = "Disconnected";
    private string _autoConnectText = "False";
    private string _itemCountText = "0";
    private string _moduleCountText = "0";
    private IBrush _connectionStatusBackground = Brushes.Black;
    private IBrush _connectionStatusForeground = Brushes.White;
    private IBrush _connectionStatusHoverBackground = Brushes.DimGray;
    private bool _canToggleConnection = true;
    private bool _hasNoModules = true;
    private string _connectionToggleText = "Connect";
    private string _publishedStatusBasePath = string.Empty;
    private string _publishedAttachOptionsBasePath = string.Empty;

    public UdlClientControl()
    {
        AttachRows = [];
        Modules = [];
        Modules.CollectionChanged += (_, _) => UpdateModuleCollectionState();
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private enum ConnectionState
    {
        Connected,
        Disconnected,
        Failed
    }

    public ObservableCollection<AttachItemEditorRow> AttachRows { get; }

    public ObservableCollection<UdlClientModuleRow> Modules { get; }

    public string SocketText
    {
        get => _socketText;
        private set => SetAndRaise(SocketTextProperty, ref _socketText, value);
    }

    public string ConnectionStateText
    {
        get => _connectionStateText;
        private set => SetAndRaise(ConnectionStateTextProperty, ref _connectionStateText, value);
    }

    public string AutoConnectText
    {
        get => _autoConnectText;
        private set => SetAndRaise(AutoConnectTextProperty, ref _autoConnectText, value);
    }

    public string ItemCountText
    {
        get => _itemCountText;
        private set => SetAndRaise(ItemCountTextProperty, ref _itemCountText, value);
    }

    public string ModuleCountText
    {
        get => _moduleCountText;
        private set => SetAndRaise(ModuleCountTextProperty, ref _moduleCountText, value);
    }

    public bool HasNoModules
    {
        get => _hasNoModules;
        private set => SetAndRaise(HasNoModulesProperty, ref _hasNoModules, value);
    }

    public bool CanConnect
    {
        get => _canConnect;
        private set => SetAndRaise(CanConnectProperty, ref _canConnect, value);
    }

    public bool CanDisconnect
    {
        get => _canDisconnect;
        private set => SetAndRaise(CanDisconnectProperty, ref _canDisconnect, value);
    }

    public IBrush ConnectionStatusBackground
    {
        get => _connectionStatusBackground;
        private set => SetAndRaise(ConnectionStatusBackgroundProperty, ref _connectionStatusBackground, value);
    }

    public IBrush ConnectionStatusForeground
    {
        get => _connectionStatusForeground;
        private set => SetAndRaise(ConnectionStatusForegroundProperty, ref _connectionStatusForeground, value);
    }

    public IBrush ConnectionStatusHoverBackground
    {
        get => _connectionStatusHoverBackground;
        private set => SetAndRaise(ConnectionStatusHoverBackgroundProperty, ref _connectionStatusHoverBackground, value);
    }

    public bool CanToggleConnection
    {
        get => _canToggleConnection;
        private set => SetAndRaise(CanToggleConnectionProperty, ref _canToggleConnection, value);
    }

    public string ConnectionToggleText
    {
        get => _connectionToggleText;
        private set => SetAndRaise(ConnectionToggleTextProperty, ref _connectionToggleText, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private static bool IsUdlClientItem(FolderItemModel? item) => item?.IsUdlClientControl == true;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attachPopup = this.FindControl<Popup>("AttachPopup");
        HostRegistries.Data.ItemChanged -= OnExposureTargetChanged;
        HostRegistries.Data.ItemChanged += OnExposureTargetChanged;
        HookObservedItem();
        RefreshPresentation();
        _ = EnsureAutoConnectAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HostRegistries.Data.ItemChanged -= OnExposureTargetChanged;
        TearDownClient();
        CancelAttachedItemsRefresh();
        ReleaseUiFolderContext();
        RemovePublishedExposureItems();
        RemovePublishedStatusItems();
        UnhookObservedItem();
        foreach (var row in AttachRows)
        {
            row.PropertyChanged -= OnAttachRowPropertyChanged;
        }

        AttachRows.Clear();
        Modules.Clear();
        _attachPopup = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RefreshPresentation();
        _ = EnsureAutoConnectAsync();
    }

    private void HookObservedItem()
    {
        var item = Item;
        if (!IsUdlClientItem(item))
        {
            if (_observedItem is not null)
            {
                TearDownClient();
            }

            RemovePublishedStatusItems();
            UnhookObservedItem();
            RebuildAttachRows();
            RebuildModuleRows();
            return;
        }

        if (ReferenceEquals(_observedItem, item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = item;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
            UpdateAttachedPathsFlag(_observedItem);
        }
        else
        {
            Volatile.Write(ref _hasAttachedPaths, 0);
        }

        RebuildAttachRows();
        RebuildModuleRows();
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is null)
        {
            return;
        }

        _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        _observedItem = null;
        Volatile.Write(ref _hasAttachedPaths, 0);
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.UdlClientHost)
            or nameof(FolderItemModel.UdlClientPort)
            or nameof(FolderItemModel.UdlClientAutoConnect)
            or nameof(FolderItemModel.UdlClientDebugLogging)
            or nameof(FolderItemModel.UdlClientDemoEnabled)
            or nameof(FolderItemModel.UdlDemoModuleDefinitions)
            or nameof(FolderItemModel.UdlModuleExposureDefinitions)
            or nameof(FolderItemModel.UdlAttachedItemPaths)
            or nameof(FolderItemModel.Name)
            or nameof(FolderItemModel.FolderName))
        {
            RefreshPresentation();
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            foreach (var row in Modules)
            {
                row.RefreshTheme();
            }
        }

        if (e.PropertyName == nameof(FolderItemModel.UdlAttachedItemPaths))
        {
            if (sender is FolderItemModel changedItem)
            {
                UpdateAttachedPathsFlag(changedItem);
            }

            RebuildAttachRows();
            SynchronizeAttachedItems();
        }

        if (e.PropertyName is nameof(FolderItemModel.UdlModuleExposureDefinitions)
            or nameof(FolderItemModel.Name)
            or nameof(FolderItemModel.FolderName))
        {
            PublishExposureItems();
            ForceAttachedItemsResync();
            RebuildModuleRows();
        }

        if (e.PropertyName == nameof(FolderItemModel.UdlClientAutoConnect))
        {
            _ = EnsureAutoConnectAsync();
        }

        if (e.PropertyName is nameof(FolderItemModel.UdlClientDemoEnabled)
            or nameof(FolderItemModel.UdlDemoModuleDefinitions))
        {
            RebuildModuleRows();
            if (_client is not null)
            {
                DisconnectInternal();
                ConnectInternal();
            }
        }
    }

    private async System.Threading.Tasks.Task EnsureAutoConnectAsync()
    {
        if (!IsUdlClientItem(Item) || Item?.UdlClientAutoConnect != true || _client is not null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(ConnectInternal);
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private void OnMenuClicked(object? sender, RoutedEventArgs e)
    {
        RebuildAttachRows();
        LogAttachListSnapshot();
        if (_attachPopup is not null)
        {
            _attachPopup.IsOpen = !_attachPopup.IsOpen;
        }

        e.Handled = true;
    }

    private void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        ConnectInternal();
        e.Handled = true;
    }

    private void OnDisconnectClicked(object? sender, RoutedEventArgs e)
    {
        DisconnectInternal();
        e.Handled = true;
    }

    private void OnToggleConnectionClicked(object? sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            ConnectInternal();
        }
        else
        {
            DisconnectInternal();
        }

        e.Handled = true;
    }

    private void ConnectInternal()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ConnectInternal);
            return;
        }

        var item = Item;
        if (item is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isConnecting, 1) == 1)
        {
            return;
        }

        try
        {
            if (_client is not null)
            {
                return;
            }

            WriteDiagnosticLog($"Connect requested endpoint={item.UdlClientHost}:{item.UdlClientPort}");
            TearDownClient();
            RemovePublishedRuntimeItems(item);
            Interlocked.Exchange(ref _messageCounter, 0);
            Interlocked.Exchange(ref _rxCounter, 0);
            Interlocked.Exchange(ref _txCounter, 0);
            Interlocked.Exchange(ref _lastLoggedRxCounter, 0);
            Interlocked.Exchange(ref _lastLoggedTxCounter, 0);
            Interlocked.Exchange(ref _monitorLoopCounter, 0);
            Interlocked.Exchange(ref _clientItemsDirty, 1);
            _lastPublishedClientItemCount = -1;
            _loggedNoFramesWarning = false;
            _lastLoggedRuntimeRootsSignature = string.Empty;
            _lastSynchronizedAttachSignature = string.Empty;
            _publishedStatusValues.Clear();
            RemovePublishedAttachOptionItems();
            RemovePublishedExposureItems();
            var client = CreateClient(item);
            client.FrameReceived += OnClientFrameReceived;
            client.Diagnostic += OnClientDiagnostic;
            client.ConnectAsync().GetAwaiter().GetResult();
            _client = client;
            _connectionState = ConnectionState.Connected;
            StartMonitor();
            PublishClientItems();
            SynchronizeAttachedItems();
            WriteDiagnosticLog($"Connect completed localPort={client.LocalPort}");
            WriteDiagnosticLog($"Initial runtime roots={GetRootItemCount()} items={EnumerateClientItems().Count}");
        }
        catch (Exception ex)
        {
            _connectionState = ConnectionState.Failed;
            HostLogger.Log.Error(ex, "UdlClient connect failed for {ClientName}", item.Name);
            WriteDiagnosticError("Connect failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isConnecting, 0);
        }

        RefreshPresentation();
    }

    private void DisconnectInternal()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(DisconnectInternal);
            return;
        }

        WriteDiagnosticLog("Disconnect requested");
        BeginDisconnectClient();
        _connectionState = ConnectionState.Disconnected;
        Interlocked.Exchange(ref _clientItemsDirty, 0);
        _lastPublishedClientItemCount = -1;
        _loggedNoFramesWarning = false;
        _lastLoggedRuntimeRootsSignature = string.Empty;
        _lastSynchronizedAttachSignature = string.Empty;
        CancelAttachedItemsRefresh();
        _publishedStatusValues.Clear();
        RemovePublishedAttachOptionItems();
        RemovePublishedExposureItems();
        RemovePublishedRuntimeItems(Item);
        RefreshPresentation();
        WriteDiagnosticLog("Disconnect completed");
    }

    private void BeginDisconnectClient()
    {
        StopMonitor();

        if (_client is null)
        {
            ReleaseUiFolderContext();
            return;
        }

        var client = _client;
        _client = null;
        client.FrameReceived -= OnClientFrameReceived;
        client.Diagnostic -= OnClientDiagnostic;
        Interlocked.Exchange(ref _clientItemsDirty, 0);
        _lastPublishedClientItemCount = -1;
        _loggedNoFramesWarning = false;
        _lastLoggedRuntimeRootsSignature = string.Empty;
        _lastSynchronizedAttachSignature = string.Empty;
        _lastModuleRowsSignature = string.Empty;
        CancelAttachedItemsRefresh();
        RemovePublishedAttachOptionItems();
        RemovePublishedExposureItems();
        ReleaseUiFolderContext();
        RemovePublishedRuntimeItems(_observedItem ?? Item);

        _ = Task.Run(() =>
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _connectionState = ConnectionState.Failed;
                    RefreshPresentation();
                    WriteDiagnosticError("Disconnect failed", ex);
                });
            }
        });
    }

    private void TearDownClient()
    {
        StopMonitor();

        if (_client is null)
        {
            return;
        }

        _client.FrameReceived -= OnClientFrameReceived;
        _client.Diagnostic -= OnClientDiagnostic;
        Interlocked.Exchange(ref _clientItemsDirty, 0);
        _lastPublishedClientItemCount = -1;
        _loggedNoFramesWarning = false;
        _lastLoggedRuntimeRootsSignature = string.Empty;
        _lastSynchronizedAttachSignature = string.Empty;
        CancelAttachedItemsRefresh();
        RemovePublishedAttachOptionItems();
        RemovePublishedExposureItems();
        ReleaseUiFolderContext();
        RemovePublishedRuntimeItems(_observedItem ?? Item);
        _client.Dispose();
        _client = null;
    }

    private void OnClientFrameReceived(uint id, byte dlc, byte[] data)
    {
        Interlocked.Increment(ref _messageCounter);
        Interlocked.Increment(ref _rxCounter);
        _connectionState = ConnectionState.Connected;
        Interlocked.Exchange(ref _clientItemsDirty, 1);
    }

    private void OnClientDiagnostic(string message)
    {
        UpdateCountersFromDiagnostic(message);

        if (IsAlwaysLoggedDiagnosticMessage(message))
        {
            WriteDiagnosticLog(message);
            return;
        }

        if (ShouldLogDiagnosticMessage(message) && ShouldWriteVerboseDiagnostics())
        {
            WriteVerboseDiagnosticLog(message);
        }
    }

    private void StartMonitor()
    {
        if (_monitorTask is not null)
        {
            return;
        }

        _monitorCts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), _monitorCts.Token);
    }

    private void StopMonitor()
    {
        _monitorCts?.Cancel();
        WaitForMonitor(_monitorTask);
        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var loop = Interlocked.Increment(ref _monitorLoopCounter);
                var publishItems = Interlocked.Exchange(ref _clientItemsDirty, 0) == 1;

                if (publishItems)
                {
                    PublishClientItems();
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshPresentation();
                });

                if (loop == 1 || loop % 4 == 0)
                {
                    if (GetRootItemCount() == 0 && Interlocked.Read(ref _messageCounter) > 0)
                    {
                        WriteVerboseDiagnosticLog($"Monitor snapshot roots=0 items={EnumerateClientItems().Count} messages={Interlocked.Read(ref _messageCounter)} localPort={_client?.LocalPort ?? 0}");
                    }
                }

                if (!_loggedNoFramesWarning && loop >= 8 && Interlocked.Read(ref _messageCounter) == 0 && GetRootItemCount() == 0)
                {
                    _loggedNoFramesWarning = true;
                    WriteDiagnosticLog($"No frames received after connect client={_client?.Name ?? string.Empty} localPort={_client?.LocalPort ?? 0} roots=0 messages=0");
                }

                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateCountersFromDiagnostic(string message)
    {
        if (message.Contains("tx queued", StringComparison.OrdinalIgnoreCase)
            || message.Contains("tx send package", StringComparison.OrdinalIgnoreCase)
            || message.Contains("send write pdo", StringComparison.OrdinalIgnoreCase)
            || message.Contains("write request", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _txCounter);
        }

        if (message.Contains("OnCanMessageReceived", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rx packet", StringComparison.OrdinalIgnoreCase))
        {
            _connectionState = ConnectionState.Connected;
        }

        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("error=", StringComparison.OrdinalIgnoreCase))
        {
            _connectionState = ConnectionState.Failed;
        }

        if (message.Contains("close", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dispose", StringComparison.OrdinalIgnoreCase))
        {
            _connectionState = ConnectionState.Disconnected;
        }
    }

    private static void WaitForMonitor(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
        }
    }

    private void PublishClientItems()
    {
        if (_client is null)
        {
            return;
        }

        var runtimeItems = EnumerateClientItems();
        if (runtimeItems.Count != _lastPublishedClientItemCount)
        {
            _lastPublishedClientItemCount = runtimeItems.Count;
            var samplePaths = string.Join(", ", runtimeItems
                .Take(3)
                .Select(static item => item.Path ?? string.Empty));
            WriteDiagnosticLog($"Runtime items updated client={_client.Name} count={runtimeItems.Count} samplePaths=[{samplePaths}]");
            ScheduleAttachedItemsRefresh();
        }

        LogRuntimeRootItems(runtimeItems);

        PublishAttachOptionItems(runtimeItems);
        PublishExposureItems();
        RebuildModuleRows();
    }

    private void LogRuntimeRootItems(IReadOnlyList<Item> runtimeItems)
    {
        if (_client is null)
        {
            return;
        }

        var rootItems = runtimeItems
            .Select(static item => new
            {
                Item = item,
                RelativePath = GetRelativeRuntimePath(item)
            })
            .Where(static entry => IsRootAttachPath(entry.RelativePath))
            .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var signature = string.Join("|", rootItems.Select(static entry => $"{entry.Item.Name}:{entry.Item.Path}:{entry.RelativePath}"));
        if (string.Equals(_lastLoggedRuntimeRootsSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedRuntimeRootsSignature = signature;
        WriteDiagnosticLog($"Runtime root modules client={_client.Name} count={rootItems.Length}");

        foreach (var rootItem in rootItems.Select(static (entry, index) => new { Index = index, entry.Item, entry.RelativePath }))
        {
            WriteDiagnosticLog($"Runtime root[{rootItem.Index}] name={rootItem.Item.Name ?? string.Empty} fullPath={rootItem.Item.Path ?? string.Empty} relativePath={rootItem.RelativePath}");
        }
    }

    private void ScheduleAttachedItemsRefresh()
    {
        if (Volatile.Read(ref _hasAttachedPaths) != 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _attachedItemsRefreshTimer ??= new DispatcherTimer();
            _attachedItemsRefreshTimer.Stop();
            _attachedItemsRefreshTimer.Interval = TimeSpan.FromSeconds(2);
            _attachedItemsRefreshTimer.Tick -= OnAttachedItemsRefreshTimerTick;
            _attachedItemsRefreshTimer.Tick += OnAttachedItemsRefreshTimerTick;
            _attachedItemsRefreshTimer.Start();
        });
    }

    private void CancelAttachedItemsRefresh()
    {
        Dispatcher.UIThread.Post(() => _attachedItemsRefreshTimer?.Stop());
    }

    private void OnAttachedItemsRefreshTimerTick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer)
        {
            timer.Stop();
            timer.Tick -= OnAttachedItemsRefreshTimerTick;
        }

        var attachmentsChanged = SynchronizeAttachedItems();
        RebuildAttachRows();
        if (attachmentsChanged)
        {
            Host?.RefreshFolderBindings(Item?.FolderName ?? string.Empty);
        }
        RefreshPresentation();
        WriteVerboseDiagnosticLog($"AttachToUi refreshed after item-settle delay itemCount={_lastPublishedClientItemCount} changed={attachmentsChanged}");
    }

    private void RebuildAttachRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildAttachRows);
            return;
        }

        var item = Item;
        foreach (var row in AttachRows)
        {
            row.PropertyChanged -= OnAttachRowPropertyChanged;
        }

        AttachRows.Clear();
        if (item is null)
        {
            return;
        }

        var selected = ParseAttachedPaths(item.UdlAttachedItemPaths);
        foreach (var option in GetAttachOptions(item))
        {
            var row = new AttachItemEditorRow
            {
                RelativePath = option,
                IsAttached = selected.Contains(option)
            };

            row.PropertyChanged += OnAttachRowPropertyChanged;
            AttachRows.Add(row);
        }
    }

    private void OnAttachRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnAttachRowPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (e.PropertyName != nameof(AttachItemEditorRow.IsAttached) || Item is null)
        {
            return;
        }

        Item.UdlAttachedItemPaths = string.Join(Environment.NewLine, AttachRows
            .Where(static row => row.IsAttached)
            .Select(static row => row.RelativePath));

        SynchronizeAttachedItems();
        RefreshPresentation();
    }

    private bool SynchronizeAttachedItems()
    {
        var item = Item;
        if (item is null || _client is null)
        {
            ReleaseUiFolderContext();
            _lastSynchronizedAttachSignature = string.Empty;
            return false;
        }

        var attachedPaths = ParseAttachedPaths(item.UdlAttachedItemPaths);
        if (attachedPaths.Count == 0)
        {
            ReleaseUiFolderContext();
            _lastSynchronizedAttachSignature = string.Empty;
            return false;
        }

        var attachments = new List<(string RelativePath, string Alias, Item RuntimeItem)>();
        foreach (var relativePath in attachedPaths)
        {
            if (!TryResolveRuntimeItem(relativePath, out var runtimeItem) || runtimeItem?.Path is null)
            {
                continue;
            }

            attachments.Add((relativePath, TargetPathHelper.NormalizeConfiguredTargetPath(relativePath), runtimeItem));
        }

        var signature = string.Join("|", attachments
            .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => $"{entry.RelativePath}>{entry.Alias}>{entry.RuntimeItem.Path}"));
        if (_uiFolderContext is not null
            && string.Equals(_lastSynchronizedAttachSignature, signature, StringComparison.Ordinal))
        {
            return false;
        }

        ReleaseUiFolderContext();

        if (attachments.Count == 0)
        {
            _lastSynchronizedAttachSignature = string.Empty;
            return false;
        }

        var folderContext = new UiFolderContext($"{item.FolderName}.{NormalizeClientName(item)}", "Project");
        _uiFolderContext = folderContext;
        _lastSynchronizedAttachSignature = signature;

        foreach (var attachment in attachments)
        {
            var attached = folderContext.Attach(attachment.RuntimeItem, attachment.Alias);
            WriteVerboseDiagnosticLog($"Attach snapshot folder={folderContext.FolderPath} client={NormalizeClientName(item)} runtimePath={attachment.RuntimeItem.Path} alias={attachment.Alias} attachedPath={attached.Path}");
            HostRegistries.Data.UpsertSnapshot(attached.Path!, attached.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        }

        return true;
    }

    private void ReleaseUiFolderContext()
    {
        _uiFolderContext?.Dispose();
        _uiFolderContext = null;
    }

    private bool TryResolveRuntimeItem(string relativePath, out Item? resolved)
    {
        resolved = EnumerateClientItems()
            .FirstOrDefault(candidate => string.Equals(GetRelativeRuntimePath(candidate), relativePath, StringComparison.OrdinalIgnoreCase));

        return resolved is not null;
    }

    private IReadOnlyList<Item> EnumerateClientItems()
    {
        if (_client is null)
        {
            return [];
        }

        var items = new List<Item>();
        foreach (var root in _client.Items.GetDictionary().Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AppendItem(root, items);
        }

        return items;
    }

    private int GetRootItemCount()
    {
        return _client?.Items.GetDictionary().Count ?? 0;
    }

    private static bool HasAttachedPaths(FolderItemModel item)
    {
        return ParseAttachedPaths(item.UdlAttachedItemPaths).Count > 0;
    }

    private void UpdateAttachedPathsFlag(FolderItemModel item)
    {
        Volatile.Write(ref _hasAttachedPaths, HasAttachedPaths(item) ? 1 : 0);
    }

    private static void AppendItem(Item item, ICollection<Item> items)
    {
        items.Add(item);
        foreach (var child in item.GetDictionary().Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AppendItem(child, items);
        }
    }

    private IEnumerable<string> GetAttachOptions(FolderItemModel item)
    {
        var prefix = $"Runtime.UdlClient.{NormalizeClientName(item)}";
        var comparablePrefix = TargetPathHelper.NormalizeComparablePath(prefix);
        var runtimeOptions = HostRegistries.Data.GetAllKeys()
            .Select(key => TryGetAttachRootOption(key, prefix, comparablePrefix))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!);

        return runtimeOptions
            .Concat(EnumerateClientItems().Select(GetRelativeRuntimePath))
            .Where(static path => IsRootAttachPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsRootAttachPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return TargetPathHelper.SplitPathSegments(path).Count == 1;
    }

    private static string? TryGetAttachRootOption(string registryKey, string prefix, string comparablePrefix)
    {
        if (string.IsNullOrWhiteSpace(registryKey))
        {
            return null;
        }

        var suffix = TryGetPathSuffix(registryKey, prefix);
        if (string.IsNullOrWhiteSpace(suffix))
        {
            var comparableKey = TargetPathHelper.NormalizeComparablePath(registryKey);
            suffix = TryGetPathSuffix(comparableKey, comparablePrefix);
        }

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var segments = TargetPathHelper.SplitPathSegments(suffix);
        return segments.Count == 0 ? null : segments[0];
    }

    private static string? TryGetPathSuffix(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = path[prefix.Length..].TrimStart('/', '.', '\\');
        return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
    }

    private void LogAttachListSnapshot()
    {
        if (!ShouldWriteVerboseDiagnostics())
        {
            return;
        }

        var item = Item;
        if (item is null)
        {
            WriteVerboseDiagnosticLog("Attach list open skipped because DataContext item is null");
            return;
        }

        var prefix = $"Runtime.UdlClient.{NormalizeClientName(item)}.";
        var registryOptions = HostRegistries.Data.GetAllKeys()
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var clientItems = EnumerateClientItems()
            .Select(static candidate => new
            {
                FullPath = candidate.Path ?? string.Empty,
                RelativePath = GetRelativeRuntimePath(candidate)
            })
            .OrderBy(static candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var attachOptions = GetAttachOptions(item).ToArray();
        var attachRows = AttachRows
            .Select(static (row, index) => new
            {
                Index = index,
                row.RelativePath,
                row.IsAttached
            })
            .ToArray();

        WriteVerboseDiagnosticLog($"Attach list open folder={item.FolderName} client={NormalizeClientName(item)} registryCount={registryOptions.Length} clientItemCount={clientItems.Length} optionCount={attachOptions.Length} rowCount={attachRows.Length}");

        foreach (var option in registryOptions.Select(static (path, index) => new { Index = index, Path = path }))
        {
            WriteVerboseDiagnosticLog($"Attach registry[{option.Index}]={option.Path}");
        }

        foreach (var clientItem in clientItems.Select(static (candidate, index) => new { Index = index, candidate.FullPath, candidate.RelativePath }))
        {
            WriteVerboseDiagnosticLog($"Attach runtime[{clientItem.Index}] full={clientItem.FullPath} relative={clientItem.RelativePath}");
        }

        foreach (var option in attachOptions.Select(static (path, index) => new { Index = index, Path = path }))
        {
            WriteVerboseDiagnosticLog($"Attach option[{option.Index}]={option.Path}");
        }

        foreach (var row in attachRows)
        {
            WriteVerboseDiagnosticLog($"Attach row[{row.Index}] path={row.RelativePath} attached={row.IsAttached}");
        }
    }

    private void RefreshPresentation()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshPresentation);
            return;
        }

        var item = Item;
        if (!IsUdlClientItem(item))
        {
            SocketText = string.Empty;
            AutoConnectText = "False";
            ConnectionStateText = string.Empty;
            ItemCountText = "0";
            CanConnect = false;
            CanDisconnect = false;
            CanToggleConnection = false;
            ConnectionToggleText = "Connect";
            ConnectionStatusBackground = Brushes.Black;
            ConnectionStatusForeground = Brushes.White;
            ConnectionStatusHoverBackground = CreateHoverBrush(ConnectionStatusBackground);
            _verboseDiagnosticsEnabled = false;
            RemovePublishedStatusItems();
            return;
        }

        SocketText = item is null
            ? string.Empty
            : item.UdlClientDemoEnabled
                ? $"Demo | {item.UdlClientHost}:{item.UdlClientPort}"
                : $"{item.UdlClientHost}:{item.UdlClientPort}";
        AutoConnectText = item?.UdlClientAutoConnect == true ? "True" : "False";
        ConnectionStateText = _connectionState.ToString();
        ItemCountText = GetRootItemCount().ToString();
        ModuleCountText = Modules.Count.ToString(CultureInfo.InvariantCulture);
        CanConnect = _client is null;
        CanDisconnect = _client is not null;
        CanToggleConnection = CanConnect || CanDisconnect;
        ConnectionToggleText = _client is null ? "Connect" : "Disconnect";

        switch (_connectionState)
        {
            case ConnectionState.Connected:
                ConnectionStatusBackground = Brushes.ForestGreen;
                ConnectionStatusForeground = Brushes.White;
                break;
            case ConnectionState.Failed:
                ConnectionStatusBackground = Brushes.Tomato;
                ConnectionStatusForeground = Brushes.White;
                break;
            default:
                ConnectionStatusBackground = Brushes.Black;
                ConnectionStatusForeground = Brushes.White;
                break;
        }

        ConnectionStatusHoverBackground = CreateHoverBrush(ConnectionStatusBackground);

        _verboseDiagnosticsEnabled = item?.UdlClientDebugLogging == true;

        if (item is not null)
        {
            item.Footer = $"Socket: {SocketText} | AutoConnect: {AutoConnectText} | Runtime Modules: {ItemCountText} | Msg {Interlocked.Read(ref _messageCounter)}";
            PublishStatusItems(item);
        }
    }

    private void UpdateModuleCollectionState()
    {
        HasNoModules = Modules.Count == 0;
        ModuleCountText = Modules.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildModuleRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildModuleRows);
            return;
        }

        var item = _observedItem;
        if (!IsUdlClientItem(item) || item is null)
        {
            Modules.Clear();
            _lastModuleRowsSignature = string.Empty;
            UpdateModuleCollectionState();
            return;
        }

        var definitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(item.UdlModuleExposureDefinitions);
        var runtimeChannels = GetRuntimeChannelDescriptors();
        var signature = BuildModuleRowsSignature(definitions, runtimeChannels);
        if (string.Equals(_lastModuleRowsSignature, signature, StringComparison.Ordinal))
        {
            UpdateModuleCollectionState();
            return;
        }

        _lastModuleRowsSignature = signature;
        Modules.Clear();

        var moduleNames = definitions
            .Select(static definition => definition.ModuleName)
            .Concat(runtimeChannels.Select(static channel => channel.ModuleName))
            .Where(static moduleName => !string.IsNullOrWhiteSpace(moduleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleName => moduleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var moduleName in moduleNames)
        {
            var moduleRuntimeChannels = runtimeChannels
                .Where(channel => string.Equals(channel.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var moduleDefinitions = definitions
                .Where(definition => string.Equals(definition.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Modules.Add(new UdlClientModuleRow(item, moduleName, moduleRuntimeChannels, moduleDefinitions));
        }

        UpdateModuleCollectionState();
    }

    private static string BuildModuleRowsSignature(
        IReadOnlyList<UdlModuleExposureDefinition> definitions,
        IReadOnlyList<UdlRuntimeModuleChannelDescriptor> runtimeChannels)
    {
        var definitionSignature = string.Join(
            "|",
            definitions
                .OrderBy(static definition => definition.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static definition => definition.ChannelName, StringComparer.OrdinalIgnoreCase)
                .Select(static definition => string.Join(
                    "~",
                    definition.ModuleName,
                    definition.ChannelName,
                    definition.Format,
                    definition.Unit,
                    definition.ExposeBits ? "1" : "0",
                    definition.BitLabels)));

        var runtimeSignature = string.Join(
            "|",
            runtimeChannels
                .OrderBy(static channel => channel.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static channel => channel.ChannelName, StringComparer.OrdinalIgnoreCase)
                .Select(static channel => string.Join(
                    "~",
                    channel.ModuleName,
                    channel.ChannelName,
                    channel.Format)));

        return $"defs:{definitionSignature}||runtime:{runtimeSignature}";
    }

    private void PublishAttachOptionItems(IReadOnlyList<Item> runtimeItems)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(() => PublishAttachOptionItems(runtimeItems)).GetAwaiter().GetResult();
            return;
        }

        var item = Item;
        if (item is null)
        {
            RemovePublishedAttachOptionItems();
            return;
        }

        var attachOptionsBasePath = GetAttachOptionsBasePath(item);
        if (!string.Equals(_publishedAttachOptionsBasePath, attachOptionsBasePath, StringComparison.OrdinalIgnoreCase))
        {
            RemovePublishedAttachOptionItems();
            _publishedAttachOptionsBasePath = attachOptionsBasePath;
        }

        var rootPaths = runtimeItems
            .Select(GetRelativeRuntimePath)
            .Where(IsRootAttachPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desiredSnapshots = rootPaths
            .Select(rootPath =>
            {
                var snapshot = new Item(rootPath, path: attachOptionsBasePath);
                snapshot.Params["Kind"].Value = "Status";
                snapshot.Params["Text"].Value = "AttachOption";
                snapshot.Params["Title"].Value = rootPath;
                return snapshot;
            })
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.Path))
            .ToArray();

        var desiredPaths = new HashSet<string>(desiredSnapshots.Select(static snapshot => snapshot.Path!), StringComparer.OrdinalIgnoreCase);

        foreach (var stalePath in _publishedAttachOptionPaths.Except(desiredPaths, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            HostRegistries.Data.Remove(stalePath);
            _publishedAttachOptionPaths.Remove(stalePath);
        }

        foreach (var snapshot in desiredSnapshots)
        {
            HostRegistries.Data.UpsertSnapshot(snapshot.Path!, snapshot, DataRegistryItemMetadata.WidgetInternal(), pruneMissingMembers: true);
            _publishedAttachOptionPaths.Add(snapshot.Path!);
        }
    }

    private void EnsureDiagnosticLog(FolderItemModel item)
    {
        _ = item;
    }

    private static IBrush CreateHoverBrush(IBrush baseBrush)
    {
        if (baseBrush is SolidColorBrush solid)
        {
            var c = solid.Color;
            static byte L(byte v) => (byte)System.Math.Min(255, v + (255 - v) * 0.25);
            var lighter = Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
            return new SolidColorBrush(lighter);
        }

        return baseBrush;
    }

    private bool ShouldWriteVerboseDiagnostics()
    {
        return _verboseDiagnosticsEnabled;
    }

    private void WriteVerboseDiagnosticLog(string message)
    {
        if (!ShouldWriteVerboseDiagnostics())
        {
            return;
        }
        // Verbose Diagnostik: als Debug loggen und nicht über den UI-Thread marshallen,
        // damit hohes Logaufkommen die UI nicht blockiert.
        HostLogger.Log.Debug("[UdlClientControl] {Message}", message);
    }

    private void WriteDiagnosticLog(string message)
    {
        // Wichtige Statusmeldungen (Connect/Disconnect/High-Level) als Information loggen.
        HostLogger.Log.Information("[UdlClientControl] {Message}", message);
    }

    private void WriteDiagnosticError(string message, Exception exception)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => WriteDiagnosticError(message, exception));
            return;
        }

        HostLogger.Log.Error(exception, "[UdlClientControl] {Message}", message);
    }

    private static bool ShouldLogDiagnosticMessage(string message)
    {
        // Nur Verbindungs-/Initialisierungs-Lifecycle loggen, alles andere ignorieren,
        // damit das Log übersichtlich bleibt.
        if (message.Contains("ctor start", StringComparison.OrdinalIgnoreCase)
            || message.Contains("remote resolved endpoint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("udp socket", StringComparison.OrdinalIgnoreCase)
            || message.Contains("open requested", StringComparison.OrdinalIgnoreCase)
            || message.Contains("open completed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rx thread started", StringComparison.OrdinalIgnoreCase)
            || message.Contains("tx thread started", StringComparison.OrdinalIgnoreCase)
            || message.Contains("close requested", StringComparison.OrdinalIgnoreCase)
            || message.Contains("close completed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dispose start", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dispose completed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsAlwaysLoggedDiagnosticMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("create module", StringComparison.OrdinalIgnoreCase);
    }

    private void PublishStatusItems(FolderItemModel item)
    {
        var statusBasePath = GetStatusBasePath(item);
        if (!string.Equals(_publishedStatusBasePath, statusBasePath, StringComparison.OrdinalIgnoreCase))
        {
            RemovePublishedStatusItems();
            _publishedStatusBasePath = statusBasePath;
        }

        PublishStatusValue(statusBasePath, "Endpoint", SocketText, "UdlClient endpoint");
        PublishStatusValue(statusBasePath, "Connection", ConnectionStateText, "Connection state");
        PublishStatusValue(statusBasePath, "ItemCount", GetRootItemCount(), "Discovered items");
        PublishStatusValue(statusBasePath, "MessageCounter", Interlocked.Read(ref _messageCounter), "Received messages");
        PublishStatusValue(statusBasePath, "AutoConnect", item.UdlClientAutoConnect, "AutoConnect");
    }

    private void RemovePublishedStatusItems()
    {
        foreach (var path in _publishedStatusValues.Keys.ToArray())
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedStatusValues.Clear();
        _publishedStatusBasePath = string.Empty;
    }

    private void PublishStatusValue(string statusBasePath, string name, object? value, string title)
    {
        var cacheKey = $"{statusBasePath}.{name}";
        var serializedValue = value?.ToString() ?? "<null>";
        if (_publishedStatusValues.TryGetValue(cacheKey, out var previousValue)
            && string.Equals(previousValue, serializedValue, StringComparison.Ordinal))
        {
            return;
        }

        _publishedStatusValues[cacheKey] = serializedValue;

        var snapshot = new Item(name, value, statusBasePath);
        snapshot.Params["Kind"].Value = "Status";
        snapshot.Params["Text"].Value = title;
        snapshot.Params["Title"].Value = title;
        WriteVerboseDiagnosticLog($"Status snapshot base={statusBasePath} name={name} value={serializedValue}");
        HostRegistries.Data.UpsertSnapshot(snapshot.Path!, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
    }

    private void RemovePublishedAttachOptionItems()
    {
        foreach (var path in _publishedAttachOptionPaths.ToArray())
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedAttachOptionPaths.Clear();
        _publishedAttachOptionsBasePath = string.Empty;
    }

    private async void OnEditModuleClicked(object? sender, RoutedEventArgs e)
    {
        if (Item is null
            || sender is not Button { CommandParameter: UdlClientModuleRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await UdlModuleExposureDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: Item.UdlModuleExposureDefinitions,
            runtimeChannels: GetRuntimeChannelDescriptors(),
            moduleName: row.ModuleName);
        if (result is null)
        {
            return;
        }

        Item.UdlModuleExposureDefinitions = result;
        PublishExposureItems();
        RebuildModuleRows();
        e.Handled = true;
    }

    private async void OnDeleteModuleClicked(object? sender, RoutedEventArgs e)
    {
        if (Item is null
            || sender is not Button { CommandParameter: UdlClientModuleRow row }
            || !row.CanDelete
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(
            owner,
            $"Delete module '{row.ModuleName}'?",
            "All persisted helper item definitions for this module will be removed.",
            confirmText: "Delete",
            cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var definitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(Item.UdlModuleExposureDefinitions)
            .Where(definition => !string.Equals(definition.ModuleName, row.ModuleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Item.UdlModuleExposureDefinitions = UdlModuleExposureDefinitionCodec.SerializeDefinitions(definitions);
        PublishExposureItems();
        RebuildModuleRows();
        e.Handled = true;
    }

    private void PublishExposureItems()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(PublishExposureItems);
            return;
        }

        var item = Item;
        if (item is null)
        {
            RemovePublishedExposureItems();
            return;
        }

        RemoveLegacyExposureItems(item);

        var definitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(item.UdlModuleExposureDefinitions);
        var desiredChannels = new Dictionary<string, (UdlModuleExposureDefinition Definition, Item RuntimeChannel, int BitCount)>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            if (!definition.ExposeBits || !TryResolveRuntimeChannel(definition, out var runtimeChannel) || runtimeChannel?.Path is null)
            {
                continue;
            }

            var bitCount = ResolveBitCount(definition, runtimeChannel);
            if (bitCount <= 0)
            {
                continue;
            }

            desiredChannels[runtimeChannel.Path] = (definition, runtimeChannel, bitCount);
        }

        var structureChanged = false;
        foreach (var runtimeChannel in EnumerateClientItems().Where(IsRuntimeChannelItem))
        {
            if (string.IsNullOrWhiteSpace(runtimeChannel.Path))
            {
                continue;
            }

            if (desiredChannels.TryGetValue(runtimeChannel.Path, out var exposure))
            {
                structureChanged |= UpsertRuntimeExposureBits(
                    runtimeChannel: runtimeChannel,
                    definition: exposure.Definition,
                    bitCount: exposure.BitCount);
            }
            else
            {
                structureChanged |= RemoveRuntimeExposureBits(runtimeChannel);
            }
        }

        if (structureChanged)
        {
            ForceAttachedItemsResync();
        }
    }

    private void RemovePublishedExposureItems()
    {
        var structureChanged = false;
        foreach (var runtimeChannel in EnumerateClientItems().Where(IsRuntimeChannelItem))
        {
            structureChanged |= RemoveRuntimeExposureBits(runtimeChannel);
        }

        if (Item is not null)
        {
            RemoveLegacyExposureItems(Item);
        }

        if (structureChanged)
        {
            ForceAttachedItemsResync();
        }
    }

    private IReadOnlyList<UdlRuntimeModuleChannelDescriptor> GetRuntimeChannelDescriptors()
    {
        return EnumerateClientItems()
            .Select(static item => new
            {
                Item = item,
                RelativePath = GetRelativeRuntimePath(item),
                Format = item.Params.Has("Format") ? item.Params["Format"].Value?.ToString() ?? string.Empty : string.Empty,
                Unit = item.Params.Has("Unit") ? item.Params["Unit"].Value?.ToString() ?? string.Empty : string.Empty
            })
            .Where(static entry => TargetPathHelper.SplitPathSegments(entry.RelativePath).Count == 2)
            .Select(static entry =>
            {
                var segments = TargetPathHelper.SplitPathSegments(entry.RelativePath);
                return new UdlRuntimeModuleChannelDescriptor
                {
                    ModuleName = segments[0],
                    ChannelName = segments[1],
                    Format = entry.Format,
                    Unit = entry.Unit
                };
            })
            .GroupBy(static entry => UdlModuleExposureEditorRow.BuildKey(entry.ModuleName, entry.ChannelName), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ChannelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool TryResolveRuntimeChannel(UdlModuleExposureDefinition definition, out Item? runtimeChannel)
    {
        runtimeChannel = EnumerateClientItems()
            .FirstOrDefault(candidate => string.Equals(GetRelativeRuntimePath(candidate), $"{definition.ModuleName}.{definition.ChannelName}", StringComparison.OrdinalIgnoreCase));
        return runtimeChannel is not null;
    }

    private void OnExposureTargetChanged(object? sender, DataChangedEventArgs e)
    {
        if (_observedItem is null)
        {
            return;
        }

        if (!TryGetExposureBitMetadata(e.Item, out var moduleName, out var channelName, out var bitIndex))
        {
            return;
        }

        if (!string.Equals(e.ParameterName, "Value", StringComparison.Ordinal)
            && e.ChangeKind != DataChangeKind.ValueUpdated)
        {
            return;
        }

        ApplyBitWriteback(moduleName, channelName, bitIndex, TryReadBool(e.Item.Value, false));
    }

    private void ApplyBitWriteback(string moduleName, string channelName, int bitIndex, bool enabled)
    {
        var effectiveChannelName = ResolveEffectiveWriteChannelName(moduleName, channelName);
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = effectiveChannelName }, out var runtimeChannel)
            || runtimeChannel is null)
        {
            return;
        }

        var writeValueItem = ResolveWriteValueItem(runtimeChannel);
        var currentMask = TryReadUnsignedInteger(writeValueItem.Value, out uint currentValue) ? currentValue : 0u;
        var nextMask = enabled
            ? currentMask | (1u << bitIndex)
            : currentMask & ~(1u << bitIndex);

        SetRuntimeExposureBitValue(moduleName, channelName, bitIndex, enabled);
        WriteDiagnosticLog(
            $"Bit writeback requested module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} enabled={enabled} sourceBit={ResolveExposureBitPath(moduleName, channelName, bitIndex)} writeTarget={writeValueItem.Path ?? runtimeChannel.Path ?? "<none>"} writeType={writeValueItem.Value?.GetType().Name ?? "<null>"} currentMask=0x{currentMask:X} nextMask=0x{nextMask:X} currentValue={FormatDiagnosticValue(writeValueItem.Value)}");
        if (nextMask == currentMask)
        {
            WriteDiagnosticLog(
                $"Bit writeback skipped module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} reason=mask-unchanged mask=0x{currentMask:X}");
            return;
        }

        writeValueItem.Value = ConvertMaskValue(writeValueItem.Value, nextMask);
        WriteDiagnosticLog(
            $"Bit writeback applied module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} writeTarget={writeValueItem.Path ?? runtimeChannel.Path ?? "<none>"} written={FormatDiagnosticValue(writeValueItem.Value)} mask=0x{nextMask:X}");
    }

    private void SetRuntimeExposureBitValue(string moduleName, string channelName, int bitIndex, bool enabled)
    {
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = channelName }, out var sourceChannel)
            || sourceChannel is null
            || !sourceChannel.Has("Bits"))
        {
            return;
        }

        var bitName = $"Bit{bitIndex}";
        if (sourceChannel["Bits"].Has(bitName))
        {
            SetItemValueIfDifferent(sourceChannel["Bits"][bitName], enabled);
        }
    }

    private string ResolveExposureBitPath(string moduleName, string channelName, int bitIndex)
    {
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = channelName }, out var sourceChannel)
            || sourceChannel is null
            || !sourceChannel.Has("Bits"))
        {
            return "<none>";
        }

        var bitName = $"Bit{bitIndex}";
        return sourceChannel["Bits"].Has(bitName)
            ? sourceChannel["Bits"][bitName].Path ?? "<none>"
            : "<none>";
    }

    private string ResolveEffectiveWriteChannelName(string moduleName, string channelName)
    {
        if (!string.Equals(channelName, "Read", StringComparison.OrdinalIgnoreCase) || Item is null)
        {
            return channelName;
        }

        var definition = UdlModuleExposureDefinitionCodec.ParseDefinitions(Item.UdlModuleExposureDefinitions)
            .FirstOrDefault(candidate => string.Equals(candidate.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(candidate.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));
        if (definition?.RouteReadInputToSetRequest != true)
        {
            return channelName;
        }

        return TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = "Set" }, out _)
            ? "Set"
            : channelName;
    }

    private void ForceAttachedItemsResync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ForceAttachedItemsResync);
            return;
        }

        if (Volatile.Read(ref _hasAttachedPaths) != 1)
        {
            return;
        }

        _lastSynchronizedAttachSignature = string.Empty;
        var attachmentsChanged = SynchronizeAttachedItems();
        RebuildAttachRows();
        if (attachmentsChanged)
        {
            Host?.RefreshFolderBindings(Item?.FolderName ?? string.Empty);
        }

        RefreshPresentation();
    }

    private static bool IsRuntimeChannelItem(Item item)
        => TargetPathHelper.SplitPathSegments(GetRelativeRuntimePath(item)).Count == 2;

    private bool UpsertRuntimeExposureBits(Item runtimeChannel, UdlModuleExposureDefinition definition, int bitCount)
    {
        var structureChanged = false;
        if (!runtimeChannel.Has("Bits"))
        {
            runtimeChannel["Bits"] = new Item("Bits", path: runtimeChannel.Path);
            structureChanged = true;
        }

        var bitsRoot = runtimeChannel["Bits"];
        structureChanged |= SetParameterValueIfDifferent(bitsRoot, "Kind", "Group");
        structureChanged |= SetParameterValueIfDifferent(bitsRoot, "Title", $"{definition.ModuleName}.{definition.ChannelName} Bits");

        var writeTargetChannel = ResolveExposureWriteTargetChannel(runtimeChannel, definition);
        var writable = !writeTargetChannel.Params.Has("Writable") || TryReadBool(writeTargetChannel.Params["Writable"].Value, false);
        var valueSourceItem = ResolveExposureBitValueSourceItem(runtimeChannel, definition, writeTargetChannel);
        var rawValue = TryReadUnsignedInteger(valueSourceItem.Value, out uint currentValue) ? currentValue : 0u;
        var labels = ParseBitLabels(definition.BitLabels);
        var desiredBitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            var bitName = $"Bit{bitIndex}";
            desiredBitNames.Add(bitName);

            if (!bitsRoot.Has(bitName))
            {
                bitsRoot[bitName] = new Item(bitName, path: bitsRoot.Path);
                structureChanged = true;
            }

            var bitItem = bitsRoot[bitName];
            var label = GetBitLabel(bitIndex, labels);
            var bitValue = ((rawValue >> bitIndex) & 1u) == 1u;

            SetItemValueIfDifferent(bitItem, bitValue);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "Kind", "Bool");
            structureChanged |= SetParameterValueIfDifferent(bitItem, "Format", "bool");
            structureChanged |= SetParameterValueIfDifferent(bitItem, "Title", label);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "Text", label);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "ModuleName", definition.ModuleName);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "ChannelName", definition.ChannelName);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "BitIndex", bitIndex);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "SourcePath", runtimeChannel.Path ?? string.Empty);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "Writable", writable);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "WritePath", string.Empty);
            structureChanged |= SetParameterValueIfDifferent(bitItem, "WriteMode", string.Empty);
        }

        foreach (var staleBitName in bitsRoot.GetDictionary().Keys.Except(desiredBitNames, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            bitsRoot.Remove(staleBitName);
            structureChanged = true;
        }

        return structureChanged;
    }

    private Item ResolveExposureWriteTargetChannel(Item runtimeChannel, UdlModuleExposureDefinition definition)
    {
        if (!definition.RouteReadInputToSetRequest
            || !string.Equals(definition.ChannelName, "Read", StringComparison.OrdinalIgnoreCase)
            || !TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = definition.ModuleName, ChannelName = "Set" }, out var setChannel)
            || setChannel is null)
        {
            return runtimeChannel;
        }

        return setChannel;
    }

    private Item ResolveExposureBitValueSourceItem(Item runtimeChannel, UdlModuleExposureDefinition definition, Item writeTargetChannel)
    {
        if (string.Equals(definition.ChannelName, "Set", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveWriteValueItem(runtimeChannel);
        }

        if (definition.RouteReadInputToSetRequest && string.Equals(definition.ChannelName, "Read", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveWriteValueItem(writeTargetChannel);
        }

        return runtimeChannel;
    }

    private static Item ResolveWriteValueItem(Item runtimeChannel)
    {
        var writeMode = runtimeChannel.Params.Has("WriteMode") ? runtimeChannel.Params["WriteMode"].Value?.ToString() ?? string.Empty : string.Empty;
        if (string.Equals(writeMode, SignalWriteMode.Request.ToString(), StringComparison.OrdinalIgnoreCase) && runtimeChannel.Has("Request"))
        {
            return runtimeChannel["Request"];
        }

        if (runtimeChannel.Has("Request") && !runtimeChannel.Params.Has("WriteMode"))
        {
            return runtimeChannel["Request"];
        }

        return runtimeChannel;
    }

    private static int ResolveBitCount(UdlModuleExposureDefinition definition, Item runtimeChannel)
    {
        if (definition.BitCount > 0)
        {
            return Math.Clamp(definition.BitCount, 1, 32);
        }

        if (!string.IsNullOrWhiteSpace(definition.Format))
        {
            var definitionBitCount = GetBitCount(definition.Format);
            if (definitionBitCount > 0)
            {
                return definitionBitCount;
            }
        }

        var runtimeFormat = runtimeChannel.Params.Has("Format")
            ? runtimeChannel.Params["Format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        return GetBitCount(runtimeFormat);
    }

    private static bool RemoveRuntimeExposureBits(Item runtimeChannel)
    {
        if (!runtimeChannel.Has("Bits"))
        {
            return false;
        }

        runtimeChannel.Remove("Bits");
        return true;
    }

    private static void RemoveLegacyExposureItems(FolderItemModel item)
    {
        HostRegistries.Data.Remove(GetLegacyExposureBasePath(item));
    }

    private static bool SetItemValueIfDifferent(Item item, object? value)
    {
        if (ValuesEqual(item.Value, value))
        {
            return false;
        }

        item.Value = value!;
        return true;
    }

    private static bool SetParameterValueIfDifferent(Item item, string parameterName, object? value)
    {
        var parameter = item.Params[parameterName];
        if (ValuesEqual(parameter.Value, value))
        {
            return false;
        }

        parameter.Value = value!;
        return true;
    }

    private static object ConvertMaskValue(object? existingValue, uint mask)
    {
        return existingValue switch
        {
            byte => (byte)mask,
            sbyte => unchecked((sbyte)mask),
            short => (short)mask,
            ushort => (ushort)mask,
            int => unchecked((int)mask),
            long => (long)mask,
            ulong => (ulong)mask,
            float => (float)mask,
            double => (double)mask,
            decimal => (decimal)mask,
            _ => unchecked((int)mask)
        };
    }

    private static bool TryGetExposureBitMetadata(Item item, out string moduleName, out string channelName, out int bitIndex)
    {
        moduleName = string.Empty;
        channelName = string.Empty;
        bitIndex = -1;

        if (!item.Params.Has("ModuleName")
            || !item.Params.Has("ChannelName")
            || !item.Params.Has("BitIndex"))
        {
            return false;
        }

        moduleName = item.Params["ModuleName"].Value?.ToString() ?? string.Empty;
        channelName = item.Params["ChannelName"].Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moduleName)
            || string.IsNullOrWhiteSpace(channelName)
            || !int.TryParse(item.Params["BitIndex"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitIndex))
        {
            return false;
        }

        return true;
    }


    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is double leftDouble && right is double rightDouble)
        {
            return leftDouble.Equals(rightDouble) || (double.IsNaN(leftDouble) && double.IsNaN(rightDouble));
        }

        if (left is float leftFloat && right is float rightFloat)
        {
            return leftFloat.Equals(rightFloat) || (float.IsNaN(leftFloat) && float.IsNaN(rightFloat));
        }

        return Equals(left, right);
    }

    private static string FormatDiagnosticValue(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value is IFormattable formattable
            ? $"{formattable.ToString(null, CultureInfo.InvariantCulture)} ({value.GetType().Name})"
            : $"{value} ({value.GetType().Name})";
    }

    private static bool TryReadBool(object? value, bool fallback)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            uint uintValue => uintValue != 0,
            _ => fallback
        };
    }

    private static bool TryReadUnsignedInteger(object? value, out uint parsed)
    {
        switch (value)
        {
            case byte byteValue:
                parsed = byteValue;
                return true;
            case sbyte sbyteValue:
                parsed = unchecked((uint)sbyteValue);
                return true;
            case short shortValue:
                parsed = unchecked((uint)shortValue);
                return true;
            case ushort ushortValue:
                parsed = ushortValue;
                return true;
            case int intValue:
                parsed = unchecked((uint)intValue);
                return true;
            case uint uintValue:
                parsed = uintValue;
                return true;
            case long longValue:
                parsed = unchecked((uint)longValue);
                return true;
            case float floatValue when floatValue >= 0f && floatValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(floatValue, MidpointRounding.AwayFromZero));
                return true;
            case double doubleValue when doubleValue >= 0d && doubleValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(doubleValue, MidpointRounding.AwayFromZero));
                return true;
            case decimal decimalValue when decimalValue >= 0m && decimalValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(decimalValue, MidpointRounding.AwayFromZero));
                return true;
            case ulong ulongValue:
                parsed = unchecked((uint)ulongValue);
                return true;
            case bool boolValue:
                parsed = boolValue ? 1u : 0u;
                return true;
            case string text when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue):
                parsed = stringValue;
                return true;
            default:
                parsed = 0;
                return false;
        }
    }

    private static int GetBitCount(string? format)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(format)
            ? string.Empty
            : format.Trim().Split(':', 2, StringSplitOptions.TrimEntries)[0].ToLowerInvariant();

        return normalizedKind switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private static Dictionary<int, string> ParseBitLabels(string? rawLabels)
    {
        var labels = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(rawLabels))
        {
            return labels;
        }

        var lines = rawLabels
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!key.StartsWith("Bit", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(key[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitIndex)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            labels[bitIndex] = value;
        }

        return labels;
    }

    private static string GetBitLabel(int bitIndex, IReadOnlyDictionary<int, string> labels)
        => labels.TryGetValue(bitIndex, out var label) ? label : $"Bit{bitIndex}";

    private static string NormalizeRuntimeSegment(string value)
    {
        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(value);
        return string.IsNullOrWhiteSpace(normalized) ? "Item" : normalized.Replace('.', '_');
    }

    private static HashSet<string> ParseAttachedPaths(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        var parsed = serialized
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path.Count(static ch => ch == '.'))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in parsed)
        {
            var hasAncestor = normalized.Any(existing => path.StartsWith(existing + ".", StringComparison.OrdinalIgnoreCase));
            if (!hasAncestor)
            {
                normalized.Add(path);
            }
        }

        return normalized;
    }

    private static string NormalizeClientName(FolderItemModel item)
        => string.IsNullOrWhiteSpace(item.Name) ? "UdlClientControl" : item.Name.Trim();

    private static IHostUdlClient CreateClient(FolderItemModel item)
    {
        if (item.UdlClientDemoEnabled)
        {
            return new SimulatedHostUdlClient(
                NormalizeClientName(item),
                item.UdlClientHost,
                item.UdlClientPort,
                UdlDemoModuleDefinitionCodec.ParseDefinitions(item.UdlDemoModuleDefinitions));
        }

        return new HostUdlClient(NormalizeClientName(item), item.UdlClientHost, item.UdlClientPort);
    }

    private static string GetStatusBasePath(FolderItemModel item)
        => $"Studio.{item.FolderName}.{NormalizeClientName(item)}.Status";

    private static string GetAttachOptionsBasePath(FolderItemModel item)
        => $"{GetStatusBasePath(item)}/AttachOptions";

    private static string GetLegacyExposureBasePath(FolderItemModel item)
        => $"Studio.{item.FolderName}.UdlClientRuntime.{NormalizeClientName(item)}.Modules";

    private static void RemovePublishedRuntimeItems(FolderItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        var prefix = $"Runtime.UdlClient.{NormalizeClientName(item)}";
        var keys = HostRegistries.Data.GetAllKeys()
            .Where(key => string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keys)
        {
            HostRegistries.Data.Remove(key);
        }
    }

    private static string GetRelativeRuntimePath(Item item)
    {
        var fullPath = item.Path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var segments = TargetPathHelper.SplitPathSegments(fullPath);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var runtimeRootIndex = -1;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], "Runtime", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], "UdlClient", StringComparison.OrdinalIgnoreCase))
            {
                runtimeRootIndex = index;
                break;
            }
        }

        if (runtimeRootIndex < 0)
        {
            return fullPath;
        }

        var relativeSegments = segments.Skip(runtimeRootIndex + 3).ToArray();
        return relativeSegments.Length == 0 ? string.Empty : string.Join('.', relativeSegments);
    }
}

public partial class UdlClientWidget : UdlClientControl
{
}

public sealed class UdlClientModuleRow : NotifyBase
{
    private readonly FolderItemModel _ownerItem;
    private readonly int _runtimeChannelCount;
    private readonly int _configuredChannelCount;
    private readonly int _activeHelperCount;
    private readonly int _missingConfiguredChannelCount;

    public UdlClientModuleRow(
        FolderItemModel ownerItem,
        string moduleName,
        IReadOnlyCollection<UdlRuntimeModuleChannelDescriptor> runtimeChannels,
        IReadOnlyCollection<UdlModuleExposureDefinition> definitions)
    {
        _ownerItem = ownerItem;
        ModuleName = moduleName?.Trim() ?? string.Empty;
        _runtimeChannelCount = runtimeChannels.Count;
        _configuredChannelCount = definitions.Count;
        _activeHelperCount = definitions.Count(static definition => definition.ExposeBits);

        var runtimeChannelKeys = new HashSet<string>(
            runtimeChannels.Select(static channel => UdlModuleExposureEditorRow.BuildKey(channel.ModuleName, channel.ChannelName)),
            StringComparer.OrdinalIgnoreCase);
        _missingConfiguredChannelCount = definitions.Count(definition => !runtimeChannelKeys.Contains(UdlModuleExposureEditorRow.BuildKey(definition.ModuleName, definition.ChannelName)));
    }

    public string ModuleName { get; }

    public bool CanDelete => _configuredChannelCount > 0;

    public string SummaryText
    {
        get
        {
            if (_runtimeChannelCount > 0)
            {
                return _configuredChannelCount > 0
                    ? $"{_runtimeChannelCount} runtime channels | {_configuredChannelCount} configured channels | {_activeHelperCount} helper sets active"
                    : $"{_runtimeChannelCount} runtime channels | no helper items configured";
            }

            return _configuredChannelCount > 0
                ? $"Persisted module | {_configuredChannelCount} configured channels | {_activeHelperCount} helper sets active"
                : "Persisted module | no helper items configured";
        }
    }

    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    public string AlertText
    {
        get
        {
            if (_runtimeChannelCount == 0 && _configuredChannelCount > 0)
            {
                return "Runtime module is currently unavailable. Editing uses persisted configuration only.";
            }

            if (_missingConfiguredChannelCount > 0)
            {
                return $"{_missingConfiguredChannelCount} configured channels are currently missing at runtime.";
            }

            return string.Empty;
        }
    }

    public string RowBackground => _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(RowBorderBrush));
        OnPropertyChanged(nameof(PrimaryForeground));
        OnPropertyChanged(nameof(SecondaryForeground));
        OnPropertyChanged(nameof(CanDelete));
    }
}
