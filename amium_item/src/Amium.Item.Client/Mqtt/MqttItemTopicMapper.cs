namespace Amium.Item.Client.Mqtt;

/// <summary>
/// Maps item broker paths and parameters to MQTT topics for client sessions.
/// </summary>
public sealed class MqttItemTopicMapper
{
    private readonly Amium.Item.Server.Mqtt.MqttItemTopicMapper _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemTopicMapper"/> class.
    /// </summary>
    /// <param name="baseTopic">The base topic used for item data.</param>
    public MqttItemTopicMapper(string baseTopic)
    {
        _inner = new Amium.Item.Server.Mqtt.MqttItemTopicMapper(baseTopic);
    }

    /// <summary>
    /// Gets the normalized base topic.
    /// </summary>
    public string BaseTopic => _inner.BaseTopic;

    /// <summary>
    /// Gets the MQTT topic filter used for all shared item data.
    /// </summary>
    public string ItemSubscriptionTopic => string.IsNullOrWhiteSpace(BaseTopic) ? "#" : $"{BaseTopic}/#";

    /// <summary>
    /// Maps a broker item path and parameter name to an MQTT topic.
    /// </summary>
    /// <param name="path">The broker item path.</param>
    /// <param name="parameterName">The broker item parameter name.</param>
    /// <param name="sourceClientId">Unused for shared item topics.</param>
    /// <returns>The MQTT topic.</returns>
    public string ToTopic(string path, string parameterName, string? sourceClientId)
        => _inner.ToTopic(path, parameterName, sourceClientId);

    /// <summary>
    /// Tries to map an MQTT topic to a broker item path and parameter name.
    /// </summary>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="mapping">The mapped broker address when successful.</param>
    /// <returns><see langword="true"/> when the topic belongs to this mapper.</returns>
    public bool TryMapTopic(string topic, out Amium.Item.Server.Mqtt.MqttTopicMapping mapping)
        => _inner.TryMapTopic(topic, out mapping);

    /// <summary>
    /// Tries to map an MQTT topic and payload to a broker item path and parameter name.
    /// </summary>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="payload">The incoming MQTT textual payload.</param>
    /// <param name="mapping">The mapped broker address when successful.</param>
    /// <returns><see langword="true"/> when the topic belongs to this mapper.</returns>
    public bool TryMapTopic(string topic, string? payload, out Amium.Item.Server.Mqtt.MqttTopicMapping mapping)
        => _inner.TryMapTopic(topic, payload, out mapping);
}