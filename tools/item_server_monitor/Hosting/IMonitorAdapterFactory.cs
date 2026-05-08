using Amium.Item.Server;

namespace Item.Server.Monitor.Hosting;

internal interface IMonitorAdapterFactory
{
    string AdapterId { get; }

    string DisplayName { get; }

    string Description { get; }

    MonitorAdapterOptions CreateDefaultOptions();

    IItemServerTransport CreateTransport(MonitorAdapterOptions options);

    string FormatEndpoint(MonitorAdapterOptions options);
}