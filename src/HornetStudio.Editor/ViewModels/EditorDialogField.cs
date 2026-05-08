using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.ViewModels;

public sealed class EditorDialogField : ObservableObject
{
    private bool _isReadOnly;
    private bool _isVisible = true;

    public EditorDialogField(EditorDialogBindingDefinition definition, ItemProperty parameter)
    {
        Definition = definition;
        Parameter = parameter;
        _isReadOnly = definition.IsReadOnly;

        foreach (var axis in new[] { "Y1", "Y2", "Y3", "Y4" })
        {
            ChartAxisOptions.Add(axis);
        }

        foreach (var style in new[] { "Line", "Step" })
        {
            ChartStyleOptions.Add(style);
        }

        foreach (var eventOption in ItemInteractionRuleCodec.EventOptions)
        {
            InteractionEventOptions.Add(eventOption);
        }

        foreach (var actionOption in ItemInteractionRuleCodec.ActionOptions)
        {
            InteractionActionOptions.Add(actionOption);
        }
    }

    public EditorDialogBindingDefinition Definition { get; }

    public FolderItemModel? OwnerItem { get; internal set; }

    public string OwnerWorkspaceDirectory { get; internal set; } = string.Empty;

    public ItemProperty Parameter { get; }

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    // Display label used in the properties grid; hide the caption
    // completely for the ApplicationExplorer editor so the
    // inline panel can use the full width without a left-hand label.
    public string DisplayLabel => IsApplicationExplorerPicker ? string.Empty : Label;

    public EditorPropertyType PropertyType => Definition.PropertyType;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        internal set
        {
            if (!SetProperty(ref _isReadOnly, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsTextInput));
            RaisePropertyChanged(nameof(ShowPickerButton));
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    public ObservableCollection<string> Options { get; } = [];

    public ObservableCollection<ChartSeriesEditorRow> ChartSeriesEntries { get; } = [];

    public ObservableCollection<AttachItemEditorRow> AttachItemEntries { get; } = [];

    public ObservableCollection<ItemInteractionEditorRow> InteractionRuleEntries { get; } = [];

    public ObservableCollection<string> ChartTargetOptions { get; } = [];

    public ObservableCollection<string> ChartAxisOptions { get; } = [];

    public ObservableCollection<string> ChartStyleOptions { get; } = [];

    public ObservableCollection<string> InteractionEventOptions { get; } = [];

    public ObservableCollection<string> InteractionActionOptions { get; } = [];

    public ObservableCollection<string> InteractionTargetOptions { get; } = [];

    public ObservableCollection<string> InteractionApplicationOptions { get; } = [];

    private readonly Dictionary<string, string> _chartTargetPathByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _chartTargetNameByPath = new(StringComparer.OrdinalIgnoreCase);

    private string _toolTipText = string.Empty;
    private string _newChartTargetPath = string.Empty;
    private string _newChartTargetName = string.Empty;
    private string _newChartAxis = "Y1";
    private string _newChartStyle = "Line";
    private string _newInteractionEventName = "BodyLeftClick";
    private string _newInteractionActionName = "OpenValueEditor";
    private string _newInteractionTargetPath = "this";
    private string _newInteractionFunctionName = string.Empty;
    private string _newInteractionArgument = string.Empty;

    public string ToolTipText
    {
        get => _toolTipText;
        set
        {
            if (!SetProperty(ref _toolTipText, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ToolTipContent));
        }
    }

    public string Value
    {
        get => Parameter.Value?.ToString() ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(Value, normalized, StringComparison.Ordinal))
            {
                return;
            }

            Parameter.Value = normalized;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(PreviewColor));
            RaisePropertyChanged(nameof(PreviewBrush));
            RaisePropertyChanged(nameof(StructuredEditorSummary));

            if (IsChartSeriesList)
            {
                RebuildChartSeriesEntries();
            }

            if (IsInteractionRuleList)
            {
                RebuildInteractionRuleEntries();
            }
        }
    }

    public string NewChartTargetPath
    {
        get => _newChartTargetPath;
        private set => SetProperty(ref _newChartTargetPath, value ?? string.Empty);
    }

    public string NewChartTargetName
    {
        get => _newChartTargetName;
        set
        {
            if (!SetProperty(ref _newChartTargetName, value ?? string.Empty))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_newChartTargetName)
                && _chartTargetPathByName.TryGetValue(_newChartTargetName, out var path)
                && !string.IsNullOrWhiteSpace(path))
            {
                NewChartTargetPath = path;
            }
            else
            {
                NewChartTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(_newChartTargetName);
            }
        }
    }

    public string NewChartAxis
    {
        get => _newChartAxis;
        set => SetProperty(ref _newChartAxis, string.IsNullOrWhiteSpace(value) ? "Y1" : value);
    }

    public string NewChartStyle
    {
        get => _newChartStyle;
        set => SetProperty(ref _newChartStyle, NormalizeStyle(value));
    }

    public string NewInteractionEventName
    {
        get => _newInteractionEventName;
        set => SetProperty(ref _newInteractionEventName, string.IsNullOrWhiteSpace(value) ? "BodyLeftClick" : value);
    }

    public string NewInteractionActionName
    {
        get => _newInteractionActionName;
        set => SetProperty(ref _newInteractionActionName, string.IsNullOrWhiteSpace(value) ? "OpenValueEditor" : value);
    }

    public string NewInteractionTargetPath
    {
        get => _newInteractionTargetPath;
        set => SetProperty(ref _newInteractionTargetPath, string.IsNullOrWhiteSpace(value) ? "this" : value);
    }

    public string NewInteractionFunctionName
    {
        get => _newInteractionFunctionName;
        set => SetProperty(ref _newInteractionFunctionName, value ?? string.Empty);
    }

    public string NewInteractionArgument
    {
        get => _newInteractionArgument;
        set => SetProperty(ref _newInteractionArgument, value ?? string.Empty);
    }

    public bool IsChoice => PropertyType == EditorPropertyType.Choice;

    public bool IsTargetTree => PropertyType == EditorPropertyType.TargetTree;

    public bool IsColor => PropertyType == EditorPropertyType.Color;

    public bool IsMultilineText => PropertyType == EditorPropertyType.MultilineText && !IsApplicationExplorerPicker;

    public bool IsChartSeriesList => PropertyType == EditorPropertyType.ChartSeriesList;

    public bool IsAttachItemList => PropertyType == EditorPropertyType.AttachItemList;

    public bool IsInteractionRuleList => PropertyType == EditorPropertyType.InteractionRuleList;

    public bool IsUdlModuleExposureList => PropertyType == EditorPropertyType.UdlModuleExposureList;

    public bool IsExposureList => IsUdlModuleExposureList;

    public bool IsPythonScriptCreator => string.Equals(Key, "PythonScriptPath", StringComparison.Ordinal);

    public bool IsPythonTemplateSelector => string.Equals(Key, "PythonScriptPath", StringComparison.Ordinal);

    public bool IsApplicationExplorerPicker => string.Equals(Key, "ApplicationDefinitions", StringComparison.Ordinal)
                                              || string.Equals(Key, "PythonEnvDefinitions", StringComparison.Ordinal);

    public bool IsTextInput => !IsChoice
                               && !IsTargetTree
                               && !IsReadOnly
                               && !IsMultilineText
                               && !IsChartSeriesList
                               && !IsAttachItemList
                               && !IsInteractionRuleList
                               && !IsUdlModuleExposureList
                               && !IsApplicationExplorerPicker;

    public bool ShowPickerButton => IsColor && !IsReadOnly;

    public string PreviewColor => string.IsNullOrWhiteSpace(Value) ? "Transparent" : Value;

    public object? ToolTipContent => string.IsNullOrWhiteSpace(ToolTipText) ? null : ToolTipText;

    public bool HasToolTipText => !string.IsNullOrWhiteSpace(ToolTipText);

    public string? InputWatermark => IsColor ? "Theme" : null;

    private bool IsCsvSignalAttachList => IsAttachItemList && string.Equals(Key, "CsvSignalPaths", StringComparison.Ordinal);

    private bool IsBrokerAttachList => IsAttachItemList && string.Equals(Key, "BrokerAttachedItemPaths", StringComparison.Ordinal);

    private bool IsBrokerPublishList => IsAttachItemList && string.Equals(Key, "BrokerPublishedItemPaths", StringComparison.Ordinal);

    public string StructuredEditorSummary => PropertyType switch
    {
        EditorPropertyType.TargetTree => string.IsNullOrWhiteSpace(Value)
            ? "No target selected"
            : Value,
        EditorPropertyType.ChartSeriesList => ChartSeriesEntries.Count == 0
            ? "No series configured"
            : $"{ChartSeriesEntries.Count} series configured",
        EditorPropertyType.AttachItemList => AttachItemEntries.Count(static row => !row.IsGroup && !row.IsRemoved) == 0
            ? "No items available"
            : IsBrokerPublishList
                ? $"{AttachItemEntries.Count(static row => row.IsAttached)} of {AttachItemEntries.Count(static row => !row.IsGroup && !row.IsRemoved)} items selected"
                : $"{AttachItemEntries.Count(static row => row.IsAttached)} of {AttachItemEntries.Count(static row => !row.IsGroup && !row.IsRemoved)} items attached",
        EditorPropertyType.InteractionRuleList => InteractionRuleEntries.Count == 0
            ? "Default left click opens value editor"
            : $"{InteractionRuleEntries.Count} rules configured",
        EditorPropertyType.UdlModuleExposureList => UdlModuleExposureDefinitionCodec.ParseDefinitions(Value).Count switch
        {
            0 => "No module exposures configured",
            1 => "1 module exposure configured",
            var count => $"{count} module exposures configured"
        },
        _ => string.Empty
    };

    public bool IsIconPathSelector => string.Equals(Key, "ButtonIcon", StringComparison.Ordinal);

    public bool IsFolderSelector => string.Equals(Key, "CsvDirectory", StringComparison.Ordinal);

    public IBrush PreviewBrush
    {
        get
        {
            if (!IsColor || string.IsNullOrWhiteSpace(Value))
            {
                return Brushes.Transparent;
            }

            return Color.TryParse(Value, out var color)
                ? new SolidColorBrush(color)
                : Brushes.Transparent;
        }
    }

    public void InitializeChartSeriesEditor()
    {
        if (!IsChartSeriesList)
        {
            return;
        }

        ChartTargetOptions.Clear();
        _chartTargetPathByName.Clear();
        _chartTargetNameByPath.Clear();

        var baseOptions = Options.Count > 0
            ? Options.Distinct(StringComparer.OrdinalIgnoreCase)
            : HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display);

        foreach (var raw in baseOptions
                     .Where(static option => !string.IsNullOrWhiteSpace(option))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var separatorIndex = raw.IndexOf('|');
            string name;
            string path;

            if (separatorIndex >= 0)
            {
                name = raw[..separatorIndex];
                path = raw[(separatorIndex + 1)..];
            }
            else
            {
                name = raw;
                path = raw;
            }

            name = name.Trim();
            path = TargetPathHelper.NormalizeConfiguredTargetPath(path);

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            _chartTargetPathByName[name] = path;
            _chartTargetNameByPath[path] = name;
            ChartTargetOptions.Add(name);
        }

        if (string.IsNullOrWhiteSpace(NewChartTargetName) && ChartTargetOptions.Count > 0)
        {
            NewChartTargetName = ChartTargetOptions[0];
        }

        RebuildChartSeriesEntries();
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    public void InitializeAttachItemEditor()
    {
        if (!IsAttachItemList)
        {
            return;
        }

        RebuildAttachItemEntries();
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    public void InitializeInteractionRuleEditor()
    {
        if (!IsInteractionRuleList)
        {
            return;
        }

        RefreshInteractionRuleOptions(
            HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display).OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
            []);
    }

    public void RefreshAttachItemOptions(IEnumerable<string> options)
    {
        if (!IsAttachItemList)
        {
            return;
        }

        Options.Clear();
        foreach (var option in options.Where(static option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Options.Add(option);
        }

        RebuildAttachItemEntries();
    }

    public void RefreshTargetTreeOptions(IEnumerable<string> options)
    {
        if (!IsTargetTree)
        {
            return;
        }

        Options.Clear();
        foreach (var option in options.Where(static option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Options.Add(option);
        }

        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    public void RefreshInteractionRuleOptions(IEnumerable<string> options, IEnumerable<string>? pythonEnvironmentOptions)
    {
        if (!IsInteractionRuleList)
        {
            return;
        }

        InteractionTargetOptions.Clear();
        InteractionTargetOptions.Add("this");
        foreach (var option in options.Where(static option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InteractionTargetOptions.Add(option);
        }

        InteractionApplicationOptions.Clear();
        foreach (var option in (pythonEnvironmentOptions ?? [])
                     .Where(static option => !string.IsNullOrWhiteSpace(option))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InteractionApplicationOptions.Add(option);
        }

        if (string.IsNullOrWhiteSpace(NewInteractionTargetPath))
        {
            NewInteractionTargetPath = "this";
        }

        RebuildInteractionRuleEntries();
    }

    public void AddChartSeriesEntry()
    {
        if (!IsChartSeriesList || string.IsNullOrWhiteSpace(NewChartTargetPath))
        {
            return;
        }

        var row = CreateChartSeriesEntry(NewChartTargetPath, NewChartAxis, NewChartStyle);
        ChartSeriesEntries.Add(row);
        SyncChartSeriesValueFromEntries();
    }

    public List<ChartSeriesEditorRow> CreateChartSeriesSnapshot()
        => ChartSeriesEntries.Select(CloneChartSeriesEntry).ToList();

    public void ApplyChartSeriesEntries(IEnumerable<ChartSeriesEditorRow> rows)
    {
        if (!IsChartSeriesList)
        {
            return;
        }

        foreach (var row in ChartSeriesEntries)
        {
            row.PropertyChanged -= OnChartSeriesRowPropertyChanged;
        }

        ChartSeriesEntries.Clear();
        foreach (var row in rows.Where(static row => !string.IsNullOrWhiteSpace(row.TargetName) || !string.IsNullOrWhiteSpace(row.TargetPath)))
        {
            var target = ResolveChartSeriesTargetPath(string.IsNullOrWhiteSpace(row.TargetName) ? row.TargetPath : row.TargetName);
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            ChartSeriesEntries.Add(CreateChartSeriesEntry(target, row.Axis, row.Style));
        }

        SyncChartSeriesValueFromEntries();
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    public void RemoveChartSeriesEntry(ChartSeriesEditorRow row)
    {
        if (!IsChartSeriesList)
        {
            return;
        }

        row.PropertyChanged -= OnChartSeriesRowPropertyChanged;
        ChartSeriesEntries.Remove(row);
        SyncChartSeriesValueFromEntries();
    }

    public List<ItemInteractionEditorRow> CreateInteractionRuleSnapshot()
        => InteractionRuleEntries.Select(CloneInteractionRuleEntry).ToList();

    public void ApplyInteractionRuleEntries(IEnumerable<ItemInteractionEditorRow> rows)
    {
        if (!IsInteractionRuleList)
        {
            return;
        }

        foreach (var row in InteractionRuleEntries)
        {
            row.PropertyChanged -= OnInteractionRuleRowPropertyChanged;
        }

        InteractionRuleEntries.Clear();
        foreach (var row in rows)
        {
            InteractionRuleEntries.Add(CreateInteractionRuleEntry(row.EventName, row.ActionName, row.TargetPath, row.FunctionName, row.Argument));
        }

        SyncInteractionRuleValueFromEntries();
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    public void RemoveInteractionRuleEntry(ItemInteractionEditorRow row)
    {
        if (!IsInteractionRuleList)
        {
            return;
        }

        row.PropertyChanged -= OnInteractionRuleRowPropertyChanged;
        InteractionRuleEntries.Remove(row);
        SyncInteractionRuleValueFromEntries();
    }

    private void RebuildChartSeriesEntries()
    {
        foreach (var row in ChartSeriesEntries)
        {
            row.PropertyChanged -= OnChartSeriesRowPropertyChanged;
        }

        ChartSeriesEntries.Clear();
        var lines = Value.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var targetPath = parts.Length > 0 ? parts[0] : string.Empty;
            var axis = parts.Length > 1 ? NormalizeAxis(parts[1]) : "Y1";
            var style = parts.Length > 2 ? NormalizeStyle(parts[2]) : "Line";
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                continue;
            }

            ChartSeriesEntries.Add(CreateChartSeriesEntry(targetPath, axis, style));
        }

        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private void RebuildInteractionRuleEntries()
    {
        foreach (var row in InteractionRuleEntries)
        {
            row.PropertyChanged -= OnInteractionRuleRowPropertyChanged;
        }

        InteractionRuleEntries.Clear();
        foreach (var rule in ItemInteractionRuleCodec.ParseDefinitions(Value))
        {
            InteractionRuleEntries.Add(CreateInteractionRuleEntry(rule.Event.ToString(), rule.Action.ToString(), rule.TargetPath, rule.FunctionName, rule.Argument));
        }

        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private ChartSeriesEditorRow CreateChartSeriesEntry(string targetPath, string axis, string style)
    {
        var row = new ChartSeriesEditorRow
        {
            TargetPath = targetPath,
            Axis = NormalizeAxis(axis),
            Style = NormalizeStyle(style)
        };

        foreach (var option in ChartTargetOptions)
        {
            row.TargetOptions.Add(option);
        }

        foreach (var option in ChartAxisOptions)
        {
            row.AxisOptions.Add(option);
        }

        foreach (var option in ChartStyleOptions)
        {
            row.StyleOptions.Add(option);
        }

        if (_chartTargetNameByPath.TryGetValue(row.TargetPath, out var displayName))
        {
            row.TargetName = displayName;
        }
        else
        {
            row.TargetName = row.TargetPath;
        }

        row.PropertyChanged += OnChartSeriesRowPropertyChanged;
        return row;
    }

    private ItemInteractionEditorRow CreateInteractionRuleEntry(string eventName, string actionName, string targetPath, string functionName, string argument)
    {
        var row = new ItemInteractionEditorRow
        {
            EventName = string.IsNullOrWhiteSpace(eventName) ? "BodyLeftClick" : eventName,
            ActionName = string.IsNullOrWhiteSpace(actionName) ? "OpenValueEditor" : actionName,
            TargetPath = string.IsNullOrWhiteSpace(targetPath) ? "this" : targetPath,
            FunctionName = functionName ?? string.Empty,
            Argument = argument ?? string.Empty
        };

        foreach (var option in InteractionEventOptions)
        {
            row.EventOptions.Add(option);
        }

        foreach (var option in InteractionActionOptions)
        {
            row.ActionOptions.Add(option);
        }

        foreach (var option in GetInteractionTargetOptions(row.ActionName))
        {
            row.TargetOptions.Add(option);
        }

        if (!row.TargetOptions.Contains(row.TargetPath))
        {
            row.TargetOptions.Add(row.TargetPath);
        }

        foreach (var option in GetInteractionFunctionOptions(row.TargetPath))
        {
            row.FunctionOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(row.FunctionName) && !row.FunctionOptions.Contains(row.FunctionName))
        {
            row.FunctionOptions.Add(row.FunctionName);
        }

        row.PropertyChanged += OnInteractionRuleRowPropertyChanged;
        return row;
    }

    private void OnChartSeriesRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartSeriesEditorRow.TargetPath) or nameof(ChartSeriesEditorRow.Axis) or nameof(ChartSeriesEditorRow.Style))
        {
            SyncChartSeriesValueFromEntries();
            return;
        }

        if (e.PropertyName is nameof(ChartSeriesEditorRow.TargetName) && sender is ChartSeriesEditorRow row)
        {
            var mapped = ResolveChartSeriesTargetPath(row.TargetName);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                row.TargetPath = mapped;
            }
        }
    }

    private void OnInteractionRuleRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ItemInteractionEditorRow.EventName)
            or nameof(ItemInteractionEditorRow.ActionName)
            or nameof(ItemInteractionEditorRow.TargetPath)
            or nameof(ItemInteractionEditorRow.FunctionName)
            or nameof(ItemInteractionEditorRow.Argument))
        {
            if (sender is ItemInteractionEditorRow row
                && e.PropertyName is nameof(ItemInteractionEditorRow.ActionName) or nameof(ItemInteractionEditorRow.TargetPath))
            {
                RefreshInteractionRuleRowOptions(row);
            }

            SyncInteractionRuleValueFromEntries();
        }
    }

    public IReadOnlyList<string> GetInteractionTargetOptions(string? actionName)
        => string.Equals(actionName, nameof(ItemInteractionAction.InvokePythonFunction), StringComparison.OrdinalIgnoreCase)
            ? InteractionApplicationOptions.ToArray()
            : InteractionTargetOptions.ToArray();

    public IReadOnlyList<string> GetInteractionFunctionOptions(string? targetPath)
        => string.IsNullOrWhiteSpace(targetPath)
            ? Array.Empty<string>()
            : PythonClientRuntimeRegistry.GetFunctionNames(HornetStudio.Editor.Widgets.ApplicationExplorerRuntime.ResolveInteractionTargetPath(null, targetPath));

    public void RefreshInteractionRuleRowOptions(ItemInteractionEditorRow row)
    {
        row.TargetOptions.Clear();
        foreach (var option in GetInteractionTargetOptions(row.ActionName))
        {
            row.TargetOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(row.TargetPath) && !row.TargetOptions.Contains(row.TargetPath))
        {
            row.TargetOptions.Add(row.TargetPath);
        }

        row.FunctionOptions.Clear();
        foreach (var option in GetInteractionFunctionOptions(row.TargetPath))
        {
            row.FunctionOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(row.FunctionName) && !row.FunctionOptions.Contains(row.FunctionName))
        {
            row.FunctionOptions.Add(row.FunctionName);
        }
    }

    private void SyncChartSeriesValueFromEntries()
    {
        var serialized = string.Join(Environment.NewLine, ChartSeriesEntries
            .Where(row => !string.IsNullOrWhiteSpace(row.TargetPath))
            .Select(row => SerializeChartSeriesEntry(row)));

        Parameter.Value = TargetPathHelper.NormalizeChartSeriesDefinitions(serialized);
        RaisePropertyChanged(nameof(Value));
    }

    private void SyncInteractionRuleValueFromEntries()
    {
        var serialized = ItemInteractionRuleCodec.SerializeDefinitions(InteractionRuleEntries.Select(static row => new ItemInteractionRule
        {
            Event = Enum.TryParse<ItemInteractionEvent>(row.EventName, ignoreCase: true, out var eventKind) ? eventKind : ItemInteractionEvent.BodyLeftClick,
            Action = Enum.TryParse<ItemInteractionAction>(row.ActionName, ignoreCase: true, out var actionKind) ? actionKind : ItemInteractionAction.OpenValueEditor,
            TargetPath = row.TargetPath,
            FunctionName = row.FunctionName,
            Argument = row.Argument
        }));

        Parameter.Value = serialized;
        RaisePropertyChanged(nameof(Value));
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private static string SerializeChartSeriesEntry(ChartSeriesEditorRow row)
    {
        var axis = NormalizeAxis(row.Axis);
        var style = NormalizeStyle(row.Style);
        return style == "Line"
            ? $"{row.TargetPath}|{axis}"
            : $"{row.TargetPath}|{axis}|{style}";
    }

    private static string NormalizeAxis(string? axis)
    {
        if (string.IsNullOrWhiteSpace(axis))
        {
            return "Y1";
        }

        var trimmed = axis.Trim();
        if (trimmed.StartsWith("Y", true, CultureInfo.InvariantCulture))
        {
            trimmed = trimmed[1..];
        }

        return int.TryParse(trimmed, out var axisIndex)
            ? $"Y{Math.Clamp(axisIndex, 1, 4)}"
            : "Y1";
    }

    private static string NormalizeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return "Line";
        }

        return style.Trim().ToLowerInvariant() switch
        {
            "step" => "Step",
            "stephorizontal" => "Step",
            "line" => "Line",
            "straight" => "Line",
            _ => "Line"
        };
    }

    private ChartSeriesEditorRow CloneChartSeriesEntry(ChartSeriesEditorRow source)
    {
        var row = new ChartSeriesEditorRow
        {
            TargetPath = source.TargetPath,
            Axis = NormalizeAxis(source.Axis),
            Style = NormalizeStyle(source.Style)
        };

        foreach (var option in ChartTargetOptions)
        {
            row.TargetOptions.Add(option);
        }

        foreach (var option in ChartAxisOptions)
        {
            row.AxisOptions.Add(option);
        }

        foreach (var option in ChartStyleOptions)
        {
            row.StyleOptions.Add(option);
        }

        if (_chartTargetNameByPath.TryGetValue(row.TargetPath, out var displayName))
        {
            row.TargetName = displayName;
        }
        else
        {
            row.TargetName = row.TargetPath;
        }

        return row;
    }

    private string ResolveChartSeriesTargetPath(string? displayOrPath)
    {
        if (!string.IsNullOrWhiteSpace(displayOrPath)
            && _chartTargetPathByName.TryGetValue(displayOrPath, out var mapped)
            && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return TargetPathHelper.NormalizeConfiguredTargetPath(displayOrPath);
    }

    private ItemInteractionEditorRow CloneInteractionRuleEntry(ItemInteractionEditorRow source)
    {
        var row = new ItemInteractionEditorRow
        {
            EventName = source.EventName,
            ActionName = source.ActionName,
            TargetPath = source.TargetPath,
            FunctionName = source.FunctionName,
            Argument = source.Argument
        };

        foreach (var option in InteractionEventOptions)
        {
            row.EventOptions.Add(option);
        }

        foreach (var option in InteractionActionOptions)
        {
            row.ActionOptions.Add(option);
        }

        foreach (var option in GetInteractionTargetOptions(row.ActionName))
        {
            row.TargetOptions.Add(option);
        }

        if (!row.TargetOptions.Contains(row.TargetPath))
        {
            row.TargetOptions.Add(row.TargetPath);
        }

        foreach (var option in GetInteractionFunctionOptions(row.TargetPath))
        {
            row.FunctionOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(row.FunctionName) && !row.FunctionOptions.Contains(row.FunctionName))
        {
            row.FunctionOptions.Add(row.FunctionName);
        }

        return row;
    }

    public List<AttachItemEditorRow> CreateAttachItemSnapshot()
        => AttachItemEntries.Select(static row => new AttachItemEditorRow
        {
            RelativePath = row.RelativePath,
            IsAttached = row.IsAttached,
            IntervalMs = row.IntervalMs,
            DisplayName = row.DisplayName,
            DisplaySource = row.DisplaySource,
            Level = row.Level,
            IsGroup = row.IsGroup,
            IsMissing = row.IsMissing,
            IsRemoved = row.IsRemoved
        }).ToList();

    public void ApplyAttachItemEntries(IEnumerable<AttachItemEditorRow> rows)
    {
        if (!IsAttachItemList)
        {
            return;
        }

        foreach (var row in AttachItemEntries)
        {
            row.PropertyChanged -= OnAttachItemRowPropertyChanged;
        }

        AttachItemEntries.Clear();
        foreach (var row in rows)
        {
            var copy = new AttachItemEditorRow
            {
                RelativePath = row.RelativePath,
                IsAttached = row.IsAttached,
                IntervalMs = row.IntervalMs,
                DisplayName = row.DisplayName,
                DisplaySource = row.DisplaySource,
                Level = row.Level,
                IsGroup = row.IsGroup,
                IsMissing = row.IsMissing,
                IsRemoved = row.IsRemoved
            };

            copy.PropertyChanged += OnAttachItemRowPropertyChanged;
            AttachItemEntries.Add(copy);
        }

        Parameter.Value = SerializeAttachItemEntries();
        RaisePropertyChanged(nameof(Value));
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private void RebuildAttachItemEntries()
    {
        foreach (var row in AttachItemEntries)
        {
            row.PropertyChanged -= OnAttachItemRowPropertyChanged;
        }

        AttachItemEntries.Clear();
        if (IsCsvSignalAttachList)
        {
            RebuildCsvSignalAttachEntries();
        }
        else if (IsBrokerAttachList)
        {
            RebuildBrokerAttachEntries();
        }
        else if (IsBrokerPublishList)
        {
            RebuildBrokerPublishEntries();
        }
        else
        {
            var selectedPaths = Value
                .Replace("\r", string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var option in Options)
            {
                var row = new AttachItemEditorRow
                {
                    RelativePath = option,
                    IsAttached = selectedPaths.Contains(option),
                    IntervalMs = 1000
                };

                row.PropertyChanged += OnAttachItemRowPropertyChanged;
                AttachItemEntries.Add(row);
            }
        }

        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private void OnAttachItemRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AttachItemEditorRow.IsAttached)
            && e.PropertyName != nameof(AttachItemEditorRow.IsRemoved)
            && (!IsCsvSignalAttachList || e.PropertyName != nameof(AttachItemEditorRow.IntervalMs)))
        {
            return;
        }

        Parameter.Value = SerializeAttachItemEntries();
        RaisePropertyChanged(nameof(Value));
        RaisePropertyChanged(nameof(StructuredEditorSummary));
    }

    private string SerializeAttachItemEntries()
    {
        if (!IsAttachItemList)
        {
            return Value;
        }

        if (IsBrokerPublishList)
        {
            var existingDefinitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(Value);
            var selectedRootPaths = AttachItemEntries
                .Where(static row => row.IsAttached && !row.IsGroup && !row.IsRemoved)
                .Select(static row => TargetPathHelper.NormalizeConfiguredTargetPath(row.RelativePath))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var selectedRootSet = selectedRootPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingForSelectedRoots = existingDefinitions
                .Where(definition => selectedRootSet.Contains(definition.LocalRootPath)
                    || selectedRootSet.Contains(definition.LocalPath))
                .ToArray();
            var existingRootSet = existingForSelectedRoots
                .Select(static definition => definition.LocalRootPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newRootDefinitions = selectedRootPaths
                .Where(path => !existingRootSet.Contains(path))
                .Select(BrokerPublishedItemDefinitionCodec.CreateDefault);
            var definitions = existingForSelectedRoots.Concat(newRootDefinitions);
            return BrokerPublishedItemDefinitionCodec.SerializeDefinitions(definitions);
        }

        if (!IsCsvSignalAttachList)
        {
            return string.Join(Environment.NewLine, AttachItemEntries
                .Where(static row => row.IsAttached && !row.IsGroup && !row.IsRemoved)
                .Select(static row => row.RelativePath));
        }

        var serialized = string.Join(Environment.NewLine, AttachItemEntries
            .Where(static row => row.IsAttached)
            .Select(static row => SerializeCsvSignalAttachRow(row)));

        return serialized;
    }

    private static string SerializeCsvSignalAttachRow(AttachItemEditorRow row)
    {
        // RelativePath for signals comes from GetCsvSignalOptions and has the form
        //   Name|TargetPath
        // or
        //   Name|TargetPath|Unit
        // We add an optional 4th field for the interval if IntervalMs > 0.
        var parts = row.RelativePath.Split('|', StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var path = parts.Length > 1 ? parts[1].Trim() : name;
        var unit = parts.Length > 2 ? parts[2].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (row.IntervalMs > 0)
        {
            return string.IsNullOrWhiteSpace(unit)
                ? $"{name}|{path}||{row.IntervalMs}"
                : $"{name}|{path}|{unit}|{row.IntervalMs}";
        }

        return string.IsNullOrWhiteSpace(unit)
            ? $"{name}|{path}"
            : $"{name}|{path}|{unit}";
    }

    private void RebuildCsvSignalAttachEntries()
    {
        // Parse current value lines (may already include per-signal intervals)
        var selectedLines = Value
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var intervalsByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in selectedLines)
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string pathPart;
            if (parts.Length == 1)
            {
                pathPart = parts[0].Trim();
            }
            else
            {
                pathPart = parts[1].Trim();
            }

            if (string.IsNullOrWhiteSpace(pathPart))
            {
                continue;
            }

            selectedByPath.Add(pathPart);

            if (parts.Length > 3 && int.TryParse(parts[3].Trim(), out var parsedInterval) && parsedInterval > 0)
            {
                intervalsByPath[pathPart] = parsedInterval;
            }
        }

        foreach (var option in Options)
        {
            var optionParts = option.Split('|', StringSplitOptions.TrimEntries);
            string optionPath;
            if (optionParts.Length == 1)
            {
                optionPath = optionParts[0].Trim();
            }
            else
            {
                optionPath = optionParts[1].Trim();
            }

            var row = new AttachItemEditorRow
            {
                RelativePath = option,
                IsAttached = selectedByPath.Contains(optionPath),
                IntervalMs = intervalsByPath.TryGetValue(optionPath, out var interval) ? interval : 1000
            };

            row.PropertyChanged += OnAttachItemRowPropertyChanged;
            AttachItemEntries.Add(row);
        }
    }

    private void RebuildBrokerAttachEntries()
    {
        var livePaths = Options
            .Select(static path => TargetPathHelper.ToBrokerReceivedAttachIdentity(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedPaths = Value
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static path => TargetPathHelper.ToBrokerReceivedAttachIdentity(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allLeafPaths = livePaths
            .Concat(selectedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => GetBrokerDisplayParts(path).Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => GetBrokerDisplayParts(path).Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in allLeafPaths)
        {
            var displayParts = GetBrokerDisplayParts(path);
            if (string.IsNullOrWhiteSpace(displayParts.Name))
            {
                continue;
            }

            var row = new AttachItemEditorRow
            {
                RelativePath = path,
                DisplayName = displayParts.Name,
                DisplaySource = displayParts.Source,
                IsAttached = selectedPaths.Contains(path),
                IsMissing = !livePaths.Contains(path),
                IntervalMs = 1000
            };

            row.PropertyChanged += OnAttachItemRowPropertyChanged;
            AttachItemEntries.Add(row);
        }
    }

    private void RebuildBrokerPublishEntries()
    {
        var livePaths = Options
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var definitions = BrokerPublishedItemDefinitionCodec.ParseDefinitions(Value);
        var selectedPaths = definitions
            .Select(static definition => definition.LocalRootPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allPaths = livePaths
            .Concat(selectedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => GetBrokerPublishDisplayParts(path).Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => GetBrokerPublishDisplayParts(path).Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in allPaths)
        {
            var displayParts = GetBrokerPublishDisplayParts(path);
            if (string.IsNullOrWhiteSpace(displayParts.Name))
            {
                continue;
            }

            var row = new AttachItemEditorRow
            {
                RelativePath = path,
                DisplayName = displayParts.Name,
                DisplaySource = displayParts.Source,
                IsAttached = selectedPaths.Contains(path),
                IsMissing = !livePaths.Contains(path),
                IntervalMs = 1000
            };

            row.PropertyChanged += OnAttachItemRowPropertyChanged;
            AttachItemEntries.Add(row);
        }
    }

    private static (string Name, string Source) GetBrokerPublishDisplayParts(string path)
    {
        path = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var segments = TargetPathHelper.SplitPathSegments(path)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (segments[0], string.Empty),
            2 => (segments[1], segments[0]),
            _ when string.Equals(segments[0], "Project", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(segments[0], "UdlProject", StringComparison.OrdinalIgnoreCase)
                => (string.Join('.', segments.Skip(2)), $"{segments[0]} -> {segments[1]}"),
            _ => (segments[^1], string.Join('.', segments.Take(segments.Length - 1)))
        };
    }

    private static (string Name, string Source) GetBrokerDisplayParts(string fullPath)
    {
        fullPath = TargetPathHelper.ToBrokerReceivedAttachIdentity(fullPath);
        var segments = TargetPathHelper.SplitPathSegments(fullPath)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (segments[0], string.Empty),
            2 => (segments[1], segments[0]),
            _ => (string.Join('.', segments.Skip(2)), $"{segments[0]} -> {segments[1]}")
        };
    }
}
