using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace HornetStudio.Host;

public sealed class ProjectFolderLayout
{
    public string FolderName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public IReadOnlyDictionary<int, string> Views { get; init; } = new Dictionary<int, string>();
    public JsonObject DocumentProperties { get; init; } = [];
    public ProjectUiNode Layout { get; init; } = new();
}

public sealed class ProjectUiNode
{
    public string Type { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? Spacing { get; init; }
    public JsonObject Properties { get; init; } = [];
    public IReadOnlyList<ProjectUiNode> Children { get; init; } = [];
}

public static class ProjectUiLayoutLoader
{

    public static ProjectFolderLayout LoadYaml(string uiFilePath, string fallbackPageName)
    {
        if (string.IsNullOrWhiteSpace(uiFilePath))
        {
            throw new ArgumentException("UI file path must not be empty.", nameof(uiFilePath));
        }

        if (!File.Exists(uiFilePath))
        {
            throw new FileNotFoundException("UI file not found.", uiFilePath);
        }

        var content = File.ReadAllText(uiFilePath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return CreateEmptyLayout(fallbackPageName);
        }

        using var reader = new StringReader(content);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException("Folder.yaml does not contain a valid root mapping.");
        }

        var legacyPageName = GetScalar(root, "Folder") ?? GetScalar(root, "Page");
        var pageName = fallbackPageName;
        var caption = GetScalar(root, "Caption") ?? GetScalar(root, "Title") ?? legacyPageName ?? pageName;
        var views = ReadViews(root);
        var controls = GetSequence(root, "Controls");

        var children = new List<ProjectUiNode>();
        if (controls is not null)
        {
            foreach (var controlNode in controls.Children.OfType<YamlMappingNode>())
            {
                children.Add(ReadYamlControlNode(controlNode));
            }
        }

        var documentProperties = new JsonObject
        {
            ["Title"] = caption,
            ["Caption"] = caption,
            ["Screens"] = new JsonObject(views.Select(static entry => new KeyValuePair<string, JsonNode?>(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value)))
        };

        return new ProjectFolderLayout
        {
            FolderName = pageName,
            Title = caption,
            Caption = caption,
            Views = views,
            DocumentProperties = documentProperties,
            Layout = new ProjectUiNode
            {
                Type = "Canvas",
                Text = caption,
                Properties = new JsonObject
                {
                    ["Type"] = "Canvas"
                },
                Children = children
            }
        };
    }

    private static ProjectFolderLayout CreateEmptyLayout(string fallbackPageName)
    {
        return new ProjectFolderLayout
        {
            FolderName = fallbackPageName,
            Title = fallbackPageName,
            Caption = fallbackPageName,
            Views = new Dictionary<int, string>
            {
                [1] = "HomeScreen"
            },
            Layout = new ProjectUiNode
            {
                Type = "Canvas",
                Properties = new JsonObject
                {
                    ["Type"] = "Canvas"
                },
                Children = []
            }
        };
    }

    private static string? GetString(JsonObject properties, string propertyName)
    {
        return properties[propertyName]?.GetValue<string>();
    }

    private static double? GetDouble(JsonObject properties, string propertyName)
    {
        var value = properties[propertyName];
        return value switch
        {
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out var result) => result,
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) && double.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static Dictionary<int, string> ReadViews(YamlMappingNode root)
    {
        var views = new Dictionary<int, string>();
        if ((GetMapping(root, "Screens") ?? GetMapping(root, "Views")) is not { } viewsNode)
        {
            views[1] = "HomeScreen";
            return views;
        }

        foreach (var entry in viewsNode.Children)
        {
            if (entry.Key is not YamlScalarNode keyNode || entry.Value is not YamlScalarNode valueNode)
            {
                continue;
            }

            if (int.TryParse(keyNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
            {
                views[key] = valueNode.Value ?? string.Empty;
            }
        }

        if (views.Count == 0)
        {
            views[1] = "HomeScreen";
        }

        return views;
    }

    private static ProjectUiNode ReadYamlControlNode(YamlMappingNode node)
    {
        var type = NormalizeControlType(GetScalar(node, "Type"));
        var properties = new JsonObject
        {
            ["Type"] = type
        };

        SetPropertyIfPresent(properties, "View", GetScalarJsonNode(node, "Screen") ?? GetScalarJsonNode(node, "View"));
        SetPropertyIfPresent(properties, "Enabled", GetScalarJsonNode(node, "Enabled"));

        if (GetMapping(node, "Identity") is { } identity)
        {
            SetPropertyIfPresent(properties, "Name", GetScalarJsonNode(identity, "Name"));
            SetPropertyIfPresent(properties, "Text", GetScalarJsonNode(identity, "Text"));
            SetPropertyIfPresent(properties, "Id", GetScalarJsonNode(identity, "Id"));
        }

        if (GetBoundsMapping(node) is { } rect)
        {
            SetPropertyIfPresent(properties, "X", GetScalarJsonNode(rect, "X"));
            SetPropertyIfPresent(properties, "Y", GetScalarJsonNode(rect, "Y"));
            SetPropertyIfPresent(properties, "Width", GetScalarJsonNode(rect, "Width"));
            SetPropertyIfPresent(properties, "Height", GetScalarJsonNode(rect, "Height"));
        }

        if (GetMapping(node, "Design") is { } design)
        {
            SetPropertyIfPresent(properties, "CornerRadius", GetScalarJsonNode(design, "CornerRadius"));
            SetPropertyIfPresent(properties, "BorderWidth", GetScalarJsonNode(design, "BorderWidth"));
            SetPropertyIfPresent(properties, "BorderColor", GetScalarJsonNode(design, "BorderColor"));
            SetPropertyIfPresent(properties, "BackgroundColor", GetScalarJsonNode(design, "BackColor"));
            SetPropertyIfPresent(properties, "ToolTipText", GetScalarJsonNode(design, "ToolTip"));
        }

        if (GetMapping(node, "Header") is { } header)
        {
            SetPropertyIfPresent(properties, "ControlCaption", GetScalarJsonNode(header, "ControlCaption"));
            SetPropertyIfPresent(properties, "SyncText", GetScalarJsonNode(header, "SyncText"));
            SetPropertyIfPresent(properties, "HeaderForeColor", GetScalarJsonNode(header, "HeaderForeColor"));
            SetPropertyIfPresent(properties, "CaptionVisible", GetScalarJsonNode(header, "CaptionVisible"));
            SetPropertyIfPresent(properties, "ShowCaption", GetScalarJsonNode(header, "CaptionVisible"));
            SetPropertyIfPresent(properties, "HeaderCornerRadius", GetScalarJsonNode(header, "HeaderCornerRadius"));
            SetPropertyIfPresent(properties, "HeaderBorderWidth", GetScalarJsonNode(header, "HeaderBorderWidth"));
            SetPropertyIfPresent(properties, "HeaderBorderColor", GetScalarJsonNode(header, "HeaderBorderColor"));
            SetPropertyIfPresent(properties, "HeaderBackColor", GetScalarJsonNode(header, "HeaderBackColor"));
        }

        if (GetMapping(node, "Body") is { } body)
        {
            SetPropertyIfPresent(properties, "BodyCaption", GetScalarJsonNode(body, "BodyCaption"));
            SetPropertyIfPresent(properties, "BodyCaptionPosition", GetScalarJsonNode(body, "BodyCaptionPosition"));
            SetPropertyIfPresent(properties, "BodyForeColor", GetScalarJsonNode(body, "BodyForeColor"));
            SetPropertyIfPresent(properties, "BodyCaptionVisible", GetScalarJsonNode(body, "BodyCaptionVisible"));
            SetPropertyIfPresent(properties, "ShowBodyCaption", GetScalarJsonNode(body, "BodyCaptionVisible"));
            SetPropertyIfPresent(properties, "BodyCornerRadius", GetScalarJsonNode(body, "BodyCornerRadius"));
            SetPropertyIfPresent(properties, "BodyBorderWidth", GetScalarJsonNode(body, "BodyBorderWidth"));
            SetPropertyIfPresent(properties, "BodyBorderColor", GetScalarJsonNode(body, "BodyBorderColor"));
            SetPropertyIfPresent(properties, "BodyBackColor", GetScalarJsonNode(body, "BodyBackColor"));
        }

        if (GetMapping(node, "Footer") is { } footer)
        {
            SetPropertyIfPresent(properties, "ShowFooter", GetScalarJsonNode(footer, "ShowFooter"));
            SetPropertyIfPresent(properties, "FooterCornerRadius", GetScalarJsonNode(footer, "FooterCornerRadius"));
            SetPropertyIfPresent(properties, "FooterBorderWidth", GetScalarJsonNode(footer, "FooterBorderWidth"));
            SetPropertyIfPresent(properties, "FooterBorderColor", GetScalarJsonNode(footer, "FooterBorderColor"));
            SetPropertyIfPresent(properties, "FooterBackColor", GetScalarJsonNode(footer, "FooterBackColor"));
        }

        if (GetSequence(node, "InteractionRules") is { } rules)
        {
            var array = new JsonArray();
            foreach (var ruleNode in rules.Children.OfType<YamlMappingNode>())
            {
                var rule = new JsonObject();
                SetPropertyIfPresent(rule, "Event", GetScalarJsonNode(ruleNode, "Event"));
                SetPropertyIfPresent(rule, "Action", GetScalarJsonNode(ruleNode, "Action"));
                SetPropertyIfPresent(rule, "TargetPath", GetScalarJsonNode(ruleNode, "TargetPath"));
                SetPropertyIfPresent(rule, "FunctionName", GetScalarJsonNode(ruleNode, "FunctionName"));
                SetPropertyIfPresent(rule, "Argument", GetScalarJsonNode(ruleNode, "Argument"));
                array.Add(rule);
            }

            properties["InteractionRules"] = array;
        }

        var children = new List<ProjectUiNode>();
        if (GetWidgetPropertiesMapping(node) is { } control)
        {
            ReadYamlControlProperties(type, control, properties, children);
        }

        var text = GetStringValue(properties, "BodyCaption")
            ?? GetStringValue(properties, "Name")
            ?? type;

        return new ProjectUiNode
        {
            Type = type,
            Text = text,
            X = GetDoubleValue(properties, "X"),
            Y = GetDoubleValue(properties, "Y"),
            Width = GetDoubleValue(properties, "Width"),
            Height = GetDoubleValue(properties, "Height"),
            Properties = properties,
            Children = children
        };
    }

    private static string NormalizeControlType(string? type)
        => string.Equals(type, "PythonValueClient", StringComparison.OrdinalIgnoreCase)
            ? "PythonClient"
            : (type ?? string.Empty);

    private static void ReadYamlControlProperties(string type, YamlMappingNode control, JsonObject properties, List<ProjectUiNode> children)
    {
        SetPropertyIfPresent(properties, "Unit", GetScalarJsonNode(control, "Unit"));
        SetPropertyIfPresent(properties, "TargetPath", GetScalarJsonNode(control, "Uri") ?? GetScalarJsonNode(control, "TargetPath"));
        SetPropertyIfPresent(properties, "PythonScript", GetScalarJsonNode(control, "PythonScript"));
        SetPropertyIfPresent(properties, "Applications", GetScalarJsonNode(control, "Applications") ?? GetScalarJsonNode(control, "PythonEnvironments"));
        SetPropertyIfPresent(properties, "ApplicationAutoStart", GetScalarJsonNode(control, "ApplicationAutoStart") ?? GetScalarJsonNode(control, "PythonEnvAutoStart"));
        if (GetSequence(control, "CustomSignals") is { } customSignals)
        {
            var array = new JsonArray();
            foreach (var signalNode in customSignals.Children.OfType<YamlMappingNode>())
            {
                var signal = new JsonObject();
                foreach (var entry in signalNode.Children)
                {
                    if (entry.Key is not YamlScalarNode keyNode)
                    {
                        continue;
                    }

                    SetPropertyIfPresent(signal, keyNode.Value ?? string.Empty, entry.Value is YamlScalarNode scalar ? ParseScalar(scalar.Value) : null);
                }

                array.Add(signal);
            }

            properties["CustomSignals"] = array;
        }
        var enhancedSignals = GetSequence(control, "EnhancedSignals");
        if (enhancedSignals is not null)
        {
            var array = new JsonArray();
            foreach (var signalNode in enhancedSignals.Children.OfType<YamlMappingNode>())
            {
                var signal = new JsonObject();
                foreach (var entry in signalNode.Children)
                {
                    if (entry.Key is not YamlScalarNode keyNode)
                    {
                        continue;
                    }

                    SetPropertyIfPresent(signal, keyNode.Value ?? string.Empty, entry.Value is YamlScalarNode scalar ? ParseScalar(scalar.Value) : null);
                }

                array.Add(signal);
            }

            properties["EnhancedSignals"] = array;
        }
        SetPropertyIfPresent(properties, "TargetParameterPath", GetScalarJsonNode(control, "Parameter") ?? GetScalarJsonNode(control, "TargetParameterPath"));
        SetPropertyIfPresent(properties, "TargetParameterFormat", GetScalarJsonNode(control, "Format") ?? GetScalarJsonNode(control, "TargetParameterFormat"));
        SetPropertyIfPresent(properties, "IsReadOnly", GetScalarJsonNode(control, "IsReadOnly"));
        SetPropertyIfPresent(properties, "RefreshRateMs", GetScalarJsonNode(control, "RefreshRateMs"));
        SetPropertyIfPresent(properties, "ButtonText", GetScalarJsonNode(control, "ButtonText"));
        SetPropertyIfPresent(properties, "ButtonIcon", GetScalarJsonNode(control, "ButtonIcon"));
        SetPropertyIfPresent(properties, "ButtonIconColor", GetScalarJsonNode(control, "ButtonIconColor"));
        SetPropertyIfPresent(properties, "ButtonBodyBackground", GetScalarJsonNode(control, "ButtonBackColor"));
        SetPropertyIfPresent(properties, "ButtonOnlyIcon", GetScalarJsonNode(control, "ButtonOnlyIcon"));
        SetPropertyIfPresent(properties, "ButtonIconAlign", GetScalarJsonNode(control, "ButtonIconAlign"));
        SetPropertyIfPresent(properties, "ButtonTextAlign", GetScalarJsonNode(control, "ButtonTextAlign"));
        SetPropertyIfPresent(properties, "TargetLog", GetScalarJsonNode(control, "TargetLog"));
        SetPropertyIfPresent(properties, "HistorySeconds", GetScalarJsonNode(control, "HistorySeconds"));
        SetPropertyIfPresent(properties, "ViewSeconds", GetScalarJsonNode(control, "ViewSeconds"));
        SetPropertyIfPresent(properties, "UdlClientHost", GetScalarJsonNode(control, "UdlClientHost"));
        SetPropertyIfPresent(properties, "UdlClientPort", GetScalarJsonNode(control, "UdlClientPort"));
        SetPropertyIfPresent(properties, "UdlClientAutoConnect", GetScalarJsonNode(control, "UdlClientAutoConnect"));
        SetPropertyIfPresent(properties, "UdlClientDebugLogging", GetScalarJsonNode(control, "UdlClientDebugLogging"));
        SetPropertyIfPresent(properties, "UdlClientDemoEnabled", GetScalarJsonNode(control, "UdlClientDemoEnabled"));
        SetPropertyIfPresent(properties, "UdlAttachedItemPaths", GetScalarJsonNode(control, "UdlAttachedItemPaths"));
        SetPropertyIfPresent(properties, "BrokerHost", GetScalarJsonNode(control, "BrokerHost"));
        SetPropertyIfPresent(properties, "BrokerPort", GetScalarJsonNode(control, "BrokerPort"));
        SetPropertyIfPresent(properties, "BrokerBaseTopic", GetScalarJsonNode(control, "BrokerBaseTopic"));
        SetPropertyIfPresent(properties, "BrokerClientId", GetScalarJsonNode(control, "BrokerClientId"));
        SetPropertyIfPresent(properties, "BrokerMode", GetScalarJsonNode(control, "BrokerMode"));
        SetPropertyIfPresent(properties, "BrokerAutoConnect", GetScalarJsonNode(control, "BrokerAutoConnect"));
        SetPropertyIfPresent(properties, "BrokerAttachedItemPaths", GetScalarJsonNode(control, "BrokerAttachedItemPaths"));
        SetPropertyIfPresent(properties, "BrokerPublishedItemPaths", GetScalarJsonNode(control, "BrokerPublishedItemPaths"));
        if (GetSequence(control, "BrokerPublishedItemPaths") is { } brokerPublishedItems)
        {
            var array = new System.Text.Json.Nodes.JsonArray();
            foreach (var child in brokerPublishedItems.Children)
            {
                if (TryConvertYamlNode(child, out var converted))
                {
                    array.Add(converted);
                }
            }

            properties["BrokerPublishedItemPaths"] = array;
        }

        if (GetSequence(control, "ItemExposures") is { } itemExposures)
        {
            var array = new System.Text.Json.Nodes.JsonArray();
            foreach (var child in itemExposures.Children)
            {
                if (TryConvertYamlNode(child, out var converted))
                {
                    array.Add(converted);
                }
            }

            properties["ItemExposures"] = array;
        }

        if (GetSequence(control, "UdlDemoModules") is { } udlDemoModules)
        {
            var array = new System.Text.Json.Nodes.JsonArray();
            foreach (var child in udlDemoModules.Children)
            {
                if (TryConvertYamlNode(child, out var converted))
                {
                    array.Add(converted);
                }
            }

            properties["UdlDemoModuleDefinitions"] = array;
        }

        if (GetSequence(control, "UdlModuleExposures") is { } udlModuleExposures)
        {
            var array = new System.Text.Json.Nodes.JsonArray();
            foreach (var child in udlModuleExposures.Children)
            {
                if (TryConvertYamlNode(child, out var converted))
                {
                    array.Add(converted);
                }
            }

            properties["UdlModuleExposureDefinitions"] = array;
        }

        SetPropertyIfPresent(properties, "ControlHeight", GetScalarJsonNode(control, "ControlHeight"));
        SetPropertyIfPresent(properties, "ListItemHeight", GetScalarJsonNode(control, "ListItemHeight"));
        SetPropertyIfPresent(properties, "Rows", GetScalarJsonNode(control, "Rows"));
        SetPropertyIfPresent(properties, "Columns", GetScalarJsonNode(control, "Columns"));
        SetPropertyIfPresent(properties, "DisplayBackColor", GetScalarJsonNode(control, "DisplayBackColor"));
        SetPropertyIfPresent(properties, "SignalColor", GetScalarJsonNode(control, "SignalColor"));
        SetPropertyIfPresent(properties, "SignalRun", GetScalarJsonNode(control, "SignalRun"));
        SetPropertyIfPresent(properties, "ProgressBar", GetScalarJsonNode(control, "ProgressBar"));
        SetPropertyIfPresent(properties, "ProgressState", GetScalarJsonNode(control, "ProgressState"));
        SetPropertyIfPresent(properties, "ProgressBarColor", GetScalarJsonNode(control, "ProgressBarColor"));

        if (GetSequence(control, "ChartSeriesDefinitions") is { } chartDefinitions)
        {
            var definitions = chartDefinitions.Children
                .OfType<YamlScalarNode>()
                .Select(static child => child.Value ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value));
            properties["ChartSeriesDefinitions"] = string.Join(Environment.NewLine, definitions);
        }

        if (GetSequence(control, "Children") is { } directChildren)
        {
            foreach (var childNode in directChildren.Children.OfType<YamlMappingNode>())
            {
                children.Add(ReadYamlControlNode(childNode));
            }
        }

        if ((string.Equals(type, "TableControl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "CircleDisplay", StringComparison.OrdinalIgnoreCase))
            && GetSequence(control, "Cells") is { } cells)
        {
            foreach (var cellNode in cells.Children.OfType<YamlMappingNode>())
            {
                if (GetMapping(cellNode, "Child") is not { } childMapping)
                {
                    continue;
                }

                var child = ReadYamlControlNode(childMapping);
                var childProperties = child.Properties.DeepClone() as JsonObject ?? new JsonObject();
                SetPropertyIfPresent(childProperties, "TableCellRow", GetScalarJsonNode(cellNode, "Row"));
                SetPropertyIfPresent(childProperties, "TableCellColumn", GetScalarJsonNode(cellNode, "Column"));
                SetPropertyIfPresent(childProperties, "TableCellRowSpan", GetScalarJsonNode(cellNode, "RowSpan"));
                SetPropertyIfPresent(childProperties, "TableCellColumnSpan", GetScalarJsonNode(cellNode, "ColumnSpan"));

                children.Add(new ProjectUiNode
                {
                    Type = child.Type,
                    Text = child.Text,
                    X = child.X,
                    Y = child.Y,
                    Width = child.Width,
                    Height = child.Height,
                    Properties = childProperties,
                    Children = child.Children
                });
            }
        }
    }

    private static YamlMappingNode? GetWidgetPropertiesMapping(YamlMappingNode node)
    {
        return GetMapping(node, "Properties") ?? GetMapping(node, "Control");
    }

    private static YamlMappingNode? GetBoundsMapping(YamlMappingNode node)
    {
        return GetMapping(node, "Bounds") ?? GetMapping(node, "Rect");
    }

    private static void SetPropertyIfPresent(JsonObject target, string propertyName, JsonNode? value)
    {
        if (value is not null)
        {
            target[propertyName] = value;
        }
    }

    private static string? GetScalar(YamlMappingNode node, string key)
    {
        return TryGetChild(node, key, out var child) && child is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }

    private static JsonNode? GetScalarJsonNode(YamlMappingNode node, string key)
    {
        if (!TryGetChild(node, key, out var child) || child is not YamlScalarNode scalar)
        {
            return null;
        }

        return ParseScalar(scalar.Value);
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode node, string key)
    {
        return TryGetChild(node, key, out var child) ? child as YamlMappingNode : null;
    }

    private static YamlSequenceNode? GetSequence(YamlMappingNode node, string key)
    {
        return TryGetChild(node, key, out var child) ? child as YamlSequenceNode : null;
    }

    private static bool TryGetChild(YamlMappingNode node, string key, out YamlNode child)
    {
        foreach (var entry in node.Children)
        {
            if (entry.Key is YamlScalarNode keyNode && string.Equals(keyNode.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                child = entry.Value;
                return true;
            }
        }

        child = null!;
        return false;
    }

    private static JsonNode? ParseScalar(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (bool.TryParse(value, out var boolResult))
        {
            return JsonValue.Create(boolResult);
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
        {
            return JsonValue.Create(intResult);
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var doubleResult))
        {
            return JsonValue.Create(doubleResult);
        }

        return JsonValue.Create(DecodeSerializedString(value));
    }

    private static bool TryConvertYamlNode(YamlNode node, out JsonNode? converted)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                converted = ParseScalar(scalar.Value);
                return true;

            case YamlSequenceNode sequence:
            {
                var array = new JsonArray();
                foreach (var child in sequence.Children)
                {
                    if (TryConvertYamlNode(child, out var childNode))
                    {
                        array.Add(childNode);
                    }
                }

                converted = array;
                return true;
            }

            case YamlMappingNode mapping:
            {
                var obj = new JsonObject();
                foreach (var entry in mapping.Children)
                {
                    if (entry.Key is not YamlScalarNode keyNode || string.IsNullOrWhiteSpace(keyNode.Value))
                    {
                        continue;
                    }

                    if (TryConvertYamlNode(entry.Value, out var childNode))
                    {
                        obj[keyNode.Value] = childNode;
                    }
                }

                converted = obj;
                return true;
            }
        }

        converted = null;
        return false;
    }

    private static string DecodeSerializedString(string value)
    {
        if (string.IsNullOrEmpty(value)
            || (value.IndexOf("\\n", StringComparison.Ordinal) < 0
                && value.IndexOf("\\r", StringComparison.Ordinal) < 0))
        {
            return value;
        }

        return value
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal);
    }

    private static string? GetStringValue(JsonObject properties, string propertyName)
    {
        return properties[propertyName] switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => null
        };
    }

    private static double? GetDoubleValue(JsonObject properties, string propertyName)
    {
        return properties[propertyName] switch
        {
            JsonValue value when value.TryGetValue<double>(out var number) => number,
            JsonValue value when value.TryGetValue<int>(out var intNumber) => intNumber,
            JsonValue value when value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
