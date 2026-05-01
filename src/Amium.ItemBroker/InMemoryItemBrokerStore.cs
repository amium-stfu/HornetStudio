using System.Collections.Concurrent;
using Amium.Item;
using ItemModel = Amium.Item.Item;

namespace Amium.ItemBroker;

/// <summary>
/// Stores retained item broker snapshots in memory.
/// </summary>
public sealed class InMemoryItemBrokerStore : IItemBrokerStore
{
    private readonly ConcurrentDictionary<string, ItemSnapshotMessage> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void UpsertSnapshot(ItemSnapshotMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var path = ItemBrokerPath.Normalize(message.Path);
        _snapshots[path] = message with { Path = path };
    }

    /// <inheritdoc />
    public void UpdateValue(ItemValueChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var path = ItemBrokerPath.Normalize(message.Path);
        var snapshot = _snapshots.GetOrAdd(path, _ => CreateSnapshot(path, message.SourceClientId, message.CorrelationId, message.Timestamp));
        object? existingValue = snapshot.Item.Value;
        if (ItemBrokerValueCoercion.TryConvertForExistingValue(message.Value, existingValue, out object? convertedValue))
        {
            snapshot.Item.Value = convertedValue!;
        }
    }

    /// <inheritdoc />
    public void UpdateParameter(ItemParameterChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.ParameterName);

        var path = ItemBrokerPath.Normalize(message.Path);
        var snapshot = _snapshots.GetOrAdd(path, _ => CreateSnapshot(path, message.SourceClientId, message.CorrelationId, message.Timestamp));
        var parameter = snapshot.Item.Params[message.ParameterName];
        object? existingValue = parameter.Value;
        if (ItemBrokerValueCoercion.TryConvertForExistingValue(message.Value, existingValue, out object? convertedValue))
        {
            parameter.Value = convertedValue!;
        }
    }

    /// <inheritdoc />
    public void Remove(string path)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        foreach (var key in _snapshots.Keys)
        {
            if (ItemBrokerPath.IsSelfOrDescendant(normalizedPath, key))
            {
                _snapshots.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ItemSnapshotMessage> GetRetained(string path, bool recursive)
    {
        var normalizedPath = ItemBrokerPath.Normalize(path);
        return _snapshots
            .Values
            .Where(snapshot => ItemBrokerPath.Matches(normalizedPath, snapshot.Path, recursive))
            .OrderBy(snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ItemSnapshotMessage CreateSnapshot(string path, string? sourceClientId, string? correlationId, DateTimeOffset timestamp)
    {
        var item = new ItemModel(GetLastSegment(path)).Repath(path);
        return new ItemSnapshotMessage(path, item, sourceClientId, correlationId, timestamp);
    }

    private static string GetLastSegment(string path)
    {
        var lastSeparatorIndex = path.LastIndexOf('.');
        return lastSeparatorIndex >= 0 ? path[(lastSeparatorIndex + 1)..] : path;
    }
}
