using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Amium.Item;
using Amium.ItemBroker;
using Amium.ItemBroker.Mqtt;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using HornetStudio.Logging;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Displays and publishes remote MQTT ItemBroker items for the editor.
/// </summary>
public partial class BrokerClientControl : EditorTemplateControl
{
    private const string MqttTransportSegment = "Mqtt";
    private const string SharedBrokerRootSegment = "shared";

    public static readonly DirectProperty<BrokerClientControl, string> ConnectionStateTextProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, string>(nameof(ConnectionStateText), control => control.ConnectionStateText);

    public static readonly DirectProperty<BrokerClientControl, string> EndpointTextProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, string>(nameof(EndpointText), control => control.EndpointText);

    public static readonly DirectProperty<BrokerClientControl, string> ClientTextProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, string>(nameof(ClientText), control => control.ClientText);

    public static readonly DirectProperty<BrokerClientControl, string> ItemCountTextProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, string>(nameof(ItemCountText), control => control.ItemCountText);

    public static readonly DirectProperty<BrokerClientControl, bool> HasNoAttachedItemsProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, bool>(nameof(HasNoAttachedItems), control => control.HasNoAttachedItems);

    public static readonly DirectProperty<BrokerClientControl, bool> HasNoPublishedItemsProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, bool>(nameof(HasNoPublishedItems), control => control.HasNoPublishedItems);

    public static readonly DirectProperty<BrokerClientControl, IBrush> ConnectionStatusBackgroundProperty =
        AvaloniaProperty.RegisterDirect<BrokerClientControl, IBrush>(nameof(ConnectionStatusBackground), control => control.ConnectionStatusBackground);

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
    private OwnedBrokerRuntime? _ownedBrokerRuntime;
    private HostItemBrokerPublisher? _hostItemPublisher;
    private HostItemBrokerWriteBackClient? _hostItemWriteBackClient;
    private bool _isConnecting;
    private string _connectionStateText = "Disconnected";
    private string _endpointText = "127.0.0.1:1883";
    private string _clientText = "BaseTopic hornet | MQTT Client hornet-studio";
    private string _itemCountText = "0 items";
    private bool _hasNoAttachedItems = true;
    private bool _hasNoPublishedItems = true;
    private IBrush _connectionStatusBackground = Brushes.Black;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerClientControl"/> class.
    /// </summary>
    public BrokerClientControl()
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

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private Control CreateHeaderActionsContent()
    {
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
        button.Click += OnToggleConnectionClicked;
        return button;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RefreshPresentation();
        if (Item?.BrokerAutoConnect == true)
        {
            ConnectInternal();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _runtimeUpdateTimer.Stop();
        UnhookObservedItem();
        DisconnectInternal();
        RemovePublishedRuntimeItems(Item);
        RemovePublishedAttachOptionItems();
        _publishedRuntimeKeys.Clear();
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

        if (e.PropertyName is nameof(FolderItemModel.BrokerAttachedItemPaths)
            or nameof(FolderItemModel.ItemExposures)
            or nameof(FolderItemModel.Name)
            or nameof(FolderItemModel.BrokerHost)
            or nameof(FolderItemModel.BrokerPort)
            or nameof(FolderItemModel.BrokerBaseTopic)
            or nameof(FolderItemModel.BrokerClientId)
            or nameof(FolderItemModel.BrokerMode)
            or nameof(FolderItemModel.BrokerPublishedItemPaths))
        {
            if (e.PropertyName is nameof(FolderItemModel.BrokerHost)
                or nameof(FolderItemModel.BrokerPort)
                or nameof(FolderItemModel.BrokerBaseTopic)
                or nameof(FolderItemModel.BrokerClientId)
                or nameof(FolderItemModel.BrokerMode))
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

        if (e.PropertyName == nameof(FolderItemModel.BrokerAutoConnect))
        {
            if (Item?.BrokerAutoConnect == true)
            {
                ConnectInternal();
            }
            else
            {
                DisconnectInternal();
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
            DisconnectInternal();
        }

        e.Handled = true;
    }

    private async void ConnectInternal()
    {
        var item = Item;
        if (item is null || _client is not null || _isConnecting)
        {
            return;
        }

        HostItemBrokerClient? client = null;
        OwnedBrokerRuntime? ownedBrokerRuntime = null;
        _isConnecting = true;
        try
        {
            if (string.Equals(item.BrokerMode, BrokerWidgetModes.Own, StringComparison.OrdinalIgnoreCase))
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
                item.BrokerClientId);
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
            _ownedBrokerRuntime = ownedBrokerRuntime;
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
                    HostLogger.Log.Warning(disposeException, "[BrokerWidgetConnect] Failed to dispose broker client after connect failure.");
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
                    HostLogger.Log.Warning(disposeException, "[BrokerWidgetConnect] Failed to dispose owned broker after connect failure.");
                }
            }

            ConnectionStateText = "Failed";
            ConnectionStatusBackground = Brushes.Firebrick;
            HostLogger.Log.Warning(
                ex,
                "[BrokerWidgetConnect] Widget={WidgetName} Mode={BrokerMode} Host={Host} Port={Port} BaseTopic={BaseTopic} ClientId={ClientId}",
                NormalizeWidgetName(item),
                item.BrokerMode,
                item.BrokerHost,
                item.BrokerPort,
                item.BrokerBaseTopic,
                item.BrokerClientId);
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

    private async void DisconnectInternal()
    {
        _runtimeUpdateTimer.Stop();
        RemovePublishedRuntimeItems(Item);
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
        var ownedBrokerRuntime = _ownedBrokerRuntime;
        _ownedBrokerRuntime = null;
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
                HostLogger.Log.Warning(ex, "[BrokerWidgetDisconnect] Failed to dispose connecting broker client.");
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
                HostLogger.Log.Warning(ex, "[BrokerWidgetDisconnect] Failed to dispose connecting owned broker.");
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
                HostLogger.Log.Warning(ex, "[BrokerWidgetDisconnect] Failed to dispose broker client.");
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
                HostLogger.Log.Warning(ex, "[BrokerWidgetDisconnect] Failed to dispose owned broker.");
            }
        }

        ConnectionStateText = "Disconnected";
        ConnectionStatusBackground = Brushes.Black;
        RefreshPresentation();
    }

    private void ReconnectIfAutoConnectEnabled()
    {
        if (Item?.BrokerAutoConnect != true)
        {
            DisconnectInternal();
            return;
        }

        DisconnectInternal();
        ConnectInternal();
    }

    private void RestartHostItemPublisher(bool publishInitialSnapshots = true)
    {
        var item = Item;
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
        _hostItemPublisher = new HostItemBrokerPublisher(item, client);
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
        var item = Item;
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
        var writeBackClient = new HostItemBrokerWriteBackClient(client, definitions);
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
        if (_runtimeUpdateTimer.IsEnabled)
        {
            return;
        }

        _runtimeUpdateTimer.Start();
    }

    private void OnRuntimeUpdateTimerTick(object? sender, EventArgs e)
    {
        _runtimeUpdateTimer.Stop();
        PublishRuntimeItems();
        RefreshPresentation();
    }

    private void PublishRuntimeItems()
    {
        if (Item is null || _client is null)
        {
            RemoveStaleRuntimeItems(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            RemovePublishedAttachOptionItems();
            RebuildAttachedItemRows();
            return;
        }

        var widgetName = NormalizeWidgetName(Item);
        var attachOptions = EnumerateAttachOptions(widgetName).ToArray();
        LogAttachDiagnostics(widgetName, attachOptions);
        PublishAttachOptionItems(Item, attachOptions);
        var attachedPaths = ParseAttachedFlatPaths(Item.BrokerAttachedItemPaths);
        var exposureDefinitions = ItemExposureDefinitionCodec.ParseDefinitions(Item.ItemExposures);
        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RemoveLegacyRuntimeItems(Item);
        foreach (var entry in _client.GetItemSnapshots())
        {
            foreach (var remoteItem in EnumerateAttachableRemoteItems(entry.Value))
            {
                var relativePath = GetRelativeRemoteItemPath(entry.Value, remoteItem);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var attachIdentity = BuildBrokerAttachIdentity(widgetName, entry.Key, relativePath);
                if (!attachedPaths.Contains(attachIdentity))
                {
                    continue;
                }

                var key = BuildReceivedMqttRuntimePath(Item, entry.Key, relativePath);
                var snapshot = remoteItem.Clone().Repath(key);
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
        var item = Item;
        if (item is null)
        {
            return;
        }

        EndpointText = $"{item.BrokerHost}:{item.BrokerPort}";
        ClientText = $"BaseTopic {item.BrokerBaseTopic} | MQTT Client {item.BrokerClientId}";
        var attachOptionCount = EnumerateAttachOptions(NormalizeWidgetName(item)).Count();
        ItemCountText = $"{attachOptionCount} items";
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

    private void RebuildAttachedItemRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildAttachedItemRows);
            return;
        }

        var item = Item;
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
            .Select(static path => TargetPathHelper.ToFlatItemBrokerPath(path))
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

        var item = Item;
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
        var flatPath = TargetPathHelper.ToFlatItemBrokerPath(attachedPath);
        return ItemExposurePublisher.FindByItemPath(definitions, flatPath)
               ?? ItemExposurePublisher.FindByItemPath(definitions, TargetPathHelper.ToRelativeItemBrokerPath(flatPath));
    }

    private static string NormalizeWidgetName(FolderItemModel item)
        => string.IsNullOrWhiteSpace(item.Name) ? "BrokerWidget" : TargetPathHelper.NormalizeConfiguredTargetPath(item.Name).Replace('.', '_');

    private static IEnumerable<Item> EnumerateAttachableRemoteItems(Item root)
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

    private static bool HasAttachableValue(Item item)
    {
        if (!item.Params.GetDictionary().TryGetValue("Value", out var parameter))
        {
            return false;
        }

        object? value = parameter.Value;
        return value is not null;
    }

    private static string GetRelativeRemoteItemPath(Item clientRoot, Item remoteItem)
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
        var remotePath = BuildVisibleRemoteMqttSuffix(widgetName, remoteRootKey, relativeRemoteItemPath);
        return string.IsNullOrWhiteSpace(remotePath)
            ? $"Studio.{item.FolderName}.{widgetName}.{MqttTransportSegment}"
            : $"Studio.{item.FolderName}.{widgetName}.{MqttTransportSegment}.{remotePath}";
    }

    private static string BuildBrokerAttachIdentity(string widgetName, string remoteRootKey, string relativeRemoteItemPath)
    {
        var remotePath = BuildVisibleRemoteMqttSuffix(widgetName, remoteRootKey, relativeRemoteItemPath);
        return string.IsNullOrWhiteSpace(remotePath)
            ? $"{widgetName}.{MqttTransportSegment}"
            : $"{widgetName}.{MqttTransportSegment}.{remotePath}";
    }

    private static string BuildLegacyBrokerAttachIdentity(string widgetName, string remoteRootKey, string relativeRemoteItemPath)
    {
        var rootKey = TargetPathHelper.NormalizeConfiguredTargetPath(remoteRootKey);
        var relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativeRemoteItemPath);
        return string.IsNullOrWhiteSpace(relativePath)
            ? $"{widgetName}.{rootKey}"
            : $"{widgetName}.{rootKey}.{relativePath}";
    }

    private static string BuildVisibleRemoteMqttPath(string remoteRootKey, string relativeRemoteItemPath)
    {
        var rootKey = TargetPathHelper.NormalizeConfiguredTargetPath(remoteRootKey);
        var relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativeRemoteItemPath);
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

    private static string BuildVisibleRemoteMqttSuffix(string widgetName, string remoteRootKey, string relativeRemoteItemPath)
    {
        var remotePath = BuildVisibleRemoteMqttPath(remoteRootKey, relativeRemoteItemPath);
        var segments = TargetPathHelper.SplitPathSegments(remotePath);
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], widgetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], MqttTransportSegment, StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Skip(index + 2));
            }
        }

        return remotePath;
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

        return _client.GetItemSnapshots()
            .SelectMany(entry =>
            {
                return EnumerateAttachableRemoteItems(entry.Value)
                    .Select(remoteItem => GetRelativeRemoteItemPath(entry.Value, remoteItem))
                    .Where(static relativePath => !string.IsNullOrWhiteSpace(relativePath))
                    .Select(relativePath => BuildBrokerAttachIdentity(widgetName, entry.Key, relativePath));
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
        var leafCount = roots.Values.Sum(root => EnumerateAttachableRemoteItems(root).Count());
        var sampleRoots = string.Join(", ", roots.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).Take(4));
        var sampleLeaves = string.Join(", ", roots.Values
            .SelectMany(EnumerateAttachableRemoteItems)
            .Select(static item => item.Path ?? item.Name ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Take(4));
        var sampleOptions = string.Join(", ", attachOptions.Take(4));
        var signature = $"{roots.Count}|{leafCount}|{attachOptions.Count}|{sampleRoots}|{sampleLeaves}|{sampleOptions}";
        if (string.Equals(signature, _lastAttachDiagnosticsSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastAttachDiagnosticsSignature = signature;
        if (roots.Count > 0 && attachOptions.Count == 0)
        {
            HostLogger.Log.Warning(
                "[BrokerWidgetAttach] Widget={WidgetName} RemoteRoots={RemoteRoots} AttachableLeaves={AttachableLeaves} AttachOptions={AttachOptions} Roots={Roots} Leaves={Leaves} Options={Options}",
                widgetName,
                roots.Count,
                leafCount,
                attachOptions.Count,
                sampleRoots,
                sampleLeaves,
                sampleOptions);
        }
        else
        {
            HostLogger.Log.Debug(
                "[BrokerWidgetAttach] Widget={WidgetName} RemoteRoots={RemoteRoots} AttachableLeaves={AttachableLeaves} AttachOptions={AttachOptions} Roots={Roots} Leaves={Leaves} Options={Options}",
                widgetName,
                roots.Count,
                leafCount,
                attachOptions.Count,
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
                var snapshot = new Item(option, path: attachOptionsBasePath);
                snapshot.Params["Kind"].Value = "Status";
                snapshot.Params["Text"].Value = "AttachOption";
                snapshot.Params["Title"].Value = option;
                snapshot.Params["Value"].Value = option;
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
        if (Item is null
            || sender is not Button { CommandParameter: BrokerAttachedItemRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: HornetStudio.Editor.ViewModels.MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await ItemExposureDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: Item.ItemExposures,
            itemPath: row.ItemPath);
        if (result is null)
        {
            return;
        }

        Item.ItemExposures = result;
        PublishRuntimeItems();
        RebuildAttachedItemRows();
        e.Handled = true;
    }

    private async void OnDeleteAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (Item is null
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

        var remainingPaths = ParseAttachedFlatPaths(Item.BrokerAttachedItemPaths)
            .Where(path => !string.Equals(path, row.ItemPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Item.BrokerAttachedItemPaths = SerializeAttachedFlatPaths(remainingPaths);
        Item.ItemExposures = RemoveExposureDefinition(Item.ItemExposures, row.ItemPath);
        PublishRuntimeItems();
        RebuildAttachedItemRows();
        e.Handled = true;
    }

    private async void OnEditPublishedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (Item is null
            || sender is not Button { CommandParameter: BrokerPublishedRootRow row }
            || TopLevel.GetTopLevel(this) is not Window { DataContext: HornetStudio.Editor.ViewModels.MainWindowViewModel viewModel } owner)
        {
            return;
        }

        var result = await PublishedItemDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            rawDefinitions: Item.BrokerPublishedItemPaths,
            rootPath: row.LocalRootPath);
        if (result is null)
        {
            return;
        }

        Item.BrokerPublishedItemPaths = result;
        PublishRetainedSnapshotsForRoot(row.LocalRootPath);
        RebuildPublishedItemRows();
        e.Handled = true;
    }

    private async void OnDeletePublishedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (Item is null
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

        var remainingDefinitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(Item.BrokerPublishedItemPaths)
            .Where(definition => !string.Equals(definition.LocalRootPath, row.LocalRootPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Item.BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(remainingDefinitions);
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
        => $"Studio.{item.FolderName}.{NormalizeWidgetName(item)}.Status.AttachOptions";

    private static void RemovePublishedRuntimeItems(FolderItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        RemoveKeysBelowPrefix($"Runtime.ItemBroker.{NormalizeWidgetName(item)}");
        RemoveKeysBelowPrefix($"Studio.{item.FolderName}.{NormalizeWidgetName(item)}.{MqttTransportSegment}");
    }

    private static void RemoveLegacyRuntimeItems(FolderItemModel item)
    {
        RemoveKeysBelowPrefix($"Runtime.ItemBroker.{NormalizeWidgetName(item)}");
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
        private readonly Dictionary<int, DispatcherTimer> _intervalTimers = new();
        private IReadOnlyList<BrokerPublishedItemDefinition> _definitions = [];
        private bool _disposed;

        public HostItemBrokerPublisher(FolderItemModel item, IHostItemBrokerClient client)
        {
            _item = item;
            _client = client;
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
                PublishDefinitionIfAvailable(definition);
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
                PublishDefinitionIfAvailable(definition);
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
                PublishDefinitionIfAvailable(definition);
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
                PublishDefinitionIfAvailable(definition);
            }
        }

        private static Item? ResolveLocalItem(string localPath)
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

        private void PublishDefinitionIfAvailable(BrokerPublishedItemDefinition definition)
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

            var snapshot = localItem.Clone().Repath(definition.BrokerPath);
            _ = PublishSnapshotAsync(snapshot, definition.BrokerPath);
        }

        private async Task PublishSnapshotAsync(Item snapshot, string brokerPath)
        {
            try
            {
                await _client.PublishItemAsync(snapshot, brokerPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[BrokerWidgetPublish] Failed to publish local item LocalPath={LocalPath} BrokerPath={BrokerPath}.", snapshot.Path ?? string.Empty, brokerPath);
            }
        }

    }

    private sealed class OwnedBrokerRuntime : IAsyncDisposable
    {
        private readonly InMemoryItemBroker _broker = new();
        private readonly MqttItemBrokerAdapter _adapter;

        public OwnedBrokerRuntime(FolderItemModel item)
        {
            _adapter = new MqttItemBrokerAdapter(new MqttItemBrokerOptions
            {
                Host = item.BrokerHost,
                Port = item.BrokerPort,
                BaseTopic = item.BrokerBaseTopic,
                ClientId = $"{item.BrokerClientId}-broker",
                SubscriptionRootPath = "Runtime",
            });
        }

        public Task StartAsync()
            => _adapter.StartAsync(_broker);

        public async ValueTask DisposeAsync()
        {
            await _adapter.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Provides the YAML-facing Broker widget type.
/// </summary>
public partial class BrokerWidget : BrokerClientControl
{
}

/// <summary>
/// Matches host registry changes to active BrokerWidget publish definitions.
/// </summary>
public static class BrokerPublishedItemChangeMatcher
{
    /// <summary>
    /// Determines whether a registry change should publish the active definition snapshot.
    /// </summary>
    /// <param name="definition">The publish definition.</param>
    /// <param name="change">The registry change.</param>
    /// <param name="resolveLocalItem">A resolver for local registry items by path.</param>
    /// <returns><see langword="true"/> when the definition should publish.</returns>
    public static bool ShouldPublish(
        BrokerPublishedItemDefinition definition,
        DataChangedEventArgs change,
        Func<string, Item?> resolveLocalItem)
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

        if (IsDescendantPath(changedPath, localPath))
        {
            var localItem = resolveLocalItem(localPath);
            return localItem?.GetDictionary().Count > 0;
        }

        if (IsDescendantPath(localPath, changedPath))
        {
            return change.ChangeKind == DataChangeKind.SnapshotUpserted && resolveLocalItem(localPath) is not null;
        }

        return false;
    }

    private static bool IsDescendantPath(string candidatePath, string ancestorPath)
        => candidatePath.StartsWith(ancestorPath + ".", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Represents one attached broker item shown in the Broker widget body.
/// </summary>
public sealed class BrokerAttachedItemRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerAttachedItemRow"/> class.
    /// </summary>
    /// <param name="itemPath">The flat broker item path.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="summaryText">The summary text.</param>
    /// <param name="alertText">The alert text.</param>
    /// <param name="isLive">Whether the attached item is currently live.</param>
    public BrokerAttachedItemRow(string itemPath, string displayName, string summaryText, string alertText, bool isLive)
    {
        ItemPath = TargetPathHelper.ToFlatItemBrokerPath(itemPath);
        DisplayName = displayName;
        SummaryText = summaryText;
        AlertText = alertText;
        IsLive = isLive;
    }

    /// <summary>
    /// Gets the flat broker item path.
    /// </summary>
    public string ItemPath { get; }

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
    public IBrush RowBorderBrush => IsLive ? Brushes.SlateGray : Brushes.DimGray;

    /// <summary>
    /// Gets the primary foreground brush.
    /// </summary>
    public IBrush PrimaryForeground => Brushes.White;

    /// <summary>
    /// Gets the secondary foreground brush.
    /// </summary>
    public IBrush SecondaryForeground => IsLive ? Brushes.LightSteelBlue : Brushes.Gray;
}

/// <summary>
/// Represents one local registry root configured for broker publishing.
/// </summary>
public sealed class BrokerPublishedRootRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerPublishedRootRow"/> class.
    /// </summary>
    /// <param name="localRootPath">The local registry root path.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="summaryText">The summary text.</param>
    /// <param name="alertText">The alert text.</param>
    /// <param name="hasActiveEntries">Whether the root has active publish entries.</param>
    /// <param name="exists">Whether the local root currently exists.</param>
    public BrokerPublishedRootRow(string localRootPath, string displayName, string summaryText, string alertText, bool hasActiveEntries, bool exists)
    {
        LocalRootPath = TargetPathHelper.NormalizeConfiguredTargetPath(localRootPath);
        DisplayName = displayName;
        SummaryText = summaryText;
        AlertText = alertText;
        HasActiveEntries = hasActiveEntries;
        Exists = exists;
    }

    /// <summary>
    /// Gets the local registry root path.
    /// </summary>
    public string LocalRootPath { get; }

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
    public IBrush RowBorderBrush => HasActiveEntries ? Brushes.ForestGreen : Brushes.DimGray;

    /// <summary>
    /// Gets the primary foreground brush.
    /// </summary>
    public IBrush PrimaryForeground => Brushes.White;

    /// <summary>
    /// Gets the secondary foreground brush.
    /// </summary>
    public IBrush SecondaryForeground => Exists ? Brushes.LightSteelBlue : Brushes.Gray;
}
