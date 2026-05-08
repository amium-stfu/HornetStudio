namespace Amium.Item.Server;

/// <summary>
/// Configures core item server health publishing.
/// </summary>
public sealed class ItemServerHealthOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether core health publishing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the retained source client id used for health items.
    /// </summary>
    public string ClientId { get; set; } = "amium-item-server-health";

    /// <summary>
    /// Gets or sets the interval used for periodic health updates.
    /// </summary>
    public TimeSpan PublishInterval { get; set; } = TimeSpan.FromSeconds(1);
}