using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HornetStudio.Logging;
using Amium.Item;
using HornetStudio.Host.Helpers;
using AForge.Video.DirectShow;

namespace HornetStudio.Host;

public enum DataChangeKind
{
    SnapshotUpserted,
    ValueUpdated,
    ParameterUpdated
}

/// <summary>
/// Describes the broad role of an item stored in the host data registry.
/// </summary>
public enum DataRegistryItemRole
{
    Data,
    Command,
    Configuration,
    Status,
    Diagnostic,
    System
}

/// <summary>
/// Describes how an item stored in the host data registry may be used by Studio features.
/// </summary>
[Flags]
public enum DataRegistryItemCapabilities
{
    None = 0,
    Display = 1 << 0,
    BrokerPublish = 1 << 1,
    BrokerAttach = 1 << 2,
    UdlAttach = 1 << 3,
    Log = 1 << 4,
    DebugInspect = 1 << 5
}

/// <summary>
/// Contains host-side metadata for an item stored in the data registry.
/// </summary>
/// <param name="Role">The item role.</param>
/// <param name="Capabilities">The item capabilities.</param>
public sealed record DataRegistryItemMetadata(DataRegistryItemRole Role, DataRegistryItemCapabilities Capabilities)
{
    /// <summary>
    /// Gets conservative metadata for items without an explicit classification.
    /// </summary>
    public static DataRegistryItemMetadata Default { get; } = System();

    /// <summary>
    /// Creates metadata for user-visible data that can be published or attached.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata PublicData()
        => new(
            DataRegistryItemRole.Data,
            DataRegistryItemCapabilities.Display
            | DataRegistryItemCapabilities.BrokerPublish
            | DataRegistryItemCapabilities.BrokerAttach
            | DataRegistryItemCapabilities.UdlAttach
            | DataRegistryItemCapabilities.Log
            | DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for user-visible data received from a broker.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata BrokerReceivedData()
        => new(
            DataRegistryItemRole.Data,
            DataRegistryItemCapabilities.Display
            | DataRegistryItemCapabilities.BrokerAttach
            | DataRegistryItemCapabilities.UdlAttach
            | DataRegistryItemCapabilities.Log
            | DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for user-visible commands that can be published.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata PublicCommand()
        => new(
            DataRegistryItemRole.Command,
            DataRegistryItemCapabilities.Display
            | DataRegistryItemCapabilities.BrokerPublish
            | DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for widget-internal operational data.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata WidgetInternal()
        => new(DataRegistryItemRole.System, DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for widget status data.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata WidgetStatus()
        => new(DataRegistryItemRole.Status, DataRegistryItemCapabilities.Display | DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for diagnostic data.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata Diagnostic()
        => new(DataRegistryItemRole.Diagnostic, DataRegistryItemCapabilities.Display | DataRegistryItemCapabilities.DebugInspect);

    /// <summary>
    /// Creates metadata for system-owned data.
    /// </summary>
    /// <returns>The metadata instance.</returns>
    public static DataRegistryItemMetadata System()
        => new(DataRegistryItemRole.System, DataRegistryItemCapabilities.DebugInspect);
}

/// <summary>
/// Provides the central policy for protected host registry parameters.
/// </summary>
public static class HostRegistryParameterPolicy
{
    private static readonly HashSet<string> ProtectedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Writable",
        "IsWritable",
        "WritePath",
        "BrokerPath",
        "LocalPath",
        "Active",
        "PublishMode",
        "PublishIntervalMs"
    };

    /// <summary>
    /// Determines whether the parameter is protected system metadata.
    /// </summary>
    /// <param name="parameterName">The parameter name to evaluate.</param>
    /// <returns><see langword="true"/> when the parameter is protected; otherwise, <see langword="false"/>.</returns>
    public static bool IsProtectedParameter(string? parameterName)
        => !string.IsNullOrWhiteSpace(parameterName) && ProtectedParameters.Contains(parameterName.Trim());

    /// <summary>
    /// Determines whether the parameter may be shown in user-facing pickers.
    /// </summary>
    /// <param name="parameterName">The parameter name to evaluate.</param>
    /// <returns><see langword="true"/> when the parameter may be shown; otherwise, <see langword="false"/>.</returns>
    public static bool CanShowInUserPicker(string? parameterName)
        => !IsProtectedParameter(parameterName);

    /// <summary>
    /// Determines whether user- or remote-driven code may write the parameter.
    /// </summary>
    /// <param name="parameterName">The parameter name to evaluate.</param>
    /// <returns><see langword="true"/> when the parameter may be written; otherwise, <see langword="false"/>.</returns>
    public static bool CanUserWriteParameter(string? parameterName)
        => !IsProtectedParameter(parameterName);
}

/// <summary>
/// Provides data for item registry change notifications.
/// </summary>
public sealed class DataChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataChangedEventArgs"/> class.
    /// </summary>
    /// <param name="key">The canonical item path or registry key that changed.</param>
    /// <param name="item">The item that changed.</param>
    /// <param name="changeKind">The kind of change that occurred.</param>
    /// <param name="parameterName">The changed parameter name, if any.</param>
    /// <param name="timestamp">The update timestamp in Unix milliseconds.</param>
    public DataChangedEventArgs(string key, Item item, DataChangeKind changeKind, string? parameterName = null, ulong? timestamp = null)
    {
        Key = key;
        Item = item;
        ChangeKind = changeKind;
        ParameterName = parameterName;
        Timestamp = timestamp ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public string Key { get; }
    public Item Item { get; }
    public DataChangeKind ChangeKind { get; }
    public string? ParameterName { get; }
    public ulong Timestamp { get; }
}

/// <summary>
/// Stores and resolves host data items by root keys and canonical item paths.
/// </summary>
public interface IDataRegistry
{
    event EventHandler<DataChangedEventArgs>? ItemChanged;
    event EventHandler<DataChangedEventArgs>? RegistryChanged;

    IReadOnlyCollection<string> GetAllKeys();
    /// <summary>
    /// Gets an item by exact root registry key.
    /// </summary>
    /// <param name="key">The root registry key.</param>
    /// <param name="value">The root item when the key exists.</param>
    /// <returns><see langword="true"/> when the root key exists; otherwise, <see langword="false"/>.</returns>
    bool TryGet(string key, out Item? value);
    /// <summary>
    /// Resolves a root or descendant item path using canonical item path rules.
    /// </summary>
    /// <param name="path">The item path to resolve.</param>
    /// <param name="item">The resolved item when the path exists.</param>
    /// <returns><see langword="true"/> when an item was resolved; otherwise, <see langword="false"/>.</returns>
    bool TryResolve(string path, out Item? item);
    /// <summary>
    /// Gets metadata for an exact root registry key.
    /// </summary>
    /// <param name="key">The root registry key.</param>
    /// <param name="metadata">The metadata when present.</param>
    /// <returns><see langword="true"/> when metadata was resolved; otherwise, <see langword="false"/>.</returns>
    bool TryGetMetadata(string key, out DataRegistryItemMetadata metadata);
    /// <summary>
    /// Gets root keys whose metadata includes the requested capability.
    /// </summary>
    /// <param name="capability">The capability to match.</param>
    /// <returns>The matching root keys.</returns>
    IReadOnlyCollection<string> GetKeysByCapability(DataRegistryItemCapabilities capability);
    /// <summary>
    /// Gets root keys whose metadata has the requested role.
    /// </summary>
    /// <param name="role">The role to match.</param>
    /// <returns>The matching root keys.</returns>
    IReadOnlyCollection<string> GetKeysByRole(DataRegistryItemRole role);
    Item UpsertSnapshot(string key, Item snapshot, bool pruneMissingMembers = false);
    Item UpsertSnapshot(string key, Item snapshot, DataRegistryItemMetadata metadata, bool pruneMissingMembers = false);
    bool UpdateValue(string key, object? value, ulong? timestamp = null);
    bool UpdateParameter(string key, string parameterName, object? value, ulong? timestamp = null);
    /// <summary>
    /// Updates a parameter through the guarded user-write path.
    /// </summary>
    /// <param name="key">The item path or registry key.</param>
    /// <param name="parameterName">The target parameter name.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="timestamp">The optional update timestamp in Unix milliseconds.</param>
    /// <returns><see langword="true"/> when the parameter was updated; otherwise, <see langword="false"/>.</returns>
    bool TryUpdateUserParameter(string key, string parameterName, object? value, ulong? timestamp = null);
    bool Remove(string key);
}

/// <summary>
/// Stores host data snapshots and resolves root or descendant item paths.
/// </summary>
public sealed class DataRegistry : IDataRegistry
{
    private const string StudioRootSegment = "Studio";
    private static readonly string[] LegacyProjectRootSegments = ["Project", "UdlProject", "UdlBook"];
    private readonly ConcurrentDictionary<string, Item> _items = new();
    private readonly ConcurrentDictionary<string, DataRegistryItemMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IndexedItem> _pathIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _rootIndexPaths = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<DataChangedEventArgs>? ItemChanged;
    public event EventHandler<DataChangedEventArgs>? RegistryChanged;

    public IReadOnlyCollection<string> GetAllKeys() => _items.Keys.ToArray();

    public bool TryGet(string key, out Item? value) => _items.TryGetValue(key, out value);

    /// <inheritdoc />
    public bool TryGetMetadata(string key, out DataRegistryItemMetadata metadata)
    {
        if (TryGetStoredRootKey(key, out var storedRootKey) && _metadata.TryGetValue(storedRootKey, out metadata!))
        {
            return true;
        }

        metadata = DataRegistryItemMetadata.Default;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetKeysByCapability(DataRegistryItemCapabilities capability)
        => _items.Keys
            .Where(key => HasCapability(key, capability))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetKeysByRole(DataRegistryItemRole role)
        => _items.Keys
            .Where(key => TryGetMetadata(key, out var metadata) && metadata.Role == role)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <inheritdoc />
    public bool TryResolve(string path, out Item? item)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            item = null;
            return false;
        }

        if (_items.TryGetValue(path, out item) && item is not null)
        {
            return true;
        }

        var comparablePath = NormalizeComparablePath(path);
        if (!string.IsNullOrWhiteSpace(comparablePath) && _pathIndex.TryGetValue(comparablePath, out var indexed))
        {
            item = indexed.Item;
            return true;
        }

        var exactRootKey = _items.Keys
            .FirstOrDefault(key => string.Equals(NormalizeComparablePath(key), comparablePath, StringComparison.OrdinalIgnoreCase));
        if (exactRootKey is not null && _items.TryGetValue(exactRootKey, out item) && item is not null)
        {
            return true;
        }

        var rootKey = _items.Keys
            .Where(key => TryGetRelativePath(path, key, out _))
            .OrderByDescending(key => SplitPathSegments(key).Count)
            .ThenByDescending(key => key.Length)
            .FirstOrDefault();

        if (rootKey is null || !_items.TryGetValue(rootKey, out var rootItem) || rootItem is null)
        {
            item = null;
            return false;
        }

        if (!TryGetRelativePath(path, rootKey, out var relativePath))
        {
            item = null;
            return false;
        }

        return TryResolveRelativeChild(rootItem, relativePath, out item);
    }

    public Item UpsertSnapshot(string key, Item snapshot, bool pruneMissingMembers = false)
        => UpsertSnapshot(key, snapshot, DataRegistryItemMetadata.Default, pruneMissingMembers);

    /// <inheritdoc />
    public Item UpsertSnapshot(string key, Item snapshot, DataRegistryItemMetadata metadata, bool pruneMissingMembers = false)
    {
        var added = false;
        var item = _items.AddOrUpdate(
            key,
            _ =>
            {
                added = true;
                return snapshot;
            },
            (_, existing) =>
            {
                MergeItem(existing, snapshot, pruneMissingMembers);
                return existing;
            });
        _metadata[key] = metadata ?? DataRegistryItemMetadata.Default;

        if (added)
        {
            // Neue Wurzeln im Registry-Log behalten, Updates dagegen still halten,
            // um Lograuschen bei hochfrequenten Runtime-Updates zu vermeiden.
            HostLogger.Log.Information("[DataRegistry] Added key={Key} itemPath={Path} name={Name}", key, snapshot.Path ?? string.Empty, snapshot.Name ?? string.Empty);
        }

        ReindexRoot(key, item);

        RaiseItemChanged(GetEventKey(key, item), item, DataChangeKind.SnapshotUpserted);

        if (added)
        {
            RaiseRegistryChanged(GetEventKey(key, item), item, DataChangeKind.SnapshotUpserted);
        }

        return item;
    }

    public bool UpdateValue(string key, object? value, ulong? timestamp = null)
    {
        if (!TryResolve(key, out var item) || item is null)
        {
            return false;
        }

        if (!TryConvertForExistingValue(value, item.Value, out object? convertedValue))
        {
            HostLogger.Log.Warning(
                "[DataRegistry] Failed to convert value for item update. key={Key} targetType={TargetType} valueType={ValueType}",
                key,
                item.Value?.GetType().FullName ?? "<null>",
                value?.GetType().FullName ?? "<null>");
            return false;
        }

        item.Value = convertedValue!;
        if (timestamp.HasValue && item.Params.Has("Value"))
        {
            item.Params["Value"].LastUpdate = timestamp.Value;
        }

        RaiseItemChanged(GetEventKey(key, item), item, DataChangeKind.ValueUpdated, timestamp: timestamp);
        return true;
    }

    public bool UpdateParameter(string key, string parameterName, object? value, ulong? timestamp = null)
    {
        if (!TryResolve(key, out var item) || item is null || !item.Params.Has(parameterName))
        {
            return false;
        }

        var parameter = item.Params[parameterName];
        if (!TryConvertForExistingValue(value, parameter.Value, out object? convertedValue))
        {
            HostLogger.Log.Warning(
                "[DataRegistry] Failed to convert value for parameter update. key={Key} parameter={Parameter} targetType={TargetType} valueType={ValueType}",
                key,
                parameterName,
                parameter.Value?.GetType().FullName ?? "<null>",
                value?.GetType().FullName ?? "<null>");
            return false;
        }

        parameter.Value = convertedValue!;
        if (timestamp.HasValue)
        {
            parameter.LastUpdate = timestamp.Value;
        }

        RaiseItemChanged(GetEventKey(key, item), item, DataChangeKind.ParameterUpdated, parameterName, timestamp);
        return true;
    }

    public bool TryUpdateUserParameter(string key, string parameterName, object? value, ulong? timestamp = null)
    {
        if (!HostRegistryParameterPolicy.CanUserWriteParameter(parameterName))
        {
            HostLogger.Log.Warning("[DataRegistry] Blocked user write to protected parameter. key={Key} parameter={Parameter}", key, parameterName);
            return false;
        }

        return UpdateParameter(key, parameterName, value, timestamp);
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var rootKey = TryGetStoredRootKey(key, out var storedRootKey) ? storedRootKey : key;
        var removed = _items.TryRemove(rootKey, out _);
        if (removed)
        {
            RemoveIndexedRoot(rootKey);
            _metadata.TryRemove(rootKey, out _);
        }

        return removed;
    }

    private bool HasCapability(string key, DataRegistryItemCapabilities capability)
        => TryGetMetadata(key, out var metadata) && metadata.Capabilities.HasFlag(capability);

    private void RaiseItemChanged(string key, Item item, DataChangeKind changeKind, string? parameterName = null, ulong? timestamp = null)
    {
        ItemChanged?.Invoke(this, new DataChangedEventArgs(key, item, changeKind, parameterName, timestamp));
    }

    private void RaiseRegistryChanged(string key, Item item, DataChangeKind changeKind, string? parameterName = null, ulong? timestamp = null)
    {
        RegistryChanged?.Invoke(this, new DataChangedEventArgs(key, item, changeKind, parameterName, timestamp));
    }

    private static bool TryConvertForExistingValue(object? value, object? existingValue, out object? convertedValue)
    {
        convertedValue = value;
        if (value is null || existingValue is null)
        {
            return true;
        }

        var targetType = existingValue.GetType();
        var valueType = value.GetType();
        if (targetType == valueType)
        {
            return true;
        }

        try
        {
            if (targetType == typeof(string))
            {
                convertedValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetType.IsEnum)
            {
                convertedValue = value is string text
                    ? Enum.Parse(targetType, text, ignoreCase: true)
                    : Enum.ToObject(targetType, value);
                return true;
            }

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
            {
                convertedValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            convertedValue = value;
            return false;
        }

        return false;
    }

    private static bool TryResolveRelativeChild(Item rootItem, string relativePath, out Item? item)
    {
        var current = rootItem;
        foreach (var segment in SplitPathSegments(relativePath))
        {
            var matchingChildName = current.GetDictionary().Keys
                .FirstOrDefault(key => string.Equals(key, segment, StringComparison.OrdinalIgnoreCase));
            if (matchingChildName is null)
            {
                item = null;
                return false;
            }

            current = current.GetDictionary()[matchingChildName];
        }

        item = current;
        return true;
    }

    private void ReindexRoot(string rootKey, Item rootItem)
    {
        RemoveIndexedRoot(rootKey, rebuildOverlaps: false);
        AddRootIndexEntries(rootKey, rootItem);
    }

    private void AddRootIndexEntries(string rootKey, Item rootItem)
    {
        var indexedPaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateIndexEntries(rootKey, rootItem))
        {
            indexedPaths.TryAdd(entry.Path, 0);
            _pathIndex.AddOrUpdate(
                entry.Path,
                entry,
                (_, existing) => SelectPreferredIndexEntry(existing, entry));
        }

        _rootIndexPaths[NormalizeComparablePath(rootKey)] = indexedPaths;
    }

    private void RemoveIndexedRoot(string rootKey, bool rebuildOverlaps = true)
    {
        var normalizedRootKey = NormalizeComparablePath(rootKey);
        if (string.IsNullOrWhiteSpace(normalizedRootKey) || !_rootIndexPaths.TryRemove(normalizedRootKey, out var indexedPaths))
        {
            return;
        }

        foreach (var indexedPath in indexedPaths.Keys)
        {
            if (_pathIndex.TryGetValue(indexedPath, out var indexed)
                && string.Equals(NormalizeComparablePath(indexed.RootKey), normalizedRootKey, StringComparison.OrdinalIgnoreCase))
            {
                _pathIndex.TryRemove(indexedPath, out _);
            }
        }

        if (!rebuildOverlaps)
        {
            return;
        }

        foreach (var rootEntry in _items)
        {
            if (!string.Equals(NormalizeComparablePath(rootEntry.Key), normalizedRootKey, StringComparison.OrdinalIgnoreCase))
            {
                AddRootIndexEntries(rootEntry.Key, rootEntry.Value);
            }
        }
    }

    private bool TryGetStoredRootKey(string key, out string storedRootKey)
    {
        if (_items.ContainsKey(key))
        {
            storedRootKey = key;
            return true;
        }

        var comparableKey = NormalizeComparablePath(key);
        storedRootKey = _items.Keys.FirstOrDefault(candidate =>
            string.Equals(NormalizeComparablePath(candidate), comparableKey, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(storedRootKey);
    }

    private static IEnumerable<IndexedItem> EnumerateIndexEntries(string rootKey, Item rootItem)
    {
        var normalizedRootPath = NormalizeComparablePath(rootItem.Path);
        if (!string.IsNullOrWhiteSpace(normalizedRootPath))
        {
            yield return new IndexedItem(normalizedRootPath, rootKey, rootItem);
        }

        var normalizedRootKey = NormalizeComparablePath(rootKey);
        if (!string.IsNullOrWhiteSpace(normalizedRootKey))
        {
            yield return new IndexedItem(normalizedRootKey, rootKey, rootItem);
        }

        foreach (var child in rootItem.GetDictionary().Values)
        {
            foreach (var childEntry in EnumerateIndexEntries(rootKey, child))
            {
                yield return childEntry;
            }
        }
    }

    private static IndexedItem SelectPreferredIndexEntry(IndexedItem current, IndexedItem candidate)
    {
        var currentRootLength = SplitPathSegments(current.RootKey).Count;
        var candidateRootLength = SplitPathSegments(candidate.RootKey).Count;
        if (candidateRootLength != currentRootLength)
        {
            return candidateRootLength > currentRootLength ? candidate : current;
        }

        return candidate.RootKey.Length > current.RootKey.Length ? candidate : current;
    }

    private static string GetEventKey(string requestedKey, Item item)
        => string.IsNullOrWhiteSpace(item.Path) ? requestedKey : item.Path!;

    private static bool TryGetRelativePath(string path, string prefix, out string relativePath)
    {
        relativePath = string.Empty;

        var pathSegments = SplitPathSegments(path);
        var prefixSegments = SplitPathSegments(prefix);
        if (pathSegments.Count == 0 || prefixSegments.Count == 0 || pathSegments.Count <= prefixSegments.Count)
        {
            return false;
        }

        for (var index = 0; index < prefixSegments.Count; index++)
        {
            if (!string.Equals(pathSegments[index], prefixSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        relativePath = string.Join('.', pathSegments.Skip(prefixSegments.Count));
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static string NormalizeComparablePath(string? path)
    {
        var segments = SplitPathSegments(path);
        return segments.Count == 0 ? string.Empty : string.Join('.', NormalizeStudioRoot(segments));
    }

    private static IReadOnlyList<string> SplitPathSegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path
            .Trim()
            .Replace('\\', '.')
            .Replace('/', '.')
            .Trim('.')
            .Split(['.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> NormalizeStudioRoot(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            yield break;
        }

        if (LegacyProjectRootSegments.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(1))
            {
                yield return segment;
            }

            yield break;
        }

        if (string.Equals(segments[0], StudioRootSegment, StringComparison.OrdinalIgnoreCase)
            && segments.Count > 1
            && LegacyProjectRootSegments.Contains(segments[1], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(2))
            {
                yield return segment;
            }

            yield break;
        }

        foreach (var segment in segments)
        {
            yield return segment;
        }
    }

    private sealed record IndexedItem(string Path, string RootKey, Item Item);

    private static void MergeItem(Item target, Item source, bool pruneMissingMembers)
    {
        MergeParameters(target, source, pruneMissingMembers);
        MergeChildren(target, source, pruneMissingMembers);
    }

    private static void MergeParameters(Item target, Item source, bool pruneMissingMembers)
    {
        foreach (var parameterEntry in source.Params.GetDictionary())
        {
            var targetParameter = target.Params[parameterEntry.Key];
            targetParameter.Value = parameterEntry.Value.Value;
            targetParameter.LastUpdate = parameterEntry.Value.LastUpdate;
            targetParameter.Path = parameterEntry.Value.Path;
        }

        if (!pruneMissingMembers)
        {
            return;
        }

        foreach (var parameterName in target.Params.GetDictionary().Keys)
        {
            if (!source.Params.Has(parameterName))
            {
                target.Params.Remove(parameterName);
            }
        }
    }

    private static void MergeChildren(Item target, Item source, bool pruneMissingMembers)
    {
        foreach (var childEntry in source.GetDictionary())
        {
            if (target.Has(childEntry.Key))
            {
                MergeItem(target[childEntry.Key], childEntry.Value, pruneMissingMembers);
                continue;
            }

            target[childEntry.Key] = childEntry.Value.Clone();
        }

        if (!pruneMissingMembers)
        {
            return;
        }

        foreach (var childName in target.GetDictionary().Keys)
        {
            if (!source.Has(childName))
            {
                target.Remove(childName);
            }
        }
    }
}


public interface IProcessLogRegistry
{
    IReadOnlyCollection<string> GetAllNames();
    bool TryGet(string name, out ProcessLog? value);
    void Register(string name, ProcessLog log);
}

public sealed class ProcessLogRegistry : IProcessLogRegistry
{
    private readonly ConcurrentDictionary<string, ProcessLog> _logs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> GetAllNames() => _logs.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string name, out ProcessLog? value) => _logs.TryGetValue(name, out value);

    public void Register(string name, ProcessLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _logs[name] = log ?? throw new ArgumentNullException(nameof(log));
    }
}
public sealed class HostCommand
{
    public HostCommand(string name, Action<object?> execute, Func<object?, bool>? canExecute = null, string? description = null, Type? parameterType = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command name must not be empty.", nameof(name));
        }

        Name = name;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        CanExecute = canExecute;
        Description = description;
        ParameterType = parameterType;
    }

    public string Name { get; }
    public Action<object?> Execute { get; }
    public Func<object?, bool>? CanExecute { get; }
    public string? Description { get; }
    public Type? ParameterType { get; }
}

public interface ICommandRegistry
{
    IReadOnlyCollection<HostCommand> GetAll();
    void Register(HostCommand command);
    bool TryGet(string name, out HostCommand? command);
    bool CanExecute(string name, object? parameter = null);
    bool Execute(string name, object? parameter = null);
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly ConcurrentDictionary<string, HostCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<HostCommand> GetAll() => _commands.Values.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(HostCommand command)
    {
        _commands[command.Name] = command;
    }

    public bool TryGet(string name, out HostCommand? command) => _commands.TryGetValue(name, out command);

    public bool CanExecute(string name, object? parameter = null)
    {
        if (!TryGet(name, out var command) || command is null)
        {
            return false;
        }

        return command.CanExecute?.Invoke(parameter) ?? true;
    }

    public bool Execute(string name, object? parameter = null)
    {
        if (!TryGet(name, out var command) || command is null || !(command.CanExecute?.Invoke(parameter) ?? true))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}

public static class HostRegistries
{
    static HostRegistries()
    {
        Data = new DataRegistry();
        Signals = new SignalRegistry(Data);
        Commands = new CommandRegistry();
        Cameras = new CameraRegistry();
        ProcessLogs = new ProcessLogRegistry();
        TryInitializeDefaultCamera();
        UiPublisher.Publish("Logs.Host", HostLogger.ProcessLog, "Host");

        var assembly = typeof(HostRegistries).Assembly;
        var loadContext = AssemblyLoadContext.GetLoadContext(assembly);
        var message = $"HostRegistries initialized. pid={ProcessId} session={SessionId} sessionUtc={SessionStartedUtc:O} dataRegistryId={DataRegistryId} Assembly={assembly.Location} LoadContext={loadContext?.Name ?? "<default>"}";
        Debug.WriteLine(message);
        HostLogger.Log.Information(message);
    }

    public static DateTimeOffset SessionStartedUtc { get; } = DateTimeOffset.UtcNow;
    public static int ProcessId { get; } = Environment.ProcessId;
    public static string SessionId { get; } = Guid.NewGuid().ToString("N");
    public static IDataRegistry Data { get; }
    public static HornetStudio.Contracts.ISignalRegistry Signals { get; }
    public static ICommandRegistry Commands { get; }
    public static ICameraRegistry Cameras { get; }
    public static IProcessLogRegistry ProcessLogs { get; }
    public static int DataRegistryId => RuntimeHelpers.GetHashCode(Data);

    private static void TryInitializeDefaultCamera()
    {
        if (!OperatingSystem.IsWindows())
        {
            HostLogger.Log.Information("[Cameras] Default camera initialization skipped on non-Windows platform.");
            return;
        }

        try
        {
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0)
            {
                HostLogger.Log.Information("[Cameras] No video input devices found.");
                return;
            }

            for (var index = 0; index < devices.Count; index++)
            {
                var device = devices[index];
                var name = string.IsNullOrWhiteSpace(device.Name) ? $"Camera {index}" : device.Name.Trim();

                try
                {
                    var source = new WindowsCameraFrameSource(name, index);
                    Cameras.Register(source);
                    HostLogger.Log.Information("[Cameras] Registered camera '{Name}' at index {Index}.", name, index);
                }
                catch (Exception ex)
                {
                    HostLogger.Log.Warning(ex, "[Cameras] Failed to initialize camera '{Name}' at index {Index}.", name, index);
                }
            }
        }
        catch (Exception ex)
        {
            HostLogger.Log.Warning(ex, "[Cameras] Failed to enumerate video input devices.");
        }
    }
}




