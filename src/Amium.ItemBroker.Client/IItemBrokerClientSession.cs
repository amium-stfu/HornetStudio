using Amium.ItemBroker;
using ItemModel = Amium.Items.Item;

namespace Amium.ItemBroker.Client;

/// <summary>
/// Defines a convenient client session for publishing, updating, and subscribing to item broker data.
/// </summary>
public interface IItemBrokerClientSession : IItemBrokerClient
{
    /// <summary>
    /// Publishes an item snapshot.
    /// </summary>
    /// <param name="item">The item to publish.</param>
    /// <param name="retained">A value indicating whether the snapshot should be retained.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishSnapshotAsync(
        ItemModel item,
        bool retained = true,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an item value.
    /// </summary>
    /// <param name="item">The item whose current value should be updated.</param>
    /// <param name="retained">A value indicating whether the updated value should be retained when the owner applies it locally.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    Task<ItemBrokerAckMessage> UpdateValueAsync(ItemModel item, bool retained = false, string? correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an item parameter.
    /// </summary>
    /// <param name="item">The item that owns the parameter.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="retained">A value indicating whether the updated parameter should be retained when the owner applies it locally.</param>
    /// <param name="correlationId">The optional correlation id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The broker acknowledgement.</returns>
    Task<ItemBrokerAckMessage> UpdateParameterAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
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
