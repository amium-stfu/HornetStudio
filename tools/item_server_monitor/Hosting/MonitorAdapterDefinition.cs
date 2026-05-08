namespace Item.Server.Monitor.Hosting;

internal sealed class MonitorAdapterDefinition
{
    public MonitorAdapterDefinition(IMonitorAdapterFactory factory)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string Id => Factory.AdapterId;

    public string DisplayName => Factory.DisplayName;

    public string Description => Factory.Description;

    public IMonitorAdapterFactory Factory { get; }
}