using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

public enum MonitorRuleMode
{
    Default,
    Custom
}

public enum MonitorLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public enum MonitorActionTrigger
{
    OnActivated,
    OnCleared
}

public enum MonitorActionType
{
    WriteLog,
    SetValue,
    InvokeFunction
}

public sealed class MonitorActionDefinition
{
    public MonitorActionTrigger Trigger { get; set; } = MonitorActionTrigger.OnActivated;

    public MonitorActionType ActionType { get; set; } = MonitorActionType.WriteLog;

    public string TargetLog { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string FunctionName { get; set; } = string.Empty;

    public string Argument { get; set; } = string.Empty;

    public MonitorActionDefinition Clone()
    {
        return new MonitorActionDefinition
        {
            Trigger = Trigger,
            ActionType = ActionType,
            TargetLog = TargetLog,
            TargetPath = TargetPath,
            FunctionName = FunctionName,
            Argument = Argument
        };
    }
}

public sealed class MonitorVariableDefinition
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public MonitorVariableDefinition Clone()
    {
        return new MonitorVariableDefinition
        {
            Name = Name,
            SourcePath = SourcePath
        };
    }
}

public sealed class MonitorDefinition
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public int RefreshRateMs { get; set; } = 1000;

    public int? TimeoutMs { get; set; }

    public MonitorRuleMode Mode { get; set; } = MonitorRuleMode.Default;

    public string LowerLimit { get; set; } = string.Empty;

    public string UpperLimit { get; set; } = string.Empty;

    public int InhibitMs { get; set; }

    public string CustomFormula { get; set; } = string.Empty;

    public List<MonitorVariableDefinition> CustomVariables { get; set; } = [];

    public int EventId { get; set; }

    public string EventText { get; set; } = string.Empty;

    public List<MonitorActionDefinition> Actions { get; set; } = [];

    public string TargetLog { get; set; } = string.Empty;

    public MonitorLogLevel LogLevel { get; set; } = MonitorLogLevel.Warning;

    public MonitorDefinition Clone()
    {
        return new MonitorDefinition
        {
            Name = Name,
            SourcePath = SourcePath,
            RefreshRateMs = RefreshRateMs,
            TimeoutMs = TimeoutMs,
            Mode = Mode,
            LowerLimit = LowerLimit,
            UpperLimit = UpperLimit,
            InhibitMs = InhibitMs,
            CustomFormula = CustomFormula,
            CustomVariables = CustomVariables.Select(static variable => variable.Clone()).ToList(),
            EventId = EventId,
            EventText = EventText,
            Actions = Actions.Select(static action => action.Clone()).ToList(),
            TargetLog = TargetLog,
            LogLevel = LogLevel
        };
    }
}

public sealed class MonitorRuntimeSettings
{
    public int StartupDelayMs { get; set; }

    public MonitorRuntimeSettings Clone()
    {
        return new MonitorRuntimeSettings
        {
            StartupDelayMs = StartupDelayMs
        };
    }
}

public sealed class MonitorRuntimeSettingsDocument
{
    public int StartupDelayMs { get; init; }
}

public sealed class MonitorActionDefinitionDocument
{
    public MonitorActionTrigger Trigger { get; init; } = MonitorActionTrigger.OnActivated;

    public MonitorActionType ActionType { get; init; } = MonitorActionType.WriteLog;

    public string TargetLog { get; init; } = string.Empty;

    public string TargetPath { get; init; } = string.Empty;

    public string FunctionName { get; init; } = string.Empty;

    public string Argument { get; init; } = string.Empty;
}

public sealed class MonitorDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public int RefreshRateMs { get; init; } = 1000;

    public int? TimeoutMs { get; init; }

    public MonitorRuleMode Mode { get; init; } = MonitorRuleMode.Default;

    public string LowerLimit { get; init; } = string.Empty;

    public string UpperLimit { get; init; } = string.Empty;

    public int InhibitMs { get; init; }

    public string CustomFormula { get; init; } = string.Empty;

    public List<MonitorVariableDefinition> CustomVariables { get; init; } = [];

    public string Condition { get; init; } = string.Empty;

    public int EventId { get; init; }

    public string EventText { get; init; } = string.Empty;

    public List<MonitorActionDefinitionDocument> Actions { get; init; } = [];

    public string TargetLog { get; init; } = string.Empty;

    public MonitorLogLevel LogLevel { get; init; } = MonitorLogLevel.Warning;
}

public static class MonitorDefinitionCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new FlexibleStringJsonConverter(),
            new JsonStringEnumConverter()
        }
    };

    public static IReadOnlyList<MonitorDefinition> ParseDefinitions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<MonitorDefinition>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MonitorDefinition>>(raw, JsonOptions);
            return parsed?
                .Where(static definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Name))
                .Select(static definition => NormalizeDefinition(definition!))
                .ToArray()
                ?? Array.Empty<MonitorDefinition>();
        }
        catch
        {
            try
            {
                var documents = JsonSerializer.Deserialize<List<MonitorDefinitionDocument>>(raw, JsonOptions);
                return documents?
                    .Where(static document => document is not null && !string.IsNullOrWhiteSpace(document.Name))
                    .Select(document => FromDocument(document!, null))
                    .ToArray()
                    ?? Array.Empty<MonitorDefinition>();
            }
            catch
            {
                return Array.Empty<MonitorDefinition>();
            }
        }
    }

    public static string SerializeDefinitions(IEnumerable<MonitorDefinition>? definitions)
    {
        var normalized = definitions?
            .Where(static definition => definition is not null)
            .Select(static definition => NormalizeDefinition(definition!))
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToArray()
            ?? Array.Empty<MonitorDefinition>();

        return normalized.Length == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static List<MonitorDefinitionDocument> ToDocuments(string? rawDefinitions, string? folderName)
    {
        return ParseDefinitions(rawDefinitions)
            .Select(definition => ToDocument(definition, folderName))
            .ToList();
    }

    public static string FromDocuments(IEnumerable<MonitorDefinitionDocument>? documents, string? folderName)
    {
        if (documents is null)
        {
            return string.Empty;
        }

        return SerializeDefinitions(documents.Select(document => FromDocument(document, folderName)));
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
            var definitions = new List<MonitorDefinition>();
            foreach (var child in array)
            {
                if (child is not JsonObject definitionNode)
                {
                    continue;
                }

                if (TryDeserializeDefinition(definitionNode, folderName, out var definition))
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

    private static bool TryDeserializeDefinition(JsonObject node, string? folderName, out MonitorDefinition definition)
    {
        try
        {
            var document = node.Deserialize<MonitorDefinitionDocument>(JsonOptions);
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

        definition = new MonitorDefinition();
        return false;
    }

    private static MonitorDefinitionDocument ToDocument(MonitorDefinition definition, string? folderName)
    {
        return new MonitorDefinitionDocument
        {
            Name = TargetPathHelper.NormalizeIdentityName(definition.Name),
            SourcePath = TargetPathHelper.ToPersistedLayoutTargetPath(definition.SourcePath, folderName),
            RefreshRateMs = Math.Max(100, definition.RefreshRateMs),
            TimeoutMs = definition.TimeoutMs,
            Mode = definition.Mode,
            LowerLimit = definition.LowerLimit?.Trim() ?? string.Empty,
            UpperLimit = definition.UpperLimit?.Trim() ?? string.Empty,
            InhibitMs = Math.Max(0, definition.InhibitMs),
            CustomFormula = definition.CustomFormula?.Trim() ?? string.Empty,
            CustomVariables = definition.CustomVariables
                .Where(static variable => variable is not null)
                .Select(variable => NormalizeVariable(variable!, folderName))
                .Where(static variable => !string.IsNullOrWhiteSpace(variable.Name))
                .ToList(),
            EventId = definition.EventId,
            EventText = definition.EventText?.Trim() ?? string.Empty,
            Actions = definition.Actions
                .Where(static action => action is not null)
                .Select(action => NormalizeActionDocument(action!, folderName))
                .ToList(),
            LogLevel = definition.LogLevel
        };
    }

    private static MonitorDefinition FromDocument(MonitorDefinitionDocument document, string? folderName)
    {
        var customFormula = !string.IsNullOrWhiteSpace(document.CustomFormula)
            ? document.CustomFormula
            : document.Condition;

        return NormalizeDefinition(new MonitorDefinition
        {
            Name = document.Name,
            SourcePath = ExpandPersistedTargetPath(document.SourcePath, folderName),
            RefreshRateMs = document.RefreshRateMs,
            TimeoutMs = document.TimeoutMs,
            Mode = document.Mode,
            LowerLimit = document.LowerLimit,
            UpperLimit = document.UpperLimit,
            InhibitMs = document.InhibitMs,
            CustomFormula = customFormula,
            CustomVariables = document.CustomVariables
                .Where(static variable => variable is not null)
                .Select(variable => NormalizeVariable(variable!, folderName))
                .Where(static variable => !string.IsNullOrWhiteSpace(variable.Name))
                .ToList(),
            EventId = document.EventId,
            EventText = document.EventText,
            Actions = document.Actions
                .Where(static action => action is not null)
                .Select(action => FromActionDocument(action!, folderName))
                .ToList(),
            TargetLog = TargetPathHelper.NormalizeConfiguredTargetPath(document.TargetLog),
            LogLevel = document.LogLevel
        });
    }

    private static MonitorDefinition NormalizeDefinition(MonitorDefinition definition)
    {
        return new MonitorDefinition
        {
            Name = TargetPathHelper.NormalizeIdentityName(definition.Name),
            SourcePath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.SourcePath),
            RefreshRateMs = definition.RefreshRateMs <= 0 ? 1000 : Math.Max(100, definition.RefreshRateMs),
            TimeoutMs = definition.TimeoutMs > 0 ? definition.TimeoutMs : null,
            Mode = definition.Mode,
            LowerLimit = definition.LowerLimit?.Trim() ?? string.Empty,
            UpperLimit = definition.UpperLimit?.Trim() ?? string.Empty,
            InhibitMs = Math.Max(0, definition.InhibitMs),
            CustomFormula = definition.CustomFormula?.Trim() ?? string.Empty,
            CustomVariables = definition.CustomVariables
                .Where(static variable => variable is not null)
                .Select(static variable => NormalizeVariable(variable!, null))
                .Where(static variable => !string.IsNullOrWhiteSpace(variable.Name))
                .ToList(),
            EventId = definition.EventId,
            EventText = definition.EventText?.Trim() ?? string.Empty,
            Actions = NormalizeActions(definition.Actions, definition.TargetLog),
            TargetLog = string.Empty,
            LogLevel = Enum.IsDefined(definition.LogLevel) ? definition.LogLevel : MonitorLogLevel.Warning
        };
    }

    private static MonitorActionDefinitionDocument NormalizeActionDocument(MonitorActionDefinition action, string? folderName)
    {
        var normalized = NormalizeAction(action);
        return new MonitorActionDefinitionDocument
        {
            Trigger = normalized.Trigger,
            ActionType = normalized.ActionType,
            TargetLog = TargetPathHelper.ToPersistedLayoutTargetPath(normalized.TargetLog, folderName),
            TargetPath = ToPersistedActionTargetPath(normalized, folderName),
            FunctionName = normalized.FunctionName,
            Argument = normalized.Argument
        };
    }

    private static MonitorActionDefinition FromActionDocument(MonitorActionDefinitionDocument document, string? folderName)
    {
        return new MonitorActionDefinition
        {
            Trigger = document.Trigger,
            ActionType = document.ActionType,
            TargetLog = ExpandPersistedTargetPath(document.TargetLog, folderName),
            TargetPath = NormalizeActionTargetPath(document.ActionType, document.TargetPath, folderName),
            FunctionName = document.FunctionName?.Trim() ?? string.Empty,
            Argument = document.Argument?.Trim() ?? string.Empty
        };
    }

    private static List<MonitorActionDefinition> NormalizeActions(IEnumerable<MonitorActionDefinition>? actions, string? legacyTargetLog)
    {
        var normalizedActions = actions?
            .Where(static action => action is not null)
            .Select(static action => NormalizeAction(action!))
            .ToList()
            ?? [];

        if (normalizedActions.Count > 0)
        {
            return normalizedActions;
        }

        var normalizedTargetLog = TargetPathHelper.NormalizeConfiguredTargetPath(legacyTargetLog);
        if (string.IsNullOrWhiteSpace(normalizedTargetLog))
        {
            return [];
        }

        return
        [
            new MonitorActionDefinition
            {
                Trigger = MonitorActionTrigger.OnActivated,
                ActionType = MonitorActionType.WriteLog,
                TargetLog = normalizedTargetLog
            }
        ];
    }

    private static MonitorActionDefinition NormalizeAction(MonitorActionDefinition action)
    {
        var normalizedTrigger = Enum.IsDefined(action.Trigger)
            ? action.Trigger
            : MonitorActionTrigger.OnActivated;
        var normalizedActionType = Enum.IsDefined(action.ActionType)
            ? action.ActionType
            : MonitorActionType.WriteLog;

        return new MonitorActionDefinition
        {
            Trigger = normalizedTrigger,
            ActionType = normalizedActionType,
            TargetLog = TargetPathHelper.NormalizeConfiguredTargetPath(action.TargetLog),
            TargetPath = NormalizeActionTargetPath(normalizedActionType, action.TargetPath, folderName: null),
            FunctionName = action.FunctionName?.Trim() ?? string.Empty,
            Argument = action.Argument?.Trim() ?? string.Empty
        };
    }

    private static string ToPersistedActionTargetPath(MonitorActionDefinition action, string? folderName)
    {
        return action.ActionType switch
        {
            MonitorActionType.InvokeFunction => HornetStudio.Editor.Widgets.ApplicationExplorerRuntime.ToPersistedInteractionTargetPath(action.TargetPath),
            MonitorActionType.SetValue => TargetPathHelper.ToPersistedLayoutTargetPath(action.TargetPath, folderName),
            _ => string.Empty
        };
    }

    private static string NormalizeActionTargetPath(MonitorActionType actionType, string? targetPath, string? folderName)
    {
        return actionType switch
        {
            MonitorActionType.InvokeFunction => targetPath?.Trim() ?? string.Empty,
            MonitorActionType.SetValue => ExpandPersistedTargetPath(targetPath, folderName),
            _ => string.Empty
        };
    }

    private static string ExpandPersistedTargetPath(string? path, string? folderName)
    {
        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "this", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return TargetPathHelper.EnumerateResolutionCandidates(normalized, folderName)
            .FirstOrDefault(static candidate => candidate.StartsWith("studio.", StringComparison.OrdinalIgnoreCase))
            ?? normalized;
    }

    private static MonitorVariableDefinition NormalizeVariable(MonitorVariableDefinition variable, string? folderName)
    {
        return new MonitorVariableDefinition
        {
            Name = NormalizeVariableName(variable.Name),
            SourcePath = folderName is null
                ? TargetPathHelper.NormalizeConfiguredTargetPath(variable.SourcePath)
                : TargetPathHelper.ToPersistedLayoutTargetPath(variable.SourcePath, folderName)
        };
    }

    private static string NormalizeVariableName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return IsValidVariableName(trimmed) ? trimmed : string.Empty;
    }

    private static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]))
        {
            return false;
        }

        return name.All(static character => char.IsLetterOrDigit(character) || character == '_');
    }

    private sealed class FlexibleStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => ReadNumberAsString(ref reader),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => string.Empty,
                _ => string.Empty
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }

        private static string ReadNumberAsString(ref Utf8JsonReader reader)
        {
            if (reader.TryGetInt64(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (reader.TryGetDecimal(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            return reader.TryGetDouble(out var doubleValue)
                ? doubleValue.ToString("R", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
