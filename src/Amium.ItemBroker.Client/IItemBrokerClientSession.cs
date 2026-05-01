using Amium.ItemBroker;
using ItemModel = Amium.Item.Item;

namespace Amium.ItemBroker.Client;

/// <summary>
/// Defines a convenient client session for publishing, writing, and subscribing to item broker data.
/// </summary>
public interface IItemBrokerClientSession : IItemBrokerClient
{
    /// <summary>
    /// Publishes an item snapshot or optimized item delta.
    /// </summary>
    /// <param name="item">The item to publish.</param>
    /// <param name="path">The optional item path override.</param>
    /// <param name="policy">The optional publish policy.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishItemAsync(
        ItemModel item,
        string? path = null,
        ItemPublishPolicy? policy = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a value delta for an item.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <param name="value">The value to publish.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishValueAsync(string path, object? value, string? correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current value of an item as a value delta.
    /// </summary>
    /// <param name="item">The item whose current value should be published.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishValueAsync(ItemModel item, string? correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a parameter delta for an item.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The value to publish.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishParameterAsync(
        string path,
        string parameterName,
        object? value,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current value of a named item parameter as a parameter delta.
    /// </summary>
    /// <param name="item">The item that owns the parameter.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishParameterAsync(
        ItemModel item,
        string parameterName,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a write request to an item owner.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    Task<ItemBrokerAckMessage> WriteAsync(
        string path,
        object? value,
        string parameterName = "Value",
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to item broker messages.
    /// </summary>
    /// <param name="path">The subscription path.</param>
    /// <param name="handler">The message handler.</param>
    /// <param name="options">The optional subscription options.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active subscription.</returns>
    Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
