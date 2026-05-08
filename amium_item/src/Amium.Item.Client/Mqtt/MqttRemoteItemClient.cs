using Amium.Item.Server;
using Amium.Items;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Client.Mqtt;

/// <summary>
/// Provides a reusable remote MQTT Item Server client for external and hybrid scenarios.
/// </summary>
public sealed class MqttRemoteItemClient : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<string> _publishedServerPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly MqttItemClientOptions _options;
    private MqttItemClientSession? _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttRemoteItemClient"/> class.
    /// </summary>
    /// <param name="options">The MQTT remote client options.</param>
    public MqttRemoteItemClient(MqttItemClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Gets the MQTT broker host.
    /// </summary>
    public string Host => _options.Host;

    /// <summary>
    /// Gets the MQTT broker port.
    /// </summary>
    public int Port => _options.Port;

    /// <summary>
    /// Gets the MQTT base topic.
    /// </summary>
    public string BaseTopic => _options.BaseTopic;

    /// <summary>
    /// Gets the local MQTT client id.
    /// </summary>
    public string ClientId => _options.ClientId;

    /// <summary>
    /// Gets a value indicating whether the client is connected.
    /// </summary>
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

    /// <summary>
    /// Occurs when diagnostics are produced.
    /// </summary>
    public event Action<string>? Diagnostic;

    /// <summary>
    /// Occurs when the visible remote item snapshots change.
    /// </summary>
    public event Action? RemoteItemsChanged;

    /// <summary>
    /// Connects to the remote MQTT-backed broker.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_session is not null)
            {
                return;
            }
        }

        var session = new MqttItemClientSession(_options);
        session.RemoteItems.Changed += OnRemoteItemsChanged;
        lock (_sync)
        {
            _session = session;
        }

        try
        {
            await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
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
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        RemoteItemsChanged?.Invoke();
    }

    /// <summary>
    /// Disconnects from the remote MQTT-backed broker.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        MqttItemClientSession? session;
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
        RemoteItemsChanged?.Invoke();
        GC.KeepAlive(cancellationToken);
    }

    /// <summary>
    /// Returns detached visible remote item snapshots keyed by remote client id.
    /// </summary>
    /// <returns>The visible remote item snapshots.</returns>
    public IReadOnlyDictionary<string, ItemModel> GetRemoteItemSnapshots()
    {
        lock (_sync)
        {
            if (_session is null)
            {
                return new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase);
            }

            var visibleRoots = new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in _session.RemoteItems.GetClientRoots())
            {
                if (string.Equals(root.Key, ClientId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var visibleRoot = root.Value.Clone();
                foreach (var publishedPath in _publishedServerPaths)
                {
                    RemoveDescendant(visibleRoot, publishedPath);
                }

                if (visibleRoot.GetDictionary().Count > 0 || visibleRoot.Properties.GetDictionary().Count > 0 || visibleRoot.Value is not null)
                {
                    visibleRoots[root.Key] = visibleRoot;
                }
            }

            return visibleRoots;
        }
    }

    /// <summary>
    /// Publishes a local item snapshot to the broker and hides the same path from visible remote snapshots.
    /// </summary>
    /// <param name="item">The item snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PublishSnapshotAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var session = GetRequiredSession();
        await session.PublishSnapshotAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _publishedServerPaths.Add(ItemServerItemPath.Resolve(item));
        }

        RemoteItemsChanged?.Invoke();
    }

    /// <summary>
    /// Publishes a value update for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    public async Task<ItemServerAckMessage> UpdateValueAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var acknowledgement = await GetRequiredSession()
            .UpdateValueAsync(item, retained: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        lock (_sync)
        {
            _publishedServerPaths.Add(ItemServerItemPath.Resolve(item));
        }

        RemoteItemsChanged?.Invoke();
        return acknowledgement;
    }

    /// <summary>
    /// Publishes a parameter update for a previously registered broker item.
    /// </summary>
    /// <param name="item">The item containing the broker path and parameter value.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    public async Task<ItemServerAckMessage> UpdatePropertyAsync(
        ItemModel item,
        string parameterName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        var acknowledgement = await GetRequiredSession()
            .UpdatePropertyAsync(
                item: item,
                parameterName: parameterName,
                retained: true,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        lock (_sync)
        {
            _publishedServerPaths.Add(ItemServerItemPath.Resolve(item));
        }

        RemoteItemsChanged?.Invoke();
        return acknowledgement;
    }

    /// <summary>
    /// Subscribes to broker updates for one item path.
    /// </summary>
    /// <param name="path">The broker item path.</param>
    /// <param name="handler">The update handler.</param>
    /// <param name="options">The subscription options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active subscription.</returns>
    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredSession().SubscribeAsync(
            path: path,
            handler: handler,
            options: options,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private MqttItemClientSession GetRequiredSession()
    {
        lock (_sync)
        {
            return _session ?? throw new InvalidOperationException("Cannot use the remote MQTT client before it is connected.");
        }
    }

    private void OnRemoteItemsChanged(object? sender, MqttRemoteItemChangedEventArgs e)
    {
        if (e.Kind == MqttRemoteItemChangeKind.Diagnostic)
        {
            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                Diagnostic?.Invoke(e.Message);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(e.RemoteClientId))
        {
            return;
        }

        RemoteItemsChanged?.Invoke();
    }

    private static void RemoveDescendant(ItemModel root, string path)
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
}