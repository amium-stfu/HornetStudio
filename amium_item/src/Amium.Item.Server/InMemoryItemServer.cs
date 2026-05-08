using System.Collections.Concurrent;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server;

/// <summary>
/// Provides an in-process, transport-neutral item broker implementation.
/// </summary>
public sealed class InMemoryItemServer : IItemServer
{
    private const string ServerClientId = "item_server";
    private const string ValueParameterName = "read";
    private readonly IItemServerStore _store;
    private readonly IItemServerClock _clock;
    private readonly ConcurrentDictionary<string, ServerSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IItemServerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _ownersByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly InMemoryItemServerStore? _inMemoryStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryItemServer"/> class.
    /// </summary>
    /// <param name="store">The retained state store.</param>
    /// <param name="clock">The timestamp source.</param>
    public InMemoryItemServer(
        IItemServerStore? store = null,
        IItemServerClock? clock = null)
    {
        _store = store ?? new InMemoryItemServerStore();
        _inMemoryStore = _store as InMemoryItemServerStore;
        _clock = clock ?? new SystemItemServerClock();
    }

    /// <summary>
    /// Gets the retained non-system item count when the broker uses the default in-memory store.
    /// </summary>
    public int RetainedItemCount => _inMemoryStore?.CountExcluding(ItemServerHealthPaths.Root) ?? 0;

    /// <inheritdoc />
    public async Task<IItemSubscription> SubscribeAsync(
        IItemServerClient client,
        string path,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var selectedOptions = options ?? ItemSubscriptionOptions.Default;
        var normalizedPath = ItemServerPath.Normalize(path);
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
        var subscription = new ServerSubscription(
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

        var normalizedPath = ItemServerItemPath.Resolve(item);
        var ownerClientId = NormalizeClientId(sourceClientId)
            ?? throw new InvalidOperationException($"A source client id is required to register ownership for '{normalizedPath}'.");
        EnsureSnapshotOwnership(normalizedPath, ownerClientId);

        var normalized = new ItemSnapshotMessage(
            Path: normalizedPath,
            ItemModel: item,
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
    public Task<ItemServerAckMessage> UpdateValueAsync(
        Amium.Items.Item item,
        bool retained = false,
        string? sourceClientId = null,
        string? correlationId = null,
        string? replyTo = null,
        CancellationToken cancellationToken = default)
        => UpdatePropertyAsync(
            item: item,
            parameterName: ValueParameterName,
            retained: retained,
            sourceClientId: sourceClientId,
            correlationId: correlationId,
            replyTo: replyTo,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ItemServerAckMessage> UpdatePropertyAsync(
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

        var normalizedPath = ItemServerItemPath.Resolve(item);
        var normalizedParameterName = string.IsNullOrWhiteSpace(parameterName) ? ValueParameterName : ItemPath.ToSnakeCaseSegment(parameterName);
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
        var value = string.Equals(normalizedParameterName, ValueParameterName, StringComparison.Ordinal)
            ? item.Value
            : item.Properties[normalizedParameterName].Value;

        if (string.Equals(ownerRegistration.Value.OwnerClientId, senderClientId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(normalizedParameterName, ValueParameterName, StringComparison.Ordinal))
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
                var parameterMessage = new ItemPropertyChangedMessage(
                    Path: normalizedPath,
                    PropertyName: normalizedParameterName,
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

        var normalizedPath = ItemServerItemPath.Resolve(item);
        EnsureRemoveOwnership(normalizedPath, NormalizeClientId(sourceClientId));

        var normalized = new ItemRemoveMessage(
            Path: normalizedPath,
            SourceClientId: sourceClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());
        _store.Remove(normalized.Path);

        foreach (var registeredPath in _ownersByPath.Keys)
        {
            if (ItemServerPath.IsSelfOrDescendant(normalized.Path, registeredPath))
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

    private ItemServerAckMessage CreateAck(string path, bool accepted, string? reason, string? correlationId)
        => new(
            Path: path,
            Accepted: accepted,
            Reason: reason,
            SourceClientId: ServerClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());

    private OwnershipRegistration? FindOwnerRegistration(string path)
    {
        var normalizedPath = ItemServerPath.Normalize(path);
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
        var normalizedPath = ItemServerPath.Normalize(path);
        foreach (var entry in _ownersByPath)
        {
            if (ItemServerPath.IsSelfOrDescendant(normalizedPath, entry.Key)
                || ItemServerPath.IsSelfOrDescendant(entry.Key, normalizedPath))
            {
                yield return new OwnershipRegistration(entry.Key, entry.Value);
            }
        }
    }

    private async Task RouteToSubscribersAsync(ItemServerMessage message, CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (ItemServerPath.Matches(subscription.Path, message.Path, subscription.Recursive))
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

    private sealed class ServerSubscription : IItemSubscription
    {
        private readonly Action<string> _onDispose;
        private bool _isDisposed;

        public ServerSubscription(string subscriptionId, IItemServerClient client, string path, bool recursive, Action<string> onDispose)
        {
            SubscriptionId = subscriptionId;
            Client = client;
            Path = path;
            Recursive = recursive;
            _onDispose = onDispose;
        }

        public string SubscriptionId { get; }

        public IItemServerClient Client { get; }

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
