using Amium.Item.Server;
using HornetStudio.Host;

namespace Item.Server.Monitor.Hosting;

internal sealed class MonitorHost : IAsyncDisposable
{
    private readonly InMemoryItemServer _broker = new();
    private readonly ItemServerHealthPublisher _healthPublisher;
    private readonly MonitorRegistryBridge _registryBridge;
    private readonly IReadOnlyList<MonitorAdapterRuntime> _adapters;
    private readonly Dictionary<string, MonitorAdapterRuntime> _adaptersById;
    private readonly Task _initializationTask;

    public MonitorHost()
    {
        _healthPublisher = new ItemServerHealthPublisher(
            server: _broker,
            options: new ItemServerHealthOptions
            {
                ClientId = "item-server-monitor-health",
            });
        _registryBridge = new MonitorRegistryBridge(HostRegistries.Data);

        var mqttDefinition = new MonitorAdapterDefinition(new MqttMonitorAdapterFactory());
        var mqttRuntime = new MonitorAdapterRuntime(mqttDefinition, mqttDefinition.Factory.CreateDefaultOptions(), _broker);

        _adapters = [mqttRuntime];
        _adaptersById = _adapters.ToDictionary(static adapter => adapter.Definition.Id, StringComparer.OrdinalIgnoreCase);
        _initializationTask = InitializeCoreAsync();
    }

    public IReadOnlyList<MonitorAdapterRuntime> Adapters => _adapters;

    public Task InitializeAsync() => _initializationTask;

    public async Task StartAdapterAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        await GetAdapter(adapterId).StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAdapterAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        await GetAdapter(adapterId).StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var adapter in _adapters)
        {
            await adapter.StopAsync().ConfigureAwait(false);
        }

        await _healthPublisher.DisposeAsync().ConfigureAwait(false);
        await _registryBridge.DisposeAsync().ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync()
    {
        await _healthPublisher.StartAsync().ConfigureAwait(false);
        await _registryBridge.StartAsync(_broker).ConfigureAwait(false);

        foreach (var adapter in _adapters)
        {
            await adapter.StartAsync().ConfigureAwait(false);
        }
    }

    private MonitorAdapterRuntime GetAdapter(string adapterId)
    {
        if (_adaptersById.TryGetValue(adapterId, out var adapter))
        {
            return adapter;
        }

        throw new InvalidOperationException($"Unknown adapter id '{adapterId}'.");
    }
}