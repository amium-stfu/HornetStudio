namespace Amium.Item.Server.MultimasterDemo.Models;

/// <summary>
/// Represents the current observable state for one mesh node.
/// </summary>
/// <param name="NodeId">The stable logical node id.</param>
/// <param name="DisplayName">The node display name.</param>
/// <param name="EndpointSummary">The broker endpoint summary.</param>
/// <param name="IsConnected">Whether the local mesh services are connected.</param>
/// <param name="StatusText">The current status text.</param>
/// <param name="LocalDynamicPath">The owned dynamic item path.</param>
/// <param name="LocalDynamicValueText">The current dynamic item value text.</param>
/// <param name="LocalDynamicUpdatedUtc">The last dynamic item update timestamp.</param>
/// <param name="LocalDynamicSequence">The dynamic update sequence number.</param>
/// <param name="LocalStaticPath">The owned static item path.</param>
/// <param name="LocalStaticValueText">The current static item value text.</param>
/// <param name="LocalStaticUpdatedUtc">The last static item update timestamp.</param>
/// <param name="LocalStaticSequence">The static update sequence number.</param>
/// <param name="VisibleItemsText">A summarized text representation of visible remote items.</param>
public sealed record MeshNodeSnapshot(
    string NodeId,
    string DisplayName,
    string EndpointSummary,
    bool IsConnected,
    string StatusText,
    string LocalDynamicPath,
    string LocalDynamicValueText,
    DateTimeOffset? LocalDynamicUpdatedUtc,
    int LocalDynamicSequence,
    string LocalStaticPath,
    string LocalStaticValueText,
    DateTimeOffset? LocalStaticUpdatedUtc,
    int LocalStaticSequence,
    string VisibleItemsText);