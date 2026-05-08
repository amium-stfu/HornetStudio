using MQTTnet.Formatter;

namespace Amium.Item.Client.Mqtt;

/// <summary>
/// Defines MQTT connection and publishing options for an item broker client session.
/// </summary>
public sealed class MqttItemClientOptions
{
    /// <summary>
    /// Gets or sets the MQTT broker host.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the MQTT broker port.
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the stable MQTT client id.
    /// </summary>
    public string ClientId { get; set; } = "item-broker-client";

    /// <summary>
    /// Gets or sets the base MQTT topic used by the item broker bridge.
    /// </summary>
    public string BaseTopic { get; set; } = "hornet";

    /// <summary>
    /// Gets or sets the MQTT protocol version.
    /// </summary>
    public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V311;

    /// <summary>
    /// Gets or sets the MQTT keep-alive period.
    /// </summary>
    public TimeSpan KeepAlivePeriod { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the reconnect delay after transient MQTT failures.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets a value indicating whether the MQTT session should be clean.
    /// </summary>
    public bool CleanSession { get; set; } = true;
}