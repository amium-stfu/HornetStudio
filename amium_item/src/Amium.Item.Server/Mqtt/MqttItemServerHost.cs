using Amium.Item.Server;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server.Mqtt;

/// <summary>
/// Provides a reusable selfhosted MQTT Item Server host for device and service scenarios.
/// </summary>
public sealed class MqttItemServerHost : IAsyncDisposable
{
    private readonly MqttItemServerOptions _options;
    private readonly ItemServerHealthPublisher _coreHealthPublisher;
    private readonly PeriodicTimer? _mqttHealthTimer;
    private readonly InMemoryItemServer _ownedServer;
    private readonly bool _ownsServer;
    private Task? _mqttHealthLoopTask;
    private CancellationTokenSource? _mqttHealthCancellation;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemServerHost"/> class.
    /// </summary>
    /// <param name="options">The MQTT host options.</param>
    /// <param name="broker">The optional broker instance. When omitted, an in-memory broker is created and owned by the host.</param>
    public MqttItemServerHost(MqttItemServerOptions options, IItemServer? broker = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _ownedServer = broker as InMemoryItemServer ?? new InMemoryItemServer();
        _ownsServer = broker is null;
        Broker = broker ?? _ownedServer;
        Adapter = new MqttItemServerAdapter(options);
        _coreHealthPublisher = new ItemServerHealthPublisher(
            server: Broker,
            options: new ItemServerHealthOptions
            {
                Enabled = options.PublishHealth,
                ClientId = options.ClientId,
                PublishInterval = options.HealthPublishInterval,
            },
            retainedItemCountProvider: () => Adapter.VisibleItemCount);
        _mqttHealthTimer = options.PublishHealth ? new PeriodicTimer(options.HealthPublishInterval) : null;
    }

    /// <summary>
    /// Gets the broker exposed by the selfhosted MQTT host.
    /// </summary>
    public IItemServer Broker { get; }

    /// <summary>
    /// Gets the MQTT adapter used by the host.
    /// </summary>
    public MqttItemServerAdapter Adapter { get; }

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

        await _coreHealthPublisher.StartAsync(cancellationToken).ConfigureAwait(false);
        await PublishMqttHealthSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        _mqttHealthCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mqttHealthLoopTask = RunMqttHealthLoopAsync(_mqttHealthCancellation.Token);
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

        if (_mqttHealthCancellation is not null)
        {
            await _mqttHealthCancellation.CancelAsync().ConfigureAwait(false);
            _mqttHealthCancellation.Dispose();
            _mqttHealthCancellation = null;
        }

        if (_mqttHealthLoopTask is not null)
        {
            try
            {
                await _mqttHealthLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _mqttHealthLoopTask = null;
        }

        await _coreHealthPublisher.StopAsync(cancellationToken).ConfigureAwait(false);

        await Adapter.StopAsync(cancellationToken).ConfigureAwait(false);
        IsRunning = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Adapter.DisposeAsync().ConfigureAwait(false);

        if (_ownsServer)
        {
            GC.KeepAlive(_ownedServer);
        }

        _mqttHealthTimer?.Dispose();
    }

    private async Task RunMqttHealthLoopAsync(CancellationToken cancellationToken)
    {
        await PublishMqttHealthAsync(cancellationToken).ConfigureAwait(false);
        while (_mqttHealthTimer is not null && await _mqttHealthTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await PublishMqttHealthAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishMqttHealthSnapshotsAsync(CancellationToken cancellationToken)
    {
        await Broker.PublishSnapshotAsync(
            item: new ItemModel("State", "Starting").Repath(ItemServerHealthPaths.MqttStatusState),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.PublishSnapshotAsync(
            item: new ItemModel("ClientCount", 0).Repath(ItemServerHealthPaths.MqttStatusClientCount),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.PublishSnapshotAsync(
            item: new ItemModel("Endpoint", $"{_options.Host}:{_options.Port}").Repath(ItemServerHealthPaths.MqttStatusEndpoint),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.PublishSnapshotAsync(
            item: new ItemModel("LastError", string.Empty).Repath(ItemServerHealthPaths.MqttStatusLastError),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishMqttHealthAsync(CancellationToken cancellationToken)
    {
        await Broker.UpdateValueAsync(
            item: new ItemModel("State", "Running").Repath(ItemServerHealthPaths.MqttStatusState),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.UpdateValueAsync(
            item: new ItemModel("ClientCount", Adapter.ConnectedExternalClientCount).Repath(ItemServerHealthPaths.MqttStatusClientCount),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.UpdateValueAsync(
            item: new ItemModel("Endpoint", $"{_options.Host}:{_options.Port}").Repath(ItemServerHealthPaths.MqttStatusEndpoint),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Broker.UpdateValueAsync(
            item: new ItemModel("LastError", string.Empty).Repath(ItemServerHealthPaths.MqttStatusLastError),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
