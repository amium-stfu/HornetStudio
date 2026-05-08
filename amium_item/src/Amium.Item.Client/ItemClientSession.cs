using System.Collections.Concurrent;
using Amium.Item.Server;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Client;

/// <summary>
/// Provides an in-process client SDK session for an item broker.
/// </summary>
public sealed class ItemClientSession : IItemClientSession
{
    private readonly IItemServer _broker;
    private readonly IItemServerClock _clock;
    private readonly ConcurrentDictionary<string, Func<ItemServerMessage, CancellationToken, Task>> _handlersBySubscription = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemClientSession"/> class.
    /// </summary>
    /// <param name="clientId">The stable client id.</param>
    /// <param name="broker">The broker instance.</param>
    /// <param name="clock">The optional timestamp source.</param>
    public ItemClientSession(
        string clientId,
        IItemServer broker,
        IItemServerClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(broker);

        ClientId = clientId;
        _broker = broker;
        _clock = clock ?? new SystemItemServerClock();
    }

    /// <inheritdoc />
    public string ClientId { get; }

    /// <inheritdoc />
    public Task PublishSnapshotAsync(
        ItemModel item,
        bool retained = true,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        => _broker.PublishSnapshotAsync(
            item: item,
            retained: retained,
            sourceClientId: ClientId,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<ItemServerAckMessage> UpdateValueAsync(ItemModel item, bool retained = false, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _broker.UpdateValueAsync(
            item: item,
            retained: retained,
            sourceClientId: ClientId,
            correlationId: correlationId,
            replyTo: ClientId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ItemServerAckMessage> UpdatePropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return _broker.UpdatePropertyAsync(
            item: item,
            parameterName: parameterName,
            retained: retained,
            sourceClientId: ClientId,
            correlationId: correlationId,
            replyTo: ClientId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var selectedOptions = options ?? ItemSubscriptionOptions.Default;
        var subscription = await _broker.SubscribeAsync(
            client: this,
            path: ItemServerPath.Normalize(path),
            options: selectedOptions,
            correlationId: correlationId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _handlersBySubscription[subscription.SubscriptionId] = handler;
        return new ClientSubscription(subscription, _handlersBySubscription);
    }

    /// <inheritdoc />
    public async Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlersBySubscription.Values)
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ClientSubscription : IItemSubscription
    {
        private readonly IItemSubscription _inner;
        private readonly ConcurrentDictionary<string, Func<ItemServerMessage, CancellationToken, Task>> _handlersBySubscription;

        public ClientSubscription(
            IItemSubscription inner,
            ConcurrentDictionary<string, Func<ItemServerMessage, CancellationToken, Task>> handlersBySubscription)
        {
            _inner = inner;
            _handlersBySubscription = handlersBySubscription;
        }

        public string SubscriptionId => _inner.SubscriptionId;

        public IItemServerClient Client => _inner.Client;

        public string Path => _inner.Path;

        public bool Recursive => _inner.Recursive;

        public async ValueTask DisposeAsync()
        {
            _handlersBySubscription.TryRemove(SubscriptionId, out _);
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
