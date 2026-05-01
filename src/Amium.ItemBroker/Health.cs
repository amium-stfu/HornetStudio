namespace Amium.ItemBroker;

/// <summary>
/// Defines standard item broker health paths.
/// </summary>
public static class ItemBrokerHealthPaths
{
    /// <summary>
    /// Gets the root path for item broker health data.
    /// </summary>
    public const string Root = "Runtime.Health.ItemBroker";

    /// <summary>
    /// Gets the broker heartbeat path.
    /// </summary>
    public const string Heartbeat = Root + ".Heartbeat";

    /// <summary>
    /// Gets the broker uptime path.
    /// </summary>
    public const string Uptime = Root + ".Uptime";

    /// <summary>
    /// Gets the connected client count path.
    /// </summary>
    public const string ClientCount = Root + ".ClientCount";

    /// <summary>
    /// Gets the active subscription count path.
    /// </summary>
    public const string SubscriptionCount = Root + ".SubscriptionCount";

    /// <summary>
    /// Gets the retained item count path.
    /// </summary>
    public const string RetainedItemCount = Root + ".RetainedItemCount";

    /// <summary>
    /// Gets the messages per second path.
    /// </summary>
    public const string MessagesPerSecond = Root + ".MessagesPerSecond";

    /// <summary>
    /// Gets the dropped messages path.
    /// </summary>
    public const string DroppedMessages = Root + ".DroppedMessages";

    /// <summary>
    /// Gets the MQTT transport status path.
    /// </summary>
    public const string MqttTransportStatus = Root + ".Transport.Mqtt.Status";
}

/// <summary>
/// Describes item broker health values for normal broker publishing.
/// </summary>
/// <param name="Heartbeat">The current heartbeat value.</param>
/// <param name="Uptime">The broker uptime.</param>
/// <param name="ClientCount">The connected client count.</param>
/// <param name="SubscriptionCount">The active subscription count.</param>
/// <param name="RetainedItemCount">The retained item count.</param>
/// <param name="MessagesPerSecond">The current message rate.</param>
/// <param name="DroppedMessages">The number of dropped messages.</param>
public sealed record ItemBrokerHealthSnapshot(
    bool Heartbeat,
    TimeSpan Uptime,
    int ClientCount,
    int SubscriptionCount,
    int RetainedItemCount,
    double MessagesPerSecond,
    long DroppedMessages);
