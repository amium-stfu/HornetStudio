namespace Amium.Item.Server.MultimasterDemo.Models;

/// <summary>
/// Represents one observed item state from the perspective of a node observer.
/// </summary>
/// <param name="ObserverNodeId">The observing node id.</param>
/// <param name="Path">The observed item path.</param>
/// <param name="IsAvailable">Whether the item is currently available.</param>
/// <param name="ValueText">The formatted observed value.</param>
/// <param name="LastUpdatedUtc">The last observed update timestamp.</param>
public sealed record ObservedItemState(
    string ObserverNodeId,
    string Path,
    bool IsAvailable,
    string ValueText,
    DateTimeOffset? LastUpdatedUtc);