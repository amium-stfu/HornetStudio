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
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Logging;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class UdlClientControl : EditorTemplateControl
{
    public static readonly DirectProperty<UdlClientControl, string> SocketTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(SocketText), control => control.SocketText);

    public static readonly DirectProperty<UdlClientControl, string> ConnectionStateTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ConnectionStateText), control => control.ConnectionStateText);

    public static readonly DirectProperty<UdlClientControl, string> AutoConnectTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(AutoConnectText), control => control.AutoConnectText);

    public static readonly DirectProperty<UdlClientControl, string> ItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ItemCountText), control => control.ItemCountText);

    public static readonly DirectProperty<UdlClientControl, string> ReceivedItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(ReceivedItemCountText), control => control.ReceivedItemCountText);

    public static readonly DirectProperty<UdlClientControl, string> AttachedItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, string>(nameof(AttachedItemCountText), control => control.AttachedItemCountText);

    public static readonly DirectProperty<UdlClientControl, bool> HasNoReceivedItemsProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(HasNoReceivedItems), control => control.HasNoReceivedItems);

    public static readonly DirectProperty<UdlClientControl, bool> HasNoAttachedItemsProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(HasNoAttachedItems), control => control.HasNoAttachedItems);

    public static readonly DirectProperty<UdlClientControl, bool> CanAddDemoProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(CanAddDemo), control => control.CanAddDemo);

    public static readonly DirectProperty<UdlClientControl, bool> CanToggleAllReceivedItemsProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(CanToggleAllReceivedItems), control => control.CanToggleAllReceivedItems);

    public static readonly DirectProperty<UdlClientControl, bool> AreAllReceivedItemsAttachedProperty =
        AvaloniaProperty.RegisterDirect<UdlClientControl, bool>(nameof(AreAllReceivedItemsAttached), control => control.AreAllReceivedItemsAttached);

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
    private string _receivedItemCountText = "0";
    private string _attachedItemCountText = "0";
    private string _moduleCountText = "0";
    private IBrush _connectionStatusBackground = Brushes.Black;
    private IBrush _connectionStatusForeground = Brushes.White;
    private IBrush _connectionStatusHoverBackground = Brushes.DimGray;
    private bool _canToggleConnection = true;
    private bool _hasNoReceivedItems = true;
    private bool _hasNoAttachedItems = true;
    private bool _canAddDemo;
    private bool _canToggleAllReceivedItems;
    private bool _areAllReceivedItemsAttached;
    private bool _hasNoModules = true;
    private string _connectionToggleText = "Connect";
    private string _publishedStatusBasePath = string.Empty;
    private string _publishedAttachOptionsBasePath = string.Empty;

    public UdlClientControl()
    {
        AttachRows = [];
        ReceivedItems = [];
        AttachedItems = [];
        Modules = [];
        ReceivedItems.CollectionChanged += (_, _) => UpdateReceivedItemCollectionState();
        AttachedItems.CollectionChanged += (_, _) => UpdateAttachedItemCollectionState();
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

    public ObservableCollection<UdlClientAttachSectionRow> ReceivedItems { get; }

    public ObservableCollection<UdlClientAttachSectionRow> AttachedItems { get; }

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

    public string ReceivedItemCountText
    {
        get => _receivedItemCountText;
        private set => SetAndRaise(ReceivedItemCountTextProperty, ref _receivedItemCountText, value);
    }

    public string AttachedItemCountText
    {
        get => _attachedItemCountText;
        private set => SetAndRaise(AttachedItemCountTextProperty, ref _attachedItemCountText, value);
    }

    public bool HasNoReceivedItems
    {
        get => _hasNoReceivedItems;
        private set => SetAndRaise(HasNoReceivedItemsProperty, ref _hasNoReceivedItems, value);
    }

    public bool HasNoAttachedItems
    {
        get => _hasNoAttachedItems;
        private set => SetAndRaise(HasNoAttachedItemsProperty, ref _hasNoAttachedItems, value);
    }

    public bool CanAddDemo
    {
        get => _canAddDemo;
        private set => SetAndRaise(CanAddDemoProperty, ref _canAddDemo, value);
    }

    public bool CanToggleAllReceivedItems
    {
        get => _canToggleAllReceivedItems;
        private set => SetAndRaise(CanToggleAllReceivedItemsProperty, ref _canToggleAllReceivedItems, value);
    }

    public bool AreAllReceivedItemsAttached
    {
        get => _areAllReceivedItemsAttached;
        private set => SetAndRaise(AreAllReceivedItemsAttachedProperty, ref _areAllReceivedItemsAttached, value);
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

    private FolderItemModel? ItemModel => DataContext as FolderItemModel;

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
        ReceivedItems.Clear();
        AttachedItems.Clear();
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
        var item = ItemModel;
        if (!IsUdlClientItem(item))
        {
            if (_observedItem is not null)
            {
                TearDownClient();
            }

            RemovePublishedStatusItems();
            UnhookObservedItem();
            RebuildAttachRows();
            RebuildAttachSectionRows();
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
        RebuildAttachSectionRows();
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

        if (e.PropertyName == nameof(FolderItemModel.UdlAttachedItemPaths))
        {
            if (_observedItem is not null)
            {
                UpdateAttachedPathsFlag(_observedItem);
            }
            else
            {
                Volatile.Write(ref _hasAttachedPaths, 0);
            }

            var attachmentsChanged = SynchronizeAttachedItems();
            RebuildAttachRows();
            RebuildAttachSectionRows();
            if (attachmentsChanged)
            {
                Host?.RefreshFolderBindings(ItemModel?.FolderName ?? string.Empty);
            }
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

            foreach (var row in ReceivedItems)
            {
                row.RefreshTheme();
            }

            foreach (var row in AttachedItems)
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
            RebuildAttachSectionRows();
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
            RebuildAttachSectionRows();
            if (_client is not null)
            {
                DisconnectInternal();
                ConnectInternal();
            }
        }
    }

    private async System.Threading.Tasks.Task EnsureAutoConnectAsync()
    {
        if (!IsUdlClientItem(ItemModel) || ItemModel?.UdlClientAutoConnect != true || _client is not null)
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

        var item = ItemModel;
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
        RemovePublishedRuntimeItems(ItemModel);
        RebuildAttachSectionRows();
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
        RemovePublishedRuntimeItems(_observedItem ?? ItemModel);

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
        RemovePublishedRuntimeItems(_observedItem ?? ItemModel);
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
        RebuildAttachSectionRows();
        RebuildModuleRows();
    }

    private void LogRuntimeRootItems(IReadOnlyList<ItemModel> runtimeItems)
    {
        if (_client is null)
        {
            return;
        }

        var rootItems = runtimeItems
            .Select(static item => new
            {
                ItemModel = item,
                RelativePath = GetRelativeRuntimePath(item)
            })
            .Where(static entry => IsRootAttachPath(entry.RelativePath))
            .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var signature = string.Join("|", rootItems.Select(static entry => $"{entry.ItemModel.Name}:{entry.ItemModel.Path}:{entry.RelativePath}"));
        if (string.Equals(_lastLoggedRuntimeRootsSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedRuntimeRootsSignature = signature;
        WriteDiagnosticLog($"Runtime root modules client={_client.Name} count={rootItems.Length}");

        foreach (var rootItem in rootItems.Select(static (entry, index) => new { Index = index, entry.ItemModel, entry.RelativePath }))
        {
            WriteDiagnosticLog($"Runtime root[{rootItem.Index}] name={rootItem.ItemModel.Name ?? string.Empty} fullPath={rootItem.ItemModel.Path ?? string.Empty} relativePath={rootItem.RelativePath}");
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
        RebuildAttachSectionRows();
        if (attachmentsChanged)
        {
            Host?.RefreshFolderBindings(ItemModel?.FolderName ?? string.Empty);
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

        var item = ItemModel;
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

    private void RebuildAttachSectionRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildAttachSectionRows);
            return;
        }

        var item = ItemModel;
        if (item is null)
        {
            ReceivedItems.Clear();
            AttachedItems.Clear();
            UpdateReceivedItemCollectionState();
            UpdateAttachedItemCollectionState();
            return;
        }

        var nextReceivedItems = new List<UdlClientAttachSectionRow>();
        var nextAttachedItems = new List<UdlClientAttachSectionRow>();

        var attachedPaths = ParseAttachedPaths(item.UdlAttachedItemPaths);
        nextReceivedItems.AddRange(BuildReceivedAttachSectionRows(item, GetReceivedRuntimeRootPaths(EnumerateClientItems()), attachedPaths));

        foreach (var attachedPath in attachedPaths)
        {
            var isLive = TryResolveRuntimeItem(attachedPath, out _);
            var moduleName = TryGetRuntimeModuleName(attachedPath, out var resolvedModuleName)
                ? resolvedModuleName
                : string.Empty;
            var summaryText = isLive
                ? "Attached item resolves to a live runtime item."
                : "Saved attachment does not currently resolve to a runtime item.";
            var alertText = isLive ? string.Empty : "Runtime item is currently unavailable.";

            nextAttachedItems.Add(new UdlClientAttachSectionRow(
                ownerItem: item,
                relativePath: attachedPath,
                summaryText: summaryText,
                alertText: alertText,
                actionText: "Detach",
                canExecuteAction: true,
                statusBrush: isLive ? Brushes.ForestGreen : Brushes.Firebrick,
                moduleName: moduleName));
        }

        if (!HasAttachSectionRowsChanged(ReceivedItems, nextReceivedItems)
            && !HasAttachSectionRowsChanged(AttachedItems, nextAttachedItems))
        {
            UpdateReceivedItemCollectionState();
            UpdateAttachedItemCollectionState();
            return;
        }

        ReplaceAttachSectionRows(target: ReceivedItems, rows: nextReceivedItems);
        ReplaceAttachSectionRows(target: AttachedItems, rows: nextAttachedItems);
        UpdateReceivedItemCollectionState();
        UpdateAttachedItemCollectionState();
    }

    private static bool HasAttachSectionRowsChanged(
        IReadOnlyList<UdlClientAttachSectionRow> currentRows,
        IReadOnlyList<UdlClientAttachSectionRow> nextRows)
    {
        if (currentRows.Count != nextRows.Count)
        {
            return true;
        }

        for (var index = 0; index < currentRows.Count; index++)
        {
            if (!currentRows[index].HasSamePresentation(nextRows[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReplaceAttachSectionRows(
        ObservableCollection<UdlClientAttachSectionRow> target,
        IEnumerable<UdlClientAttachSectionRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
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

        if (e.PropertyName != nameof(AttachItemEditorRow.IsAttached) || ItemModel is null)
        {
            return;
        }

        ItemModel.UdlAttachedItemPaths = SerializeAttachedPaths(AttachRows
            .Where(static row => row.IsAttached)
            .Select(static row => row.RelativePath));

        SynchronizeAttachedItems();
        RebuildAttachSectionRows();
        RefreshPresentation();
    }

    private static IReadOnlyList<UdlClientAttachSectionRow> BuildReceivedAttachSectionRows(
        FolderItemModel item,
        IEnumerable<string> runtimeRootPaths,
        IReadOnlySet<string> attachedPaths)
    {
        ArgumentNullException.ThrowIfNull(item);

        return (runtimeRootPaths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var isAttached = attachedPaths.Contains(path);
                return new UdlClientAttachSectionRow(
                    ownerItem: item,
                    relativePath: path,
                    summaryText: isAttached
                        ? "Runtime item is available and already attached to the UI."
                        : "Runtime item is available and can be attached to the UI.",
                    alertText: isAttached ? "ItemModel is already attached." : string.Empty,
                    actionText: isAttached ? "Attached" : "Attach",
                    canExecuteAction: !isAttached,
                    statusBrush: isAttached ? Brushes.ForestGreen : Brushes.Gray);
            })
            .ToArray();
    }

    private void OnAttachReceivedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: UdlClientAttachSectionRow row }
            || !row.CanExecuteAction)
        {
            return;
        }

        ItemModel.UdlAttachedItemPaths = AddAttachedPath(ItemModel.UdlAttachedItemPaths, row.RelativePath);
        e.Handled = true;
    }

    private async void OnAddDemoClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || ItemModel.UdlClientDemoEnabled != true
            || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await UdlDemoModulesDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            ownerItem: ItemModel,
            rawDefinitions: ItemModel.UdlDemoModuleDefinitions);
        if (result is null)
        {
            return;
        }

        ItemModel.UdlDemoModuleDefinitions = result;
        e.Handled = true;
    }

    private void OnToggleAllReceivedItemsClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null || sender is not ToggleButton toggleButton)
        {
            return;
        }

        var receivedPaths = ReceivedItems.Select(static row => row.RelativePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (receivedPaths.Length == 0)
        {
            return;
        }

        ItemModel.UdlAttachedItemPaths = toggleButton.IsChecked == true
            ? AddAttachedPaths(ItemModel.UdlAttachedItemPaths, receivedPaths)
            : RemoveAttachedPaths(ItemModel.UdlAttachedItemPaths, receivedPaths);
        e.Handled = true;
    }

    private async void OnEditAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: UdlClientAttachSectionRow row }
            || !row.CanEdit
            || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await UdlModuleExposureDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: ItemModel.UdlModuleExposureDefinitions,
            runtimeChannels: GetRuntimeChannelDescriptors(),
            moduleName: row.ModuleName);
        if (result is null)
        {
            return;
        }

        ItemModel.UdlModuleExposureDefinitions = result;
        PublishExposureItems();
        RebuildModuleRows();
        RebuildAttachSectionRows();
        e.Handled = true;
    }

    private void OnDetachAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: UdlClientAttachSectionRow row }
            || !row.CanDetach)
        {
            return;
        }

        ItemModel.UdlAttachedItemPaths = RemoveAttachedPath(ItemModel.UdlAttachedItemPaths, row.RelativePath);
        e.Handled = true;
    }

    private static IEnumerable<string> GetUdlAttachOptions(FolderItemModel item)
    {
        var normalizedName = NormalizeClientName(item);
        var prefixes = UdlPathHelper.GetAttachOptionPrefixes(item.FolderName, normalizedName);

        return HostRegistries.Data.GetAllKeys()
            .SelectMany(key => prefixes.Select(prefix => TryGetAttachRootOption(key, prefix, TargetPathHelper.NormalizeComparablePath(prefix))))
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void UpdateReceivedItemCollectionState()
    {
        HasNoReceivedItems = ReceivedItems.Count == 0;
        ReceivedItemCountText = ReceivedItems.Count.ToString(CultureInfo.InvariantCulture);
        UpdateReceivedSectionActionsState();
    }

    private void UpdateAttachedItemCollectionState()
    {
        HasNoAttachedItems = AttachedItems.Count == 0;
        AttachedItemCountText = AttachedItems.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateReceivedSectionActionsState()
    {
        var item = ItemModel;
        CanAddDemo = item?.UdlClientDemoEnabled == true;

        if (item is null)
        {
            CanToggleAllReceivedItems = false;
            AreAllReceivedItemsAttached = false;
            return;
        }

        var receivedPaths = ReceivedItems.Select(static row => row.RelativePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var attachedPaths = ParseAttachedPaths(item.UdlAttachedItemPaths);

        CanToggleAllReceivedItems = receivedPaths.Length > 0;
        AreAllReceivedItemsAttached = receivedPaths.Length > 0
            && receivedPaths.All(attachedPaths.Contains);
    }

    private bool SynchronizeAttachedItems()
    {
        var item = ItemModel;
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

        var attachments = new List<(string RelativePath, string Alias, ItemModel RuntimeItem)>();
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

    private bool TryResolveRuntimeItem(string relativePath, out ItemModel? resolved)
    {
        resolved = ResolveRuntimeItemFromSources(ItemModel, EnumerateClientItems(), relativePath);
        return resolved is not null;
    }

    private static ItemModel? ResolveRuntimeItemFromSources(
        FolderItemModel? ownerItem,
        IEnumerable<ItemModel> runtimeItems,
        string relativePath)
    {
        var normalizedRelativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativePath);
        var resolved = (runtimeItems ?? [])
            .FirstOrDefault(candidate => string.Equals(GetRelativeRuntimePath(candidate), normalizedRelativePath, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null)
        {
            return resolved;
        }

        if (ownerItem is null || string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return null;
        }

        foreach (var runtimePath in EnumerateRuntimeRegistryPaths(ownerItem, normalizedRelativePath))
        {
            if (HostRegistries.Data.TryResolve(runtimePath, out resolved) && resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> EnumerateRuntimeRegistryPaths(FolderItemModel ownerItem, string relativePath)
    {
        var normalizedRelativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativePath);
        return UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(ownerItem))
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => string.IsNullOrWhiteSpace(normalizedRelativePath)
                ? prefix
                : $"{prefix}.{normalizedRelativePath}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ItemModel> EnumerateClientItems()
    {
        if (_client is null)
        {
            return [];
        }

        var items = new List<ItemModel>();
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

    private static void AppendItem(ItemModel item, ICollection<ItemModel> items)
    {
        items.Add(item);
        foreach (var child in item.GetDictionary().Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AppendItem(child, items);
        }
    }

    private IEnumerable<string> GetAttachOptions(FolderItemModel item)
    {
        var runtimePrefixes = UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(item));
        var runtimeOptions = HostRegistries.Data.GetAllKeys()
            .SelectMany(key => runtimePrefixes.Select(prefix => TryGetAttachRootOption(key, prefix, TargetPathHelper.NormalizeComparablePath(prefix))))
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

    private static IReadOnlyList<string> GetReceivedRuntimeRootPaths(IEnumerable<ItemModel> runtimeItems)
    {
        return (runtimeItems ?? [])
            .Select(GetRelativeRuntimePath)
            .Where(IsRootAttachPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

        var item = ItemModel;
        if (item is null)
        {
            WriteVerboseDiagnosticLog("Attach list open skipped because DataContext item is null");
            return;
        }

        var runtimePrefixes = UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(item));
        var registryOptions = HostRegistries.Data.GetAllKeys()
            .SelectMany(key => runtimePrefixes.Select(prefix => TryGetPathSuffix(key, prefix)))
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

        var item = ItemModel;
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
            CanAddDemo = false;
            CanToggleAllReceivedItems = false;
            AreAllReceivedItemsAttached = false;
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
        UpdateReceivedSectionActionsState();

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

    private void PublishAttachOptionItems(IReadOnlyList<ItemModel> runtimeItems)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(() => PublishAttachOptionItems(runtimeItems)).GetAwaiter().GetResult();
            return;
        }

        var item = ItemModel;
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

        var rootPaths = GetReceivedRuntimeRootPaths(runtimeItems);

        var desiredSnapshots = rootPaths
            .Select(rootPath =>
            {
                var snapshot = new ItemModel(rootPath, path: attachOptionsBasePath);
                snapshot.Properties["kind"].Value = "Status";
                snapshot.Properties["text"].Value = "AttachOption";
                snapshot.Properties["title"].Value = rootPath;
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

        PublishStatusValue(statusBasePath, "endpoint", SocketText, "UdlClient endpoint");
        PublishStatusValue(statusBasePath, "connection", ConnectionStateText, "Connection state");
        PublishStatusValue(statusBasePath, "item_count", GetRootItemCount(), "Discovered items");
        PublishStatusValue(statusBasePath, "message_counter", Interlocked.Read(ref _messageCounter), "Received messages");
        PublishStatusValue(statusBasePath, "auto_connect", item.UdlClientAutoConnect, "AutoConnect");
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

        var snapshot = new ItemModel(name, value, statusBasePath);
        snapshot.Properties["kind"].Value = "Status";
        snapshot.Properties["text"].Value = title;
        snapshot.Properties["title"].Value = title;
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
        if (ItemModel is null
            || sender is not Button { CommandParameter: UdlClientModuleRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await UdlModuleExposureDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: ItemModel.UdlModuleExposureDefinitions,
            runtimeChannels: GetRuntimeChannelDescriptors(),
            moduleName: row.ModuleName);
        if (result is null)
        {
            return;
        }

        ItemModel.UdlModuleExposureDefinitions = result;
        PublishExposureItems();
        RebuildModuleRows();
        e.Handled = true;
    }

    private async void OnDeleteModuleClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
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

        var definitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(ItemModel.UdlModuleExposureDefinitions)
            .Where(definition => !string.Equals(definition.ModuleName, row.ModuleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        ItemModel.UdlModuleExposureDefinitions = UdlModuleExposureDefinitionCodec.SerializeDefinitions(definitions);
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

        var item = ItemModel;
        if (item is null)
        {
            RemovePublishedExposureItems();
            return;
        }

        RemoveLegacyExposureItems(item);

        var definitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(item.UdlModuleExposureDefinitions);
        var desiredChannels = new Dictionary<string, (UdlModuleExposureDefinition Definition, ItemModel RuntimeChannel, int BitCount)>(StringComparer.OrdinalIgnoreCase);

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

        if (ItemModel is not null)
        {
            RemoveLegacyExposureItems(ItemModel);
        }

        if (structureChanged)
        {
            ForceAttachedItemsResync();
        }
    }

    private IReadOnlyList<UdlRuntimeModuleChannelDescriptor> GetRuntimeChannelDescriptors()
    {
        return BuildRuntimeChannelDescriptors(ItemModel, EnumerateClientItems());
    }

    private static IReadOnlyList<UdlRuntimeModuleChannelDescriptor> BuildRuntimeChannelDescriptors(
        FolderItemModel? ownerItem,
        IEnumerable<ItemModel> runtimeItems)
    {
        var clientDescriptors = (runtimeItems ?? [])
            .Select(static item => new
            {
                ItemModel = item,
                RelativePath = GetRelativeRuntimePath(item),
                Format = item.Properties.Has("format") ? item.Properties["format"].Value?.ToString() ?? string.Empty : string.Empty,
                Unit = item.Properties.Has("unit") ? item.Properties["unit"].Value?.ToString() ?? string.Empty : string.Empty
            })
            .Where(entry => TargetPathHelper.SplitPathSegments(entry.RelativePath).Count == 2)
            .Select(entry =>
            {
                var segments = TargetPathHelper.SplitPathSegments(entry.RelativePath);
                return new UdlRuntimeModuleChannelDescriptor
                {
                    ModuleName = segments[0],
                    ChannelName = NormalizeRuntimeChannelName(segments[1]),
                    Format = entry.Format,
                    Unit = entry.Unit,
                    BitCount = GetBitCount(entry.Format)
                };
            });

        var registryDescriptors = ownerItem is null
            ? []
            : HostRegistries.Data.GetAllKeys()
                .Select(key => ResolveRuntimeChannelDescriptor(ownerItem, key))
                .Where(descriptor => descriptor is not null)
                .Select(descriptor => descriptor!);

        return clientDescriptors
            .Concat(registryDescriptors)
            .GroupBy(static entry => UdlModuleExposureEditorRow.BuildKey(entry.ModuleName, entry.ChannelName), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ChannelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UdlRuntimeModuleChannelDescriptor? ResolveRuntimeChannelDescriptor(FolderItemModel ownerItem, string key)
    {
        if (!TryResolveRuntimeRegistryRelativePath(ownerItem, key, out var relativePath, out var resolvedFullPath))
        {
            return null;
        }

        var segments = TargetPathHelper.SplitPathSegments(relativePath);
        if (segments.Count != 2)
        {
            return null;
        }

        var format = HostRegistries.Data.TryResolve(resolvedFullPath, out var runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("format")
            ? runtimeItem.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        var unit = HostRegistries.Data.TryResolve(resolvedFullPath, out runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("unit")
            ? runtimeItem.Properties["unit"].Value?.ToString() ?? string.Empty
            : string.Empty;

        return new UdlRuntimeModuleChannelDescriptor
        {
            ModuleName = segments[0],
            ChannelName = NormalizeRuntimeChannelName(segments[1]),
            Format = format,
            Unit = unit,
            BitCount = GetBitCount(format)
        };
    }

    private static bool TryResolveRuntimeRegistryRelativePath(
        FolderItemModel ownerItem,
        string fullPath,
        out string relativePath,
        out string resolvedFullPath)
    {
        relativePath = string.Empty;
        resolvedFullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        foreach (var prefix in UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(ownerItem)))
        {
            var suffix = TryGetPathSuffix(fullPath, prefix);
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = TryGetPathSuffix(TargetPathHelper.NormalizeComparablePath(fullPath), TargetPathHelper.NormalizeComparablePath(prefix));
            }

            if (string.IsNullOrWhiteSpace(suffix))
            {
                continue;
            }

            relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(suffix);
            resolvedFullPath = TargetPathHelper.NormalizeConfiguredTargetPath(fullPath);
            return true;
        }

        return false;
    }

    private bool TryResolveRuntimeChannel(UdlModuleExposureDefinition definition, out ItemModel? runtimeChannel)
    {
        var expectedRelativePath = $"{definition.ModuleName}.{NormalizeRuntimeChannelName(definition.ChannelName)}";
        runtimeChannel = ResolveRuntimeItemFromSources(ItemModel, EnumerateClientItems(), expectedRelativePath);
        return runtimeChannel is not null;
    }

    private void OnExposureTargetChanged(object? sender, DataChangedEventArgs e)
    {
        if (_observedItem is null)
        {
            return;
        }

        if (!TryGetExposureBitMetadata(e.ItemModel, out var moduleName, out var channelName, out var bitIndex))
        {
            return;
        }

        if (!string.Equals(e.ParameterName, "read", StringComparison.Ordinal)
            && e.ChangeKind != DataChangeKind.ValueUpdated)
        {
            return;
        }

        ApplyBitWriteback(moduleName, channelName, bitIndex, TryReadBool(e.ItemModel.Value, false));
    }

    private void ApplyBitWriteback(string moduleName, string channelName, int bitIndex, bool enabled)
    {
        var effectiveChannelName = ResolveEffectiveWriteChannelName(moduleName, channelName);
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = effectiveChannelName }, out var runtimeChannel)
            || runtimeChannel is null)
        {
            return;
        }

        var currentWriteValue = GetChannelWriteValue(runtimeChannel);
        var currentMask = TryReadUnsignedInteger(currentWriteValue, out uint currentValue) ? currentValue : 0u;
        var nextMask = enabled
            ? currentMask | (1u << bitIndex)
            : currentMask & ~(1u << bitIndex);

        SetRuntimeExposureBitValue(moduleName, channelName, bitIndex, enabled);
        WriteDiagnosticLog(
            $"Bit writeback requested module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} enabled={enabled} sourceBit={ResolveExposureBitPath(moduleName, channelName, bitIndex)} writeTarget={runtimeChannel.Path ?? "<none>"} writeType={currentWriteValue?.GetType().Name ?? "<null>"} currentMask=0x{currentMask:X} nextMask=0x{nextMask:X} currentValue={FormatDiagnosticValue(currentWriteValue)}");
        if (nextMask == currentMask)
        {
            WriteDiagnosticLog(
                $"Bit writeback skipped module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} reason=mask-unchanged mask=0x{currentMask:X}");
            return;
        }

        SetChannelWriteValue(runtimeChannel, ConvertMaskValue(currentWriteValue, nextMask));
        WriteDiagnosticLog(
            $"Bit writeback applied module={moduleName} channel={channelName} effectiveChannel={effectiveChannelName} bit={bitIndex} writeTarget={runtimeChannel.Path ?? "<none>"} written={FormatDiagnosticValue(GetChannelWriteValue(runtimeChannel))} mask=0x{nextMask:X}");
    }

    private void SetRuntimeExposureBitValue(string moduleName, string channelName, int bitIndex, bool enabled)
    {
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = channelName }, out var sourceChannel)
            || sourceChannel is null
            || !sourceChannel.Has("bits"))
        {
            return;
        }

        var bitName = $"bit{bitIndex}";
        if (sourceChannel["bits"].Has(bitName))
        {
            SetItemValueIfDifferent(sourceChannel["bits"][bitName], enabled);
        }
    }

    private string ResolveExposureBitPath(string moduleName, string channelName, int bitIndex)
    {
        if (!TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = channelName }, out var sourceChannel)
            || sourceChannel is null
            || !sourceChannel.Has("bits"))
        {
            return "<none>";
        }

        var bitName = $"bit{bitIndex}";
        return sourceChannel["bits"].Has(bitName)
            ? sourceChannel["bits"][bitName].Path ?? "<none>"
            : "<none>";
    }

    private string ResolveEffectiveWriteChannelName(string moduleName, string channelName)
    {
        if (!string.Equals(channelName, "read", StringComparison.OrdinalIgnoreCase) || ItemModel is null)
        {
            return NormalizeRuntimeChannelName(channelName);
        }

        var definition = UdlModuleExposureDefinitionCodec.ParseDefinitions(ItemModel.UdlModuleExposureDefinitions)
            .FirstOrDefault(candidate => string.Equals(candidate.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(candidate.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));
        if (definition?.RouteReadInputToSetRequest != true)
        {
            return NormalizeRuntimeChannelName(channelName);
        }

        return TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = "set" }, out _)
            ? "set"
            : NormalizeRuntimeChannelName(channelName);
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
            Host?.RefreshFolderBindings(ItemModel?.FolderName ?? string.Empty);
        }

        RefreshPresentation();
    }

    private static bool TryGetRuntimeModuleName(string relativePath, out string moduleName)
    {
        var segments = TargetPathHelper.SplitPathSegments(relativePath);
        if (segments.Count >= 1 && !string.IsNullOrWhiteSpace(segments[0]))
        {
            moduleName = segments[0];
            return true;
        }

        moduleName = string.Empty;
        return false;
    }

    private static bool IsRuntimeChannelItem(ItemModel item)
        => TargetPathHelper.SplitPathSegments(GetRelativeRuntimePath(item)).Count == 2;

    private bool UpsertRuntimeExposureBits(ItemModel runtimeChannel, UdlModuleExposureDefinition definition, int bitCount)
    {
        var structureChanged = false;
        if (!runtimeChannel.Has("bits"))
        {
            runtimeChannel["bits"] = new ItemModel("bits", path: runtimeChannel.Path);
            structureChanged = true;
        }

        var bitsRoot = runtimeChannel["bits"];
        structureChanged |= SetPropertyValueIfDifferent(bitsRoot, "kind", "Group");
        structureChanged |= SetPropertyValueIfDifferent(bitsRoot, "title", $"{definition.ModuleName}.{definition.ChannelName} Bits");

        var writeTargetChannel = ResolveExposureWriteTargetChannel(runtimeChannel, definition);
        var writable = writeTargetChannel.Properties.Has("write")
            || !writeTargetChannel.Properties.Has("writable")
            || TryReadBool(writeTargetChannel.Properties["writable"].Value, false);
        var sourceValue = ResolveExposureBitValueSourceValue(runtimeChannel, definition, writeTargetChannel);
        var rawValue = TryReadUnsignedInteger(sourceValue, out uint currentValue) ? currentValue : 0u;
        var labels = ParseBitLabels(definition.BitLabels);
        var desiredBitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            var bitName = $"bit{bitIndex}";
            desiredBitNames.Add(bitName);

            if (!bitsRoot.Has(bitName))
            {
                bitsRoot[bitName] = new ItemModel(bitName, path: bitsRoot.Path);
                structureChanged = true;
            }

            var bitItem = bitsRoot[bitName];
            var label = GetBitLabel(bitIndex, labels);
            var bitValue = ((rawValue >> bitIndex) & 1u) == 1u;

            SetItemValueIfDifferent(bitItem, bitValue);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "kind", "Bool");
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "format", "bool");
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "title", label);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "text", label);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "module_name", definition.ModuleName);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "channel_name", definition.ChannelName);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "bit_index", bitIndex);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "source_path", runtimeChannel.Path ?? string.Empty);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "writable", writable);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "write_path", string.Empty);
            structureChanged |= SetPropertyValueIfDifferent(bitItem, "write_mode", string.Empty);
        }

        foreach (var staleBitName in bitsRoot.GetDictionary().Keys.Except(desiredBitNames, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            bitsRoot.Remove(staleBitName);
            structureChanged = true;
        }

        return structureChanged;
    }

    private ItemModel ResolveExposureWriteTargetChannel(ItemModel runtimeChannel, UdlModuleExposureDefinition definition)
    {
        if (!definition.RouteReadInputToSetRequest
            || !string.Equals(definition.ChannelName, "read", StringComparison.OrdinalIgnoreCase)
            || !TryResolveRuntimeChannel(new UdlModuleExposureDefinition { ModuleName = definition.ModuleName, ChannelName = "set" }, out var setChannel)
            || setChannel is null)
        {
            return runtimeChannel;
        }

        return setChannel;
    }

    private static object? ResolveExposureBitValueSourceValue(ItemModel runtimeChannel, UdlModuleExposureDefinition definition, ItemModel writeTargetChannel)
    {
        if (string.Equals(definition.ChannelName, "set", StringComparison.OrdinalIgnoreCase))
        {
            return GetChannelWriteValue(runtimeChannel);
        }

        if (definition.RouteReadInputToSetRequest && string.Equals(definition.ChannelName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return GetChannelWriteValue(writeTargetChannel);
        }

        return GetChannelReadValue(runtimeChannel);
    }

    private static string NormalizeRuntimeChannelName(string? channelName)
        => channelName?.Trim().ToLowerInvariant() ?? string.Empty;

    private static object? GetChannelReadValue(ItemModel runtimeChannel)
        => runtimeChannel.Properties.Has("read")
            ? runtimeChannel.Properties["read"].Value
            : null;

    private static object? GetChannelWriteValue(ItemModel runtimeChannel)
        => runtimeChannel.Properties.Has("write")
            ? runtimeChannel.Properties["write"].Value
            : null;

    private static void SetChannelWriteValue(ItemModel runtimeChannel, object? value)
    {
        if (runtimeChannel.Properties.Has("write"))
        {
            runtimeChannel.Properties["write"].Value = value!;
        }
    }

    private static int ResolveBitCount(UdlModuleExposureDefinition definition, ItemModel runtimeChannel)
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

        var runtimeFormat = runtimeChannel.Properties.Has("format")
            ? runtimeChannel.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        return GetBitCount(runtimeFormat);
    }

    private static bool RemoveRuntimeExposureBits(ItemModel runtimeChannel)
    {
        if (!runtimeChannel.Has("bits"))
        {
            return false;
        }

        runtimeChannel.Remove("bits");
        return true;
    }

    private static void RemoveLegacyExposureItems(FolderItemModel item)
    {
        HostRegistries.Data.Remove(GetLegacyExposureBasePath(item));
    }

    private static bool SetItemValueIfDifferent(ItemModel item, object? value)
    {
        if (ValuesEqual(item.Value, value))
        {
            return false;
        }

        item.Value = value!;
        return true;
    }

    private static bool SetPropertyValueIfDifferent(ItemModel item, string parameterName, object? value)
    {
        var parameter = item.Properties[parameterName];
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

    private static bool TryGetExposureBitMetadata(ItemModel item, out string moduleName, out string channelName, out int bitIndex)
    {
        moduleName = string.Empty;
        channelName = string.Empty;
        bitIndex = -1;

        if (!item.Properties.Has("module_name")
            || !item.Properties.Has("channel_name")
            || !item.Properties.Has("bit_index"))
        {
            return false;
        }

        moduleName = item.Properties["module_name"].Value?.ToString() ?? string.Empty;
        channelName = item.Properties["channel_name"].Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moduleName)
            || string.IsNullOrWhiteSpace(channelName)
            || !int.TryParse(item.Properties["bit_index"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitIndex))
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
        return string.IsNullOrWhiteSpace(normalized) ? "ItemModel" : normalized.Replace('.', '_');
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

    private static string SerializeAttachedPaths(IEnumerable<string> paths)
        => string.Join(Environment.NewLine, ParseAttachedPaths(string.Join(Environment.NewLine, paths ?? []))
            .OrderBy(static path => path.Count(static ch => ch == '.'))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase));

    private static string AddAttachedPath(string? serialized, string path)
    {
        var attachedPaths = ParseAttachedPaths(serialized);
        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            attachedPaths.Add(normalizedPath);
        }

        return SerializeAttachedPaths(attachedPaths);
    }

    private static string AddAttachedPaths(string? serialized, IEnumerable<string> paths)
    {
        var attachedPaths = ParseAttachedPaths(serialized);
        foreach (var path in paths ?? [])
        {
            var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                attachedPaths.Add(normalizedPath);
            }
        }

        return SerializeAttachedPaths(attachedPaths);
    }

    private static string RemoveAttachedPath(string? serialized, string path)
    {
        var attachedPaths = ParseAttachedPaths(serialized);
        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            attachedPaths.RemoveWhere(candidate => string.Equals(candidate, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        return SerializeAttachedPaths(attachedPaths);
    }

    private static string RemoveAttachedPaths(string? serialized, IEnumerable<string> paths)
    {
        var attachedPaths = ParseAttachedPaths(serialized);
        foreach (var path in paths ?? [])
        {
            var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                attachedPaths.RemoveWhere(candidate => string.Equals(candidate, normalizedPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        return SerializeAttachedPaths(attachedPaths);
    }

    private static string NormalizeClientName(FolderItemModel item)
        => UdlPathHelper.NormalizeClientName(item.Name);

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
        => UdlPathHelper.GetCanonicalStatusBasePath(item.FolderName, NormalizeClientName(item));

    private static string GetAttachOptionsBasePath(FolderItemModel item)
        => UdlPathHelper.GetCanonicalAttachOptionsBasePath(item.FolderName, NormalizeClientName(item));

    private static string GetLegacyExposureBasePath(FolderItemModel item)
        => $"studio.{item.FolderName}.UdlClientruntime.{NormalizeClientName(item)}.Modules";

    private static void RemovePublishedRuntimeItems(FolderItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        var runtimePrefixes = UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(item));
        var keys = HostRegistries.Data.GetAllKeys()
            .Where(key => runtimePrefixes.Any(prefix => string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var key in keys)
        {
            HostRegistries.Data.Remove(key);
        }
    }

    private static string GetRelativeRuntimePath(ItemModel item)
        => UdlPathHelper.GetRelativeRuntimePath(item.Path);
}

public partial class UdlClientWidget : UdlClientControl
{
}

/// <summary>
/// Represents one compact received or attached row shown in the UDL client body.
/// </summary>
public sealed class UdlClientAttachSectionRow : NotifyBase
{
    private readonly FolderItemModel _ownerItem;
    private readonly IBrush _statusBrush;
    private readonly string _actionText;
    private readonly bool _canExecuteAction;
    private readonly string _statusKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdlClientAttachSectionRow"/> class.
    /// </summary>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="relativePath">The relative runtime path.</param>
    /// <param name="summaryText">The tooltip summary text.</param>
    /// <param name="alertText">The tooltip alert text.</param>
    /// <param name="statusBrush">The traffic-light status brush.</param>
    /// <param name="actionText">The primary row action text.</param>
    /// <param name="canExecuteAction">Whether the primary row action is enabled.</param>
    /// <param name="moduleName">The resolved module name.</param>
    public UdlClientAttachSectionRow(
        FolderItemModel ownerItem,
        string relativePath,
        string summaryText,
        string alertText,
        IBrush statusBrush,
        string actionText = "",
        bool canExecuteAction = false,
        string moduleName = "")
    {
        _ownerItem = ownerItem ?? throw new ArgumentNullException(nameof(ownerItem));
        _statusBrush = statusBrush ?? throw new ArgumentNullException(nameof(statusBrush));
        RelativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativePath);
        ModuleName = moduleName?.Trim() ?? string.Empty;
        SummaryText = summaryText?.Trim() ?? string.Empty;
        AlertText = alertText?.Trim() ?? string.Empty;
        _actionText = actionText?.Trim() ?? string.Empty;
        _canExecuteAction = canExecuteAction;
        _statusKey = statusBrush.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets the relative runtime path.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the compact row path text.
    /// </summary>
    public string PathText => RelativePath;

    /// <summary>
    /// Gets the resolved module name for exposure editing.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Gets the tooltip summary text.
    /// </summary>
    public string SummaryText { get; }

    /// <summary>
    /// Gets the tooltip alert text.
    /// </summary>
    public string AlertText { get; }

    /// <summary>
    /// Gets the primary row action text.
    /// </summary>
    public string ActionText => _actionText;

    /// <summary>
    /// Gets a value indicating whether the primary row action can run.
    /// </summary>
    public bool CanExecuteAction => _canExecuteAction;

    /// <summary>
    /// Gets a value indicating whether the attached row can open the exposure editor.
    /// </summary>
    public bool CanEdit => !string.IsNullOrWhiteSpace(ModuleName);

    /// <summary>
    /// Gets a value indicating whether the attached row can be detached.
    /// </summary>
    public bool CanDetach => true;

    /// <summary>
    /// Determines whether this row matches another row's visible presentation state.
    /// </summary>
    /// <param name="other">The other row to compare.</param>
    /// <returns><see langword="true"/> when both rows present the same UI state; otherwise <see langword="false"/>.</returns>
    public bool HasSamePresentation(UdlClientAttachSectionRow other)
    {
        return other is not null
            && string.Equals(RelativePath, other.RelativePath, StringComparison.Ordinal)
            && string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal)
            && string.Equals(SummaryText, other.SummaryText, StringComparison.Ordinal)
            && string.Equals(AlertText, other.AlertText, StringComparison.Ordinal)
            && string.Equals(ActionText, other.ActionText, StringComparison.Ordinal)
            && CanExecuteAction == other.CanExecuteAction
            && CanEdit == other.CanEdit
            && CanDetach == other.CanDetach
            && string.Equals(_statusKey, other._statusKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the row tooltip text.
    /// </summary>
    public string ToolTipText => string.IsNullOrWhiteSpace(AlertText)
        ? SummaryText
        : $"{SummaryText}{Environment.NewLine}{AlertText}";

    /// <summary>
    /// Gets the row background brush.
    /// </summary>
    public IBrush RowBackground => Brushes.Transparent;

    /// <summary>
    /// Gets the row border brush.
    /// </summary>
    public IBrush RowBorderBrush => _ownerItem.EffectiveBodyBorderBrush;

    /// <summary>
    /// Gets the primary foreground brush.
    /// </summary>
    public IBrush PrimaryForeground => _ownerItem.EffectiveBodyForegroundBrush;

    /// <summary>
    /// Gets the traffic-light status brush.
    /// </summary>
    public IBrush StatusIndicatorBrush => _statusBrush;

    /// <summary>
    /// Raises property changed notifications for theme-dependent row values.
    /// </summary>
    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(RowBorderBrush));
        OnPropertyChanged(nameof(PrimaryForeground));
    }
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
