using Amium.Item.Server;
using Amium.Items;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Server.Mqtt;

/// <summary>
/// Describes a mapping between an MQTT topic and an item broker parameter.
/// </summary>
/// <param name="Path">The broker item path.</param>
/// <param name="PropertyName">The broker item parameter name.</param>
/// <param name="ClientId">The logical source group for reconstructed remote item trees, when available.</param>
public sealed record MqttTopicMapping(string Path, string PropertyName, string? ClientId);

/// <summary>
/// Maps broker item paths and parameters to MQTT topics.
/// </summary>
public sealed class MqttItemTopicMapper
{
    private const string MetaParameterName = "meta";
    private const string ValueParameterName = "read";
    private const string SharedClientId = "shared";
    private const string SystemTopicPrefix = "$SYS";
    private static readonly IReadOnlyDictionary<string, string> ServerHealthTopics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ItemServerHealthPaths.StatusState] = SystemTopicPrefix + "/status/state",
        [ItemServerHealthPaths.StatusUptimeSeconds] = SystemTopicPrefix + "/status/uptime_seconds",
        [ItemServerHealthPaths.StatusStartedAtUtc] = SystemTopicPrefix + "/status/started_at_utc",
        [ItemServerHealthPaths.StatusLastUpdatedUtc] = SystemTopicPrefix + "/status/last_updated_utc",
        [ItemServerHealthPaths.MetricsItemCount] = SystemTopicPrefix + "/metrics/item_count",
        [ItemServerHealthPaths.MetricsMemoryWorkingSetMb] = SystemTopicPrefix + "/metrics/memory_working_set_mb",
        [ItemServerHealthPaths.MetricsMemoryManagedHeapMb] = SystemTopicPrefix + "/metrics/memory_managed_heap_mb",
        [ItemServerHealthPaths.MetricsCpuUsagePercent] = SystemTopicPrefix + "/metrics/cpu_usage_percent",
        [ItemServerHealthPaths.MqttStatusState] = SystemTopicPrefix + "/mqtt/status/state",
        [ItemServerHealthPaths.MqttStatusClientCount] = SystemTopicPrefix + "/mqtt/status/client_count",
        [ItemServerHealthPaths.MqttStatusEndpoint] = SystemTopicPrefix + "/mqtt/status/endpoint",
        [ItemServerHealthPaths.MqttStatusLastError] = SystemTopicPrefix + "/mqtt/status/last_error",
    };

    private readonly string _baseTopic;
    private readonly string[] _baseSegments;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemTopicMapper"/> class.
    /// </summary>
    /// <param name="baseTopic">The base topic used for item data.</param>
    public MqttItemTopicMapper(string baseTopic)
    {
        _baseTopic = string.IsNullOrWhiteSpace(baseTopic) ? string.Empty : NormalizeTopic(baseTopic);
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
        var normalizedPath = ItemServerPath.Normalize(path);
        var normalizedParameterName = NormalizeParameterName(parameterName);

        if (string.Equals(normalizedParameterName, ValueParameterName, StringComparison.Ordinal)
            && ServerHealthTopics.TryGetValue(normalizedPath, out var brokerTopic))
        {
            return brokerTopic;
        }

        var pathSegments = normalizedPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString);
        var segments = new List<string>(_baseSegments);
        segments.AddRange(pathSegments);

        if (!string.Equals(normalizedParameterName, MetaParameterName, StringComparison.Ordinal))
        {
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
        => TryMapTopic(topic, payload: null, mapping: out mapping);

    /// <summary>
    /// Tries to map an MQTT topic and payload to a broker item path and parameter name.
    /// </summary>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="payload">The incoming MQTT textual payload.</param>
    /// <param name="mapping">The mapped broker address when successful.</param>
    /// <returns><see langword="true"/> when the topic belongs to this mapper.</returns>
    public bool TryMapTopic(string topic, string? payload, out MqttTopicMapping mapping)
    {
        mapping = null!;

        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var topicSegments = NormalizeTopic(topic)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (TryMapServerHealthTopic(topicSegments, out mapping))
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
        if (relativeSegments.Length <= 0)
        {
            return false;
        }

        var parameterName = MetaParameterName;
        var parameterStartIndex = relativeSegments.Length;
        if (string.Equals(relativeSegments[^1], ValueParameterName, StringComparison.OrdinalIgnoreCase)
            && (payload is null || !LooksLikeJsonObjectPayload(payload)))
        {
            parameterStartIndex = relativeSegments.Length - 1;
            parameterName = ValueParameterName;
        }
        else if (payload is not null && relativeSegments.Length > 1 && !LooksLikeJsonObjectPayload(payload))
        {
            parameterStartIndex = relativeSegments.Length - 1;
            parameterName = Uri.UnescapeDataString(relativeSegments[^1]);
        }

        if (parameterStartIndex <= 0)
        {
            return false;
        }

        var path = string.Join('.', relativeSegments
            .Take(parameterStartIndex)
            .Select(Uri.UnescapeDataString));

        mapping = new MqttTopicMapping(ItemServerPath.Normalize(path), NormalizeParameterName(parameterName), SharedClientId);
        return true;
    }

    private static string NormalizeTopic(string topic)
        => topic.Replace('\\', '/').Trim('/');

    private static string NormalizeParameterName(string parameterName)
        => string.IsNullOrWhiteSpace(parameterName) ? ValueParameterName : ItemPath.ToSnakeCaseSegment(parameterName);

    private static string JoinSegments(IEnumerable<string> segments)
        => string.Join('/', segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));

    private static bool LooksLikeJsonObjectPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var trimmed = payload.Trim();
        return trimmed.StartsWith('{') && trimmed.EndsWith('}');
    }

    private bool TryMapServerHealthTopic(string[] topicSegments, out MqttTopicMapping mapping)
    {
        mapping = null!;

        var relativeTopic = string.Join('/', topicSegments);
        var healthPath = ServerHealthTopics.FirstOrDefault(pair => string.Equals(pair.Value, relativeTopic, StringComparison.OrdinalIgnoreCase)).Key;
        if (healthPath is null)
        {
            return false;
        }

        mapping = new MqttTopicMapping(healthPath, ValueParameterName, SharedClientId);
        return true;
    }

    private bool HasBaseTopic(string[] topicSegments)
    {
        if (_baseSegments.Length == 0)
        {
            return true;
        }

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