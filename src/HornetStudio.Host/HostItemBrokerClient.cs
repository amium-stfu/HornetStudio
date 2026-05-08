using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Server;
using Amium.Item.Client.Mqtt;
using HornetStudio.Logging;

namespace HornetStudio.Host;

/// <summary>
/// Defines a host-side MQTT ItemBroker client that exposes remote runtime items.
/// </summary>
public interface IHostItemBrokerClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the widget client name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the MQTT broker host.
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Gets the MQTT broker port.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Gets the MQTT base topic.
    /// </summary>
    string BaseTopic { get; }

    /// <summary>
    /// Gets the local MQTT client id.
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Gets a value indicating whether the client is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the remote runtime items.
    /// </summary>
    ItemDictionary Items { get; }

    /// <summary>
    /// Gets snapshot clones of the current remote runtime item roots keyed by remote client id.
    /// </summary>
    /// <returns>The snapshot clones keyed by remote client id.</returns>
    IReadOnlyDictionary<string, ItemModel> GetItemSnapshots();

    /// <summary>
    /// Publishes a local item snapshot to the broker.
    /// </summary>
    /// <param name="item">The item snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishSnapshotAsync(ItemModel item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a value update for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    Task<ItemServerAckMessage> UpdateValueAsync(ItemModel item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a parameter update for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and parameter value.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    Task<ItemServerAckMessage> UpdateParameterAsync(ItemModel item, string parameterName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to broker updates for one item path.
    /// </summary>
    /// <param name="path">The broker item path.</param>
    /// <param name="handler">The update handler.</param>
    /// <param name="options">The subscription options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active subscription.</returns>
    Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Occurs when diagnostics are produced.
    /// </summary>
    event Action<string>? Diagnostic;

    /// <summary>
    /// Occurs when remote items change.
    /// </summary>
    event Action? ItemsChanged;

    /// <summary>
    /// Connects to the MQTT broker.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the MQTT broker.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Hosts a generic MQTT ItemBroker client and mirrors remote items into runtime paths.
/// </summary>
public sealed class HostItemBrokerClient : IHostItemBrokerClient
{
    private readonly object _sync = new();
    private readonly MqttRemoteItemClient _remoteClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemBrokerClient"/> class.
    /// </summary>
    /// <param name="name">The widget client name.</param>
    /// <param name="host">The MQTT broker host.</param>
    /// <param name="port">The MQTT broker port.</param>
    /// <param name="baseTopic">The MQTT base topic.</param>
    /// <param name="clientId">The local MQTT client id.</param>
    public HostItemBrokerClient(string name, string host, int port, string baseTopic, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        Name = name.Trim();
        Host = host.Trim();
        Port = port <= 0 ? 1883 : port;
        BaseTopic = baseTopic.Trim();
        ClientId = clientId.Trim();
        Items = new ItemDictionary($"runtime.item_broker.{Name}");
        _remoteClient = new MqttRemoteItemClient(new MqttItemClientOptions
        {
            Host = Host,
            Port = Port,
            BaseTopic = BaseTopic,
            ClientId = ClientId,
        });
        _remoteClient.Diagnostic += OnRemoteDiagnostic;
        _remoteClient.RemoteItemsChanged += OnRemoteItemsChanged;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public int Port { get; }

    /// <inheritdoc />
    public string BaseTopic { get; }

    /// <inheritdoc />
    public string ClientId { get; }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            return _remoteClient.IsConnected;
        }
    }

    /// <inheritdoc />
    public ItemDictionary Items { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ItemModel> GetItemSnapshots()
    {
        lock (_sync)
        {
            return Items.GetDictionary()
                .ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public Task PublishSnapshotAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        HostLogger.Log.Debug(
            "[HostItemBrokerClientPublish] kind=snapshot client={ClientId} path={Path} value={Value}",
            ClientId,
            item.Path ?? string.Empty,
            item.Value);
        return _remoteClient.PublishSnapshotAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ItemServerAckMessage> UpdateValueAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        HostLogger.Log.Debug(
            "[HostItemBrokerClientPublish] kind=value client={ClientId} path={Path} value={Value}",
            ClientId,
            item.Path ?? string.Empty,
            item.Value);
        return _remoteClient.UpdateValueAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ItemServerAckMessage> UpdateParameterAsync(ItemModel item, string parameterName, CancellationToken cancellationToken = default)
    {
        var value = item.Properties.Has(parameterName) ? item.Properties[parameterName].Value : null;
        HostLogger.Log.Debug(
            "[HostItemBrokerClientPublish] kind=parameter client={ClientId} path={Path} parameter={Parameter} value={Value}",
            ClientId,
            item.Path ?? string.Empty,
            parameterName,
            value);
        return _remoteClient.UpdatePropertyAsync(item, parameterName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        HostLogger.Log.Debug(
            "[HostItemBrokerClientSubscribe] client={ClientId} path={Path} recursive={Recursive}",
            ClientId,
            path,
            options?.Recursive ?? true);
        return _remoteClient.SubscribeAsync(
            path: path,
            handler: handler,
            options: options,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public event Action<string>? Diagnostic;

    /// <inheritdoc />
    public event Action? ItemsChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            RebuildItems();
        }

        ItemsChanged?.Invoke();
        RaiseDiagnostic($"connected host={Host}:{Port} baseTopic={BaseTopic} clientId={ClientId}");
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClient.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            Items.Clear();
        }

        RaiseDiagnostic("disconnected");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void OnRemoteItemsChanged()
    {
        lock (_sync)
        {
            RebuildItems();
        }

        ItemsChanged?.Invoke();
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var root in _remoteClient.GetRemoteItemSnapshots())
        {
            Items[root.Key] = root.Value.Clone();
        }
    }

    private void OnRemoteDiagnostic(string message)
        => RaiseDiagnostic(message);

    private void RaiseDiagnostic(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Diagnostic?.Invoke($"[HostItemBrokerClient:{Name}] {message}");
        }
    }
}
