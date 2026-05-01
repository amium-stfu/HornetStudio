using Amium.Item;
using Amium.ItemBroker.Mqtt.Client;

await using var session = new MqttItemBrokerClientSession(new MqttItemBrokerClientOptions
{
    Host = "127.0.0.1",
    Port = 1883,
    ClientId = "DummyClient1",
    BaseTopic = "hornet",
});

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var publisher = new DemoItemPublisher(session);

Console.WriteLine("Amium.ItemBroker MQTT demo client");
Console.WriteLine("Publishing two demo items to MQTT at 10 Hz. Press Ctrl+C to stop.");

try
{
    await publisher.RunAsync(cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
}

Console.WriteLine("Demo client stopped.");

internal sealed class DemoItemPublisher
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(100);
    private readonly MqttItemBrokerClientSession _session;
    private readonly Item _temperature;
    private readonly Item _pressure;

    internal DemoItemPublisher(MqttItemBrokerClientSession session)
    {
        _session = session;
        _temperature = new Item("Temperature", 22.0, "Edm1");
        _temperature.Params["Unit"].Value = "degC";
        _temperature.Params["Format"].Value = "0.0";
        _temperature["Raw"].Value = "0.0";

        _pressure = new Item("Pressure", 1012.0, "Edm1");
        _pressure.Params["Unit"].Value = "hPa";
        _pressure.Params["Format"].Value = "0.0";
        _pressure["Raw"].Value = "0.0";
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await PublishSnapshotsAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(UpdateInterval);
        var startedAt = DateTimeOffset.UtcNow;

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            var temperature = Math.Round(22.5 + Math.Sin(elapsed.TotalSeconds) * 2.5, digits: 1);
            var pressure = Math.Round(1010.0 + Math.Cos(elapsed.TotalSeconds * 0.7) * 10.0, digits: 1);

            _temperature.Value = temperature;
            _pressure.Value = pressure;

            await _session.PublishValueAsync(_temperature, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _session.PublishValueAsync(_pressure, cancellationToken: cancellationToken).ConfigureAwait(false);

            Console.Write($"\rTemperature: {temperature,5:0.0} degC | Pressure: {pressure,6:0.0} hPa");
        }
    }

    private async Task PublishSnapshotsAsync(CancellationToken cancellationToken)
    {
        await _session.PublishItemAsync(_temperature, cancellationToken: cancellationToken, retained: true).ConfigureAwait(false);
        await _session.PublishItemAsync(_pressure, cancellationToken: cancellationToken, retained: true).ConfigureAwait(false);
    }
}
