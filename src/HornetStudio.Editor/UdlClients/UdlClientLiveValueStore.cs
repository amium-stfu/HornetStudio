using System;
using System.Collections.Concurrent;
using System.Threading;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.UdlClients;

internal sealed record SignalLiveValueBindingDescriptor(
    string FolderName,
    string ClientName,
    string PublicTargetPath,
    string RuntimeItemPath,
    string RuntimeParameterName,
    string ModuleName);

internal sealed record SignalLiveValueSnapshot(
    object? Value,
    ulong? Epoch,
    long Version,
    DateTimeOffset UpdatedAtUtc);

internal static class UdlClientLiveValueStore
{
    private static readonly ConcurrentDictionary<string, ScopeState> StatesByScope = new(StringComparer.OrdinalIgnoreCase);
    private static long _versionCounter;

    internal static string BuildScopeKey(string folderName, string clientName)
    {
        var normalizedFolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        var normalizedClientName = UdlPathHelper.NormalizeClientName(clientName);
        return string.Concat(normalizedFolderName, "|", normalizedClientName);
    }

    internal static void UpdateProperty(string folderName, string clientName, string itemPath, string parameterName, object? value, ulong? epoch = null)
    {
        if (string.IsNullOrWhiteSpace(folderName)
            || string.IsNullOrWhiteSpace(clientName)
            || string.IsNullOrWhiteSpace(itemPath)
            || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        var scope = StatesByScope.GetOrAdd(
            BuildScopeKey(folderName, clientName),
            static _ => new ScopeState());
        scope.Upsert(itemPath, parameterName, value, epoch, Interlocked.Increment(ref _versionCounter));
    }

    internal static bool TryGetSnapshot(SignalLiveValueBindingDescriptor binding, out SignalLiveValueSnapshot snapshot)
        => TryGetSnapshot(
            folderName: binding.FolderName,
            clientName: binding.ClientName,
            itemPath: binding.RuntimeItemPath,
            parameterName: binding.RuntimeParameterName,
            snapshot: out snapshot);

    internal static bool TryGetSnapshot(string folderName, string clientName, string itemPath, string parameterName, out SignalLiveValueSnapshot snapshot)
    {
        snapshot = default!;
        if (!StatesByScope.TryGetValue(BuildScopeKey(folderName, clientName), out var scope))
        {
            return false;
        }

        return scope.TryGet(itemPath, parameterName, out snapshot);
    }

    internal static void ClearScope(string folderName, string clientName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(clientName))
        {
            return;
        }

        StatesByScope.TryRemove(BuildScopeKey(folderName, clientName), out _);
    }

    private sealed class ScopeState
    {
        private readonly ConcurrentDictionary<string, SignalLiveValueSnapshot> _entries = new(StringComparer.OrdinalIgnoreCase);

        internal void Upsert(string itemPath, string parameterName, object? value, ulong? epoch, long version)
        {
            _entries[BuildEntryKey(itemPath, parameterName)] = new SignalLiveValueSnapshot(
                Value: value,
                Epoch: epoch,
                Version: version,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }

        internal bool TryGet(string itemPath, string parameterName, out SignalLiveValueSnapshot snapshot)
            => _entries.TryGetValue(BuildEntryKey(itemPath, parameterName), out snapshot!);

        private static string BuildEntryKey(string itemPath, string parameterName)
            => string.Concat(
                TargetPathHelper.NormalizeComparablePath(itemPath),
                "|",
                parameterName.Trim().ToLowerInvariant());
    }
}
