using System.Collections.Concurrent;
using Amium.ItemBroker;
using ItemModel = Amium.Item.Item;

namespace Amium.ItemBroker.Client;

/// <summary>
/// Provides an in-process client SDK session for an item broker.
/// </summary>
public sealed class ItemBrokerClientSession : IItemBrokerClientSession
{
    private readonly IItemBroker _broker;
    private readonly IItemBrokerClock _clock;
    private readonly IItemPublishPolicyResolver _publishPolicyResolver;
    private readonly ConcurrentDictionary<string, PublishedItemState> _publishedStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<ItemBrokerMessage, CancellationToken, Task>> _handlersBySubscription = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemBrokerClientSession"/> class.
    /// </summary>
    /// <param name="clientId">The stable client id.</param>
    /// <param name="broker">The broker instance.</param>
    /// <param name="clock">The optional timestamp source.</param>
    /// <param name="publishPolicyResolver">The optional publish policy resolver.</param>
    public ItemBrokerClientSession(
        string clientId,
        IItemBroker broker,
        IItemBrokerClock? clock = null,
        IItemPublishPolicyResolver? publishPolicyResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(broker);

        ClientId = clientId;
        _broker = broker;
        _clock = clock ?? new SystemItemBrokerClock();
        _publishPolicyResolver = publishPolicyResolver ?? new DefaultItemPublishPolicyResolver();
    }

    /// <inheritdoc />
    public string ClientId { get; }

    /// <inheritdoc />
    public async Task PublishItemAsync(
        ItemModel item,
        string? path = null,
        ItemPublishPolicy? policy = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedPath = ResolveItemPath(item, path);
        var nextState = PublishedItemState.From(item);
        var requiresSnapshot = !_publishedStates.TryGetValue(normalizedPath, out var previousState);
        var decision = policy is null
            ? _publishPolicyResolver.Resolve(path: normalizedPath, isSnapshotRequired: requiresSnapshot)
            : new ItemPublishDecision(policy.Mode, ShouldPublish: true);

        if (!decision.ShouldPublish)
        {
            return;
        }

        if (requiresSnapshot || decision.Mode == ItemPublishMode.Snapshot)
        {
            await _broker.PublishSnapshotAsync(
                message: new ItemSnapshotMessage(normalizedPath, item, ClientId, correlationId, _clock.GetUtcNow()),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _publishedStates[normalizedPath] = nextState;
            return;
        }

        if (!Equals(previousState!.Value, nextState.Value))
        {
            await PublishValueAsync(
                path: normalizedPath,
                value: nextState.Value,
                correlationId: correlationId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        foreach (var parameter in nextState.Parameters)
        {
            if (!previousState.Parameters.TryGetValue(parameter.Key, out var previousValue) || !Equals(previousValue, parameter.Value))
            {
                await PublishParameterAsync(
                    path: normalizedPath,
                    parameterName: parameter.Key,
                    value: parameter.Value,
                    correlationId: correlationId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        _publishedStates[normalizedPath] = nextState;
    }

    /// <inheritdoc />
    public Task PublishValueAsync(string path, object? value, string? correlationId = null, CancellationToken cancellationToken = default)
        => _broker.PublishValueChangedAsync(
            message: new ItemValueChangedMessage(ItemBrokerPath.Normalize(path), value, ClientId, correlationId, _clock.GetUtcNow()),
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task PublishValueAsync(ItemModel item, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return PublishValueAsync(
            path: ResolveItemPath(item),
            value: item.Value,
            correlationId: correlationId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishParameterAsync(
        string path,
        string parameterName,
        object? value,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return _broker.PublishParameterChangedAsync(
            message: new ItemParameterChangedMessage(ItemBrokerPath.Normalize(path), parameterName, value, ClientId, correlationId, _clock.GetUtcNow()),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishParameterAsync(
        ItemModel item,
        string parameterName,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        return PublishParameterAsync(
            path: ResolveItemPath(item),
            parameterName: parameterName,
            value: item.Params[parameterName].Value,
            correlationId: correlationId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ItemBrokerAckMessage> WriteAsync(
        string path,
        object? value,
        string parameterName = "Value",
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        => _broker.WriteAsync(
            message: new ItemWriteRequestMessage(
                Path: ItemBrokerPath.Normalize(path),
                ParameterName: string.IsNullOrWhiteSpace(parameterName) ? "Value" : parameterName,
                Value: value,
                ReplyTo: ClientId,
                SourceClientId: ClientId,
                CorrelationId: correlationId,
                Timestamp: _clock.GetUtcNow()),
            cancellationToken: cancellationToken);

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
            message: new ItemSubscribeMessage(
                ItemBrokerPath.Normalize(path),
                selectedOptions.Recursive,
                selectedOptions.IncludeRetained,
                ClientId,
                correlationId,
                _clock.GetUtcNow())
            {
                Options = selectedOptions,
            },
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

    private static string ResolveItemPath(ItemModel item, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemBrokerPath.Normalize(path ?? item.Path ?? item.Name ?? throw new ArgumentException("Item must provide a path or name.", nameof(item)));
    }

    private sealed record PublishedItemState(object? Value, IReadOnlyDictionary<string, object?> Parameters)
    {
        public static PublishedItemState From(ItemModel item)
        {
            var parameters = item.Params
                .GetDictionary()
                .Where(parameter => !string.Equals(parameter.Key, "Value", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value.Value, StringComparer.OrdinalIgnoreCase);

            return new PublishedItemState(item.Value, parameters);
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
