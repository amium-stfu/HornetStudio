using Amium.ItemBroker;
using Amium.ItemBroker.Mqtt;

var mqttOptions = new MqttItemBrokerOptions
{
    Enabled = true,
    Host = "127.0.0.1",
    Port = 1883,
    BaseTopic = "hornet",
};
await using var host = new MqttItemBrokerHost(mqttOptions);

Console.WriteLine("Amium.ItemBroker service host started.");
Console.WriteLine($"MQTT transport enabled on {mqttOptions.Host}:{mqttOptions.Port} with base topic '{mqttOptions.BaseTopic}'.");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

await host.StartAsync(shutdown.Token);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException)
{
}

await host.StopAsync();
Console.WriteLine("Amium.ItemBroker service host stopped.");
