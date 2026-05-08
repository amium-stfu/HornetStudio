using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

/// <summary>
/// Describes source-neutral metadata that can be applied to a runtime item exposed by a widget.
/// </summary>
public sealed class ItemExposureDefinition
{
    /// <summary>
    /// Gets or sets the source-relative item path.
    /// </summary>
    public string ItemPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional display format.
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional unit text.
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether bit helper items should be published.
    /// </summary>
    public bool ExposeBits { get; set; }

    /// <summary>
    /// Gets or sets the explicit bit count.
    /// </summary>
    public int BitCount { get; set; }

    /// <summary>
    /// Gets or sets optional bit labels in <c>Bit0=Label</c> line format.
    /// </summary>
    public string BitLabels { get; set; } = string.Empty;
}

/// <summary>
/// Parses and serializes source-neutral item exposure definitions.
/// </summary>
public static class ItemExposureDefinitionCodec
{
    /// <summary>
    /// Parses item exposure definitions from their JSON representation.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON definitions.</param>
    /// <returns>The parsed definitions, or an empty list when parsing fails.</returns>
    public static IReadOnlyList<ItemExposureDefinition> ParseDefinitions(string? rawDefinitions)
    {
        if (string.IsNullOrWhiteSpace(rawDefinitions))
        {
            return [];
        }

        try
        {
            return FromJsonNode(JsonNode.Parse(rawDefinitions));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Converts a JSON node into item exposure definitions.
    /// </summary>
    /// <param name="node">The JSON node to convert.</param>
    /// <returns>The converted definitions.</returns>
    public static IReadOnlyList<ItemExposureDefinition> FromJsonNode(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .OfType<JsonObject>()
            .Select(static obj => new ItemExposureDefinition
            {
                ItemPath = obj["ItemPath"]?.GetValue<string>()?.Trim() ?? string.Empty,
                Format = obj["Format"]?.GetValue<string>()?.Trim() ?? string.Empty,
                Unit = obj["Unit"]?.GetValue<string>()?.Trim() ?? string.Empty,
                ExposeBits = obj["ExposeBits"]?.GetValue<bool>() ?? false,
                BitCount = obj["BitCount"]?.GetValue<int>() ?? 0,
                BitLabels = obj["BitLabels"]?.GetValue<string>()?.Trim() ?? string.Empty
            })
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.ItemPath))
            .ToArray();
    }

    /// <summary>
    /// Serializes item exposure definitions to JSON.
    /// </summary>
    /// <param name="definitions">The definitions to serialize.</param>
    /// <returns>The serialized JSON string.</returns>
    public static string SerializeDefinitions(IEnumerable<ItemExposureDefinition>? definitions)
    {
        return ToJsonArray(definitions).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Converts raw JSON definitions into a normalized JSON array.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON definitions.</param>
    /// <returns>The normalized JSON array.</returns>
    public static JsonArray ToJsonArray(string? rawDefinitions)
        => ToJsonArray(ParseDefinitions(rawDefinitions));

    /// <summary>
    /// Converts item exposure definitions into a JSON array.
    /// </summary>
    /// <param name="definitions">The definitions to convert.</param>
    /// <returns>The converted JSON array.</returns>
    public static JsonArray ToJsonArray(IEnumerable<ItemExposureDefinition>? definitions)
    {
        var array = new JsonArray();
        foreach (var definition in definitions?
                     .Where(static definition => definition is not null)
                     .Select(static definition => Normalize(definition))
                     .Where(static definition => !string.IsNullOrWhiteSpace(definition.ItemPath)
                                                && (definition.ExposeBits
                                                    || definition.BitCount > 0
                                                    || !string.IsNullOrWhiteSpace(definition.BitLabels)
                                                    || !string.IsNullOrWhiteSpace(definition.Format)
                                                    || !string.IsNullOrWhiteSpace(definition.Unit)))
                 ?? [])
        {
            array.Add(new JsonObject
            {
                ["ItemPath"] = definition.ItemPath,
                ["Format"] = definition.Format,
                ["Unit"] = definition.Unit,
                ["ExposeBits"] = definition.ExposeBits,
                ["BitCount"] = definition.BitCount,
                ["BitLabels"] = definition.BitLabels
            });
        }

        return array;
    }

    /// <summary>
    /// Serializes a JSON array back to a normalized definition string.
    /// </summary>
    /// <param name="array">The JSON array.</param>
    /// <returns>The serialized definition string.</returns>
    public static string FromJsonArray(JsonArray? array)
        => SerializeDefinitions(FromJsonNode(array));

    /// <summary>
    /// Replaces the definition for an item path while preserving other definitions.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON definitions.</param>
    /// <param name="itemPath">The item path to replace.</param>
    /// <param name="definition">The replacement definition.</param>
    /// <returns>The serialized merged definitions.</returns>
    public static string UpsertDefinition(string? rawDefinitions, string itemPath, ItemExposureDefinition definition)
    {
        var normalizedPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
        var relativePath = TargetPathHelper.ToRelativeItemServerPath(normalizedPath);
        var mergedDefinitions = ParseDefinitions(rawDefinitions)
            .Where(existing => !PathMatches(existing.ItemPath, normalizedPath, relativePath))
            .Append(definition)
            .ToArray();
        return SerializeDefinitions(mergedDefinitions);
    }

    /// <summary>
    /// Removes the definition for an item path while preserving other definitions.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON definitions.</param>
    /// <param name="itemPath">The item path to remove.</param>
    /// <returns>The serialized remaining definitions.</returns>
    public static string RemoveDefinition(string? rawDefinitions, string itemPath)
    {
        var normalizedPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
        var relativePath = TargetPathHelper.ToRelativeItemServerPath(normalizedPath);
        var remainingDefinitions = ParseDefinitions(rawDefinitions)
            .Where(existing => !PathMatches(existing.ItemPath, normalizedPath, relativePath))
            .ToArray();
        return SerializeDefinitions(remainingDefinitions);
    }

    private static ItemExposureDefinition Normalize(ItemExposureDefinition definition)
    {
        return new ItemExposureDefinition
        {
            ItemPath = definition.ItemPath?.Trim() ?? string.Empty,
            Format = definition.Format?.Trim() ?? string.Empty,
            Unit = definition.Unit?.Trim() ?? string.Empty,
            ExposeBits = definition.ExposeBits,
            BitCount = definition.BitCount,
            BitLabels = definition.BitLabels?.Trim() ?? string.Empty
        };
    }

    private static bool PathMatches(string? candidatePath, string normalizedPath, string relativePath)
    {
        var normalizedCandidate = TargetPathHelper.ToFlatItemServerPath(candidatePath);
        return string.Equals(normalizedCandidate, normalizedPath, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedCandidate, relativePath, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('/', '.').Replace('\\', '.').Trim('.');
}

/// <summary>
/// Applies source-neutral exposure metadata to runtime item snapshots.
/// </summary>
public static class ItemExposurePublisher
{
    /// <summary>
    /// Applies metadata and helper bit items to a runtime item snapshot.
    /// </summary>
    /// <param name="runtimeItem">The runtime item to update.</param>
    /// <param name="definition">The metadata definition to apply.</param>
    public static void Apply(ItemModel runtimeItem, ItemExposureDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Format))
        {
            runtimeItem.Properties["format"].Value = definition.Format;
        }

        if (!string.IsNullOrWhiteSpace(definition.Unit))
        {
            runtimeItem.Properties["unit"].Value = definition.Unit;
        }

        var bitCount = ResolveBitCount(definition, runtimeItem);
        if (!definition.ExposeBits || bitCount <= 0)
        {
            runtimeItem.Remove("Bits");
            return;
        }

        UpsertBits(runtimeItem, definition, bitCount);
    }

    /// <summary>
    /// Resolves a definition by source-relative item path.
    /// </summary>
    /// <param name="definitions">The available definitions.</param>
    /// <param name="itemPath">The source-relative item path.</param>
    /// <returns>The matching definition, or <see langword="null"/>.</returns>
    public static ItemExposureDefinition? FindByItemPath(IEnumerable<ItemExposureDefinition> definitions, string itemPath)
    {
        var normalizedPath = NormalizePath(itemPath);
        return definitions.FirstOrDefault(definition => string.Equals(NormalizePath(definition.ItemPath), normalizedPath, System.StringComparison.OrdinalIgnoreCase));
    }

    private static void UpsertBits(ItemModel runtimeItem, ItemExposureDefinition definition, int bitCount)
    {
        if (!runtimeItem.Has("Bits"))
        {
            runtimeItem["Bits"] = new ItemModel("Bits", path: runtimeItem.Path);
        }

        var bitsRoot = runtimeItem["Bits"];
        bitsRoot.Properties["kind"].Value = "Group";
        bitsRoot.Properties["title"].Value = $"{runtimeItem.Name} Bits";

        var rawValue = TryReadUnsignedInteger((object?)runtimeItem.Value, out ulong currentValue) ? currentValue : 0UL;
        var labels = ParseBitLabels(definition.BitLabels);
        var desiredBitNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var writable = !runtimeItem.Properties.Has("writable") || TryReadBool(runtimeItem.Properties["writable"].Value);

        for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            var bitName = $"Bit{bitIndex}";
            desiredBitNames.Add(bitName);
            if (!bitsRoot.Has(bitName))
            {
                bitsRoot[bitName] = new ItemModel(bitName, path: bitsRoot.Path);
            }

            var bitItem = bitsRoot[bitName];
            var label = labels.TryGetValue(bitIndex, out var configuredLabel) ? configuredLabel : bitName;
            bitItem.Value = ((rawValue >> bitIndex) & 1UL) == 1UL;
            bitItem.Properties["kind"].Value = "Bool";
            bitItem.Properties["format"].Value = "bool";
            bitItem.Properties["title"].Value = label;
            bitItem.Properties["text"].Value = label;
            bitItem.Properties["item_path"].Value = definition.ItemPath;
            bitItem.Properties["bit_index"].Value = bitIndex;
            bitItem.Properties["source_path"].Value = runtimeItem.Path ?? string.Empty;
            bitItem.Properties["writable"].Value = writable;
            bitItem.Properties["write_path"].Value = string.Empty;
            bitItem.Properties["write_mode"].Value = string.Empty;
        }

        foreach (var staleBitName in bitsRoot.GetDictionary().Keys.Except(desiredBitNames, System.StringComparer.OrdinalIgnoreCase).ToArray())
        {
            bitsRoot.Remove(staleBitName);
        }
    }

    private static int ResolveBitCount(ItemExposureDefinition definition, ItemModel runtimeItem)
    {
        if (definition.BitCount > 0)
        {
            return System.Math.Clamp(definition.BitCount, 1, 32);
        }

        var definitionBitCount = GetBitCount(definition.Format);
        if (definitionBitCount > 0)
        {
            return definitionBitCount;
        }

        var runtimeFormat = runtimeItem.Properties.Has("format")
            ? runtimeItem.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        return GetBitCount(runtimeFormat);
    }

    private static int GetBitCount(string? format)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(format)
            ? string.Empty
            : format.Trim().Split(':', 2, System.StringSplitOptions.TrimEntries)[0].ToLowerInvariant();

        return normalizedKind switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private static Dictionary<int, string> ParseBitLabels(string? rawLabels)
    {
        var labels = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(rawLabels))
        {
            return labels;
        }

        foreach (var line in rawLabels.Replace("\r", string.Empty).Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.StartsWith("Bit", System.StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key[3..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bitIndex)
                && !string.IsNullOrWhiteSpace(value))
            {
                labels[bitIndex] = value;
            }
        }

        return labels;
    }

    private static bool TryReadUnsignedInteger(object? value, out ulong result)
    {
        switch (value)
        {
            case byte byteValue:
                result = byteValue;
                return true;
            case sbyte sbyteValue:
                result = unchecked((ulong)sbyteValue);
                return true;
            case short shortValue:
                result = unchecked((ulong)shortValue);
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case int intValue:
                result = unchecked((ulong)intValue);
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case long longValue:
                result = unchecked((ulong)longValue);
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case string text when ulong.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0UL;
                return false;
        }
    }

    private static bool TryReadBool(object? value)
        => value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            string text when long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var numeric) => numeric != 0,
            null => true,
            _ => true
        };

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('/', '.').Replace('\\', '.').Trim('.');
}
