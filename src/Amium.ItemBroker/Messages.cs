using ItemModel = Amium.Item.Item;

namespace Amium.ItemBroker;

/// <summary>
/// Defines the base contract for item broker messages.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="SourceClientId">The client id that produced the message.</param>
/// <param name="CorrelationId">The optional correlation id for request tracking.</param>
/// <param name="Timestamp">The message timestamp.</param>
public abstract record ItemBrokerMessage(
    string Path,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp);

/// <summary>
/// Represents a retained snapshot for an item and its descendants.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="Item">The item snapshot.</param>
/// <param name="SourceClientId">The client id that published the snapshot.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The snapshot timestamp.</param>
public sealed record ItemSnapshotMessage(
    string Path,
    ItemModel Item,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents a value change for an item.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="Value">The new item value.</param>
/// <param name="SourceClientId">The client id that published the value.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The value timestamp.</param>
public sealed record ItemValueChangedMessage(
    string Path,
    object? Value,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents a parameter change for an item.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="ParameterName">The changed parameter name.</param>
/// <param name="Value">The new parameter value.</param>
/// <param name="SourceClientId">The client id that published the parameter.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The parameter timestamp.</param>
public sealed record ItemParameterChangedMessage(
    string Path,
    string ParameterName,
    object? Value,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents a write request targeting an item parameter.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="ParameterName">The target parameter name.</param>
/// <param name="Value">The requested value.</param>
/// <param name="ReplyTo">The optional reply target.</param>
/// <param name="SourceClientId">The client id that requested the write.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The request timestamp.</param>
public sealed record ItemWriteRequestMessage(
    string Path,
    string ParameterName,
    object? Value,
    string? ReplyTo,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents removal of a retained item path.
/// </summary>
/// <param name="Path">The canonical item path.</param>
/// <param name="SourceClientId">The client id that requested removal.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The removal timestamp.</param>
public sealed record ItemRemoveMessage(
    string Path,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents a subscription request.
/// </summary>
/// <param name="Path">The canonical subscription path.</param>
/// <param name="Recursive">Whether descendants should be included.</param>
/// <param name="IncludeRetained">Whether retained snapshots should be delivered immediately.</param>
/// <param name="SourceClientId">The subscribing client id.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The request timestamp.</param>
public sealed record ItemSubscribeMessage(
    string Path,
    bool Recursive,
    bool IncludeRetained,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp)
{
    /// <summary>
    /// Gets additional subscription options for high-throughput consumers.
    /// </summary>
    public ItemSubscriptionOptions Options { get; init; } = new()
    {
        Recursive = Recursive,
        IncludeRetained = IncludeRetained,
    };
}

/// <summary>
/// Represents an unsubscribe request.
/// </summary>
/// <param name="Path">The canonical subscription path.</param>
/// <param name="Recursive">Whether the subscription was recursive.</param>
/// <param name="SourceClientId">The unsubscribing client id.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The request timestamp.</param>
public sealed record ItemUnsubscribeMessage(
    string Path,
    bool Recursive,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);

/// <summary>
/// Represents an acknowledgement for broker requests.
/// </summary>
/// <param name="Path">The related item path.</param>
/// <param name="Accepted">Whether the request was accepted.</param>
/// <param name="Reason">The optional rejection reason.</param>
/// <param name="SourceClientId">The broker or client id producing the acknowledgement.</param>
/// <param name="CorrelationId">The optional correlation id.</param>
/// <param name="Timestamp">The acknowledgement timestamp.</param>
public sealed record ItemBrokerAckMessage(
    string Path,
    bool Accepted,
    string? Reason,
    string? SourceClientId,
    string? CorrelationId,
    DateTimeOffset Timestamp)
    : ItemBrokerMessage(Path, SourceClientId, CorrelationId, Timestamp);
