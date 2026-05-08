namespace Amium.Item.Server;

/// <summary>
/// Defines how the broker should retain published data.
/// </summary>
public enum ItemRetentionMode
{
    /// <summary>
    /// The message should not update retained state.
    /// </summary>
    NotRetained,

    /// <summary>
    /// The latest known message or snapshot should be retained.
    /// </summary>
    LatestOnly,

    /// <summary>
    /// The message should be retained until its time to live expires.
    /// </summary>
    TimeToLive,
}

/// <summary>
/// Defines how a publisher should send data to the broker.
/// </summary>
public enum ItemPublishMode
{
    /// <summary>
    /// Publish a full item snapshot.
    /// </summary>
    Snapshot,

    /// <summary>
    /// Publish only changed values or parameters.
    /// </summary>
    Delta,

    /// <summary>
    /// Keep only the latest pending value when publishing faster than consumers can receive it.
    /// </summary>
    LatestOnly,

    /// <summary>
    /// Publish the latest value at a throttled interval.
    /// </summary>
    ThrottledLatest,

    /// <summary>
    /// Publish the latest value at a cyclic interval.
    /// </summary>
    CyclicInterval,
}

/// <summary>
/// Describes broker-side retention settings for a published path.
/// </summary>
/// <param name="Mode">The retention mode.</param>
/// <param name="TimeToLive">The optional retention time to live.</param>
public sealed record ItemRetentionPolicy(ItemRetentionMode Mode, TimeSpan? TimeToLive = null)
{
    /// <summary>
    /// Gets the default policy for normal item data.
    /// </summary>
    public static ItemRetentionPolicy Default { get; } = new(ItemRetentionMode.LatestOnly);

    /// <summary>
    /// Gets the policy for transient messages.
    /// </summary>
    public static ItemRetentionPolicy Transient { get; } = new(ItemRetentionMode.NotRetained);
}

/// <summary>
/// Describes client-side publish preferences for a path.
/// </summary>
/// <param name="Mode">The preferred publish mode.</param>
/// <param name="Interval">The optional throttle or cyclic interval.</param>
public sealed record ItemPublishPolicy(ItemPublishMode Mode, TimeSpan? Interval = null)
{
    /// <summary>
    /// Gets the default publish policy for item snapshots and deltas.
    /// </summary>
    public static ItemPublishPolicy Default { get; } = new(ItemPublishMode.Delta);
}

/// <summary>
/// Describes the result of a retention policy decision.
/// </summary>
/// <param name="ShouldRetain">Whether the broker should update retained state.</param>
/// <param name="Mode">The selected retention mode.</param>
/// <param name="ExpiresAt">The optional expiration timestamp.</param>
public sealed record ItemRetentionDecision(bool ShouldRetain, ItemRetentionMode Mode, DateTimeOffset? ExpiresAt);

/// <summary>
/// Describes the result of a publish policy decision.
/// </summary>
/// <param name="Mode">The selected publish mode.</param>
/// <param name="ShouldPublish">Whether a message should be published now.</param>
public sealed record ItemPublishDecision(ItemPublishMode Mode, bool ShouldPublish);

/// <summary>
/// Resolves broker-side retention policy for published messages.
/// </summary>
public interface IItemRetentionPolicyResolver
{
    /// <summary>
    /// Resolves the retention decision for a broker message.
    /// </summary>
    /// <param name="message">The published message.</param>
    /// <param name="timestamp">The broker timestamp used for TTL calculations.</param>
    /// <returns>The retention decision.</returns>
    ItemRetentionDecision Resolve(ItemServerMessage message, DateTimeOffset timestamp);
}

/// <summary>
/// Resolves client-side publish policy decisions.
/// </summary>
public interface IItemPublishPolicyResolver
{
    /// <summary>
    /// Resolves the publish decision for a path and message shape.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <param name="isSnapshotRequired">Whether the client must publish a full snapshot.</param>
    /// <returns>The publish decision.</returns>
    ItemPublishDecision Resolve(string path, bool isSnapshotRequired);
}

/// <summary>
/// Provides default broker retention decisions.
/// </summary>
public sealed class DefaultItemRetentionPolicyResolver : IItemRetentionPolicyResolver
{
    private static readonly TimeSpan DefaultHealthTimeToLive = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public ItemRetentionDecision Resolve(ItemServerMessage message, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is ItemRemoveMessage or ItemWriteRequestMessage or ItemServerAckMessage)
        {
            return new ItemRetentionDecision(ShouldRetain: false, ItemRetentionMode.NotRetained, ExpiresAt: null);
        }

        var normalizedPath = ItemServerPath.Normalize(message.Path);
        if (string.Equals(normalizedPath, ItemServerHealthPaths.Root, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(ItemServerHealthPaths.Root + ".", StringComparison.OrdinalIgnoreCase))
        {
            return new ItemRetentionDecision(ShouldRetain: true, ItemRetentionMode.TimeToLive, timestamp.Add(DefaultHealthTimeToLive));
        }

        return new ItemRetentionDecision(ShouldRetain: true, ItemRetentionMode.LatestOnly, ExpiresAt: null);
    }
}

/// <summary>
/// Provides default client publish decisions.
/// </summary>
public sealed class DefaultItemPublishPolicyResolver : IItemPublishPolicyResolver
{
    /// <inheritdoc />
    public ItemPublishDecision Resolve(string path, bool isSnapshotRequired)
    {
        _ = ItemServerPath.Normalize(path);
        return isSnapshotRequired
            ? new ItemPublishDecision(ItemPublishMode.Snapshot, ShouldPublish: true)
            : new ItemPublishDecision(ItemPublishMode.Delta, ShouldPublish: true);
    }
}
