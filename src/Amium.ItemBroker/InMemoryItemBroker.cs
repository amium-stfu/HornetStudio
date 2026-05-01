using System.Collections.Concurrent;

namespace Amium.ItemBroker;

/// <summary>
/// Provides an in-process, transport-neutral item broker implementation.
/// </summary>
public sealed class InMemoryItemBroker : IItemBroker
{
    private const string BrokerClientId = "ItemBroker";
    private readonly IItemBrokerStore _store;
    private readonly IItemBrokerClock _clock;
    private readonly IItemRetentionPolicyResolver _retentionPolicyResolver;
    private readonly ConcurrentDictionary<string, BrokerSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IItemBrokerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _lastPublishersByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryItemBroker"/> class.
    /// </summary>
    /// <param name="store">The retained state store.</param>
    /// <param name="clock">The timestamp source.</param>
    /// <param name="retentionPolicyResolver">The central retention policy resolver.</param>
    public InMemoryItemBroker(
        IItemBrokerStore? store = null,
        IItemBrokerClock? clock = null,
        IItemRetentionPolicyResolver? retentionPolicyResolver = null)
    {
        _store = store ?? new InMemoryItemBrokerStore();
        _clock = clock ?? new SystemItemBrokerClock();
        _retentionPolicyResolver = retentionPolicyResolver ?? new DefaultItemRetentionPolicyResolver();
    }

    /// <inheritdoc />
    public async Task<IItemSubscription> SubscribeAsync(IItemBrokerClient client, ItemSubscribeMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(message);

        _clients[client.ClientId] = client;
        var subscription = new BrokerSubscription(
            subscriptionId: Guid.NewGuid().ToString("N"),
            client: client,
            path: ItemBrokerPath.Normalize(message.Path),
            recursive: message.Options.Recursive,
            onDispose: RemoveSubscription);

        _subscriptions[subscription.SubscriptionId] = subscription;

        if (message.Options.IncludeRetained)
        {
            foreach (var retained in _store.GetRetained(subscription.Path, subscription.Recursive))
            {
                await client.ReceiveAsync(retained, cancellationToken).ConfigureAwait(false);
            }
        }

        return subscription;
    }

    /// <inheritdoc />
    public async Task PublishSnapshotAsync(ItemSnapshotMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = message with { Path = ItemBrokerPath.Normalize(message.Path) };
        if (ShouldRetain(normalized))
        {
            _store.UpsertSnapshot(normalized);
        }

        TrackPublisher(normalized.Path, normalized.SourceClientId);
        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishValueChangedAsync(ItemValueChangedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = message with { Path = ItemBrokerPath.Normalize(message.Path) };
        if (ShouldRetain(normalized))
        {
            _store.UpdateValue(normalized);
        }

        TrackPublisher(normalized.Path, normalized.SourceClientId);
        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishParameterChangedAsync(ItemParameterChangedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = message with { Path = ItemBrokerPath.Normalize(message.Path) };
        if (ShouldRetain(normalized))
        {
            _store.UpdateParameter(normalized);
        }

        TrackPublisher(normalized.Path, normalized.SourceClientId);
        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(ItemRemoveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = message with { Path = ItemBrokerPath.Normalize(message.Path) };
        _store.Remove(normalized.Path);

        foreach (var path in _lastPublishersByPath.Keys)
        {
            if (ItemBrokerPath.IsSelfOrDescendant(normalized.Path, path))
            {
                _lastPublishersByPath.TryRemove(path, out _);
            }
        }

        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ItemBrokerAckMessage> WriteAsync(ItemWriteRequestMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = message with
        {
            Path = ItemBrokerPath.Normalize(message.Path),
            ParameterName = string.IsNullOrWhiteSpace(message.ParameterName) ? "Value" : message.ParameterName,
        };

        if (TryGetOwningClient(normalized.Path, out var owner))
        {
            await owner.ReceiveAsync(normalized, cancellationToken).ConfigureAwait(false);
            return new ItemBrokerAckMessage(
                normalized.Path,
                Accepted: true,
                Reason: null,
                SourceClientId: BrokerClientId,
                normalized.CorrelationId,
                _clock.GetUtcNow());
        }

        var reason = $"No owner is registered for item path '{normalized.Path}'.";
        return new ItemBrokerAckMessage(
            normalized.Path,
            Accepted: false,
            reason,
            SourceClientId: BrokerClientId,
            normalized.CorrelationId,
            _clock.GetUtcNow());
    }

    private void TrackPublisher(string path, string? sourceClientId)
    {
        if (!string.IsNullOrWhiteSpace(sourceClientId))
        {
            _lastPublishersByPath[ItemBrokerPath.Normalize(path)] = sourceClientId;
        }
    }

    private bool ShouldRetain(ItemBrokerMessage message)
        => _retentionPolicyResolver.Resolve(message, _clock.GetUtcNow()).ShouldRetain;

    private bool TryGetOwningClient(string path, out IItemBrokerClient client)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        var currentPath = normalizedPath;

        while (true)
        {
            if (_lastPublishersByPath.TryGetValue(currentPath, out var clientId)
                && _clients.TryGetValue(clientId, out client!))
            {
                return true;
            }

            var separatorIndex = currentPath.LastIndexOf('.');
            if (separatorIndex < 0)
            {
                break;
            }

            currentPath = currentPath[..separatorIndex];
        }

        client = null!;
        return false;
    }

    private async Task RouteToSubscribersAsync(ItemBrokerMessage message, CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (ItemBrokerPath.Matches(subscription.Path, message.Path, subscription.Recursive))
            {
                await subscription.Client.ReceiveAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RemoveSubscription(string subscriptionId)
    {
        _subscriptions.TryRemove(subscriptionId, out _);
    }

    private sealed class BrokerSubscription : IItemSubscription
    {
        private readonly Action<string> _onDispose;
        private bool _isDisposed;

        public BrokerSubscription(string subscriptionId, IItemBrokerClient client, string path, bool recursive, Action<string> onDispose)
        {
            SubscriptionId = subscriptionId;
            Client = client;
            Path = path;
            Recursive = recursive;
            _onDispose = onDispose;
        }

        public string SubscriptionId { get; }

        public IItemBrokerClient Client { get; }

        public string Path { get; }

        public bool Recursive { get; }

        public ValueTask DisposeAsync()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _onDispose(SubscriptionId);
            }

            return ValueTask.CompletedTask;
        }
    }
}
