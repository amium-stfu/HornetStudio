namespace Amium.Item.Server;

/// <summary>
/// Defines how slow clients should be handled.
/// </summary>
public enum SlowClientDropPolicy
{
    /// <summary>
    /// Do not drop messages.
    /// </summary>
    None,

    /// <summary>
    /// Drop older queued values and keep the latest value.
    /// </summary>
    DropOldestKeepLatest,

    /// <summary>
    /// Drop new values while the client queue is full.
    /// </summary>
    DropNewest,
}

/// <summary>
/// Describes subscription behavior for high-throughput consumers.
/// </summary>
public sealed record ItemSubscriptionOptions
{
    /// <summary>
    /// Gets the default subscription options.
    /// </summary>
    public static ItemSubscriptionOptions Default { get; } = new();

    /// <summary>
    /// Gets whether descendant paths should be included.
    /// </summary>
    public bool Recursive { get; init; }

    /// <summary>
    /// Gets whether retained snapshots should be delivered immediately.
    /// </summary>
    public bool IncludeRetained { get; init; } = true;

    /// <summary>
    /// Gets the optional maximum update rate in messages per second.
    /// </summary>
    public double? MaxUpdateRate { get; init; }

    /// <summary>
    /// Gets the optional batch interval.
    /// </summary>
    public TimeSpan? BatchInterval { get; init; }

    /// <summary>
    /// Gets whether only the latest pending value should be kept.
    /// </summary>
    public bool KeepLatest { get; init; }

    /// <summary>
    /// Gets the slow-client drop policy.
    /// </summary>
    public SlowClientDropPolicy SlowClientDropPolicy { get; init; } = SlowClientDropPolicy.None;
}