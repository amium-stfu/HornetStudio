using System.Collections.Concurrent;
using Amium.ItemBroker;
using ItemModel = Amium.Items.Item;

namespace Amium.ItemBroker.Client;

/// <summary>
/// Provides an in-process client SDK session for an item broker.
/// </summary>
public sealed class ItemBrokerClientSession : IItemBrokerClientSession
{
    private readonly IItemBroker _broker;
    private readonly IItemBrokerClock _clock;
    private readonly ConcurrentDictionary<string, Func<ItemBrokerMessage, CancellationToken, Task>> _handlersBySubscription = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemBrokerClientSession"/> class.
    /// </summary>
    /// <param name="clientId">The stable client id.</param>
    /// <param name="broker">The broker instance.</param>
    /// <param name="clock">The optional timestamp source.</param>
    public ItemBrokerClientSession(
        string clientId,
        IItemBroker broker,
        IItemBrokerClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(broker);

        ClientId = clientId;
        _broker = broker;
        _clock = clock ?? new SystemItemBrokerClock();
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
    public Task<ItemBrokerAckMessage> UpdateValueAsync(ItemModel item, bool retained = false, string? correlationId = null, CancellationToken cancellationToken = default)
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
    public Task<ItemBrokerAckMessage> UpdateParameterAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return _broker.UpdateParameterAsync(
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
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var selectedOptions = options ?? ItemSubscriptionOptions.Default;
        var subscription = await _broker.SubscribeAsync(
            client: this,
            path: ItemBrokerPath.Normalize(path),
            options: selectedOptions,
            correlationId: correlationId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _handlersBySubscription[subscription.SubscriptionId] = handler;
        return new ClientSubscription(subscription, _handlersBySubscription);
    }

    /// <inheritdoc />
    public async Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlersBySubscription.Values)
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ClientSubscription : IItemSubscription
    {
        private readonly IItemSubscription _inner;
        private readonly ConcurrentDictionary<string, Func<ItemBrokerMessage, CancellationToken, Task>> _handlersBySubscription;

        public ClientSubscription(
            IItemSubscription inner,
            ConcurrentDictionary<string, Func<ItemBrokerMessage, CancellationToken, Task>> handlersBySubscription)
        {
            _inner = inner;
            _handlersBySubscription = handlersBySubscription;
        }

        public string SubscriptionId => _inner.SubscriptionId;

        public IItemBrokerClient Client => _inner.Client;

        public string Path => _inner.Path;

        public bool Recursive => _inner.Recursive;

        public async ValueTask DisposeAsync()
        {
            _handlersBySubscription.TryRemove(SubscriptionId, out _);
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
