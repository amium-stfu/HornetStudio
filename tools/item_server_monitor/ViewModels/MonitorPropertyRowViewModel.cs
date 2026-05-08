namespace Item.Server.Monitor.ViewModels;

public sealed class MonitorPropertyRowViewModel
{
    public MonitorPropertyRowViewModel(string name, string valueText)
    {
        Name = name;
        ValueText = valueText;
    }

    public string Name { get; }

    public string ValueText { get; }
}