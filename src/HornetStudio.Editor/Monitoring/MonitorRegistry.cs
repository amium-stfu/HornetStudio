using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Identifies where one monitor registry entry originates.
/// </summary>
public enum MonitorRegistrySource
{
    CentralFile,
    LegacyWidget
}

/// <summary>
/// Represents one folder-level monitor registry entry.
/// </summary>
/// <param name="Name">The stable monitor rule name.</param>
/// <param name="Definition">The normalized monitor definition.</param>
/// <param name="Source">The entry source.</param>
/// <param name="SourceIdentifier">The backing file path or widget name.</param>
public sealed record MonitorRegistryEntry(
    string Name,
    MonitorDefinition Definition,
    MonitorRegistrySource Source,
    string SourceIdentifier);

/// <summary>
/// Represents one selectable monitor option for picker-based monitor selection.
/// </summary>
/// <param name="Name">The persisted stable monitor rule name.</param>
/// <param name="Label">The primary display label.</param>
/// <param name="Description">The secondary descriptive text.</param>
/// <param name="IsMissing">Indicates whether the option represents a currently missing persisted rule.</param>
public sealed record MonitorSelectionOption(
    string Name,
    string Label,
    string Description,
    bool IsMissing = false);

/// <summary>
/// Provides widget-independent monitor lookup for one folder.
/// </summary>
public static class MonitorRegistry
{
    private const char SelectionOptionSeparator = '\t';

    /// <summary>
    /// Parses the persisted selected monitor id list.
    /// </summary>
    /// <param name="rawSelection">The raw persisted selection text.</param>
    /// <returns>The normalized monitor ids in configured order.</returns>
    public static IReadOnlyList<string> ParseSelectedIds(string? rawSelection)
    {
        if (string.IsNullOrWhiteSpace(rawSelection))
        {
            return [];
        }

        return rawSelection
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSelectedId)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Serializes selected monitor ids for item storage.
    /// </summary>
    /// <param name="selectedIds">The selected monitor ids.</param>
    /// <returns>The normalized persisted text.</returns>
    public static string SerializeSelectedIds(IEnumerable<string>? selectedIds)
        => string.Join(", ", (selectedIds ?? [])
            .Select(NormalizeSelectedId)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Converts selected monitor ids into a JSON array for YAML persistence.
    /// </summary>
    /// <param name="rawSelection">The raw persisted selection text.</param>
    /// <returns>The JSON array.</returns>
    public static JsonArray ToJsonArray(string? rawSelection)
    {
        var array = new JsonArray();
        foreach (var id in ParseSelectedIds(rawSelection))
        {
            array.Add(id);
        }

        return array;
    }

    /// <summary>
    /// Converts a JSON array back into the persisted selected monitor id text.
    /// </summary>
    /// <param name="node">The JSON node.</param>
    /// <returns>The normalized persisted text.</returns>
    public static string FromJsonNode(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return SerializeSelectedIds(array
                .OfType<JsonValue>()
                .Select(static value => value.TryGetValue<string>(out var text) ? text : string.Empty));
        }

        return node is JsonValue value && value.TryGetValue<string>(out var rawValue)
            ? SerializeSelectedIds(ParseSelectedIds(rawValue))
            : string.Empty;
    }

    /// <summary>
    /// Creates picker options for available monitor rules.
    /// </summary>
    /// <param name="entries">The available registry entries.</param>
    /// <returns>The picker options in display order.</returns>
    public static IReadOnlyList<MonitorSelectionOption> CreateSelectionOptions(IEnumerable<MonitorRegistryEntry>? entries)
    {
        return (entries ?? [])
            .OrderBy(static entry => entry.Definition.EventId <= 0 ? int.MaxValue : entry.Definition.EventId)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSelectionOption)
            .ToArray();
    }

    /// <summary>
    /// Serializes one monitor picker option for editor dialog transport.
    /// </summary>
    /// <param name="option">The option to serialize.</param>
    /// <returns>The serialized option payload.</returns>
    public static string SerializeSelectionOption(MonitorSelectionOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return string.Join(
            SelectionOptionSeparator,
            option.Name ?? string.Empty,
            option.Label ?? string.Empty,
            option.Description ?? string.Empty,
            option.IsMissing ? "1" : "0");
    }

    /// <summary>
    /// Parses one serialized monitor picker option.
    /// </summary>
    /// <param name="serialized">The serialized option payload.</param>
    /// <param name="option">The parsed option.</param>
    /// <returns><c>true</c> when parsing succeeded.</returns>
    public static bool TryParseSelectionOption(string? serialized, out MonitorSelectionOption option)
    {
        option = new MonitorSelectionOption(string.Empty, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return false;
        }

        var parts = serialized.Split(SelectionOptionSeparator);
        if (parts.Length < 3)
        {
            return false;
        }

        var name = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        option = new MonitorSelectionOption(
            Name: name,
            Label: parts[1].Trim(),
            Description: parts[2].Trim(),
            IsMissing: parts.Length > 3 && string.Equals(parts[3].Trim(), "1", StringComparison.Ordinal));
        return true;
    }

    /// <summary>
    /// Determines whether the persisted selection token refers to the specified entry.
    /// </summary>
    /// <param name="selectionToken">The persisted selection token.</param>
    /// <param name="entry">The registry entry to compare.</param>
    /// <returns><c>true</c> when the token selects the entry.</returns>
    public static bool SelectionMatchesEntry(string? selectionToken, MonitorRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(selectionToken))
        {
            return false;
        }

        var normalizedSelection = NormalizeSelectedId(selectionToken);
        var normalizedEntryName = NormalizeSelectedId(entry.Name);
        return string.Equals(normalizedSelection, normalizedEntryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedSelection, entry.Definition.EventId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enumerates all available monitor entries for the specified folder.
    /// </summary>
    /// <param name="folderDirectory">The directory that contains Folder.yaml.</param>
    /// <param name="folderName">The technical folder name.</param>
    /// <param name="legacyMonitorItems">Placed monitor widgets that may still contain legacy definitions.</param>
    /// <returns>The combined registry entries.</returns>
    public static IReadOnlyList<MonitorRegistryEntry> EnumerateEntries(string? folderDirectory, string? folderName, IEnumerable<FolderItemModel>? legacyMonitorItems)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return [];
        }

        var entries = new Dictionary<string, MonitorRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        var normalizedFolderDirectory = string.IsNullOrWhiteSpace(folderDirectory)
            ? string.Empty
            : Path.GetFullPath(folderDirectory);
        var hasCentralFile = !string.IsNullOrWhiteSpace(normalizedFolderDirectory)
            && File.Exists(MonitorDefinitionFileCodec.GetMonitorFilePath(normalizedFolderDirectory));

        if (hasCentralFile)
        {
            var codec = new MonitorDefinitionFileCodec();
            foreach (var definition in codec.LoadDefinitions(normalizedFolderDirectory, folderName))
            {
                if (string.IsNullOrWhiteSpace(definition.Name))
                {
                    continue;
                }

                entries[definition.Name] = new MonitorRegistryEntry(
                    Name: definition.Name,
                    Definition: definition.Clone(),
                    Source: MonitorRegistrySource.CentralFile,
                    SourceIdentifier: MonitorDefinitionFileCodec.GetMonitorFilePath(normalizedFolderDirectory));
            }

            return entries.Values
                .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        foreach (var item in legacyMonitorItems ?? [])
        {
            if (item.Kind != ControlKind.Monitor)
            {
                continue;
            }

            foreach (var definition in MonitorDefinitionCodec.ParseDefinitions(item.MonitorDefinitions))
            {
                if (string.IsNullOrWhiteSpace(definition.Name) || entries.ContainsKey(definition.Name))
                {
                    continue;
                }

                entries[definition.Name] = new MonitorRegistryEntry(
                    Name: definition.Name,
                    Definition: definition.Clone(),
                    Source: MonitorRegistrySource.LegacyWidget,
                    SourceIdentifier: item.Name ?? string.Empty);
            }
        }

        return entries.Values
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Builds the folder-stable runtime path for one monitor rule.
    /// </summary>
    /// <param name="folderName">The technical folder name.</param>
    /// <param name="ruleName">The monitor rule name.</param>
    /// <returns>The runtime registry path.</returns>
    public static string BuildRulePath(string? folderName, string? ruleName)
    {
        var normalizedFolder = TargetPathHelper.NormalizePathSegment(folderName, "page");
        var normalizedRule = TargetPathHelper.NormalizePathSegment(ruleName, "rule");
        return $"studio.{normalizedFolder}.monitor.{normalizedRule}";
    }

    /// <summary>
    /// Builds the folder-stable aggregate runtime path for monitor state.
    /// </summary>
    /// <param name="folderName">The technical folder name.</param>
    /// <returns>The aggregate runtime registry path.</returns>
    public static string BuildAggregatePath(string? folderName)
    {
        var normalizedFolder = TargetPathHelper.NormalizePathSegment(folderName, "page");
        return $"studio.{normalizedFolder}.monitor";
    }

    private static MonitorSelectionOption CreateSelectionOption(MonitorRegistryEntry entry)
    {
        var eventText = string.IsNullOrWhiteSpace(entry.Definition.EventText)
            ? entry.Name
            : entry.Definition.EventText.Trim();
        var eventIdText = entry.Definition.EventId.ToString(CultureInfo.InvariantCulture);
        var label = $"{eventIdText} - {eventText} ({entry.Name})";

        var descriptionParts = new List<string>
        {
            $"LogLevel: {entry.Definition.LogLevel}"
        };

        if (!string.IsNullOrWhiteSpace(entry.Definition.SourcePath))
        {
            descriptionParts.Add($"Source: {entry.Definition.SourcePath}");
        }

        return new MonitorSelectionOption(
            Name: entry.Name,
            Label: label,
            Description: string.Join(" | ", descriptionParts));
    }

    private static string NormalizeSelectedId(string? selectedId)
        => string.IsNullOrWhiteSpace(selectedId)
            ? string.Empty
            : TargetPathHelper.NormalizePathSegment(selectedId, string.Empty);
}
