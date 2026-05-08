namespace Item.Server.Monitor.Hosting;

internal sealed class MonitorAdapterOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string BaseTopic { get; set; } = string.Empty;

    public MonitorAdapterOptions Clone()
        => new()
        {
            Host = Host,
            Port = Port,
            BaseTopic = BaseTopic,
        };
}