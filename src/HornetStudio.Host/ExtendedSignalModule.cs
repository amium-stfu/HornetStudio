using Amium.Items;

namespace HornetStudio.Host;

public sealed class ExtendedSignalModule : Item
{
    private const string RequestItemName = "Request";
    private const string RawItemName = "Raw";
    private const string ReadItemName = "Read";
    private const string SetItemName = "Set";
    private const string OutItemName = "Out";
    private const string StateItemName = "State";
    private const string AlertItemName = "Alert";
    private const string CommandItemName = "Command";
    private const string ConfigItemName = "Config";

    public ExtendedSignalModule(string name, string? path = null)
        : base(name, path: path)
    {
        Params["Kind"].Value = "ExtendedSignalModule";
        Params["Text"].Value = name;
        Params["Unit"].Value = string.Empty;

        AddItem(RawItemName);
        AddRequestChannel(ReadItemName);
        AddRequestChannel(SetItemName);
        AddRequestChannel(OutItemName);
        AddItem(StateItemName);
        AddItem(AlertItemName);
        AddRequestChannel(CommandItemName);
        AddItem(ConfigItemName);
    }

    public Item Raw => this[RawItemName];

    public Item Read => this[ReadItemName];

    public Item ReadRequest => Read[RequestItemName];

    public Item Set => this[SetItemName];

    public Item SetRequest => Set[RequestItemName];

    public Item Out => this[OutItemName];

    public Item OutRequest => Out[RequestItemName];

    public Item State => this[StateItemName];

    public Item Alert => this[AlertItemName];

    public Item Command => this[CommandItemName];

    public Item CommandRequest => Command[RequestItemName];

    public Item Config => this[ConfigItemName];

    private void AddRequestChannel(string name)
    {
        AddItem(name);
        var channel = this[name];
        channel.AddItem(RequestItemName);
        channel[RequestItemName].Params["Text"].Value = $"{name} Request";
        channel[RequestItemName].Value = channel.Value;
    }
}
