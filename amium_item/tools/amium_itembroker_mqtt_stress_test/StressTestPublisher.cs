using Amium.Item.Client.Mqtt;

namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Publishes deterministic random stress signal updates through the MQTT ItemBroker client session.
/// </summary>
public sealed class StressTestPublisher : IAsyncDisposable
{
    private readonly StressTestRunSettings _settings;
    private readonly RandomSignalCatalog _catalog;
    private readonly StressTestMetrics _metrics;
    private readonly MqttItemClientSession _session;
    private readonly Dictionary<string, long> _sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new(1337);

    /// <summary>
    /// Initializes a new instance of the <see cref="StressTestPublisher"/> class.
    /// </summary>
    /// <param name="settings">The run settings.</param>
    /// <param name="catalog">The generated signal catalog.</param>
    /// <param name="metrics">The metrics sink.</param>
    /// <param name="runId">The unique run identifier.</param>
    public StressTestPublisher(
        StressTestRunSettings settings,
        RandomSignalCatalog catalog,
        StressTestMetrics metrics,
        string runId)
    {
        _settings = settings;
        _catalog = catalog;
        _metrics = metrics;
        RunId = runId;
        _session = new MqttItemClientSession(
            new MqttItemClientOptions
            {
                Host = settings.Host,
                Port = settings.Port,
                ClientId = "stress-publisher-" + runId,
                BaseTopic = settings.BaseTopic,
            });
    }

    /// <summary>
    /// Gets the unique run identifier.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// Connects to the broker and publishes the steady stress load until stopped or the duration expires.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);

        var interval = TimeSpan.FromSeconds(1.0 / _settings.MessagesPerSecond);
        var startedAt = DateTimeOffset.UtcNow;
        var nextPublishAt = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested
               && DateTimeOffset.UtcNow - startedAt < _settings.Duration)
        {
            await PublishNextAsync(cancellationToken).ConfigureAwait(false);
            nextPublishAt += interval;

            var delay = nextPublishAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else if (delay < TimeSpan.FromSeconds(-1))
            {
                nextPublishAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => _session.DisposeAsync();

    private async Task PublishNextAsync(CancellationToken cancellationToken)
    {
        var signal = _catalog.NextSignal();
        var sequence = _sequences.TryGetValue(signal.Path, out var currentSequence) ? currentSequence + 1 : 1;
        _sequences[signal.Path] = sequence;

        var value = Math.Round(signal.InitialValue + ((_random.NextDouble() - 0.5) * 10.0), 3);
        var payload = StressTestPayload.Create(
            runId: RunId,
            sequence: sequence,
            value: value);
        signal.ItemModel.Value = payload.Format();

        await _session.UpdateValueAsync(
            item: signal.ItemModel,
            retained: _settings.Retained,
            correlationId: RunId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _metrics.RecordPublished();
    }
}
