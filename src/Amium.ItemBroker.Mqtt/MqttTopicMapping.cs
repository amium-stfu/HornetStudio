namespace Amium.ItemBroker.Mqtt;

/// <summary>
/// Describes a mapping between an MQTT topic and an item broker parameter.
/// </summary>
/// <param name="Path">The broker item path.</param>
/// <param name="ParameterName">The broker item parameter name.</param>
/// <param name="ClientId">The logical source group for reconstructed remote item trees, when available.</param>
public sealed record MqttTopicMapping(string Path, string ParameterName, string? ClientId);

/// <summary>
/// Maps broker item paths and parameters to MQTT topics.
/// </summary>
public sealed class MqttItemTopicMapper
{
    private const string ParamsSegment = "params";
    private const string SharedClientId = "shared";
    private static readonly IReadOnlyDictionary<string, string> BrokerHealthTopics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ItemBrokerHealthPaths.Heartbeat] = "broker/heartbeat",
        [ItemBrokerHealthPaths.Uptime] = "broker/uptime",
        [ItemBrokerHealthPaths.ClientCount] = "broker/clients/count",
        [ItemBrokerHealthPaths.SubscriptionCount] = "broker/subscriptions/count",
        [ItemBrokerHealthPaths.RetainedItemCount] = "broker/retained/count",
        [ItemBrokerHealthPaths.MessagesPerSecond] = "broker/messages/per-second",
        [ItemBrokerHealthPaths.DroppedMessages] = "broker/messages/dropped",
        [ItemBrokerHealthPaths.MqttTransportStatus] = "broker/mqtt/status",
    };

    private readonly string _baseTopic;
    private readonly string[] _baseSegments;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemTopicMapper"/> class.
    /// </summary>
    /// <param name="baseTopic">The base topic used for item data.</param>
    public MqttItemTopicMapper(string baseTopic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopic);
        _baseTopic = NormalizeTopic(baseTopic);
        _baseSegments = _baseTopic.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Gets the normalized base topic.
    /// </summary>
    public string BaseTopic => _baseTopic;

    /// <summary>
    /// Maps a broker item path and parameter name to an MQTT topic.
    /// </summary>
    /// <param name="path">The broker item path.</param>
    /// <param name="parameterName">The broker item parameter name.</param>
    /// <returns>The MQTT topic.</returns>
    public string ToTopic(string path, string parameterName)
        => ToTopic(path, parameterName, sourceClientId: null);

    /// <summary>
    /// Maps a broker item path and parameter name to an MQTT topic.
    /// </summary>
    /// <param name="path">The broker item path.</param>
    /// <param name="parameterName">The broker item parameter name.</param>
    /// <param name="sourceClientId">Unused for shared item topics.</param>
    /// <returns>The MQTT topic.</returns>
    public string ToTopic(string path, string parameterName, string? sourceClientId)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        var normalizedParameterName = NormalizeParameterName(parameterName);

        if (string.Equals(normalizedParameterName, "Value", StringComparison.OrdinalIgnoreCase)
            && BrokerHealthTopics.TryGetValue(normalizedPath, out var brokerTopic))
        {
            return JoinSegments(_baseSegments.Concat(brokerTopic.Split('/')));
        }

        var pathSegments = normalizedPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString);
        var segments = new List<string>(_baseSegments);
        segments.AddRange(pathSegments);

        if (!string.Equals(normalizedParameterName, "Value", StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(ParamsSegment);
            segments.Add(Uri.EscapeDataString(normalizedParameterName));
        }

        return JoinSegments(segments);
    }

    /// <summary>
    /// Tries to map an MQTT topic to a broker item path and parameter name.
    /// </summary>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="mapping">The mapped broker address when successful.</param>
    /// <returns><see langword="true"/> when the topic belongs to this mapper.</returns>
    public bool TryMapTopic(string topic, out MqttTopicMapping mapping)
    {
        mapping = null!;

        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var topicSegments = NormalizeTopic(topic)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (TryMapBrokerHealthTopic(topicSegments, out mapping))
        {
            return true;
        }

        if (topicSegments.Length <= _baseSegments.Length)
        {
            return false;
        }

        if (!HasBaseTopic(topicSegments))
        {
            return false;
        }

        var relativeSegments = topicSegments.Skip(_baseSegments.Length).ToArray();
        var parameterName = "Value";
        var parameterStartIndex = relativeSegments.Length;
        if (relativeSegments.Length > 2 && string.Equals(relativeSegments[^2], ParamsSegment, StringComparison.OrdinalIgnoreCase))
        {
            parameterStartIndex = relativeSegments.Length - 2;
            parameterName = Uri.UnescapeDataString(relativeSegments[^1]);
        }
        else if (string.Equals(relativeSegments[^1], ParamsSegment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parameterStartIndex <= 0)
        {
            return false;
        }

        var path = string.Join('.', relativeSegments
            .Take(parameterStartIndex)
            .Select(Uri.UnescapeDataString));

        mapping = new MqttTopicMapping(ItemBrokerPath.Normalize(path), NormalizeParameterName(parameterName), SharedClientId);
        return true;
    }

    private static string NormalizeTopic(string topic)
        => topic.Replace('\\', '/').Trim('/');

    private static string NormalizeParameterName(string parameterName)
        => string.IsNullOrWhiteSpace(parameterName) ? "Value" : parameterName.Trim();

    private static string JoinSegments(IEnumerable<string> segments)
        => string.Join('/', segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));

    private bool TryMapBrokerHealthTopic(string[] topicSegments, out MqttTopicMapping mapping)
    {
        mapping = null!;

        if (topicSegments.Length <= _baseSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < _baseSegments.Length; index++)
        {
            if (!string.Equals(topicSegments[index], _baseSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var relativeTopic = string.Join('/', topicSegments.Skip(_baseSegments.Length));
        var healthPath = BrokerHealthTopics.FirstOrDefault(pair => string.Equals(pair.Value, relativeTopic, StringComparison.OrdinalIgnoreCase)).Key;
        if (healthPath is null)
        {
            return false;
        }

        mapping = new MqttTopicMapping(healthPath, "Value", ClientId: null);
        return true;
    }

    private bool HasBaseTopic(string[] topicSegments)
    {
        if (topicSegments.Length <= _baseSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < _baseSegments.Length; index++)
        {
            if (!string.Equals(topicSegments[index], _baseSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
