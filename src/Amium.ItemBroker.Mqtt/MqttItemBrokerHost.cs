using Amium.ItemBroker;
using Amium.Items;

namespace Amium.ItemBroker.Mqtt;

/// <summary>
/// Provides a reusable selfhosted MQTT ItemBroker host for device and service scenarios.
/// </summary>
public sealed class MqttItemBrokerHost : IAsyncDisposable
{
    private readonly MqttItemBrokerOptions _options;
    private readonly PeriodicTimer _healthTimer;
    private readonly InMemoryItemBroker _ownedBroker;
    private readonly bool _ownsBroker;
    private Task? _healthLoopTask;
    private CancellationTokenSource? _healthCancellation;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemBrokerHost"/> class.
    /// </summary>
    /// <param name="options">The MQTT host options.</param>
    /// <param name="broker">The optional broker instance. When omitted, an in-memory broker is created and owned by the host.</param>
    public MqttItemBrokerHost(MqttItemBrokerOptions options, IItemBroker? broker = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _ownedBroker = broker as InMemoryItemBroker ?? new InMemoryItemBroker();
        _ownsBroker = broker is null;
        Broker = broker ?? _ownedBroker;
        Adapter = new MqttItemBrokerAdapter(options);
        _healthTimer = new PeriodicTimer(options.HealthPublishInterval);
    }

    /// <summary>
    /// Gets the broker exposed by the selfhosted MQTT host.
    /// </summary>
    public IItemBroker Broker { get; }

    /// <summary>
    /// Gets the MQTT adapter used by the host.
    /// </summary>
    public MqttItemBrokerAdapter Adapter { get; }

    /// <summary>
    /// Gets a value indicating whether the host is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts the selfhosted MQTT host and optional broker health publishing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        await Adapter.StartAsync(Broker, cancellationToken).ConfigureAwait(false);
        IsRunning = true;

        if (!_options.PublishHealth)
        {
            return;
        }

        await PublishHealthSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        _healthCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _healthLoopTask = RunHealthLoopAsync(_healthCancellation.Token);
    }

    /// <summary>
    /// Stops the selfhosted MQTT host.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        if (_healthCancellation is not null)
        {
            await _healthCancellation.CancelAsync().ConfigureAwait(false);
            _healthCancellation.Dispose();
            _healthCancellation = null;
        }

        if (_healthLoopTask is not null)
        {
            try
            {
                await _healthLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _healthLoopTask = null;
        }

        await Adapter.StopAsync(cancellationToken).ConfigureAwait(false);
        IsRunning = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Adapter.DisposeAsync().ConfigureAwait(false);

        if (_ownsBroker)
        {
            GC.KeepAlive(_ownedBroker);
        }

        _healthTimer.Dispose();
    }

    private async Task RunHealthLoopAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var heartbeat = false;

        await PublishHealthAsync(startedAt, heartbeat, cancellationToken).ConfigureAwait(false);
        while (await _healthTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            heartbeat = !heartbeat;
            await PublishHealthAsync(startedAt, heartbeat, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishHealthSnapshotsAsync(CancellationToken cancellationToken)
    {
        await Broker.PublishSnapshotAsync(
            item: new Item("Heartbeat", false).Repath(ItemBrokerHealthPaths.Heartbeat),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.PublishSnapshotAsync(
            item: new Item("Uptime", 0.0).Repath(ItemBrokerHealthPaths.Uptime),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.PublishSnapshotAsync(
            item: new Item("Status", "Starting").Repath(ItemBrokerHealthPaths.MqttTransportStatus),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishHealthAsync(DateTimeOffset startedAt, bool heartbeat, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await Broker.UpdateValueAsync(
            item: new Amium.Items.Item("Heartbeat", heartbeat).Repath(ItemBrokerHealthPaths.Heartbeat),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.UpdateValueAsync(
            item: new Amium.Items.Item("Uptime", (now - startedAt).TotalSeconds).Repath(ItemBrokerHealthPaths.Uptime),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.UpdateValueAsync(
            item: new Amium.Items.Item("Status", "Running").Repath(ItemBrokerHealthPaths.MqttTransportStatus),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
