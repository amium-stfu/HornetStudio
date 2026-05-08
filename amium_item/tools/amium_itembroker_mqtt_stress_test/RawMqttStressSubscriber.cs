using System.Text;
using Amium.Item.Server.Mqtt;
using MQTTnet;

namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Subscribes directly to raw MQTT stress messages and forwards parsed payloads to metrics.
/// </summary>
public sealed class RawMqttStressSubscriber : IAsyncDisposable
{
    private const string ValueParameterName = "read";
    private readonly StressTestRunSettings _settings;
    private readonly StressTestMetrics _metrics;
    private readonly string _runId;
    private readonly MqttItemTopicMapper _topicMapper;
    private readonly MqttClientFactory _factory = new();
    private IMqttClient? _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawMqttStressSubscriber"/> class.
    /// </summary>
    /// <param name="settings">The run settings.</param>
    /// <param name="metrics">The metrics sink.</param>
    /// <param name="runId">The expected run identifier.</param>
    public RawMqttStressSubscriber(
        StressTestRunSettings settings,
        StressTestMetrics metrics,
        string runId)
    {
        _settings = settings;
        _metrics = metrics;
        _runId = runId;
        _topicMapper = new MqttItemTopicMapper(settings.BaseTopic);
    }

    /// <summary>
    /// Connects the raw MQTT subscriber and subscribes to the stress topic filter.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client = _factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;

        var options = new MqttClientOptionsBuilder()
            .WithClientId("stress-subscriber-" + _runId)
            .WithTcpServer(_settings.Host, _settings.Port)
            .WithCleanSession()
            .Build();

        var result = await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"MQTT connection was rejected by the broker: {result.ResultCode}.");
        }

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(CreateStressTopicFilter())
            .Build();
        await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is null)
        {
            return;
        }

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }

        _client.Dispose();
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var payloadText = args.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : Encoding.UTF8.GetString(args.ApplicationMessage.Payload);

        if (!_topicMapper.TryMapTopic(args.ApplicationMessage.Topic, payloadText, out var mapping)
            || !mapping.Path.StartsWith(_settings.StressRootPath + ".", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mapping.PropertyName, ValueParameterName, StringComparison.OrdinalIgnoreCase)
            || !StressTestPayload.TryParse(payloadText, out var payload)
            || !string.Equals(payload.RunId, _runId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _metrics.RecordReceived(mapping.Path, payload);
        return Task.CompletedTask;
    }

    private string CreateStressTopicFilter()
    {
        var rootTopic = _topicMapper.ToTopic(_settings.StressRootPath, parameterName: "meta");
        return string.IsNullOrWhiteSpace(rootTopic) ? "#" : rootTopic + "/#";
    }
}
