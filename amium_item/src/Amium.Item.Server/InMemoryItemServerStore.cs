using System.Collections.Concurrent;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server;

/// <summary>
/// Stores retained item broker snapshots in memory.
/// </summary>
public sealed class InMemoryItemServerStore : IItemServerStore
{
    private readonly ConcurrentDictionary<string, ItemSnapshotMessage> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the retained snapshot count.
    /// </summary>
    public int Count => _snapshots.Count;

    /// <summary>
    /// Counts retained snapshots that are outside the specified root path.
    /// </summary>
    /// <param name="excludedRootPath">The root path to exclude.</param>
    /// <returns>The retained snapshot count outside the excluded root.</returns>
    public int CountExcluding(string excludedRootPath)
    {
        var normalizedExcludedRootPath = ItemServerPath.Normalize(excludedRootPath);
        return _snapshots.Keys.Count(path => !ItemServerPath.IsSelfOrDescendant(normalizedExcludedRootPath, path));
    }

    /// <inheritdoc />
    public void UpsertSnapshot(ItemSnapshotMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var path = ItemServerPath.Normalize(message.Path);
        _snapshots[path] = message with { Path = path };
    }

    /// <inheritdoc />
    public void UpdateValue(ItemValueChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var path = ItemServerPath.Normalize(message.Path);
        var snapshot = _snapshots.GetOrAdd(path, _ => CreateSnapshot(path, message.SourceClientId, message.CorrelationId, message.Timestamp));
        object? existingValue = snapshot.ItemModel.Value;
        if (ItemServerValueCoercion.TryConvertForExistingValue(message.Value, existingValue, out object? convertedValue))
        {
            snapshot.ItemModel.Value = convertedValue!;
        }
    }

    /// <inheritdoc />
    public void UpdateParameter(ItemPropertyChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.PropertyName);

        var path = ItemServerPath.Normalize(message.Path);
        var snapshot = _snapshots.GetOrAdd(path, _ => CreateSnapshot(path, message.SourceClientId, message.CorrelationId, message.Timestamp));
        var parameter = snapshot.ItemModel.Properties[ItemPath.ToSnakeCaseSegment(message.PropertyName)];
        object? existingValue = parameter.Value;
        if (ItemServerValueCoercion.TryConvertForExistingValue(message.Value, existingValue, out object? convertedValue))
        {
            parameter.Value = convertedValue!;
        }
    }

    /// <inheritdoc />
    public void Remove(string path)
    {
        var normalizedPath = ItemServerPath.Normalize(path);
        foreach (var key in _snapshots.Keys)
        {
            if (ItemServerPath.IsSelfOrDescendant(normalizedPath, key))
            {
                _snapshots.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ItemSnapshotMessage> GetRetained(string path, bool recursive)
    {
        var normalizedPath = ItemServerPath.Normalize(path);
        return _snapshots
            .Values
            .Where(snapshot => ItemServerPath.Matches(normalizedPath, snapshot.Path, recursive))
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
