using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server.Mqtt;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using HornetStudio.Logging;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Displays and publishes remote MQTT ItemBroker items for the editor.
/// </summary>
public partial class ItemClientControl : EditorTemplateControl
{
    private const string MqttTransportSegment = "mqtt";
    private const string SharedBrokerRootSegment = "shared";
    private const string PublishDiagnosticsSwitchName = "HornetStudio.ItemClient.PublishDiagnostics";
    private static readonly string[] BrokerReconnectPropertyNames =
    [
        nameof(FolderItemModel.BrokerHost),
        nameof(FolderItemModel.BrokerPort),
        nameof(FolderItemModel.BrokerBaseTopic),
        nameof(FolderItemModel.ServerClientId),
        nameof(FolderItemModel.BrokerMode)
    ];
    private static readonly string[] BrokerRuntimeRefreshPropertyNames =
    [
        nameof(FolderItemModel.BrokerAttachedItemPaths),
        nameof(FolderItemModel.ItemExposures),
        nameof(FolderItemModel.Name),
        nameof(FolderItemModel.BrokerPublishedItemPaths)
    ];

    public static readonly DirectProperty<ItemClientControl, string> ConnectionStateTextProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, string>(nameof(ConnectionStateText), control => control.ConnectionStateText);

    public static readonly DirectProperty<ItemClientControl, string> EndpointTextProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, string>(nameof(EndpointText), control => control.EndpointText);

    public static readonly DirectProperty<ItemClientControl, string> ClientTextProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, string>(nameof(ClientText), control => control.ClientText);

    public static readonly DirectProperty<ItemClientControl, string> ItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, string>(nameof(ItemCountText), control => control.ItemCountText);

    public static readonly DirectProperty<ItemClientControl, string> ReceivedItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, string>(nameof(ReceivedItemCountText), control => control.ReceivedItemCountText);

    public static readonly DirectProperty<ItemClientControl, bool> HasNoAttachedItemsProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, bool>(nameof(HasNoAttachedItems), control => control.HasNoAttachedItems);

    public static readonly DirectProperty<ItemClientControl, bool> HasNoPublishedItemsProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, bool>(nameof(HasNoPublishedItems), control => control.HasNoPublishedItems);

    public static readonly DirectProperty<ItemClientControl, IBrush> ConnectionStatusBackgroundProperty =
        AvaloniaProperty.RegisterDirect<ItemClientControl, IBrush>(nameof(ConnectionStatusBackground), control => control.ConnectionStatusBackground);

    private IHostItemBrokerClient? _client;
    private FolderItemModel? _observedItem;
    private readonly HashSet<string> _publishedRuntimeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _publishedAttachOptionPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _runtimeUpdateTimer;
    private string _publishedAttachOptionsBasePath = string.Empty;
    private string _lastAttachDiagnosticsSignature = string.Empty;
    private string _lastAttachedItemRowsSignature = string.Empty;
    private string _lastPublishedItemRowsSignature = string.Empty;
    private IHostItemBrokerClient? _connectingClient;
    private OwnedBrokerRuntime? _connectingOwnedBrokerRuntime;
    private OwnedBrokerRuntime? _ownedServerRuntime;
    private HostItemBrokerPublisher? _hostItemPublisher;
    private HostItemBrokerWriteBackClient? _hostItemWriteBackClient;
    private readonly OwnWriteEchoTracker _ownWriteEchoTracker = new();
    private readonly LocalHostWriteTracker _localHostWriteTracker = new();
    private bool _isAttachedToVisualTree;
    private bool _isConnecting;
    private string _connectionStateText = "Disconnected";
    private string _endpointText = $"{ItemClientDefaults.Host}:{ItemClientDefaults.Port}";
    private string _clientText = $"BaseTopic {FormatBaseTopic(ItemClientDefaults.BaseTopic)} | MQTT Client {ItemClientDefaults.ClientIdDisplay}";
    private string _itemCountText = "0 items";
    private string _receivedItemCountText = "0";
    private bool _hasNoAttachedItems = true;
    private bool _hasNoPublishedItems = true;
    private IBrush _connectionStatusBackground = Brushes.Black;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemClientControl"/> class.
    /// </summary>
    public ItemClientControl()
    {
        _runtimeUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _runtimeUpdateTimer.Tick += OnRuntimeUpdateTimerTick;
        AttachedItems = [];
        AttachedItems.CollectionChanged += (_, _) => UpdateAttachedItemCollectionState();
        PublishedItems = [];
        PublishedItems.CollectionChanged += (_, _) => UpdatePublishedItemCollectionState();
        InitializeComponent();
        HeaderActionsContent = CreateHeaderActionsContent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Gets the current connection state text.
    /// </summary>
    public string ConnectionStateText
    {
        get => _connectionStateText;
        private set => SetAndRaise(ConnectionStateTextProperty, ref _connectionStateText, value);
    }

    /// <summary>
    /// Gets the endpoint text.
    /// </summary>
    public string EndpointText
    {
        get => _endpointText;
        private set => SetAndRaise(EndpointTextProperty, ref _endpointText, value);
    }

    /// <summary>
    /// Gets the client details text.
    /// </summary>
    public string ClientText
    {
        get => _clientText;
        private set => SetAndRaise(ClientTextProperty, ref _clientText, value);
    }

    /// <summary>
    /// Gets the item count text.
    /// </summary>
    public string ItemCountText
    {
        get => _itemCountText;
        private set => SetAndRaise(ItemCountTextProperty, ref _itemCountText, value);
    }

    /// <summary>
    /// Gets the received broker item count text.
    /// </summary>
    public string ReceivedItemCountText
    {
        get => _receivedItemCountText;
        private set => SetAndRaise(ReceivedItemCountTextProperty, ref _receivedItemCountText, value);
    }

    /// <summary>
    /// Gets the connection status background.
    /// </summary>
    public IBrush ConnectionStatusBackground
    {
        get => _connectionStatusBackground;
        private set => SetAndRaise(ConnectionStatusBackgroundProperty, ref _connectionStatusBackground, value);
    }

    /// <summary>
    /// Gets the attached broker item rows shown in the widget body.
    /// </summary>
    public ObservableCollection<BrokerAttachedItemRow> AttachedItems { get; }

    /// <summary>
    /// Gets the local publish root rows shown in the widget body.
    /// </summary>
    public ObservableCollection<BrokerPublishedRootRow> PublishedItems { get; }

    /// <summary>
    /// Gets a value indicating whether no broker items are attached.
    /// </summary>
    public bool HasNoAttachedItems
    {
        get => _hasNoAttachedItems;
        private set => SetAndRaise(HasNoAttachedItemsProperty, ref _hasNoAttachedItems, value);
    }

    /// <summary>
    /// Gets a value indicating whether no local roots are configured for publishing.
    /// </summary>
    public bool HasNoPublishedItems
    {
        get => _hasNoPublishedItems;
        private set => SetAndRaise(HasNoPublishedItemsProperty, ref _hasNoPublishedItems, value);
    }

    private FolderItemModel? ItemModel => DataContext as FolderItemModel;

    private Control CreateHeaderActionsContent()
    {
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(ConnectionStateText)) { Source = this });

        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            Child = statusText
        };
        statusBorder.Bind(Border.BackgroundProperty, new Binding(nameof(ConnectionStatusBackground)) { Source = this });

        var button = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Content = statusBorder
        };
        button.PointerPressed += static (_, e) => e.Handled = true;
        button.Click += OnToggleConnectionClicked;
        actionsPanel.Children.Add(button);
        return actionsPanel;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        HookObservedItem();
        RefreshPresentation();
        if (ItemModel?.BrokerAutoConnect == true)
        {
            ConnectInternal();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _runtimeUpdateTimer.Stop();
        UnhookObservedItem();
        _ = DisconnectInternalAsync();
        RemovePublishedRuntimeItems(ItemModel);
        RemovePublishedAttachOptionItems();
        _publishedRuntimeKeys.Clear();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        if (!CanUpdateUi())
        {
            return;
        }

        RefreshPresentation();
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, ItemModel))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = ItemModel;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
        }
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        }

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

        if (!CanUpdateUi())
        {
            return;
        }

        if (IsBrokerPresentationProperty(e.PropertyName))
        {
            if (IsBrokerReconnectProperty(e.PropertyName))
            {
                ReconnectIfAutoConnectEnabled();
            }
            else
            {
                PublishRuntimeItems();
                RestartHostItemPublisher(publishInitialSnapshots: e.PropertyName is not nameof(FolderItemModel.BrokerPublishedItemPaths));
            }

            RefreshPresentation();
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            RefreshBrokerRowThemes();
        }

        if (e.PropertyName == nameof(FolderItemModel.BrokerAutoConnect))
        {
            if (ItemModel?.BrokerAutoConnect == true)
            {
                ConnectInternal();
            }
            else
            {
                _ = DisconnectInternalAsync();
            }

            RefreshPresentation();
        }
    }

    private void OnToggleConnectionClicked(object? sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            ConnectInternal();
        }
        else
        {
            _ = DisconnectInternalAsync();
        }

        e.Handled = true;
    }

    private static bool IsBrokerPresentationProperty(string? propertyName)
        => IsBrokerReconnectProperty(propertyName)
            || BrokerRuntimeRefreshPropertyNames.Contains(propertyName, StringComparer.Ordinal);

    private static bool IsBrokerReconnectProperty(string? propertyName)
        => BrokerReconnectPropertyNames.Contains(propertyName, StringComparer.Ordinal);

    private async void ConnectInternal()
    {
        var item = ItemModel;
        if (item is null || _client is not null || _isConnecting || !CanUpdateUi())
        {
            return;
        }

        HostItemBrokerClient? client = null;
        OwnedBrokerRuntime? ownedBrokerRuntime = null;
        _isConnecting = true;
        try
        {
            if (string.Equals(item.BrokerMode, ItemClientModes.Own, StringComparison.OrdinalIgnoreCase))
            {
                ownedBrokerRuntime = new OwnedBrokerRuntime(item);
                _connectingOwnedBrokerRuntime = ownedBrokerRuntime;
                await ownedBrokerRuntime.StartAsync().ConfigureAwait(true);
                if (!ReferenceEquals(_connectingOwnedBrokerRuntime, ownedBrokerRuntime))
                {
                    await ownedBrokerRuntime.DisposeAsync().ConfigureAwait(true);
                    RefreshPresentation();
                    return;
                }
            }

            client = new HostItemBrokerClient(
                NormalizeWidgetName(item),
                item.BrokerHost,
                item.BrokerPort,
                item.BrokerBaseTopic,
                item.ServerClientId);
            client.ItemsChanged += OnClientItemsChanged;
            client.Diagnostic += OnClientDiagnostic;
            _connectingClient = client;
            await client.ConnectAsync().ConfigureAwait(true);
            if (!ReferenceEquals(_connectingClient, client))
            {
                client.ItemsChanged -= OnClientItemsChanged;
                client.Diagnostic -= OnClientDiagnostic;
                await client.DisposeAsync().ConfigureAwait(true);
                if (ownedBrokerRuntime is not null)
                {
                    await ownedBrokerRuntime.DisposeAsync().ConfigureAwait(true);
                }

                RefreshPresentation();
                return;
            }

            _connectingClient = null;
            _connectingOwnedBrokerRuntime = null;
            _client = client;
            _ownedServerRuntime = ownedBrokerRuntime;
            ConnectionStateText = "Connected";
            ConnectionStatusBackground = Brushes.ForestGreen;
            PublishRuntimeItems();
            StartHostItemPublisher(item, client);
            await StartHostItemWriteBackClientAsync(item, client).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
            }

            StopHostItemPublisher();
            await StopHostItemWriteBackClientAsync().ConfigureAwait(true);

            if (client is not null)
            {
                client.ItemsChanged -= OnClientItemsChanged;
                client.Diagnostic -= OnClientDiagnostic;
                try
                {
                    await client.DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception disposeException)
                {
                    HostLogger.Log.Warning(disposeException, "[ItemClientConnect] Failed to dispose broker client after connect failure.");
                }
            }

            if (ownedBrokerRuntime is not null)
            {
                try
                {
                    await ownedBrokerRuntime.DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception disposeException)
                {
                    HostLogger.Log.Warning(disposeException, "[ItemClientConnect] Failed to dispose owned broker after connect failure.");
                }
            }

            ConnectionStateText = "Failed";
            ConnectionStatusBackground = Brushes.Firebrick;
            HostLogger.Log.Warning(
                ex,
                "[ItemClientConnect] Widget={WidgetName} Mode={BrokerMode} Host={Host} Port={Port} BaseTopic={BaseTopic} ClientId={ClientId}",
                NormalizeWidgetName(item),
                item.BrokerMode,
                item.BrokerHost,
                item.BrokerPort,
                item.BrokerBaseTopic,
                item.ServerClientId);
        }
        finally
        {
            if (ReferenceEquals(_connectingClient, client))
            {
                _connectingClient = null;
            }

            if (ReferenceEquals(_connectingOwnedBrokerRuntime, ownedBrokerRuntime))
            {
                _connectingOwnedBrokerRuntime = null;
            }

            _isConnecting = false;
        }

        RefreshPresentation();
    }

    private async Task DisconnectInternalAsync()
    {
        _runtimeUpdateTimer.Stop();
        RemovePublishedRuntimeItems(ItemModel);
        RemovePublishedAttachOptionItems();
        _publishedRuntimeKeys.Clear();
        var connectingClient = _connectingClient;
        _connectingClient = null;
        var connectingOwnedBrokerRuntime = _connectingOwnedBrokerRuntime;
        _connectingOwnedBrokerRuntime = null;
        _isConnecting = false;
        var client = _client;
        _client = null;
        StopHostItemPublisher();
        await StopHostItemWriteBackClientAsync().ConfigureAwait(true);
        var ownedBrokerRuntime = _ownedServerRuntime;
        _ownedServerRuntime = null;
        if (connectingClient is not null)
        {
            connectingClient.ItemsChanged -= OnClientItemsChanged;
            connectingClient.Diagnostic -= OnClientDiagnostic;
            try
            {
                await connectingClient.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientDisconnect] Failed to dispose connecting broker client.");
            }
        }

        if (connectingOwnedBrokerRuntime is not null)
        {
            try
            {
                await connectingOwnedBrokerRuntime.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientDisconnect] Failed to dispose connecting owned broker.");
            }
        }

        if (client is not null)
        {
            client.ItemsChanged -= OnClientItemsChanged;
            client.Diagnostic -= OnClientDiagnostic;
            try
            {
                await client.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientDisconnect] Failed to dispose broker client.");
            }
        }

        if (ownedBrokerRuntime is not null)
        {
            try
            {
                await ownedBrokerRuntime.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientDisconnect] Failed to dispose owned broker.");
            }
        }

        if (!CanUpdateUi())
        {
            return;
        }

        ConnectionStateText = "Disconnected";
        ConnectionStatusBackground = Brushes.Black;
        RefreshPresentation();
    }

    private void ReconnectIfAutoConnectEnabled()
    {
        if (ItemModel?.BrokerAutoConnect != true)
        {
            _ = DisconnectInternalAsync();
            return;
        }

        _ = DisconnectInternalAsync();
        ConnectInternal();
    }

    private void RestartHostItemPublisher(bool publishInitialSnapshots = true)
    {
        var item = ItemModel;
        var client = _client;
        if (item is null || client is null)
        {
            StopHostItemPublisher();
            return;
        }

        StartHostItemPublisher(item, client, publishInitialSnapshots);
        RestartHostItemWriteBackClient();
    }

    private void StartHostItemPublisher(FolderItemModel item, IHostItemBrokerClient client, bool publishInitialSnapshots = true)
    {
        StopHostItemPublisher();
        _hostItemPublisher = new HostItemBrokerPublisher(item, client, _ownWriteEchoTracker.Record, _localHostWriteTracker.Record);
        _hostItemPublisher.Start(publishInitialSnapshots);
    }

    private void PublishRetainedSnapshotsForRoot(string localRootPath)
    {
        _hostItemPublisher?.PublishRetainedSnapshotsForRoot(localRootPath);
    }

    private void StopHostItemPublisher()
    {
        _hostItemPublisher?.Dispose();
        _hostItemPublisher = null;
    }

    private async void RestartHostItemWriteBackClient()
    {
        var item = ItemModel;
        var client = _client;
        await StopHostItemWriteBackClientAsync().ConfigureAwait(true);
        if (item is null || client is null)
        {
            return;
        }

        await StartHostItemWriteBackClientAsync(item, client).ConfigureAwait(true);
    }

    private async Task StartHostItemWriteBackClientAsync(FolderItemModel item, IHostItemBrokerClient client)
    {
        await StopHostItemWriteBackClientAsync().ConfigureAwait(true);
        var definitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(item.BrokerPublishedItemPaths);
        var writeBackClient = new HostItemBrokerWriteBackClient(client, definitions, _ownWriteEchoTracker.TryConsume, _localHostWriteTracker.HasRecentConflict);
        if (writeBackClient.WritablePathCount == 0)
        {
            await writeBackClient.DisposeAsync().ConfigureAwait(true);
            return;
        }

        _hostItemWriteBackClient = writeBackClient;
        try
        {
            await writeBackClient.StartAsync().ConfigureAwait(true);
        }
        catch
        {
            if (ReferenceEquals(_hostItemWriteBackClient, writeBackClient))
            {
                _hostItemWriteBackClient = null;
            }

            await writeBackClient.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    private async Task StopHostItemWriteBackClientAsync()
    {
        var writeBackClient = _hostItemWriteBackClient;
        _hostItemWriteBackClient = null;
        if (writeBackClient is not null)
        {
            await writeBackClient.DisposeAsync().ConfigureAwait(true);
        }
    }

    private void OnClientItemsChanged()
    {
        if (!CanUpdateUi())
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleRuntimeUpdate);
            return;
        }

        ScheduleRuntimeUpdate();
    }

    private void OnClientDiagnostic(string message)
    {
        HostLogger.Log.Debug("{Message}", message);
    }

    private void ScheduleRuntimeUpdate()
    {
        if (!CanUpdateUi() || _runtimeUpdateTimer.IsEnabled)
        {
            return;
        }

        _runtimeUpdateTimer.Start();
    }

    private void OnRuntimeUpdateTimerTick(object? sender, EventArgs e)
    {
        _runtimeUpdateTimer.Stop();
        if (!CanUpdateUi())
        {
            return;
        }

        PublishRuntimeItems();
        RefreshPresentation();
    }

    private void PublishRuntimeItems()
    {
        if (!CanUpdateUi())
        {
            return;
        }

        if (ItemModel is null || _client is null)
        {
            RemoveStaleRuntimeItems(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            RemovePublishedAttachOptionItems();
            RebuildAttachedItemRows();
            return;
        }

        var widgetName = NormalizeWidgetName(ItemModel);
        var attachOptions = EnumerateAttachOptions(widgetName).ToArray();
        LogAttachDiagnostics(widgetName, attachOptions);
        PublishAttachOptionItems(ItemModel, attachOptions);
        var attachedPaths = ParseAttachedFlatPaths(ItemModel.BrokerAttachedItemPaths);
        var exposureDefinitions = ItemExposureDefinitionCodec.ParseDefinitions(ItemModel.ItemExposures);
        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RemoveLegacyRuntimeItems(ItemModel);
        foreach (var entry in _client.GetItemSnapshots())
        {
            foreach (var remoteItem in EnumerateAttachableRemoteItems(entry.Value))
            {
                var relativePath = GetRelativeRemoteItemPath(entry.Value, remoteItem);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var attachIdentity = BuildBrokerAttachIdentity(widgetName, ItemModel.BrokerBaseTopic, entry.Key, relativePath);
                if (!attachedPaths.Contains(attachIdentity))
                {
                    continue;
                }

                var key = BuildReceivedMqttRuntimePath(ItemModel, entry.Key, relativePath);
                var snapshot = ItemExtension.CloneWithPath(remoteItem, key);
                var exposureDefinition = ItemExposurePublisher.FindByItemPath(exposureDefinitions, attachIdentity)
                                         ?? ItemExposurePublisher.FindByItemPath(exposureDefinitions, BuildLegacyBrokerAttachIdentity(widgetName, entry.Key, relativePath))
                                         ?? ItemExposurePublisher.FindByItemPath(exposureDefinitions, relativePath);
                if (exposureDefinition is not null)
                {
                    ItemExposurePublisher.Apply(snapshot, exposureDefinition);
                }

                HostRegistries.Data.UpsertSnapshot(key, snapshot, DataRegistryItemMetadata.BrokerReceivedData(), pruneMissingMembers: true);
                currentKeys.Add(key);
            }
        }

        RemoveStaleRuntimeItems(currentKeys);

        foreach (var key in currentKeys)
        {
            _publishedRuntimeKeys.Add(key);
        }

        RebuildAttachedItemRows();
    }

    private void RefreshPresentation()
    {
        if (!CanUpdateUi())
        {
            return;
        }

        var item = ItemModel;
        if (item is null)
        {
            return;
        }

        EndpointText = $"{item.BrokerHost}:{item.BrokerPort}";
        ClientText = $"BaseTopic {FormatBaseTopic(item.BrokerBaseTopic)} | MQTT Client {item.ServerClientId}";
        var attachOptionCount = EnumerateAttachOptions(NormalizeWidgetName(item)).Count();
        ItemCountText = $"{attachOptionCount} items";
        ReceivedItemCountText = attachOptionCount.ToString(CultureInfo.InvariantCulture);
        RebuildAttachedItemRows();
        RebuildPublishedItemRows();
    }

    private void UpdateAttachedItemCollectionState()
    {
        HasNoAttachedItems = AttachedItems.Count == 0;
    }

    private void UpdatePublishedItemCollectionState()
    {
        HasNoPublishedItems = PublishedItems.Count == 0;
    }

    private void RefreshBrokerRowThemes()
    {
        foreach (var row in AttachedItems)
        {
            row.RefreshTheme();
        }

        foreach (var row in PublishedItems)
        {
            row.RefreshTheme();
        }
    }

    private void RebuildAttachedItemRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildAttachedItemRows);
            return;
        }

        if (!CanUpdateUi())
        {
            return;
        }

        var item = ItemModel;
        if (item is null)
        {
            AttachedItems.Clear();
            PublishedItems.Clear();
            _lastAttachedItemRowsSignature = string.Empty;
            _lastPublishedItemRowsSignature = string.Empty;
            UpdateAttachedItemCollectionState();
            UpdatePublishedItemCollectionState();
            return;
        }

        var widgetName = NormalizeWidgetName(item);
        var attachedPaths = ParseAttachedFlatPaths(item.BrokerAttachedItemPaths)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var livePaths = EnumerateAttachOptions(widgetName)
            .Select(static path => TargetPathHelper.ToFlatItemServerPath(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = ItemExposureDefinitionCodec.ParseDefinitions(item.ItemExposures);
        var signature = BuildAttachedRowsSignature(attachedPaths, livePaths, definitions);
        if (string.Equals(_lastAttachedItemRowsSignature, signature, StringComparison.Ordinal))
        {
            UpdateAttachedItemCollectionState();
            return;
        }

        _lastAttachedItemRowsSignature = signature;
        AttachedItems.Clear();
        foreach (var path in attachedPaths)
        {
            var definition = FindExposureDefinition(definitions, path);
            var displayParts = GetBrokerDisplayParts(path);
            var helperCount = ResolveConfiguredHelperCount(definition);
            var isLive = _client is not null && livePaths.Contains(path);
            AttachedItems.Add(new BrokerAttachedItemRow(
                item,
                path,
                string.IsNullOrWhiteSpace(displayParts.Name) ? path : displayParts.Name,
                BuildAttachedItemSummary(isLive, helperCount, definition),
                isLive ? string.Empty : "Saved attachment is not currently live.",
                isLive));
        }

        UpdateAttachedItemCollectionState();
    }

    private void RebuildPublishedItemRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildPublishedItemRows);
            return;
        }

        if (!CanUpdateUi())
        {
            return;
        }

        var item = ItemModel;
        if (item is null)
        {
            PublishedItems.Clear();
            _lastPublishedItemRowsSignature = string.Empty;
            UpdatePublishedItemCollectionState();
            return;
        }

        var definitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(item.BrokerPublishedItemPaths);
        var roots = definitions
            .GroupBy(static definition => definition.LocalRootPath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new
            {
                LocalRootPath = group.Key,
                DefinitionCount = group.Count(),
                ActiveCount = group.Count(static definition => definition.Active),
                Existing = HostRegistries.Data.TryResolve(group.Key, out var localItem) && localItem is not null
            })
            .Where(static root => !string.IsNullOrWhiteSpace(root.LocalRootPath))
            .OrderBy(static root => root.LocalRootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var signature = string.Join("|", roots.Select(static root => string.Join("~", root.LocalRootPath, root.DefinitionCount, root.ActiveCount, root.Existing)));
        if (string.Equals(_lastPublishedItemRowsSignature, signature, StringComparison.Ordinal))
        {
            UpdatePublishedItemCollectionState();
            return;
        }

        _lastPublishedItemRowsSignature = signature;
        PublishedItems.Clear();
        foreach (var root in roots)
        {
            var displayParts = GetBrokerPublishDisplayParts(root.LocalRootPath);
            PublishedItems.Add(new BrokerPublishedRootRow(
                item,
                root.LocalRootPath,
                string.IsNullOrWhiteSpace(displayParts.Name) ? root.LocalRootPath : displayParts.Name,
                BuildPublishedRootSummary(root.ActiveCount, root.DefinitionCount),
                root.Existing ? string.Empty : "Selected local root is not currently available.",
                root.ActiveCount > 0,
                root.Existing));
        }

        UpdatePublishedItemCollectionState();
    }

    private static string BuildPublishedRootSummary(int activeCount, int definitionCount)
    {
        var activeText = activeCount == 1 ? "1 active entry" : $"{activeCount.ToString(CultureInfo.InvariantCulture)} active entries";
        var totalText = definitionCount == 1 ? "1 configured entry" : $"{definitionCount.ToString(CultureInfo.InvariantCulture)} configured entries";
        return $"{activeText} | {totalText}";
    }

    private static string BuildAttachedRowsSignature(
        IEnumerable<string> attachedPaths,
        IEnumerable<string> livePaths,
        IReadOnlyList<ItemExposureDefinition> definitions)
        => string.Join(
            "|",
            attachedPaths)
           + "||"
           + string.Join("|", livePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
           + "||"
           + string.Join(
               "|",
               definitions
                   .OrderBy(static definition => definition.ItemPath, StringComparer.OrdinalIgnoreCase)
                   .Select(static definition => string.Join("~", definition.ItemPath, definition.Format, definition.Unit, definition.ExposeBits, definition.BitCount, definition.BitLabels)));

    private static string BuildAttachedItemSummary(bool isLive, int helperCount, ItemExposureDefinition? definition)
    {
        var runtimeText = isLive ? "1 runtime item" : "saved attachment";
        var helperText = helperCount == 0
            ? "no helper items configured"
            : helperCount == 1
                ? "1 helper item configured"
                : $"{helperCount.ToString(CultureInfo.InvariantCulture)} helper items configured";
        var metadataText = definition is null || (string.IsNullOrWhiteSpace(definition.Format) && string.IsNullOrWhiteSpace(definition.Unit))
            ? string.Empty
            : " | metadata configured";
        return $"{runtimeText} | {helperText}{metadataText}";
    }

    private static int ResolveConfiguredHelperCount(ItemExposureDefinition? definition)
    {
        if (definition?.ExposeBits != true)
        {
            return 0;
        }

        if (definition.BitCount > 0)
        {
            return Math.Clamp(definition.BitCount, 1, 32);
        }

        var format = string.IsNullOrWhiteSpace(definition.Format)
            ? string.Empty
            : definition.Format.Trim().Split(':', 2, StringSplitOptions.TrimEntries)[0].ToLowerInvariant();
        return format switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private static ItemExposureDefinition? FindExposureDefinition(IReadOnlyList<ItemExposureDefinition> definitions, string attachedPath)
    {
        var flatPath = TargetPathHelper.ToFlatItemServerPath(attachedPath);
        return ItemExposurePublisher.FindByItemPath(definitions, flatPath)
               ?? ItemExposurePublisher.FindByItemPath(definitions, TargetPathHelper.ToRelativeItemServerPath(flatPath));
    }

    private static string NormalizeWidgetName(FolderItemModel item)
        => TargetPathHelper.GetCanonicalItemClientName(item.Name);

    private static IEnumerable<ItemModel> EnumerateAttachableRemoteItems(ItemModel root)
    {
        foreach (var child in root.GetDictionary().Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var item in EnumerateAttachableRemoteItems(child))
            {
                yield return item;
            }
        }

        if (HasAttachableValue(root))
        {
            yield return root;
        }
    }

    private static bool HasAttachableValue(ItemModel item)
    {
        if (item.Value is not null)
        {
            return true;
        }

        if (!item.Properties.GetDictionary().TryGetValue("Value", out var parameter))
        {
            return false;
        }

        object? value = parameter.Value;
        return value is not null;
    }

    private static string GetRelativeRemoteItemPath(ItemModel clientRoot, ItemModel remoteItem)
    {
        var rootPath = clientRoot.Path?.Trim() ?? string.Empty;
        var itemPath = remoteItem.Path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(itemPath))
        {
            return string.Empty;
        }

        if (itemPath.Length <= rootPath.Length || !itemPath.StartsWith(rootPath + ".", StringComparison.OrdinalIgnoreCase))
        {
            return itemPath.Trim('.');
        }

        return itemPath[(rootPath.Length + 1)..].Trim('.');
    }

    private static string BuildReceivedMqttRuntimePath(FolderItemModel item, string remoteRootKey, string relativeRemoteItemPath)
    {
        var widgetName = NormalizeWidgetName(item);
        var remotePath = BuildVisibleRemoteMqttSuffix(widgetName, item.BrokerBaseTopic, remoteRootKey, relativeRemoteItemPath);
        var folderPath = TargetPathHelper.NormalizeConfiguredTargetPath(item.FolderName);
        return string.IsNullOrWhiteSpace(remotePath)
            ? $"studio.{folderPath}.{widgetName}"
            : $"studio.{folderPath}.{widgetName}.{remotePath}";
    }

    private static string BuildBrokerAttachIdentity(string widgetName, string baseTopic, string remoteRootKey, string relativeRemoteItemPath)
    {
        var remotePath = BuildVisibleRemoteMqttSuffix(widgetName, baseTopic, remoteRootKey, relativeRemoteItemPath);
        return string.IsNullOrWhiteSpace(remotePath)
            ? widgetName
            : $"{widgetName}.{remotePath}";
    }

    private static string BuildLegacyBrokerAttachIdentity(string widgetName, string remoteRootKey, string relativeRemoteItemPath)
    {
        var rootKey = TargetPathHelper.NormalizeConfiguredTargetPath(remoteRootKey);
        var relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativeRemoteItemPath);
        return string.IsNullOrWhiteSpace(relativePath)
            ? $"{widgetName}.{rootKey}"
            : $"{widgetName}.{rootKey}.{relativePath}";
    }

    private static string BuildVisibleRemoteMqttPath(string baseTopic, string remoteRootKey, string relativeRemoteItemPath)
    {
        var basePath = TargetPathHelper.NormalizeConfiguredTargetPath(baseTopic);
        var rootKey = TargetPathHelper.NormalizeConfiguredTargetPath(remoteRootKey);
        var relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativeRemoteItemPath);
        if (!string.IsNullOrWhiteSpace(basePath) && !RelativePathStartsWithRoot(relativePath, basePath))
        {
            relativePath = string.IsNullOrWhiteSpace(relativePath)
                ? basePath
                : $"{basePath}.{relativePath}";
        }

        if (string.Equals(rootKey, SharedBrokerRootSegment, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(rootKey)
            || RelativePathStartsWithRoot(relativePath, rootKey))
        {
            return relativePath;
        }

        return string.IsNullOrWhiteSpace(relativePath)
            ? rootKey
            : $"{rootKey}.{relativePath}";
    }

    private static string BuildVisibleRemoteMqttSuffix(string widgetName, string baseTopic, string remoteRootKey, string relativeRemoteItemPath)
    {
        var remotePath = BuildVisibleRemoteMqttPath(baseTopic, remoteRootKey, relativeRemoteItemPath);
        var segments = TargetPathHelper.SplitPathSegments(remotePath);
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], widgetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], MqttTransportSegment, StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Skip(index + 2));
            }
        }

        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!string.Equals(segments[index], widgetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffixSegments = segments.Skip(index + 1).ToArray();
            if (index == 0 || SegmentsEqual(segments.Take(index), suffixSegments))
            {
                return string.Join('.', suffixSegments);
            }
        }

        return remotePath;
    }

    private static bool SegmentsEqual(IEnumerable<string> left, IReadOnlyList<string> right)
    {
        var leftArray = left.ToArray();
        if (leftArray.Length != right.Count)
        {
            return false;
        }

        for (var index = 0; index < leftArray.Length; index++)
        {
            if (!string.Equals(leftArray[index], right[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RelativePathStartsWithRoot(string relativePath, string rootKey)
    {
        var relativeSegments = TargetPathHelper.SplitPathSegments(relativePath);
        var rootSegments = TargetPathHelper.SplitPathSegments(rootKey);
        if (relativeSegments.Count < rootSegments.Count || rootSegments.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < rootSegments.Count; index++)
        {
            if (!string.Equals(relativeSegments[index], rootSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<string> EnumerateAttachOptions(string widgetName)
    {
        if (_client is null)
        {
            return [];
        }

        return EnumerateAttachOptions(widgetName, _client.BaseTopic, _client.GetItemSnapshots());
    }

    private static IEnumerable<string> EnumerateAttachOptions(string widgetName, string baseTopic, IReadOnlyDictionary<string, ItemModel> snapshots)
    {
        return snapshots
            .SelectMany(entry =>
            {
                return EnumerateAttachableRemoteItems(entry.Value)
                    .Select(remoteItem => GetRelativeRemoteItemPath(entry.Value, remoteItem))
                    .Where(static relativePath => !string.IsNullOrWhiteSpace(relativePath))
                    .Select(relativePath => BuildBrokerAttachIdentity(widgetName, baseTopic, entry.Key, relativePath));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void LogAttachDiagnostics(string widgetName, IReadOnlyList<string> attachOptions)
    {
        if (_client is null)
        {
            return;
        }

        var roots = _client.GetItemSnapshots();
        var receivedRoots = _client.GetReceivedItemRootSnapshots();
        var leafCount = roots.Values.Sum(root => EnumerateAttachableRemoteItems(root).Count());
        var receivedLeafCount = receivedRoots.Values.Sum(root => EnumerateAttachableRemoteItems(root).Count());
        var sampleReceivedRoots = string.Join(", ", receivedRoots.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).Take(4));
        var sampleRoots = string.Join(", ", roots.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).Take(4));
        var sampleLeaves = string.Join(", ", roots.Values
            .SelectMany(EnumerateAttachableRemoteItems)
            .Select(static item => item.Path ?? item.Name ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Take(4));
        var sampleOptions = string.Join(", ", attachOptions.Take(4));
        var signature = $"{receivedRoots.Count}|{roots.Count}|{receivedLeafCount}|{leafCount}|{attachOptions.Count}|{sampleReceivedRoots}|{sampleRoots}|{sampleLeaves}|{sampleOptions}";
        if (string.Equals(signature, _lastAttachDiagnosticsSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastAttachDiagnosticsSignature = signature;
        if (roots.Count > 0 && attachOptions.Count == 0)
        {
            HostLogger.Log.Warning(
                "[ItemClientReceive] widget={WidgetName} receivedRoots={ReceivedRoots} visibleRoots={VisibleRoots} receivedLeaves={ReceivedLeaves} visibleLeaves={VisibleLeaves} attachOptions={AttachOptions} received={Received} visible={Visible} leaves={Leaves} options={Options}",
                widgetName,
                receivedRoots.Count,
                roots.Count,
                receivedLeafCount,
                leafCount,
                attachOptions.Count,
                sampleReceivedRoots,
                sampleRoots,
                sampleLeaves,
                sampleOptions);
        }
        else
        {
            HostLogger.Log.Debug(
                "[ItemClientReceive] widget={WidgetName} receivedRoots={ReceivedRoots} visibleRoots={VisibleRoots} receivedLeaves={ReceivedLeaves} visibleLeaves={VisibleLeaves} attachOptions={AttachOptions} received={Received} visible={Visible} leaves={Leaves} options={Options}",
                widgetName,
                receivedRoots.Count,
                roots.Count,
                receivedLeafCount,
                leafCount,
                attachOptions.Count,
                sampleReceivedRoots,
                sampleRoots,
                sampleLeaves,
                sampleOptions);
        }
    }

    private void PublishAttachOptionItems(FolderItemModel item, IReadOnlyCollection<string> attachOptions)
    {
        var attachOptionsBasePath = GetAttachOptionsBasePath(item);
        if (!string.Equals(_publishedAttachOptionsBasePath, attachOptionsBasePath, StringComparison.OrdinalIgnoreCase))
        {
            RemovePublishedAttachOptionItems();
            _publishedAttachOptionsBasePath = attachOptionsBasePath;
        }

        var desiredSnapshots = attachOptions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(option =>
            {
                var snapshot = ItemExtension.CreateWithPath(BuildAttachOptionPath(attachOptionsBasePath, option));
                snapshot.Properties["kind"].Value = "Status";
                snapshot.Properties["text"].Value = "AttachOption";
                snapshot.Properties["title"].Value = option;
                snapshot.Properties["read"].Value = option;
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

    private static string BuildAttachOptionPath(string attachOptionsBasePath, string option)
    {
        var basePath = TargetPathHelper.NormalizeConfiguredTargetPath(attachOptionsBasePath);
        var optionPath = TargetPathHelper.NormalizeConfiguredTargetPath(option);
        return string.IsNullOrWhiteSpace(optionPath)
            ? basePath
            : $"{basePath}.{optionPath}";
    }

    private void RemoveStaleRuntimeItems(IReadOnlySet<string> currentKeys)
    {
        foreach (var staleKey in _publishedRuntimeKeys.Where(key => !currentKeys.Contains(key)).ToArray())
        {
            HostRegistries.Data.Remove(staleKey);
            _publishedRuntimeKeys.Remove(staleKey);
        }
    }

    private static HashSet<string> ParseAttachedFlatPaths(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        return serialized
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static path => TargetPathHelper.ToBrokerReceivedAttachIdentity(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string SerializeAttachedFlatPaths(IEnumerable<string> paths)
        => string.Join(Environment.NewLine, paths
            .Select(static path => TargetPathHelper.ToBrokerReceivedAttachIdentity(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));

    private static string RemoveExposureDefinition(string rawDefinitions, string attachedPath)
        => ItemExposureDefinitionCodec.RemoveDefinition(rawDefinitions, attachedPath);

    private static (string Name, string Source) GetBrokerDisplayParts(string fullPath)
    {
        fullPath = TargetPathHelper.ToBrokerReceivedAttachIdentity(fullPath);
        var segments = TargetPathHelper.SplitPathSegments(fullPath)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (segments[0], string.Empty),
            2 => (segments[1], segments[0]),
            _ => (string.Join('.', segments.Skip(2)), $"{segments[0]} -> {segments[1]}")
        };
    }

    private static (string Name, string Source) GetBrokerPublishDisplayParts(string path)
    {
        path = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var segments = TargetPathHelper.SplitPathSegments(path)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (segments[0], string.Empty),
            2 => (segments[1], segments[0]),
            _ when string.Equals(segments[0], "Studio", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(segments[0], "Project", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(segments[0], "UdlProject", StringComparison.OrdinalIgnoreCase)
                => (string.Join('.', segments.Skip(2)), $"{segments[0]} -> {segments[1]}"),
            _ => (segments[^1], string.Join('.', segments.Take(segments.Length - 1)))
        };
    }

    private async void OnEditAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: BrokerAttachedItemRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: HornetStudio.Editor.ViewModels.MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await ItemExposureDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: ItemModel.ItemExposures,
            itemPath: row.ItemPath);
        if (result is null)
        {
            return;
        }

        ItemModel.ItemExposures = result;
        PublishRuntimeItems();
        RebuildAttachedItemRows();
        e.Handled = true;
    }

    private async void OnHeaderAttachClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetDialogContext(out var item, out var viewModel, out var owner))
        {
            return;
        }

        var field = CreateBrokerAttachEditorField(item);
        var dialog = new AttachItemsEditorDialogWindow(viewModel, field);
        await dialog.ShowDialog(owner);
        field.Definition.Apply(item, field.Value);
        PublishRuntimeItems();
        RebuildAttachedItemRows();
        e.Handled = true;
    }

    private async void OnDeleteAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: BrokerAttachedItemRow row }
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(
            owner,
            $"Delete broker item '{row.DisplayName}'?",
            "The attached item and its exposure definition will be removed.",
            confirmText: "Delete",
            cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var remainingPaths = ParseAttachedFlatPaths(ItemModel.BrokerAttachedItemPaths)
            .Where(path => !string.Equals(path, row.ItemPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ItemModel.BrokerAttachedItemPaths = SerializeAttachedFlatPaths(remainingPaths);
        ItemModel.ItemExposures = RemoveExposureDefinition(ItemModel.ItemExposures, row.ItemPath);
        PublishRuntimeItems();
        RebuildAttachedItemRows();
        e.Handled = true;
    }

    private async void OnEditPublishedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: BrokerPublishedRootRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: HornetStudio.Editor.ViewModels.MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await PublishedItemDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: ItemModel.BrokerPublishedItemPaths,
            rootPath: row.LocalRootPath);
        if (result is null)
        {
            return;
        }

        ItemModel.BrokerPublishedItemPaths = result;
        PublishRetainedSnapshotsForRoot(row.LocalRootPath);
        RebuildPublishedItemRows();
        e.Handled = true;
    }

    private async void OnHeaderPublishClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetDialogContext(out var item, out var viewModel, out var owner))
        {
            return;
        }

        var field = CreateBrokerPublishEditorField(item);
        var dialog = new AttachItemsEditorDialogWindow(viewModel, field);
        await dialog.ShowDialog(owner);
        field.Definition.Apply(item, field.Value);
        RestartHostItemPublisher(publishInitialSnapshots: false);
        RebuildPublishedItemRows();
        e.Handled = true;
    }

    private async void OnDeletePublishedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null
            || sender is not Button { CommandParameter: BrokerPublishedRootRow row }
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(
            owner,
            $"Remove published root '{row.DisplayName}'?",
            "The local registry item will not be changed.",
            confirmText: "Remove",
            cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var remainingDefinitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(ItemModel.BrokerPublishedItemPaths)
            .Where(definition => !string.Equals(definition.LocalRootPath, row.LocalRootPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ItemModel.BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(remainingDefinitions);
        RebuildPublishedItemRows();
        e.Handled = true;
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

    private static string GetAttachOptionsBasePath(FolderItemModel item)
        => TargetPathHelper.GetCanonicalBrokerAttachOptionsBasePath(item.FolderName, item.Name);

    private bool TryGetDialogContext(
        out FolderItemModel item,
        out HornetStudio.Editor.ViewModels.MainWindowViewModel viewModel,
        out Window owner)
    {
        item = ItemModel!;
        viewModel = null!;
        owner = null!;

        if (ItemModel is null
            || TopLevel.GetTopLevel(this) is not Window { DataContext: HornetStudio.Editor.ViewModels.MainWindowViewModel mainWindowViewModel } window)
        {
            return false;
        }

        item = ItemModel;
        viewModel = mainWindowViewModel;
        owner = window;
        return true;
    }

    private static HornetStudio.Editor.ViewModels.EditorDialogField CreateBrokerAttachEditorField(FolderItemModel item)
    {
        var definition = new HornetStudio.Editor.ViewModels.EditorDialogBindingDefinition(
            key: "BrokerAttachedItemPaths",
            label: "AttachToUi",
            propertyType: HornetStudio.Editor.ViewModels.EditorPropertyType.AttachItemList,
            readValue: static current => current.BrokerAttachedItemPaths,
            applyValue: static (current, value) =>
            {
                current.BrokerAttachedItemPaths = value;
                return null;
            },
            optionsFactory: GetBrokerAttachItemOptions);
        return definition.CreateField(item);
    }

    private static HornetStudio.Editor.ViewModels.EditorDialogField CreateBrokerPublishEditorField(FolderItemModel item)
    {
        var definition = new HornetStudio.Editor.ViewModels.EditorDialogBindingDefinition(
            key: "BrokerPublishedItemPaths",
            label: "PublishItems",
            propertyType: HornetStudio.Editor.ViewModels.EditorPropertyType.AttachItemList,
            readValue: static current => current.BrokerPublishedItemPaths,
            applyValue: static (current, value) =>
            {
                current.BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(BrokerPublishedItemDefinitionCodec.ParseDefinitions(value));
                return null;
            },
            optionsFactory: GetBrokerPublishItemOptions);
        return definition.CreateField(item);
    }

    private static IEnumerable<string> GetBrokerPublishItemOptions(FolderItemModel item)
        => HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.BrokerPublish)
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> GetBrokerAttachItemOptions(FolderItemModel item)
    {
        var prefixes = TargetPathHelper.GetBrokerAttachOptionPrefixes(item.FolderName, item.Name);

        return HostRegistries.Data.GetAllKeys()
            .SelectMany(key => prefixes.Select(prefix => TryGetBrokerAttachRuntimePath(key, prefix)))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static string? TryGetBrokerAttachRuntimePath(string registryKey, string prefix)
    {
        var suffix = TryGetPathSuffix(registryKey, prefix);
        return ExtractBrokerAttachPath(suffix);
    }

    private static string? ExtractBrokerAttachPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var markerIndex = normalizedPath.IndexOf("runtime.item_broker.", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var runtimePath = normalizedPath[markerIndex..];
        var segments = TargetPathHelper.SplitPathSegments(runtimePath);
        if (segments.Count < 4)
        {
            return null;
        }

        var brokerPath = string.Join('.', segments.Skip(3));
        return TargetPathHelper.ToBrokerReceivedAttachIdentity(brokerPath);
    }

    private static void RemovePublishedRuntimeItems(FolderItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        RemoveKeysBelowPrefix($"runtime.item_broker.{NormalizeWidgetName(item)}");
        RemoveKeysBelowPrefix($"studio.{TargetPathHelper.NormalizeConfiguredTargetPath(item.FolderName)}.{NormalizeWidgetName(item)}");
    }

    private static void RemoveLegacyRuntimeItems(FolderItemModel item)
    {
        RemoveKeysBelowPrefix($"runtime.item_broker.{NormalizeWidgetName(item)}");
    }

    private static void RemoveKeysBelowPrefix(string prefix)
    {
        foreach (var key in HostRegistries.Data.GetAllKeys()
            .Where(key => string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            HostRegistries.Data.Remove(key);
        }
    }

    private sealed class HostItemBrokerPublisher : IDisposable
    {
        private readonly FolderItemModel _item;
        private readonly IHostItemBrokerClient _client;
        private readonly Action<string, string, object?> _recordOwnWriteCommand;
        private readonly Action<string, string, object?> _recordLocalHostWriteState;
        private readonly Dictionary<int, DispatcherTimer> _intervalTimers = new();
        private readonly object _registrationSync = new();
        private readonly HashSet<string> _registeredBrokerPaths = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<BrokerPublishedItemDefinition> _definitions = [];
        private bool _disposed;

        public HostItemBrokerPublisher(FolderItemModel item, IHostItemBrokerClient client, Action<string, string, object?> recordOwnWriteCommand)
            : this(item, client, recordOwnWriteCommand, static (_, _, _) =>
            {
            })
        {
        }

        public HostItemBrokerPublisher(
            FolderItemModel item,
            IHostItemBrokerClient client,
            Action<string, string, object?> recordOwnWriteCommand,
            Action<string, string, object?> recordLocalHostWriteState)
        {
            _item = item;
            _client = client;
            _recordOwnWriteCommand = recordOwnWriteCommand;
            _recordLocalHostWriteState = recordLocalHostWriteState;
        }

        public void Start(bool publishInitialSnapshots = true)
        {
            _definitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(_item.BrokerPublishedItemPaths);
            _definitions = _definitions.Where(static definition => definition.Active).ToArray();
            if (_definitions.Count == 0)
            {
                return;
            }

            HostRegistries.Data.ItemChanged += OnDataItemChanged;
            if (publishInitialSnapshots)
            {
                PublishInitialSnapshots();
            }

            StartIntervalTimers();
        }

        public void PublishRetainedSnapshotsForRoot(string localRootPath)
        {
            foreach (var definition in BrokerPublishedItemDefinitionCodec.GetActiveDefinitionsForRoot(_definitions, localRootPath))
            {
                PublishDefinitionIfAvailable(definition, PublishIntent.Snapshot);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HostRegistries.Data.ItemChanged -= OnDataItemChanged;
            foreach (var timer in _intervalTimers.Values)
            {
                timer.Stop();
                timer.Tick -= OnIntervalTimerTick;
            }

            _intervalTimers.Clear();
        }

        private void PublishInitialSnapshots()
        {
            foreach (var definition in _definitions)
            {
                PublishDefinitionIfAvailable(definition, PublishIntent.Snapshot);
            }
        }

        private void StartIntervalTimers()
        {
            foreach (var interval in _definitions
                .Where(static definition => string.Equals(definition.PublishMode, BrokerPublishedItemPublishModes.Interval, StringComparison.OrdinalIgnoreCase))
                .Select(static definition => Math.Max(1, definition.PublishIntervalMs))
                .Distinct()
                .Order())
            {
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(interval),
                };
                timer.Tick += OnIntervalTimerTick;
                _intervalTimers[interval] = timer;
                timer.Start();
            }
        }

        private void OnIntervalTimerTick(object? sender, EventArgs e)
        {
            if (_disposed || sender is not DispatcherTimer timer)
            {
                return;
            }

            var interval = _intervalTimers
                .Where(entry => ReferenceEquals(entry.Value, timer))
                .Select(static entry => entry.Key)
                .FirstOrDefault();
            if (interval <= 0)
            {
                return;
            }

            foreach (var definition in _definitions.Where(definition =>
                         string.Equals(definition.PublishMode, BrokerPublishedItemPublishModes.Interval, StringComparison.OrdinalIgnoreCase)
                         && Math.Max(1, definition.PublishIntervalMs) == interval))
            {
                PublishDefinitionIfAvailable(definition, PublishIntent.IntervalUpdate);
            }
        }

        private void OnDataItemChanged(object? sender, DataChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var definition in _definitions.Where(definition =>
                         string.Equals(definition.PublishMode, BrokerPublishedItemPublishModes.OnChanged, StringComparison.OrdinalIgnoreCase)
                         && BrokerPublishedItemChangeMatcher.ShouldPublish(definition, e, ResolveLocalItem)))
            {
                RecordLocalHostWriteState(definition, e);
                if (BrokerPublishedItemChangeMatcher.ShouldPublishDefinitionChange(definition, e, ResolveLocalItem))
                {
                    PublishDefinitionIfAvailable(definition, PublishIntent.ChangeUpdate, e);
                }
            }
        }

        private void RecordLocalHostWriteState(BrokerPublishedItemDefinition definition, DataChangedEventArgs change)
        {
            if (!definition.Writable
                || change.ChangeKind is not (DataChangeKind.ValueUpdated or DataChangeKind.PropertyUpdated))
            {
                return;
            }

            var localItem = ResolveLocalItem(definition.LocalPath);
            if (localItem is null)
            {
                return;
            }

            var parameterName = change.ChangeKind == DataChangeKind.ValueUpdated
                ? "read"
                : change.ParameterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            object? value;
            string targetPath;
            var changedPath = TargetPathHelper.NormalizeConfiguredTargetPath(change.Key);
            if (change.ChangeKind == DataChangeKind.ValueUpdated)
            {
                targetPath = HostItemBrokerWriteBackClient.ResolveValueWriteTargetPath(localItem, definition.LocalPath, ResolveLocalItem);
                if (string.Equals(TargetPathHelper.NormalizeConfiguredTargetPath(targetPath), changedPath, StringComparison.OrdinalIgnoreCase))
                {
                    value = ResolveLocalItem(change.Key)?.Value ?? change.ItemModel.Value;
                }
                else
                {
                    var targetItem = ResolveLocalItem(targetPath);
                    value = targetItem?.Value ?? change.ItemModel.Value;
                }
            }
            else
            {
                targetPath = string.Equals(parameterName, "write", StringComparison.OrdinalIgnoreCase)
                    ? HostItemBrokerWriteBackClient.ResolveWriteRequestTargetPath(localItem, definition.LocalPath, ResolveLocalItem)
                    : localItem.Path ?? TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
                ItemModel? valueSource;
                if (string.Equals(TargetPathHelper.NormalizeConfiguredTargetPath(targetPath), changedPath, StringComparison.OrdinalIgnoreCase)
                    && ResolveLocalItem(change.Key) is { } changedTargetItem
                    && changedTargetItem.Properties.Has(parameterName))
                {
                    valueSource = changedTargetItem;
                }
                else
                {
                    var targetItem = ResolveLocalItem(targetPath);
                    valueSource = string.Equals(parameterName, "write", StringComparison.OrdinalIgnoreCase)
                        ? targetItem
                        : localItem;
                }

                if (valueSource is null || !valueSource.Properties.Has(parameterName))
                {
                    return;
                }

                value = valueSource.Properties[parameterName].Value;
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            _recordLocalHostWriteState(targetPath, parameterName, value);
        }

        private static ItemModel? ResolveLocalItem(string localPath)
        {
            foreach (var candidatePath in TargetPathHelper.EnumerateResolutionCandidates(localPath))
            {
                if (HostRegistries.Data.TryResolve(candidatePath, out var localItem) && localItem is not null)
                {
                    return localItem;
                }
            }

            return null;
        }

        private void PublishDefinitionIfAvailable(
            BrokerPublishedItemDefinition definition,
            PublishIntent intent,
            DataChangedEventArgs? change = null)
        {
            if (_disposed || string.IsNullOrWhiteSpace(definition.LocalPath) || string.IsNullOrWhiteSpace(definition.BrokerPath))
            {
                return;
            }

            var localItem = ResolveLocalItem(definition.LocalPath);
            if (localItem is null)
            {
                return;
            }

            var snapshot = CreateFlatPublishSnapshot(localItem, definition.BrokerPath);
            RemoveNonMqttPublishableProperties(snapshot, removeCommandProperties: true);
            var brokerPath = NormalizeBrokerPath(definition.BrokerPath);
            if (intent == PublishIntent.ChangeUpdate && change is not null && TryCreateChangedBrokerItem(definition, change, out var changedItem))
            {
                RemoveNonMqttPublishableProperties(changedItem, removeCommandProperties: false);
                var changedBrokerPath = NormalizeBrokerPath(changedItem.Path);
                if (!string.IsNullOrWhiteSpace(changedBrokerPath))
                {
                    if (change.ChangeKind == DataChangeKind.ValueUpdated)
                    {
                        _ = PublishValueUpdateAsync(changedItem, changedBrokerPath);
                        return;
                    }

                    if (change.ChangeKind == DataChangeKind.PropertyUpdated && !string.IsNullOrWhiteSpace(change.ParameterName))
                    {
                        _ = PublishParameterUpdateAsync(changedItem, change.ParameterName, changedBrokerPath);
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(brokerPath) || !IsRegisteredBrokerPath(brokerPath))
            {
                _ = PublishSnapshotAsync(snapshot, brokerPath);
                return;
            }

            if (intent == PublishIntent.IntervalUpdate)
            {
                if (snapshot.GetDictionary().Count == 0)
                {
                    _ = PublishValueUpdateAsync(snapshot, brokerPath);
                }
                else
                {
                    _ = PublishSnapshotAsync(snapshot, brokerPath);
                }

                return;
            }

            _ = PublishSnapshotAsync(snapshot, brokerPath);
        }

        private async Task PublishSnapshotAsync(ItemModel snapshot, string brokerPath)
        {
            try
            {
                if (ShouldWritePublishDiagnostics())
                {
                    HostLogger.Log.Debug(
                        "[ItemClientPublish] kind=snapshot widget={WidgetName} brokerPath={BrokerPath} value={Value}",
                        _item.Name,
                        brokerPath,
                        snapshot.Value);
                }

                await _client.PublishSnapshotAsync(snapshot).ConfigureAwait(false);
                RegisterBrokerPaths(snapshot);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientPublish] Failed to publish local item LocalPath={LocalPath} BrokerPath={BrokerPath}.", snapshot.Path ?? string.Empty, brokerPath);
            }
        }

        private async Task PublishValueUpdateAsync(ItemModel item, string brokerPath)
        {
            try
            {
                if (ShouldWritePublishDiagnostics())
                {
                    HostLogger.Log.Debug(
                        "[ItemClientPublish] kind=value widget={WidgetName} brokerPath={BrokerPath} value={Value}",
                        _item.Name,
                        brokerPath,
                        item.Value);
                }

                await _client.PublishReadAsync(item).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientPublish] Failed to update local item value LocalPath={LocalPath} BrokerPath={BrokerPath}.", item.Path ?? string.Empty, brokerPath);
            }
        }

        private async Task PublishParameterUpdateAsync(ItemModel item, string parameterName, string brokerPath)
        {
            if (!IsMqttPublishablePropertyName(parameterName))
            {
                if (ShouldWritePublishDiagnostics())
                {
                    HostLogger.Log.Debug(
                        "[ItemClientPublish] skipped invalid MQTT property widget={WidgetName} brokerPath={BrokerPath} parameter={Parameter}",
                        _item.Name,
                        brokerPath,
                        parameterName);
                }

                return;
            }

            try
            {
                if (ShouldWritePublishDiagnostics())
                {
                    var value = item.Properties.Has(parameterName) ? item.Properties[parameterName].Value : null;
                    HostLogger.Log.Debug(
                        "[ItemClientPublish] kind=parameter widget={WidgetName} brokerPath={BrokerPath} parameter={Parameter} value={Value}",
                        _item.Name,
                        brokerPath,
                        parameterName,
                        value);
                }

                await _client.PublishPropertyAsync(item, parameterName).ConfigureAwait(false);
                if (IsSnapshotCommandPropertyName(parameterName))
                {
                    var value = item.Properties.Has(parameterName) ? item.Properties[parameterName].Value : null;
                    _recordOwnWriteCommand(brokerPath, parameterName, value);
                }
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(
                    ex,
                    "[ItemClientPublish] Failed to update local item parameter LocalPath={LocalPath} BrokerPath={BrokerPath} Parameter={Parameter}.",
                    item.Path ?? string.Empty,
                    brokerPath,
                    parameterName);
            }
        }

        private static bool TryCreateChangedBrokerItem(
            BrokerPublishedItemDefinition definition,
            DataChangedEventArgs change,
            out ItemModel changedItem)
        {
            changedItem = null!;

            if (change.ChangeKind is not (DataChangeKind.ValueUpdated or DataChangeKind.PropertyUpdated))
            {
                return false;
            }

            var localPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
            var changedPath = TargetPathHelper.NormalizeConfiguredTargetPath(change.Key);
            var brokerPath = NormalizeBrokerPath(definition.BrokerPath);
            if (string.IsNullOrWhiteSpace(localPath)
                || string.IsNullOrWhiteSpace(changedPath)
                || string.IsNullOrWhiteSpace(brokerPath))
            {
                return false;
            }

            if (string.Equals(localPath, changedPath, StringComparison.OrdinalIgnoreCase))
            {
                changedItem = CreateFlatPublishSnapshot(change.ItemModel, brokerPath);
                return HasChangedParameter(changedItem, change);
            }

            return false;
        }

        private static bool HasChangedParameter(ItemModel item, DataChangedEventArgs change)
            => change.ChangeKind == DataChangeKind.ValueUpdated
                || (!string.IsNullOrWhiteSpace(change.ParameterName) && item.Properties.Has(change.ParameterName));

        private static ItemModel CreateFlatPublishSnapshot(ItemModel source, string brokerPath)
        {
            var snapshot = ItemExtension.CloneWithPath(source, brokerPath);
            foreach (var childName in snapshot.GetDictionary().Keys.ToArray())
            {
                snapshot.Remove(childName);
            }

            return snapshot;
        }

        private static void RemoveNonMqttPublishableProperties(ItemModel item, bool removeCommandProperties)
        {
            foreach (var propertyName in item.Properties.GetDictionary().Keys.ToArray())
            {
                if (!IsMqttPublishablePropertyName(propertyName)
                    || (removeCommandProperties && IsSnapshotCommandPropertyName(propertyName)))
                {
                    item.Properties.Remove(propertyName);
                }
            }

            foreach (var child in item.GetDictionary().Values)
            {
                RemoveNonMqttPublishableProperties(child, removeCommandProperties);
            }
        }

        private static bool IsSnapshotCommandPropertyName(string propertyName)
            => string.Equals(propertyName, "write", StringComparison.OrdinalIgnoreCase);

        private static bool IsMqttPublishablePropertyName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName)
                || propertyName[0] < 'a'
                || propertyName[0] > 'z')
            {
                return false;
            }

            for (var index = 1; index < propertyName.Length; index++)
            {
                var character = propertyName[index];
                if ((character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '_')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool ShouldWritePublishDiagnostics()
            => AppContext.TryGetSwitch(PublishDiagnosticsSwitchName, out var enabled) && enabled;

        private static IEnumerable<string> EnumerateBrokerPaths(ItemModel item)
        {
            var path = NormalizeBrokerPath(item.Path);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }

            foreach (var child in item.GetDictionary().Values)
            {
                foreach (var childPath in EnumerateBrokerPaths(child))
                {
                    yield return childPath;
                }
            }
        }

        private static string NormalizeBrokerPath(string? brokerPath)
            => TargetPathHelper.NormalizeConfiguredTargetPath(brokerPath);

        private bool IsRegisteredBrokerPath(string brokerPath)
        {
            lock (_registrationSync)
            {
                return _registeredBrokerPaths.Contains(brokerPath);
            }
        }

        private void RegisterBrokerPaths(ItemModel snapshot)
        {
            lock (_registrationSync)
            {
                foreach (var path in EnumerateBrokerPaths(snapshot))
                {
                    _registeredBrokerPaths.Add(path);
                }
            }
        }

        private enum PublishIntent
        {
            Snapshot,
            IntervalUpdate,
            ChangeUpdate
        }

    }

    private sealed class OwnWriteEchoTracker
    {
        private const int MaximumPendingEchoes = 256;
        private static readonly TimeSpan EchoRetention = TimeSpan.FromSeconds(30);
        private readonly object _sync = new();
        private readonly List<PendingWriteEcho> _pendingEchoes = [];

        public void Record(string brokerPath, string parameterName, object? value)
        {
            var normalizedBrokerPath = TargetPathHelper.NormalizeConfiguredTargetPath(brokerPath);
            var normalizedParameterName = TargetPathHelper.NormalizeConfiguredTargetPath(parameterName);
            if (string.IsNullOrWhiteSpace(normalizedBrokerPath)
                || !string.Equals(normalizedParameterName, "write", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_sync)
            {
                PruneExpired(DateTimeOffset.UtcNow);
                _pendingEchoes.Add(new PendingWriteEcho(normalizedBrokerPath, normalizedParameterName, value, DateTimeOffset.UtcNow));
                while (_pendingEchoes.Count > MaximumPendingEchoes)
                {
                    _pendingEchoes.RemoveAt(0);
                }
            }
        }

        public bool TryConsume(string brokerPath, string parameterName, object? value)
        {
            var normalizedBrokerPath = TargetPathHelper.NormalizeConfiguredTargetPath(brokerPath);
            var normalizedParameterName = TargetPathHelper.NormalizeConfiguredTargetPath(parameterName);
            if (string.IsNullOrWhiteSpace(normalizedBrokerPath)
                || !string.Equals(normalizedParameterName, "write", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            lock (_sync)
            {
                PruneExpired(DateTimeOffset.UtcNow);
                for (var index = 0; index < _pendingEchoes.Count; index++)
                {
                    var echo = _pendingEchoes[index];
                    if (string.Equals(echo.BrokerPath, normalizedBrokerPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(echo.ParameterName, normalizedParameterName, StringComparison.OrdinalIgnoreCase)
                        && ValuesEqual(echo.Value, value))
                    {
                        _pendingEchoes.RemoveAt(index);
                        return true;
                    }
                }
            }

            return false;
        }

        private void PruneExpired(DateTimeOffset now)
        {
            for (var index = _pendingEchoes.Count - 1; index >= 0; index--)
            {
                if (now - _pendingEchoes[index].CreatedAt > EchoRetention)
                {
                    _pendingEchoes.RemoveAt(index);
                }
            }
        }

        private static bool ValuesEqual(object? left, object? right)
        {
            if (Equals(left, right))
            {
                return true;
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                try
                {
                    return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                        == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsNumeric(object? value)
            => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

        private readonly record struct PendingWriteEcho(string BrokerPath, string ParameterName, object? Value, DateTimeOffset CreatedAt);
    }

    private sealed class LocalHostWriteTracker
    {
        private const int MaximumPendingWrites = 512;
        private static readonly TimeSpan WriteRetention = TimeSpan.FromSeconds(5);
        private readonly object _sync = new();
        private readonly List<PendingHostWrite> _pendingWrites = [];

        public void Record(string targetPath, string parameterName, object? value)
        {
            var normalizedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
            var normalizedParameterName = parameterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedTargetPath)
                || string.IsNullOrWhiteSpace(normalizedParameterName))
            {
                return;
            }

            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                PruneExpired(now);
                _pendingWrites.Add(new PendingHostWrite(normalizedTargetPath, normalizedParameterName, value, now));
                while (_pendingWrites.Count > MaximumPendingWrites)
                {
                    _pendingWrites.RemoveAt(0);
                }
            }
        }

        public bool HasRecentConflict(string targetPath, string parameterName, object? value)
        {
            var normalizedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
            var normalizedParameterName = parameterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedTargetPath)
                || string.IsNullOrWhiteSpace(normalizedParameterName))
            {
                return false;
            }

            lock (_sync)
            {
                PruneExpired(DateTimeOffset.UtcNow);
                for (var index = _pendingWrites.Count - 1; index >= 0; index--)
                {
                    var pendingWrite = _pendingWrites[index];
                    if (!string.Equals(pendingWrite.TargetPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase)
                        || !IsRelevantParameter(pendingWrite.ParameterName, normalizedParameterName))
                    {
                        continue;
                    }

                    return !ValuesEqual(pendingWrite.Value, value);
                }
            }

            return false;
        }

        private static bool IsRelevantParameter(string recordedParameterName, string incomingParameterName)
        {
            if (string.Equals(recordedParameterName, incomingParameterName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(incomingParameterName, "read", StringComparison.OrdinalIgnoreCase)
                && string.Equals(recordedParameterName, "write", StringComparison.OrdinalIgnoreCase);
        }

        private void PruneExpired(DateTimeOffset now)
        {
            for (var index = _pendingWrites.Count - 1; index >= 0; index--)
            {
                if (now - _pendingWrites[index].CreatedAt > WriteRetention)
                {
                    _pendingWrites.RemoveAt(index);
                }
            }
        }

        private static bool ValuesEqual(object? left, object? right)
        {
            if (Equals(left, right))
            {
                return true;
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                try
                {
                    return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                        == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsNumeric(object? value)
            => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

        private readonly record struct PendingHostWrite(string TargetPath, string ParameterName, object? Value, DateTimeOffset CreatedAt);
    }

    private sealed class OwnedBrokerRuntime : IAsyncDisposable
    {
        private readonly MqttItemServerHost _host;

        public OwnedBrokerRuntime(FolderItemModel item)
        {
            _host = new MqttItemServerHost(new MqttItemServerOptions
            {
                Host = item.BrokerHost,
                Port = item.BrokerPort,
                BaseTopic = item.BrokerBaseTopic,
                ClientId = $"{item.ServerClientId}-broker",
                SubscriptionRootPath = "Runtime",
            });
        }

        public Task StartAsync()
            => _host.StartAsync();

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
    }

    private bool CanUpdateUi()
        => _isAttachedToVisualTree && this.GetVisualRoot() is not null;

    private static string FormatBaseTopic(string? baseTopic)
        => string.IsNullOrWhiteSpace(baseTopic) ? "(none)" : baseTopic.Trim();
}

/// <summary>
/// Provides the YAML-facing Item client type.
/// </summary>
public partial class ItemClient : ItemClientControl
{
}

/// <summary>
/// Matches host registry changes to active ItemClient publish definitions.
/// </summary>
public static class BrokerPublishedItemChangeMatcher
{
    /// <summary>
    /// Determines whether a registry change should be observed for a published definition.
    /// </summary>
    /// <param name="definition">The publish definition.</param>
    /// <param name="change">The registry change.</param>
    /// <param name="resolveLocalItem">The resolver used to inspect effective write targets.</param>
    /// <returns><see langword="true"/> when the change belongs to the definition or its effective writable target.</returns>
    public static bool ShouldObserveChange(
        BrokerPublishedItemDefinition definition,
        DataChangedEventArgs change,
        Func<string, ItemModel?> resolveLocalItem)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(resolveLocalItem);

        var localPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
        var changedPath = TargetPathHelper.NormalizeConfiguredTargetPath(change.Key);
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(changedPath))
        {
            return false;
        }

        if (string.Equals(localPath, changedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!definition.Writable || change.ChangeKind is not (DataChangeKind.ValueUpdated or DataChangeKind.PropertyUpdated))
        {
            return false;
        }

        var localItem = resolveLocalItem(definition.LocalPath);
        if (localItem is null)
        {
            return false;
        }

        var effectiveTargetPath = change.ChangeKind == DataChangeKind.ValueUpdated
            ? HostItemBrokerWriteBackClient.ResolveValueWriteTargetPath(localItem, definition.LocalPath, resolveLocalItem)
            : string.Equals(change.ParameterName?.Trim(), "write", StringComparison.OrdinalIgnoreCase)
                ? HostItemBrokerWriteBackClient.ResolveWriteRequestTargetPath(localItem, definition.LocalPath, resolveLocalItem)
                : localItem.Path ?? localPath;

        return string.Equals(
            TargetPathHelper.NormalizeConfiguredTargetPath(effectiveTargetPath),
            changedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a registry change should publish the active definition item.
    /// </summary>
    /// <param name="definition">The publish definition.</param>
    /// <param name="change">The registry change.</param>
    /// <param name="resolveLocalItem">A resolver retained for publish matcher compatibility.</param>
    /// <returns><see langword="true"/> when the definition should publish.</returns>
    public static bool ShouldPublish(
        BrokerPublishedItemDefinition definition,
        DataChangedEventArgs change,
        Func<string, ItemModel?> resolveLocalItem)
        => ShouldObserveChange(definition, change, resolveLocalItem);

    /// <summary>
    /// Determines whether a registry change should publish a direct change update for the definition path.
    /// </summary>
    /// <param name="definition">The publish definition.</param>
    /// <param name="change">The registry change.</param>
    /// <param name="resolveLocalItem">A resolver retained for matcher compatibility.</param>
    /// <returns><see langword="true"/> when the change happened on the definition's local publish path.</returns>
    public static bool ShouldPublishDefinitionChange(
        BrokerPublishedItemDefinition definition,
        DataChangedEventArgs change,
        Func<string, ItemModel?> resolveLocalItem)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(resolveLocalItem);

        var localPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
        var changedPath = TargetPathHelper.NormalizeConfiguredTargetPath(change.Key);
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(changedPath))
        {
            return false;
        }

        return string.Equals(localPath, changedPath, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents one attached broker item shown in the Item client body.
/// </summary>
public sealed class BrokerAttachedItemRow
    : INotifyPropertyChanged
{
    private readonly FolderItemModel _ownerItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerAttachedItemRow"/> class.
    /// </summary>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="itemPath">The flat broker item path.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="summaryText">The summary text.</param>
    /// <param name="alertText">The alert text.</param>
    /// <param name="isLive">Whether the attached item is currently live.</param>
    public BrokerAttachedItemRow(FolderItemModel ownerItem, string itemPath, string displayName, string summaryText, string alertText, bool isLive)
    {
        _ownerItem = ownerItem ?? throw new ArgumentNullException(nameof(ownerItem));
        ItemPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
        DisplayName = displayName;
        SummaryText = summaryText;
        AlertText = alertText;
        IsLive = isLive;
    }

    /// <summary>
    /// Occurs when a row property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the flat broker item path.
    /// </summary>
    public string ItemPath { get; }

    /// <summary>
    /// Gets the compact path text shown in the row.
    /// </summary>
    public string PathText => StripOwnerPrefix(ItemPath, TargetPathHelper.GetCanonicalItemClientName(_ownerItem.Name));

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the summary text.
    /// </summary>
    public string SummaryText { get; }

    /// <summary>
    /// Gets the alert text.
    /// </summary>
    public string AlertText { get; }

    /// <summary>
    /// Gets the row tooltip text.
    /// </summary>
    public string ToolTipText => BuildToolTipText(SummaryText, AlertText);

    /// <summary>
    /// Gets a value indicating whether an alert should be shown.
    /// </summary>
    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    /// <summary>
    /// Gets a value indicating whether the attached item is currently live.
    /// </summary>
    public bool IsLive { get; }

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
    /// Gets the traffic-light status indicator brush.
    /// </summary>
    public IBrush StatusIndicatorBrush => IsLive ? Brushes.ForestGreen : Brushes.Firebrick;

    /// <summary>
    /// Raises property changed notifications for theme-dependent row values.
    /// </summary>
    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
    }

    private static string BuildToolTipText(string summaryText, string alertText)
        => string.IsNullOrWhiteSpace(alertText)
            ? summaryText
            : $"{summaryText}{Environment.NewLine}{alertText}";

    private static string StripOwnerPrefix(string path, string ownerPrefix)
    {
        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var normalizedPrefix = TargetPathHelper.NormalizeConfiguredTargetPath(ownerPrefix);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return normalizedPath;
        }

        return normalizedPath.StartsWith(normalizedPrefix + ".", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[(normalizedPrefix.Length + 1)..]
            : normalizedPath;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Represents one local registry root configured for broker publishing.
/// </summary>
public sealed class BrokerPublishedRootRow
    : INotifyPropertyChanged
{
    private readonly FolderItemModel _ownerItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerPublishedRootRow"/> class.
    /// </summary>
    /// <param name="ownerItem">The owning widget item.</param>
    /// <param name="localRootPath">The local registry root path.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="summaryText">The summary text.</param>
    /// <param name="alertText">The alert text.</param>
    /// <param name="hasActiveEntries">Whether the root has active publish entries.</param>
    /// <param name="exists">Whether the local root currently exists.</param>
    public BrokerPublishedRootRow(FolderItemModel ownerItem, string localRootPath, string displayName, string summaryText, string alertText, bool hasActiveEntries, bool exists)
    {
        _ownerItem = ownerItem ?? throw new ArgumentNullException(nameof(ownerItem));
        LocalRootPath = TargetPathHelper.NormalizeConfiguredTargetPath(localRootPath);
        DisplayName = displayName;
        SummaryText = summaryText;
        AlertText = alertText;
        HasActiveEntries = hasActiveEntries;
        Exists = exists;
    }

    /// <summary>
    /// Occurs when a row property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the local registry root path.
    /// </summary>
    public string LocalRootPath { get; }

    /// <summary>
    /// Gets the compact path text shown in the row.
    /// </summary>
    public string PathText => StripFolderPrefix(LocalRootPath, _ownerItem.FolderName);

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the summary text.
    /// </summary>
    public string SummaryText { get; }

    /// <summary>
    /// Gets the alert text.
    /// </summary>
    public string AlertText { get; }

    /// <summary>
    /// Gets the row tooltip text.
    /// </summary>
    public string ToolTipText => BuildToolTipText(SummaryText, AlertText);

    /// <summary>
    /// Gets a value indicating whether an alert should be shown.
    /// </summary>
    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    /// <summary>
    /// Gets a value indicating whether active entries exist.
    /// </summary>
    public bool HasActiveEntries { get; }

    /// <summary>
    /// Gets a value indicating whether the local root currently exists.
    /// </summary>
    public bool Exists { get; }

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
    /// Gets the traffic-light status indicator brush.
    /// </summary>
    public IBrush StatusIndicatorBrush => !Exists
        ? Brushes.Firebrick
        : HasActiveEntries
            ? Brushes.ForestGreen
            : Brushes.DarkOrange;

    /// <summary>
    /// Raises property changed notifications for theme-dependent row values.
    /// </summary>
    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
    }

    private static string BuildToolTipText(string summaryText, string alertText)
        => string.IsNullOrWhiteSpace(alertText)
            ? summaryText
            : $"{summaryText}{Environment.NewLine}{alertText}";

    private static string StripFolderPrefix(string path, string folderName)
    {
        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var normalizedFolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return normalizedPath;
        }

        var prefix = $"studio.{normalizedFolderName}";
        return normalizedPath.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[(prefix.Length + 1)..]
            : normalizedPath;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
