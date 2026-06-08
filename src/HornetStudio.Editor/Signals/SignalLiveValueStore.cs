using System;
using HornetStudio.Host.Runtimes.Udl;
using EditorUdlPathHelper = HornetStudio.Editor.Helpers.UdlPathHelper;
using TargetPathHelper = HornetStudio.Editor.Helpers.TargetPathHelper;

namespace HornetStudio.Editor.Signals;

internal sealed record SignalLiveValueBindingDescriptor(
    string FolderName,
    string ClientName,
    string PublicTargetPath,
    string RuntimeItemPath,
    string RuntimeParameterName,
    string ModuleName)
{
    internal HornetStudio.Host.Runtimes.Udl.SignalLiveValueBindingDescriptor ToRuntimeBinding()
        => new(
            FolderName: FolderName,
            ClientName: ClientName,
            PublicTargetPath: PublicTargetPath,
            RuntimeItemPath: RuntimeItemPath,
            RuntimeParameterName: RuntimeParameterName,
            ModuleName: ModuleName);
}

internal sealed record SignalLiveValueSnapshot(
    object? Value,
    ulong? Epoch,
    long Version,
    DateTimeOffset UpdatedAtUtc);

internal static class SignalLiveValueStore
{
    internal static string BuildScopeKey(string folderName, string clientName)
    {
        var normalizedFolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        var normalizedClientName = EditorUdlPathHelper.NormalizeClientName(clientName);
        return string.Concat(normalizedFolderName, "|", normalizedClientName);
    }

    internal static bool TryGetSnapshot(SignalLiveValueBindingDescriptor binding, out SignalLiveValueSnapshot snapshot)
    {
        if (!UdlRuntimeLiveValueStore.TryGetSnapshot(binding.ToRuntimeBinding(), out var runtimeSnapshot))
        {
            snapshot = default!;
            return false;
        }

        snapshot = new SignalLiveValueSnapshot(
            Value: runtimeSnapshot.Value,
            Epoch: runtimeSnapshot.Epoch,
            Version: runtimeSnapshot.Version,
            UpdatedAtUtc: runtimeSnapshot.UpdatedAtUtc);
        return true;
    }

    internal static bool TryGetSnapshot(string folderName, string clientName, string itemPath, string parameterName, out SignalLiveValueSnapshot snapshot)
    {
        if (!UdlRuntimeLiveValueStore.TryGetSnapshot(folderName, clientName, itemPath, parameterName, out var runtimeSnapshot))
        {
            snapshot = default!;
            return false;
        }

        snapshot = new SignalLiveValueSnapshot(
            Value: runtimeSnapshot.Value,
            Epoch: runtimeSnapshot.Epoch,
            Version: runtimeSnapshot.Version,
            UpdatedAtUtc: runtimeSnapshot.UpdatedAtUtc);
        return true;
    }
}
