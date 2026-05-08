using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Client.Mqtt;
using System.Text.Json;

await using var session = new MqttItemClientSession(new MqttItemClientOptions
{
    Host = "127.0.0.1",
    Port = 1883,
    ClientId = "DummyClient1",
    BaseTopic = "",
});

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var publisher = new DemoItemPublisher(session);

Console.WriteLine("Amium.Item.Server MQTT demo client");
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
    private readonly MqttItemClientSession _session;
    private readonly ItemModel _temperature;
    private readonly ItemModel _pressure;

    internal DemoItemPublisher(MqttItemClientSession session)
    {
        _session = session;
        _temperature = new ItemModel("Temperature", 22.0, "Edm1");
        _temperature.Properties["unit"].Value = "degC";
        _temperature.Properties["format"].Value = "0.0";
        _temperature["Raw"].Value = "0.0";

        _pressure = new ItemModel("Pressure", 1012.0, "Edm1");
        _pressure.Properties["unit"].Value = "hPa";
        _pressure.Properties["format"].Value = "0.0";
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

            await _session.UpdateValueAsync(_temperature, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _session.UpdateValueAsync(_pressure, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            Dictionary<string, object> meta = new()
            {
                ["value"] = temperature,
                ["unit"] = "degC",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),    
                ["source"] = "DemoItemPublisher",
            };


            _temperature.Properties["meta"].Value = JsonSerializer.Serialize(meta);
            await _session.UpdatePropertyAsync(_temperature,"meta", cancellationToken: cancellationToken).ConfigureAwait(false);
            
            
            
            Dictionary<string, object> pressureMeta = new()
            {
                ["value"] = pressure,
                ["unit"] = "hPa",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),    
                ["source"] = "DemoItemPublisher",
            };

            _pressure.Properties["meta"].Value = JsonSerializer.Serialize(pressureMeta);
            await _session.UpdatePropertyAsync(_pressure,"meta", cancellationToken: cancellationToken).ConfigureAwait(false);



            Console.Write($"\rTemperature: {temperature,5:0.0} degC | Pressure: {pressure,6:0.0} hPa");
        }
    }

    private async Task PublishSnapshotsAsync(CancellationToken cancellationToken)
    {
        await _session.PublishSnapshotAsync(_temperature, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _session.PublishSnapshotAsync(_pressure, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
