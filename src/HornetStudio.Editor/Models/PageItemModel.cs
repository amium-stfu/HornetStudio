using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Amium.Items;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Models;

public enum ControlKind
{
    Button,
    Signal,
    Item,
    WidgetList,
    TableControl,
    CircleDisplay,
    LogControl,
    ChartControl,
    UdlClientControl,
    BrokerWidget,
    CsvLoggerControl,
    SqlLoggerControl,
    CameraControl,
    PythonClient,
    ApplicationExplorer,
    CustomSignals,
    EnhancedSignals
}

public sealed class FolderItemModel : ObservableObject
{
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
    private const string CircleDisplaySignalColorItemName = "SignalColor";
    private const string CircleDisplaySignalRunItemName = "SignalRun";
    private const string CircleDisplayProgressBarItemName = "ProgressBar";
    private const string CircleDisplayProgressStateItemName = "ProgressState";
    private const string CircleDisplayProgressBarColorItemName = "ProgressBarColor";
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
    private string _enhancedSignalDefinitions = string.Empty;
    private bool _applicationAutoStart;
    private string _blockedLegacyScriptPath = string.Empty;
    private DateTime _blockedLegacyScriptWriteTimeUtc;
    private string _targetParameterPath = string.Empty;
    private string _targetParameterFormat = string.Empty;
    private string _unit = string.Empty;
    private string _targetLog = "Logs.Host";
    private int _view = 1;
    private int _activeViewId = 1;
    private int _historySeconds = 120;
    private int _viewSeconds = 30;
    private string _chartSeriesDefinitions = string.Empty;
    private string _interactionRules = string.Empty;
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
    private string _brokerHost = BrokerWidgetDefaults.Host;
    private int _brokerPort = BrokerWidgetDefaults.Port;
    private string _brokerBaseTopic = BrokerWidgetDefaults.BaseTopic;
    private string _brokerClientId = BrokerWidgetClientId.Create();
    private string _brokerMode = BrokerWidgetModes.External;
    private bool _brokerAutoConnect;
    private string _brokerAttachedItemPaths = string.Empty;
    private string _brokerPublishedItemPaths = string.Empty;
    private string _itemExposures = string.Empty;
    private string _udlAttachedItemPaths = string.Empty;
    private string _udlDemoModuleDefinitions = string.Empty;
    private string _udlModuleExposureDefinitions = string.Empty;
    private Item? _target;
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
    private string _effectiveBodyBorder = LightBorder;
    private string _effectiveFooterForeground = LightMutedForeground;
    private string _effectiveFooterBackground = "Transparent";
    private string _effectiveFooterBorder = LightBorder;
    private DispatcherTimer? _pendingRefreshTimer;
    private DispatcherTimer? _scriptTimer;
    private int _registryRefreshQueued;
    private DateTimeOffset _lastTargetRefreshUtc = DateTimeOffset.MinValue;
    private bool _hasPendingTargetRefresh;
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

        return $"Studio.{folderName}.LoggerRuntime.{itemName}";
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

        return $"Studio.{folderName}.DisplayRuntime.{itemName}";
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

        return TargetPathHelper.PathsEqual(e.Key, runtimePath)
               || TargetPathHelper.IsDescendantPath(e.Key, runtimePath)
               || TargetPathHelper.IsDescendantPath(runtimePath, e.Key);
    }

    private static bool TryResolveCircleDisplayTargetCore(string candidatePath, out Item? targetItem, out Parameter? parameter)
    {
        if (HostRegistries.Data.TryResolve(candidatePath, out targetItem) && targetItem is not null)
        {
            parameter = targetItem.Params.Has("Value") ? targetItem.Params["Value"] : null;
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
                        slot.ContentLabel = string.IsNullOrWhiteSpace(child.Name) ? "Item" : child.Name;
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
                RaisePropertyChanged(nameof(TargetParameterView));
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
                    RaisePropertyChanged(nameof(TargetParameterView));
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
                    RaisePropertyChanged(nameof(TargetParameterView));
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
                    RaisePropertyChanged(nameof(TargetParameterView));
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

    public bool IsItem => Kind == ControlKind.Item || Kind == ControlKind.Signal;

    public bool IsWidgetList => Kind == ControlKind.WidgetList;

    public bool IsTableControl => Kind == ControlKind.TableControl;

    public bool IsCircleDisplay => Kind == ControlKind.CircleDisplay;

    public bool UsesTableLayout => Kind is ControlKind.TableControl or ControlKind.CircleDisplay;

    public bool IsLogControl => Kind == ControlKind.LogControl;

    public bool IsCsvLoggerControl => Kind == ControlKind.CsvLoggerControl;

    public bool IsSqlLoggerControl => Kind == ControlKind.SqlLoggerControl;

    public bool IsCameraControl => Kind == ControlKind.CameraControl;

    public bool IsChartControl => Kind == ControlKind.ChartControl;

    public bool IsUdlClientControl => Kind == ControlKind.UdlClientControl;

    public bool IsBrokerWidget => Kind == ControlKind.BrokerWidget;

    public bool IsApplicationExplorer => Kind == ControlKind.ApplicationExplorer;

    public bool IsCustomSignals => Kind == ControlKind.CustomSignals;

    public bool IsEnhancedSignals => Kind == ControlKind.EnhancedSignals;

    // Controls, die als Child in einem Table gerendert und selektiert werden duerfen.
    public bool IsTableChildControl => Kind is ControlKind.Item
        or ControlKind.Signal
        or ControlKind.Button
        or ControlKind.LogControl
        or ControlKind.ChartControl
        or ControlKind.UdlClientControl
        or ControlKind.BrokerWidget
        or ControlKind.CsvLoggerControl
        or ControlKind.SqlLoggerControl
        or ControlKind.CameraControl
        or ControlKind.ApplicationExplorer
        or ControlKind.CustomSignals
        or ControlKind.EnhancedSignals;

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

    public string EnhancedSignalDefinitions
    {
        get => _enhancedSignalDefinitions;
        set => SetProperty(ref _enhancedSignalDefinitions, value ?? string.Empty);
    }

    public bool ApplicationAutoStart
    {
        get => _applicationAutoStart;
        set => SetProperty(ref _applicationAutoStart, value);
    }

    public string TargetParameterPath
    {
        get => _targetParameterPath;
        set
        {
            var normalizedValue = NormalizeTargetParameterPath(value);
            if (SetProperty(ref _targetParameterPath, normalizedValue))
            {
                RaisePropertyChanged(nameof(TargetParameterView));
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

    public string TargetParameterFormat
    {
        get => _targetParameterFormat;
        set
        {
            if (SetProperty(ref _targetParameterFormat, value))
            {
                RaisePropertyChanged(nameof(TargetParameterView));
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
    public int HistorySeconds
    {
        get => _historySeconds;
        set => SetProperty(ref _historySeconds, value <= 1 ? 1 : value);
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
        set => SetProperty(ref _brokerHost, string.IsNullOrWhiteSpace(value) ? BrokerWidgetDefaults.Host : value.Trim());
    }

    public int BrokerPort
    {
        get => _brokerPort;
        set => SetProperty(ref _brokerPort, value <= 0 ? BrokerWidgetDefaults.Port : value);
    }

    public string BrokerBaseTopic
    {
        get => _brokerBaseTopic;
        set => SetProperty(ref _brokerBaseTopic, string.IsNullOrWhiteSpace(value) ? BrokerWidgetDefaults.BaseTopic : value.Trim());
    }

    public string BrokerClientId
    {
        get => _brokerClientId;
        set => SetProperty(ref _brokerClientId, BrokerWidgetClientId.Normalize(value));
    }

    public string BrokerMode
    {
        get => _brokerMode;
        set => SetProperty(ref _brokerMode, BrokerWidgetModes.Normalize(value));
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
    /// Gets or sets the local HornetStudio item paths configured for future BrokerWidget publishing.
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

    public Item? Target
    {
        get => _target;
        private set
        {
            if (SetProperty(ref _target, value))
            {
                RaisePropertyChanged(nameof(DisplayValue));
                RaisePropertyChanged(nameof(DisplayUnit));
                RaisePropertyChanged(nameof(RequestStatusText));
                RaisePropertyChanged(nameof(DisplayFooter));
                RaisePropertyChanged(nameof(TargetParameterView));
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
        => Target is null ? "-" : (!string.IsNullOrWhiteSpace(TargetParameterFormat) ? "True" : "False");

    public string ResolvedTargetWriteMode
        => Target is null ? "-" : GetResolvedWriteMode(Target).ToString();

    public string ResolvedTargetWritePath
        => Target is null ? "-" : GetResolvedWritePath(Target);

    public string ResolvedTargetSourceFormat
        => Target is null
            ? "-"
            : Target.Params.Has("Format")
                ? Target.Params["Format"].Value?.ToString() ?? "-"
                : "-";

    public string ResolvedTargetEffectiveFormat
        => !string.IsNullOrWhiteSpace(TargetParameterFormat)
            ? TargetParameterFormat
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
        ControlKind.Item => Height * 0.18,
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

    public ParameterDisplayModel ItemBodyPresentation => BuildTargetParameterView(string.Empty, DisplayValue);

    public string RequestStatusText
        => Target?.Params.Has("SendStatus") == true
            ? Target.Params["SendStatus"].Value?.ToString() ?? string.Empty
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
        => string.IsNullOrWhiteSpace(ButtonBodyBackground)
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
            if (TargetParameterView.Definition.Kind is not (ParameterVisualKind.Bool or ParameterVisualKind.Bits))
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
            var reservedUnitWidth = TargetParameterView.ShowUnit ? ItemUnitWidth + 8 : 0;
            return System.Math.Max(Width - reservedUnitWidth - 16, 24);
        }
    }

    public int ValueCharacterCount
        => System.Math.Max((TargetParameterView.ValueText ?? string.Empty).Length, 1);

    public int UnitCharacterCount
        => System.Math.Max((TargetParameterView.UnitText ?? string.Empty).Length, 1);

    public double ItemValueFontSize
    {
        get
        {
            if (TargetParameterView.Definition.Kind is ParameterVisualKind.Bool or ParameterVisualKind.Bits)
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
                "Value",
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
            if (!TargetParameterView.ShowUnit)
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

    public ParameterDisplayModel TargetParameterView => BuildTargetParameterView();

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

            var definition = TargetParameterView.Definition;
            return definition.Kind is ParameterVisualKind.Text or ParameterVisualKind.Numeric or ParameterVisualKind.Hex or ParameterVisualKind.Bits
                && ResolveTargetParameter() is not null
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

            return TargetParameterView.Label;
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
        ControlKind.Item => 50,
        ControlKind.WidgetList => 240,
        ControlKind.TableControl => 240,
        ControlKind.CircleDisplay => 240,
        ControlKind.LogControl => 320,
        ControlKind.ChartControl => 360,
        ControlKind.CsvLoggerControl => 260,
        ControlKind.SqlLoggerControl => 260,
        ControlKind.CameraControl => 260,
        ControlKind.BrokerWidget => 320,
        _ => 140
    };

    public double MinHeight => Kind switch
    {
        ControlKind.Button => 56,
        ControlKind.Signal => 1,
        ControlKind.Item => 1,
        ControlKind.WidgetList => 180,
        ControlKind.TableControl => 180,
        ControlKind.CircleDisplay => 220,
        ControlKind.LogControl => 220,
        ControlKind.ChartControl => 220,
        ControlKind.CsvLoggerControl => 120,
        ControlKind.SqlLoggerControl => 120,
        ControlKind.CameraControl => 160,
        ControlKind.BrokerWidget => 180,
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
            TargetParameterPath = string.Empty;
            CancelPendingTargetRefresh();
            TriggerTargetRefresh();
            return;
        }

        if (!TryResolveTargetItem(TargetPath, out var item))
        {
            Target = null;
            TargetParameterPath = string.Empty;
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
        EnsureTargetParameterSelection(selectedItem);
        CancelPendingTargetRefresh();
        TriggerTargetRefresh();
    }

    public void ApplyTargetSelection(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            TargetPath = string.Empty;
            TargetParameterPath = string.Empty;
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
        EnsureTargetParameterSelection(selectedItem);
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
        RaisePropertyChanged(nameof(TargetParameterView));
        CancelPendingTargetRefresh();
        TriggerTargetRefresh();
    }

    public void RefreshTargetBindings()
    {
        var targetParameterViewChanged = HasPresentationChanged(TargetParameterView, ref _lastTargetParameterViewSignature);
        var itemBodyPresentationChanged = HasPresentationChanged(ItemBodyPresentation, ref _lastItemBodyPresentationSignature);

        RaisePropertyChanged(nameof(DisplayValue));
        RaisePropertyChanged(nameof(DisplayUnit));
        RaisePropertyChanged(nameof(RequestStatusText));
        RaisePropertyChanged(nameof(DisplayFooter));
        if (targetParameterViewChanged)
        {
            RaisePropertyChanged(nameof(TargetParameterView));
        }

        if (itemBodyPresentationChanged)
        {
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

    public bool TryUpdateTargetParameterValue(object? rawValue, out string error)
    {
        if (IsReadOnly)
        {
            error = "Item ist schreibgeschuetzt.";
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
            var targetParameter = ResolveTargetParameter();
            var writeTargetItem = ResolveWriteTargetItem();
            var refreshDisplayImmediately = Target is not null
                && (ReferenceEquals(writeTargetItem, Target)
                    || TargetPathHelper.PathsEqual(writeTargetItem.Path, Target.Path));
            var convertedValue = ConvertEditorValue(rawValue, parameter.Value?.GetType() ?? targetParameter?.Value?.GetType());
            var targetLogPath = Target?.Path ?? TargetPath;
            Core.LogInfo(
                $"[SignalWrite] item={Path} target={targetLogPath} targetParam={targetParameter?.Name ?? "<none>"} targetValue={FormatDiagnosticValue(targetParameter?.Value)} writeTarget={writeTargetItem.Path ?? "<none>"} writeParam={parameter.Name} raw={FormatDiagnosticValue(rawValue)} converted={FormatDiagnosticValue(convertedValue)}");
            if (!string.Equals(parameter.Name, "Value", StringComparison.OrdinalIgnoreCase)
                && !HostRegistryParameterPolicy.CanUserWriteParameter(parameter.Name))
            {
                error = $"Parameter '{parameter.Name}' is protected and cannot be written.";
                Core.LogInfo($"[SignalWrite] result=blocked item={Path} writeParam={parameter.Name}");
                return false;
            }

            var targetPath = writeTargetItem.Path ?? Target?.Path ?? TargetPath;
            var updated = string.Equals(parameter.Name, "Value", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(targetPath, convertedValue)
                : HostRegistries.Data.TryUpdateUserParameter(targetPath, parameter.Name, convertedValue);
            if (!updated)
            {
                if (string.Equals(parameter.Name, "Value", StringComparison.OrdinalIgnoreCase))
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
            return TryUpdateTargetParameterValue(rawValue, out error);
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

        var readParameter = ResolveTargetParameter();
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

                return TryApplyInteractionWrite(setTarget!, rule.TargetPath, rule.Argument, out error);

            case ItemInteractionAction.InvokePythonFunction:
                return TryInvokeApplicationFunction(rule, out error);

            default:
                error = $"Action {rule.Action} wird noch nicht unterstuetzt.";
                return false;
        }
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

    private bool TryResolveInteractionTarget(string? targetPath, out Item? item)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
        {
            item = Target;
            return item is not null;
        }

        return TryResolveTargetItem(targetPath, out item);
    }

    private Parameter? ResolveInteractionReadParameter(string? targetPath, Item targetItem)
    {
        if ((string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
            && ReferenceEquals(targetItem, Target))
        {
            return ResolveTargetParameter();
        }

        if (targetItem.Params.Has("Value"))
        {
            return targetItem.Params["Value"];
        }

        var firstParameter = targetItem.Params.GetDictionary().Keys
            .Where(HostRegistryParameterPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return firstParameter is null ? null : targetItem.Params[firstParameter];
    }

    private Parameter? ResolveInteractionWriteParameter(string? targetPath, Item targetItem)
    {
        if ((string.IsNullOrWhiteSpace(targetPath) || string.Equals(targetPath, "this", StringComparison.OrdinalIgnoreCase))
            && ReferenceEquals(targetItem, Target))
        {
            return ResolveWriteParameter();
        }

        if (TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget))
        {
            return ResolveValueParameter(declaredTarget);
        }

        if (targetItem.Has("Request"))
        {
            return targetItem["Request"].Params["Value"];
        }

        return ResolveInteractionReadParameter(targetPath, targetItem);
    }

    private bool TryApplyInteractionWrite(Item targetItem, string? targetPath, object? rawValue, out string error)
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
            var convertedValue = ConvertEditorValue(rawValue, writeParameter.Value?.GetType() ?? readParameter?.Value?.GetType());
            if (!string.Equals(writeParameter.Name, "Value", StringComparison.OrdinalIgnoreCase)
                && !HostRegistryParameterPolicy.CanUserWriteParameter(writeParameter.Name))
            {
                error = $"Parameter '{writeParameter.Name}' is protected and cannot be written.";
                return false;
            }

            var resolvedTargetPath = writeTargetItem.Path ?? targetItem.Path ?? targetPath ?? string.Empty;
            var updated = string.Equals(writeParameter.Name, "Value", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(resolvedTargetPath, convertedValue)
                : HostRegistries.Data.TryUpdateUserParameter(resolvedTargetPath, writeParameter.Name, convertedValue);
            if (!updated)
            {
                if (string.Equals(writeParameter.Name, "Value", StringComparison.OrdinalIgnoreCase))
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
        if (string.IsNullOrWhiteSpace(TargetPath))
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
            return;
        }

        var isDirectTarget = TargetPathHelper.PathsEqual(e.Key, matchedTargetPath);
        var isChildTarget = TargetPathHelper.IsDescendantPath(e.Key, matchedTargetPath);
        var isAncestorTarget = TargetPathHelper.IsDescendantPath(matchedTargetPath, e.Key);
        if (!isDirectTarget && !isChildTarget && !isAncestorTarget)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            QueueRegistryRefresh();
            return;
        }

        if (isDirectTarget && (Target is null || !ReferenceEquals(Target, e.Item)))
        {
            Target = e.Item;
        }

        if (isAncestorTarget
            && TargetPathHelper.TryGetRelativePath(matchedTargetPath, e.Key, out var targetRelativePath)
            && TryResolveRelativeChild(e.Item, targetRelativePath, out var resolvedTarget)
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
            EnsureTargetParameterSelection(Target);
        }

        RequestTargetRefresh();
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
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                return;
            }

            ResolveTarget();
            RequestTargetRefresh();
        }, DispatcherPriority.Background);
    }

    private static void ApplyChildRegistryUpdate(Item rootItem, string relativePath, DataChangedEventArgs e)
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

        if (string.Equals(e.ParameterName, "Value", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            current.Value = e.Item.Value;
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.ParameterName) && e.Item.Params.Has(e.ParameterName) && current.Params.Has(e.ParameterName))
        {
            current.Params[e.ParameterName].Value = e.Item.Params[e.ParameterName].Value;
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

    private static void PublishItemSnapshot(Item item)
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
        Dispatcher.UIThread.Post(() =>
        {
            if (IsScriptTarget && !IsLegacyScriptBlockedForCurrentPath())
            {
                RefreshScriptValue();
            }

            RefreshTargetBindings();
            _lastTargetRefreshUtc = DateTimeOffset.UtcNow;
        });
    }

    private void CancelPendingTargetRefresh()
    {
        _hasPendingTargetRefresh = false;
        Dispatcher.UIThread.Post(() => _pendingRefreshTimer?.Stop());
    }

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
        // es unter Studio.<Folder>.Applications.Python.<Name> im Target-Tree und RealtimeChart
        // sichtbar und direkt aufloesbar ist.
        Item item;
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            item = new Item(nameSegment, value);
        }
        else
        {
            item = new Item(nameSegment, value, parentPath);
        }

        item.Params["Path"].Value = normalizedPath;

        HostRegistries.Data.UpsertSnapshot(normalizedPath, item, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: false);
    }

    private string? GetScriptRuntimePath()
    {
        if (!IsScriptTarget)
        {
            return null;
        }

        static string NormalizeSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('.', '/', '\\');
        }

        var folderSegment = NormalizeSegment(FolderName);
        if (string.IsNullOrWhiteSpace(folderSegment))
        {
            folderSegment = "Page";
        }

        var nameSource = !string.IsNullOrWhiteSpace(Name)
            ? Name
            : (!string.IsNullOrWhiteSpace(Title) ? Title : Id);
        var nameSegment = NormalizeSegment(nameSource);
        if (string.IsNullOrWhiteSpace(nameSegment))
        {
            nameSegment = Id;
        }

        return $"Studio.{folderSegment}.Applications.Python.{nameSegment}";
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

    private bool TryResolveTargetItem(string targetPath, out Item? item)
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
        if (!string.IsNullOrWhiteSpace(resolvedPath) && TargetPathHelper.IsRuntimeItemBrokerPath(resolvedPath))
        {
            return TargetPathHelper.ToFlatItemBrokerPath(resolvedPath);
        }

        return resolvedPath ?? TargetPathHelper.NormalizeConfiguredTargetPath(configuredPath);
    }

    private static bool TryResolveRelativeChild(Item rootItem, string relativePath, out Item? item)
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

        Item snapshot;
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
                ? new Item(nameSegment)
                : new Item(nameSegment, null, parentPath);
        }

        snapshot.Params["Path"].Value = runtimeBasePath;
        snapshot.Params["Kind"].Value = "DisplayRuntime";
        snapshot.Params["Text"].Value = string.IsNullOrWhiteSpace(Name) ? Title : Name;
        snapshot[CircleDisplaySignalColorItemName].Value = string.IsNullOrWhiteSpace(SignalColor)
            ? CircleDisplayDefaultSignalColor
            : SignalColor;
        snapshot[CircleDisplaySignalColorItemName].Params["Text"].Value = CircleDisplaySignalColorItemName;
        snapshot[CircleDisplaySignalRunItemName].Value = SignalRun;
        snapshot[CircleDisplaySignalRunItemName].Params["Text"].Value = CircleDisplaySignalRunItemName;
        snapshot[CircleDisplayProgressBarItemName].Value = ProgressBar;
        snapshot[CircleDisplayProgressBarItemName].Params["Text"].Value = CircleDisplayProgressBarItemName;
        snapshot[CircleDisplayProgressStateItemName].Value = System.Math.Clamp(ProgressState, 0d, 100d);
        snapshot[CircleDisplayProgressStateItemName].Params["Text"].Value = CircleDisplayProgressStateItemName;
        snapshot[CircleDisplayProgressBarColorItemName].Value = string.IsNullOrWhiteSpace(ProgressBarColor)
            ? CircleDisplayDefaultProgressBarColor
            : ProgressBarColor;
        snapshot[CircleDisplayProgressBarColorItemName].Params["Text"].Value = CircleDisplayProgressBarColorItemName;

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

        var item = new Item(runtimeItemName, value, GetDisplayRuntimeBasePath());
        item.Params["Kind"].Value = "DisplayRuntime";
        item.Params["Text"].Value = title;
        item.Params["Title"].Value = title;
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

    private static HorizontalAlignment ParseHorizontalAlignment(string? value, HorizontalAlignment fallback)
    {
        return NormalizeAlignment(value, fallback.ToString()) switch
        {
            "Left" => HorizontalAlignment.Left,
            "Right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
    }

    private ParameterDisplayModel BuildTargetParameterView()
        => BuildTargetParameterView(null, Title);

    private ParameterDisplayModel BuildTargetParameterView(string? labelOverride, string fallbackText)
    {
        var parameter = ResolveTargetParameter();
        var isValueParameter = parameter is not null && string.Equals(parameter.Name, "Value", StringComparison.OrdinalIgnoreCase);
        var label = labelOverride ?? (isValueParameter && Target?.Params.Has("Text") == true
            ? Target.Params["Text"].Value?.ToString() ?? string.Empty
            : (parameter?.Name ?? Header));
        var format = !string.IsNullOrWhiteSpace(TargetParameterFormat)
            ? TargetParameterFormat
            : isValueParameter && Target?.Params.Has("Format") == true
                ? Target.Params["Format"].Value?.ToString() ?? string.Empty
                : string.Empty;
        var unitText = !string.IsNullOrWhiteSpace(Unit)
            ? Unit
            : isValueParameter
                ? GetTargetUnitText(Target)
                : string.Empty;
        // Wenn ein Script-Wert aktiv ist, soll dieser auch durch die bestehende
        // Formatlogik (z.B. numeric:0.000) laufen. Dazu uebergeben wir den
        // Script-Wert als Fallback-Text und unterdruecken den Parameterwert,
        // damit ParameterDisplayModel den Fallback formatiert.
        if (IsScriptTarget && _scriptValue is not null)
        {
            var scriptText = _scriptValue is IFormattable formattable
                ? (formattable.ToString(null, CultureInfo.InvariantCulture) ?? _scriptValue.ToString() ?? string.Empty)
                : (_scriptValue.ToString() ?? string.Empty);

            return new ParameterDisplayModel(null, label, format, unitText, scriptText);
        }

        return new ParameterDisplayModel(parameter, label, format, unitText, fallbackText);
    }

    private static bool HasPresentationChanged(ParameterDisplayModel presentation, ref string previousSignature)
    {
        var signature = BuildPresentationSignature(presentation);
        if (string.Equals(signature, previousSignature, StringComparison.Ordinal))
        {
            return false;
        }

        previousSignature = signature;
        return true;
    }

    private static string BuildPresentationSignature(ParameterDisplayModel presentation)
    {
        var parameter = presentation.Parameter;
        return string.Join(
            "|",
            parameter?.Path ?? string.Empty,
            parameter?.Name ?? string.Empty,
            FormatDiagnosticValue(parameter?.Value),
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

    private static string GetTargetUnitText(Item? item)
    {
        return item?.Params.Has("Unit") == true
            ? item.Params["Unit"].Value?.ToString() ?? string.Empty
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
            "Value",
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
            ControlKind.UdlClientControl => "UdlClientControl",
            ControlKind.BrokerWidget => "BrokerWidget",
            ControlKind.CsvLoggerControl => "CsvLoggerControl",
            ControlKind.SqlLoggerControl => "SqlLoggerControl",
            ControlKind.CameraControl => "CameraControl",
            ControlKind.ApplicationExplorer => "ApplicationExplorer",
            ControlKind.Item or ControlKind.Signal => "Signal",
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

    private Parameter? ResolveTargetParameter()
    {
        if (Target is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(TargetParameterPath)
            && HostRegistryParameterPolicy.CanShowInUserPicker(TargetParameterPath)
            && Target.Params.Has(TargetParameterPath))
        {
            return Target.Params[TargetParameterPath];
        }

        return Target.Params.Has("Value") ? Target.Params["Value"] : null;
    }

    private Parameter? ResolveWriteParameter()
    {
        if (Target is null)
        {
            return null;
        }

        if (TryResolveDeclaredWriteBinding(Target, out var declaredTarget))
        {
            return ResolveValueParameter(declaredTarget);
        }

        if (Target.Has("Request"))
        {
            return Target["Request"].Params["Value"];
        }

        return ResolveTargetParameter();
    }

    private Item ResolveWriteTargetItem()
    {
        if (Target is null)
        {
            throw new InvalidOperationException("Target ist nicht gesetzt.");
        }

        if (TryResolveDeclaredWriteBinding(Target, out var declaredTarget))
        {
            return declaredTarget;
        }

        if (Target.Has("Request"))
        {
            return Target["Request"];
        }

        return Target;
    }

    private static Item ResolveInteractionWriteTargetItem(Item targetItem)
    {
        if (TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget))
        {
            return declaredTarget;
        }

        if (targetItem.Has("Request"))
        {
            return targetItem["Request"];
        }

        return targetItem;
    }

    private void EnsureTargetParameterSelection(Item targetItem)
    {
        if (!string.IsNullOrWhiteSpace(TargetParameterPath)
            && HostRegistryParameterPolicy.CanShowInUserPicker(TargetParameterPath)
            && targetItem.Params.Has(TargetParameterPath))
        {
            return;
        }

        if (targetItem.Params.Has("Value"))
        {
            TargetParameterPath = "Value";
            return;
        }

        var firstParameter = targetItem.Params.GetDictionary().Keys
            .Where(HostRegistryParameterPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        TargetParameterPath = firstParameter ?? string.Empty;
    }

    private string NormalizeTargetParameterPath(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (Target is null)
        {
            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(trimmed)
            && HostRegistryParameterPolicy.CanShowInUserPicker(trimmed)
            && Target.Params.Has(trimmed))
        {
            return trimmed;
        }

        if (Target.Params.Has("Value"))
        {
            return "Value";
        }

        return Target.Params.GetDictionary().Keys
            .Where(HostRegistryParameterPolicy.CanShowInUserPicker)
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

    private static bool IsDeclaredWritable(Item? item)
    {
        if (item is null)
        {
            return false;
        }

        if (item.Params.Has("Writable"))
        {
            return ToBooleanLikeValue(item.Params["Writable"].Value);
        }

        return true;
    }

    private static Parameter? ResolveValueParameter(Item item)
    {
        return item.Params.Has("Value") ? item.Params["Value"] : null;
    }

    private static SignalWriteMode GetResolvedWriteMode(Item sourceItem)
    {
        var parsedMode = SignalWriteMode.Direct;
        if (sourceItem.Params.Has("WriteMode")
            && Enum.TryParse<SignalWriteMode>(sourceItem.Params["WriteMode"].Value?.ToString(), true, out parsedMode))
        {
            return parsedMode;
        }

        return sourceItem.Has("Request") ? SignalWriteMode.Request : SignalWriteMode.Direct;
    }

    private static string GetResolvedWritePath(Item sourceItem)
    {
        var declaredWritePath = sourceItem.Params.Has("WritePath")
            ? sourceItem.Params["WritePath"].Value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(declaredWritePath))
        {
            if (TryResolveDeclaredWriteBinding(sourceItem, out var declaredTarget))
            {
                return declaredTarget.Path ?? declaredWritePath;
            }

            return declaredWritePath;
        }

        if (sourceItem.Has("Request"))
        {
            return sourceItem["Request"].Path ?? "-";
        }

        return sourceItem.Path ?? "-";
    }

    private static bool TryResolveDeclaredWriteBinding(Item sourceItem, out Item writeTargetItem)
    {
        writeTargetItem = null!;
        if (!sourceItem.Params.Has("WritePath"))
        {
            return false;
        }

        var writePath = sourceItem.Params["WritePath"].Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(writePath))
        {
            return false;
        }

        Item? resolvedItem;
        if (!HostRegistries.Data.TryResolve(writePath, out resolvedItem) || resolvedItem is null)
        {
            return false;
        }

        var writeMode = SignalWriteMode.Direct;
        var parsedMode = SignalWriteMode.Direct;
        if (sourceItem.Params.Has("WriteMode")
            && Enum.TryParse<SignalWriteMode>(sourceItem.Params["WriteMode"].Value?.ToString(), true, out parsedMode))
        {
            writeMode = parsedMode;
        }

        var nonNullResolvedItem = resolvedItem!;
        writeTargetItem = writeMode == SignalWriteMode.Request && nonNullResolvedItem.Has("Request")
            ? nonNullResolvedItem["Request"]
            : nonNullResolvedItem;
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
}
















