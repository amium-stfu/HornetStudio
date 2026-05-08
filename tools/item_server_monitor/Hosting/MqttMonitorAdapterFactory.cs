using Amium.Item.Server;
using Amium.Item.Server.Mqtt;

namespace Item.Server.Monitor.Hosting;

internal sealed class MqttMonitorAdapterFactory : IMonitorAdapterFactory
{
    public string AdapterId => "mqtt";

    public string DisplayName => "MQTT";

    public string Description => "Embedded MQTT transport for inspecting the local item server runtime.";

    public MonitorAdapterOptions CreateDefaultOptions()
        => new()
        {
            Host = "127.0.0.1",
            Port = 1883,
            BaseTopic = string.Empty,
        };

    public IItemServerTransport CreateTransport(MonitorAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new MqttItemServerAdapter(new MqttItemServerOptions
        {
            Enabled = true,
            Host = options.Host,
            Port = options.Port,
            BaseTopic = options.BaseTopic,
            ClientId = "item-server-monitor-mqtt",
            SubscriptionRootPath = "runtime",
            AllowObservedInboundPublishes = true,
            PublishHealth = false,
        });
    }

    public string FormatEndpoint(MonitorAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"{options.Host}:{options.Port}";
    }
}