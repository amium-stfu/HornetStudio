using Amium.Item;
using Amium.ItemBroker;
using Amium.ItemBroker.Mqtt.Client;

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
    IReadOnlyDictionary<string, Item> GetItemSnapshots();

    /// <summary>
    /// Publishes a local item snapshot to the broker.
    /// </summary>
    /// <param name="item">The item snapshot.</param>
    /// <param name="path">The broker path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishItemAsync(Item item, string path, CancellationToken cancellationToken = default);

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
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
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
    private readonly HashSet<string> _publishedBrokerPaths = new(StringComparer.OrdinalIgnoreCase);
    private MqttItemBrokerClientSession? _session;

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
        Items = new ItemDictionary($"Runtime.ItemBroker.{Name}");
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
            lock (_sync)
            {
                return _session is not null;
            }
        }
    }

    /// <inheritdoc />
    public ItemDictionary Items { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, Item> GetItemSnapshots()
    {
        lock (_sync)
        {
            return Items.GetDictionary()
                .ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public Task PublishItemAsync(Item item, string path, CancellationToken cancellationToken = default)
    {
        MqttItemBrokerClientSession? session;
        lock (_sync)
        {
            session = _session;
        }

        if (session is null)
        {
            throw new InvalidOperationException("Cannot publish an item before the broker client is connected.");
        }

        return PublishItemAndTrackAsync(session, item, path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        MqttItemBrokerClientSession? session;
        lock (_sync)
        {
            session = _session;
        }

        if (session is null)
        {
            throw new InvalidOperationException("Cannot subscribe before the broker client is connected.");
        }

        return session.SubscribeAsync(
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
        lock (_sync)
        {
            if (_session is not null)
            {
                return;
            }
        }

        var session = new MqttItemBrokerClientSession(new MqttItemBrokerClientOptions
        {
            Host = Host,
            Port = Port,
            BaseTopic = BaseTopic,
            ClientId = ClientId,
        });
        session.RemoteItems.Changed += OnRemoteItemsChanged;
        lock (_sync)
        {
            _session = session;
        }

        try
        {
            await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var isCurrentSession = false;
            lock (_sync)
            {
                isCurrentSession = ReferenceEquals(_session, session);
            }

            if (!isCurrentSession)
            {
                session.RemoteItems.Changed -= OnRemoteItemsChanged;
                await session.DisposeAsync().ConfigureAwait(false);
                return;
            }

            lock (_sync)
            {
                RebuildItems();
            }
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                }
            }

            session.RemoteItems.Changed -= OnRemoteItemsChanged;
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposeException)
            {
                RaiseDiagnostic($"dispose after connect failure failed: {disposeException.Message}");
            }

            throw;
        }

        ItemsChanged?.Invoke();
        RaiseDiagnostic($"connected host={Host}:{Port} baseTopic={BaseTopic} clientId={ClientId}");
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        MqttItemBrokerClientSession? session;
        lock (_sync)
        {
            session = _session;
            _session = null;
        }

        if (session is null)
        {
            return;
        }

        session.RemoteItems.Changed -= OnRemoteItemsChanged;
        await session.DisposeAsync().ConfigureAwait(false);
        RaiseDiagnostic("disconnected");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void OnRemoteItemsChanged(object? sender, MqttRemoteItemChangedEventArgs e)
    {
        if (e.Kind == MqttRemoteItemChangeKind.Diagnostic)
        {
            RaiseDiagnostic(e.Message ?? string.Empty);
            return;
        }

        if (e.Item is null || string.IsNullOrWhiteSpace(e.RemoteClientId))
        {
            return;
        }

        if (string.Equals(e.RemoteClientId.Trim(), ClientId, StringComparison.OrdinalIgnoreCase))
        {
            lock (_sync)
            {
                RebuildItems();
            }

            return;
        }

        int rootCount;
        lock (_sync)
        {
            RebuildItems();
            rootCount = Items.GetDictionary().Count;
        }

        if (e.Kind is MqttRemoteItemChangeKind.ClientStatus)
        {
            RaiseDiagnostic($"remote kind={e.Kind} clientId={e.RemoteClientId} path={e.Path} rootCount={rootCount}");
        }

        ItemsChanged?.Invoke();
    }

    private void RebuildItems()
    {
        Items.Clear();
        if (_session is null)
        {
            return;
        }

        foreach (var root in _session.RemoteItems.GetClientRoots())
        {
            if (string.Equals(root.Key, ClientId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var visibleRoot = root.Value.Clone();
            foreach (var publishedPath in _publishedBrokerPaths)
            {
                RemoveDescendant(visibleRoot, publishedPath);
            }

            if (visibleRoot.GetDictionary().Count > 0 || visibleRoot.Params.GetDictionary().Count > 0 || visibleRoot.Value is not null)
            {
                Items[root.Key] = visibleRoot;
            }
        }

    }

    private async Task PublishItemAndTrackAsync(MqttItemBrokerClientSession session, Item item, string path, CancellationToken cancellationToken)
    {
        await session.PublishItemAsync(item, path, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _publishedBrokerPaths.Add(NormalizePath(path));
            RebuildItems();
        }

        ItemsChanged?.Invoke();
    }

    private static void RemoveDescendant(Item root, string path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count == 0)
        {
            return;
        }

        var current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            var segment = segments[index];
            var matchingChildName = current.GetDictionary().Keys
                .FirstOrDefault(key => string.Equals(key, segment, StringComparison.OrdinalIgnoreCase));
            if (matchingChildName is null)
            {
                return;
            }

            current = current.GetDictionary()[matchingChildName];
        }

        var leafName = current.GetDictionary().Keys
            .FirstOrDefault(key => string.Equals(key, segments[^1], StringComparison.OrdinalIgnoreCase));
        if (leafName is not null)
        {
            current.Remove(leafName);
        }
    }

    private static string NormalizePath(string? path)
        => string.Join('.', SplitPathSegments(path));

    private static IReadOnlyList<string> SplitPathSegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path
            .Trim()
            .Replace('\\', '.')
            .Replace('/', '.')
            .Trim('.')
            .Split(['.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void RaiseDiagnostic(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Diagnostic?.Invoke($"[HostItemBrokerClient:{Name}] {message}");
        }
    }
}
