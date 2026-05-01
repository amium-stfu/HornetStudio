using Amium.ItemBroker;
using Amium.ItemBroker.Mqtt;

var broker = new InMemoryItemBroker();
var mqttOptions = new MqttItemBrokerOptions
{
    Enabled = true,
    Host = "127.0.0.1",
    Port = 1883,
    BaseTopic = "hornet",
};
await using var mqttAdapter = new MqttItemBrokerAdapter(mqttOptions);

Console.WriteLine("Amium.ItemBroker service host started.");
Console.WriteLine($"MQTT transport enabled on {mqttOptions.Host}:{mqttOptions.Port} with base topic '{mqttOptions.BaseTopic}'.");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

await mqttAdapter.StartAsync(broker, shutdown.Token);
var healthTask = PublishHealthAsync(broker, mqttOptions, shutdown.Token);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException)
{
}

await mqttAdapter.StopAsync();
try
{
    await healthTask;
}
catch (OperationCanceledException)
{
}
GC.KeepAlive(broker);
Console.WriteLine("Amium.ItemBroker service host stopped.");

static async Task PublishHealthAsync(IItemBroker broker, MqttItemBrokerOptions mqttOptions, CancellationToken cancellationToken)
{
    var startedAt = DateTimeOffset.UtcNow;
    var heartbeat = false;

    while (!cancellationToken.IsCancellationRequested)
    {
        heartbeat = !heartbeat;
        var now = DateTimeOffset.UtcNow;
        await broker.PublishValueChangedAsync(new ItemValueChangedMessage(ItemBrokerHealthPaths.Heartbeat, heartbeat, mqttOptions.ClientId, null, now), cancellationToken);
        await broker.PublishValueChangedAsync(new ItemValueChangedMessage(ItemBrokerHealthPaths.Uptime, (now - startedAt).TotalSeconds, mqttOptions.ClientId, null, now), cancellationToken);
        await broker.PublishValueChangedAsync(new ItemValueChangedMessage(ItemBrokerHealthPaths.MqttTransportStatus, "Running", mqttOptions.ClientId, null, now), cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }
}
