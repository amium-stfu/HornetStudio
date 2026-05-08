using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server;

/// <summary>
/// Defines the transport-neutral item broker surface.
/// </summary>
public interface IItemServer
{
    /// <summary>
    /// Subscribes a client to item updates.
    /// </summary>
    /// <param name="client">The subscribing client.</param>
    /// <param name="path">The subscription path.</param>
    /// <param name="options">The optional subscription options.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active subscription.</returns>
    Task<IItemSubscription> SubscribeAsync(
        IItemServerClient client,
        string path,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a retained item snapshot.
    /// </summary>
    /// <param name="item">The item snapshot.</param>
    /// <param name="retained">A value indicating whether the snapshot should be retained.</param>
    /// <param name="sourceClientId">The optional source client id.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishSnapshotAsync(
        ItemModel item,
        bool retained = true,
        string? sourceClientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an item value or routes a write request to the current owner.
    /// </summary>
    /// <param name="item">The item whose current value should be published.</param>
    /// <param name="retained">A value indicating whether the updated value should be retained when the owner applies it locally.</param>
    /// <param name="sourceClientId">The optional source client id.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="replyTo">The optional reply target when the update becomes a routed write request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An acknowledgement describing the update result.</returns>
    Task<ItemServerAckMessage> UpdateValueAsync(
        ItemModel item,
        bool retained = false,
        string? sourceClientId = null,
        string? correlationId = null,
        string? replyTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an item parameter or routes a write request to the current owner.
    /// </summary>
    /// <param name="item">The item that owns the parameter.</param>
    /// <param name="parameterName">The changed parameter name.</param>
    /// <param name="retained">A value indicating whether the updated parameter should be retained when the owner applies it locally.</param>
    /// <param name="sourceClientId">The optional source client id.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="replyTo">The optional reply target when the update becomes a routed write request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An acknowledgement describing the update result.</returns>
    Task<ItemServerAckMessage> UpdatePropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        string? sourceClientId = null,
        string? correlationId = null,
        string? replyTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes retained state for a path and its descendants.
    /// </summary>
    /// <param name="item">The item root to remove.</param>
    /// <param name="sourceClientId">The optional source client id.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAsync(
        ItemModel item,
        string? sourceClientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The exception that is thrown when an item owner would be overwritten implicitly.
/// </summary>
public sealed class ItemOwnershipConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemOwnershipConflictException"/> class.
    /// </summary>
    /// <param name="path">The affected item path.</param>
    /// <param name="existingOwnerClientId">The currently registered owner client id.</param>
    /// <param name="requestedOwnerClientId">The conflicting requested owner client id.</param>
    public ItemOwnershipConflictException(
        string path,
        string existingOwnerClientId,
        string requestedOwnerClientId)
        : base($"ItemModel path '{path}' is already owned by '{existingOwnerClientId}' and cannot be claimed by '{requestedOwnerClientId}'.")
    {
        Path = path;
        ExistingOwnerClientId = existingOwnerClientId;
        RequestedOwnerClientId = requestedOwnerClientId;
    }

    /// <summary>
    /// Gets the affected item path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the currently registered owner client id.
    /// </summary>
    public string ExistingOwnerClientId { get; }

    /// <summary>
    /// Gets the requested conflicting owner client id.
    /// </summary>
    public string RequestedOwnerClientId { get; }
}

/// <summary>
/// Defines a broker client that can receive routed messages.
/// </summary>
public interface IItemServerClient
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
    Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a transport adapter boundary for broker integration.
/// </summary>
public interface IItemServerTransport
{
    /// <summary>
    /// Starts the transport adapter.
    /// </summary>
    /// <param name="broker">The broker instance to connect to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartAsync(IItemServer broker, CancellationToken cancellationToken = default);

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
public interface IItemServerStore
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
    void UpdateParameter(ItemPropertyChangedMessage message);

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
    IItemServerClient Client { get; }

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
public interface IItemServerClock
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
public sealed class SystemItemServerClock : IItemServerClock
{
    /// <inheritdoc />
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}