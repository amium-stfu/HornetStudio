namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Defines validated settings for one MQTT stress test run.
/// </summary>
/// <param name="Host">The MQTT broker host.</param>
/// <param name="Port">The MQTT broker port.</param>
/// <param name="BaseTopic">The ItemBroker MQTT base topic.</param>
/// <param name="StressRootPath">The root item path used for generated stress signals.</param>
/// <param name="SignalCount">The number of generated signals.</param>
/// <param name="MessagesPerSecond">The target steady publish rate.</param>
/// <param name="Duration">The requested run duration.</param>
/// <param name="Retained">A value indicating whether stress messages should be retained.</param>
public sealed record StressTestRunSettings(
    string Host,
    int Port,
    string BaseTopic,
    string StressRootPath,
    int SignalCount,
    int MessagesPerSecond,
    TimeSpan Duration,
    bool Retained)
{
    /// <summary>
    /// Creates validated stress test settings from raw UI input.
    /// </summary>
    /// <param name="host">The MQTT broker host.</param>
    /// <param name="port">The MQTT broker port.</param>
    /// <param name="baseTopic">The ItemBroker MQTT base topic.</param>
    /// <param name="stressRootPath">The stress root item path.</param>
    /// <param name="signalCount">The number of generated signals.</param>
    /// <param name="messagesPerSecond">The target steady publish rate.</param>
    /// <param name="durationSeconds">The run duration in seconds.</param>
    /// <param name="retained">A value indicating whether MQTT retain should be enabled.</param>
    /// <returns>The validated run settings.</returns>
    /// <exception cref="ArgumentException">Thrown when a setting is invalid.</exception>
    public static StressTestRunSettings Create(
        string host,
        int port,
        string baseTopic,
        string stressRootPath,
        int signalCount,
        int messagesPerSecond,
        int durationSeconds,
        bool retained)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.", nameof(port));
        }

        if (string.IsNullOrWhiteSpace(stressRootPath))
        {
            throw new ArgumentException("Stress root is required.", nameof(stressRootPath));
        }

        if (signalCount is < 1 or > 1_000_000)
        {
            throw new ArgumentException("Signal count must be between 1 and 1000000.", nameof(signalCount));
        }

        if (messagesPerSecond is < 1 or > 1_000_000)
        {
            throw new ArgumentException("Message rate must be between 1 and 1000000.", nameof(messagesPerSecond));
        }

        if (durationSeconds is < 1 or > 86_400)
        {
            throw new ArgumentException("Duration must be between 1 and 86400 seconds.", nameof(durationSeconds));
        }

        return new StressTestRunSettings(
            Host: host.Trim(),
            Port: port,
            BaseTopic: baseTopic.Trim().Trim('/'),
            StressRootPath: NormalizePath(stressRootPath),
            SignalCount: signalCount,
            MessagesPerSecond: messagesPerSecond,
            Duration: TimeSpan.FromSeconds(durationSeconds),
            Retained: retained);
    }

    private static string NormalizePath(string path)
        => string.Join('.', path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
