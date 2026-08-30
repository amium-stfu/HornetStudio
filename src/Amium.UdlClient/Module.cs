using HornetStudio.Editor.Models;
using Amium.Items;


namespace Amium.UdlClient;

public sealed class Module : Item
{
    public Module(string name, string? path = null)
        : base(name, path: path)
    {
        Properties["kind"].Value = "UdlModule";
        Properties["text"].Value = name;
        Properties["unit"].Value = string.Empty;

        this["read"] = new Item(name: "read",path: Path, hasWriteChannel: true);
        this["set"] = new Item(name: "set",path: Path, hasWriteChannel: true);
        this["out"] = new Item(name: "out",path: Path, hasWriteChannel: true);
        this["state"] = new Item(name: "state",path: Path, hasWriteChannel: true);
        this["alert"] = new Item(name: "alert",path: Path, hasWriteChannel: false);
    }

    public Item Read => this["read"];
    public Item Set => this["set"];
    public Item Out => this["out"];
    public Item State => this["state"];
    public Item Alert => this["alert"];

}
