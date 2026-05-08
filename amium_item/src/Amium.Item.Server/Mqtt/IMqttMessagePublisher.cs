namespace Amium.Item.Server.Mqtt;

/// <summary>
/// Publishes MQTT messages for the item broker adapter.
/// </summary>
public interface IMqttMessagePublisher
{
    /// <summary>
    /// Publishes a message to an MQTT topic.
    /// </summary>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="payload">The textual payload.</param>
    /// <param name="retain">Whether the message should be retained.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken = default);
}
