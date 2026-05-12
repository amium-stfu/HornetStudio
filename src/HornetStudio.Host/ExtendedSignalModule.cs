using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Host;

public sealed class ExtendedSignalModule : ItemModel
{
    private const string RequestItemName = "request";
    private const string RawItemName = "raw";
    private const string ReadItemName = "read";
    private const string SetItemName = "set";
    private const string OutItemName = "out";
    private const string StateItemName = "state";
    private const string AlertItemName = "alert";
    private const string CommandItemName = "command";
    private const string ConfigItemName = "config";

    public ExtendedSignalModule(string name, string? path = null)
        : base(name, path: path)
    {
        Properties["kind"].Value = "ExtendedSignalModule";
        Properties["text"].Value = name;
        Properties["unit"].Value = string.Empty;

        AddItem(RawItemName);
        AddRequestChannel(ReadItemName);
        AddRequestChannel(SetItemName);
        AddRequestChannel(OutItemName);
        AddItem(StateItemName);
        AddItem(AlertItemName);
        AddRequestChannel(CommandItemName);
        AddItem(ConfigItemName);
    }

    public ItemModel Raw => this[RawItemName];

    public ItemModel Read => this[ReadItemName];

    public ItemModel ReadRequest => Read[RequestItemName];

    public ItemModel Set => this[SetItemName];

    public ItemModel SetRequest => Set[RequestItemName];

    public ItemModel Out => this[OutItemName];

    public ItemModel OutRequest => Out[RequestItemName];

    public ItemModel State => this[StateItemName];

    public ItemModel Alert => this[AlertItemName];

    public ItemModel Command => this[CommandItemName];

    public ItemModel CommandRequest => Command[RequestItemName];

    public ItemModel Config => this[ConfigItemName];

    private void AddRequestChannel(string name)
    {
        AddItem(name);
        var channel = this[name];
        channel.AddItem(RequestItemName);
        channel[RequestItemName].Properties["text"].Value = $"{name} Request";
        channel[RequestItemName].Value = channel.Value;
    }
}
