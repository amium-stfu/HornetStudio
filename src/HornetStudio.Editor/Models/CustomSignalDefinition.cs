using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

public enum CustomSignalMode
{
    Input = 0,
    Computed = 2
}

public enum CustomSignalComputationTrigger
{
    OnSourceChange = 0,
    Timer = 1,
    Manual = 2
}

public enum CustomSignalDataType
{
    Number,
    Boolean,
    Text
}

public enum CustomSignalOperation
{
    Copy,
    Add,
    Subtract,
    Multiply,
    Divide,
    Min,
    Max,
    GreaterThan,
    LessThan,
    Equals,
    And,
    Or,
    Concat,
    If
}

public sealed class CustomSignalVariableDefinition
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public CustomSignalVariableDefinition Clone()
    {
        return new CustomSignalVariableDefinition
        {
            Name = Name,
            SourcePath = SourcePath
        };
    }
}

public sealed class CustomSignalDefinition
{
    public string Name { get; set; } = string.Empty;

    public CustomSignalMode Mode { get; set; } = CustomSignalMode.Input;

    public CustomSignalDataType DataType { get; set; } = CustomSignalDataType.Number;

    public bool IsWritable { get; set; } = true;

    public string WritePath { get; set; } = string.Empty;

    public SignalWriteMode WriteMode { get; set; } = SignalWriteMode.Direct;

    public string Unit { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string ValueText { get; set; } = string.Empty;

    public string Formula { get; set; } = string.Empty;

    public CustomSignalComputationTrigger Trigger { get; set; } = CustomSignalComputationTrigger.OnSourceChange;

    public int TriggerIntervalSeconds { get; set; } = 1;

    public List<CustomSignalVariableDefinition> Variables { get; set; } = [];

    public CustomSignalOperation Operation { get; set; } = CustomSignalOperation.Copy;

    public string SourcePath { get; set; } = string.Empty;

    public string SourcePath2 { get; set; } = string.Empty;

    public string SourcePath3 { get; set; } = string.Empty;

    public CustomSignalDefinition Clone()
    {
        return new CustomSignalDefinition
        {
            Name = Name,
            Mode = Mode,
            DataType = DataType,
            IsWritable = IsWritable,
            WritePath = WritePath,
            WriteMode = WriteMode,
            Unit = Unit,
            Format = Format,
            ValueText = ValueText,
            Formula = Formula,
            Trigger = Trigger,
            TriggerIntervalSeconds = TriggerIntervalSeconds,
            Variables = Variables.Select(static variable => variable.Clone()).ToList(),
            Operation = Operation,
            SourcePath = SourcePath,
            SourcePath2 = SourcePath2,
            SourcePath3 = SourcePath3
        };
    }
}

public sealed class CustomSignalDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public CustomSignalMode Mode { get; init; } = CustomSignalMode.Input;

    public CustomSignalDataType DataType { get; init; } = CustomSignalDataType.Number;

    public bool IsWritable { get; init; } = true;

    public string WritePath { get; init; } = string.Empty;

    public SignalWriteMode WriteMode { get; init; } = SignalWriteMode.Direct;

    public string Unit { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string ValueText { get; init; } = string.Empty;

    public string Formula { get; init; } = string.Empty;

    public CustomSignalComputationTrigger Trigger { get; init; } = CustomSignalComputationTrigger.OnSourceChange;

    public int TriggerIntervalSeconds { get; init; } = 1;

    public List<CustomSignalVariableDefinitionDocument> Variables { get; init; } = [];

    public CustomSignalOperation Operation { get; init; } = CustomSignalOperation.Copy;

    public string SourcePath { get; init; } = string.Empty;

    public string SourcePath2 { get; init; } = string.Empty;

    public string SourcePath3 { get; init; } = string.Empty;
}

public sealed class CustomSignalVariableDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;
}

public static class CustomSignalDefinitionCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<CustomSignalDefinition> ParseDefinitions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<CustomSignalDefinition>();
        }

        try
        {
            if (JsonNode.Parse(raw) is JsonArray array)
            {
                var definitions = new List<CustomSignalDefinition>();
                foreach (var child in array.OfType<JsonObject>())
                {
                    if (TryDeserializeDefinition(child, folderName: null, out var definition))
                    {
                        definitions.Add(definition);
                    }
                }

                return definitions;
            }

            var parsed = JsonSerializer.Deserialize<List<CustomSignalDefinition>>(raw, JsonOptions);
            if (parsed is not null)
            {
                return parsed
                    .Where(static definition => definition is not null)
                    .Select(static definition => NormalizeLegacyDefinition(definition!))
                    .ToArray();
            }

            var documentParsed = JsonSerializer.Deserialize<List<CustomSignalDefinitionDocument>>(raw, JsonOptions);
            return documentParsed?
                .Where(static document => document is not null)
                .Select(document => FromDocument(document!, folderName: null))
                .ToArray()
                ?? Array.Empty<CustomSignalDefinition>();
        }
        catch
        {
            try
            {
                var documentParsed = JsonSerializer.Deserialize<List<CustomSignalDefinitionDocument>>(raw, JsonOptions);
                return documentParsed?
                    .Where(static document => document is not null)
                    .Select(document => FromDocument(document!, folderName: null))
                    .ToArray()
                    ?? Array.Empty<CustomSignalDefinition>();
            }
            catch
            {
                return Array.Empty<CustomSignalDefinition>();
            }
        }
    }

    public static string SerializeDefinitions(IEnumerable<CustomSignalDefinition>? definitions)
    {
        var normalized = definitions?
            .Where(static definition => definition is not null)
            .Select(static definition => definition!)
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToArray()
            ?? Array.Empty<CustomSignalDefinition>();

        return normalized.Length == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static List<CustomSignalDefinitionDocument> ToDocuments(string? rawDefinitions, string? folderName)
    {
        return ParseDefinitions(rawDefinitions)
            .Select(definition => ToDocument(definition, folderName))
            .ToList();
    }

    public static string FromDocuments(IEnumerable<CustomSignalDefinitionDocument>? documents, string? folderName)
    {
        if (documents is null)
        {
            return string.Empty;
        }

        var definitions = documents
            .Where(static document => document is not null)
            .Select(document => FromDocument(document!, folderName))
            .ToArray();

        return SerializeDefinitions(definitions);
    }

    public static JsonArray ToJsonArray(string? rawDefinitions, string? folderName)
    {
        var array = new JsonArray();
        foreach (var definition in ParseDefinitions(rawDefinitions))
        {
            array.Add(JsonSerializer.SerializeToNode(ToDocument(definition, folderName), JsonOptions));
        }

        return array;
    }

    public static string FromJsonNode(JsonNode? node, string? folderName)
    {
        if (node is JsonArray array)
        {
            var definitions = new List<CustomSignalDefinition>();
            foreach (var child in array)
            {
                if (child is not JsonObject childObject)
                {
                    continue;
                }

                if (TryDeserializeDefinition(childObject, folderName, out var definition))
                {
                    definitions.Add(definition);
                }
            }

            return SerializeDefinitions(definitions);
        }

        return node is JsonValue value && value.TryGetValue<string>(out var rawValue)
            ? rawValue
            : string.Empty;
    }

    private static bool TryDeserializeDefinition(JsonObject node, string? folderName, out CustomSignalDefinition definition)
    {
        var normalizedNode = NormalizeLegacyNode(node.DeepClone().AsObject());

        try
        {
            var parsedDefinition = normalizedNode.Deserialize<CustomSignalDefinition>(JsonOptions);
            if (parsedDefinition is not null)
            {
                definition = NormalizeLegacyDefinition(parsedDefinition);
                return true;
            }
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            var document = normalizedNode.Deserialize<CustomSignalDefinitionDocument>(JsonOptions);
            if (document is not null)
            {
                definition = FromDocument(document, folderName);
                return true;
            }
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        definition = null!;
        return false;
    }

    private static CustomSignalDefinitionDocument ToDocument(CustomSignalDefinition definition, string? folderName)
    {
        return new CustomSignalDefinitionDocument
        {
            Name = definition.Name,
            Mode = definition.Mode,
            DataType = definition.DataType,
            IsWritable = definition.IsWritable,
            WritePath = ToPersistedTargetPath(definition.WritePath, folderName),
            WriteMode = definition.WriteMode,
            Unit = definition.Unit,
            Format = definition.Format,
            ValueText = definition.ValueText,
            Formula = definition.Formula,
            Trigger = definition.Trigger,
            TriggerIntervalSeconds = definition.TriggerIntervalSeconds,
            Variables = definition.Variables
                .Where(static variable => variable is not null)
                .Select(variable => new CustomSignalVariableDefinitionDocument
                {
                    Name = variable.Name,
                    SourcePath = ToPersistedTargetPath(variable.SourcePath, folderName)
                })
                .ToList(),
            Operation = definition.Operation,
            SourcePath = ToPersistedTargetPath(definition.SourcePath, folderName),
            SourcePath2 = ToPersistedTargetPath(definition.SourcePath2, folderName),
            SourcePath3 = ToPersistedTargetPath(definition.SourcePath3, folderName)
        };
    }

    private static CustomSignalDefinition FromDocument(CustomSignalDefinitionDocument document, string? folderName)
    {
        return new CustomSignalDefinition
        {
            Name = document.Name,
            Mode = document.Mode,
            DataType = document.DataType,
            IsWritable = document.IsWritable,
            WritePath = NormalizeTargetPath(document.WritePath, folderName),
            WriteMode = document.WriteMode,
            Unit = document.Unit,
            Format = document.Format,
            ValueText = document.ValueText,
            Formula = document.Formula,
            Trigger = document.Trigger,
            TriggerIntervalSeconds = document.TriggerIntervalSeconds,
            Variables = document.Variables
                .Where(static variable => variable is not null)
                .Select(variable => new CustomSignalVariableDefinition
                {
                    Name = variable.Name,
                    SourcePath = NormalizeTargetPath(variable.SourcePath, folderName)
                })
                .ToList(),
            Operation = document.Operation,
            SourcePath = NormalizeTargetPath(document.SourcePath, folderName),
            SourcePath2 = NormalizeTargetPath(document.SourcePath2, folderName),
            SourcePath3 = NormalizeTargetPath(document.SourcePath3, folderName)
        };
    }

    private static JsonObject NormalizeLegacyNode(JsonObject node)
    {
        if (node["mode"] is JsonValue modeValue)
        {
            var normalizedMode = modeValue.TryGetValue<string>(out var modeText)
                ? modeText
                : modeValue.TryGetValue<int>(out var modeNumber)
                    ? modeNumber.ToString()
                    : string.Empty;

            if (string.Equals(normalizedMode, "Constant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedMode, "1", StringComparison.OrdinalIgnoreCase))
            {
                node["mode"] = nameof(CustomSignalMode.Input);
                node["isWritable"] = false;
            }

            if (string.Equals(normalizedMode, "2", StringComparison.OrdinalIgnoreCase))
            {
                node["mode"] = nameof(CustomSignalMode.Computed);
            }
        }

        return node;
    }

    private static CustomSignalDefinition NormalizeLegacyDefinition(CustomSignalDefinition definition)
    {
        if (definition.Mode == (CustomSignalMode)1)
        {
            definition.Mode = CustomSignalMode.Input;
            definition.IsWritable = false;
        }

        if (!Enum.IsDefined(definition.Trigger))
        {
            definition.Trigger = CustomSignalComputationTrigger.OnSourceChange;
        }

        definition.TriggerIntervalSeconds = Math.Max(1, definition.TriggerIntervalSeconds);
        definition.Variables ??= [];
        if (!Enum.IsDefined(definition.WriteMode))
        {
            definition.WriteMode = SignalWriteMode.Direct;
        }

        if (definition.Mode == CustomSignalMode.Computed && string.IsNullOrWhiteSpace(definition.Formula))
        {
            MigrateLegacyComputedDefinition(definition);
        }

        return definition;
    }

    private static void MigrateLegacyComputedDefinition(CustomSignalDefinition definition)
    {
        var variables = new List<CustomSignalVariableDefinition>();
        var tokenA = AddVariable(variables, "A", definition.SourcePath);
        var tokenB = AddVariable(variables, "B", definition.SourcePath2);
        var tokenC = AddVariable(variables, "C", definition.SourcePath3);

        definition.Variables = variables;
        definition.Trigger = CustomSignalComputationTrigger.OnSourceChange;
        definition.Formula = definition.Operation switch
        {
            CustomSignalOperation.Copy => tokenA,
            CustomSignalOperation.Add => $"{tokenA} + {tokenB}",
            CustomSignalOperation.Subtract => $"{tokenA} - {tokenB}",
            CustomSignalOperation.Multiply => $"{tokenA} * {tokenB}",
            CustomSignalOperation.Divide => $"{tokenA} / {tokenB}",
            CustomSignalOperation.Min => $"min({tokenA}, {tokenB})",
            CustomSignalOperation.Max => $"max({tokenA}, {tokenB})",
            CustomSignalOperation.GreaterThan => $"{tokenA} > {tokenB}",
            CustomSignalOperation.LessThan => $"{tokenA} < {tokenB}",
            CustomSignalOperation.Equals => $"{tokenA} == {tokenB}",
            CustomSignalOperation.And => $"{tokenA} && {tokenB}",
            CustomSignalOperation.Or => $"{tokenA} || {tokenB}",
            CustomSignalOperation.Concat => $"concat({tokenA}, {tokenB})",
            CustomSignalOperation.If => $"if({tokenA}, {tokenB}, {tokenC})",
            _ => tokenA
        };
    }

    private static string AddVariable(List<CustomSignalVariableDefinition> variables, string name, string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            if (variables.Any(variable => string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return $"{{{name}}}";
            }

            variables.Add(new CustomSignalVariableDefinition
            {
                Name = name,
                SourcePath = sourcePath
            });
        }

        return $"{{{name}}}";
    }

    private static string ToPersistedTargetPath(string? value, string? folderName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : TargetPathHelper.ToPersistedLayoutTargetPath(value, folderName);
    }

    private static string NormalizeTargetPath(string? value, string? folderName)
    {
        _ = folderName;
        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(value ?? string.Empty);
        return CollapseLegacyCustomSignalPath(normalized);
    }

    private static string CollapseLegacyCustomSignalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 5
            || !string.Equals(segments[0], "Project", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "CustomSignals", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return string.Join('.', segments.Where((_, index) => index != 3));
    }
}