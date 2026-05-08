using System.Text.Json;
using System.Text.Json.Nodes;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

/// <summary>
/// Defines how a local HornetStudio registry item is published to the MQTT item_broker.
/// </summary>
public sealed class BrokerPublishedItemDefinition
{
    /// <summary>
    /// Gets or sets the selected local registry root path that owns this definition.
    /// </summary>
    public string LocalRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local registry path.
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the broker item path.
    /// </summary>
    public string BrokerPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publish mode.
    /// </summary>
    public string PublishMode { get; set; } = BrokerPublishedItemPublishModes.OnChanged;

    /// <summary>
    /// Gets or sets a value indicating whether this entry is actively published.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets the interval in milliseconds for interval publishing.
    /// </summary>
    public int PublishIntervalMs { get; set; } = BrokerPublishedItemDefinitionCodec.DefaultPublishIntervalMs;

    /// <summary>
    /// Gets or sets a value indicating whether future write-back should treat the item as writable.
    /// </summary>
    public bool Writable { get; set; }
}

/// <summary>
/// Provides BrokerWidget publish mode constants.
/// </summary>
public static class BrokerPublishedItemPublishModes
{
    /// <summary>
    /// Publishes snapshots when the local registry item changes.
    /// </summary>
    public const string OnChanged = "OnChanged";

    /// <summary>
    /// Publishes snapshots at a configured interval.
    /// </summary>
    public const string Interval = "Interval";

    /// <summary>
    /// Normalizes a publish mode value.
    /// </summary>
    /// <param name="value">The raw publish mode.</param>
    /// <returns>The normalized publish mode.</returns>
    public static string Normalize(string? value)
        => string.Equals(value, Interval, StringComparison.OrdinalIgnoreCase) ? Interval : OnChanged;
}

/// <summary>
/// Parses and serializes BrokerWidget published item definitions.
/// </summary>
public static class BrokerPublishedItemDefinitionCodec
{
    /// <summary>
    /// The default publish interval in milliseconds.
    /// </summary>
    public const int DefaultPublishIntervalMs = 1000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <summary>
    /// Parses stored definitions and migrates legacy newline-separated paths.
    /// </summary>
    /// <param name="serialized">The serialized definitions or legacy path list.</param>
    /// <returns>The parsed definitions.</returns>
    public static IReadOnlyList<BrokerPublishedItemDefinition> ParseDefinitions(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        var trimmed = serialized.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var definitions = JsonSerializer.Deserialize<List<BrokerPublishedItemDefinition>>(trimmed, SerializerOptions) ?? [];
                return NormalizeDefinitions(definitions);
            }
            catch (JsonException)
            {
                return [];
            }
        }

        return NormalizeDefinitions(trimmed
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CreateDefault)
            .ToArray());
    }

    /// <summary>
    /// Serializes definitions as JSON.
    /// </summary>
    /// <param name="definitions">The definitions to serialize.</param>
    /// <returns>The serialized JSON.</returns>
    public static string SerializeDefinitions(IEnumerable<BrokerPublishedItemDefinition> definitions)
    {
        var normalized = NormalizeDefinitions(definitions).ToArray();
        return normalized.Length == 0 ? string.Empty : JsonSerializer.Serialize(normalized, SerializerOptions);
    }

    /// <summary>
    /// Converts serialized definitions to a JSON array node.
    /// </summary>
    /// <param name="serialized">The serialized definitions.</param>
    /// <returns>A JSON array node.</returns>
    public static JsonArray ToJsonArray(string? serialized)
    {
        var array = new JsonArray();
        foreach (var definition in ParseDefinitions(serialized))
        {
            array.Add(new JsonObject
            {
                ["LocalPath"] = definition.LocalPath,
                ["LocalRootPath"] = definition.LocalRootPath,
                ["BrokerPath"] = definition.BrokerPath,
                ["Active"] = definition.Active,
                ["PublishMode"] = definition.PublishMode,
                ["PublishIntervalMs"] = definition.PublishIntervalMs,
                ["Writable"] = definition.Writable
            });
        }

        return array;
    }

    /// <summary>
    /// Gets active publish definitions that belong to one local root path.
    /// </summary>
    /// <param name="definitions">The definitions to filter.</param>
    /// <param name="localRootPath">The local root path.</param>
    /// <returns>The active definitions owned by the local root.</returns>
    public static IReadOnlyList<BrokerPublishedItemDefinition> GetActiveDefinitionsForRoot(
        IEnumerable<BrokerPublishedItemDefinition> definitions,
        string? localRootPath)
    {
        var normalizedRootPath = TargetPathHelper.NormalizeConfiguredTargetPath(localRootPath);
        if (string.IsNullOrWhiteSpace(normalizedRootPath))
        {
            return [];
        }

        return NormalizeDefinitions(definitions)
            .Where(definition => definition.Active
                && string.Equals(definition.LocalRootPath, normalizedRootPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Reads definitions from a JSON node.
    /// </summary>
    /// <param name="node">The source JSON node.</param>
    /// <returns>The parsed definitions.</returns>
    public static IReadOnlyList<BrokerPublishedItemDefinition> FromJsonNode(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            var definitions = new List<BrokerPublishedItemDefinition>();
            foreach (var item in array.OfType<JsonObject>())
            {
                definitions.Add(new BrokerPublishedItemDefinition
                {
                    LocalRootPath = GetStringValue(item, "LocalRootPath"),
                    LocalPath = GetStringValue(item, "LocalPath"),
                    BrokerPath = GetStringValue(item, "BrokerPath"),
                    Active = GetBoolValue(item, "Active"),
                    PublishMode = GetStringValue(item, "PublishMode", BrokerPublishedItemPublishModes.OnChanged),
                    PublishIntervalMs = GetIntValue(item, "PublishIntervalMs", DefaultPublishIntervalMs),
                    Writable = GetBoolValue(item, "Writable"),
                });
            }

            return NormalizeDefinitions(definitions);
        }

        return ParseDefinitions(node?.GetValue<string>());
    }

    /// <summary>
    /// Creates a default publish definition for a local path.
    /// </summary>
    /// <param name="localPath">The local registry path.</param>
    /// <returns>The default definition.</returns>
    public static BrokerPublishedItemDefinition CreateDefault(string? localPath)
    {
        var normalizedLocalPath = TargetPathHelper.NormalizeConfiguredTargetPath(localPath);
        return new BrokerPublishedItemDefinition
        {
            LocalRootPath = normalizedLocalPath,
            LocalPath = normalizedLocalPath,
            BrokerPath = BuildDefaultBrokerPath(normalizedLocalPath),
            Active = false,
            PublishMode = BrokerPublishedItemPublishModes.OnChanged,
            PublishIntervalMs = DefaultPublishIntervalMs,
            Writable = false,
        };
    }

    /// <summary>
    /// Builds the default flat broker path for a local registry path.
    /// </summary>
    /// <param name="localPath">The local registry path.</param>
    /// <returns>The broker path.</returns>
    public static string BuildDefaultBrokerPath(string? localPath)
    {
        var normalizedLocalPath = TargetPathHelper.NormalizeConfiguredTargetPath(localPath);
        return normalizedLocalPath;
    }

    private static IReadOnlyList<BrokerPublishedItemDefinition> NormalizeDefinitions(IEnumerable<BrokerPublishedItemDefinition> definitions)
        => definitions
            .Select(NormalizeDefinition)
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.LocalPath))
            .GroupBy(static definition => $"{definition.LocalRootPath}\n{definition.LocalPath}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static definition => definition.LocalRootPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.LocalPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static BrokerPublishedItemDefinition NormalizeDefinition(BrokerPublishedItemDefinition definition)
    {
        var localPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
        var localRootPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalRootPath);
        if (string.IsNullOrWhiteSpace(localRootPath))
        {
            localRootPath = localPath;
        }

        var brokerPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.BrokerPath);
        if (string.IsNullOrWhiteSpace(brokerPath))
        {
            brokerPath = BuildDefaultBrokerPath(localPath);
        }

        return new BrokerPublishedItemDefinition
        {
            LocalRootPath = localRootPath,
            LocalPath = localPath,
            BrokerPath = brokerPath,
            Active = definition.Active,
            PublishMode = BrokerPublishedItemPublishModes.Normalize(definition.PublishMode),
            PublishIntervalMs = Math.Max(1, definition.PublishIntervalMs),
            Writable = definition.Writable,
        };
    }

    private static string GetStringValue(JsonObject item, string propertyName, string defaultValue = "")
    {
        try
        {
            return item[propertyName]?.GetValue<string>() ?? defaultValue;
        }
        catch (InvalidOperationException)
        {
            return item[propertyName]?.ToJsonString() ?? defaultValue;
        }
    }

    private static int GetIntValue(JsonObject item, string propertyName, int defaultValue)
    {
        try
        {
            return item[propertyName]?.GetValue<int>() ?? defaultValue;
        }
        catch (InvalidOperationException)
        {
            return int.TryParse(GetStringValue(item, propertyName), out var parsed) ? parsed : defaultValue;
        }
    }

    private static bool GetBoolValue(JsonObject item, string propertyName)
    {
        try
        {
            return item[propertyName]?.GetValue<bool>() ?? false;
        }
        catch (InvalidOperationException)
        {
            return bool.TryParse(GetStringValue(item, propertyName), out var parsed) && parsed;
        }
    }
}
