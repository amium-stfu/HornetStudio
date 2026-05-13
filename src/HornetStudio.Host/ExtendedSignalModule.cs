using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Host;

public sealed class ExtendedSignalModule : ItemModel
{
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

        AddChannel(RawItemName);
        AddChannel(ReadItemName, hasWriteChannel: true);
        AddChannel(SetItemName, hasWriteChannel: true);
        AddChannel(OutItemName, hasWriteChannel: true);
        AddChannel(StateItemName, hasWriteChannel: true);
        AddChannel(AlertItemName);
        AddChannel(CommandItemName, hasWriteChannel: true);
        AddItem(ConfigItemName);
    }

    public ItemModel Raw => this[RawItemName];

    public ItemModel Read => this[ReadItemName];

    public ItemModel Set => this[SetItemName];

    public ItemModel Out => this[OutItemName];

    public ItemModel State => this[StateItemName];

    public ItemModel Alert => this[AlertItemName];

    public ItemModel Command => this[CommandItemName];

    public ItemModel Config => this[ConfigItemName];

    private void AddChannel(string name, bool hasWriteChannel = false)
    {
        this[name] = new ItemModel(
            name,
            path: Path,
            hasWriteChannel: hasWriteChannel);
    }
}
