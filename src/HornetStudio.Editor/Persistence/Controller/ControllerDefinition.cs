using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

/// <summary>
/// Represents the persisted controller document in Folder.yaml.
/// </summary>
public sealed class ControllerDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public ControllerType Type { get; init; } = ControllerType.PID;

    public string SourcePath { get; init; } = string.Empty;

    public string SetpointPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public PidControllerDefinitionDocument Pid { get; init; } = new();
}

/// <summary>
/// Represents the persisted PID controller document in Folder.yaml.
/// </summary>
public sealed class PidControllerDefinitionDocument
{
    public double Ks { get; init; }

    public double Tu { get; init; }

    public double Tg { get; init; }

    public double DFilterTauMs { get; init; }

    public double SetMin { get; init; }

    public double SetMax { get; init; } = 100.0;

    public double OutMin { get; init; }

    public double OutMax { get; init; } = 100.0;

    public int ComputeIntervalMs { get; init; } = 100;

    public int OutputIntervalMs { get; init; } = 100;
}

/// <summary>
/// Converts controller definitions between raw JSON and layout documents.
/// </summary>
public static class ControllerDefinitionCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Parses raw controller definitions from the stored widget payload.
    /// </summary>
    /// <param name="raw">The raw JSON payload.</param>
    /// <returns>The normalized controller definitions.</returns>
    public static IReadOnlyList<ControllerDefinition> ParseDefinitions(string? raw)
    {
        var sharedDefinitions = ControllerDefinitionJsonCodec.ParseDefinitions(raw);
        if (sharedDefinitions.Count > 0)
        {
            return sharedDefinitions;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<ControllerDefinition>();
        }

        try
        {
            var documents = JsonSerializer.Deserialize<List<ControllerDefinitionDocument>>(raw, JsonOptions);
            return documents?
                .Where(static document => document is not null && !string.IsNullOrWhiteSpace(document.Name))
                .Select(document => FromDocument(document!))
                .ToArray()
                ?? Array.Empty<ControllerDefinition>();
        }
        catch
        {
            return Array.Empty<ControllerDefinition>();
        }
    }

    /// <summary>
    /// Serializes controller definitions into the stored widget payload.
    /// </summary>
    /// <param name="definitions">The controller definitions to serialize.</param>
    /// <returns>The raw JSON payload.</returns>
    public static string SerializeDefinitions(IEnumerable<ControllerDefinition>? definitions)
    {
        return ControllerDefinitionJsonCodec.SerializeDefinitions(definitions);
    }

    /// <summary>
    /// Converts raw controller definitions to layout documents.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON payload.</param>
    /// <returns>The persisted layout documents.</returns>
    public static List<ControllerDefinitionDocument> ToDocuments(string? rawDefinitions)
    {
        return ParseDefinitions(rawDefinitions)
            .Select(ToDocument)
            .ToList();
    }

    /// <summary>
    /// Converts layout documents back to the raw JSON widget payload.
    /// </summary>
    /// <param name="documents">The persisted layout documents.</param>
    /// <returns>The raw JSON payload.</returns>
    public static string FromDocuments(IEnumerable<ControllerDefinitionDocument>? documents)
    {
        if (documents is null)
        {
            return string.Empty;
        }

        return SerializeDefinitions(documents.Select(FromDocument));
    }

    /// <summary>
    /// Converts raw controller definitions to the JSON node representation used by Folder.yaml persistence.
    /// </summary>
    /// <param name="rawDefinitions">The raw JSON payload.</param>
    /// <returns>The JSON array node.</returns>
    public static JsonArray ToJsonArray(string? rawDefinitions)
    {
        var array = new JsonArray();
        foreach (var definition in ParseDefinitions(rawDefinitions))
        {
            array.Add(JsonSerializer.SerializeToNode(ToDocument(definition), JsonOptions));
        }

        return array;
    }

    /// <summary>
    /// Converts the JSON node representation used by Folder.yaml persistence into the raw widget payload.
    /// </summary>
    /// <param name="node">The persisted JSON node.</param>
    /// <returns>The raw JSON payload.</returns>
    public static string FromJsonNode(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            var definitions = array
                .Select(child => child?.Deserialize<ControllerDefinitionDocument>(JsonOptions))
                .Where(static document => document is not null)
                .Select(document => FromDocument(document!));

            return SerializeDefinitions(definitions);
        }

        return node?.GetValue<string>() ?? string.Empty;
    }

    public static ControllerDefinitionDocument ToDocument(ControllerDefinition definition)
    {
        var normalized = definition.Normalize();
        return new ControllerDefinitionDocument
        {
            Name = normalized.Name,
            Enabled = normalized.Enabled,
            Type = normalized.Type,
            SourcePath = normalized.SourcePath,
            SetpointPath = normalized.SetpointPath,
            OutputPath = normalized.OutputPath,
            Pid = new PidControllerDefinitionDocument
            {
                Ks = normalized.Pid.Ks,
                Tu = normalized.Pid.Tu,
                Tg = normalized.Pid.Tg,
                DFilterTauMs = normalized.Pid.DFilterTauMs,
                SetMin = normalized.Pid.SetMin,
                SetMax = normalized.Pid.SetMax,
                OutMin = normalized.Pid.OutMin,
                OutMax = normalized.Pid.OutMax,
                ComputeIntervalMs = normalized.Pid.ComputeIntervalMs,
                OutputIntervalMs = normalized.Pid.OutputIntervalMs
            }
        };
    }

    public static ControllerDefinition FromDocument(ControllerDefinitionDocument document)
    {
        return new ControllerDefinition
        {
            Name = document.Name,
            Enabled = document.Enabled,
            Type = document.Type,
            SourcePath = document.SourcePath,
            SetpointPath = document.SetpointPath,
            OutputPath = document.OutputPath,
            Pid = new PidControllerDefinition
            {
                Ks = document.Pid.Ks,
                Tu = document.Pid.Tu,
                Tg = document.Pid.Tg,
                DFilterTauMs = document.Pid.DFilterTauMs,
                SetMin = document.Pid.SetMin,
                SetMax = document.Pid.SetMax,
                OutMin = document.Pid.OutMin,
                OutMax = document.Pid.OutMax,
                ComputeIntervalMs = document.Pid.ComputeIntervalMs,
                OutputIntervalMs = document.Pid.OutputIntervalMs
            }
        }.Normalize();
    }
}