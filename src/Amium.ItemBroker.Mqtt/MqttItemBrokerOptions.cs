namespace Amium.ItemBroker.Mqtt;

/// <summary>
/// Configures the MQTT item broker transport adapter.
/// </summary>
public sealed class MqttItemBrokerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the MQTT adapter is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the local MQTT bind address.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the MQTT TCP port.
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the MQTT base topic for broker-owned state.
    /// </summary>
    public string BaseTopic { get; set; } = "hornet";

    /// <summary>
    /// Gets or sets the adapter client id used for broker messages.
    /// </summary>
    public string ClientId { get; set; } = "amium-itembroker-mqtt";

    /// <summary>
    /// Gets or sets the broker subscription root path mirrored to MQTT.
    /// </summary>
    public string SubscriptionRootPath { get; set; } = "Runtime";
}
