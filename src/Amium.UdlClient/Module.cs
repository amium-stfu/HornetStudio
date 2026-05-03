using HornetStudio.Editor.Models;
using ItemModel = Amium.Items.Item;

namespace Amium.UdlClient;

public sealed class Module : ItemModel
{
    private const string RequestItemName = "Request";
    private const string ReadItemName = "Read";
    private const string SetItemName = "Set";
    private const string OutItemName = "Out";
    private const string StateItemName = "State";
    private const string AlertItemName = "Alert";
    private const string CommandItemName = "Command";

    

    public Module(string name, string? path = null)
        : base(name, path: path)
    {
        Params["Kind"].Value = "UdlModule";
        Params["Text"].Value = name;
        Params["Unit"].Value = string.Empty;

        AddRequestChannel(ReadItemName);
        AddRequestChannel(SetItemName);
        AddRequestChannel(OutItemName);
        AddItem(StateItemName);
        AddItem(AlertItemName);
        AddRequestChannel(CommandItemName);
    }

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

    public void EnsureWriteMetadata()
    {
        ApplyWriteMetadata(Read);
        ApplyWriteMetadata(Set);
        ApplyWriteMetadata(Out);
        ApplyWriteMetadata(Command);
    }

    private void AddRequestChannel(string name)
    {
        AddItem(name);
        var channel = this[name];
        channel.AddItem(RequestItemName);
        channel[RequestItemName].Params["Text"].Value = $"{name} Request";
        channel[RequestItemName].Value = channel.Value;
        ApplyWriteMetadata(channel);
    }

    private static void ApplyWriteMetadata(ItemModel channel)
    {
        channel.Params["Writable"].Value = true;
        channel.Params["WritePath"].Value = channel.Path ?? string.Empty;
        channel.Params["WriteMode"].Value = SignalWriteMode.Request.ToString();
    }
}
