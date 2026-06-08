using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Functions;
using HornetStudio.Host;
using HornetStudio.Logging;
using HornetStudio.Host.Python.Client;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets.Workflow;
using Serilog.Events;
using HornetStudio.Editor.Signals;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Models;

public enum ControlKind
{
    Button,
    Signal,
    ItemModel,
    WidgetList,
    TableControl,
    CircleDisplay,
    LogControl,
    ChartControl,
    ItemClient,
    CsvLoggerControl,
    SqlLoggerControl,
    CameraControl,
    PythonClient,
    ApplicationExplorer,
    CustomSignals,
    ControllerWidget,
    Monitor,
    MonitorView,
    Functions,
    DialogWidget
}

public sealed class FolderItemModel : ObservableObject
{
    private const string ValueRefPathPropertyName = "valueRefPath";
    private const string ValueRefParameterPropertyName = "valueRefParameter";
    private static readonly ConcurrentDictionary<string, RunningDeclarativeFunctionExecution> RunningDeclarativeFunctions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Occurs when the catalog function execution state changes.
    /// </summary>
    public static event EventHandler? CatalogFunctionExecutionStateChanged;

    private static readonly Typeface ItemValueTypeface = new(new FontFamily("Calibri"), FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface ItemUnitTypeface = new(new FontFamily("Calibri"), FontStyle.Italic, FontWeight.Normal);

    private const string LightBackground = "#FFFFFF";
    private const string LightInnerBackground = "#FFFFFF";
    private const string LightBorder = "#30343A";
    private const string LightPrimaryForeground = "#111827";
    private const string LightSecondaryForeground = "#374151";
    private const string LightMutedForeground = "#667085";
    private const string LightAccentBackground = "#DBEAFE";
    private const string LightAccentForeground = "#1D4ED8";

    private const string DarkBackground = "#111111";
    private const string DarkInnerBackground = "#1A1A1A";
    private const string DarkBorder = "#6B7280";
    private const string DarkPrimaryForeground = "#F9FAFB";
    private const string DarkSecondaryForeground = "#D1D5DB";
    private const string DarkMutedForeground = "#9CA3AF";
    private const string DarkAccentBackground = "#1E3A8A";
    private const string DarkAccentForeground = "#DBEAFE";
    private const string CircleDisplayDefaultSignalColor = "#FFC107";
    private const string CircleDisplayDefaultProgressBarColor = "#FFC107";
    private const string CircleDisplaySignalColorItemName = "signal_color";
    private const string CircleDisplaySignalRunItemName = "signal_run";
    private const string CircleDisplayProgressBarItemName = "progress_bar";
    private const string CircleDisplayProgressStateItemName = "progress_state";
    private const string CircleDisplayProgressBarColorItemName = "progress_bar_color";
    private const string CircleDisplaySignalColorText = "SignalColor";
    private const string CircleDisplaySignalRunText = "SignalRun";
    private const string CircleDisplayProgressBarText = "ProgressBar";
    private const string CircleDisplayProgressStateText = "ProgressState";
    private const string CircleDisplayProgressBarColorText = "ProgressBarColor";
    private const int FooterSubItemColumns = 2;
    private const double FooterSubItemRowSpacing = 6;
    private const double FooterSubItemDesiredHeight = 32;
    private const double FooterHorizontalPadding = 16;

    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private bool _isSelected;
    private bool _isMasterSelected;
    private bool _isAutoHeight = true;
    private bool _isApplyingListHeight;
    private bool _isDarkThemeApplied;
    private double _listItemHeight = 72;
    private double _borderWidth = 1;
    private double _cornerRadius = 12;
    private string _draftListItemHeightText = "72";
    private FolderItemModel? _selectedListItem;
    private FolderItemModel? _parentWidgetList;
    private string _header = string.Empty;
    private string _title = string.Empty;
    private string _bodyCaption = string.Empty;
    private bool _syncText = true;
    private string _footer = string.Empty;
    private string _bodyCaptionPosition = "Top";
    private bool _showCaption = true;
    private bool _showBodyCaption = true;
    private bool _showFooter = true;
    private string _toolTipText = string.Empty;
    private string _buttonText = string.Empty;
    private string _buttonIcon = string.Empty;
    private bool _buttonOnlyIcon;
    private string _buttonIconAlign = "Left";
    private string _buttonTextAlign = "Center";
    private string _buttonCommand = string.Empty;
    private string _buttonBodyBackground = string.Empty;
    private string _buttonBodyForegroundColor = string.Empty;
    private string _buttonIconColor = string.Empty;
    private bool _useThemeColor = true;
    private string? _backgroundColor;
    private string? _borderColor;
    private string? _containerBorder;
    private string? _containerBackgroundColor;
    private double _containerBorderWidth;
    private double _controlBorderWidth;
    private string? _controlBorderColor;
    private double _controlCornerRadius;
    private string? _primaryForegroundColor;
    private string? _secondaryForegroundColor;
    private string? _accentBackgroundColor;
    private string? _accentForegroundColor;
    private string? _headerForeColor;
    private string? _headerBackColor;
    private string? _headerBorderColor;
    private double _headerBorderWidth;
    private double _headerCornerRadius = 6;
    private string? _bodyForeColor;
    private string? _bodyBackColor;
    private string? _displayBackColor;
    private string? _bodyBorderColor;
    private double _bodyBorderWidth;
    private double _bodyCornerRadius;
    private string? _footerForeColor;
    private string? _footerBackColor;
    private string? _footerBorderColor;
    private double _footerBorderWidth;
    private double _footerCornerRadius = 6;
    private string _targetPath = string.Empty;
    private string? _signalColor = CircleDisplayDefaultSignalColor;
    private bool _signalRun;
    private bool _progressBar;
    private double _progressState;
    private string? _progressBarColor = CircleDisplayDefaultProgressBarColor;
    private string _pythonScriptPath = string.Empty;
    private string _applicationDefinitions = string.Empty;
    private string _customSignalDefinitions = string.Empty;
    private string _controllerDefinitions = string.Empty;
    private string _monitorDefinitions = string.Empty;
    private string _selectedMonitorIds = string.Empty;
    private string _onActiveColor = string.Empty;
    private bool _applicationAutoStart;
    private string _blockedLegacyScriptPath = string.Empty;
    private DateTime _blockedLegacyScriptWriteTimeUtc;
    private string _targetParameterPath = string.Empty;
    private string _targetParameterFormat = string.Empty;
    private string _unit = string.Empty;
    private string _targetLog = "Logs.Host";
    private bool _autoCreateLog;
    private int _view = 1;
    private int _activeViewId = 1;
    private int _historySeconds = 120;
    private int _viewSeconds = 30;
    private string _chartSeriesDefinitions = string.Empty;
    private string _interactionRules = string.Empty;
    private string _visualRules = string.Empty;
    private bool _enabled = true;
    private string _csvDirectory = string.Empty;
    private string _csvFilename = string.Empty;
    private bool _csvAddTimestamp = true;
    private int _csvIntervalMs = 1000;
    private string _csvSignalPaths = string.Empty;
    private bool _csvSplitDaily;
    private string _csvSplitDailyTime = "00:00:00";
    private int _csvSplitMaxFileSizeMb;
    private string _csvPersistenceMode = "Balanced";
    private int _csvFlushIntervalMs;
    private int _csvFlushBatchSize;
    private string _cameraName = string.Empty;
    private string _cameraResolution = string.Empty;
    private string _cameraOverlayText = string.Empty;
    private string _udlClientHost = "192.168.178.151";
    private int _udlClientPort = 9001;
    private bool _udlClientAutoConnect;
    private bool _udlClientDebugLogging;
    private bool _udlClientDemoEnabled;
    private string _brokerHost = ItemClientDefaults.Host;
    private int _brokerPort = ItemClientDefaults.Port;
    private string _brokerBaseTopic = ItemClientDefaults.BaseTopic;
    private string _brokerClientId = ItemClientId.Create();
    private string _brokerMode = ItemClientModes.External;
    private bool _brokerAutoConnect;
    private string _brokerAttachedItemPaths = string.Empty;
    private string _brokerPublishedItemPaths = string.Empty;
    private string _itemExposures = string.Empty;
    private string _udlAttachedItemPaths = string.Empty;
    private string _udlDemoModuleDefinitions = string.Empty;
    private string _udlModuleExposureDefinitions = string.Empty;
    private ItemModel? _target;
    private int _refreshRateMs = 1000;
    private bool _isReadOnly;
    private string _effectiveBackground = LightBackground;
    private string _effectiveInnerBackground = LightInnerBackground;
    private string _effectiveBorderBrush = LightBorder;
    private string _effectivePrimaryForeground = LightPrimaryForeground;
    private string _effectiveSecondaryForeground = LightSecondaryForeground;
    private string _effectiveMutedForeground = LightMutedForeground;
    private string _effectiveAccentBackground = LightAccentBackground;
    private string _effectiveAccentForeground = LightAccentForeground;
    private string _effectiveHeaderForeground = LightSecondaryForeground;
    private string _effectiveHeaderBackground = "Transparent";
    private string _effectiveHeaderBorder = LightBorder;
    private string _effectiveBodyForeground = LightPrimaryForeground;
    private string _effectiveBodyBackground = "Transparent";
    private string _effectiveDisplayBackColor = LightInnerBackground;
    private string? _visualRuleButtonBackColorOverride;
    private string _effectiveBodyBorder = LightBorder;
    private string _effectiveFooterForeground = LightMutedForeground;
    private string _effectiveFooterBackground = "Transparent";
    private string _effectiveFooterBorder = LightBorder;
    private DispatcherTimer? _visualRuleBlinkTimer;
    private bool _visualRuleBlinkPhaseVisible = true;
    private DispatcherTimer? _pendingRefreshTimer;
    private DispatcherTimer? _scriptTimer;
    private int _registryRefreshQueued;
    private int _targetRefreshQueued;
    private int _targetValueRefreshQueued;
    private readonly object _targetRefreshSync = new();
    private readonly object _targetValueRefreshSync = new();
    private DateTimeOffset _lastTargetRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSignalReceiveUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTargetRefreshQueuedUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _targetRefreshQueuedUtc = DateTimeOffset.MinValue;
    private string _targetRefreshQueuedPath = string.Empty;
    private string _targetRefreshQueuedModule = string.Empty;
    private DateTimeOffset _targetValueRefreshQueuedUtc = DateTimeOffset.MinValue;
    private string _targetValueRefreshQueuedPath = string.Empty;
    private string _targetValueRefreshQueuedModule = string.Empty;
    private bool _hasPendingTargetRefresh;
    private bool _hasPendingQueuedTargetRefresh;
    private PendingTargetRefresh _pendingQueuedTargetRefresh;
    private bool _hasPendingTargetValueRefresh;
    private PendingTargetValueRefresh _pendingTargetValueRefresh;
    private string _lastSignalDiagnosticsPath = string.Empty;
    private string _lastSignalDiagnosticsModule = string.Empty;
    private string _lastSignalRawValueSignature = string.Empty;
    private string _lastSignalResolvedDisplayValue = string.Empty;
    private string _lastSignalDisplayText = string.Empty;
    private string _cachedSignalValueRefPath = string.Empty;
    private string _cachedSignalValueRefParameter = string.Empty;
    private ItemModel? _cachedSignalValueRefRuntimeItem;
    private readonly string _signalValueSchedulerSubscriptionKey = Guid.NewGuid().ToString("N");
    private SignalLiveValueBindingDescriptor? _resolvedSignalLiveValueBinding;
    private long _lastAppliedSignalLiveValueVersion = -1;
    private string _unresolvedSignalTargetPath = string.Empty;
    private string _lastTargetParameterViewSignature = string.Empty;
    private string _lastItemBodyPresentationSignature = string.Empty;
    private object? _scriptValue;
    private string _name = string.Empty;
    private string _id = Guid.NewGuid().ToString("N");
    private string _path = string.Empty;
    private string _pageName = string.Empty;
    private string _folderLayoutPath = string.Empty;
    private FolderItemModel? _parentItem;
    private int _tableRows = 2;
    private int _tableColumns = 2;
    private int _tableCellRow = 1;
    private int _tableCellColumn = 1;
    private int _tableCellRowSpan = 1;
    private int _tableCellColumnSpan = 1;
    public FolderItemModel()
    {
        HostRegistries.Data.ItemChanged += OnDataRegistryChanged;
        Items.CollectionChanged += OnItemsCollectionChanged;
        // Ensure table controls start with an initialized cell grid.
        RefreshTableCellSlots();
    }

    public ControlKind Kind { get; init; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                RefreshPathRecursive();
            }
        }
    }

    public string GetLoggerRuntimeBasePath()
    {
        return BuildLoggerRuntimeBasePath("logger_runtime");
    }

    public string GetLoggerLegacyRuntimeBasePath()
    {
        return BuildLoggerRuntimeBasePath("Loggerruntime");
    }

    public string GetLoggerRuntimePath(string runtimeItemName)
    {
        var basePath = GetLoggerRuntimeBasePath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var normalizedRuntimeItemName = TargetPathHelper.NormalizeConfiguredTargetPath(runtimeItemName);
        return string.IsNullOrWhiteSpace(normalizedRuntimeItemName)
            ? basePath
            : $"{basePath}.{normalizedRuntimeItemName}";
    }

    public string GetLoggerLegacyRuntimePath(string runtimeItemName)
    {
        var basePath = GetLoggerLegacyRuntimeBasePath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var normalizedRuntimeItemName = TargetPathHelper.NormalizeConfiguredTargetPath(runtimeItemName);
        return string.IsNullOrWhiteSpace(normalizedRuntimeItemName)
            ? basePath
            : $"{basePath}.{normalizedRuntimeItemName}";
    }

    public string GetDisplayRuntimeBasePath()
    {
        if (!IsCircleDisplay)
        {
            return string.Empty;
        }

        var folderName = TargetPathHelper.NormalizeConfiguredTargetPath(FolderName);
        var itemName = TargetPathHelper.NormalizeConfiguredTargetPath(Name);
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        return $"studio.{folderName}.display_runtime.{itemName}";
    }

    public string GetDisplayRuntimePath(string runtimeItemName)
    {
        var basePath = GetDisplayRuntimeBasePath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var normalizedRuntimeItemName = TargetPathHelper.NormalizeConfiguredTargetPath(runtimeItemName);
        return string.IsNullOrWhiteSpace(normalizedRuntimeItemName)
            ? basePath
            : $"{basePath}.{normalizedRuntimeItemName}";
    }

    public string GetLoggerConfiguredOutputPath()
    {
        var directory = string.IsNullOrWhiteSpace(CsvDirectory)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HornetStudioLogs")
            : CsvDirectory.Trim();

        var filename = string.IsNullOrWhiteSpace(CsvFilename)
            ? (!string.IsNullOrWhiteSpace(Name)
                ? Name.Trim()
                : (IsSqlLoggerControl ? "SqlLogger" : "CsvLogger"))
            : CsvFilename.Trim();

        if (IsSqlLoggerControl)
        {
            if (!filename.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                filename += ".db";
            }
        }
        else if (!filename.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".csv";
        }

        return System.IO.Path.Combine(directory, filename);
    }

    public bool TryApplyLoggerOutputPath(string? outputPath)
    {
        var normalizedPath = outputPath?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var directory = System.IO.Path.GetDirectoryName(normalizedPath);
        var fileName = System.IO.Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        CsvDirectory = directory;
        CsvFilename = fileName;
        return true;
    }

    public bool LoggerRuntimeBindingMatchesRegistryChange(string? runtimePath, DataChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(runtimePath))
        {
            return false;
        }

        var legacyRuntimePath = runtimePath.Replace(".logger_runtime.", ".Loggerruntime.", StringComparison.OrdinalIgnoreCase);

        return MatchesLoggerRuntimePath(e.Key, runtimePath)
               || MatchesLoggerRuntimePath(e.Key, legacyRuntimePath);
    }

    private string BuildLoggerRuntimeBasePath(string runtimeSegment)
    {
        if (!IsCsvLoggerControl && !IsSqlLoggerControl)
        {
            return string.Empty;
        }

        var folderName = TargetPathHelper.NormalizeConfiguredTargetPath(FolderName);
        var itemName = TargetPathHelper.NormalizeConfiguredTargetPath(Name);
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        return $"studio.{folderName}.{runtimeSegment}.{itemName}";
    }

    private static bool MatchesLoggerRuntimePath(string? changedPath, string? runtimePath)
    {
        if (string.IsNullOrWhiteSpace(changedPath) || string.IsNullOrWhiteSpace(runtimePath))
        {
            return false;
        }

        return TargetPathHelper.PathsEqual(changedPath, runtimePath)
               || TargetPathHelper.IsDescendantPath(changedPath, runtimePath)
               || TargetPathHelper.IsDescendantPath(runtimePath, changedPath);
    }

    private static bool TryResolveCircleDisplayTargetCore(string candidatePath, out ItemModel? targetItem, out ItemProperty? parameter)
    {
        if (HostRegistries.Data.TryResolve(candidatePath, out targetItem) && targetItem is not null)
        {
            parameter = targetItem.Properties.Has("read") ? targetItem.Properties["read"] : null;
            return true;
        }

        targetItem = null;
        parameter = null;
        return false;
    }

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public string UiNodeType { get; set; } = string.Empty;

    public JsonObject UiProperties { get; set; } = [];

    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public string FolderName
    {
        get => _pageName;
        private set => SetProperty(ref _pageName, value);
    }

    public string? FolderLayoutPath => string.IsNullOrWhiteSpace(_folderLayoutPath) ? null : _folderLayoutPath;

    public string CsvDirectory
    {
        get => _csvDirectory;
        set => SetProperty(ref _csvDirectory, value ?? string.Empty);
    }

    public string CsvFilename
    {
        get => _csvFilename;
        set => SetProperty(ref _csvFilename, value ?? string.Empty);
    }

    public bool CsvAddTimestamp
    {
        get => _csvAddTimestamp;
        set => SetProperty(ref _csvAddTimestamp, value);
    }

    public int CsvIntervalMs
    {
        get => _csvIntervalMs;
        set => SetProperty(ref _csvIntervalMs, value);
    }

    public string CsvSignalPaths
    {
        get => _csvSignalPaths;
        set
        {
            if (SetProperty(ref _csvSignalPaths, value ?? string.Empty))
            {
            }
        }
    }

    public string CameraName
    {
        get => _cameraName;
        set => SetProperty(ref _cameraName, value ?? string.Empty);
    }

    public string CameraResolution
    {
        get => _cameraResolution;
        set => SetProperty(ref _cameraResolution, value ?? string.Empty);
    }

    public string CameraOverlayText
    {
        get => _cameraOverlayText;
        set => SetProperty(ref _cameraOverlayText, value ?? string.Empty);
    }

    public int TableRows
    {
        get => _tableRows;
        set
        {
            var normalized = System.Math.Max(1, value);
            if (SetProperty(ref _tableRows, normalized))
            {
                RefreshTableCellSlots();
            }
        }
    }

    public int TableColumns
    {
        get => _tableColumns;
        set
        {
            var normalized = System.Math.Max(1, value);
            if (SetProperty(ref _tableColumns, normalized))
            {
                RefreshTableCellSlots();
            }
        }
    }

    public string? SignalColor
    {
        get => _signalColor;
        set
        {
            if (SetProperty(ref _signalColor, value))
            {
                EnsureCircleDisplayRuntimeSignals();
            }
        }
    }

    public bool SignalRun
    {
        get => _signalRun;
        set
        {
            if (SetProperty(ref _signalRun, value))
            {
                EnsureCircleDisplayRuntimeSignals();
            }
        }
    }

    public bool ProgressBar
    {
        get => _progressBar;
        set
        {
            if (SetProperty(ref _progressBar, value))
            {
                EnsureCircleDisplayRuntimeSignals();
            }
        }
    }

    public double ProgressState
    {
        get => _progressState;
        set
        {
            var normalized = System.Math.Clamp(value, 0d, 100d);
            if (SetProperty(ref _progressState, normalized))
            {
                EnsureCircleDisplayRuntimeSignals();
            }
        }
    }

    public string? ProgressBarColor
    {
        get => _progressBarColor;
        set
        {
            if (SetProperty(ref _progressBarColor, value))
            {
                EnsureCircleDisplayRuntimeSignals();
            }
        }
    }

    // Positionierung eines Kindes innerhalb eines TableControl
    public int TableCellRow
    {
        get => _tableCellRow;
        set => SetProperty(ref _tableCellRow, System.Math.Max(1, value));
    }

    public int TableCellColumn
    {
        get => _tableCellColumn;
        set => SetProperty(ref _tableCellColumn, System.Math.Max(1, value));
    }

    public int TableCellRowSpan
    {
        get => _tableCellRowSpan;
        set => SetProperty(ref _tableCellRowSpan, System.Math.Max(1, value));
    }

    public int TableCellColumnSpan
    {
        get => _tableCellColumnSpan;
        set => SetProperty(ref _tableCellColumnSpan, System.Math.Max(1, value));
    }

    public void RefreshTableCellSlots()
    {
        TableCellSlots.Clear();
        for (var row = 1; row <= TableRows; row++)
        {
            for (var column = 1; column <= TableColumns; column++)
            {
                TableCellSlots.Add(new TableCellSlot(this) { Row = row, Column = column });
            }
        }

        UpdateTableCellContentFromChildren();
    }

    public void UpdateTableCellContentFromChildren()
    {
        if (!UsesTableLayout)
        {
            return;
        }

        foreach (var slot in TableCellSlots)
        {
            slot.ContentLabel = null;
        }

        foreach (var child in Items)
        {
            var maxRow = System.Math.Min(TableRows, child.TableCellRow + child.TableCellRowSpan - 1);
            var maxColumn = System.Math.Min(TableColumns, child.TableCellColumn + child.TableCellColumnSpan - 1);

            for (var row = child.TableCellRow; row <= maxRow; row++)
            {
                for (var column = child.TableCellColumn; column <= maxColumn; column++)
                {
                    var slot = TableCellSlots.FirstOrDefault(s => s.Row == row && s.Column == column);
                    if (slot is not null)
                    {
                        slot.ContentLabel = string.IsNullOrWhiteSpace(child.Name) ? "ItemModel" : child.Name;
                    }
                }
            }
        }
    }

    public bool IsCircleCellVisible(int row, int column)
    {
        if (!IsCircleDisplay || TableRows <= 0 || TableColumns <= 0)
        {
            return true;
        }

        var top = (((row - 1) / (double)TableRows) * 2d) - 1d;
        var bottom = ((row / (double)TableRows) * 2d) - 1d;
        var left = (((column - 1) / (double)TableColumns) * 2d) - 1d;
        var right = ((column / (double)TableColumns) * 2d) - 1d;

        return IsInsideCircle(top, left)
            && IsInsideCircle(top, right)
            && IsInsideCircle(bottom, left)
            && IsInsideCircle(bottom, right);
    }

    public string Header
    {
        get => _header;
        set
        {
            var hadControlCaption = ShowControlCaption;
            if (!SetProperty(ref _header, value))
            {
                return;
            }

            if (Target is null)
            {
                RaisePropertyChanged(nameof(TargetPropertyView));
                RaisePropertyChanged(nameof(ItemBodyPresentation));
            }

            RaisePropertyChanged(nameof(WidgetCaption));
            RaisePropertyChanged(nameof(ControlCaption));
            RaisePropertyChanged(nameof(ShowControlCaption));

            if (hadControlCaption != ShowControlCaption)
            {
                RaisePropertyChanged(nameof(ItemHeaderHeight));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            var hadControlCaption = ShowControlCaption;
            if (SetProperty(ref _title, value))
            {
                if (Target is null)
                {
                    RaisePropertyChanged(nameof(DisplayValue));
                    RaisePropertyChanged(nameof(TargetPropertyView));
                    RaisePropertyChanged(nameof(ItemBodyPresentation));
                    RaisePropertyChanged(nameof(ItemValueFontSize));
                }

                RaisePropertyChanged(nameof(EffectiveButtonText));
                RaisePropertyChanged(nameof(ShowButtonText));
                RaisePropertyChanged(nameof(WidgetCaption));
                RaisePropertyChanged(nameof(ControlCaption));
                RaisePropertyChanged(nameof(ShowControlCaption));

                if (hadControlCaption != ShowControlCaption)
                {
                    RaisePropertyChanged(nameof(ItemHeaderHeight));
                    RaisePropertyChanged(nameof(ItemBodyHeight));
                    RaisePropertyChanged(nameof(AvailableBodyHeight));
                    RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                    RaisePropertyChanged(nameof(ItemUnitFontSize));
                    RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                    RaisePropertyChanged(nameof(ButtonBodyFontSize));
                    RaisePropertyChanged(nameof(ButtonIconSize));
                }
            }
        }
    }

    public bool SyncText
    {
        get => _syncText;
        set
        {
            var hadControlCaption = ShowControlCaption;
            if (!SetProperty(ref _syncText, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(WidgetCaption));
            RaisePropertyChanged(nameof(ControlCaption));
            RaisePropertyChanged(nameof(ShowControlCaption));

            if (hadControlCaption != ShowControlCaption)
            {
                RaisePropertyChanged(nameof(ItemHeaderHeight));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
            }
        }
    }

    public string Footer
    {
        get => _footer;
        set
        {
            if (SetProperty(ref _footer, value))
            {
                if (Target is null)
                {
                    RaisePropertyChanged(nameof(DisplayUnit));
                    RaisePropertyChanged(nameof(TargetPropertyView));
                    RaisePropertyChanged(nameof(ItemBodyPresentation));
                    RaisePropertyChanged(nameof(ItemUnitWidth));
                    RaisePropertyChanged(nameof(ItemValueFontSize));
                    RaisePropertyChanged(nameof(CanOpenValueEditor));
                }

                RaisePropertyChanged(nameof(ShowButtonFooter));
                RaisePropertyChanged(nameof(ShowItemFooterPanel));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetProperty(ref _unit, value ?? string.Empty))
            {
                if (Target is null)
                {
                    RaisePropertyChanged(nameof(DisplayUnit));
                    RaisePropertyChanged(nameof(TargetPropertyView));
                    RaisePropertyChanged(nameof(ItemBodyPresentation));
                    RaisePropertyChanged(nameof(ItemUnitWidth));
                    RaisePropertyChanged(nameof(ItemValueFontSize));
                    RaisePropertyChanged(nameof(CanOpenValueEditor));
                }
            }
        }
    }

    public bool BodyCaptionVisible
    {
        get => _showBodyCaption && !string.IsNullOrWhiteSpace(BodyCaption);
        set
        {
            if (SetProperty(ref _showBodyCaption, value))
            {
                RaisePropertyChanged(nameof(ShowBodyCaption));
                RaisePropertyChanged(nameof(ShowTopBodyCaption));
                RaisePropertyChanged(nameof(ShowInlineBodyCaption));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
            }
        }
    }

    public bool ShowBodyCaption
    {
        get => BodyCaptionVisible;
        set => BodyCaptionVisible = value;
    }

    public string BodyCaptionPosition
    {
        get => _bodyCaptionPosition;
        set
        {
            var normalized = NormalizeBodyCaptionPosition(value);
            if (SetProperty(ref _bodyCaptionPosition, normalized))
            {
                RaisePropertyChanged(nameof(ShowTopBodyCaption));
                RaisePropertyChanged(nameof(ShowInlineBodyCaption));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
            }
        }
    }

    public bool CaptionVisible
    {
        get => _showCaption;
        set
        {
            if (SetProperty(ref _showCaption, value))
            {
                RaisePropertyChanged(nameof(ShowCaption));
                RaisePropertyChanged(nameof(ShowControlCaption));
                RaisePropertyChanged(nameof(ItemHeaderHeight));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
            }
        }
    }

    public bool ShowCaption
    {
        get => CaptionVisible;
        set => CaptionVisible = value;
    }

    public bool ShowFooter
    {
        get => _showFooter;
        set
        {
            if (SetProperty(ref _showFooter, value))
            {
                RaisePropertyChanged(nameof(ShowButtonFooter));
                RaisePropertyChanged(nameof(ShowItemFooterPanel));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
            }
        }
    }

    public string ToolTipText
    {
        get => _toolTipText;
        set
        {
            if (SetProperty(ref _toolTipText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(EffectiveToolTipText));
            }
        }
    }

    public string ButtonText
    {
        get => _buttonText;
        set
        {
            if (SetProperty(ref _buttonText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(EffectiveButtonText));
                RaisePropertyChanged(nameof(ShowButtonText));
            }
        }
    }

    public string ButtonIcon
    {
        get => _buttonIcon;
        set
        {
            var normalizedIconPath = IconPathHelper.NormalizeStoredPath(value, FolderLayoutPath);
            if (SetProperty(ref _buttonIcon, normalizedIconPath))
            {
                RaisePropertyChanged(nameof(HasButtonIcon));
                RaisePropertyChanged(nameof(ShowButtonIcon));
                RaisePropertyChanged(nameof(EffectiveButtonIconPath));
                RaisePropertyChanged(nameof(ShowButtonText));
                RaisePropertyChanged(nameof(ShowLeftButtonIcon));
                RaisePropertyChanged(nameof(ShowCenterButtonIcon));
                RaisePropertyChanged(nameof(ShowRightButtonIcon));
            }
        }
    }

    public bool ButtonOnlyIcon
    {
        get => _buttonOnlyIcon;
        set
        {
            if (SetProperty(ref _buttonOnlyIcon, value))
            {
                RaisePropertyChanged(nameof(ShowButtonText));
                RaisePropertyChanged(nameof(ShowButtonIcon));
                RaisePropertyChanged(nameof(EffectiveButtonIconPath));
                RaisePropertyChanged(nameof(ShowLeftButtonIcon));
                RaisePropertyChanged(nameof(ShowCenterButtonIcon));
                RaisePropertyChanged(nameof(ShowRightButtonIcon));
            }
        }
    }

    public string ButtonIconAlign
    {
        get => _buttonIconAlign;
        set
        {
            if (SetProperty(ref _buttonIconAlign, NormalizeAlignment(value, "Left")))
            {
                RaisePropertyChanged(nameof(ButtonIconHorizontalAlignment));
                RaisePropertyChanged(nameof(ShowLeftButtonIcon));
                RaisePropertyChanged(nameof(ShowCenterButtonIcon));
                RaisePropertyChanged(nameof(ShowRightButtonIcon));
            }
        }
    }

    public string ButtonTextAlign
    {
        get => _buttonTextAlign;
        set
        {
            if (SetProperty(ref _buttonTextAlign, NormalizeAlignment(value, "Center")))
            {
                RaisePropertyChanged(nameof(ButtonTextHorizontalAlignment));
            }
        }
    }

    public string ButtonCommand
    {
        get => _buttonCommand;
        set
        {
            if (SetProperty(ref _buttonCommand, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(EffectiveButtonCommand));
            }
        }
    }

    public string ButtonBodyBackground
    {
        get => _buttonBodyBackground;
        set
        {
            if (SetProperty(ref _buttonBodyBackground, string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()))
            {
                RaisePropertyChanged(nameof(EffectiveButtonBodyBackground));
                RaisePropertyChanged(nameof(EffectiveButtonBodyBackgroundBrush));
                RaisePropertyChanged(nameof(EffectiveButtonHoverBackground));
                RaisePropertyChanged(nameof(EffectiveButtonHoverBackgroundBrush));
                RaisePropertyChanged(nameof(EffectiveButtonPressBackground));
                RaisePropertyChanged(nameof(EffectiveButtonPressBackgroundBrush));
            }
        }
    }

    public string ButtonBodyForegroundColor
    {
        get => _buttonBodyForegroundColor;
        set
        {
            if (SetProperty(ref _buttonBodyForegroundColor, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(EffectiveButtonBodyForeground));
                RaisePropertyChanged(nameof(EffectiveButtonBodyForegroundBrush));
                RaisePropertyChanged(nameof(ButtonIconCss));
                RaisePropertyChanged(nameof(EffectiveButtonIconTintColor));
            }
        }
    }

    public string ButtonIconColor
    {
        get => _buttonIconColor;
        set
        {
            if (SetProperty(ref _buttonIconColor, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(ButtonIconCss));
                RaisePropertyChanged(nameof(EffectiveButtonIconTintColor));
            }
        }
    }

    public bool UseThemeColor
    {
        get => _useThemeColor;
        set
        {
            if (SetProperty(ref _useThemeColor, value))
            {
                RaisePropertyChanged(nameof(ButtonIconCss));
                RaisePropertyChanged(nameof(EffectiveButtonIconTintColor));
            }
        }
    }

    public ObservableCollection<FolderItemModel> Items { get; } = [];
    public ObservableCollection<TableCellSlot> TableCellSlots { get; } = new ObservableCollection<TableCellSlot>();

    public bool IsButton => Kind == ControlKind.Button;

    public bool IsSignal => Kind == ControlKind.Signal;

    public bool IsItem => Kind == ControlKind.ItemModel || Kind == ControlKind.Signal;

    public bool IsWidgetList => Kind == ControlKind.WidgetList;

    public bool IsTableControl => Kind == ControlKind.TableControl;

    public bool IsDialogWidget => Kind == ControlKind.DialogWidget;

    public bool IsCircleDisplay => Kind == ControlKind.CircleDisplay;

    public bool UsesTableLayout => Kind is ControlKind.TableControl or ControlKind.CircleDisplay or ControlKind.DialogWidget;

    public bool IsLogControl => Kind == ControlKind.LogControl;

    public bool IsCsvLoggerControl => Kind == ControlKind.CsvLoggerControl;

    public bool IsSqlLoggerControl => Kind == ControlKind.SqlLoggerControl;

    public bool IsCameraControl => Kind == ControlKind.CameraControl;

    public bool IsChartControl => Kind == ControlKind.ChartControl;

    public bool IsItemClient => Kind == ControlKind.ItemClient;

    public bool IsApplicationExplorer => Kind == ControlKind.ApplicationExplorer;

    public bool IsCustomSignals => Kind == ControlKind.CustomSignals;

    public bool IsControllerWidget => Kind == ControlKind.ControllerWidget;

    public bool IsMonitor => Kind == ControlKind.Monitor;

    public bool IsMonitorView => Kind == ControlKind.MonitorView;

    public bool IsFunctions => Kind == ControlKind.Functions;

    // Controls, die als Child in einem Table gerendert und selektiert werden duerfen.
    public bool IsTableChildControl => Kind is ControlKind.ItemModel
        or ControlKind.Signal
        or ControlKind.Button
        or ControlKind.LogControl
        or ControlKind.ChartControl
        or ControlKind.ItemClient
        or ControlKind.CsvLoggerControl
        or ControlKind.SqlLoggerControl
        or ControlKind.CameraControl
        or ControlKind.ApplicationExplorer
        or ControlKind.CustomSignals
        or ControlKind.ControllerWidget
        or ControlKind.MonitorView
        or ControlKind.Functions;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsMasterSelected
    {
        get => _isMasterSelected;
        set => SetProperty(ref _isMasterSelected, value);
    }

    public bool IsAutoHeight
    {
        get => _isAutoHeight;
        set
        {
            if (SetProperty(ref _isAutoHeight, value))
            {
                ApplyListHeightRules();
                SyncDraftListItemHeightText();
                RaisePropertyChanged(nameof(CurrentListItemHeight));
                RaisePropertyChanged(nameof(CanEditListHeight));
                RaisePropertyChanged(nameof(WidgetHeight));
            }
        }
    }

    public double ListItemHeight
    {
        get => _listItemHeight;
        set
        {
            var normalized = System.Math.Max(40, value);
            if (SetProperty(ref _listItemHeight, normalized))
            {
                ApplyListHeightRules();
                SyncDraftListItemHeightText();
                RaisePropertyChanged(nameof(CurrentListItemHeight));
                RaisePropertyChanged(nameof(WidgetHeight));
            }
        }
    }

    public double BorderWidth
    {
        get => _borderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _borderWidth, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveBorderThickness));
            }
        }
    }

    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 48);
            if (SetProperty(ref _cornerRadius, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveCornerRadius));
            }
        }
    }

    public string DraftListItemHeightText
    {
        get => _draftListItemHeightText;
        set => SetProperty(ref _draftListItemHeightText, value);
    }

    public FolderItemModel? SelectedListItem
    {
        get => _selectedListItem;
        set
        {
            if (SetProperty(ref _selectedListItem, value))
            {
                SyncDraftListItemHeightText();
                RaisePropertyChanged(nameof(CurrentListItemHeight));
                RaisePropertyChanged(nameof(CanEditListHeight));
                RaisePropertyChanged(nameof(WidgetHeight));
            }
        }
    }

    public FolderItemModel? ParentWidgetList
    {
        get => _parentWidgetList;
        private set => SetProperty(ref _parentWidgetList, value);
    }

    public FolderItemModel? ParentItem
    {
        get => _parentItem;
        private set => SetProperty(ref _parentItem, value);
    }

    public bool CanEditListHeight => IsAutoHeight || SelectedListItem is not null;

    public double CurrentListItemHeight
    {
        get => IsAutoHeight ? ListItemHeight : SelectedListItem?.Height ?? ListItemHeight;
        set
        {
            var normalized = System.Math.Max(40, value);
            if (IsAutoHeight)
            {
                ListItemHeight = normalized;
                return;
            }

            if (SelectedListItem is null)
            {
                return;
            }

            SelectedListItem.Height = System.Math.Max(normalized, SelectedListItem.MinHeight);
            SyncDraftListItemHeightText();
            RaisePropertyChanged(nameof(CurrentListItemHeight));
            RaisePropertyChanged(nameof(WidgetHeight));
        }
    }

    public string? BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? BorderColor
    {
        get => _borderColor;
        set
        {
            if (SetProperty(ref _borderColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? ContainerBorder
    {
        get => _containerBorder;
        set
        {
            if (SetProperty(ref _containerBorder, value))
            {
                RaisePropertyChanged(nameof(EffectiveContainerBorderBrush));
                RaisePropertyChanged(nameof(EffectiveContainerBorderBrushValue));
            }
        }
    }

    public string? ContainerBackgroundColor
    {
        get => _containerBackgroundColor;
        set
        {
            if (SetProperty(ref _containerBackgroundColor, value))
            {
                RaisePropertyChanged(nameof(EffectiveContainerBackground));
                RaisePropertyChanged(nameof(EffectiveContainerBackgroundBrush));
            }
        }
    }

    public double ContainerBorderWidth
    {
        get => _containerBorderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _containerBorderWidth, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveContainerBorderThickness));
            }
        }
    }

    public double WidgetBorderWidth
    {
        get => _controlBorderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _controlBorderWidth, normalized) && IsWidgetList)
            {
                foreach (var item in Items)
                {
                    item.BorderWidth = normalized;
                }
            }
        }
    }

    // Backwards-compatible alias used by existing code and persistence
    public double ControlBorderWidth
    {
        get => WidgetBorderWidth;
        set => WidgetBorderWidth = value;
    }

    public string? ControlBorderColor
    {
        get => _controlBorderColor;
        set
        {
            if (SetProperty(ref _controlBorderColor, value) && IsWidgetList)
            {
                foreach (var item in Items)
                {
                    item.BorderColor = value;
                }
            }
        }
    }

    public double WidgetCornerRadius
    {
        get => _controlCornerRadius;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 48);
            if (SetProperty(ref _controlCornerRadius, normalized) && IsWidgetList)
            {
                foreach (var item in Items)
                {
                    item.CornerRadius = normalized;
                }
            }
        }
    }

    // Backwards-compatible alias used by existing code and persistence
    public double ControlCornerRadius
    {
        get => WidgetCornerRadius;
        set => WidgetCornerRadius = value;
    }

    public string? PrimaryForegroundColor
    {
        get => _primaryForegroundColor;
        set
        {
            if (SetProperty(ref _primaryForegroundColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? SecondaryForegroundColor
    {
        get => _secondaryForegroundColor;
        set
        {
            if (SetProperty(ref _secondaryForegroundColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? AccentBackgroundColor
    {
        get => _accentBackgroundColor;
        set
        {
            if (SetProperty(ref _accentBackgroundColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? AccentForegroundColor
    {
        get => _accentForegroundColor;
        set
        {
            if (SetProperty(ref _accentForegroundColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? HeaderForeColor
    {
        get => _headerForeColor;
        set
        {
            if (SetProperty(ref _headerForeColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? HeaderBackColor
    {
        get => _headerBackColor;
        set
        {
            if (SetProperty(ref _headerBackColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? HeaderBorderColor
    {
        get => _headerBorderColor;
        set
        {
            if (SetProperty(ref _headerBorderColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public double HeaderBorderWidth
    {
        get => _headerBorderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _headerBorderWidth, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveHeaderBorderThickness));
            }
        }
    }

    public double HeaderCornerRadius
    {
        get => _headerCornerRadius;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 48);
            if (SetProperty(ref _headerCornerRadius, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveHeaderCornerRadius));
            }
        }
    }

    public string? BodyForeColor
    {
        get => _bodyForeColor;
        set
        {
            if (SetProperty(ref _bodyForeColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? BodyBackColor
    {
        get => _bodyBackColor;
        set
        {
            if (SetProperty(ref _bodyBackColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? DisplayBackColor
    {
        get => _displayBackColor;
        set
        {
            if (SetProperty(ref _displayBackColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? BodyBorderColor
    {
        get => _bodyBorderColor;
        set
        {
            if (SetProperty(ref _bodyBorderColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public double BodyBorderWidth
    {
        get => _bodyBorderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _bodyBorderWidth, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveBodyBorderThickness));
            }
        }
    }

    public double BodyCornerRadius
    {
        get => _bodyCornerRadius;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 48);
            if (SetProperty(ref _bodyCornerRadius, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveBodyCornerRadius));
            }
        }
    }

    public string? FooterForeColor
    {
        get => _footerForeColor;
        set
        {
            if (SetProperty(ref _footerForeColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? FooterBackColor
    {
        get => _footerBackColor;
        set
        {
            if (SetProperty(ref _footerBackColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string? FooterBorderColor
    {
        get => _footerBorderColor;
        set
        {
            if (SetProperty(ref _footerBorderColor, value))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public double FooterBorderWidth
    {
        get => _footerBorderWidth;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 12);
            if (SetProperty(ref _footerBorderWidth, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveFooterBorderThickness));
            }
        }
    }

    public double FooterCornerRadius
    {
        get => _footerCornerRadius;
        set
        {
            var normalized = System.Math.Clamp(value, 0, 48);
            if (SetProperty(ref _footerCornerRadius, normalized))
            {
                RaisePropertyChanged(nameof(EffectiveFooterCornerRadius));
            }
        }
    }

    public string? TextColor
    {
        get => BodyForeColor ?? PrimaryForegroundColor;
        set
        {
            var changed = false;
            changed |= SetProperty(ref _primaryForegroundColor, value, nameof(PrimaryForegroundColor));
            changed |= SetProperty(ref _secondaryForegroundColor, value, nameof(SecondaryForegroundColor));
            changed |= SetProperty(ref _accentForegroundColor, value, nameof(AccentForegroundColor));
            changed |= SetProperty(ref _headerForeColor, value, nameof(HeaderForeColor));
            changed |= SetProperty(ref _bodyForeColor, value, nameof(BodyForeColor));
            changed |= SetProperty(ref _footerForeColor, value, nameof(FooterForeColor));
            if (changed)
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            var normalizedValue = NormalizeConfiguredTargetPathForKind(value);
            if (SetProperty(ref _targetPath, normalizedValue))
            {
                ClearBlockedLegacyScript();
                RaisePropertyChanged(nameof(ShowBodyCaption));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                UpdateScriptTimer();
                ResolveTarget();
            }
        }
    }

    public string PythonScriptPath
    {
        get => _pythonScriptPath;
        set
        {
            var normalizedValue = NormalizePythonScriptPathForKind(value);
            if (SetProperty(ref _pythonScriptPath, normalizedValue))
            {
                ClearBlockedLegacyScript();
                UpdateScriptTimer();
            }
        }
    }

    /// <summary>
    /// Optional application definitions for the ApplicationExplorer widget.
    ///
    /// Current minimum format (one environment per line):
    ///   Name | ScriptPath
    /// If Name is omitted, the script file name is used.
    /// </summary>
    public string ApplicationDefinitions
    {
        get => _applicationDefinitions;
        set => SetProperty(ref _applicationDefinitions, value ?? string.Empty);
    }

    public string CustomSignalDefinitions
    {
        get => _customSignalDefinitions;
        set => SetProperty(ref _customSignalDefinitions, value ?? string.Empty);
    }

    public string ControllerDefinitions
    {
        get => _controllerDefinitions;
        set => SetProperty(ref _controllerDefinitions, value ?? string.Empty);
    }

    public string MonitorDefinitions
    {
        get => _monitorDefinitions;
        set => SetProperty(ref _monitorDefinitions, value ?? string.Empty);
    }

    public string SelectedMonitorIds
    {
        get => _selectedMonitorIds;
        set => SetProperty(ref _selectedMonitorIds, value ?? string.Empty);
    }

    public string OnActiveColor
    {
        get => _onActiveColor;
        set => SetProperty(ref _onActiveColor, value ?? string.Empty);
    }

    public bool ApplicationAutoStart
    {
        get => _applicationAutoStart;
        set => SetProperty(ref _applicationAutoStart, value);
    }

    public string TargetPropertyPath
    {
        get => _targetParameterPath;
        set
        {
            var normalizedValue = NormalizeTargetPropertyPath(value);
            if (SetProperty(ref _targetParameterPath, normalizedValue))
            {
                UpdateSignalLiveValueBinding();
                RaisePropertyChanged(nameof(TargetPropertyView));
                RaisePropertyChanged(nameof(ItemBodyPresentation));
                RaisePropertyChanged(nameof(ShowItemFooterPanel));
                RaisePropertyChanged(nameof(ItemUnitWidth));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(CanOpenValueEditor));
            }
        }
    }

    public string TargetPropertyFormat
    {
        get => _targetParameterFormat;
        set
        {
            if (SetProperty(ref _targetParameterFormat, value))
            {
                RaisePropertyChanged(nameof(TargetPropertyView));
                RaisePropertyChanged(nameof(ItemBodyPresentation));
                RaisePropertyChanged(nameof(ShowItemFooterPanel));
                RaisePropertyChanged(nameof(ItemUnitWidth));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(CanOpenValueEditor));
                RaisePropertyChanged(nameof(ResolvedTargetSourceFormat));
                RaisePropertyChanged(nameof(ResolvedTargetEffectiveFormat));
            }
        }
    }

    public string TargetLog
    {
        get => _targetLog;
        set => SetProperty(ref _targetLog, NormalizeLogTargetPath(value));
    }

    public bool AutoCreateLog
    {
        get => _autoCreateLog;
        set => SetProperty(ref _autoCreateLog, value);
    }

    public string GetOwnedProcessLogPath()
    {
        if (!IsLogControl)
        {
            return string.Empty;
        }

        var folderSegment = TargetPathHelper.NormalizeConfiguredTargetPath(FolderName);
        var relativeIdentity = TargetPathHelper.NormalizeConfiguredTargetPath(GetRelativeWidgetIdentityPath());
        if (string.IsNullOrWhiteSpace(folderSegment) || string.IsNullOrWhiteSpace(relativeIdentity))
        {
            return string.Empty;
        }

        return $"studio.{folderSegment}.logs.{relativeIdentity}";
    }

    public string GetOwnedProcessLogDirectory(string? projectRootDirectory = null)
    {
        if (!IsLogControl)
        {
            return string.Empty;
        }

        var rootDirectory = string.IsNullOrWhiteSpace(projectRootDirectory)
            ? Core.OpenedDirectory
            : projectRootDirectory;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return string.Empty;
        }

        var folderSegment = TargetPathHelper.NormalizePathSegment(FolderName, "folder");
        var logSegment = TargetPathHelper.NormalizePathSegment(Name, Id);
        if (string.IsNullOrWhiteSpace(folderSegment) || string.IsNullOrWhiteSpace(logSegment))
        {
            return string.Empty;
        }

        return System.IO.Path.Combine(rootDirectory, "Logs", folderSegment, logSegment);
    }

    public string GetAutoCreatedLogPath()
    {
        if (IsLogControl)
        {
            return GetOwnedProcessLogPath();
        }

        return AutoCreateLog
            ? NormalizeLogTargetPath(Name)
            : TargetLog;
    }

    public void EnsureOwnedProcessLog(string? projectRootDirectory = null)
    {
        if (IsLogControl)
        {
            var ownedTargetPath = GetOwnedProcessLogPath();
            if (string.IsNullOrWhiteSpace(ownedTargetPath))
            {
                return;
            }

            var logDirectory = GetOwnedProcessLogDirectory(projectRootDirectory);
            ProcessLogRuntime.EnsurePublished(
                ownedTargetPath,
                string.IsNullOrWhiteSpace(Name) ? "Log" : Name.Trim(),
                string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory);
            return;
        }

        if (!AutoCreateLog)
        {
            return;
        }

        var targetPath = GetAutoCreatedLogPath();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        ProcessLogRuntime.EnsurePublished(targetPath, string.IsNullOrWhiteSpace(Name) ? "Log" : Name.Trim());
    }
    public int HistorySeconds
    {
        get => _historySeconds;
        set => SetProperty(ref _historySeconds, value <= 0 ? 0 : value);
    }

    public int View
    {
        get => _view;
        set
        {
            if (SetProperty(ref _view, value <= 0 ? 1 : value))
            {
                RaisePropertyChanged(nameof(IsVisibleInActiveView));
            }
        }
    }

    public int ActiveViewId
    {
        get => _activeViewId;
        private set
        {
            if (SetProperty(ref _activeViewId, value <= 0 ? 1 : value))
            {
                RaisePropertyChanged(nameof(IsVisibleInActiveView));
            }
        }
    }

    public bool IsVisibleInActiveView => View == ActiveViewId;

    public int ViewSeconds
    {
        get => _viewSeconds;
        set => SetProperty(ref _viewSeconds, value <= 1 ? 1 : value);
    }

    public string ChartSeriesDefinitions
    {
        get => _chartSeriesDefinitions;
        set => SetProperty(ref _chartSeriesDefinitions, value ?? string.Empty);
    }

    public string InteractionRules
    {
        get => _interactionRules;
        set => SetProperty(ref _interactionRules, value ?? string.Empty);
    }

    public string VisualRules
    {
        get => _visualRules;
        set
        {
            if (SetProperty(ref _visualRules, value ?? string.Empty))
            {
                ApplyTheme(_isDarkThemeApplied);
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string UdlClientHost
    {
        get => _udlClientHost;
        set => SetProperty(ref _udlClientHost, string.IsNullOrWhiteSpace(value) ? "192.168.178.151" : value.Trim());
    }

    public int UdlClientPort
    {
        get => _udlClientPort;
        set => SetProperty(ref _udlClientPort, value <= 0 ? 9001 : value);
    }

    public bool UdlClientAutoConnect
    {
        get => _udlClientAutoConnect;
        set => SetProperty(ref _udlClientAutoConnect, value);
    }

    public bool UdlClientDebugLogging
    {
        get => _udlClientDebugLogging;
        set => SetProperty(ref _udlClientDebugLogging, value);
    }

    public bool UdlClientDemoEnabled
    {
        get => _udlClientDemoEnabled;
        set => SetProperty(ref _udlClientDemoEnabled, value);
    }

    public string UdlAttachedItemPaths
    {
        get => _udlAttachedItemPaths;
        set => SetProperty(ref _udlAttachedItemPaths, value ?? string.Empty);
    }

    public string UdlDemoModuleDefinitions
    {
        get => _udlDemoModuleDefinitions;
        set => SetProperty(ref _udlDemoModuleDefinitions, value ?? string.Empty);
    }

    public string UdlModuleExposureDefinitions
    {
        get => _udlModuleExposureDefinitions;
        set => SetProperty(ref _udlModuleExposureDefinitions, value ?? string.Empty);
    }

    public string BrokerHost
    {
        get => _brokerHost;
        set => SetProperty(ref _brokerHost, string.IsNullOrWhiteSpace(value) ? ItemClientDefaults.Host : value.Trim());
    }

    public int BrokerPort
    {
        get => _brokerPort;
        set => SetProperty(ref _brokerPort, value <= 0 ? ItemClientDefaults.Port : value);
    }

    public string BrokerBaseTopic
    {
        get => _brokerBaseTopic;
        set => SetProperty(ref _brokerBaseTopic, value?.Trim() ?? string.Empty);
    }

    public string ServerClientId
    {
        get => _brokerClientId;
        set => SetProperty(ref _brokerClientId, ItemClientId.Normalize(value));
    }

    public string BrokerMode
    {
        get => _brokerMode;
        set => SetProperty(ref _brokerMode, ItemClientModes.Normalize(value));
    }

    public bool BrokerAutoConnect
    {
        get => _brokerAutoConnect;
        set => SetProperty(ref _brokerAutoConnect, value);
    }

    public string BrokerAttachedItemPaths
    {
        get => _brokerAttachedItemPaths;
        set => SetProperty(ref _brokerAttachedItemPaths, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the local HornetStudio item paths configured for future ItemClient publishing.
    /// </summary>
    public string BrokerPublishedItemPaths
    {
        get => _brokerPublishedItemPaths;
        set => SetProperty(ref _brokerPublishedItemPaths, value ?? string.Empty);
    }

    public string ItemExposures
    {
        get => _itemExposures;
        set => SetProperty(ref _itemExposures, value ?? string.Empty);
    }

    public bool CsvSplitDaily
    {
        get => _csvSplitDaily;
        set => SetProperty(ref _csvSplitDaily, value);
    }

    public string CsvSplitDailyTime
    {
        get => _csvSplitDailyTime;
        set => SetProperty(ref _csvSplitDailyTime, string.IsNullOrWhiteSpace(value) ? "00:00:00" : value.Trim());
    }

    public int CsvSplitMaxFileSizeMb
    {
        get => _csvSplitMaxFileSizeMb;
        set => SetProperty(ref _csvSplitMaxFileSizeMb, value < 0 ? 0 : value);
    }

    public string CsvPersistenceMode
    {
        get => _csvPersistenceMode;
        set => SetProperty(ref _csvPersistenceMode, string.IsNullOrWhiteSpace(value) ? "Balanced" : value.Trim());
    }

    public int CsvFlushIntervalMs
    {
        get => _csvFlushIntervalMs;
        set => SetProperty(ref _csvFlushIntervalMs, value < 0 ? 0 : value);
    }

    public int CsvFlushBatchSize
    {
        get => _csvFlushBatchSize;
        set => SetProperty(ref _csvFlushBatchSize, value < 0 ? 0 : value);
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value);
    }

    public ItemModel? Target
    {
        get => _target;
        private set
        {
            if (SetProperty(ref _target, value))
            {
                RefreshSignalValueReferenceCache();
                UpdateSignalLiveValueBinding();
                RaisePropertyChanged(nameof(DisplayValue));
                RaisePropertyChanged(nameof(DisplayUnit));
                RaisePropertyChanged(nameof(RequestStatusText));
                RaisePropertyChanged(nameof(DisplayFooter));
                RaisePropertyChanged(nameof(TargetPropertyView));
                RaisePropertyChanged(nameof(ItemBodyPresentation));
                RaisePropertyChanged(nameof(ShowBodyCaption));
                RaisePropertyChanged(nameof(ShowItemFooterPanel));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemUnitWidth));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(CanOpenValueEditor));
                RaisePropertyChanged(nameof(ResolvedTargetWritable));
                RaisePropertyChanged(nameof(ResolvedTargetUsesFormatOverride));
                RaisePropertyChanged(nameof(ResolvedTargetWriteMode));
                RaisePropertyChanged(nameof(ResolvedTargetWritePath));
                RaisePropertyChanged(nameof(ResolvedTargetSourceFormat));
                RaisePropertyChanged(nameof(ResolvedTargetEffectiveFormat));
            }
        }
    }

    public string ResolvedTargetWritable
        => Target is null ? "-" : (IsDeclaredWritable(Target) ? "True" : "False");

    public string ResolvedTargetUsesFormatOverride
        => Target is null ? "-" : (!string.IsNullOrWhiteSpace(TargetPropertyFormat) ? "True" : "False");

    public string ResolvedTargetWriteMode
        => Target is null ? "-" : GetResolvedWriteMode(Target).ToString();

    public string ResolvedTargetWritePath
        => Target is null ? "-" : GetResolvedWritePath(Target);

    public string ResolvedTargetSourceFormat
        => Target is null
            ? "-"
            : Target.Properties.Has("format")
                ? Target.Properties["format"].Value?.ToString() ?? "-"
                : "-";

    public string ResolvedTargetEffectiveFormat
        => !string.IsNullOrWhiteSpace(TargetPropertyFormat)
            ? TargetPropertyFormat
            : ResolvedTargetSourceFormat;

    public int RefreshRateMs
    {
        get => _refreshRateMs;
        set
        {
            var normalized = value <= 0 ? 0 : value;
            if (SetProperty(ref _refreshRateMs, normalized))
            {
                if (normalized == 0)
                {
                    CancelPendingTargetRefresh();
                    TriggerTargetRefresh();
                }
                else if (_hasPendingTargetRefresh)
                {
                    SchedulePendingRefresh(TimeSpan.FromMilliseconds(normalized));
                }

                UpdateScriptTimer();
            }
        }
    }

    public string EffectiveBackground
    {
        get => _effectiveBackground;
        private set => SetProperty(ref _effectiveBackground, value);
    }

    public IBrush EffectiveBackgroundBrush => ParseBrush(EffectiveBackground);

    public string EffectiveInnerBackground
    {
        get => _effectiveInnerBackground;
        private set => SetProperty(ref _effectiveInnerBackground, value);
    }

    public IBrush EffectiveInnerBackgroundBrush => ParseBrush(EffectiveInnerBackground);

    public string EffectiveBorderBrush
    {
        get => _effectiveBorderBrush;
        private set => SetProperty(ref _effectiveBorderBrush, value);
    }

    public IBrush EffectiveBorderBrushValue => ParseBrush(EffectiveBorderBrush);

    public string EffectivePrimaryForeground
    {
        get => _effectivePrimaryForeground;
        private set => SetProperty(ref _effectivePrimaryForeground, value);
    }

    public IBrush EffectivePrimaryForegroundBrush => ParseBrush(EffectivePrimaryForeground);

    public string EffectiveSecondaryForeground
    {
        get => _effectiveSecondaryForeground;
        private set => SetProperty(ref _effectiveSecondaryForeground, value);
    }

    public IBrush EffectiveSecondaryForegroundBrush => ParseBrush(EffectiveSecondaryForeground);

    public string EffectiveMutedForeground
    {
        get => _effectiveMutedForeground;
        private set => SetProperty(ref _effectiveMutedForeground, value);
    }

    public IBrush EffectiveMutedForegroundBrush => ParseBrush(EffectiveMutedForeground);

    public string EffectiveAccentBackground
    {
        get => _effectiveAccentBackground;
        private set => SetProperty(ref _effectiveAccentBackground, value);
    }

    public IBrush EffectiveAccentBackgroundBrush => ParseBrush(EffectiveAccentBackground);

    public string EffectiveAccentForeground
    {
        get => _effectiveAccentForeground;
        private set => SetProperty(ref _effectiveAccentForeground, value);
    }

    public IBrush EffectiveAccentForegroundBrush => ParseBrush(EffectiveAccentForeground);

    public string EffectiveHeaderForeground
    {
        get => _effectiveHeaderForeground;
        private set => SetProperty(ref _effectiveHeaderForeground, value);
    }

    public IBrush EffectiveHeaderForegroundBrush => ParseBrush(EffectiveHeaderForeground);

    public string EffectiveHeaderBackground
    {
        get => _effectiveHeaderBackground;
        private set => SetProperty(ref _effectiveHeaderBackground, value);
    }

    public IBrush EffectiveHeaderBackgroundBrush => ParseBrush(EffectiveHeaderBackground);

    public string EffectiveHeaderBorder
    {
        get => _effectiveHeaderBorder;
        private set => SetProperty(ref _effectiveHeaderBorder, value);
    }

    public IBrush EffectiveHeaderBorderBrush => ParseBrush(EffectiveHeaderBorder);

    public Thickness EffectiveHeaderBorderThickness => new(HeaderBorderWidth);

    public CornerRadius EffectiveHeaderCornerRadius => new(HeaderCornerRadius);

    public string EffectiveBodyForeground
    {
        get => _effectiveBodyForeground;
        private set => SetProperty(ref _effectiveBodyForeground, value);
    }

    public IBrush EffectiveBodyForegroundBrush => ParseBrush(EffectiveBodyForeground);

    public string EffectiveBodyBackground
    {
        get => _effectiveBodyBackground;
        private set => SetProperty(ref _effectiveBodyBackground, value);
    }

    public IBrush EffectiveBodyBackgroundBrush => ParseBrush(EffectiveBodyBackground);

    public string EffectiveDisplayBackColor
    {
        get => _effectiveDisplayBackColor;
        private set => SetProperty(ref _effectiveDisplayBackColor, value);
    }

    public IBrush EffectiveDisplayBackColorBrush => ParseBrush(EffectiveDisplayBackColor);

    public string EffectiveBodyBorder
    {
        get => _effectiveBodyBorder;
        private set => SetProperty(ref _effectiveBodyBorder, value);
    }

    public IBrush EffectiveBodyBorderBrush => ParseBrush(EffectiveBodyBorder);

    public Thickness EffectiveBodyBorderThickness => new(BodyBorderWidth);

    public CornerRadius EffectiveBodyCornerRadius => new(BodyCornerRadius);

    public string EffectiveFooterForeground
    {
        get => _effectiveFooterForeground;
        private set => SetProperty(ref _effectiveFooterForeground, value);
    }

    public IBrush EffectiveFooterForegroundBrush => ParseBrush(EffectiveFooterForeground);

    public string EffectiveFooterBackground
    {
        get => _effectiveFooterBackground;
        private set => SetProperty(ref _effectiveFooterBackground, value);
    }

    public IBrush EffectiveFooterBackgroundBrush => ParseBrush(EffectiveFooterBackground);

    public string EffectiveFooterBorder
    {
        get => _effectiveFooterBorder;
        private set => SetProperty(ref _effectiveFooterBorder, value);
    }

    public IBrush EffectiveFooterBorderBrush => ParseBrush(EffectiveFooterBorder);

    public Thickness EffectiveFooterBorderThickness => new(FooterBorderWidth);

    public CornerRadius EffectiveFooterCornerRadius => new(FooterCornerRadius);

    public Thickness EffectiveBorderThickness => new(BorderWidth);

    public CornerRadius EffectiveCornerRadius => new(CornerRadius);

    public string EffectiveContainerBorderBrush
        => string.IsNullOrWhiteSpace(ContainerBorder) ? EffectiveBorderBrush : ContainerBorder!;

    public IBrush EffectiveContainerBorderBrushValue => ParseBrush(EffectiveContainerBorderBrush);

    public string EffectiveContainerBackground
        => string.IsNullOrWhiteSpace(ContainerBackgroundColor) ? EffectiveInnerBackground : ContainerBackgroundColor!;

    public IBrush EffectiveContainerBackgroundBrush => ParseBrush(EffectiveContainerBackground);

    public Thickness EffectiveContainerBorderThickness => new(ContainerBorderWidth);

    public double FontSize => Kind switch
    {
        ControlKind.Button => System.Math.Clamp(Height * 0.36, 20, 46),
        ControlKind.Signal => Height * 0.18,
        ControlKind.ItemModel => Height * 0.18,
        ControlKind.LogControl => 18,
        ControlKind.ChartControl => 18,
        _ => 18
    };

    public double ItemTitleFontSize => Height * 0.18;
    public string WidgetCaption
    {
        get => SyncText ? Title : Header;
        set => Header = value;
    }

    // Backwards-compatible alias: existing code and serialized layouts still use ControlCaption
    public string ControlCaption
    {
        get => WidgetCaption;
        set => WidgetCaption = value;
    }

    public bool ShowControlCaption => CaptionVisible && !string.IsNullOrWhiteSpace(WidgetCaption);

    public bool ShowTopBodyCaption => BodyCaptionVisible && string.Equals(BodyCaptionPosition, "Top", StringComparison.OrdinalIgnoreCase);

    public bool ShowInlineBodyCaption => BodyCaptionVisible && string.Equals(BodyCaptionPosition, "Left", StringComparison.OrdinalIgnoreCase);

    public string BodyCaption
    {
        get => _bodyCaption;
        set
        {
            if (!SetProperty(ref _bodyCaption, value ?? string.Empty))
            {
                return;
            }

            RaisePropertyChanged(nameof(ShowBodyCaption));
            RaisePropertyChanged(nameof(ShowTopBodyCaption));
            RaisePropertyChanged(nameof(ShowInlineBodyCaption));
            RaisePropertyChanged(nameof(ItemBodyHeight));
            RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
            RaisePropertyChanged(nameof(AvailableBodyHeight));
            RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
            RaisePropertyChanged(nameof(ItemUnitFontSize));
        }
    }

    public PropertyDisplayModel ItemBodyPresentation => BuildTargetParameterView(string.Empty, DisplayValue);

    public string RequestStatusText
        => Target?.Properties.Has("SendStatus") == true
            ? Target.Properties["SendStatus"].Value?.ToString() ?? string.Empty
            : string.Empty;

    public string DisplayFooter => string.Empty;

    public bool ShowItemFooterPanel => IsItem && ShowFooter;

    // Footer-Subitems werden im SignalWidget nicht mehr verwendet.
    public bool HasFooterSubItems => false;

    public int FooterSubItemRowCount => 0;

    public double FooterSubItemWidth => 0;

    public double FooterSubItemHeight => FooterSubItemDesiredHeight;

    public double FooterPanelHeight => ShowItemFooterPanel ? ItemFooterFontSize + 8 : 0;

    public double ItemControlCaptionFontSize => System.Math.Clamp(Height * 0.13, 10, 18);

    public double ItemBodyCaptionFontSize => System.Math.Clamp(Height * 0.09, 8, 14);

    public double ItemFooterFontSize => System.Math.Clamp(Height * 0.09, 8, 13);

    public string EffectiveButtonText => !string.IsNullOrWhiteSpace(ButtonText) ? ButtonText : Title;

    public bool HasButtonIcon => !string.IsNullOrWhiteSpace(ButtonIcon);

    public bool ShowButtonText => (!ButtonOnlyIcon || !HasButtonIcon) && !string.IsNullOrWhiteSpace(EffectiveButtonText);

    public bool ShowButtonIcon => HasButtonIcon;

    public string EffectiveButtonIconPath => HasButtonIcon
        ? IconPathHelper.ResolvePath(ButtonIcon, FolderLayoutPath) ?? ButtonIcon
        : "avares://HornetStudio.Editor/EditorIcons/clear.svg";

    public string EffectiveButtonCommand => ButtonCommand;

    public string? EffectiveToolTipText => string.IsNullOrWhiteSpace(ToolTipText) ? null : ToolTipText;

    public string EffectiveButtonBodyBackground
        => !string.IsNullOrWhiteSpace(_visualRuleButtonBackColorOverride)
            ? _visualRuleButtonBackColorOverride!
            : string.IsNullOrWhiteSpace(ButtonBodyBackground)
            ? (_isDarkThemeApplied ? ThemePalette.Dark.ButtonBackColor : ThemePalette.Light.ButtonBackColor)
            : (string.Equals(ButtonBodyBackground.Trim(), "Transparent", StringComparison.OrdinalIgnoreCase)
                ? "Transparent"
                : ButtonBodyBackground);

    public IBrush EffectiveButtonBodyBackgroundBrush => ParseBrush(EffectiveButtonBodyBackground);

    public string EffectiveButtonHoverBackground
        => CreateRelativeButtonStateColor(EffectiveButtonBodyBackground, 0.14);

    public IBrush EffectiveButtonHoverBackgroundBrush => ParseBrush(EffectiveButtonHoverBackground);

    public string EffectiveButtonPressBackground
        => CreateRelativeButtonStateColor(EffectiveButtonBodyBackground, 0.24);

    public IBrush EffectiveButtonPressBackgroundBrush => ParseBrush(EffectiveButtonPressBackground);

    public string EffectiveButtonBodyForeground => string.IsNullOrWhiteSpace(ButtonBodyForegroundColor) ? EffectiveBodyForeground : ButtonBodyForegroundColor;

    public IBrush EffectiveButtonBodyForegroundBrush => ParseBrush(EffectiveButtonBodyForeground);

    public string ButtonIconCss
        => !string.IsNullOrWhiteSpace(ButtonIconColor)
            ? $"path {{ fill: {ButtonIconColor}; }}"
            : (UseThemeColor
                ? $"path {{ fill: {EffectiveButtonBodyForeground}; }}"
                : string.Empty);

    public string EffectiveButtonIconTintColor
        => !string.IsNullOrWhiteSpace(ButtonIconColor)
            ? ButtonIconColor
            : (UseThemeColor ? EffectiveButtonBodyForeground : string.Empty);

    public bool ShowButtonFooter => ShowFooter && !string.IsNullOrWhiteSpace(Footer);

    public HorizontalAlignment ButtonTextHorizontalAlignment => ParseHorizontalAlignment(ButtonTextAlign, HorizontalAlignment.Center);

    public HorizontalAlignment ButtonIconHorizontalAlignment => ParseHorizontalAlignment(ButtonIconAlign, HorizontalAlignment.Left);

    public bool ShowLeftButtonIcon => ShowButtonIcon && !ButtonOnlyIcon && ButtonIconHorizontalAlignment == HorizontalAlignment.Left;

    public bool ShowCenterButtonIcon => ShowButtonIcon && ButtonOnlyIcon;

    public bool ShowRightButtonIcon => ShowButtonIcon && !ButtonOnlyIcon && ButtonIconHorizontalAlignment == HorizontalAlignment.Right;

    public double ButtonOverlayTopInset => ShowControlCaption ? 0 : 24;

    public double ButtonAvailableBodyHeight
    {
        get
        {
            var captionHeight = ShowControlCaption ? ItemHeaderHeight : 0;
            var footerHeight = ShowButtonFooter ? ItemFooterFontSize + 2 : 0;
            var bodyCaptionHeight = ShowTopBodyCaption ? ItemBodyCaptionFontSize * 2.5 : 0;
            var bodyChromeHeight = 12;
            var availableHeight = Height - 8 - captionHeight - footerHeight - bodyCaptionHeight - bodyChromeHeight;
            return System.Math.Max(availableHeight, 12);
        }
    }

    public double ButtonBodyFontSize
    {
        get
        {
            var maxSize = ButtonAvailableBodyHeight;
            var minSize = System.Math.Min(12, maxSize);
            return System.Math.Clamp(ButtonAvailableBodyHeight * 0.60, minSize, maxSize);
        }
    }

    public double ButtonIconSize
    {
        get
        {
            var maxSize = ButtonAvailableBodyHeight;
            var minSize = System.Math.Min(14, maxSize);
            return System.Math.Clamp(ButtonAvailableBodyHeight * 0.62, minSize, maxSize);
        }
    }

    public double WidgetHeight
    {
        get => ListItemHeight;
        set => ListItemHeight = value;
    }

    // Backwards-compatible alias used by existing code and persistence
    public double ControlHeight
    {
        get => WidgetHeight;
        set => WidgetHeight = value;
    }

    public double ItemBodyHeight
    {
        get
        {
            var footerHeight = FooterPanelHeight;
            var baseBodyHeight = Height - ItemHeaderHeight - ItemBodyCaptionHeight - footerHeight - 8;
            return System.Math.Max(baseBodyHeight, 18);
        }
    }

    public double ItemBodyCaptionHeight => ShowTopBodyCaption ? ItemBodyCaptionFontSize + 3 : 0;

    public double AvailableBodyHeight
    {
        get
        {
            return System.Math.Max(ItemBodyHeight, 18);
        }
    }

    public double ItemChoiceButtonHeight
    {
        get
        {
            if (TargetPropertyView.Definition.Kind is not (PropertyVisualKind.Bool or PropertyVisualKind.Bits))
            {
                return 0;
            }

            return System.Math.Max(AvailableBodyHeight - 40, 16);
        }
    }

    public double AvailableValueWidth
    {
        get
        {
            var reservedUnitWidth = TargetPropertyView.ShowUnit ? ItemUnitWidth + 8 : 0;
            return System.Math.Max(Width - reservedUnitWidth - 16, 24);
        }
    }

    public int ValueCharacterCount
        => System.Math.Max((TargetPropertyView.ValueText ?? string.Empty).Length, 1);

    public int UnitCharacterCount
        => System.Math.Max((TargetPropertyView.UnitText ?? string.Empty).Length, 1);

    public double ItemValueFontSize
    {
        get
        {
            if (TargetPropertyView.Definition.Kind is PropertyVisualKind.Bool or PropertyVisualKind.Bits)
            {
                var maxSize = System.Math.Max(ItemChoiceButtonHeight - 6, 9);
                var minSize = System.Math.Min(9, maxSize);
                return System.Math.Clamp(ItemChoiceButtonHeight * 0.34, minSize, maxSize);
            }

            var maxTextSize = System.Math.Max(AvailableBodyHeight, 11);
            var minTextSize = System.Math.Min(11, maxTextSize);
            return System.Math.Clamp(AvailableBodyHeight, minTextSize, maxTextSize);
        }
    }

    public double ItemUnitFontSize => System.Math.Clamp(ItemValueFontSize * 0.42, 9, ItemValueFontSize);

    public double ItemUnitBaselineOffset
    {
        get
        {
            var rawOffset = BaselineHelper.GetBaselineOffsetBRelativeToA(
                ItemValueTypeface,
                ItemValueFontSize,
                "value",
                ItemUnitTypeface,
                ItemUnitFontSize,
                "Unit");

            var helperMix = System.Math.Clamp((ItemValueFontSize - 20) / 24, 0.0, 0.65);
            var fallbackOffset = -ItemUnitFontSize * (ItemValueFontSize * 0.01) / 100;
            return fallbackOffset + (rawOffset * helperMix);
        }
    }

    public double ItemBottomSpacing => 0;

    public double ItemHeaderHeight => ShowControlCaption ? System.Math.Max(Height * 0.12, 16) : 0;

    public double ItemUnitWidth
    {
        get
        {
            if (!TargetPropertyView.ShowUnit)
            {
                return 0;
            }

            var estimatedWidth = ItemUnitFontSize * (UnitCharacterCount * 0.72) + 10;
            return System.Math.Max(estimatedWidth, 24);
        }
    }

    public string DisplayValue
    {
        get
        {
            if (IsScriptTarget && _scriptValue is not null)
            {
                return _scriptValue.ToString() ?? Title;
            }

            if (Target is not null)
            {
                return Target.Value?.ToString() ?? Title;
            }

            return Title;
        }
    }

    public PropertyDisplayModel TargetPropertyView => BuildTargetPropertyView();

    public bool HasInteractionRules => ItemInteractionRuleCodec.ParseDefinitions(InteractionRules)
        .Any(rule => rule.Action != ItemInteractionAction.SendInputTo);

    public bool TryGetOpenValueEditorTarget(ItemInteractionEvent interactionEvent, out string? targetPath)
    {
        var rules = ItemInteractionRuleCodec.ParseDefinitions(InteractionRules)
            .Where(rule => rule.Event == interactionEvent && rule.Action == ItemInteractionAction.OpenValueEditor)
            .ToList();

        if (rules.Count == 0)
        {
            targetPath = null;
            return false;
        }

        targetPath = rules[0].TargetPath;
        return true;
    }

    public bool CanOpenValueEditor
    {
        get
        {
            if (IsReadOnly)
            {
                return false;
            }

            var definition = TargetPropertyView.Definition;
            return definition.Kind is PropertyVisualKind.Text or PropertyVisualKind.Numeric or PropertyVisualKind.Hex or PropertyVisualKind.Bits
                && ResolveTargetProperty() is not null
                && ResolveWriteParameter() is not null
                && IsDeclaredWritable(Target);
        }
    }

    public string ValueEditorTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            return TargetPropertyView.Label;
        }
    }

    public string DisplayUnit
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Unit))
            {
                return Unit;
            }

            return GetTargetUnitText(Target);
        }
    }

    public double MinWidth => Kind switch
    {
        ControlKind.Button => 140,
        ControlKind.Signal => 50,
        ControlKind.ItemModel => 50,
        ControlKind.WidgetList => 240,
        ControlKind.TableControl => 240,
        ControlKind.CircleDisplay => 240,
        ControlKind.LogControl => 320,
        ControlKind.ChartControl => 360,
        ControlKind.CsvLoggerControl => 260,
        ControlKind.SqlLoggerControl => 260,
        ControlKind.CameraControl => 260,
        ControlKind.ItemClient => 320,
        _ => 140
    };

    public double MinHeight => Kind switch
    {
        ControlKind.Button => 56,
        ControlKind.Signal => 1,
        ControlKind.ItemModel => 1,
        ControlKind.WidgetList => 180,
        ControlKind.TableControl => 180,
        ControlKind.CircleDisplay => 220,
        ControlKind.LogControl => 220,
        ControlKind.ChartControl => 220,
        ControlKind.CsvLoggerControl => 120,
        ControlKind.SqlLoggerControl => 120,
        ControlKind.CameraControl => 160,
        ControlKind.ItemClient => 180,
        _ => 72
    };

    public double ChildContentWidth => System.Math.Max(120, Width - 84);

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set
        {
            if (SetProperty(ref _width, value))
            {
                RaisePropertyChanged(nameof(FontSize));
                RaisePropertyChanged(nameof(ChildContentWidth));
                RaisePropertyChanged(nameof(ItemUnitWidth));
                RaisePropertyChanged(nameof(CanOpenValueEditor));
                RaisePropertyChanged(nameof(FooterSubItemWidth));

                if (IsWidgetList)
                {
                    SyncChildWidths();
                }

                // Footer-Subitems werden nicht mehr skaliert.
            }
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (SetProperty(ref _height, value))
            {
                RaisePropertyChanged(nameof(FontSize));
                RaisePropertyChanged(nameof(ItemTitleFontSize));
                RaisePropertyChanged(nameof(ItemControlCaptionFontSize));
                RaisePropertyChanged(nameof(ItemBodyCaptionFontSize));
                RaisePropertyChanged(nameof(ItemFooterFontSize));
                RaisePropertyChanged(nameof(ItemBodyHeight));
                RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
                RaisePropertyChanged(nameof(AvailableBodyHeight));
                RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
                RaisePropertyChanged(nameof(ItemValueFontSize));
                RaisePropertyChanged(nameof(ItemUnitBaselineOffset));
                RaisePropertyChanged(nameof(ItemUnitFontSize));
                RaisePropertyChanged(nameof(ItemBottomSpacing));
                RaisePropertyChanged(nameof(ItemHeaderHeight));
                RaisePropertyChanged(nameof(ItemUnitWidth));
                RaisePropertyChanged(nameof(CanOpenValueEditor));
                RaisePropertyChanged(nameof(ButtonAvailableBodyHeight));
                RaisePropertyChanged(nameof(ButtonBodyFontSize));
                RaisePropertyChanged(nameof(ButtonIconSize));
                RaisePropertyChanged(nameof(FooterPanelHeight));

                // Tabelle: Kinder proportional mitskalieren, damit Schriftgroesse zum Zellenbereich passt.
                if (UsesTableLayout)
                {
                    SyncTableChildHeights();
                }

                if (ParentWidgetList?.IsWidgetList == true && ParentWidgetList.IsAutoHeight && !ParentWidgetList._isApplyingListHeight)
                {
                    ParentWidgetList.SyncAutoHeightFromChild(value);
                }

                ParentWidgetList?.SyncDraftListItemHeightText();
                ParentWidgetList?.RaisePropertyChanged(nameof(CurrentListItemHeight));
            }
        }
    }

    public void ApplyTheme(bool isDarkTheme)
    {
        _isDarkThemeApplied = isDarkTheme;
        EffectiveBackground = string.IsNullOrWhiteSpace(BackgroundColor) ? (isDarkTheme ? DarkBackground : LightBackground) : BackgroundColor!;
        EffectiveInnerBackground = string.IsNullOrWhiteSpace(BackgroundColor) ? (isDarkTheme ? DarkInnerBackground : LightInnerBackground) : BackgroundColor!;
        EffectiveBorderBrush = string.IsNullOrWhiteSpace(BorderColor) ? (isDarkTheme ? DarkBorder : LightBorder) : BorderColor!;
        EffectivePrimaryForeground = string.IsNullOrWhiteSpace(PrimaryForegroundColor) ? (isDarkTheme ? DarkPrimaryForeground : LightPrimaryForeground) : PrimaryForegroundColor!;
        EffectiveSecondaryForeground = string.IsNullOrWhiteSpace(SecondaryForegroundColor) ? (isDarkTheme ? DarkSecondaryForeground : LightSecondaryForeground) : SecondaryForegroundColor!;
        EffectiveMutedForeground = isDarkTheme ? DarkMutedForeground : LightMutedForeground;
        EffectiveAccentBackground = string.IsNullOrWhiteSpace(AccentBackgroundColor) ? (isDarkTheme ? DarkAccentBackground : LightAccentBackground) : AccentBackgroundColor!;
        EffectiveAccentForeground = string.IsNullOrWhiteSpace(AccentForegroundColor) ? (isDarkTheme ? DarkAccentForeground : LightAccentForeground) : AccentForegroundColor!;
        EffectiveHeaderForeground = string.IsNullOrWhiteSpace(HeaderForeColor) ? EffectiveSecondaryForeground : HeaderForeColor!;
        EffectiveHeaderBackground = string.IsNullOrWhiteSpace(HeaderBackColor) ? "Transparent" : HeaderBackColor!;
        EffectiveHeaderBorder = string.IsNullOrWhiteSpace(HeaderBorderColor) ? EffectiveBorderBrush : HeaderBorderColor!;
        EffectiveBodyForeground = string.IsNullOrWhiteSpace(BodyForeColor) ? EffectivePrimaryForeground : BodyForeColor!;
        EffectiveBodyBackground = string.IsNullOrWhiteSpace(BodyBackColor)
            ? (string.IsNullOrWhiteSpace(ContainerBackgroundColor) ? "Transparent" : ContainerBackgroundColor!)
            : BodyBackColor!;
        EffectiveDisplayBackColor = string.IsNullOrWhiteSpace(DisplayBackColor)
            ? EffectiveInnerBackground
            : DisplayBackColor!;
        EffectiveBodyBorder = string.IsNullOrWhiteSpace(BodyBorderColor)
            ? (string.IsNullOrWhiteSpace(ContainerBorder) ? EffectiveBorderBrush : ContainerBorder!)
            : BodyBorderColor!;
        EffectiveFooterForeground = string.IsNullOrWhiteSpace(FooterForeColor) ? EffectiveMutedForeground : FooterForeColor!;
        EffectiveFooterBackground = string.IsNullOrWhiteSpace(FooterBackColor) ? "Transparent" : FooterBackColor!;
        EffectiveFooterBorder = string.IsNullOrWhiteSpace(FooterBorderColor) ? EffectiveBorderBrush : FooterBorderColor!;
        ApplyVisualRuleOverrides();
        RaisePropertyChanged(nameof(EffectiveBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveInnerBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveBorderBrushValue));
        RaisePropertyChanged(nameof(EffectivePrimaryForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveSecondaryForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveMutedForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveAccentBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveAccentForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveHeaderForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveHeaderBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveHeaderBorderBrush));
        RaisePropertyChanged(nameof(EffectiveBodyForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveBodyBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveDisplayBackColorBrush));
        RaisePropertyChanged(nameof(EffectiveBodyBorderBrush));
        RaisePropertyChanged(nameof(EffectiveFooterForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveFooterBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveFooterBorderBrush));
        RaisePropertyChanged(nameof(EffectiveContainerBorderBrush));
        RaisePropertyChanged(nameof(EffectiveContainerBorderBrushValue));
        RaisePropertyChanged(nameof(EffectiveContainerBackground));
        RaisePropertyChanged(nameof(EffectiveContainerBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveButtonBodyBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveButtonHoverBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveButtonPressBackgroundBrush));
        RaisePropertyChanged(nameof(EffectiveButtonBodyForegroundBrush));
        RaisePropertyChanged(nameof(EffectiveButtonIconTintColor));
    }

    public void ResolveTarget()
    {
        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            Target = null;
            TargetPropertyPath = string.Empty;
            ClearUnresolvedSignalTargetRefreshGate();
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        if (!TryResolveTargetItem(TargetPath, out var item))
        {
            Target = null;
            TargetPropertyPath = string.Empty;
            ArmUnresolvedSignalTargetRefreshGate(TargetPath);
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        var selectedItem = item!;
        var resolvedTargetPath = GetPersistedTargetPath(selectedItem.Path, TargetPath);
        if (!string.Equals(TargetPath, resolvedTargetPath, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(resolvedTargetPath))
        {
            _targetPath = resolvedTargetPath;
            RaisePropertyChanged(nameof(TargetPath));
        }

        Target = selectedItem;
        EnsureTargetPropertySelection(selectedItem);
        ClearUnresolvedSignalTargetRefreshGate();
        CancelPendingTargetRefresh();
        TriggerTargetRefresh();
    }

    public void ApplyTargetSelection(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            TargetPath = string.Empty;
            TargetPropertyPath = string.Empty;
            Target = null;
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        if (!TryResolveTargetItem(targetPath, out var item))
        {
            return;
        }

        var previousSuggestedName = GetSuggestedNameFromTargetPath(Target?.Path ?? TargetPath);
        var previousTargetUnit = GetTargetUnitText(Target);
        var selectedItem = item!;
        _targetPath = GetPersistedTargetPath(selectedItem.Path, targetPath);
        RaisePropertyChanged(nameof(TargetPath));
        Target = selectedItem;
        EnsureTargetPropertySelection(selectedItem);
        var suggestedName = GetSuggestedNameFromTargetPath(_targetPath);

        if (string.IsNullOrWhiteSpace(Name)
            || string.Equals(Name, previousSuggestedName, StringComparison.Ordinal)
            || IsAutoGeneratedControlName(Name))
        {
            Name = suggestedName;
        }

        if (string.IsNullOrWhiteSpace(WidgetCaption)
            || string.Equals(WidgetCaption, previousSuggestedName, StringComparison.Ordinal)
            || IsAutoGeneratedControlCaption(WidgetCaption))
        {
            WidgetCaption = suggestedName;
        }

        var nextTargetUnit = GetTargetUnitText(selectedItem);
        if (string.IsNullOrWhiteSpace(Unit)
            || string.Equals(Unit, previousTargetUnit, StringComparison.Ordinal))
        {
            Unit = nextTargetUnit;
        }

        RaisePropertyChanged(nameof(DisplayUnit));
        RaisePropertyChanged(nameof(TargetPropertyView));
        CancelPendingTargetRefresh();
        TriggerTargetRefresh();
    }

    internal string SignalValueSchedulerSubscriptionKey => _signalValueSchedulerSubscriptionKey;

    internal bool IsSignalValueSchedulerActive()
        => TryGetResolvedSignalLiveValueBinding(out _);

    internal bool ApplyScheduledSignalValueRefresh(DateTimeOffset tickUtc)
    {
        if (!TryGetResolvedSignalLiveValueBinding(out var binding)
            || !SignalLiveValueStore.TryGetSnapshot(binding, out var snapshot)
            || snapshot.Version == _lastAppliedSignalLiveValueVersion)
        {
            return false;
        }

        _lastAppliedSignalLiveValueVersion = snapshot.Version;
        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: "SignalLatestValueApply",
            path: binding.RuntimeItemPath,
            module: binding.ModuleName,
            executedAtUtc: tickUtc);
        RefreshTargetValueBindings();
        return true;
    }

    private void UpdateSignalLiveValueBinding()
    {
        var hadBinding = _resolvedSignalLiveValueBinding is not null;
        if (TryCreateSignalLiveValueBinding(out var binding))
        {
            _resolvedSignalLiveValueBinding = binding;
            SignalValueUiScheduler.Subscribe(this);
            return;
        }

        _resolvedSignalLiveValueBinding = null;
        _lastAppliedSignalLiveValueVersion = -1;
        if (hadBinding)
        {
            SignalValueUiScheduler.Unsubscribe(this);
        }
    }

    private bool TryGetResolvedSignalLiveValueBinding(out SignalLiveValueBindingDescriptor binding)
    {
        binding = _resolvedSignalLiveValueBinding!;
        return binding is not null;
    }

    private bool TryCreateSignalLiveValueBinding(out SignalLiveValueBindingDescriptor binding)
    {
        binding = null!;
        if (!IsSignal
            || Target is null)
        {
            return false;
        }

        var folderName = ResolveSignalLiveValueFolderName();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var selectedParameterName = ResolveSignalLiveValueParameterName(Target);
        if (string.IsNullOrWhiteSpace(selectedParameterName))
        {
            return false;
        }

        string runtimeItemPath;
        string runtimeParameterName;
        if (TryGetSignalValueReference(out var valueRefPath, out var valueRefParameter)
            && UdlPathHelper.IsUdlRuntimePath(valueRefPath))
        {
            if (!string.Equals(selectedParameterName, valueRefParameter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            runtimeItemPath = valueRefPath;
            runtimeParameterName = valueRefParameter;
        }
        else if (!string.IsNullOrWhiteSpace(Target.Path)
                 && UdlPathHelper.IsUdlRuntimePath(Target.Path))
        {
            runtimeItemPath = Target.Path;
            runtimeParameterName = selectedParameterName;
        }
        else
        {
            return false;
        }

        if (!UdlPathHelper.TryGetRuntimeClientName(runtimeItemPath, out var clientName))
        {
            return false;
        }

        var moduleName = GetSignalPipelineModuleName(runtimeItemPath, Target);
        binding = new SignalLiveValueBindingDescriptor(
            FolderName: folderName,
            ClientName: clientName,
            PublicTargetPath: TargetPathHelper.NormalizeConfiguredTargetPath(!string.IsNullOrWhiteSpace(Target.Path) ? Target.Path : TargetPath),
            RuntimeItemPath: TargetPathHelper.NormalizeConfiguredTargetPath(runtimeItemPath),
            RuntimeParameterName: runtimeParameterName,
            ModuleName: moduleName);
        return true;
    }

    private string ResolveSignalLiveValueFolderName()
    {
        if (!string.IsNullOrWhiteSpace(FolderName))
        {
            return FolderName;
        }

        var targetPath = !string.IsNullOrWhiteSpace(Target?.Path) ? Target.Path : TargetPath;
        var segments = TargetPathHelper.SplitPathSegments(targetPath);
        if (segments.Count >= 2 && string.Equals(segments[0], "studio", StringComparison.OrdinalIgnoreCase))
        {
            return segments[1];
        }

        return string.Empty;
    }

    private string ResolveSignalLiveValueParameterName(ItemModel targetItem)
    {
        if (!string.IsNullOrWhiteSpace(TargetPropertyPath)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(TargetPropertyPath)
            && targetItem.Properties.Has(TargetPropertyPath))
        {
            return TargetPropertyPath;
        }

        if (targetItem.Properties.Has("read"))
        {
            return "read";
        }

        return string.Empty;
    }

    public void RefreshTargetBindings()
    {
        var targetPropertyView = TargetPropertyView;
        var itemBodyPresentation = ItemBodyPresentation;
        RecordSignalVisualPipelineState(targetPropertyView, itemBodyPresentation);

        var targetParameterViewChanged = HasPresentationChanged(targetPropertyView, ref _lastTargetParameterViewSignature);
        var itemBodyPresentationChanged = HasPresentationChanged(itemBodyPresentation, ref _lastItemBodyPresentationSignature);

        RaisePropertyChanged(nameof(DisplayValue));
        RaisePropertyChanged(nameof(DisplayUnit));
        RaisePropertyChanged(nameof(RequestStatusText));
        RaisePropertyChanged(nameof(DisplayFooter));
        if (targetParameterViewChanged)
        {
            RecordSignalVisualPropertyChanged(stage: "SignalTargetPropertyChanged");
            RaisePropertyChanged(nameof(TargetPropertyView));
        }

        if (itemBodyPresentationChanged)
        {
            RecordSignalVisualPropertyChanged(stage: "SignalDisplayPropertyChanged");
            RaisePropertyChanged(nameof(ItemBodyPresentation));
        }

        RaisePropertyChanged(nameof(ShowBodyCaption));
        RaisePropertyChanged(nameof(ShowTopBodyCaption));
        RaisePropertyChanged(nameof(ShowInlineBodyCaption));
        RaisePropertyChanged(nameof(ShowItemFooterPanel));
        RaisePropertyChanged(nameof(ItemBodyHeight));
        RaisePropertyChanged(nameof(ItemBodyCaptionHeight));
        RaisePropertyChanged(nameof(AvailableBodyHeight));
        RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
        RaisePropertyChanged(nameof(ItemUnitWidth));
        RaisePropertyChanged(nameof(ItemValueFontSize));
        RaisePropertyChanged(nameof(ItemUnitFontSize));
        RaisePropertyChanged(nameof(CanOpenValueEditor));
    }

    private bool IsScriptTarget
    {
        get
        {
            if (!SupportsLegacyPythonScript)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(PythonScriptPath))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                return false;
            }

            if (TargetPath.StartsWith("python:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TargetPath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    private bool SupportsLegacyPythonScript => !IsSignal;

    private string NormalizeConfiguredTargetPathForKind(string? value)
    {
        var normalizedValue = value ?? string.Empty;
        return IsSignal && IsLegacyPythonTargetPath(normalizedValue)
            ? string.Empty
            : normalizedValue;
    }

    private string NormalizePythonScriptPathForKind(string? value)
        => IsSignal ? string.Empty : value ?? string.Empty;

    private static bool IsLegacyPythonTargetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("python:", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
    }

    public string? ResolveConfiguredScriptPath()
    {
        // 1) Rohen Skript-Namen aus den Properties ermitteln
        string? script = null;

        if (!string.IsNullOrWhiteSpace(PythonScriptPath))
        {
            script = PythonScriptPath.TrimStart('/', '\\');
        }
        else if (!string.IsNullOrWhiteSpace(TargetPath))
        {
            if (TargetPath.StartsWith("python:", StringComparison.OrdinalIgnoreCase))
            {
                script = TargetPath["python:".Length..].TrimStart('/', '\\');
            }
            else
            {
                script = TargetPath;
            }
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            return null;
        }

        // 2) Bereits absoluter Pfad? Dann direkt zurueckgeben.
        if (System.IO.Path.IsPathRooted(script))
        {
            return script;
        }

        // 3) Relativen Namen moeglichst auf Basis der Layout-Datei aufloesen.
        //    Damit landet das Skript im gleichen Folder wie Folder.yaml/Folder.json.
        var layoutPath = FolderLayoutPath;
        if (!string.IsNullOrWhiteSpace(layoutPath))
        {
            var layoutDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(layoutPath));
            if (!string.IsNullOrWhiteSpace(layoutDirectory))
            {
                var normalized = script
                    .Replace('/', System.IO.Path.DirectorySeparatorChar)
                    .TrimStart(System.IO.Path.DirectorySeparatorChar);

                // Wenn schon ein Unterpfad enthalten ist ("Scripts/foo.py" oder "Sub/foo.py"),
                // direkt relativ zum Layout-Verzeichnis verwenden.
                if (normalized.Contains(System.IO.Path.DirectorySeparatorChar))
                {
                    return System.IO.Path.Combine(layoutDirectory, normalized);
                }

                // Nur Dateiname: bevorzugt den neuen Standardordner "Scripts"
                // neben der Layout-Datei, faellt fuer bestehende Projekte aber auf
                // den alten Ordner "Skript" zurueck.
                var preferredPath = System.IO.Path.Combine(layoutDirectory, "Scripts", normalized);
                var legacyPath = System.IO.Path.Combine(layoutDirectory, "Skript", normalized);
                return File.Exists(preferredPath) || !File.Exists(legacyPath)
                    ? preferredPath
                    : legacyPath;
            }
        }

        // 4) Fallback: unveraendert zurueckgeben, PythonScriptHost.ResolvePath
        //    kuemmert sich dann (z.B. im Host-Prozess ueber Core.OpenedDirectory).
        return script;
    }

    public bool TryUpdateTargetPropertyValue(object? rawValue, out string error)
    {
        if (IsReadOnly)
        {
            error = "ItemModel ist schreibgeschuetzt.";
            return false;
        }

        if (Target is null)
        {
            error = "Kein Target fuer den Writeback gesetzt.";
            return false;
        }

        if (!IsDeclaredWritable(Target))
        {
            error = "Target ist nicht schreibbar.";
            return false;
        }

        var parameter = ResolveWriteParameter();
        if (parameter is null)
        {
            error = "Kein Parameter fuer den Writeback gefunden.";
            return false;
        }

        try
        {
            var targetParameter = ResolveTargetProperty();
            var writeTargetItem = ResolveWriteTargetItem();
            var refreshDisplayImmediately = Target is not null
                && (ReferenceEquals(writeTargetItem, Target)
                    || TargetPathHelper.PathsEqual(writeTargetItem.Path, Target.Path));
            var convertedValue = ConvertEditorValue(rawValue, parameter.Value?.GetType() ?? targetParameter?.Value?.GetType());
            var targetLogPath = Target?.Path ?? TargetPath;
            Core.LogInfo(
                $"[SignalWrite] item={Path} target={targetLogPath} targetParam={targetParameter?.Name ?? "<none>"} targetValue={FormatDiagnosticValue(targetParameter?.Value)} writeTarget={writeTargetItem.Path ?? "<none>"} writeParam={parameter.Name} raw={FormatDiagnosticValue(rawValue)} converted={FormatDiagnosticValue(convertedValue)}");
            if (!string.Equals(parameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                && !HostRegistryPropertyPolicy.CanUserWriteProperty(parameter.Name))
            {
                error = $"Parameter '{parameter.Name}' is protected and cannot be written.";
                Core.LogInfo($"[SignalWrite] result=blocked item={Path} writeParam={parameter.Name}");
                return false;
            }

            var targetPath = writeTargetItem.Path ?? Target?.Path ?? TargetPath;
            var forceWriteNotification = string.Equals(parameter.Name, "write", StringComparison.OrdinalIgnoreCase);
            var updated = string.Equals(parameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(targetPath, convertedValue)
                : HostRegistries.Data.TryUpdateUserProperty(targetPath, parameter.Name, convertedValue, forceChangeNotification: forceWriteNotification);
            if (!updated)
            {
                if (string.Equals(parameter.Name, "read", StringComparison.OrdinalIgnoreCase))
                {
                    writeTargetItem.Value = convertedValue!;
                }
                else
                {
                    parameter.Value = convertedValue!;
                }

                PublishTargetSnapshot();
            }

            if (refreshDisplayImmediately)
            {
                RefreshTargetBindings();
            }

            error = string.Empty;
            Core.LogInfo(
                $"[SignalWrite] result=ok item={Path} writeTarget={targetPath} writeParam={parameter.Name} value={FormatDiagnosticValue(convertedValue)} registryUpdated={updated}");
            Core.LogInfo(
                $"[SignalWrite] state item={Path} writeTarget={targetPath} writeParam={parameter.Name} current={FormatRegistryWriteState(targetPath, parameter.Name)} targetSameAsWriteTarget={refreshDisplayImmediately}");
            return true;

        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }


    public bool TrySendInput(object? rawValue, out string error)
    {
        var rules = ItemInteractionRuleCodec.ParseDefinitions(InteractionRules)
            .Where(rule => rule.Action == ItemInteractionAction.SendInputTo)
            .ToList();

        if (rules.Count == 0)
        {
            return TryUpdateTargetPropertyValue(rawValue, out error);
        }

        foreach (var rule in rules)
        {
            if (!TryResolveInteractionTarget(rule.TargetPath, out var target))
            {
                error = "Target fuer SendInputTo wurde nicht gefunden.";
                return false;
            }

            if (!TryApplyInteractionWrite(target!, rule.TargetPath, rawValue, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }


    public bool TryToggleTargetBit(int bitIndex, out string error)
    {
        if (bitIndex < 0)
        {
            error = "Ungueltiger Bitindex.";
            return false;
        }

        var readParameter = ResolveTargetProperty();
        var writeParameter = ResolveWriteParameter();
        var parameter = writeParameter ?? readParameter;
        if (parameter is null)
        {
            error = "Kein Parameter fuer den Writeback gefunden.";
            return false;
        }

        var currentValue = ToUInt64ForBitOperations(parameter.Value);
        var updatedValue = currentValue ^ (1UL << bitIndex);
        Core.LogInfo(
            $"[SignalBitToggle] item={Path} target={Target?.Path ?? TargetPath} bit={bitIndex} readParam={readParameter?.Name ?? "<none>"} readValue={FormatDiagnosticValue(readParameter?.Value)} writeParam={writeParameter?.Name ?? "<none>"} writeValue={FormatDiagnosticValue(writeParameter?.Value)} currentMask=0x{currentValue:X} updatedMask=0x{updatedValue:X}");
        var result = TrySendInput((long)updatedValue, out error);
        Core.LogInfo(
            $"[SignalBitToggle] result={(result ? "ok" : "failed")} item={Path} target={Target?.Path ?? TargetPath} bit={bitIndex} updatedMask=0x{updatedValue:X} error={error}");
        return result;
    }

    public bool TryExecuteInteraction(ItemInteractionEvent interactionEvent, MainWindowViewModel? viewModel, out string error)
    {
        error = string.Empty;
        var matchingRules = ItemInteractionRuleCodec.ParseDefinitions(InteractionRules)
            .Where(rule => rule.Event == interactionEvent && rule.Action != ItemInteractionAction.SendInputTo)
            .ToList();

        if (matchingRules.Count == 0)
        {
            return false;
        }

        foreach (var rule in matchingRules)
        {
            if (!TryExecuteInteractionRule(rule, viewModel, out error))
            {
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to start one catalog function using the existing RunFunction execution path.
    /// </summary>
    /// <param name="functionReference">The stable catalog function reference.</param>
    /// <param name="argument">The optional function argument.</param>
    /// <param name="error">The resulting error message when execution could not be started.</param>
    /// <returns><see langword="true"/> when execution was started; otherwise <see langword="false"/>.</returns>
    public bool TryRunCatalogFunction(string? functionReference, string? argument, out string error)
    {
        var rule = new ItemInteractionRule
        {
            Action = ItemInteractionAction.RunFunction,
            FunctionName = functionReference?.Trim() ?? string.Empty,
            Argument = argument?.Trim() ?? string.Empty,
            TargetPath = "this"
        };

        return TryStartRunFunction(rule, out error);
    }

    /// <summary>
    /// Tries to request a controlled stop for one running catalog function.
    /// </summary>
    /// <param name="functionReference">The stable catalog function reference.</param>
    /// <param name="error">The resulting error message when the stop request could not be sent.</param>
    /// <returns><see langword="true"/> when the stop request was accepted; otherwise <see langword="false"/>.</returns>
    public bool TryStopCatalogFunction(string? functionReference, out string error)
    {
        var rule = new ItemInteractionRule
        {
            Action = ItemInteractionAction.StopFunction,
            FunctionName = functionReference?.Trim() ?? string.Empty,
            TargetPath = "this"
        };

        return TryStopRunFunction(rule, out error);
    }

    /// <summary>
    /// Gets a value indicating whether the specified catalog function is currently running.
    /// </summary>
    /// <param name="functionReference">The stable catalog function reference.</param>
    /// <returns><see langword="true"/> when a matching declarative execution is active; otherwise <see langword="false"/>.</returns>
    public bool IsCatalogFunctionRunning(string? functionReference)
    {
        var normalizedFunctionReference = FunctionRegistry.NormalizeReference(functionReference);
        if (string.IsNullOrWhiteSpace(normalizedFunctionReference))
        {
            return false;
        }

        return RunningDeclarativeFunctions.Values.Any(execution => string.Equals(
            execution.NormalizedFunctionReference,
            normalizedFunctionReference,
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a value indicating whether the specified catalog function has a pending stop request.
    /// </summary>
    /// <param name="functionReference">The stable catalog function reference.</param>
    /// <returns><see langword="true"/> when a matching declarative execution is stopping; otherwise <see langword="false"/>.</returns>
    public bool IsCatalogFunctionStopping(string? functionReference)
    {
        var normalizedFunctionReference = FunctionRegistry.NormalizeReference(functionReference);
        if (string.IsNullOrWhiteSpace(normalizedFunctionReference))
        {
            return false;
        }

        return RunningDeclarativeFunctions.Values.Any(execution => string.Equals(
                execution.NormalizedFunctionReference,
                normalizedFunctionReference,
                StringComparison.OrdinalIgnoreCase)
            && execution.StopController.IsStopRequested);
    }

    public bool TryExecuteButtonCommand(out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(ButtonCommand))
        {
            return false;
        }

        var commandText = ButtonCommand.Trim();

        if (commandText.StartsWith("python:", StringComparison.OrdinalIgnoreCase) || commandText.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = commandText.StartsWith("python:", StringComparison.OrdinalIgnoreCase)
                ? commandText["python:".Length..].TrimStart('/', '\\')
                : commandText;

            try
            {
                HornetStudio.Host.Python.Legacy.PythonScriptHost.ExecuteButtonScript(scriptPath);
                return true;
            }
            catch
            {
                error = "Python-Skript konnte nicht ausgefuehrt werden.";
                return true;
            }
        }

        if (HostRegistries.Commands.Execute(commandText))
        {
            return true;
        }

        error = "ButtonCommand wurde nicht im Host gefunden.";
        return true;
    }

    public void SyncChildWidths()
    {
        if (!IsWidgetList)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.Width = ChildContentWidth;
        }
    }

    public void SyncFooterSubItemLayout()
    {
        // Footer-Subitems werden nicht mehr verwendet.
    }

    private bool TryExecuteInteractionRule(ItemInteractionRule rule, MainWindowViewModel? viewModel, out string error)
    {
        error = string.Empty;

        switch (rule.Action)
        {
            case ItemInteractionAction.OpenValueEditor:
                if (viewModel is null)
                {
                    error = "Kein ViewModel fuer den Value-Editor verfuegbar.";
                    return false;
                }

                viewModel.OpenValueInputForTargetPath(rule.TargetPath, this);
                return true;

            case ItemInteractionAction.ToggleBool:
                if (!TryResolveInteractionTarget(rule.TargetPath, out var toggleTarget))
                {
                    error = "Target fuer ToggleBool wurde nicht gefunden.";
                    return false;
                }

                var toggleRead = ResolveInteractionReadParameter(rule.TargetPath, toggleTarget!);
                var toggleWrite = ResolveInteractionWriteParameter(rule.TargetPath, toggleTarget!);
                if (toggleWrite is null)
                {
                    error = "Kein Write-Parameter fuer ToggleBool gefunden.";
                    return false;
                }

                var toggledValue = ToBooleanLikeValue(toggleRead?.Value ?? toggleWrite.Value) ? 0 : 1;
                return TryApplyInteractionWrite(toggleTarget!, rule.TargetPath, toggledValue, out error);

            case ItemInteractionAction.SetValue:
                if (!TryResolveInteractionTarget(rule.TargetPath, out var setTarget))
                {
                    error = "Target fuer SetValue wurde nicht gefunden.";
                    return false;
                }

                return TryExecuteSetValueInteraction(rule, setTarget!, out error);

            case ItemInteractionAction.OpenDialog:
                if (viewModel is null)
                {
                    error = "Kein ViewModel fuer OpenDialog verfuegbar.";
                    return false;
                }

                return viewModel.OpenDialogWidget(rule.TargetPath, rule.Argument, this, out error);

            case ItemInteractionAction.CloseDialog:
                if (viewModel is null)
                {
                    error = "Kein ViewModel fuer CloseDialog verfuegbar.";
                    return false;
                }

                return viewModel.CloseDialogWidget(rule.TargetPath, this, out error);

            case ItemInteractionAction.InvokePythonFunction:
                return TryInvokeApplicationFunction(rule, out error);

            case ItemInteractionAction.RunFunction:
                return TryStartRunFunction(rule, out error);

            case ItemInteractionAction.StopFunction:
                return TryStopRunFunction(rule, out error);

            default:
                error = $"Action {rule.Action} wird noch nicht unterstuetzt.";
                return false;
        }
    }

    private bool TryStartRunFunction(ItemInteractionRule rule, out string error)
    {
        if (string.IsNullOrWhiteSpace(rule.FunctionName))
        {
            error = "No function reference configured.";
            Core.LogWarn($"[RunFunction] item={Path} failed: {error}");
            return false;
        }

        var folderDirectory = GetFunctionRegistryDirectory();
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            error = "Function registry directory is not available.";
            Core.LogWarn($"[RunFunction] item={Path} function={rule.FunctionName} failed: {error}");
            return false;
        }

        if (!FunctionRegistry.TryGetEntry(folderDirectory, rule.FunctionName, out var entry) || entry is null)
        {
            error = $"Function '{rule.FunctionName}' was not found.";
            Core.LogWarn($"[RunFunction] item={Path} function={rule.FunctionName} failed: {error}");
            return false;
        }

        if (!entry.CanRun || !entry.IsValid)
        {
            error = string.IsNullOrWhiteSpace(entry.StatusText)
                ? $"Function '{rule.FunctionName}' is not runnable."
                : entry.StatusText;
            Core.LogWarn($"[RunFunction] item={Path} function={rule.FunctionName} failed: {error}");
            return false;
        }

        return entry.Kind switch
        {
            FunctionCatalogKind.Declarative => TryStartRunDeclarativeFunction(rule.FunctionName, entry, out error),
            FunctionCatalogKind.Python => TryStartRunPythonFunction(rule.FunctionName, rule.Argument, entry, out error),
            _ => FailRunFunctionUnsupportedKind(rule.FunctionName, entry.Kind, out error)
        };
    }

    private bool TryStartRunDeclarativeFunction(string functionReference, FunctionCatalogEntry entry, out string error)
    {
        if (!FunctionDefinitionCodec.TryLoadFromFile(entry.SourceIdentifier, out var definition, out var validation) || definition is null)
        {
            error = validation.Errors.FirstOrDefault()?.Message ?? $"Function '{functionReference}' could not be loaded.";
            Core.LogWarn($"[RunFunction] item={Path} function={functionReference} failed: {error}");
            return false;
        }

        Core.LogInfo($"[RunFunction] start item={Path} function={functionReference} source={entry.SourceIdentifier} kind={entry.Kind}");
        var stopController = new FunctionExecutionStopController();
        var executionKey = BuildDeclarativeFunctionExecutionKey();
        var execution = new RunningDeclarativeFunctionExecution(
            ExecutionKey: executionKey,
            OwnerPath: Path,
            FunctionReference: functionReference,
            NormalizedFunctionReference: FunctionRegistry.NormalizeReference(functionReference),
            StopController: stopController);
        RunningDeclarativeFunctions[executionKey] = execution;
        RaiseCatalogFunctionExecutionStateChanged();
        _ = ExecuteRunFunctionAsync(executionKey, execution, definition);
        error = string.Empty;
        return true;
    }

    private bool TryStopRunFunction(ItemInteractionRule rule, out string error)
    {
        if (string.IsNullOrWhiteSpace(rule.FunctionName))
        {
            error = "No function reference configured.";
            Core.LogWarn($"[StopFunction] item={Path} failed: {error}");
            return false;
        }

        var normalizedFunctionReference = FunctionRegistry.NormalizeReference(rule.FunctionName);
        var matchingExecutions = RunningDeclarativeFunctions.Values
            .Where(execution => string.Equals(execution.NormalizedFunctionReference, normalizedFunctionReference, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingExecutions.Length == 0)
        {
            error = $"Function '{rule.FunctionName}' is not running.";
            Core.LogWarn($"[StopFunction] item={Path} function={rule.FunctionName} failed: {error}");
            return false;
        }

        if (matchingExecutions.Length > 1)
        {
            error = $"Function '{rule.FunctionName}' is running more than once and cannot be stopped unambiguously.";
            Core.LogWarn($"[StopFunction] item={Path} function={rule.FunctionName} failed: {error}");
            return false;
        }

        var execution = matchingExecutions[0];

        execution.StopController.RequestStop();
        RaiseCatalogFunctionExecutionStateChanged();
        Core.LogInfo($"[StopFunction] item={Path} function={rule.FunctionName} requested stop for owner={execution.OwnerPath}");
        error = string.Empty;
        return true;
    }

    private bool TryStartRunPythonFunction(string functionReference, string? argument, FunctionCatalogEntry entry, out string error)
    {
        var resolvedTargetPath = entry.SourceIdentifier?.Trim() ?? string.Empty;
        var functionName = entry.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedTargetPath) || string.IsNullOrWhiteSpace(functionName))
        {
            error = $"Function '{functionReference}' is not runnable.";
            Core.LogWarn($"[RunFunction] item={Path} function={functionReference} failed: {error}");
            return false;
        }

        if (!PythonClientRuntimeRegistry.TryGetClient(resolvedTargetPath, out var client) || client is null)
        {
            error = $"Python target '{resolvedTargetPath}' is not available.";
            Core.LogWarn($"[RunFunction] item={Path} function={functionReference} failed: {error}");
            return false;
        }

        Core.LogInfo($"[RunFunction] start item={Path} function={functionReference} source={resolvedTargetPath} kind={entry.Kind}");
        _ = ExecuteRunPythonFunctionAsync(functionReference, resolvedTargetPath, functionName, client, argument);
        error = string.Empty;
        return true;
    }

    private bool FailRunFunctionUnsupportedKind(string functionReference, FunctionCatalogKind kind, out string error)
    {
        error = $"Function '{functionReference}' uses unsupported kind '{kind}'.";
        Core.LogWarn($"[RunFunction] item={Path} function={functionReference} failed: {error}");
        return false;
    }

    private async Task ExecuteRunPythonFunctionAsync(string functionReference, string resolvedTargetPath, string functionName, PythonClient client, string? argument)
    {
        try
        {
            var result = await client.InvokeFunctionAsync(
                    functionName,
                    BuildPythonInteractionArgumentPayload(argument))
                .ConfigureAwait(false);

            if (!result.Success)
            {
                var message = string.IsNullOrWhiteSpace(result.Message)
                    ? $"Python function '{functionReference}' failed."
                    : result.Message!;

                if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
                {
                    failedRow?.SetInvocationError(HornetStudio.Editor.Widgets.ApplicationErrorDetails.FromResultPayload(failedRow.Name, message, result.Payload));
                }

                Core.LogWarn($"[RunFunction] item={Path} function={functionReference} failed: {message}");
                return;
            }

            if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var successRow))
            {
                successRow?.ClearInvocationError();
            }

            Core.LogInfo($"[RunFunction] success item={Path} function={functionReference}");
        }
        catch (Exception ex)
        {
            if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
            {
                failedRow?.SetInvocationError(ex.Message);
            }

            Core.LogWarn($"[RunFunction] item={Path} function={functionReference} threw an exception: {ex.Message}", ex);
        }
    }

    private async Task ExecuteRunFunctionAsync(string executionKey, RunningDeclarativeFunctionExecution execution, FunctionDefinition definition)
    {
        try
        {
            var result = await FunctionExecutor.ExecuteAsync(
                    definition,
                    CreateRunFunctionExecutionEnvironment(execution.StopController))
                .ConfigureAwait(false);

            if (result.State == FunctionState.Done)
            {
                Core.LogInfo($"[RunFunction] success item={Path} function={execution.FunctionReference}");
                return;
            }

            var message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? $"Function '{execution.FunctionReference}' finished with state '{result.State}'."
                : result.ErrorMessage;
            Core.LogWarn($"[RunFunction] item={Path} function={execution.FunctionReference} failed: {message}");
        }
        catch (Exception ex)
        {
            Core.LogWarn($"[RunFunction] item={Path} function={execution.FunctionReference} threw an exception: {ex.Message}", ex);
        }
        finally
        {
            if (RunningDeclarativeFunctions.TryRemove(executionKey, out _))
            {
                RaiseCatalogFunctionExecutionStateChanged();
            }
        }
    }

    private FunctionExecutionEnvironment CreateRunFunctionExecutionEnvironment(FunctionExecutionStopController stopController)
        => new()
        {
            SetValueAsync = ExecuteRunFunctionSetValueAsync,
            WriteLogAsync = ExecuteRunFunctionLogAsync,
            ResolveConditionSourceValueAsync = ResolveRunFunctionConditionSourceValueAsync,
            StopController = stopController
        };

    private string BuildDeclarativeFunctionExecutionKey()
        => Guid.NewGuid().ToString("N");

    private static void RaiseCatalogFunctionExecutionStateChanged()
        => CatalogFunctionExecutionStateChanged?.Invoke(sender: null, e: EventArgs.Empty);

    private sealed record RunningDeclarativeFunctionExecution(
        string ExecutionKey,
        string OwnerPath,
        string FunctionReference,
        string NormalizedFunctionReference,
        FunctionExecutionStopController StopController);

    private async ValueTask ExecuteRunFunctionSetValueAsync(FunctionSetValueStepDefinition step, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryResolveInteractionTarget(step.Target, out var targetItem) || targetItem is null)
            {
                throw new InvalidOperationException($"Target '{step.Target}' was not found.");
            }

            if (!string.IsNullOrWhiteSpace(step.ValueFrom))
            {
                var legacyOperation = new SetValueOperation
                {
                    Kind = SetValueOperationKind.SetFromItem,
                    SourcePath = step.ValueFrom,
                    IsLegacyLiteral = false
                };

                if (!TryResolveRunFunctionSetValueValue(legacyOperation, step.Target, targetItem, out var legacyResolvedValue, out var legacyError))
                {
                    throw new InvalidOperationException(legacyError);
                }

                if (!TryApplyInteractionWrite(targetItem, step.Target, legacyResolvedValue, out var legacyWriteError))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(legacyWriteError)
                        ? $"Target '{step.Target}' could not be written."
                        : legacyWriteError);
                }

                return;
            }

            var parsedOperation = SetValueOperationCodec.Parse(step.Value);
            if (!parsedOperation.IsValid)
            {
                throw new InvalidOperationException(parsedOperation.ErrorMessage);
            }

            if (parsedOperation.Operation.IsLegacyLiteral)
            {
                if (!TryApplyInteractionWrite(targetItem, step.Target, parsedOperation.Operation.LiteralValue, out var legacyLiteralError))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(legacyLiteralError)
                        ? $"Target '{step.Target}' could not be written."
                        : legacyLiteralError);
                }

                return;
            }

            var targetKind = GetInteractionSetValueTargetKind(step.Target, targetItem);
            var validation = SetValueOperationCodec.Validate(
                parsedOperation.Operation,
                targetKind,
                isCompatibleSourcePath: sourcePath => IsCompatibleInteractionSourcePath(sourcePath, targetKind));
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.ErrorMessage);
            }

            if (!TryResolveRunFunctionSetValueValue(parsedOperation.Operation, step.Target, targetItem, out var resolvedValue, out var resolveError))
            {
                throw new InvalidOperationException(resolveError);
            }

            if (!TryApplyInteractionWrite(targetItem, step.Target, resolvedValue, out var error))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"Target '{step.Target}' could not be written."
                    : error);
            }
        });
    }

    private bool TryResolveRunFunctionSetValueValue(SetValueOperation operation, string? targetPath, ItemModel targetItem, out object? resolvedValue, out string error)
        => TryResolveSetValueOperationValue(operation, targetPath, targetItem, out resolvedValue, out error);

    private ValueTask ExecuteRunFunctionLogAsync(FunctionLogStepDefinition step, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveRunFunctionProcessLog(step.TargetLog, out var processLog) || processLog is null)
        {
            throw new InvalidOperationException($"Log target '{step.TargetLog}' could not be resolved.");
        }

        processLog.WriteEntry(ToLogEventLevel(step.Level), step.Text ?? string.Empty);
        return ValueTask.CompletedTask;
    }

    private ValueTask<FunctionConditionVariableResolutionResult> ResolveRunFunctionConditionSourceValueAsync(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveInteractionTarget(sourcePath, out var targetItem) || targetItem is null)
        {
            return ValueTask.FromResult(new FunctionConditionVariableResolutionResult(false, null));
        }

        return ValueTask.FromResult(new FunctionConditionVariableResolutionResult(true, targetItem.Value));
    }

    private bool TryInvokeApplicationFunction(ItemInteractionRule rule, out string error)
    {
        if (string.IsNullOrWhiteSpace(rule.TargetPath))
        {
            error = "Kein Python-Anwendungsziel konfiguriert.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.FunctionName))
        {
            error = "Keine Python-Funktion konfiguriert.";
            return false;
        }

        var resolvedTargetPath = HornetStudio.Editor.Widgets.ApplicationExplorerRuntime.ResolveInteractionTargetPath(this, rule.TargetPath);
        if (!PythonClientRuntimeRegistry.TryGetClient(resolvedTargetPath, out var client) || client is null)
        {
            error = $"Python-Anwendung '{rule.TargetPath}' ist nicht aktiv.";
            return false;
        }

        try
        {
            var result = client.InvokeFunctionAsync(
                    rule.FunctionName,
                    BuildPythonInteractionArgumentPayload(rule.Argument))
                .GetAwaiter()
                .GetResult();

            if (!result.Success)
            {
                error = string.IsNullOrWhiteSpace(result.Message)
                    ? $"Python-Funktion '{rule.FunctionName}' ist fehlgeschlagen."
                    : result.Message!;
                if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
                {
                    failedRow?.SetInvocationError(HornetStudio.Editor.Widgets.ApplicationErrorDetails.FromResultPayload(failedRow.Name, error, result.Payload));
                }

                Core.LogWarn($"[ApplicationExplorer] Python-Funktion '{rule.FunctionName}' in '{resolvedTargetPath}' fehlgeschlagen: {error}");
                return false;
            }

            if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var successRow))
            {
                successRow?.ClearInvocationError();
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (HornetStudio.Editor.Widgets.ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
            {
                failedRow?.SetInvocationError(error);
            }

            Core.LogWarn($"[ApplicationExplorer] Python-Funktion '{rule.FunctionName}' in '{resolvedTargetPath}' wurde mit Ausnahme beendet: {error}", ex);
            return false;
        }
    }

    private string GetFunctionRegistryDirectory()
    {
        if (string.IsNullOrWhiteSpace(FolderLayoutPath))
        {
            return string.Empty;
        }

        var layoutDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(FolderLayoutPath));
        return string.IsNullOrWhiteSpace(layoutDirectory)
            ? string.Empty
            : layoutDirectory;
    }

    private bool TryResolveRunFunctionProcessLog(string? targetLog, out ProcessLog? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(targetLog))
        {
            return false;
        }

        var normalized = NormalizeLogTargetPath(targetLog);
        foreach (var candidate in EnumerateProcessLogResolutionCandidates(normalized, FolderName))
        {
            if (HostRegistries.Data.TryResolve(candidate, out var item) && item?.Value is ProcessLog processLog)
            {
                resolved = processLog;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateProcessLogResolutionCandidates(string normalizedTargetLog, string? folderName)
    {
        if (string.IsNullOrWhiteSpace(normalizedTargetLog))
        {
            yield break;
        }

        yield return normalizedTargetLog;

        var normalizedFolder = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolder))
        {
            yield break;
        }

        if (normalizedTargetLog.StartsWith("logs.", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"studio.{normalizedFolder}.{normalizedTargetLog}";
        }
    }

    private static LogEventLevel ToLogEventLevel(MonitorLogLevel level)
    {
        return level switch
        {
            MonitorLogLevel.Debug => LogEventLevel.Debug,
            MonitorLogLevel.Info => LogEventLevel.Information,
            MonitorLogLevel.Warning => LogEventLevel.Warning,
            MonitorLogLevel.Error => LogEventLevel.Error,
            MonitorLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Warning
        };
    }

    public IReadOnlyList<string> GetApplicationInteractionTargets()
    {
        if (!IsApplicationExplorer)
        {
            return [];
        }

        return HornetStudio.Editor.Widgets.ApplicationDefinitionHelper.ParseDefinitions(ApplicationDefinitions)
            .Select(env => HornetStudio.Editor.Widgets.ApplicationExplorerRuntime.BuildInteractionTargetPath(this, env.Name))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static JsonNode BuildPythonInteractionArgumentPayload(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return new JsonObject();
        }

        var trimmed = argument.Trim();
        try
        {
            var parsed = JsonNode.Parse(trimmed);
            if (parsed is JsonObject or JsonArray)
            {
                return parsed;
            }

            return new JsonObject
            {
                ["value"] = parsed
            };
        }
        catch
        {
            return new JsonObject
            {
                ["value"] = trimmed
            };
        }
    }

    private bool TryResolveInteractionTarget(string? targetPath, out ItemModel? item)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
        {
            item = Target;
            return item is not null;
        }

        return TryResolveTargetItem(targetPath, out item);
    }

    private ItemProperty? ResolveInteractionReadParameter(string? targetPath, ItemModel targetItem)
    {
        if ((string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
            && ReferenceEquals(targetItem, Target))
        {
            return ResolveTargetProperty();
        }

        if (targetItem.Properties.Has("read"))
        {
            return targetItem.Properties["read"];
        }

        var firstParameter = targetItem.Properties.GetDictionary().Keys
            .Where(HostRegistryPropertyPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return firstParameter is null ? null : targetItem.Properties[firstParameter];
    }

    private ItemProperty? ResolveInteractionWriteParameter(string? targetPath, ItemModel targetItem)
    {
        if ((string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
            && ReferenceEquals(targetItem, Target))
        {
            return ResolveWriteParameter();
        }

        if (targetItem.Properties.Has("write"))
        {
            return targetItem.Properties["write"];
        }

        if (TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget))
        {
            return declaredTarget.Properties.Has("write")
                ? declaredTarget.Properties["write"]
                : ResolveValueParameter(declaredTarget);
        }

        return ResolveInteractionReadParameter(targetPath, targetItem);
    }

    private bool TryExecuteSetValueInteraction(ItemInteractionRule rule, ItemModel targetItem, out string error)
    {
        var parsedOperation = SetValueOperationCodec.Parse(rule.Argument);
        if (!parsedOperation.IsValid)
        {
            error = parsedOperation.ErrorMessage;
            Core.LogInfo($"[SetValue] result=invalid item={Path} target={rule.TargetPath} error={error}");
            return false;
        }

        if (parsedOperation.Operation.IsLegacyLiteral)
        {
            return TryApplyInteractionWrite(targetItem, rule.TargetPath, parsedOperation.Operation.LiteralValue, out error);
        }

        var targetKind = GetInteractionSetValueTargetKind(rule.TargetPath, targetItem);
        var validation = SetValueOperationCodec.Validate(
            parsedOperation.Operation,
            targetKind,
            isCompatibleSourcePath: sourcePath => IsCompatibleInteractionSourcePath(sourcePath, targetKind));
        if (!validation.IsValid)
        {
            error = validation.ErrorMessage;
            Core.LogInfo($"[SetValue] result=blocked item={Path} target={rule.TargetPath} operation={parsedOperation.Operation.Kind} error={error}");
            return false;
        }

        if (!TryResolveSetValueOperationValue(parsedOperation.Operation, rule.TargetPath, targetItem, out var resolvedValue, out error))
        {
            Core.LogInfo($"[SetValue] result=failed item={Path} target={rule.TargetPath} operation={parsedOperation.Operation.Kind} error={error}");
            return false;
        }

        return TryApplyInteractionWrite(targetItem, rule.TargetPath, resolvedValue, out error);
    }

    private bool TryResolveSetValueOperationValue(SetValueOperation operation, string? targetPath, ItemModel targetItem, out object? resolvedValue, out string error)
    {
        resolvedValue = null;
        error = string.Empty;

        var targetKind = GetInteractionSetValueTargetKind(targetPath, targetItem);
        var writeParameter = ResolveInteractionWriteParameter(targetPath, targetItem);
        var readParameter = ResolveInteractionReadParameter(targetPath, targetItem);
        var currentValue = writeParameter?.Value ?? readParameter?.Value;
        if (currentValue is null && TryResolveSetValueSiblingReadValue(targetPath, out var siblingReadValue))
        {
            currentValue = siblingReadValue;
        }

        switch (operation.Kind)
        {
            case SetValueOperationKind.SetLiteral:
                resolvedValue = operation.LiteralValue;
                return true;

            case SetValueOperationKind.SetFromItem:
                if (!TryResolveInteractionTarget(operation.SourcePath, out var sourceItem) || sourceItem is null)
                {
                    error = $"Value source '{operation.SourcePath}' was not found.";
                    return false;
                }

                resolvedValue = ResolveInteractionReadParameter(operation.SourcePath, sourceItem)?.Value ?? sourceItem.Value?.ToString() ?? string.Empty;
                return true;

            case SetValueOperationKind.IncrementBy:
            {
                object? numericResolvedValue;
                string numericError;
                var success = TryResolveNumericOperationValue(currentValue, operation.LiteralValue, false, out numericResolvedValue, out numericError);
                resolvedValue = numericResolvedValue;
                error = numericError;
                return success;
            }

            case SetValueOperationKind.DecrementBy:
            {
                object? numericResolvedValue;
                string numericError;
                var success = TryResolveNumericOperationValue(currentValue, operation.LiteralValue, true, out numericResolvedValue, out numericError);
                resolvedValue = numericResolvedValue;
                error = numericError;
                return success;
            }

            case SetValueOperationKind.IncrementOne:
            {
                object? numericResolvedValue;
                string numericError;
                var success = TryResolveNumericOperationValue(currentValue, "1", false, out numericResolvedValue, out numericError);
                resolvedValue = numericResolvedValue;
                error = numericError;
                return success;
            }

            case SetValueOperationKind.DecrementOne:
            {
                object? numericResolvedValue;
                string numericError;
                var success = TryResolveNumericOperationValue(currentValue, "1", true, out numericResolvedValue, out numericError);
                resolvedValue = numericResolvedValue;
                error = numericError;
                return success;
            }

            case SetValueOperationKind.AppendText:
            {
                var currentText = currentValue?.ToString() ?? string.Empty;
                var appendedText = operation.LiteralValue ?? string.Empty;
                var separator = operation.Separator ?? string.Empty;
                var shouldInsertSeparator = !string.IsNullOrEmpty(separator)
                    && !string.IsNullOrEmpty(currentText)
                    && !string.IsNullOrEmpty(appendedText);
                resolvedValue = shouldInsertSeparator
                    ? string.Concat(currentText, separator, appendedText)
                    : string.Concat(currentText, appendedText);
                return true;
            }

            case SetValueOperationKind.SetTrue:
                resolvedValue = true;
                return true;

            case SetValueOperationKind.SetFalse:
                resolvedValue = false;
                return true;

            default:
                error = targetKind == SetValueTargetKind.Unknown
                    ? "The target type is unknown and this operation cannot be applied."
                    : $"SetValue operation '{operation.Kind}' is not supported.";
                return false;
        }
    }

    private bool TryResolveNumericOperationValue(object? currentValue, string? literalDelta, bool subtract, out object? resolvedValue, out string error)
    {
        resolvedValue = null;
        error = string.Empty;

        if (!TryConvertToDouble(currentValue, out var numericCurrentValue))
        {
            error = "The current target value is not numeric.";
            return false;
        }

        if (!SetValueOperationCodec.TryParseNumericLiteral(literalDelta, out var numericDelta))
        {
            error = "The numeric SetValue delta is invalid.";
            return false;
        }

        resolvedValue = subtract
            ? numericCurrentValue - numericDelta
            : numericCurrentValue + numericDelta;
        return true;
    }

    private SetValueTargetKind GetInteractionSetValueTargetKind(string? targetPath, ItemModel targetItem)
    {
        var writeParameter = ResolveInteractionWriteParameter(targetPath, targetItem);
        var readParameter = ResolveInteractionReadParameter(targetPath, targetItem);
        var declaredType = targetItem.Properties.Has("type")
            ? targetItem.Properties["type"].Value?.ToString()
            : null;
        var targetValue = writeParameter?.Value ?? readParameter?.Value;
        var targetType = writeParameter?.Value?.GetType() ?? readParameter?.Value?.GetType();
        if (targetValue is null && TryResolveSetValueSiblingReadValue(targetPath, out var siblingReadValue))
        {
            targetValue = siblingReadValue;
            targetType = siblingReadValue?.GetType();
        }

        return SetValueOperationCodec.ClassifyTargetKind(declaredType, targetType, targetValue);
    }

    private bool TryResolveSetValueSiblingReadValue(string? targetPath, out object? value)
    {
        value = null;
        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
        if (!normalizedPath.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var readPath = normalizedPath[..^".set".Length] + ".read";
        if (!TryResolveInteractionTarget(readPath, out var readItem) || readItem is null)
        {
            return false;
        }

        value = ResolveInteractionReadParameter(readPath, readItem)?.Value ?? readItem.Value;
        return value is not null;
    }

    private bool IsCompatibleInteractionSourcePath(string? sourcePath, SetValueTargetKind targetKind)
    {
        if (!TryResolveInteractionTarget(sourcePath, out var sourceItem) || sourceItem is null)
        {
            return false;
        }

        var sourceReadParameter = ResolveInteractionReadParameter(sourcePath, sourceItem);
        var declaredType = sourceItem.Properties.Has("type")
            ? sourceItem.Properties["type"].Value?.ToString()
            : null;
        var sourceKind = SetValueOperationCodec.ClassifyTargetKind(
            declaredType,
            sourceReadParameter?.Value?.GetType(),
            sourceReadParameter?.Value ?? sourceItem.Value);

        return targetKind == SetValueTargetKind.Unknown
               || sourceKind == SetValueTargetKind.Unknown
               || sourceKind == targetKind;
    }

    private bool TryApplyInteractionWrite(ItemModel targetItem, string? targetPath, object? rawValue, out string error)
    {
        if (!IsDeclaredWritable(targetItem))
        {
            error = "Target ist nicht schreibbar.";
            return false;
        }

        var writeParameter = ResolveInteractionWriteParameter(targetPath, targetItem);
        var readParameter = ResolveInteractionReadParameter(targetPath, targetItem);
        var writeTargetItem = ResolveInteractionWriteTargetItem(targetItem);
        if (writeParameter is null)
        {
            error = "Kein Write-Parameter fuer die Action gefunden.";
            return false;
        }

        try
        {
            var declaredTargetType = targetItem.Properties.Has("type")
                ? targetItem.Properties["type"].Value?.ToString()
                : null;
            var effectiveTargetType = writeParameter.Value?.GetType()
                                      ?? readParameter?.Value?.GetType()
                                      ?? TargetValueTypes.ToClrType(TargetValueTypes.Parse(declaredTargetType));
            var convertedValue = ConvertEditorValue(rawValue, effectiveTargetType);
            if (!string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                && !HostRegistryPropertyPolicy.CanUserWriteProperty(writeParameter.Name))
            {
                error = $"Parameter '{writeParameter.Name}' is protected and cannot be written.";
                return false;
            }

            var resolvedTargetPath = writeTargetItem.Path ?? targetItem.Path ?? targetPath ?? string.Empty;
            var forceWriteNotification = string.Equals(writeParameter.Name, "write", StringComparison.OrdinalIgnoreCase);
            var updated = string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(resolvedTargetPath, convertedValue)
                : HostRegistries.Data.TryUpdateUserProperty(resolvedTargetPath, writeParameter.Name, convertedValue, forceChangeNotification: forceWriteNotification);
            if (!updated)
            {
                if (string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase))
                {
                    writeTargetItem.Value = convertedValue!;
                }
                else
                {
                    writeParameter.Value = convertedValue!;
                }

                PublishItemSnapshot(targetItem);
            }

            if (ReferenceEquals(targetItem, Target))
            {
                RefreshTargetBindings();
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool ToBooleanLikeValue(object? value)
        => value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong) => parsedLong != 0,
            byte numeric => numeric != 0,
            sbyte numeric => numeric != 0,
            short numeric => numeric != 0,
            ushort numeric => numeric != 0,
            int numeric => numeric != 0,
            uint numeric => numeric != 0,
            long numeric => numeric != 0,
            ulong numeric => numeric != 0,
            float numeric => System.Math.Abs(numeric) > float.Epsilon,
            double numeric => System.Math.Abs(numeric) > double.Epsilon,
            decimal numeric => numeric != 0,
            _ => false
        };

    private static bool TryConvertToDouble(object? value, out double numericValue)
    {
        switch (value)
        {
            case byte byteValue:
                numericValue = byteValue;
                return true;
            case sbyte sbyteValue:
                numericValue = sbyteValue;
                return true;
            case short shortValue:
                numericValue = shortValue;
                return true;
            case ushort ushortValue:
                numericValue = ushortValue;
                return true;
            case int intValue:
                numericValue = intValue;
                return true;
            case uint uintValue:
                numericValue = uintValue;
                return true;
            case long longValue:
                numericValue = longValue;
                return true;
            case ulong ulongValue:
                numericValue = ulongValue;
                return true;
            case float floatValue:
                numericValue = floatValue;
                return true;
            case double doubleValue:
                numericValue = doubleValue;
                return true;
            case decimal decimalValue:
                numericValue = (double)decimalValue;
                return true;
            case string textValue when SetValueOperationCodec.TryParseNumericLiteral(textValue, out var parsedNumericValue):
                numericValue = parsedNumericValue;
                return true;
            default:
                numericValue = 0d;
                return false;
        }
    }

    public void ApplyWidgetListDefaultsToChild(FolderItemModel item)
    {
        if (!IsWidgetList)
        {
            return;
        }

        item.Width = ChildContentWidth;
        item.BorderWidth = WidgetBorderWidth;
        item.BorderColor = ControlBorderColor;
        item.CornerRadius = WidgetCornerRadius;
        if (IsAutoHeight)
        {
            item.Height = System.Math.Max(ListItemHeight, item.MinHeight);
        }
        else
        {
            item.Height = System.Math.Max(item.Height, item.MinHeight);
        }
    }

    public void AttachChildToList(FolderItemModel item)
    {
        if (!IsWidgetList)
        {
            return;
        }

        item.ParentWidgetList = this;
        item.ParentItem = this;
        item.FolderName = FolderName;
        item.SetLayoutFilePath(FolderLayoutPath);
        item.ApplyActiveView(ActiveViewId);
        item.RefreshPathRecursive();
        item.ResolveTarget();
        ApplyWidgetListDefaultsToChild(item);
    }

    public void SetLayoutFilePath(string? layoutFilePath)
    {
        var normalizedLayoutPath = string.IsNullOrWhiteSpace(layoutFilePath)
            ? string.Empty
            : System.IO.Path.GetFullPath(layoutFilePath);

        if (string.Equals(_folderLayoutPath, normalizedLayoutPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _folderLayoutPath = normalizedLayoutPath;

        if (!string.IsNullOrWhiteSpace(_buttonIcon))
        {
            ButtonIcon = _buttonIcon;
        }
        else
        {
            RaisePropertyChanged(nameof(EffectiveButtonIconPath));
        }

        foreach (var child in Items)
        {
            child.SetLayoutFilePath(_folderLayoutPath);
        }
    }

    public void SetHierarchy(string pageName, FolderItemModel? parentItem, int activeViewId = 1)
    {
        FolderName = pageName;
        ParentItem = parentItem;
        ParentWidgetList = parentItem?.IsWidgetList == true ? parentItem : null;
        if (parentItem is not null)
        {
            SetLayoutFilePath(parentItem.FolderLayoutPath);
        }

        ApplyActiveView(parentItem?.ActiveViewId ?? activeViewId);
        RefreshPathRecursive();
        ResolveTarget();
    }

    public void ApplyActiveView(int activeViewId)
    {
        ActiveViewId = activeViewId;

        foreach (var child in Items)
        {
            child.ApplyActiveView(ActiveViewId);
        }
    }

    public void ApplyListHeightRules()
    {
        if (!IsWidgetList || !IsAutoHeight)
        {
            return;
        }

        _isApplyingListHeight = true;
        try
        {
            foreach (var item in Items)
            {
                item.Height = System.Math.Max(ListItemHeight, item.MinHeight);
            }
        }
        finally
        {
            _isApplyingListHeight = false;
        }

        SyncDraftListItemHeightText();
        RaisePropertyChanged(nameof(CurrentListItemHeight));
        RaisePropertyChanged(nameof(WidgetHeight));
    }

    public void ApplyEnteredListHeight(double value)
    {
        var normalized = System.Math.Max(40, value);
        if (IsAutoHeight)
        {
            ListItemHeight = normalized;
            ApplyListHeightRules();
            SyncDraftListItemHeightText();
            return;
        }

        CurrentListItemHeight = normalized;
        SyncDraftListItemHeightText();
    }

    public void SyncDraftListItemHeightText()
    {
        DraftListItemHeightText = $"{CurrentListItemHeight:0}";
    }

    private void SyncAutoHeightFromChild(double value)
    {
        if (!IsWidgetList || !IsAutoHeight)
        {
            return;
        }

        var normalized = System.Math.Max(40, value);
        if (System.Math.Abs(ListItemHeight - normalized) < 0.01)
        {
            return;
        }

        _isApplyingListHeight = true;
        try
        {
            _listItemHeight = normalized;
            RaisePropertyChanged(nameof(ListItemHeight));
            RaisePropertyChanged(nameof(CurrentListItemHeight));
            RaisePropertyChanged(nameof(WidgetHeight));
            foreach (var item in Items)
            {
                item.Height = System.Math.Max(normalized, item.MinHeight);
            }
        }
        finally
        {
            _isApplyingListHeight = false;
        }

        SyncDraftListItemHeightText();
    }

    private void OnDataRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        var visualRulesAffected = HasMatchingVisualRuleSource(e.Key);
        if (!visualRulesAffected && string.IsNullOrWhiteSpace(TargetPath))
        {
            return;
        }

        if (visualRulesAffected && Dispatcher.UIThread.CheckAccess())
        {
            ApplyTheme(_isDarkThemeApplied);
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                QueueRegistryRefresh();
            }

            return;
        }

        if (TryHandleSignalLiveValueRegistryFastPath(e, visualRulesAffected))
        {
            return;
        }

        var matchedTargetPath = TargetPathHelper.EnumerateResolutionCandidates(TargetPath, FolderName)
            .Concat(TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(TargetPath))
            .FirstOrDefault(candidate => TargetPathHelper.PathsEqual(e.Key, candidate)
                || TargetPathHelper.IsDescendantPath(e.Key, candidate)
                || TargetPathHelper.IsDescendantPath(candidate, e.Key));

        if (string.IsNullOrWhiteSpace(matchedTargetPath))
        {
            if (TryHandleSignalValueRefRegistryChange(e, visualRulesAffected))
            {
                return;
            }

            RecordSignalIrrelevantEvent(path: e.Key, item: e.ItemModel);

            if (!visualRulesAffected)
            {
                return;
            }

            RequestTargetRefresh();
            return;
        }

        var isDirectTarget = TargetPathHelper.PathsEqual(e.Key, matchedTargetPath);
        var isChildTarget = TargetPathHelper.IsDescendantPath(e.Key, matchedTargetPath);
        var isAncestorTarget = TargetPathHelper.IsDescendantPath(matchedTargetPath, e.Key);
        if (!isDirectTarget && !isChildTarget && !isAncestorTarget)
        {
            RecordSignalIrrelevantEvent(path: e.Key, item: e.ItemModel);
            return;
        }

        RecordSignalRegistryEvent(stage: "SignalRegistryEventRelevant", path: matchedTargetPath, item: e.ItemModel);
        RecordSignalPipelineReceive(path: matchedTargetPath, item: e.ItemModel);

        if (ShouldSuppressUnresolvedTargetRefresh(
                e,
                matchedTargetPath,
                isDirectTarget,
                isAncestorTarget))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (IsTargetValueOnlyChange(e, matchedTargetPath, isDirectTarget, isAncestorTarget))
            {
                QueueTargetValueRefresh(e, matchedTargetPath);
                return;
            }

            QueueRegistryRefresh();
            return;
        }

        var previousTargetPropertyPath = TargetPropertyPath;
        if (isDirectTarget && (Target is null || !ReferenceEquals(Target, e.ItemModel)))
        {
            Target = e.ItemModel;
        }

        if (isAncestorTarget
            && TargetPathHelper.TryGetRelativePath(matchedTargetPath, e.Key, out var targetRelativePath)
            && TryResolveRelativeChild(e.ItemModel, targetRelativePath, out var resolvedTarget)
            && resolvedTarget is not null)
        {
            Target = resolvedTarget;
        }

        if (isChildTarget && Target is not null)
        {
            if (TargetPathHelper.TryGetRelativePath(e.Key, matchedTargetPath, out var childRelativePath))
            {
                ApplyChildRegistryUpdate(Target, childRelativePath, e);
            }
        }

        if (Target is not null)
        {
            EnsureTargetPropertySelection(Target);
        }

        if (string.Equals(previousTargetPropertyPath, TargetPropertyPath, StringComparison.Ordinal)
            && IsTargetValueOnlyChange(e, matchedTargetPath, isDirectTarget, isAncestorTarget))
        {
            RequestTargetValueRefresh();
            return;
        }

        RequestTargetRefresh();
    }

    private bool TryHandleSignalLiveValueRegistryFastPath(DataChangedEventArgs e, bool visualRulesAffected)
    {
        if (visualRulesAffected
            || !TryGetResolvedSignalLiveValueBinding(out var binding))
        {
            return false;
        }

        var publicPathMatched = MatchesSignalBindingPath(e.Key, binding.PublicTargetPath);
        var runtimePathMatched = MatchesSignalBindingPath(e.Key, binding.RuntimeItemPath);
        if (!publicPathMatched && !runtimePathMatched)
        {
            return true;
        }

        if (publicPathMatched
            && IsTargetValueOnlyChange(
                e,
                binding.PublicTargetPath,
                isDirectTarget: TargetPathHelper.PathsEqual(e.Key, binding.PublicTargetPath),
                isAncestorTarget: TargetPathHelper.IsDescendantPath(binding.PublicTargetPath, e.Key)))
        {
            RecordSignalPipelineStage(stage: "SignalRegistryValueRefreshBypassed", path: binding.PublicTargetPath, module: binding.ModuleName);
            return true;
        }

        if (runtimePathMatched
            && IsSignalValueReferenceValueOnlyChange(
                e,
                binding.RuntimeItemPath,
                binding.RuntimeParameterName,
                isDirectValueRef: TargetPathHelper.PathsEqual(e.Key, binding.RuntimeItemPath),
                isAncestorValueRef: TargetPathHelper.IsDescendantPath(binding.RuntimeItemPath, e.Key)))
        {
            RecordSignalPipelineStage(stage: "SignalRuntimeValueRefreshBypassed", path: binding.RuntimeItemPath, module: binding.ModuleName);
            return true;
        }

        return false;
    }

    private static bool MatchesSignalBindingPath(string? changedPath, string? bindingPath)
    {
        if (string.IsNullOrWhiteSpace(changedPath) || string.IsNullOrWhiteSpace(bindingPath))
        {
            return false;
        }

        return TargetPathHelper.PathsEqual(changedPath, bindingPath)
               || TargetPathHelper.IsDescendantPath(changedPath, bindingPath)
               || TargetPathHelper.IsDescendantPath(bindingPath, changedPath);
    }

    private bool TryHandleSignalValueRefRegistryChange(DataChangedEventArgs e, bool visualRulesAffected)
    {
        if (!IsSignal
            || CanUseResolvedTargetProperty()
            || !TryGetSignalValueReference(out var valueRefPath, out var valueRefParameter)
            || !MatchesSignalValueReferencePath(e.Key, valueRefPath, out var isDirectValueRef, out var isAncestorValueRef))
        {
            return false;
        }

        RecordSignalRegistryEvent(stage: "SignalValueRefDirtyReceived", path: valueRefPath, item: e.ItemModel);
        RecordSignalPipelineReceive(path: valueRefPath, item: e.ItemModel);

        if (ShouldSuppressUnresolvedValueReferenceRefresh(valueRefPath, e.ItemModel))
        {
            return true;
        }

        var valueOnlyChange = IsSignalValueReferenceValueOnlyChange(
            e,
            valueRefPath,
            valueRefParameter,
            isDirectValueRef,
            isAncestorValueRef);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (valueOnlyChange)
            {
                RequestTargetValueRefresh();
                return true;
            }

            QueueRegistryRefresh();
            return true;
        }

        if (valueOnlyChange)
        {
            RequestTargetValueRefresh();
            return true;
        }

        if (visualRulesAffected)
        {
            ApplyTheme(_isDarkThemeApplied);
        }

        RequestTargetRefresh();
        return true;
    }

    private void QueueRegistryRefresh()
    {
        if (Interlocked.Exchange(ref _registryRefreshQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _registryRefreshQueued, 0);
            ApplyTheme(_isDarkThemeApplied);
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                return;
            }

            ResolveTarget();
            RequestTargetRefresh();
        }, DispatcherPriority.Background);
    }

    private void ArmUnresolvedSignalTargetRefreshGate(string? targetPath)
    {
        if (!IsSignal)
        {
            return;
        }

        var normalizedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
        if (string.IsNullOrWhiteSpace(normalizedTargetPath)
            || string.Equals(_unresolvedSignalTargetPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _unresolvedSignalTargetPath = normalizedTargetPath;
        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(unresolvedTargetGateDelta: 1);
        RecordSignalRegistryEvent(stage: "SignalTargetAvailabilityGateArmed", path: normalizedTargetPath, item: Target);
    }

    private void ClearUnresolvedSignalTargetRefreshGate()
    {
        if (string.IsNullOrWhiteSpace(_unresolvedSignalTargetPath))
        {
            return;
        }

        _unresolvedSignalTargetPath = string.Empty;
        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(unresolvedTargetGateDelta: -1);
    }

    private bool ShouldSuppressUnresolvedTargetRefresh(
        DataChangedEventArgs e,
        string matchedTargetPath,
        bool isDirectTarget,
        bool isAncestorTarget)
    {
        if (!IsUnresolvedSignalTargetRefreshGateActive(matchedTargetPath))
        {
            return false;
        }

        if (CanResolveUnresolvedSignalTarget(e, matchedTargetPath, isDirectTarget, isAncestorTarget))
        {
            RecordSignalRegistryEvent(stage: "SignalTargetAvailabilityDetected", path: matchedTargetPath, item: e.ItemModel);
            ClearUnresolvedSignalTargetRefreshGate();
            return false;
        }

        RecordSignalRegistryEvent(stage: "SignalTargetAwaitingAvailability", path: matchedTargetPath, item: e.ItemModel);
        return true;
    }

    private bool ShouldSuppressUnresolvedValueReferenceRefresh(string valueRefPath, ItemModel? item)
    {
        if (!IsUnresolvedSignalTargetRefreshGateActive(TargetPath))
        {
            return false;
        }

        RecordSignalRegistryEvent(stage: "SignalValueRefAwaitingTargetAvailability", path: valueRefPath, item: item);
        return true;
    }

    private bool IsUnresolvedSignalTargetRefreshGateActive(string? matchedTargetPath)
    {
        if (!IsSignal)
        {
            return false;
        }

        var unresolvedTargetPath = _unresolvedSignalTargetPath;
        if (string.IsNullOrWhiteSpace(unresolvedTargetPath))
        {
            return false;
        }

        var normalizedMatchedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(matchedTargetPath);
        return !string.IsNullOrWhiteSpace(normalizedMatchedTargetPath)
            && TargetPathHelper.PathsEqual(normalizedMatchedTargetPath, unresolvedTargetPath);
    }

    private bool CanResolveUnresolvedSignalTarget(
        DataChangedEventArgs e,
        string matchedTargetPath,
        bool isDirectTarget,
        bool isAncestorTarget)
    {
        if (TryResolveTargetItem(matchedTargetPath, out var resolvedTarget) && resolvedTarget is not null)
        {
            return true;
        }

        if (isDirectTarget && e.ItemModel is not null)
        {
            return true;
        }

        return isAncestorTarget
            && e.ItemModel is not null
            && TryGetTargetRelativePath(matchedTargetPath, e.Key, out var targetRelativePath)
            && TryResolveRelativeChild(e.ItemModel, targetRelativePath, out resolvedTarget)
            && resolvedTarget is not null;
    }

    private static void ApplyChildRegistryUpdate(ItemModel rootItem, string relativePath, DataChangedEventArgs e)
    {
        var current = rootItem;
        foreach (var segment in TargetPathHelper.SplitPathSegments(relativePath))
        {
            if (!current.Has(segment))
            {
                return;
            }

            current = current[segment];
        }

        if (string.Equals(e.ParameterName, "read", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            current.Value = e.ItemModel.Value;
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.ParameterName) && e.ItemModel.Properties.Has(e.ParameterName) && current.Properties.Has(e.ParameterName))
        {
            current.Properties[e.ParameterName].Value = e.ItemModel.Properties[e.ParameterName].Value;
        }
    }

    private void PublishTargetSnapshot()
    {
        if (Target is null)
        {
            return;
        }

        PublishItemSnapshot(Target);
    }

    private static void PublishItemSnapshot(ItemModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        HostRegistries.Data.UpsertSnapshot(item.Path!, item.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
    }

    private void RequestTargetRefresh()
    {
        var refreshRateMs = RefreshRateMs;
        if (refreshRateMs <= 0)
        {
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var dueAt = _lastTargetRefreshUtc + TimeSpan.FromMilliseconds(refreshRateMs);
        if (_lastTargetRefreshUtc == DateTimeOffset.MinValue || now >= dueAt)
        {
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        _hasPendingTargetRefresh = true;
        SchedulePendingRefresh(dueAt - now);
    }

    private bool IsTargetValueOnlyChange(DataChangedEventArgs e, string matchedTargetPath, bool isDirectTarget, bool isAncestorTarget)
    {
        if (Kind is not (ControlKind.Signal or ControlKind.ItemModel)
            || IsScriptTarget
            || Target is null
            || e.ChangeKind == DataChangeKind.SnapshotUpserted)
        {
            return false;
        }

        if (!isDirectTarget)
        {
            if (!isAncestorTarget || !IsTargetAncestorPropertyChange(e, matchedTargetPath))
            {
                return false;
            }
        }
        else if (isAncestorTarget)
        {
            return false;
        }

        if (e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            return true;
        }

        return e.ChangeKind == DataChangeKind.PropertyUpdated
            && IsValueOnlyTargetParameter(e.ParameterName);
    }

    private bool IsSignalValueReferenceValueOnlyChange(
        DataChangedEventArgs e,
        string valueRefPath,
        string valueRefParameter,
        bool isDirectValueRef,
        bool isAncestorValueRef)
    {
        if (Target is null || e.ChangeKind == DataChangeKind.SnapshotUpserted)
        {
            return false;
        }

        if (!isDirectValueRef)
        {
            if (!isAncestorValueRef || !IsTargetAncestorPropertyChange(e, valueRefPath))
            {
                return false;
            }
        }
        else if (isAncestorValueRef)
        {
            return false;
        }

        if (e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            return true;
        }

        return e.ChangeKind == DataChangeKind.PropertyUpdated
            && IsValueOnlySignalValueReferenceParameter(e.ParameterName, valueRefParameter);
    }

    private bool IsValueOnlyTargetParameter(string? parameterName)
    {
        var normalizedParameterName = parameterName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedParameterName))
        {
            return false;
        }

        return string.Equals(normalizedParameterName, TargetPropertyPath, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(TargetPropertyPath) && string.Equals(normalizedParameterName, "read", StringComparison.OrdinalIgnoreCase))
            || string.Equals(normalizedParameterName, "unit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedParameterName, "SendStatus", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValueOnlySignalValueReferenceParameter(string? parameterName, string valueRefParameter)
    {
        var normalizedParameterName = parameterName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedParameterName))
        {
            return false;
        }

        return string.Equals(normalizedParameterName, valueRefParameter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedParameterName, "unit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedParameterName, "SendStatus", StringComparison.OrdinalIgnoreCase);
    }

    private void QueueTargetValueRefresh(DataChangedEventArgs e, string matchedTargetPath)
    {
        var pendingItem = e.ItemModel;
        if (IsTargetAncestorPropertyChange(e, matchedTargetPath)
            && TryGetTargetRelativePath(matchedTargetPath, e.Key, out var targetRelativePath)
            && TryResolveRelativeChild(e.ItemModel, targetRelativePath, out var resolvedTarget)
            && resolvedTarget is not null)
        {
            pendingItem = resolvedTarget;
        }

        lock (_targetValueRefreshSync)
        {
            _pendingTargetValueRefresh = new PendingTargetValueRefresh(
                e.Key,
                matchedTargetPath,
                pendingItem,
                e.ChangeKind,
                e.ParameterName,
                TargetPropertyPath);
            _hasPendingTargetValueRefresh = true;
        }

        RecordSignalRegistryEvent(stage: "SignalLatestValueStored", path: matchedTargetPath, item: pendingItem);

        RequestTargetValueRefresh();
    }

    private void RequestTargetValueRefresh()
    {
        var queuedAtUtc = DateTimeOffset.UtcNow;
        var queuedPath = _lastSignalDiagnosticsPath;
        var queuedModule = _lastSignalDiagnosticsModule;
        if (Interlocked.Exchange(ref _targetValueRefreshQueued, 1) == 1)
        {
            RecordSignalPipelineStage(stage: "SignalValueRefreshCoalesced", path: queuedPath, module: queuedModule);
            return;
        }

        lock (_targetValueRefreshSync)
        {
            _targetValueRefreshQueuedUtc = queuedAtUtc;
            _targetValueRefreshQueuedPath = queuedPath;
            _targetValueRefreshQueuedModule = queuedModule;
        }

        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetValueRefreshQueuedDelta: 1);
        RecordSignalPipelineStage(stage: "SignalValueRefreshScheduled", path: queuedPath, module: queuedModule);
        RecordTargetValueRefreshQueued(queuedAtUtc, queuedPath, queuedModule);

        Dispatcher.UIThread.Post(ExecuteTargetValueRefresh, DispatcherPriority.Background);
    }

    private void ExecuteTargetValueRefresh()
    {
        Interlocked.Exchange(ref _targetValueRefreshQueued, 0);
        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetValueRefreshQueuedDelta: -1);

        var queuedAtUtc = DateTimeOffset.MinValue;
        var queuedPath = string.Empty;
        var queuedModule = string.Empty;
        var hasPendingTargetValueRefresh = false;
        var pendingTargetValueRefresh = default(PendingTargetValueRefresh);
        lock (_targetValueRefreshSync)
        {
            queuedAtUtc = _targetValueRefreshQueuedUtc;
            _targetValueRefreshQueuedUtc = DateTimeOffset.MinValue;
            queuedPath = _targetValueRefreshQueuedPath;
            queuedModule = _targetValueRefreshQueuedModule;
            _targetValueRefreshQueuedPath = string.Empty;
            _targetValueRefreshQueuedModule = string.Empty;
            if (_hasPendingTargetValueRefresh)
            {
                pendingTargetValueRefresh = _pendingTargetValueRefresh;
                _pendingTargetValueRefresh = default;
                _hasPendingTargetValueRefresh = false;
                hasPendingTargetValueRefresh = true;
            }
        }

        if (hasPendingTargetValueRefresh && !ApplyPendingTargetValueRefresh(pendingTargetValueRefresh))
        {
            TriggerTargetRefresh();
            return;
        }

        if (hasPendingTargetValueRefresh)
        {
            RecordSignalRegistryEvent(
                stage: "SignalLatestValueApplied",
                path: pendingTargetValueRefresh.MatchedTargetPath,
                item: pendingTargetValueRefresh.Item);
        }

        RecordTargetValueRefreshExecuted(queuedAtUtc, queuedPath, queuedModule);
        RefreshTargetValueBindings();
        RecordSignalValueReferenceApplied();
        _lastTargetRefreshUtc = DateTimeOffset.UtcNow;
    }

    private void RecordSignalValueReferenceApplied()
    {
        if (!TryResolveSignalValueReferenceProperty(out _, out var runtimeItem, out var valueRefPath, out _))
        {
            return;
        }

        RecordSignalRegistryEvent(stage: "SignalValueRefLatestValueApplied", path: valueRefPath, item: runtimeItem);
    }

    private bool ApplyPendingTargetValueRefresh(PendingTargetValueRefresh pendingRefresh)
    {
        if (Target is null
            || !IsCurrentTargetCandidate(pendingRefresh.MatchedTargetPath)
            || !string.Equals(pendingRefresh.TargetPropertyPath, TargetPropertyPath, StringComparison.Ordinal))
        {
            return false;
        }

        Target = pendingRefresh.Item;
        if (Target is null)
        {
            return false;
        }

        var previousTargetPropertyPath = TargetPropertyPath;
        EnsureTargetPropertySelection(Target);
        return string.Equals(previousTargetPropertyPath, TargetPropertyPath, StringComparison.Ordinal)
            && IsTargetValueOnlyChange(
                new DataChangedEventArgs(
                    pendingRefresh.ChangedPath,
                    pendingRefresh.Item,
                    pendingRefresh.ChangeKind,
                    pendingRefresh.ParameterName),
                pendingRefresh.MatchedTargetPath,
                isDirectTarget: true,
                isAncestorTarget: false);
    }

    private static bool IsTargetAncestorPropertyChange(DataChangedEventArgs e, string matchedTargetPath)
    {
        if (e.ChangeKind != DataChangeKind.PropertyUpdated || string.IsNullOrWhiteSpace(e.ParameterName))
        {
            return false;
        }

        return TryGetTargetRelativePath(matchedTargetPath, e.Key, out var targetRelativePath)
            && !targetRelativePath.Contains('.', StringComparison.Ordinal)
            && string.Equals(targetRelativePath, e.ParameterName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetTargetRelativePath(string matchedTargetPath, string changedPath, out string targetRelativePath)
        => TargetPathHelper.TryGetRelativePath(matchedTargetPath, changedPath, out targetRelativePath);

    private bool IsCurrentTargetCandidate(string matchedTargetPath)
        => TargetPathHelper.EnumerateResolutionCandidates(TargetPath, FolderName)
            .Concat(TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(TargetPath))
            .Any(candidate => TargetPathHelper.PathsEqual(candidate, matchedTargetPath));

    private void RefreshTargetValueBindings()
    {
        var targetPropertyView = TargetPropertyView;
        var itemBodyPresentation = ItemBodyPresentation;
        RecordSignalVisualPipelineState(targetPropertyView, itemBodyPresentation);

        var targetParameterViewChanged = HasPresentationChanged(targetPropertyView, ref _lastTargetParameterViewSignature);
        var itemBodyPresentationChanged = HasPresentationChanged(itemBodyPresentation, ref _lastItemBodyPresentationSignature);

        RaisePropertyChanged(nameof(DisplayValue));
        RaisePropertyChanged(nameof(DisplayUnit));
        RaisePropertyChanged(nameof(RequestStatusText));
        if (targetParameterViewChanged)
        {
            RecordSignalVisualPropertyChanged(stage: "SignalTargetPropertyChanged");
            RaisePropertyChanged(nameof(TargetPropertyView));
        }

        if (itemBodyPresentationChanged)
        {
            RecordSignalVisualPropertyChanged(stage: "SignalDisplayPropertyChanged");
            RaisePropertyChanged(nameof(ItemBodyPresentation));
        }

        RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
        RaisePropertyChanged(nameof(ItemUnitWidth));
        RaisePropertyChanged(nameof(ItemValueFontSize));
        RaisePropertyChanged(nameof(ItemUnitFontSize));
        RaisePropertyChanged(nameof(CanOpenValueEditor));
    }

    private void RecordSignalVisualPipelineState(PropertyDisplayModel targetPropertyView, PropertyDisplayModel itemBodyPresentation)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        var (path, module) = GetSignalVisualDiagnosticIdentity();

        var rawValueSignature = GetSignalRawValueSignature(targetPropertyView);
        if (!string.Equals(rawValueSignature, _lastSignalRawValueSignature, StringComparison.Ordinal))
        {
            _lastSignalRawValueSignature = rawValueSignature;
            UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
                stage: "TargetRawValueChanged",
                path: path,
                module: module);
        }

        var resolvedDisplayValue = DisplayValue;
        if (!string.Equals(resolvedDisplayValue, _lastSignalResolvedDisplayValue, StringComparison.Ordinal))
        {
            _lastSignalResolvedDisplayValue = resolvedDisplayValue;
            UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
                stage: "SignalResolvedDisplayValueChanged",
                path: path,
                module: module);
        }

        var displayText = itemBodyPresentation.ValueText ?? string.Empty;
        if (!string.Equals(displayText, _lastSignalDisplayText, StringComparison.Ordinal))
        {
            _lastSignalDisplayText = displayText;
            UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
                stage: "SignalDisplayTextChanged",
                path: path,
                module: module);
        }
    }

    private void RecordSignalVisualPropertyChanged(string stage)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        var (path, module) = GetSignalVisualDiagnosticIdentity();
        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module);
    }

    private (string Path, string Module) GetSignalVisualDiagnosticIdentity()
    {
        var path = !string.IsNullOrWhiteSpace(_lastSignalDiagnosticsPath)
            ? _lastSignalDiagnosticsPath
            : !string.IsNullOrWhiteSpace(Target?.Path)
                ? TargetPathHelper.NormalizeConfiguredTargetPath(Target.Path)
                : TargetPathHelper.NormalizeConfiguredTargetPath(TargetPath);
        var module = !string.IsNullOrWhiteSpace(_lastSignalDiagnosticsModule)
            ? _lastSignalDiagnosticsModule
            : GetSignalPipelineModuleName(path, Target);
        return (path, module);
    }

    private string GetSignalRawValueSignature(PropertyDisplayModel targetPropertyView)
    {
        if (IsScriptTarget)
        {
            return FormatDiagnosticValue(_scriptValue);
        }

        return FormatDiagnosticValue(targetPropertyView.Property?.Value);
    }

    private void SchedulePendingRefresh(TimeSpan dueIn)
    {
        if (dueIn < TimeSpan.Zero)
        {
            dueIn = TimeSpan.Zero;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _pendingRefreshTimer ??= new DispatcherTimer();
            _pendingRefreshTimer.Stop();
            _pendingRefreshTimer.Interval = dueIn;
            _pendingRefreshTimer.Tick -= OnPendingRefreshTimerTick;
            _pendingRefreshTimer.Tick += OnPendingRefreshTimerTick;
            _pendingRefreshTimer.Start();
        });
    }

    private void OnPendingRefreshTimerTick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer)
        {
            timer.Stop();
            timer.Tick -= OnPendingRefreshTimerTick;
        }

        _hasPendingTargetRefresh = false;
        TriggerTargetRefresh();
    }

    private void TriggerTargetRefresh()
    {
        var queuedAtUtc = DateTimeOffset.UtcNow;
        var (diagnosticPath, diagnosticModule) = GetSignalVisualDiagnosticIdentity();
        var queuedPath = !string.IsNullOrWhiteSpace(_lastSignalDiagnosticsPath)
            ? _lastSignalDiagnosticsPath
            : diagnosticPath;
        var queuedModule = !string.IsNullOrWhiteSpace(_lastSignalDiagnosticsModule)
            ? _lastSignalDiagnosticsModule
            : diagnosticModule;

        lock (_targetRefreshSync)
        {
            if (Interlocked.Exchange(ref _targetRefreshQueued, 1) == 1)
            {
                var hadPendingQueuedTargetRefresh = _hasPendingQueuedTargetRefresh;
                _pendingQueuedTargetRefresh = new PendingTargetRefresh(
                    QueuedAtUtc: queuedAtUtc,
                    Path: queuedPath,
                    Module: queuedModule);
                _hasPendingQueuedTargetRefresh = true;
                if (!hadPendingQueuedTargetRefresh)
                {
                    UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetRefreshFollowUpDelta: 1);
                }

                RecordSignalPipelineStage(stage: "TargetRefreshCoalesced", path: queuedPath, module: queuedModule);
                return;
            }

            _targetRefreshQueuedUtc = queuedAtUtc;
            _targetRefreshQueuedPath = queuedPath;
            _targetRefreshQueuedModule = queuedModule;
        }

        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetRefreshQueuedDelta: 1);
        RecordTargetRefreshQueued(queuedAtUtc, queuedPath, queuedModule);
        Dispatcher.UIThread.Post(ExecuteTargetRefresh, DispatcherPriority.Background);
    }

    private void ExecuteTargetRefresh()
    {
        var queuedAtUtc = DateTimeOffset.MinValue;
        var queuedPath = string.Empty;
        var queuedModule = string.Empty;
        lock (_targetRefreshSync)
        {
            queuedAtUtc = _targetRefreshQueuedUtc;
            queuedPath = _targetRefreshQueuedPath;
            queuedModule = _targetRefreshQueuedModule;
            _targetRefreshQueuedUtc = DateTimeOffset.MinValue;
            _targetRefreshQueuedPath = string.Empty;
            _targetRefreshQueuedModule = string.Empty;
        }

        if (IsScriptTarget && !IsLegacyScriptBlockedForCurrentPath())
        {
            RefreshScriptValue();
        }

        RecordTargetRefreshExecuted(queuedAtUtc, queuedPath, queuedModule);
        RefreshTargetBindings();
        _lastTargetRefreshUtc = DateTimeOffset.UtcNow;

        PendingTargetRefresh pendingTargetRefresh;
        lock (_targetRefreshSync)
        {
            if (!_hasPendingQueuedTargetRefresh)
            {
                Interlocked.Exchange(ref _targetRefreshQueued, 0);
                UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetRefreshQueuedDelta: -1);
                return;
            }

            pendingTargetRefresh = _pendingQueuedTargetRefresh;
            _pendingQueuedTargetRefresh = default;
            _hasPendingQueuedTargetRefresh = false;
            _targetRefreshQueuedUtc = pendingTargetRefresh.QueuedAtUtc;
            _targetRefreshQueuedPath = pendingTargetRefresh.Path;
            _targetRefreshQueuedModule = pendingTargetRefresh.Module;
        }

        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(targetRefreshFollowUpDelta: -1);
        RecordTargetRefreshQueued(pendingTargetRefresh.QueuedAtUtc, pendingTargetRefresh.Path, pendingTargetRefresh.Module);
        Dispatcher.UIThread.Post(ExecuteTargetRefresh, DispatcherPriority.Background);
    }

    private void CancelPendingTargetRefresh()
    {
        _hasPendingTargetRefresh = false;
        Dispatcher.UIThread.Post(() => _pendingRefreshTimer?.Stop());
    }

    private void RecordSignalPipelineReceive(string? path, ItemModel? item)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? TargetPathHelper.NormalizeConfiguredTargetPath(TargetPath)
            : TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var module = GetSignalPipelineModuleName(path: normalizedPath, item: item);
        _lastSignalReceiveUtc = DateTimeOffset.UtcNow;
        _lastSignalDiagnosticsPath = normalizedPath;
        _lastSignalDiagnosticsModule = module;

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: "SignalReceive",
            path: normalizedPath,
            module: module);
    }

    private void RecordSignalRegistryEvent(string stage, string? path, ItemModel? item)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? TargetPathHelper.NormalizeConfiguredTargetPath(TargetPath)
            : TargetPathHelper.NormalizeConfiguredTargetPath(path);
        var module = GetSignalPipelineModuleName(path: normalizedPath, item: item);
        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: stage,
            path: normalizedPath,
            module: module);
    }

    private void RecordSignalIrrelevantEvent(string? path, ItemModel? item)
    {
        if (!ShouldRecordSignalIrrelevantEvent(path))
        {
            return;
        }

        RecordSignalRegistryEvent(stage: "SignalRegistryEventIrrelevant", path: path, item: item);
    }

    private bool ShouldRecordSignalIrrelevantEvent(string? changedPath)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return false;
        }

        var normalizedChangedPath = TargetPathHelper.NormalizeConfiguredTargetPath(changedPath);
        if (string.IsNullOrWhiteSpace(normalizedChangedPath))
        {
            return false;
        }

        var normalizedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(TargetPath);
        if (string.IsNullOrWhiteSpace(normalizedTargetPath))
        {
            return false;
        }

        if (PathsShareSignalScope(normalizedChangedPath, normalizedTargetPath))
        {
            return true;
        }

        return TryGetSignalTargetScopePath(normalizedTargetPath, out var targetScopePath)
            && PathsShareSignalScope(normalizedChangedPath, targetScopePath);
    }

    private static bool PathsShareSignalScope(string changedPath, string targetScopePath)
        => TargetPathHelper.PathsEqual(changedPath, targetScopePath)
           || TargetPathHelper.IsDescendantPath(changedPath, targetScopePath)
           || TargetPathHelper.IsDescendantPath(targetScopePath, changedPath);

    private static bool TryGetSignalTargetScopePath(string targetPath, out string targetScopePath)
    {
        targetScopePath = string.Empty;

        var segments = TargetPathHelper.SplitPathSegments(targetPath);
        if (segments.Count <= 1)
        {
            return false;
        }

        targetScopePath = string.Join('.', segments.Take(segments.Count - 1));
        return !string.IsNullOrWhiteSpace(targetScopePath);
    }

    private void RecordSignalPipelineStage(string stage, string? path, string? module)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module);
    }

    private void RecordTargetRefreshQueued(DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        RecordTargetRefreshQueued(stage: "TargetRefreshQueued", queuedAtUtc, path, module);
    }

    private void RecordTargetValueRefreshQueued(DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        RecordTargetRefreshQueued(stage: "TargetValueRefreshQueued", queuedAtUtc, path, module);
    }

    private void RecordTargetRefreshQueued(string stage, DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        _lastTargetRefreshQueuedUtc = queuedAtUtc;
        if (_lastSignalReceiveUtc != DateTimeOffset.MinValue && queuedAtUtc >= _lastSignalReceiveUtc)
        {
            UiResponsivenessDiagnostics.RecordSignalPipelineDelay(
                stage: stage,
                delay: queuedAtUtc - _lastSignalReceiveUtc,
                path: path,
                module: module,
                queuedAtUtc: _lastSignalReceiveUtc,
                executedAtUtc: queuedAtUtc);
            return;
        }

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module,
            queuedAtUtc: queuedAtUtc);
    }

    private void RecordTargetRefreshExecuted(DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        RecordTargetRefreshExecuted(stage: "TargetRefreshExecuted", queuedAtUtc, path, module);
    }

    private void RecordTargetValueRefreshExecuted(DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        RecordTargetRefreshExecuted(stage: "TargetValueRefreshExecuted", queuedAtUtc, path, module);
    }

    private void RecordTargetRefreshExecuted(string stage, DateTimeOffset queuedAtUtc, string? path, string? module)
    {
        if (!ShouldTrackSignalPipelineDiagnostics())
        {
            return;
        }

        var executedAtUtc = DateTimeOffset.UtcNow;
        if (queuedAtUtc != DateTimeOffset.MinValue && executedAtUtc >= queuedAtUtc)
        {
            UiResponsivenessDiagnostics.RecordSignalPipelineDelay(
                stage: stage,
                delay: executedAtUtc - queuedAtUtc,
                path: path,
                module: module,
                queuedAtUtc: queuedAtUtc,
                executedAtUtc: executedAtUtc);
            return;
        }

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module,
            executedAtUtc: executedAtUtc);
    }

    private bool ShouldTrackSignalPipelineDiagnostics()
    {
        return UiResponsivenessDiagnostics.IsEnabled
            && Kind == ControlKind.Signal;
    }

    private static string GetSignalPipelineModuleName(string? path, ItemModel? item)
    {
        if (!string.IsNullOrWhiteSpace(item?.Name))
        {
            return item.Name.Trim();
        }

        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        var lastSeparator = normalizedPath.LastIndexOf('.');
        return lastSeparator >= 0 && lastSeparator < normalizedPath.Length - 1
            ? normalizedPath[(lastSeparator + 1)..]
            : normalizedPath;
    }

    private readonly record struct PendingTargetValueRefresh(
        string ChangedPath,
        string MatchedTargetPath,
        ItemModel Item,
        DataChangeKind ChangeKind,
        string? ParameterName,
        string TargetPropertyPath);

    private readonly record struct PendingTargetRefresh(
        DateTimeOffset QueuedAtUtc,
        string Path,
        string Module);

    private void UpdateScriptTimer()
    {
        if (!IsScriptTarget || RefreshRateMs <= 0 || IsLegacyScriptBlockedForCurrentPath())
        {
            if (_scriptTimer is not null)
            {
                var timer = _scriptTimer;
                _scriptTimer = null;
                timer.Stop();
                timer.Tick -= OnScriptTimerTick;
            }

            return;
        }

        if (_scriptTimer is null)
        {
            _scriptTimer = new DispatcherTimer();
            _scriptTimer.Tick += OnScriptTimerTick;
        }

        _scriptTimer.Interval = TimeSpan.FromMilliseconds(RefreshRateMs);
        _scriptTimer.Start();

        RefreshScriptValue();
    }

    private void OnScriptTimerTick(object? sender, EventArgs e)
    {
        RefreshScriptValue();
    }

    private void RefreshScriptValue()
    {
        var scriptPath = ResolveConfiguredScriptPath();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return;
        }

        if (!HornetStudio.Host.Python.Legacy.PythonScriptHost.IsLegacyScriptCompatible(scriptPath, out _))
        {
            MarkLegacyScriptBlocked(scriptPath);
            UpdateScriptTimer();
            return;
        }

        try
        {
            var result = HornetStudio.Host.Python.Legacy.PythonScriptHost.ExecuteSignalScript(scriptPath);
            _scriptValue = result;
            PublishScriptValueToRegistry(result);
            RefreshTargetBindings();
        }
        catch
        {
            // Fehler im Skript werden im Host geloggt; UI bleibt stabil.
        }
    }

    private void ClearBlockedLegacyScript()
    {
        _blockedLegacyScriptPath = string.Empty;
        _blockedLegacyScriptWriteTimeUtc = default;
    }

    private void MarkLegacyScriptBlocked(string scriptPath)
    {
        _blockedLegacyScriptPath = scriptPath;
        try
        {
            _blockedLegacyScriptWriteTimeUtc = File.Exists(scriptPath)
                ? File.GetLastWriteTimeUtc(scriptPath)
                : default;
        }
        catch
        {
            _blockedLegacyScriptWriteTimeUtc = default;
        }
    }

    private bool IsLegacyScriptBlockedForCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(_blockedLegacyScriptPath))
        {
            return false;
        }

        var currentScriptPath = ResolveConfiguredScriptPath();
        if (!string.Equals(currentScriptPath, _blockedLegacyScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearBlockedLegacyScript();
            return false;
        }

        try
        {
            var currentWriteTimeUtc = File.Exists(currentScriptPath)
                ? File.GetLastWriteTimeUtc(currentScriptPath)
                : default;
            if (currentWriteTimeUtc != _blockedLegacyScriptWriteTimeUtc)
            {
                ClearBlockedLegacyScript();
                return false;
            }
        }
        catch
        {
            return true;
        }

        return true;
    }

    private void PublishScriptValueToRegistry(object? value)
    {
        var runtimePath = GetScriptRuntimePath();
        if (string.IsNullOrWhiteSpace(runtimePath))
        {
            return;
        }

        var normalizedPath = TargetPathHelper.NormalizeConfiguredTargetPath(runtimePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var segments = TargetPathHelper.SplitPathSegments(normalizedPath);
        if (segments.Count == 0)
        {
            return;
        }

        var nameSegment = segments[^1];
        var parentPath = segments.Count > 1
            ? string.Join('.', segments.Take(segments.Count - 1))
            : string.Empty;

        // Einzelnes Script-Signal als eigenstaendigen Snapshot publizieren, so dass
        // es unter studio.<Folder>.Applications.Python.<Name> im Target-Tree und RealtimeChart
        // sichtbar und direkt aufloesbar ist.
        ItemModel item;
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            item = new ItemModel(nameSegment, value);
        }
        else
        {
            item = new ItemModel(nameSegment, value, parentPath);
        }

        item.Properties["path"].Value = normalizedPath;

        HostRegistries.Data.UpsertSnapshot(normalizedPath, item, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: false);
    }

    private string? GetScriptRuntimePath()
    {
        if (!IsScriptTarget)
        {
            return null;
        }

        var folderSegment = TargetPathHelper.NormalizePathSegment(FolderName, "page");

        var nameSource = !string.IsNullOrWhiteSpace(Name)
            ? Name
            : (!string.IsNullOrWhiteSpace(Title) ? Title : Id);
        var nameSegment = TargetPathHelper.NormalizePathSegment(nameSource, Id);

        return $"studio.{folderSegment}.applications.python.{nameSegment}";
    }

    private void RefreshPathRecursive()
    {
        Path = BuildPath();
        EnsureCircleDisplayRuntimeSignals();

        foreach (var child in Items)
        {
            child.FolderName = FolderName;
            child.SetLayoutFilePath(FolderLayoutPath);
            child.ParentItem = this;
            child.ParentWidgetList = IsWidgetList ? this : null;
            child.ApplyActiveView(ActiveViewId);
            child.RefreshPathRecursive();
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var child in Items)
        {
            child.FolderName = FolderName;
            child.SetLayoutFilePath(FolderLayoutPath);
            child.ParentItem = this;
            child.ParentWidgetList = IsWidgetList ? this : null;
            child.ApplyActiveView(ActiveViewId);
        }

        if (IsWidgetList)
        {
            SyncChildWidths();
        }

        if (IsItem)
        {
            RaisePropertyChanged(nameof(HasFooterSubItems));
            RaisePropertyChanged(nameof(FooterSubItemRowCount));
            RaisePropertyChanged(nameof(FooterSubItemWidth));
            RaisePropertyChanged(nameof(FooterSubItemHeight));
            RaisePropertyChanged(nameof(FooterPanelHeight));
            RaisePropertyChanged(nameof(ShowItemFooterPanel));
            RaisePropertyChanged(nameof(ItemBodyHeight));
            RaisePropertyChanged(nameof(AvailableBodyHeight));
            RaisePropertyChanged(nameof(ItemChoiceButtonHeight));
            RaisePropertyChanged(nameof(ItemValueFontSize));
            RaisePropertyChanged(nameof(ItemUnitFontSize));
        }

        if (UsesTableLayout)
        {
            UpdateTableCellContentFromChildren();
        }
    }

    private void SyncTableChildHeights()
    {
        if (!UsesTableLayout || TableRows <= 0)
        {
            return;
        }

        var baseCellHeight = Height / TableRows;
        foreach (var child in Items)
        {
            if (!child.IsTableChildControl)
            {
                continue;
            }

            var span = System.Math.Max(1, child.TableCellRowSpan);
            child.Height = System.Math.Max(baseCellHeight * span, child.MinHeight);
        }
    }

    private bool TryResolveTargetItem(string targetPath, out ItemModel? item)
    {
        foreach (var candidatePath in TargetPathHelper.EnumerateResolutionCandidates(targetPath, FolderName))
        {
            if (HostRegistries.Data.TryResolve(candidatePath, out item) && item is not null)
            {
                return true;
            }
        }

        foreach (var candidatePath in TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(targetPath))
        {
            if (HostRegistries.Data.TryResolve(candidatePath, out item) && item is not null)
            {
                return true;
            }
        }

        item = null;
        return false;
    }

    private static string GetPersistedTargetPath(string? resolvedPath, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(resolvedPath) && TargetPathHelper.IsRuntimeItemServerPath(resolvedPath))
        {
            return TargetPathHelper.ToFlatItemServerPath(resolvedPath);
        }

        return resolvedPath ?? TargetPathHelper.NormalizeConfiguredTargetPath(configuredPath);
    }

    private static bool TryResolveRelativeChild(ItemModel rootItem, string relativePath, out ItemModel? item)
    {
        var current = rootItem;
        foreach (var segment in TargetPathHelper.SplitPathSegments(relativePath))
        {
            var matchingChildName = current.GetDictionary().Keys
                .FirstOrDefault(key => string.Equals(key, segment, StringComparison.OrdinalIgnoreCase));
            if (matchingChildName is null)
            {
                item = null;
                return false;
            }

            current = current.GetDictionary()[matchingChildName];
        }

        item = current;
        return true;
    }

    private void EnsureCircleDisplayRuntimeSignals()
    {
        var runtimeBasePath = GetDisplayRuntimeBasePath();
        if (!IsCircleDisplay || string.IsNullOrWhiteSpace(runtimeBasePath))
        {
            return;
        }

        var segments = TargetPathHelper.SplitPathSegments(runtimeBasePath);
        if (segments.Count == 0)
        {
            return;
        }

        ItemModel snapshot;
        if (HostRegistries.Data.TryGet(runtimeBasePath, out var existing) && existing is not null)
        {
            snapshot = existing.Clone();
        }
        else
        {
            var nameSegment = segments[^1];
            var parentPath = segments.Count > 1
                ? string.Join('.', segments.Take(segments.Count - 1))
                : string.Empty;

            snapshot = string.IsNullOrWhiteSpace(parentPath)
                ? new ItemModel(nameSegment)
                : new ItemModel(nameSegment, null, parentPath);
        }

        snapshot.Properties["path"].Value = runtimeBasePath;
        snapshot.Properties["kind"].Value = "DisplayRuntime";
        snapshot.Properties["text"].Value = string.IsNullOrWhiteSpace(Name) ? Title : Name;
        snapshot[CircleDisplaySignalColorItemName].Value = string.IsNullOrWhiteSpace(SignalColor)
            ? CircleDisplayDefaultSignalColor
            : SignalColor;
        snapshot[CircleDisplaySignalColorItemName].Properties["text"].Value = CircleDisplaySignalColorText;
        snapshot[CircleDisplaySignalRunItemName].Value = SignalRun;
        snapshot[CircleDisplaySignalRunItemName].Properties["text"].Value = CircleDisplaySignalRunText;
        snapshot[CircleDisplayProgressBarItemName].Value = ProgressBar;
        snapshot[CircleDisplayProgressBarItemName].Properties["text"].Value = CircleDisplayProgressBarText;
        snapshot[CircleDisplayProgressStateItemName].Value = System.Math.Clamp(ProgressState, 0d, 100d);
        snapshot[CircleDisplayProgressStateItemName].Properties["text"].Value = CircleDisplayProgressStateText;
        snapshot[CircleDisplayProgressBarColorItemName].Value = string.IsNullOrWhiteSpace(ProgressBarColor)
            ? CircleDisplayDefaultProgressBarColor
            : ProgressBarColor;
        snapshot[CircleDisplayProgressBarColorItemName].Properties["text"].Value = CircleDisplayProgressBarColorText;

        HostRegistries.Data.UpsertSnapshot(runtimeBasePath, snapshot, DataRegistryItemMetadata.WidgetInternal(), pruneMissingMembers: false);
        UpsertCircleDisplayRuntimeValue(
            runtimeItemName: CircleDisplaySignalColorItemName,
            value: string.IsNullOrWhiteSpace(SignalColor) ? CircleDisplayDefaultSignalColor : SignalColor,
            title: "Circle display signal color");
        UpsertCircleDisplayRuntimeValue(
            runtimeItemName: CircleDisplaySignalRunItemName,
            value: SignalRun,
            title: "Circle display signal state");
        UpsertCircleDisplayRuntimeValue(
            runtimeItemName: CircleDisplayProgressBarItemName,
            value: ProgressBar,
            title: "Circle display progress visibility");
        UpsertCircleDisplayRuntimeValue(
            runtimeItemName: CircleDisplayProgressStateItemName,
            value: System.Math.Clamp(ProgressState, 0d, 100d),
            title: "Circle display progress state");
        UpsertCircleDisplayRuntimeValue(
            runtimeItemName: CircleDisplayProgressBarColorItemName,
            value: string.IsNullOrWhiteSpace(ProgressBarColor) ? CircleDisplayDefaultProgressBarColor : ProgressBarColor,
            title: "Circle display progress color");
    }

    private void UpsertCircleDisplayRuntimeValue(string runtimeItemName, object? value, string title)
    {
        var runtimePath = GetDisplayRuntimePath(runtimeItemName);
        if (string.IsNullOrWhiteSpace(runtimePath))
        {
            return;
        }

        var item = new ItemModel(runtimeItemName, value, GetDisplayRuntimeBasePath());
        item.Properties["kind"].Value = "DisplayRuntime";
        item.Properties["text"].Value = title;
        item.Properties["title"].Value = title;
        HostRegistries.Data.UpsertSnapshot(runtimePath, item, DataRegistryItemMetadata.WidgetInternal(), pruneMissingMembers: true);
    }

    private static bool IsInsideCircle(double normalizedRow, double normalizedColumn)
        => (normalizedRow * normalizedRow) + (normalizedColumn * normalizedColumn) <= 0.92d;

    private static string NormalizeLogTargetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Logs.Host";
        }

        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Logs.Host";
        }

        return normalized.Contains('.', StringComparison.Ordinal)
            ? normalized
            : $"Logs.{normalized}";
    }

    private static string NormalizeAlignment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "left" => "Left",
            "right" => "Right",
            "center" => "Center",
            _ => fallback
        };
    }


            private void ApplyVisualRuleOverrides()
            {
                _visualRuleButtonBackColorOverride = null;
                var rules = VisualRuleCodec.ParseDefinitions(VisualRules);
                if (rules.Count == 0 || !VisualRuleCodec.SupportsVisualRules(this))
                {
                    StopVisualRuleBlinkTimer();
                    _visualRuleBlinkPhaseVisible = true;
                    return;
                }

                var bodyMatch = ResolveVisualRuleMatch(rules, VisualRuleProperty.BodyBackColor);
                var buttonMatch = ResolveVisualRuleMatch(rules, VisualRuleProperty.ButtonBackColor);
                var displayMatch = ResolveVisualRuleMatch(rules, VisualRuleProperty.DisplayBackColor);

                if (IsItem && bodyMatch.HasValue)
                {
                    EffectiveBodyBackground = bodyMatch.Value!;
                }

                if (IsButton && buttonMatch.HasValue)
                {
                    _visualRuleButtonBackColorOverride = buttonMatch.Value!;
                }

                if (IsCircleDisplay && displayMatch.HasValue)
                {
                    EffectiveDisplayBackColor = displayMatch.Value!;
                }

                var hasActiveBlink = bodyMatch.IsBlinking || buttonMatch.IsBlinking || displayMatch.IsBlinking;
                if (hasActiveBlink)
                {
                    StartVisualRuleBlinkTimer();
                }
                else
                {
                    StopVisualRuleBlinkTimer();
                    _visualRuleBlinkPhaseVisible = true;
                }
            }

            private VisualRuleMatch ResolveVisualRuleMatch(IEnumerable<VisualRule> rules, VisualRuleProperty property)
            {
                var match = VisualRuleMatch.None;
                foreach (var rule in rules)
                {
                    if (rule.Property != property || !VisualRuleCodec.IsSupportedProperty(this, property))
                    {
                        continue;
                    }

                    if (!TryGetVisualRuleState(rule, out var isActive))
                    {
                        continue;
                    }

                    if (isActive)
                    {
                        if (rule.Effect == VisualRuleEffect.Blink && !_visualRuleBlinkPhaseVisible)
                        {
                            match = string.IsNullOrWhiteSpace(rule.InactiveValue)
                                ? new VisualRuleMatch(null, true)
                                : new VisualRuleMatch(rule.InactiveValue, true);
                            continue;
                        }

                        match = string.IsNullOrWhiteSpace(rule.ActiveValue)
                            ? VisualRuleMatch.None
                            : new VisualRuleMatch(rule.ActiveValue, rule.Effect == VisualRuleEffect.Blink);
                        continue;
                    }

                    match = string.IsNullOrWhiteSpace(rule.InactiveValue)
                        ? VisualRuleMatch.None
                        : new VisualRuleMatch(rule.InactiveValue, false);
                }

                return match;
            }

            private bool TryGetVisualRuleState(VisualRule rule, out bool isActive)
            {
                isActive = false;
                if (rule.SourceKind != VisualRuleSourceKind.MonitorRule || string.IsNullOrWhiteSpace(rule.SourcePath))
                {
                    return false;
                }

                if (!TryResolveTargetItem(rule.SourcePath, out var sourceItem) || sourceItem is null)
                {
                    return false;
                }

                switch (sourceItem.Value)
                {
                    case bool boolValue:
                        isActive = boolValue;
                        return true;
                    case string stringValue when bool.TryParse(stringValue, out var parsedBool):
                        isActive = parsedBool;
                        return true;
                    case sbyte or byte or short or ushort or int or uint or long or ulong:
                        isActive = System.Convert.ToInt64(sourceItem.Value, CultureInfo.InvariantCulture) != 0;
                        return true;
                    default:
                        return false;
                }
            }

            private bool HasMatchingVisualRuleSource(string? changedPath)
            {
                if (string.IsNullOrWhiteSpace(changedPath))
                {
                    return false;
                }

                foreach (var rule in VisualRuleCodec.ParseDefinitions(VisualRules))
                {
                    if (rule.SourceKind != VisualRuleSourceKind.MonitorRule || string.IsNullOrWhiteSpace(rule.SourcePath))
                    {
                        continue;
                    }

                    foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(rule.SourcePath, FolderName))
                    {
                        if (TargetPathHelper.PathsEqual(changedPath, candidate)
                            || TargetPathHelper.IsDescendantPath(changedPath, candidate)
                            || TargetPathHelper.IsDescendantPath(candidate, changedPath))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private void StartVisualRuleBlinkTimer()
            {
                if (_visualRuleBlinkTimer is null)
                {
                    _visualRuleBlinkTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    _visualRuleBlinkTimer.Tick += OnVisualRuleBlinkTimerTick;
                }

                if (!_visualRuleBlinkTimer.IsEnabled)
                {
                    _visualRuleBlinkTimer.Start();
                }
            }

            private void StopVisualRuleBlinkTimer()
            {
                if (_visualRuleBlinkTimer is null)
                {
                    return;
                }

                _visualRuleBlinkTimer.Stop();
            }

            private void OnVisualRuleBlinkTimerTick(object? sender, EventArgs e)
            {
                _visualRuleBlinkPhaseVisible = !_visualRuleBlinkPhaseVisible;
                ApplyTheme(_isDarkThemeApplied);
            }

            private readonly record struct VisualRuleMatch(string? Value, bool IsBlinking)
            {
                public static VisualRuleMatch None => new(null, false);

                public bool HasValue => !string.IsNullOrWhiteSpace(Value);
            }
    private static HorizontalAlignment ParseHorizontalAlignment(string? value, HorizontalAlignment fallback)
    {
        return NormalizeAlignment(value, fallback.ToString()) switch
        {
            "Left" => HorizontalAlignment.Left,
            "Right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
    }

    private PropertyDisplayModel BuildTargetPropertyView()
        => BuildTargetParameterView(null, Title);

    private PropertyDisplayModel BuildTargetParameterView(string? labelOverride, string fallbackText)
    {
        var parameter = ResolveTargetProperty();
        var isValueParameter = parameter is not null && string.Equals(parameter.Name, "read", StringComparison.OrdinalIgnoreCase);
        var label = labelOverride ?? (isValueParameter && Target?.Properties.Has("text") == true
            ? Target.Properties["text"].Value?.ToString() ?? string.Empty
            : (parameter?.Name ?? Header));
        var format = !string.IsNullOrWhiteSpace(TargetPropertyFormat)
            ? TargetPropertyFormat
            : isValueParameter && Target?.Properties.Has("format") == true
                ? Target.Properties["format"].Value?.ToString() ?? string.Empty
                : string.Empty;
        var unitText = !string.IsNullOrWhiteSpace(Unit)
            ? Unit
            : isValueParameter
                ? GetTargetUnitText(Target)
                : string.Empty;
        // Wenn ein Script-Wert aktiv ist, soll dieser auch durch die bestehende
        // Formatlogik (z.B. numeric:0.000) laufen. Dazu uebergeben wir den
        // Script-Wert als Fallback-Text und unterdruecken den Parameterwert,
        // damit PropertyDisplayModel den Fallback formatiert.
        if (IsScriptTarget && _scriptValue is not null)
        {
            var scriptText = _scriptValue is IFormattable formattable
                ? (formattable.ToString(null, CultureInfo.InvariantCulture) ?? _scriptValue.ToString() ?? string.Empty)
                : (_scriptValue.ToString() ?? string.Empty);

            return new PropertyDisplayModel(null, label, format, unitText, scriptText);
        }

        return new PropertyDisplayModel(parameter, label, format, unitText, fallbackText);
    }

    private static bool HasPresentationChanged(PropertyDisplayModel presentation, ref string previousSignature)
    {
        var signature = BuildPresentationSignature(presentation);
        if (string.Equals(signature, previousSignature, StringComparison.Ordinal))
        {
            return false;
        }

        previousSignature = signature;
        return true;
    }

    private static string BuildPresentationSignature(PropertyDisplayModel presentation)
    {
        var property = presentation.Property;
        return string.Join(
            "|",
            property?.Path ?? string.Empty,
            property?.Name ?? string.Empty,
            FormatDiagnosticValue(property?.Value),
            presentation.Label,
            presentation.Format,
            presentation.UnitText,
            presentation.FallbackText,
            presentation.ValueText,
            presentation.Definition.Kind.ToString(),
            presentation.Definition.BitCount.ToString(CultureInfo.InvariantCulture),
            presentation.Definition.UseSingleToggle ? "toggle" : string.Empty,
            string.Join(",", presentation.Definition.Options));
    }

    private static string GetTargetUnitText(ItemModel? item)
    {
        return item?.Properties.Has("unit") == true
            ? item.Properties["unit"].Value?.ToString() ?? string.Empty
            : string.Empty;
    }

    private string GetSuggestedNameFromTargetPath(string? targetPath)
    {
        var normalizedPath = NormalizeTargetRelativePath(targetPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        var segments = TargetPathHelper.SplitPathSegments(normalizedPath);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var ignoredTailSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "read",
            "Request",
            "Status",
            "Command",
            "Set",
            "Read",
            "Write"
        };

        for (var index = segments.Count - 1; index >= 0; index--)
        {
            var candidate = segments[index];
            if (string.IsNullOrWhiteSpace(candidate) || ignoredTailSegments.Contains(candidate))
            {
                continue;
            }

            return candidate.Replace('.', '_');
        }

        return segments[^1].Replace('.', '_');
    }

    private string NormalizeTargetRelativePath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        targetPath = TargetPathHelper.ToPersistedLayoutTargetPath(targetPath, FolderName);

        var segments = TargetPathHelper.SplitPathSegments(targetPath);

        if (segments.Count == 0)
        {
            return string.Empty;
        }

        if (segments.Count > 3
            && string.Equals(segments[0], "Runtime", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "UdlClient", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('.', segments.Skip(3));
        }

        if (segments.Count > 3
            && string.Equals(segments[0], "UdlProject", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('.', segments.Skip(3));
        }

        if (segments.Count > 3
            && string.Equals(segments[0], "UdlBook", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('.', segments.Skip(3));
        }

        return string.Join('.', segments);
    }

    private static string NormalizeBodyCaptionPosition(string? value)
    {
        return string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)
            ? "Left"
            : "Top";
    }

    private bool IsAutoGeneratedControlName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var normalizedName = name.Trim();
        var baseName = Kind switch
        {
            ControlKind.Button => "Button",
            ControlKind.WidgetList => "WidgetList",
            ControlKind.LogControl => "LogControl",
            ControlKind.ChartControl => "ChartControl",
            ControlKind.ItemClient => "ItemClient",
            ControlKind.CsvLoggerControl => "CsvLoggerControl",
            ControlKind.SqlLoggerControl => "SqlLoggerControl",
            ControlKind.CameraControl => "CameraControl",
            ControlKind.ApplicationExplorer => "ApplicationExplorer",
            ControlKind.Functions => "Functions",
            ControlKind.DialogWidget => "DialogWidget",
            ControlKind.ItemModel or ControlKind.Signal => "Signal",
            _ => "Signal"
        };

        if (string.Equals(normalizedName, baseName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalizedName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = normalizedName[baseName.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private bool IsAutoGeneratedControlCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return true;
        }

        return IsAutoGeneratedControlName(caption);
    }

    private ItemProperty? ResolveTargetProperty()
    {
        var resolvedTarget = GetResolvedTargetForDisplay();
        if (resolvedTarget is null)
        {
            if (IsSignal)
            {
                RecordSignalRegistryEvent(stage: "SignalTargetUnresolved", path: TargetPath, item: Target);
            }

            return null;
        }

        if (TryResolveSignalLiveValueProperty(out var liveValueProperty))
        {
            return liveValueProperty;
        }

        if (!string.IsNullOrWhiteSpace(TargetPropertyPath)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(TargetPropertyPath)
            && resolvedTarget.Properties.Has(TargetPropertyPath))
        {
            if (IsSignal)
            {
                RecordSignalRegistryEvent(stage: "SignalEffectiveTargetPropertyUsed", path: resolvedTarget.Path, item: resolvedTarget);
            }

            return resolvedTarget.Properties[TargetPropertyPath];
        }

        var hasSignalValueReference = IsSignal && TryGetSignalValueReference(out _, out _);

        if (TryResolveSignalValueReferenceProperty(out var valueRefProperty, out var runtimeItem, out var valueRefPath, out _))
        {
            RecordSignalRegistryEvent(stage: "SignalValueRefResolved", path: valueRefPath, item: runtimeItem);
            return valueRefProperty;
        }

        if (IsSignal && resolvedTarget.Properties.Has("read"))
        {
            RecordSignalRegistryEvent(stage: GetSignalReadFallbackStage(hasSignalValueReference), path: resolvedTarget.Path, item: resolvedTarget);
            return resolvedTarget.Properties["read"];
        }

        return null;
    }

    private bool TryResolveSignalLiveValueProperty(out ItemProperty? property)
    {
        property = null;
        if (!TryGetResolvedSignalLiveValueBinding(out var binding)
            || !SignalLiveValueStore.TryGetSnapshot(binding, out var snapshot))
        {
            return false;
        }

        property = new ItemProperty(
            binding.RuntimeParameterName,
            snapshot.Value,
            $"{binding.RuntimeItemPath}.{binding.RuntimeParameterName}");
        return true;
    }

    private string GetSignalReadFallbackStage(bool hasSignalValueReference)
    {
        if (!string.IsNullOrWhiteSpace(TargetPropertyPath)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(TargetPropertyPath)
            && !string.Equals(TargetPropertyPath, "read", StringComparison.OrdinalIgnoreCase))
        {
            return "SignalFallbackSelectedPropertyMissing";
        }

        return hasSignalValueReference
            ? "SignalFallbackInvalidValueReference"
            : "SignalFallbackPublicReadUsed";
    }

    private bool CanUseResolvedTargetProperty()
    {
        var resolvedTarget = GetResolvedTargetForDisplay();
        if (resolvedTarget is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(TargetPropertyPath)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(TargetPropertyPath)
            && resolvedTarget.Properties.Has(TargetPropertyPath))
        {
            return true;
        }

        return resolvedTarget.Properties.Has("read");
    }

    private ItemModel? GetResolvedTargetForDisplay()
    {
        if (Target is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(Target.Path)
            || !TryResolveTargetItem(Target.Path, out var resolvedTarget)
            || resolvedTarget is null)
        {
            return Target;
        }

        return resolvedTarget;
    }

    private bool TryResolveSignalValueReferenceProperty(
        out ItemProperty? property,
        out ItemModel? runtimeItem,
        out string valueRefPath,
        out string valueRefParameter)
    {
        property = null;
        runtimeItem = null;
        valueRefPath = string.Empty;
        valueRefParameter = string.Empty;

        if (!TryGetSignalValueReference(out valueRefPath, out valueRefParameter))
        {
            return false;
        }

        if (TryGetResolvedSignalLiveValueBinding(out var binding)
            && TargetPathHelper.PathsEqual(binding.RuntimeItemPath, valueRefPath)
            && string.Equals(binding.RuntimeParameterName, valueRefParameter, StringComparison.OrdinalIgnoreCase)
            && SignalLiveValueStore.TryGetSnapshot(binding, out var snapshot))
        {
            property = new ItemProperty(
                valueRefParameter,
                snapshot.Value,
                $"{valueRefPath}.{valueRefParameter}");
            return true;
        }

        runtimeItem = _cachedSignalValueRefRuntimeItem;
        if (runtimeItem is null
            || !TargetPathHelper.PathsEqual(runtimeItem.Path, valueRefPath)
            || !runtimeItem.Properties.Has(valueRefParameter))
        {
            if (!TryResolveTargetItem(valueRefPath, out runtimeItem)
                || runtimeItem is null
                || !runtimeItem.Properties.Has(valueRefParameter))
            {
                _cachedSignalValueRefRuntimeItem = null;
                return false;
            }

            _cachedSignalValueRefRuntimeItem = runtimeItem;
        }

        property = runtimeItem.Properties[valueRefParameter];
        return true;
    }

    private bool TryGetSignalValueReference(out string valueRefPath, out string valueRefParameter)
    {
        valueRefPath = _cachedSignalValueRefPath;
        valueRefParameter = _cachedSignalValueRefParameter;
        if (!string.IsNullOrWhiteSpace(valueRefPath) && !string.IsNullOrWhiteSpace(valueRefParameter))
        {
            return true;
        }

        RefreshSignalValueReferenceCache();
        valueRefPath = _cachedSignalValueRefPath;
        valueRefParameter = _cachedSignalValueRefParameter;
        return !string.IsNullOrWhiteSpace(valueRefPath) && !string.IsNullOrWhiteSpace(valueRefParameter);
    }

    private void RefreshSignalValueReferenceCache()
    {
        _cachedSignalValueRefPath = string.Empty;
        _cachedSignalValueRefParameter = string.Empty;
        _cachedSignalValueRefRuntimeItem = null;

        if (!IsSignal)
        {
            return;
        }

        if (!TryReadSignalValueReferenceMetadata(Target, out var valueRefPath, out var valueRefParameter))
        {
            if (!TryResolveTargetItem(TargetPath, out var resolvedTarget)
                || !TryReadSignalValueReferenceMetadata(resolvedTarget, out valueRefPath, out valueRefParameter))
            {
                return;
            }
        }

        _cachedSignalValueRefPath = valueRefPath;
        _cachedSignalValueRefParameter = valueRefParameter;

        if (TryResolveTargetItem(valueRefPath, out var runtimeItem))
        {
            _cachedSignalValueRefRuntimeItem = runtimeItem;
        }
    }

    private static bool TryReadSignalValueReferenceMetadata(ItemModel? targetItem, out string valueRefPath, out string valueRefParameter)
    {
        valueRefPath = string.Empty;
        valueRefParameter = string.Empty;

        if (targetItem is null)
        {
            return false;
        }

        var rawValueRefPath = targetItem.Properties.Has(ValueRefPathPropertyName)
            ? targetItem.Properties[ValueRefPathPropertyName].Value?.ToString()
            : null;
        var rawValueRefParameter = targetItem.Properties.Has(ValueRefParameterPropertyName)
            ? targetItem.Properties[ValueRefParameterPropertyName].Value?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(rawValueRefPath) || string.IsNullOrWhiteSpace(rawValueRefParameter))
        {
            return false;
        }

        valueRefPath = TargetPathHelper.NormalizeConfiguredTargetPath(rawValueRefPath);
        valueRefParameter = rawValueRefParameter?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(valueRefPath) && !string.IsNullOrWhiteSpace(valueRefParameter);
    }

    private static bool MatchesSignalValueReferencePath(string changedPath, string valueRefPath, out bool isDirectValueRef, out bool isAncestorValueRef)
    {
        isDirectValueRef = TargetPathHelper.PathsEqual(changedPath, valueRefPath);
        var isChildValueRef = TargetPathHelper.IsDescendantPath(changedPath, valueRefPath);
        isAncestorValueRef = TargetPathHelper.IsDescendantPath(valueRefPath, changedPath);
        return isDirectValueRef || isChildValueRef || isAncestorValueRef;
    }

    private ItemProperty? ResolveWriteParameter()
    {
        if (Target is null)
        {
            return null;
        }

        if (Target.Properties.Has("write"))
        {
            return Target.Properties["write"];
        }

        if (TryResolveDeclaredWriteBinding(Target, out var declaredTarget))
        {
            return declaredTarget.Properties.Has("write")
                ? declaredTarget.Properties["write"]
                : ResolveValueParameter(declaredTarget);
        }

        return ResolveTargetProperty();
    }

    private ItemModel ResolveWriteTargetItem()
    {
        if (Target is null)
        {
            throw new InvalidOperationException("Target ist nicht gesetzt.");
        }

        if (TryResolveDeclaredWriteBinding(Target, out var declaredTarget))
        {
            return declaredTarget;
        }

        if (Target.Properties.Has("write"))
        {
            return Target;
        }

        return Target;
    }

    private static ItemModel ResolveInteractionWriteTargetItem(ItemModel targetItem)
    {
        if (TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget))
        {
            return declaredTarget;
        }

        if (targetItem.Properties.Has("write"))
        {
            return targetItem;
        }

        return targetItem;
    }

    private void EnsureTargetPropertySelection(ItemModel targetItem)
    {
        if (!string.IsNullOrWhiteSpace(TargetPropertyPath)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(TargetPropertyPath)
            && targetItem.Properties.Has(TargetPropertyPath))
        {
            return;
        }

        if (targetItem.Properties.Has("read"))
        {
            TargetPropertyPath = "read";
            return;
        }

        var firstParameter = targetItem.Properties.GetDictionary().Keys
            .Where(HostRegistryPropertyPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        TargetPropertyPath = firstParameter ?? string.Empty;
    }

    private string NormalizeTargetPropertyPath(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (Target is null)
        {
            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(trimmed)
            && HostRegistryPropertyPolicy.CanShowInUserPicker(trimmed)
            && Target.Properties.Has(trimmed))
        {
            return trimmed;
        }

        if (Target.Properties.Has("read"))
        {
            return "read";
        }

        return Target.Properties.GetDictionary().Keys
            .Where(HostRegistryPropertyPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;
    }

    private static ulong ToUInt64ForBitOperations(object? value)
        => value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => unchecked((ulong)sbyteValue),
            short shortValue => unchecked((ulong)shortValue),
            ushort ushortValue => ushortValue,
            int intValue => unchecked((ulong)intValue),
            uint uintValue => uintValue,
            long longValue => unchecked((ulong)longValue),
            ulong ulongValue => ulongValue,
            float floatValue => unchecked((ulong)floatValue),
            double doubleValue => unchecked((ulong)doubleValue),
            decimal decimalValue => unchecked((ulong)decimalValue),
            string text when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0UL
        };
    private static object? ConvertEditorValue(object? rawValue, Type? targetType)
    {
        if (targetType is null || rawValue is null)
        {
            return rawValue;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(rawValue))
        {
            return rawValue;
        }

        if (effectiveType.IsEnum)
        {
            return rawValue switch
            {
                string text when Enum.TryParse(effectiveType, text, ignoreCase: true, out var parsedEnum) => parsedEnum,
                _ => TryConvertEnumNumeric(rawValue, effectiveType)
            };
        }

        if (rawValue is string textValue)
        {
            if (effectiveType == typeof(string))
            {
                return textValue;
            }

            if (effectiveType == typeof(bool))
            {
                if (bool.TryParse(textValue, out var boolResult))
                {
                    return boolResult;
                }

                if (long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericBool))
                {
                    return numericBool != 0;
                }

                return rawValue;
            }

            if (effectiveType == typeof(byte))
            {
                return byte.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(sbyte))
            {
                return sbyte.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(short))
            {
                return short.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(ushort))
            {
                return ushort.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(int))
            {
                return int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(uint))
            {
                return uint.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(long))
            {
                return long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(ulong))
            {
                return ulong.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(float))
            {
                return float.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(double))
            {
                return double.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(decimal))
            {
                return decimal.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }
        }

        try
        {
            return Convert.ChangeType(rawValue, effectiveType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return rawValue;
        }
    }

    private static bool IsDeclaredWritable(ItemModel? item)
    {
        if (item is null)
        {
            return false;
        }

        if (item.Properties.Has("write"))
        {
            return true;
        }

        if (item.Properties.Has("writable"))
        {
            return ToBooleanLikeValue(item.Properties["writable"].Value);
        }

        return true;
    }

    private static ItemProperty? ResolveValueParameter(ItemModel item)
    {
        return item.Properties.Has("read") ? item.Properties["read"] : null;
    }

    private static SignalWriteMode GetResolvedWriteMode(ItemModel sourceItem)
    {
        if (sourceItem.Properties.Has("write"))
        {
            return SignalWriteMode.Direct;
        }

        var parsedMode = SignalWriteMode.Direct;
        if (sourceItem.Properties.Has("write_mode")
            && Enum.TryParse<SignalWriteMode>(sourceItem.Properties["write_mode"].Value?.ToString(), true, out parsedMode))
        {
            return parsedMode == SignalWriteMode.Request
                ? SignalWriteMode.Direct
                : parsedMode;
        }

        return SignalWriteMode.Direct;
    }

    private static string GetResolvedWritePath(ItemModel sourceItem)
    {
        if (sourceItem.Properties.Has("write"))
        {
            return sourceItem.Path ?? "-";
        }

        var declaredWritePath = sourceItem.Properties.Has("write_path")
            ? sourceItem.Properties["write_path"].Value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(declaredWritePath))
        {
            if (TryResolveDeclaredWriteBinding(sourceItem, out var declaredTarget))
            {
                return declaredTarget.Path ?? declaredWritePath;
            }

            return declaredWritePath;
        }

        return sourceItem.Path ?? "-";
    }

    private static bool TryResolveDeclaredWriteBinding(ItemModel sourceItem, out ItemModel writeTargetItem)
    {
        writeTargetItem = null!;
        if (sourceItem.Properties.Has("write"))
        {
            writeTargetItem = sourceItem;
            return true;
        }

        if (!sourceItem.Properties.Has("write_path"))
        {
            return false;
        }

        var writePath = sourceItem.Properties["write_path"].Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(writePath))
        {
            return false;
        }

        ItemModel? resolvedItem;
        if (!HostRegistries.Data.TryResolve(writePath, out resolvedItem) || resolvedItem is null)
        {
            return false;
        }

        var writeMode = SignalWriteMode.Direct;
        var parsedMode = SignalWriteMode.Direct;
        if (sourceItem.Properties.Has("write_mode")
            && Enum.TryParse<SignalWriteMode>(sourceItem.Properties["write_mode"].Value?.ToString(), true, out parsedMode))
        {
            writeMode = parsedMode == SignalWriteMode.Request
                ? SignalWriteMode.Direct
                : parsedMode;
        }

        var nonNullResolvedItem = resolvedItem!;
        writeTargetItem = nonNullResolvedItem;
        return true;
    }

    private static object? TryConvertEnumNumeric(object rawValue, Type enumType)
    {
        try
        {
            return Enum.ToObject(enumType, Convert.ToInt64(rawValue, CultureInfo.InvariantCulture));
        }
        catch
        {
            return rawValue;
        }
    }

    private static string FormatDiagnosticValue(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value is IFormattable formattable
            ? $"{formattable.ToString(null, CultureInfo.InvariantCulture)} ({value.GetType().Name})"
            : $"{value} ({value.GetType().Name})";
    }

    private static string FormatRegistryWriteState(string targetPath, string parameterName)
    {
        if (!HostRegistries.Data.TryResolve(targetPath, out var currentItem) || currentItem is null)
        {
            return "<missing>";
        }

        if (string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return FormatDiagnosticValue(currentItem.Value);
        }

        return currentItem.Properties.Has(parameterName)
            ? FormatDiagnosticValue(currentItem.Properties[parameterName].Value)
            : "<missing>";
    }

    private static IBrush ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Brushes.Transparent;
        }

        try
        {
            return Brush.Parse(value);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    private static string CreateRelativeButtonStateColor(string baseColor, double amount)
    {
        if (string.IsNullOrWhiteSpace(baseColor)
            || string.Equals(baseColor.Trim(), "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "Transparent";
        }

        try
        {
            var color = Color.Parse(baseColor);
            var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
            return luminance < 0.48
                ? ToHex(Mix(color, Colors.White, amount))
                : ToHex(Mix(color, Colors.Black, amount));
        }
        catch
        {
            return baseColor;
        }
    }

    private static Color Mix(Color source, Color target, double amount)
    {
        amount = System.Math.Clamp(amount, 0, 1);
        static byte Lerp(byte from, byte to, double amount)
            => (byte)System.Math.Clamp(System.Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero), 0, 255);

        return Color.FromArgb(
            source.A,
            Lerp(source.R, target.R, amount),
            Lerp(source.G, target.G, amount),
            Lerp(source.B, target.B, amount));
    }

    private static string ToHex(Color color)
        => color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public sealed class TableCellSlot : ObservableObject
    {
        private readonly FolderItemModel _owner;
        private bool _isSelected;
        private string? _contentLabel;
        private bool _isLastSelected;

        public TableCellSlot(FolderItemModel owner)
        {
            _owner = owner;
        }

        public int Row { get; init; }

        public int Column { get; init; }

        public string Label => string.IsNullOrWhiteSpace(ContentLabel) ? $"{Row},{Column}" : ContentLabel!;

        public string? ContentLabel
        {
            get => _contentLabel;
            set => SetProperty(ref _contentLabel, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsLastSelected
        {
            get => _isLastSelected;
            set => SetProperty(ref _isLastSelected, value);
        }

        public bool IsVisibleInLayout => !_owner.IsCircleDisplay || _owner.IsCircleCellVisible(Row, Column);

        // Bezieht ein evtl. vorhandenes Child-Widget fuer alle belegten Zellen mit ein,
        // auch wenn das Widget ueber mehrere Zeilen/Spalten geht.
        public FolderItemModel? ChildItem
            => _owner.Items.FirstOrDefault(c => c.IsTableChildControl
                                                && Row >= c.TableCellRow
                                                && Row < c.TableCellRow + System.Math.Max(1, c.TableCellRowSpan)
                                                && Column >= c.TableCellColumn
                                                && Column < c.TableCellColumn + System.Math.Max(1, c.TableCellColumnSpan));
    }

    private string BuildPath()
    {
        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(FolderName))
        {
            segments.Add(FolderName);
        }

        if (ParentItem is not null && !string.IsNullOrWhiteSpace(ParentItem.Name))
        {
            segments.Add(ParentItem.Name);
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            segments.Add(Name);
        }

        return string.Join(".", segments);
    }

    private string GetRelativeWidgetIdentityPath()
    {
        var relativePath = ParentItem is not null
            && TargetPathHelper.TryGetRelativePath(Path, ParentItem.Path, out var childRelativePath)
            ? childRelativePath
            : TargetPathHelper.ToPersistedLayoutTargetPath(Path, FolderName);

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return relativePath;
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        return Id;
    }
}
















