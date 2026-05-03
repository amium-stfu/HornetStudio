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
    private readonly ConcurrentDictionary<string, BrokerSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IItemBrokerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _ownersByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryItemBroker"/> class.
    /// </summary>
    /// <param name="store">The retained state store.</param>
    /// <param name="clock">The timestamp source.</param>
    public InMemoryItemBroker(
        IItemBrokerStore? store = null,
        IItemBrokerClock? clock = null)
    {
        _store = store ?? new InMemoryItemBrokerStore();
        _clock = clock ?? new SystemItemBrokerClock();
    }

    /// <inheritdoc />
    public async Task<IItemSubscription> SubscribeAsync(
        IItemBrokerClient client,
        string path,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var selectedOptions = options ?? ItemSubscriptionOptions.Default;
        var normalizedPath = ItemBrokerPath.Normalize(path);
        var message = new ItemSubscribeMessage(
            Path: normalizedPath,
            Recursive: selectedOptions.Recursive,
            IncludeRetained: selectedOptions.IncludeRetained,
            SourceClientId: client.ClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow())
        {
            Options = selectedOptions,
        };

        _clients[client.ClientId] = client;
        var subscription = new BrokerSubscription(
            subscriptionId: Guid.NewGuid().ToString("N"),
            client: client,
            path: normalizedPath,
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
    public async Task PublishSnapshotAsync(
        Amium.Items.Item item,
        bool retained = true,
        string? sourceClientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedPath = ItemBrokerItemPath.Resolve(item);
        var ownerClientId = NormalizeClientId(sourceClientId)
            ?? throw new InvalidOperationException($"A source client id is required to register ownership for '{normalizedPath}'.");
        EnsureSnapshotOwnership(normalizedPath, ownerClientId);

        var normalized = new ItemSnapshotMessage(
            Path: normalizedPath,
            Item: item,
            SourceClientId: ownerClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());
        if (retained)
        {
            _store.UpsertSnapshot(normalized);
        }

        _ownersByPath[normalized.Path] = ownerClientId;
        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ItemBrokerAckMessage> UpdateValueAsync(
        Amium.Items.Item item,
        bool retained = false,
        string? sourceClientId = null,
        string? correlationId = null,
        string? replyTo = null,
        CancellationToken cancellationToken = default)
        => UpdateParameterAsync(
            item: item,
            parameterName: "Value",
            retained: retained,
            sourceClientId: sourceClientId,
            correlationId: correlationId,
            replyTo: replyTo,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ItemBrokerAckMessage> UpdateParameterAsync(
        Amium.Items.Item item,
        string parameterName,
        bool retained = false,
        string? sourceClientId = null,
        string? correlationId = null,
        string? replyTo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        var normalizedPath = ItemBrokerItemPath.Resolve(item);
        var normalizedParameterName = string.IsNullOrWhiteSpace(parameterName) ? "Value" : parameterName.Trim();
        var ownerRegistration = FindOwnerRegistration(normalizedPath);
        if (ownerRegistration is null)
        {
            return CreateAck(
                path: normalizedPath,
                accepted: false,
                reason: $"No owner is registered for item path '{normalizedPath}'.",
                correlationId: correlationId);
        }

        var senderClientId = NormalizeClientId(sourceClientId);
        var value = string.Equals(normalizedParameterName, "Value", StringComparison.OrdinalIgnoreCase)
            ? item.Value
            : item.Params[normalizedParameterName].Value;

        if (string.Equals(ownerRegistration.Value.OwnerClientId, senderClientId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(normalizedParameterName, "Value", StringComparison.OrdinalIgnoreCase))
            {
                var valueMessage = new ItemValueChangedMessage(
                    Path: normalizedPath,
                    Value: value,
                    SourceClientId: senderClientId,
                    CorrelationId: correlationId,
                    Timestamp: _clock.GetUtcNow());
                if (retained)
                {
                    _store.UpdateValue(valueMessage);
                }

                await RouteToSubscribersAsync(valueMessage, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var parameterMessage = new ItemParameterChangedMessage(
                    Path: normalizedPath,
                    ParameterName: normalizedParameterName,
                    Value: value,
                    SourceClientId: senderClientId,
                    CorrelationId: correlationId,
                    Timestamp: _clock.GetUtcNow());
                if (retained)
                {
                    _store.UpdateParameter(parameterMessage);
                }

                await RouteToSubscribersAsync(parameterMessage, cancellationToken).ConfigureAwait(false);
            }

            return CreateAck(path: normalizedPath, accepted: true, reason: null, correlationId: correlationId);
        }

        if (!_clients.TryGetValue(ownerRegistration.Value.OwnerClientId, out var ownerClient))
        {
            return CreateAck(
                path: normalizedPath,
                accepted: false,
                reason: $"Owner '{ownerRegistration.Value.OwnerClientId}' is not currently available for item path '{normalizedPath}'.",
                correlationId: correlationId);
        }

        var writeRequest = new ItemWriteRequestMessage(
            Path: normalizedPath,
            ParameterName: normalizedParameterName,
            Value: value,
            ReplyTo: replyTo,
            SourceClientId: senderClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());
        await ownerClient.ReceiveAsync(writeRequest, cancellationToken).ConfigureAwait(false);

        return CreateAck(path: normalizedPath, accepted: true, reason: null, correlationId: correlationId);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        Amium.Items.Item item,
        string? sourceClientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedPath = ItemBrokerItemPath.Resolve(item);
        EnsureRemoveOwnership(normalizedPath, NormalizeClientId(sourceClientId));

        var normalized = new ItemRemoveMessage(
            Path: normalizedPath,
            SourceClientId: sourceClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());
        _store.Remove(normalized.Path);

        foreach (var registeredPath in _ownersByPath.Keys)
        {
            if (ItemBrokerPath.IsSelfOrDescendant(normalized.Path, registeredPath))
            {
                _ownersByPath.TryRemove(registeredPath, out _);
            }
        }

        await RouteToSubscribersAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureSnapshotOwnership(string path, string requestedOwnerClientId)
    {
        foreach (var registration in EnumerateOverlappingOwners(path))
        {
            if (!string.Equals(registration.OwnerClientId, requestedOwnerClientId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ItemOwnershipConflictException(
                    path: path,
                    existingOwnerClientId: registration.OwnerClientId,
                    requestedOwnerClientId: requestedOwnerClientId);
            }
        }
    }

    private void EnsureRemoveOwnership(string path, string? sourceClientId)
    {
        foreach (var registration in EnumerateOverlappingOwners(path))
        {
            if (string.Equals(registration.OwnerClientId, sourceClientId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new ItemOwnershipConflictException(
                path: path,
                existingOwnerClientId: registration.OwnerClientId,
                requestedOwnerClientId: sourceClientId ?? string.Empty);
        }
    }

    private ItemBrokerAckMessage CreateAck(string path, bool accepted, string? reason, string? correlationId)
        => new(
            Path: path,
            Accepted: accepted,
            Reason: reason,
            SourceClientId: BrokerClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());

    private OwnershipRegistration? FindOwnerRegistration(string path)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        var currentPath = normalizedPath;
        while (true)
        {
            if (_ownersByPath.TryGetValue(currentPath, out var ownerClientId))
            {
                return new OwnershipRegistration(currentPath, ownerClientId);
            }

            var separatorIndex = currentPath.LastIndexOf('.');
            if (separatorIndex < 0)
            {
                break;
            }

            currentPath = currentPath[..separatorIndex];
        }

        return null;
    }

    private IEnumerable<OwnershipRegistration> EnumerateOverlappingOwners(string path)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        foreach (var entry in _ownersByPath)
        {
            if (ItemBrokerPath.IsSelfOrDescendant(normalizedPath, entry.Key)
                || ItemBrokerPath.IsSelfOrDescendant(entry.Key, normalizedPath))
            {
                yield return new OwnershipRegistration(entry.Key, entry.Value);
            }
        }
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

    private static string? NormalizeClientId(string? clientId)
        => string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();

    private readonly record struct OwnershipRegistration(string Path, string OwnerClientId);

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
