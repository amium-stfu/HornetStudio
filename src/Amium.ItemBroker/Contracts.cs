using Amium.Item;

namespace Amium.ItemBroker;

/// <summary>
/// Defines the transport-neutral item broker surface.
/// </summary>
public interface IItemBroker
{
    /// <summary>
    /// Subscribes a client to item updates.
    /// </summary>
    /// <param name="client">The subscribing client.</param>
    /// <param name="message">The subscription request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active subscription.</returns>
    Task<IItemSubscription> SubscribeAsync(IItemBrokerClient client, ItemSubscribeMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a retained item snapshot.
    /// </summary>
    /// <param name="message">The snapshot message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishSnapshotAsync(ItemSnapshotMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an item value change.
    /// </summary>
    /// <param name="message">The value change message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishValueChangedAsync(ItemValueChangedMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an item parameter change.
    /// </summary>
    /// <param name="message">The parameter change message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishParameterChangedAsync(ItemParameterChangedMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes retained state for a path and its descendants.
    /// </summary>
    /// <param name="message">The remove message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAsync(ItemRemoveMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Routes a write request to the owning publisher or matching adapter.
    /// </summary>
    /// <param name="message">The write request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An acknowledgement describing the routing result.</returns>
    Task<ItemBrokerAckMessage> WriteAsync(ItemWriteRequestMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a broker client that can receive routed messages.
/// </summary>
public interface IItemBrokerClient
{
    /// <summary>
    /// Gets the stable client id.
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Receives a broker message.
    /// </summary>
    /// <param name="message">The routed message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous receive operation.</returns>
    Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a transport adapter boundary for broker integration.
/// </summary>
public interface IItemBrokerTransport
{
    /// <summary>
    /// Starts the transport adapter.
    /// </summary>
    /// <param name="broker">The broker instance to connect to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartAsync(IItemBroker broker, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the transport adapter.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines retained state storage for item broker snapshots.
/// </summary>
public interface IItemBrokerStore
{
    /// <summary>
    /// Upserts a retained item snapshot.
    /// </summary>
    /// <param name="message">The snapshot message.</param>
    void UpsertSnapshot(ItemSnapshotMessage message);

    /// <summary>
    /// Updates the retained value for an item.
    /// </summary>
    /// <param name="message">The value change message.</param>
    void UpdateValue(ItemValueChangedMessage message);

    /// <summary>
    /// Updates a retained item parameter.
    /// </summary>
    /// <param name="message">The parameter change message.</param>
    void UpdateParameter(ItemParameterChangedMessage message);

    /// <summary>
    /// Removes retained state for a path and its descendants.
    /// </summary>
    /// <param name="path">The canonical path to remove.</param>
    void Remove(string path);

    /// <summary>
    /// Gets retained snapshots matching a subscription.
    /// </summary>
    /// <param name="path">The subscription path.</param>
    /// <param name="recursive">Whether descendants should be included.</param>
    /// <returns>The retained snapshots matching the subscription.</returns>
    IReadOnlyList<ItemSnapshotMessage> GetRetained(string path, bool recursive);
}

/// <summary>
/// Defines an active broker subscription.
/// </summary>
public interface IItemSubscription : IAsyncDisposable
{
    /// <summary>
    /// Gets the subscription id.
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    /// Gets the subscribed client.
    /// </summary>
    IItemBrokerClient Client { get; }

    /// <summary>
    /// Gets the canonical subscription path.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets a value indicating whether descendants are included.
    /// </summary>
    bool Recursive { get; }
}

/// <summary>
/// Defines testable timestamp creation for broker messages.
/// </summary>
public interface IItemBrokerClock
{
    /// <summary>
    /// Gets the current timestamp.
    /// </summary>
    /// <returns>The current timestamp.</returns>
    DateTimeOffset GetUtcNow();
}

/// <summary>
/// Provides system UTC timestamps for broker messages.
/// </summary>
public sealed class SystemItemBrokerClock : IItemBrokerClock
{
    /// <inheritdoc />
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}
