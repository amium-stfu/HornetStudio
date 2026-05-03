using System;
using HornetStudio.Logging;
using Amium.Items;

namespace HornetStudio.Host;

public static class UiPublisher
{
    public static Item Publish(Item item, bool pruneMissingMembers = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Path);
        return HostRegistries.Data.UpsertSnapshot(item.Path!, item, DataRegistryItemMetadata.PublicData(), pruneMissingMembers);
    }

    public static Item Publish(string path, ProcessLog log, string? title = null, bool pruneMissingMembers = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);

        var normalizedPath = NormalizeProcessLogPath(path);
        var lastSeparatorIndex = normalizedPath.LastIndexOf('.');
        var itemName = lastSeparatorIndex >= 0 ? normalizedPath[(lastSeparatorIndex + 1)..] : normalizedPath;
        var parentPath = lastSeparatorIndex >= 0 ? normalizedPath[..lastSeparatorIndex] : null;
        var displayTitle = string.IsNullOrWhiteSpace(title) ? itemName : title.Trim();

        var item = parentPath is null
            ? new Item(itemName, log)
            : new Item(itemName, log, parentPath);

        item.Params["Kind"].Value = "ProcessLog";
        item.Params["Title"].Value = displayTitle;
        item.Params["Text"].Value = displayTitle;
        // Wichtig: Der Registry-Schlüssel soll exakt dem TargetLog-Pfad entsprechen,
        // damit EditorLogControl den Log über "Logs.Host" o.ä. direkt auflösen kann.
        return HostRegistries.Data.UpsertSnapshot(normalizedPath, item, DataRegistryItemMetadata.Diagnostic(), pruneMissingMembers);
    }

    private static string NormalizeProcessLogPath(string path)
    {
        var normalized = path.Replace('\\', '.').Replace('/', '.').Trim('.');
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized;
    }

    public static void Publish(HostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        HostRegistries.Commands.Register(command);
    }

    public static void Publish(ICameraFrameSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        HostRegistries.Cameras.Register(source);
    }
}

