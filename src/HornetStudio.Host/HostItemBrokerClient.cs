using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Client;
using HornetStudio.Logging;
using System.Text;

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
    /// Publishes a read value for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and read value.</param>
    /// <param name="publishEpoch">A value indicating whether the item epoch should be published together with the read value.</param>
    /// <param name="retained">A value indicating whether the published values should be retained.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishReadAsync(
        ItemModel item,
        bool publishEpoch = true,
        bool retained = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a parameter update for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and parameter value.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="retained">A value indicating whether the published property should be retained.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishPropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        CancellationToken cancellationToken = default);

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
    private readonly MqttItemClient _remoteClient;
    private readonly string _runtimeName;

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
        _runtimeName = NormalizePathSegment(Name, "broker_widget");
        Host = host.Trim();
        Port = port <= 0 ? 1883 : port;
        BaseTopic = baseTopic.Trim();
        ClientId = clientId.Trim();
        Items = new ItemDictionary($"runtime.item_broker.{_runtimeName}");
        _remoteClient = new MqttItemClient(new MqttItemClientOptions
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
        return _remoteClient.PublishSnapshotAsync(item, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishReadAsync(
        ItemModel item,
        bool publishEpoch = true,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        HostLogger.Log.Debug(
            "[HostItemBrokerClientPublish] kind=read client={ClientId} path={Path} value={Value} retained={Retained} publishEpoch={PublishEpoch}",
            ClientId,
            item.Path ?? string.Empty,
            item.Value,
            retained,
            publishEpoch);
        return _remoteClient.PublishReadAsync(item, publishEpoch, retained, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishPropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        var value = item.Properties.Has(parameterName) ? item.Properties[parameterName].Value : null;
        HostLogger.Log.Debug(
            "[HostItemBrokerClientPublish] kind=property client={ClientId} path={Path} parameter={Parameter} value={Value} retained={Retained}",
            ClientId,
            item.Path ?? string.Empty,
            parameterName,
            value,
            retained);
        return _remoteClient.PublishPropertyAsync(item, parameterName, retained, cancellationToken);
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
        var sharedPath = $"runtime.item_broker.{_runtimeName}.shared";
        var sharedRoot = new ItemModel("shared", path: $"runtime.item_broker.{_runtimeName}");
        var hasSharedItems = false;

        foreach (var root in _remoteClient.GetRemoteItemSnapshots())
        {
            sharedRoot[root.Key] = root.Value.CloneWithPath($"{sharedPath}.{root.Key}");
            hasSharedItems = true;
        }

        if (hasSharedItems)
        {
            Items[sharedRoot.Name] = sharedRoot;
        }
    }

    private static string NormalizePathSegment(string? segment, string fallbackSegment)
    {
        var value = string.IsNullOrWhiteSpace(segment)
            ? fallbackSegment
            : segment.Trim();

        var builder = new StringBuilder(value.Length + 8);
        var previousWasSeparator = true;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsLetterOrDigit(character))
            {
                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (char.IsUpper(character) && ShouldInsertSeparator(value, index) && !previousWasSeparator && builder.Length > 0)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }

            builder.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? fallbackSegment : normalized;
    }

    private static bool ShouldInsertSeparator(string value, int index)
    {
        if (index == 0)
        {
            return false;
        }

        var previous = value[index - 1];
        if (!char.IsLetterOrDigit(previous))
        {
            return false;
        }

        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        return index + 1 < value.Length && char.IsLower(value[index + 1]);
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
