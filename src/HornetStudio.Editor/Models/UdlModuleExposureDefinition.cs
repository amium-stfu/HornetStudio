using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace HornetStudio.Editor.Models;

public sealed class UdlModuleExposureDefinition
{
    public string ModuleName { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public bool ExposeBits { get; set; }

    public int BitCount { get; set; }

    public bool RouteReadInputToSetRequest { get; set; }

    public string BitLabels { get; set; } = string.Empty;
}

public static class UdlModuleExposureDefinitionCodec
{
    public static IReadOnlyList<UdlModuleExposureDefinition> ParseDefinitions(string? rawDefinitions)
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

    public static IReadOnlyList<UdlModuleExposureDefinition> FromJsonNode(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .OfType<JsonObject>()
            .Select(static obj => new UdlModuleExposureDefinition
            {
                ModuleName = obj["ModuleName"]?.GetValue<string>()?.Trim() ?? string.Empty,
                ChannelName = NormalizeChannelName(obj["ChannelName"]?.GetValue<string>()),
                Format = obj["Format"]?.GetValue<string>()?.Trim() ?? string.Empty,
                Unit = obj["Unit"]?.GetValue<string>()?.Trim() ?? string.Empty,
                ExposeBits = obj["ExposeBits"]?.GetValue<bool>() ?? false,
                BitCount = obj["BitCount"]?.GetValue<int>() ?? 0,
                RouteReadInputToSetRequest = obj["RouteReadInputToSetRequest"]?.GetValue<bool>() ?? false,
                BitLabels = obj["BitLabels"]?.GetValue<string>()?.Trim() ?? string.Empty
            })
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.ModuleName)
                                        && !string.IsNullOrWhiteSpace(definition.ChannelName))
            .ToArray();
    }

    public static string SerializeDefinitions(IEnumerable<UdlModuleExposureDefinition>? definitions)
    {
        return ToJsonArray(definitions).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public static JsonArray ToJsonArray(string? rawDefinitions)
        => ToJsonArray(ParseDefinitions(rawDefinitions));

    public static JsonArray ToJsonArray(IEnumerable<UdlModuleExposureDefinition>? definitions)
    {
        var array = new JsonArray();
        foreach (var definition in definitions?
                     .Where(static definition => definition is not null)
                     .Select(static definition => Normalize(definition))
                     .Where(static definition => !string.IsNullOrWhiteSpace(definition.ModuleName)
                                                && !string.IsNullOrWhiteSpace(definition.ChannelName)
                                                && (definition.ExposeBits
                                                    || definition.BitCount > 0
                                                    || definition.RouteReadInputToSetRequest
                                                    || !string.IsNullOrWhiteSpace(definition.BitLabels)
                                                    || !string.IsNullOrWhiteSpace(definition.Format)
                                                    || !string.IsNullOrWhiteSpace(definition.Unit)))
                 ?? [])
        {
            array.Add(new JsonObject
            {
                ["ModuleName"] = definition.ModuleName,
                ["ChannelName"] = definition.ChannelName,
                ["Format"] = definition.Format,
                ["Unit"] = definition.Unit,
                ["ExposeBits"] = definition.ExposeBits,
                ["BitCount"] = definition.BitCount,
                ["RouteReadInputToSetRequest"] = definition.RouteReadInputToSetRequest,
                ["BitLabels"] = definition.BitLabels
            });
        }

        return array;
    }

    public static string FromJsonArray(JsonArray? array)
        => SerializeDefinitions(FromJsonNode(array));

    private static UdlModuleExposureDefinition Normalize(UdlModuleExposureDefinition definition)
    {
        return new UdlModuleExposureDefinition
        {
            ModuleName = definition.ModuleName?.Trim() ?? string.Empty,
            ChannelName = NormalizeChannelName(definition.ChannelName),
            Format = definition.Format?.Trim() ?? string.Empty,
            Unit = definition.Unit?.Trim() ?? string.Empty,
            ExposeBits = definition.ExposeBits,
            BitCount = definition.BitCount,
            RouteReadInputToSetRequest = definition.RouteReadInputToSetRequest,
            BitLabels = definition.BitLabels?.Trim() ?? string.Empty
        };
    }

    private static string NormalizeChannelName(string? channelName)
        => channelName?.Trim().ToLowerInvariant() ?? string.Empty;
}