using System.Collections.Generic;
using System.Linq;
using Amium.Items;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;

namespace Item.Server.Monitor.Monitoring;

public sealed class MonitorItemSource : IDisposable
{
    private readonly IDataRegistry _dataRegistry;
    private bool _disposed;

    public MonitorItemSource(IDataRegistry dataRegistry)
    {
        _dataRegistry = dataRegistry ?? throw new ArgumentNullException(nameof(dataRegistry));
        _dataRegistry.ItemChanged += OnRegistryChanged;
        _dataRegistry.RegistryChanged += OnRegistryChanged;
    }

    public event EventHandler? SourceInvalidated;

    public IReadOnlyList<MonitorItemSnapshot> CaptureSnapshot()
    {
        var snapshots = new Dictionary<string, MonitorItemSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _dataRegistry.GetKeysByCapability(DataRegistryItemCapabilities.Display)
                     .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (!_dataRegistry.TryResolve(key, out var rootItem) || rootItem is null)
            {
                continue;
            }

            foreach (var snapshot in EnumerateSnapshots(rootItem, DataChangeKind.SnapshotUpserted))
            {
                snapshots[snapshot.Path] = snapshot;
            }
        }

        return snapshots.Values
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dataRegistry.ItemChanged -= OnRegistryChanged;
        _dataRegistry.RegistryChanged -= OnRegistryChanged;
    }

    private void OnRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!_dataRegistry.TryGetMetadata(e.Key, out var metadata)
            || !metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Display))
        {
            return;
        }

        SourceInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<MonitorItemSnapshot> EnumerateSnapshots(ItemModel item, DataChangeKind changeKind)
    {
        var path = string.IsNullOrWhiteSpace(item.Path) ? item.Name ?? string.Empty : item.Path!;
        if (!string.IsNullOrWhiteSpace(path))
        {
            yield return new MonitorItemSnapshot(path, item.Clone(), changeKind, GetLatestTimestamp(item));
        }

        foreach (var child in item.GetDictionary().Values)
        {
            foreach (var childSnapshot in EnumerateSnapshots(child, changeKind))
            {
                yield return childSnapshot;
            }
        }
    }

    private static ulong GetLatestTimestamp(ItemModel item)
    {
        var latest = 0UL;
        foreach (var property in item.Properties.GetDictionary().Values)
        {
            if (property.LastUpdate > latest)
            {
                latest = property.LastUpdate;
            }
        }

        return latest;
    }
}