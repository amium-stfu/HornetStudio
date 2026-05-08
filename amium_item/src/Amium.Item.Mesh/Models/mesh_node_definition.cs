namespace Amium.Item.Server.MultimasterDemo.Models;

/// <summary>
/// Describes one mesh node endpoint and its default item paths.
/// </summary>
/// <param name="NodeId">The stable logical node id.</param>
/// <param name="DisplayName">The display name for diagnostics and UIs.</param>
/// <param name="Host">The MQTT broker host.</param>
/// <param name="Port">The MQTT broker port.</param>
/// <param name="BaseTopic">The node-specific MQTT base topic.</param>
/// <param name="DynamicItemName">The dynamic item name published by the node.</param>
/// <param name="StaticItemName">The static writable item name published by the node.</param>
public sealed record MeshNodeDefinition(
    string NodeId,
    string DisplayName,
    string Host,
    int Port,
    string BaseTopic,
    string DynamicItemName,
    string StaticItemName)
{
    /// <summary>
    /// Gets the broker endpoint summary.
    /// </summary>
    public string EndpointSummary => $"{Host}:{Port} / {BaseTopic}";

    /// <summary>
    /// Gets the default dynamic item path for this node.
    /// </summary>
    public string DynamicItemPath => $"nodes.{NodeId}.{DynamicItemName}";

    /// <summary>
    /// Gets the default static item path for this node.
    /// </summary>
    public string StaticItemPath => $"nodes.{NodeId}.{StaticItemName}";
}