using System.Collections.Generic;
using System.Text.Json.Serialization;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Persistence;

public sealed class LayoutDocument
{
    public string TabStripPlacement { get; init; } = "Right";

    [JsonPropertyName("Folders")]
    public List<FolderDocument> Folders { get; init; } = [];

    [JsonPropertyName("Pages")]
    public List<FolderDocument>? LegacyPages { get; init; }
}

public sealed class FolderDocument
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public List<FolderItemDocument> Items { get; init; } = [];
}

public sealed class FolderItemDocument
{
    private List<ExtendedSignalDefinitionDocument> _enhancedSignals = [];

    public ControlKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string ControlCaption { get; init; } = string.Empty;

    public bool SyncText { get; init; } = true;

    public bool CaptionVisible { get; init; } = true;

    public bool ShowCaption { get; init; } = true;

    public string BodyCaption { get; init; } = string.Empty;

    public string BodyCaptionPosition { get; init; } = "Top";

    public bool BodyCaptionVisible { get; init; } = true;

    public bool ShowBodyCaption { get; init; } = true;

    public bool ShowFooter { get; init; } = true;

    public string Header { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Footer { get; init; } = string.Empty;

    public string? HeaderForeColor { get; init; }

    public string? HeaderBackColor { get; init; }

    public string? HeaderBorderColor { get; init; }

    public double HeaderBorderWidth { get; init; }

    public double HeaderCornerRadius { get; init; } = 6;

    public string? BodyForeColor { get; init; }

    public string? BodyBackColor { get; init; }

    public string? BodyBorderColor { get; init; }

    public double BodyBorderWidth { get; init; }

    public double BodyCornerRadius { get; init; }

    public string? FooterForeColor { get; init; }

    public string? FooterBackColor { get; init; }

    public string? FooterBorderColor { get; init; }

    public double FooterBorderWidth { get; init; }

    public double FooterCornerRadius { get; init; } = 6;

    public string ToolTipText { get; init; } = string.Empty;

    public string ButtonText { get; init; } = string.Empty;

    public string ButtonIcon { get; init; } = string.Empty;

    public bool ButtonOnlyIcon { get; init; }

    public string ButtonIconAlign { get; init; } = "Left";

    public string ButtonTextAlign { get; init; } = "Center";

    public string ButtonCommand { get; init; } = string.Empty;

    public string ButtonBodyBackground { get; init; } = "";

    public string ButtonBodyForegroundColor { get; init; } = string.Empty;

    public string ButtonIconColor { get; init; } = string.Empty;

    public bool UseThemeColor { get; init; } = true;

    public bool Enabled { get; init; } = true;

    public string? BackgroundColor { get; init; }

    public string? BorderColor { get; init; }

    public string? ContainerBorder { get; init; }

    public string? ContainerBackgroundColor { get; init; }

    public double ContainerBorderWidth { get; init; } = 0;

    public double BorderWidth { get; init; } = 1;

    public double CornerRadius { get; init; } = 12;

    public string? PrimaryForegroundColor { get; init; }

    public string? SecondaryForegroundColor { get; init; }

    public string? AccentBackgroundColor { get; init; }

    public string? AccentForegroundColor { get; init; }

    public string TargetPath { get; init; } = string.Empty;

    public string TargetPropertyPath { get; init; } = string.Empty;

    public string TargetPropertyFormat { get; init; } = string.Empty;

    public string Applications { get; init; } = string.Empty;

    public List<CustomSignalDefinitionDocument> CustomSignals { get; init; } = [];

    [JsonPropertyName("EnhancedSignals")]
    public List<ExtendedSignalDefinitionDocument> EnhancedSignals
    {
        get => _enhancedSignals;
        init => _enhancedSignals = value ?? [];
    }

    public bool ApplicationAutoStart { get; init; }

    [JsonPropertyName("PythonEnvironments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPythonEnvironments { get; init; }

    [JsonPropertyName("PythonEnvAutoStart")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyPythonEnvAutoStart { get; init; }

    public string Unit { get; init; } = string.Empty;

    public string TargetLog { get; init; } = "Logs.Host";

    public int RefreshRateMs { get; init; } = 1000;

    public int HistorySeconds { get; init; } = 120;

    public int ViewSeconds { get; init; } = 30;

    public string ChartSeriesDefinitions { get; init; } = string.Empty;

    public List<ItemInteractionRuleDocument> InteractionRules { get; init; } = [];

    public string UdlClientHost { get; init; } = "192.168.178.151";

    public int UdlClientPort { get; init; } = 9001;

    public bool UdlClientAutoConnect { get; init; }

    public bool UdlClientDebugLogging { get; init; }

    public bool UdlClientDemoEnabled { get; init; }

    public string UdlAttachedItemPaths { get; init; } = string.Empty;

    public string UdlDemoModuleDefinitions { get; init; } = string.Empty;

    public string UdlModuleExposureDefinitions { get; init; } = string.Empty;

    public string BrokerHost { get; init; } = BrokerWidgetDefaults.Host;

    public int BrokerPort { get; init; } = BrokerWidgetDefaults.Port;

    public string BrokerBaseTopic { get; init; } = BrokerWidgetDefaults.BaseTopic;

    public string ServerClientId { get; init; } = BrokerWidgetDefaults.ClientIdDisplay;

    public string BrokerMode { get; init; } = BrokerWidgetModes.External;

    public bool BrokerAutoConnect { get; init; }

    public string BrokerAttachedItemPaths { get; init; } = string.Empty;

    public string BrokerPublishedItemPaths { get; init; } = string.Empty;

    public string ItemExposures { get; init; } = string.Empty;

    public string CsvDirectory { get; init; } = string.Empty;

    public string CsvFilename { get; init; } = string.Empty;

    public bool CsvAddTimestamp { get; init; } = true;

    public int CsvIntervalMs { get; init; } = 1000;

    public string CsvSignalPaths { get; init; } = string.Empty;

    public bool CsvSplitDaily { get; init; }

    public string CsvSplitDailyTime { get; init; } = "00:00:00";

    public int CsvSplitMaxFileSizeMb { get; init; }

    public string CsvPersistenceMode { get; init; } = "Balanced";

    public int CsvFlushIntervalMs { get; init; }

    public int CsvFlushBatchSize { get; init; }

    public string CameraName { get; init; } = string.Empty;

    public string CameraResolution { get; init; } = string.Empty;

    public string CameraOverlayText { get; init; } = string.Empty;

    public bool IsReadOnly { get; init; }

    public bool IsAutoHeight { get; init; } = true;

    public double ListItemHeight { get; init; } = 72;

    public double ControlHeight { get; init; } = 72;

    public int TableRows { get; init; }

    public int TableColumns { get; init; }

    public string? DisplayBackColor { get; init; }

    public string? SignalColor { get; init; }

    public bool SignalRun { get; init; }

    public bool ProgressBar { get; init; }

    public double ProgressState { get; init; }

    public string? ProgressBarColor { get; init; }

    public double ControlBorderWidth { get; init; } = 0;

    public string? ControlBorderColor { get; init; }

    public double ControlCornerRadius { get; init; } = 0;

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public List<FolderItemDocument> Items { get; init; } = [];
}

public sealed class ItemInteractionRuleDocument
{
    public ItemInteractionEvent Event { get; init; } = ItemInteractionEvent.BodyLeftClick;

    public ItemInteractionAction Action { get; init; } = ItemInteractionAction.OpenValueEditor;

    public string TargetPath { get; init; } = "this";

    public string FunctionName { get; init; } = string.Empty;

    public string Argument { get; init; } = string.Empty;
}


