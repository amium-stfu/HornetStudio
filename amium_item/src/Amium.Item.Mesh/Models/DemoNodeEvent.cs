namespace Amium.Item.Server.MultimasterDemo.Models;

/// <summary>
/// Represents a timestamped mesh node event entry.
/// </summary>
/// <param name="NodeId">The emitting node id.</param>
/// <param name="DisplayName">The node display name.</param>
/// <param name="TimestampUtc">The event timestamp in UTC.</param>
/// <param name="Message">The event message.</param>
public sealed record DemoNodeEvent(
    string NodeId,
    string DisplayName,
    DateTimeOffset TimestampUtc,
    string Message);