using System.Collections.Generic;
using System.Linq;

namespace Item.Server.Monitor.Monitoring;

public sealed class MonitorSnapshotStore
{
    private Dictionary<string, MonitorItemSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _snapshots.Count;

    public IReadOnlyCollection<MonitorItemSnapshot> Items => _snapshots.Values;

    public void ReplaceAll(IEnumerable<MonitorItemSnapshot> snapshots)
    {
        _snapshots = snapshots
            .GroupBy(static snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string path, out MonitorItemSnapshot? snapshot)
        => _snapshots.TryGetValue(path, out snapshot);

    public IReadOnlyCollection<string> GetPaths()
        => _snapshots.Keys
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}