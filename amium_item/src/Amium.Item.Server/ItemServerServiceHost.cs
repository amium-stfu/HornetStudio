using Amium.Item.Server.Mqtt;

namespace Amium.Item.Server;

/// <summary>
/// Provides the former service-host startup flow from inside the item server project.
/// </summary>
public static class ItemServerServiceHost
{
    /// <summary>
    /// Runs the default MQTT item server host until cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous host lifetime.</returns>
    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var mqttOptions = new MqttItemServerOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = 1883,
            BaseTopic = string.Empty,
        };

        await using var host = new MqttItemServerHost(mqttOptions);

        Console.WriteLine("Amium.Item.Server service host started.");
        Console.WriteLine($"MQTT transport enabled on {mqttOptions.Host}:{mqttOptions.Port} with base topic '{mqttOptions.BaseTopic}'.");

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await host.StopAsync().ConfigureAwait(false);
        Console.WriteLine("Amium.Item.Server service host stopped.");
    }
}
