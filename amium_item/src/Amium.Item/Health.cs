namespace Amium.Item.Server;

/// <summary>
/// Defines standard item broker health paths.
/// </summary>
public static class ItemServerHealthPaths
{
    /// <summary>
    /// Gets the root path for item broker system data.
    /// </summary>
    public const string Root = "sys";

    /// <summary>
    /// Gets the service state path.
    /// </summary>
    public const string StatusState = Root + ".status.state";

    /// <summary>
    /// Gets the service uptime path.
    /// </summary>
    public const string StatusUptimeSeconds = Root + ".status.uptime_seconds";

    /// <summary>
    /// Gets the service start timestamp path.
    /// </summary>
    public const string StatusStartedAtUtc = Root + ".status.started_at_utc";

    /// <summary>
    /// Gets the service last updated timestamp path.
    /// </summary>
    public const string StatusLastUpdatedUtc = Root + ".status.last_updated_utc";

    /// <summary>
    /// Gets the retained non-system item count metric path.
    /// </summary>
    public const string MetricsItemCount = Root + ".metrics.item_count";

    /// <summary>
    /// Gets the process working set memory metric path.
    /// </summary>
    public const string MetricsMemoryWorkingSetMb = Root + ".metrics.memory_working_set_mb";

    /// <summary>
    /// Gets the managed heap memory metric path.
    /// </summary>
    public const string MetricsMemoryManagedHeapMb = Root + ".metrics.memory_managed_heap_mb";

    /// <summary>
    /// Gets the process CPU usage metric path.
    /// </summary>
    public const string MetricsCpuUsagePercent = Root + ".metrics.cpu_usage_percent";

    /// <summary>
    /// Gets the MQTT server state path.
    /// </summary>
    public const string MqttStatusState = Root + ".mqtt.status.state";

    /// <summary>
    /// Gets the MQTT connected client count path.
    /// </summary>
    public const string MqttStatusClientCount = Root + ".mqtt.status.client_count";

    /// <summary>
    /// Gets the MQTT server endpoint path.
    /// </summary>
    public const string MqttStatusEndpoint = Root + ".mqtt.status.endpoint";

    /// <summary>
    /// Gets the MQTT last error path.
    /// </summary>
    public const string MqttStatusLastError = Root + ".mqtt.status.last_error";
}

/// <summary>
/// Describes item broker health values for normal broker publishing.
/// </summary>
/// <param name="State">The service state.</param>
/// <param name="UptimeSeconds">The service uptime in seconds.</param>
/// <param name="StartedAtUtc">The service start timestamp.</param>
/// <param name="LastUpdatedUtc">The last system data update timestamp.</param>
/// <param name="ItemCount">The retained non-system server item count.</param>
/// <param name="MemoryWorkingSetMb">The process working set memory in megabytes.</param>
/// <param name="MemoryManagedHeapMb">The managed heap memory in megabytes.</param>
/// <param name="CpuUsagePercent">The process CPU usage percentage.</param>
/// <param name="MqttState">The MQTT server state.</param>
/// <param name="MqttClientCount">The connected MQTT client count.</param>
/// <param name="MqttEndpoint">The MQTT server endpoint.</param>
/// <param name="MqttLastError">The last MQTT server error.</param>
public sealed record ItemServerHealthSnapshot(
    string State,
    double UptimeSeconds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastUpdatedUtc,
    int ItemCount,
    double MemoryWorkingSetMb,
    double MemoryManagedHeapMb,
    double CpuUsagePercent,
    string MqttState,
    int MqttClientCount,
    string MqttEndpoint,
    string MqttLastError);