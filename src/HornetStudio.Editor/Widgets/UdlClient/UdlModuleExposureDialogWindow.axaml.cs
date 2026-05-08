using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using HornetStudio.Host;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class UdlModuleExposureDialogWindow : Window
{
    private static readonly IReadOnlyList<string> ParameterFormatOptions = ["Text", "Numeric", "Hex", "bool", "EpochToDatetime", "b4", "b8", "b16"];
    private readonly EditorDialogField? _field;
    private readonly DialogViewModel _viewModel;

    public UdlModuleExposureDialogWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }

    public UdlModuleExposureDialogWindow(
        MainWindowViewModel ownerViewModel,
        IReadOnlyList<UdlModuleExposureDefinition> definitions,
        IReadOnlyList<UdlRuntimeModuleChannelDescriptor> runtimeChannels,
        string? moduleName = null)
    {
        InitializeComponent();

        _viewModel = new DialogViewModel(ownerViewModel, definitions, runtimeChannels, moduleName);
        DataContext = _viewModel;
    }

    public UdlModuleExposureDialogWindow(MainWindowViewModel ownerViewModel, EditorDialogField field)
    {
        InitializeComponent();

        _field = field;
        var ownerItem = field.OwnerItem;
        _viewModel = new DialogViewModel(
            ownerViewModel,
            UdlModuleExposureDefinitionCodec.ParseDefinitions(field.Value),
            ownerItem is null ? [] : ResolveRuntimeChannels(ownerItem),
            moduleName: null);
        DataContext = _viewModel;
    }

    public static async Task<string?> ShowAsync(
        Window owner,
        MainWindowViewModel viewModel,
        string rawDefinitions,
        IReadOnlyList<UdlRuntimeModuleChannelDescriptor> runtimeChannels,
        string? moduleName = null)
    {
        var parsedDefinitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(rawDefinitions);
        var effectiveDefinitions = string.IsNullOrWhiteSpace(moduleName)
            ? parsedDefinitions
            : parsedDefinitions
                .Where(definition => string.Equals(definition.ModuleName, moduleName, System.StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var effectiveRuntimeChannels = string.IsNullOrWhiteSpace(moduleName)
            ? runtimeChannels
            : runtimeChannels
                .Where(channel => string.Equals(channel.ModuleName, moduleName, System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var dialog = new UdlModuleExposureDialogWindow(
            ownerViewModel: viewModel,
            definitions: effectiveDefinitions,
            runtimeChannels: effectiveRuntimeChannels,
            moduleName: moduleName);
        var result = await dialog.ShowDialog<string?>(owner);
        if (result is null || string.IsNullOrWhiteSpace(moduleName))
        {
            return result;
        }

        var updatedModuleDefinitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(result);
        var mergedDefinitions = parsedDefinitions
            .Where(definition => !string.Equals(definition.ModuleName, moduleName, System.StringComparison.OrdinalIgnoreCase))
            .Concat(updatedModuleDefinitions)
            .OrderBy(definition => definition.ModuleName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.ChannelName, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return UdlModuleExposureDefinitionCodec.SerializeDefinitions(mergedDefinitions);
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildResult(out var result))
        {
            return;
        }

        if (_field is not null)
        {
            _field.Value = result;
            Close();
            return;
        }

        Close(result);
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private static IReadOnlyList<UdlRuntimeModuleChannelDescriptor> ResolveRuntimeChannels(FolderItemModel ownerItem)
    {
        var prefixes = UdlPathHelper.GetRuntimeBasePaths(NormalizeClientName(ownerItem));

        return HostRegistries.Data.GetAllKeys()
            .Select(key => ResolveRuntimeChannelDescriptor(prefixes, key))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .GroupBy(static descriptor => UdlModuleExposureEditorRow.BuildKey(descriptor.ModuleName, descriptor.ChannelName), System.StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static descriptor => descriptor.ModuleName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(static descriptor => descriptor.ChannelName, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UdlRuntimeModuleChannelDescriptor? ResolveRuntimeChannelDescriptor(IReadOnlyList<string> prefixes, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string? suffix = null;
        string? resolvedFullPath = null;
        foreach (var prefix in prefixes)
        {
            suffix = TryGetPathSuffix(key, prefix);
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = TryGetPathSuffix(TargetPathHelper.NormalizeComparablePath(key), TargetPathHelper.NormalizeComparablePath(prefix));
            }

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                resolvedFullPath = TargetPathHelper.NormalizeConfiguredTargetPath(key);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(suffix) || string.IsNullOrWhiteSpace(resolvedFullPath))
        {
            return null;
        }

        var segments = TargetPathHelper.SplitPathSegments(suffix);
        if (segments.Count != 2)
        {
            return null;
        }

        var format = HostRegistries.Data.TryResolve(resolvedFullPath, out var runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("format")
            ? runtimeItem.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        var unit = HostRegistries.Data.TryResolve(resolvedFullPath, out runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("unit")
            ? runtimeItem.Properties["unit"].Value?.ToString() ?? string.Empty
            : string.Empty;
        var bitCount = GetBitCount(format);

        return new UdlRuntimeModuleChannelDescriptor
        {
            ModuleName = segments[0],
            ChannelName = segments[1],
            Format = format,
            Unit = unit,
            BitCount = bitCount
        };
    }

    private static string? TryGetPathSuffix(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        if (string.Equals(path, prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!path.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = path[prefix.Length..].TrimStart('/', '.', '\\');
        return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
    }

    private static string NormalizeClientName(FolderItemModel item)
        => string.IsNullOrWhiteSpace(item.Name) ? "UdlClientControl" : item.Name.Trim();

    private static int GetBitCount(string? format)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(format)
            ? string.Empty
            : format.Trim().Split(':', 2, StringSplitOptions.TrimEntries)[0].ToLowerInvariant();

        return normalizedKind switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private sealed class DialogViewModel : NotifyBase
    {
        private UdlModuleExposureEditorRow? _selectedRow;
        private string _errorMessage = string.Empty;

        public DialogViewModel(
            MainWindowViewModel ownerViewModel,
            IReadOnlyList<UdlModuleExposureDefinition> definitions,
            IReadOnlyList<UdlRuntimeModuleChannelDescriptor> runtimeChannels,
            string? moduleName)
        {
            DialogBackground = ownerViewModel.DialogBackground;
            PrimaryTextBrush = ownerViewModel.PrimaryTextBrush;
            SecondaryTextBrush = ownerViewModel.SecondaryTextBrush;
            BorderColor = ownerViewModel.CardBorderBrush;
            SectionBackground = ownerViewModel.CardBackground;
            EditorBackground = ownerViewModel.EditPanelInputBackground;
            EditorForeground = ownerViewModel.EditPanelInputForeground;
            ButtonBackground = ownerViewModel.EditPanelButtonBackground;
            ButtonBorderBrush = ownerViewModel.EditPanelButtonBorderBrush;
            ButtonForeground = ownerViewModel.PrimaryTextBrush;
            TitleText = string.IsNullOrWhiteSpace(moduleName) ? "UDL Module Exposures" : $"UDL Module Exposures - {moduleName}";
            DescriptionText = string.IsNullOrWhiteSpace(moduleName)
                ? "Configure UdlClient-owned helper items per module and channel. Bit helper items are useful for bitmask channels and stay out of the generic Signal widget."
                : $"Configure UdlClient-owned helper items for module '{moduleName}'.";
            IsModuleScopedView = !string.IsNullOrWhiteSpace(moduleName);
            IsGenericChannelView = !IsModuleScopedView;

            Rows = new ObservableCollection<UdlModuleExposureEditorRow>(BuildRows(definitions, runtimeChannels));
            SelectedRow = Rows.FirstOrDefault();
            ModuleCard = BuildModuleCard(moduleName, Rows);
            BitmaskRows = new ObservableCollection<UdlModuleExposureBitmaskRow>(BuildBitmaskRows(Rows));
            SettingsRows = new ObservableCollection<UdlModuleExposurePreparedFieldRow>(BuildSettingsRows());
            AdjustRows = new ObservableCollection<UdlModuleExposureAdjustRow>(BuildAdjustRows());
        }

        public string TitleText { get; }

        public string DescriptionText { get; }

        public bool IsModuleScopedView { get; }

        public bool IsGenericChannelView { get; }

        public ObservableCollection<UdlModuleExposureEditorRow> Rows { get; }

        public ModuleCardViewModel ModuleCard { get; }

        public ObservableCollection<UdlModuleExposureBitmaskRow> BitmaskRows { get; }

        public ObservableCollection<UdlModuleExposurePreparedFieldRow> SettingsRows { get; }

        public ObservableCollection<UdlModuleExposureAdjustRow> AdjustRows { get; }

        public bool ReadInputRouteToSetRequest
        {
            get => GetReadRouteRows().Any(static row => row.RouteReadInputToSetRequest);
            set
            {
                var changed = false;
                foreach (var row in GetReadRouteRows())
                {
                    if (row.RouteReadInputToSetRequest == value)
                    {
                        continue;
                    }

                    row.RouteReadInputToSetRequest = value;
                    changed = true;
                }

                if (changed)
                {
                    OnPropertyChanged();
                }
            }
        }

        public UdlModuleExposureEditorRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRow));
                    OnPropertyChanged(nameof(ShowEmptyState));
                }
            }
        }

        public bool HasSelectedRow => SelectedRow is not null;

        public bool ShowEmptyState => Rows.Count == 0;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public object DialogBackground { get; }

        public object PrimaryTextBrush { get; }

        public object SecondaryTextBrush { get; }

        public object BorderColor { get; }

        public object SectionBackground { get; }

        public object EditorBackground { get; }

        public object EditorForeground { get; }

        public object ButtonBackground { get; }

        public object ButtonBorderBrush { get; }

        public object ButtonForeground { get; }

        public IReadOnlyList<string> FormatOptions => ParameterFormatOptions;

        public bool TryBuildResult(out string result)
        {
            ErrorMessage = string.Empty;

            foreach (var row in Rows)
            {
                if (row.ExposeBits && row.EffectiveBitCount <= 0)
                {
                    result = string.Empty;
                    ErrorMessage = $"{row.DisplayName}: bit count must be greater than zero when Publish Bits is enabled.";
                    return false;
                }
            }

            result = UdlModuleExposureDefinitionCodec.SerializeDefinitions(Rows.Select(static row => row.ToDefinition()));
            return true;
        }

        private static IReadOnlyList<UdlModuleExposureEditorRow> BuildRows(
            IReadOnlyList<UdlModuleExposureDefinition> definitions,
            IReadOnlyList<UdlRuntimeModuleChannelDescriptor> runtimeChannels)
        {
            var rows = new Dictionary<string, UdlModuleExposureEditorRow>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var runtime in runtimeChannels)
            {
                var row = new UdlModuleExposureEditorRow(
                    moduleName: runtime.ModuleName,
                    channelName: runtime.ChannelName,
                    format: runtime.Format,
                    unit: runtime.Unit,
                    bitCount: runtime.BitCount,
                    exposeBits: false,
                    bitLabels: string.Empty);
                rows[row.Key] = row;
            }

            foreach (var definition in definitions)
            {
                var key = UdlModuleExposureEditorRow.BuildKey(definition.ModuleName, definition.ChannelName);
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new UdlModuleExposureEditorRow(
                        moduleName: definition.ModuleName,
                        channelName: definition.ChannelName,
                        format: definition.Format,
                        unit: definition.Unit,
                        bitCount: definition.BitCount,
                        exposeBits: definition.ExposeBits,
                        bitLabels: definition.BitLabels);
                    rows[key] = row;
                    continue;
                }

                row.Format = string.IsNullOrWhiteSpace(definition.Format) ? row.Format : definition.Format;
                row.Unit = string.IsNullOrWhiteSpace(definition.Unit) ? row.Unit : definition.Unit;
                row.BitCount = definition.BitCount > 0 ? definition.BitCount : row.BitCount;
                row.ExposeBits = definition.ExposeBits;
                row.RouteReadInputToSetRequest = definition.RouteReadInputToSetRequest;
                row.BitLabels = definition.BitLabels;
            }

            foreach (var row in rows.Values)
            {
                if (row.BitCount <= 0)
                {
                    row.BitCount = GetSuggestedBitCount(row.ChannelName);
                }
            }

            return rows.Values
                .OrderBy(static row => row.ModuleName, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.ChannelName, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private IEnumerable<UdlModuleExposureEditorRow> GetReadRouteRows()
        {
            var readRows = Rows.Where(static row => string.Equals(row.ChannelName, "Read", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (readRows.Length > 0)
            {
                return readRows;
            }

            return Rows.Where(static row => string.Equals(row.ChannelName, "Set", StringComparison.OrdinalIgnoreCase));
        }

        private static int GetSuggestedBitCount(string? channelName)
        {
            return channelName?.Trim().ToLowerInvariant() switch
            {
                "read" => 4,
                "set" => 4,
                "cmd" => 4,
                "command" => 4,
                "state" => 4,
                "alert" => 4,
                _ => 0
            };
        }

        private static ModuleCardViewModel BuildModuleCard(string? moduleName, IEnumerable<UdlModuleExposureEditorRow> rows)
        {
            var effectiveModuleName = !string.IsNullOrWhiteSpace(moduleName)
                ? moduleName.Trim()
                : rows.Select(static row => row.ModuleName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

            return new ModuleCardViewModel(
                moduleName: effectiveModuleName,
                type: "Module",
                serialNumber: "Module",
                version: "Module");
        }

        private static IReadOnlyList<UdlModuleExposureBitmaskRow> BuildBitmaskRows(IEnumerable<UdlModuleExposureEditorRow> rows)
        {
            var lookup = rows.ToDictionary(static row => row.ChannelName, System.StringComparer.OrdinalIgnoreCase);
            var result = new List<UdlModuleExposureBitmaskRow>();
            var consumedChannels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            AddBitmaskRow(result, lookup, consumedChannels, "Read / Set", ["Read", "Set"]);
            AddBitmaskRow(result, lookup, consumedChannels, "Alert", ["Alert"]);

            foreach (var row in rows.Where(static row => row.SupportsBitExposure)
                         .Where(static row => !string.Equals(row.ChannelName, "Cmd", System.StringComparison.OrdinalIgnoreCase)
                                       && !string.Equals(row.ChannelName, "Command", System.StringComparison.OrdinalIgnoreCase)
                                       && !string.Equals(row.ChannelName, "State", System.StringComparison.OrdinalIgnoreCase))
                         .Where(row => !consumedChannels.Contains(row.ChannelName)))
            {
                result.Add(new UdlModuleExposureBitmaskRow(row.ChannelName, [row]));
            }

            return result;
        }

        private static void AddBitmaskRow(List<UdlModuleExposureBitmaskRow> target, Dictionary<string, UdlModuleExposureEditorRow> lookup, HashSet<string> consumedChannels, string label, string[] aliases)
        {
            var mappedRows = aliases
                .Where(alias => lookup.TryGetValue(alias, out _))
                .Select(alias => lookup[alias])
                .ToArray();

            if (mappedRows.Length == 0)
            {
                return;
            }

            target.Add(new UdlModuleExposureBitmaskRow(label, mappedRows));

            foreach (var row in mappedRows)
            {
                consumedChannels.Add(row.ChannelName);
            }
        }

        private static IReadOnlyList<UdlModuleExposurePreparedFieldRow> BuildSettingsRows()
        {
            string[] labels = ["Set", "State", "Mode", "SetMin", "SetMax", "OutMin", "OutMax", "AID", "MSN", "ST"];
            return labels.Select(static label => new UdlModuleExposurePreparedFieldRow(label, "<double>", "<double>")).ToArray();
        }

        private static IReadOnlyList<UdlModuleExposureAdjustRow> BuildAdjustRows()
        {
            return Enumerable.Range(1, 10)
                .Select(index => new UdlModuleExposureAdjustRow(string.Empty, "<double>", "<double>"))
                .ToArray();
        }
    }
}

public sealed class ModuleCardViewModel
{
    public ModuleCardViewModel(string moduleName, string type, string serialNumber, string version)
    {
        ModuleName = moduleName;
        Type = type;
        SerialNumber = serialNumber;
        Version = version;
    }

    public string ModuleName { get; }

    public string Type { get; }

    public string SerialNumber { get; }

    public string Version { get; }
}

public sealed class UdlModuleExposureBitmaskRow : NotifyBase
{
    private readonly IReadOnlyList<UdlModuleExposureEditorRow> _rows;

    public UdlModuleExposureBitmaskRow(string label, IReadOnlyList<UdlModuleExposureEditorRow> rows)
    {
        Label = label;
        _rows = rows;

        foreach (var row in _rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    public string Label { get; }

    public bool IsEditable => _rows.Count > 0;

    public bool ExposeBits
    {
        get => _rows.Any(static row => row.ExposeBits);
        set
        {
            foreach (var row in _rows)
            {
                row.ExposeBits = value;
            }

            OnPropertyChanged();
        }
    }

    public string BitCountText
    {
        get => ResolveBitCount().ToString();
        set
        {
            var nextCount = UdlModuleExposureEditorRow.ParseBitCount(value);
            foreach (var row in _rows)
            {
                row.BitCount = nextCount;
            }

            OnPropertyChanged();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UdlModuleExposureEditorRow.ExposeBits)
            || e.PropertyName == nameof(UdlModuleExposureEditorRow.Format)
            || e.PropertyName == nameof(UdlModuleExposureEditorRow.SelectedFormatKind)
            || e.PropertyName == nameof(UdlModuleExposureEditorRow.BitCount)
            || e.PropertyName == nameof(UdlModuleExposureEditorRow.EffectiveBitCount))
        {
            OnPropertyChanged(nameof(ExposeBits));
            OnPropertyChanged(nameof(BitCountText));
        }
    }

    private int ResolveBitCount()
    {
        return _rows.Select(static row => row.EffectiveBitCount).FirstOrDefault(static count => count > 0);
    }
}

public sealed class UdlModuleExposurePreparedFieldRow
{
    public UdlModuleExposurePreparedFieldRow(string label, string valuePlaceholder, string infoPlaceholder)
    {
        Label = label;
        ValuePlaceholder = valuePlaceholder;
        InfoPlaceholder = infoPlaceholder;
    }

    public string Label { get; }

    public string ValuePlaceholder { get; }

    public string InfoPlaceholder { get; }
}

public sealed class UdlModuleExposureAdjustRow
{
    public UdlModuleExposureAdjustRow(string label, string xPlaceholder, string yPlaceholder)
    {
        Label = label;
        XPlaceholder = xPlaceholder;
        YPlaceholder = yPlaceholder;
    }

    public string Label { get; }

    public string XPlaceholder { get; }

    public string YPlaceholder { get; }
}

public sealed class UdlRuntimeModuleChannelDescriptor
{
    public required string ModuleName { get; init; }

    public required string ChannelName { get; init; }

    public string Format { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public int BitCount { get; init; }
}

public sealed class UdlModuleExposureEditorRow : NotifyBase
{
    private string _format;
    private string _unit;
    private int _bitCount;
    private bool _exposeBits;
    private bool _routeReadInputToSetRequest;
    private string _bitLabels;

    public UdlModuleExposureEditorRow(string moduleName, string channelName, string format, string unit, int bitCount, bool exposeBits, string bitLabels)
    {
        ModuleName = moduleName?.Trim() ?? string.Empty;
        ChannelName = channelName?.Trim() ?? string.Empty;
        _format = format?.Trim() ?? string.Empty;
        _unit = unit?.Trim() ?? string.Empty;
        _bitCount = bitCount > 0 ? bitCount : GetBitCountFromFormat(format);
        _exposeBits = exposeBits;
        _bitLabels = bitLabels?.Trim() ?? string.Empty;
    }

    public string Key => BuildKey(ModuleName, ChannelName);

    public string ModuleName { get; }

    public string ChannelName { get; }

    public string DisplayName => $"{ModuleName}.{ChannelName}";

    public string Summary => ExposeBits
        ? $"Count {EffectiveBitCount} | Unit {EffectiveUnit} | bit helpers active"
        : $"Count {EffectiveBitCount} | Unit {EffectiveUnit} | no helper items";

    public string EffectiveFormat => string.IsNullOrWhiteSpace(Format) ? "<empty>" : Format;

    public string EffectiveUnit => string.IsNullOrWhiteSpace(Unit) ? "<empty>" : Unit;

    public int EffectiveBitCount => BitCount > 0 ? BitCount : GetBitCountFromFormat(Format);

    public string Format
    {
        get => _format;
        set
        {
            if (SetProperty(ref _format, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(SelectedFormatKind));
                OnPropertyChanged(nameof(FormatProperty));
                OnPropertyChanged(nameof(FormatPropertyToolTip));
                OnPropertyChanged(nameof(SupportsBitExposure));
                OnPropertyChanged(nameof(BitCount));
                OnPropertyChanged(nameof(EffectiveBitCount));
                OnPropertyChanged(nameof(ShowBitLabelsEditor));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(EffectiveFormat));
            }
        }
    }

    public string SelectedFormatKind
    {
        get => SplitPropertyFormat(Format).Kind;
        set
        {
            var current = SplitPropertyFormat(Format);
            Format = ComposePropertyFormat(value, current.Property);
        }
    }

    public string FormatProperty
    {
        get => SplitPropertyFormat(Format).Property;
        set
        {
            var current = SplitPropertyFormat(Format);
            Format = ComposePropertyFormat(current.Kind, value);
        }
    }

    public string FormatPropertyToolTip => GetFormatPropertyToolTip(SelectedFormatKind);

    public int BitCount
    {
        get => _bitCount;
        set
        {
            var normalizedValue = NormalizeBitCount(value);
            if (SetProperty(ref _bitCount, normalizedValue))
            {
                OnPropertyChanged(nameof(EffectiveBitCount));
                OnPropertyChanged(nameof(SupportsBitExposure));
                OnPropertyChanged(nameof(ShowBitLabelsEditor));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetProperty(ref _unit, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(EffectiveUnit));
            }
        }
    }

    public bool SupportsBitExposure => EffectiveBitCount > 0;

    public bool ExposeBits
    {
        get => _exposeBits;
        set
        {
            if (SetProperty(ref _exposeBits, value))
            {
                OnPropertyChanged(nameof(ShowBitLabelsEditor));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool RouteReadInputToSetRequest
    {
        get => _routeReadInputToSetRequest;
        set => SetProperty(ref _routeReadInputToSetRequest, value);
    }

    public bool ShowBitLabelsEditor => ExposeBits;

    public string BitLabels
    {
        get => _bitLabels;
        set => SetProperty(ref _bitLabels, value?.Trim() ?? string.Empty);
    }

    public UdlModuleExposureDefinition ToDefinition()
    {
        return new UdlModuleExposureDefinition
        {
            ModuleName = ModuleName,
            ChannelName = ChannelName,
            Format = Format,
            Unit = Unit,
            ExposeBits = ExposeBits,
            BitCount = EffectiveBitCount,
            RouteReadInputToSetRequest = RouteReadInputToSetRequest,
            BitLabels = BitLabels
        };
    }

    public static int ParseBitCount(string? text)
    {
        return int.TryParse(text?.Trim(), out var parsedValue)
            ? NormalizeBitCount(parsedValue)
            : 0;
    }

    public static string BuildKey(string moduleName, string channelName)
        => $"{moduleName?.Trim() ?? string.Empty}|{channelName?.Trim() ?? string.Empty}";

    private static bool IsBitFormat(string format)
    {
        return string.Equals(format?.Trim(), "b4", StringComparison.OrdinalIgnoreCase)
               || string.Equals(format?.Trim(), "b8", StringComparison.OrdinalIgnoreCase)
               || string.Equals(format?.Trim(), "b16", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetBitCountFromFormat(string? format)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(format)
            ? string.Empty
            : format.Trim().Split(':', 2, StringSplitOptions.TrimEntries)[0].ToLowerInvariant();

        return normalizedKind switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private static int NormalizeBitCount(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return Math.Clamp(value, 1, 32);
    }

    private static (string Kind, string Property) SplitPropertyFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return ("Text", string.Empty);
        }

        var trimmed = format.Trim();
        if (trimmed.StartsWith("numeric:", StringComparison.OrdinalIgnoreCase))
        {
            return ("Numeric", trimmed[8..].Trim());
        }

        if (string.Equals(trimmed, "numeric", StringComparison.OrdinalIgnoreCase))
        {
            return ("Numeric", "0.##");
        }

        if (trimmed.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
        {
            return ("Hex", trimmed[4..].Trim());
        }

        if (string.Equals(trimmed, "hex", StringComparison.OrdinalIgnoreCase))
        {
            return ("Hex", string.Empty);
        }

        if (trimmed.StartsWith("EpochToDatetime:", StringComparison.OrdinalIgnoreCase))
        {
            var epochProperty = trimmed[16..].Trim();
            return ("EpochToDatetime", string.IsNullOrWhiteSpace(epochProperty) ? "UtcDefault" : epochProperty);
        }

        if (string.Equals(trimmed, "EpochToDatetime", StringComparison.OrdinalIgnoreCase))
        {
            return ("EpochToDatetime", "UtcDefault");
        }

        if (trimmed.StartsWith("h", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 2)
        {
            return ("Hex", trimmed[1..]);
        }

        if (trimmed.StartsWith("D", StringComparison.Ordinal) && trimmed.Length >= 2 && int.TryParse(trimmed[1..], out var decPrecision))
        {
            return ("Numeric", new string('0', decPrecision));
        }

        if (trimmed.StartsWith("F", StringComparison.Ordinal) && trimmed.Length >= 2 && int.TryParse(trimmed[1..], out var floatPrecision))
        {
            var decimals = new string('0', floatPrecision);
            return ("Numeric", floatPrecision > 0 ? $"0.{decimals}" : "0");
        }

        var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries);
        var kind = string.IsNullOrWhiteSpace(parts[0]) ? "Text" : parts[0];
        var property = parts.Length > 1 ? parts[1] : string.Empty;
        return (kind, property);
    }

    private static string ComposePropertyFormat(string? kind, string? property)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? "Text" : kind.Trim();
        var normalizedProperty = string.IsNullOrWhiteSpace(property) ? string.Empty : property.Trim();

        if (string.Equals(normalizedKind, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (string.Equals(normalizedKind, "Numeric", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(normalizedProperty) ? "numeric" : $"numeric:{normalizedProperty}";
        }

        if (string.Equals(normalizedKind, "Hex", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(normalizedProperty) ? "hex" : $"hex:{normalizedProperty}";
        }

        if (string.Equals(normalizedKind, "EpochToDatetime", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(normalizedProperty) || string.Equals(normalizedProperty, "UtcDefault", StringComparison.OrdinalIgnoreCase)
                ? "EpochToDatetime"
                : $"EpochToDatetime:{normalizedProperty}";
        }

        return FormatUsesParameter(normalizedKind) && !string.IsNullOrWhiteSpace(normalizedProperty)
            ? $"{normalizedKind}:{normalizedProperty}"
            : normalizedKind;
    }

    private static bool FormatUsesParameter(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        return string.Equals(kind, "Numeric", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "Hex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "bool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "EpochToDatetime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "b4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "b8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "b16", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFormatPropertyToolTip(string? kind)
    {
        if (string.Equals(kind, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional labels for true,false. Example: ON,OFF or toggle:On,Off for a single toggle button.";
        }

        if (string.Equals(kind, "b4", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional labels for 4 bits. Example: DI1,DI2,Alert,4";
        }

        if (string.Equals(kind, "b8", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional labels for 8 bits. Example: DI1,DI2,Alert,4,5,6,7,8";
        }

        if (string.Equals(kind, "b16", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional labels for 16 bits. Example: 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16";
        }

        if (string.Equals(kind, "Numeric", StringComparison.OrdinalIgnoreCase))
        {
            return "Number format pattern. Example: 0 | 0.00 | 000.000";
        }

        if (string.Equals(kind, "Hex", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional digit count without 0x. Example: 2 | 4 | 8";
        }

        if (string.Equals(kind, "EpochToDatetime", StringComparison.OrdinalIgnoreCase))
        {
            return "Optional date/time format. Empty or UtcDefault uses ISO-8601 with offset. Example: yyyy-MM-dd HH:mm:ss.fff";
        }

        if (string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return "No extra property. The raw value is shown directly.";
        }

        return "Optional property for the selected format.";
    }
}

public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
