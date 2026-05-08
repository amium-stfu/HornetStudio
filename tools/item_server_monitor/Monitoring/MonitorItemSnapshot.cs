using HornetStudio.Host;
using ItemModel = Amium.Items.Item;

namespace Item.Server.Monitor.Monitoring;

public sealed record MonitorItemSnapshot(
    string Path,
    ItemModel ItemModel,
    DataChangeKind ChangeKind,
    ulong Timestamp);