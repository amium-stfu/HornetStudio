using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amium.Items;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host;
using HornetStudio.Logging;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.Widgets;

public partial class MonitorControl : EditorTemplateControl
{
    private static readonly JsonSerializerOptions AggregateMetaJsonOptions = new();

    public static readonly DirectProperty<MonitorControl, bool> HasNoRulesProperty =
        AvaloniaProperty.RegisterDirect<MonitorControl, bool>(nameof(HasNoRules), control => control.HasNoRules);

    public static readonly StyledProperty<MainWindowViewModel?> EditorViewModelProperty =
        AvaloniaProperty.Register<MonitorControl, MainWindowViewModel?>(nameof(EditorViewModel));

    private FolderItemModel? _observedItem;
    private bool _hasNoRules = true;
    private int _suppressObservedItemRebuild;

    public MonitorControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        Rules.CollectionChanged += OnRulesCollectionChanged;
    }

    public ObservableCollection<MonitorRuleRow> Rules { get; } = new();

    public ObservableCollection<MonitorRuleRow> DisplayRules { get; } = new();

    public bool HasNoRules
    {
        get => _hasNoRules;
        private set => SetAndRaise(HasNoRulesProperty, ref _hasNoRules, value);
    }

    public MainWindowViewModel? EditorViewModel
    {
        get => GetValue(EditorViewModelProperty);
        set => SetValue(EditorViewModelProperty, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel => EditorViewModel ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorViewModelProperty)
        {
            RebuildRules();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RebuildRules();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DisposeRules();
        Rules.Clear();
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RebuildRules();
    }

    private void HookObservedItem()
    {
        var item = Item;
        if (ReferenceEquals(_observedItem, item))
        {
            return;
        }

        UnhookObservedItem();
        if (!IsMonitorItem(item))
        {
            return;
        }

        _observedItem = item;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
        }
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is null)
        {
            return;
        }

        _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        _observedItem = null;
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.MonitorDefinitions)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.Path)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            if (_suppressObservedItemRebuild == 0)
            {
                RebuildRules();
            }

            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            foreach (var row in Rules)
            {
                row.RefreshTheme();
            }
        }
    }

    private void RebuildRules()
    {
        var item = _observedItem;
        DisposeRules();
        Rules.Clear();

        if (item is null)
        {
            UpdateFooter();
            return;
        }

        var definitions = GetDefinitions(item);
        if (string.IsNullOrWhiteSpace(item.FolderName))
        {
            foreach (var definition in definitions)
            {
                Rules.Add(new MonitorRuleRow(item, definition, UpdateFooter));
            }
        }
        else
        {
            var runtimes = MonitorRuntimeManager.SyncDefinitions(
                item.FolderName,
                definitions,
                forceRecreate: false);
            foreach (var runtime in runtimes)
            {
                Rules.Add(new MonitorRuleRow(item, runtime, UpdateFooter));
            }
        }

        UpdateFooter();
    }

    private void DisposeRules()
    {
        foreach (var row in Rules)
        {
            row.Dispose();
        }
    }

    private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasNoRules = Rules.Count == 0;
        RefreshDisplayRules();
    }

    private void RefreshDisplayRules()
    {
        var orderedRules = Rules
            .OrderBy(static row => row.SeveritySortOrder)
            .ThenBy(static row => row.Definition.EventId)
            .ThenBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DisplayRules.Clear();
        foreach (var row in orderedRules)
        {
            DisplayRules.Add(row);
        }
    }

    public void RefreshCatalog()
    {
        RebuildRules();
    }

    private void UpdateFooter()
    {
        var item = _observedItem;
        if (item is null)
        {
            return;
        }

        var activeCount = Rules.Count(rule => rule.IsActive);
        item.Footer = Rules.Count == 0
            ? "No monitor rules configured"
            : $"{Rules.Count} monitor rule{(Rules.Count == 1 ? string.Empty : "s")}, {activeCount} active";
    }

    private void UpdateFooterAndAggregates()
    {
        UpdateFooter();
    }

    private void PublishAggregateRuntime()
    {
        PublishAggregateRuntime(_observedItem, Rules);
    }

    private static bool PublishAggregateRuntime(FolderItemModel? item, IEnumerable<MonitorRuleRow> rules)
    {
        if (item is not { Kind: ControlKind.Monitor })
        {
            return false;
        }

        var runtimePath = MonitorRuleRow.BuildMonitorRegistryPath(item.FolderName, item.Name);
        if (string.IsNullOrWhiteSpace(runtimePath))
        {
            return false;
        }

        var segments = TargetPathHelper.SplitPathSegments(runtimePath);
        if (segments.Count == 0)
        {
            return false;
        }

        var ruleList = rules.ToArray();
        var nameSegment = segments[^1];
        var parentPath = segments.Count > 1 ? string.Join('.', segments.Take(segments.Count - 1)) : string.Empty;
        var snapshot = string.IsNullOrWhiteSpace(parentPath)
            ? new ItemModel(nameSegment, ruleList.Any(rule => rule.IsActive))
            : new ItemModel(nameSegment, ruleList.Any(rule => rule.IsActive), parentPath);

        snapshot.Properties["path"].Value = runtimePath;
        snapshot.Properties["kind"].Value = "MonitorAggregate";
        snapshot.Properties["text"].Value = item.Name;
        snapshot.Properties["title"].Value = item.Name;
        snapshot["active"].Value = ruleList.Any(rule => rule.IsActive);
        snapshot["active"].Properties["text"].Value = "Active";
        snapshot["active_count"].Value = ruleList.Count(rule => rule.IsActive);
        snapshot["active_count"].Properties["text"].Value = "ActiveCount";

        foreach (var aggregate in BuildActiveEventIdAggregates(ruleList))
        {
            snapshot[aggregate.ItemName].Value = aggregate.EventIds;
            snapshot[aggregate.ItemName].Properties["text"].Value = aggregate.ItemName;
            snapshot[aggregate.ItemName].Properties["meta"].Value = aggregate.MetaJson;
        }

        HostRegistries.Data.UpsertSnapshot(runtimePath, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
        return true;
    }

    public static IReadOnlyList<MonitorAggregateItem> BuildActiveEventIdAggregates(IEnumerable<MonitorRuleRow> rules)
    {
        var result = new List<MonitorAggregateItem>();
        foreach (var level in Enum.GetValues<MonitorLogLevel>())
        {
            var itemName = $"{TargetPathHelper.NormalizePathSegment(level.ToString(), level.ToString().ToLowerInvariant())}_active";
            var events = rules
                .Where(rule => rule.IsActive && rule.Definition.LogLevel == level)
                .OrderBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
                .Select(rule => new MonitorAggregateEvent(rule.Definition.EventId, rule.Definition.EventText ?? string.Empty))
                .ToArray();
            result.Add(new MonitorAggregateItem(
                itemName,
                string.Join(',', events.Select(static entry => entry.EventId.ToString(CultureInfo.InvariantCulture))),
                JsonSerializer.Serialize(new MonitorAggregateMeta(events), AggregateMetaJsonOptions)));
        }

        return result;
    }

    public sealed record MonitorAggregateItem(string ItemName, string EventIds, string MetaJson);

    private sealed record MonitorAggregateMeta([property: JsonPropertyName("events")] IReadOnlyList<MonitorAggregateEvent> Events);

    private sealed record MonitorAggregateEvent(
        [property: JsonPropertyName("event_id")] int EventId,
        [property: JsonPropertyName("text")] string Text);

    private static bool IsMonitorItem(FolderItemModel? item)
        => item?.Kind == ControlKind.Monitor;

    private IReadOnlyList<MonitorDefinition> GetDefinitions(FolderItemModel item)
        => ResolveDefinitions(item, ViewModel);

    internal static IReadOnlyList<MonitorDefinition> ResolveDefinitions(FolderItemModel item, MainWindowViewModel? viewModel)
    {
        if (IsMonitorBrowserOwner(item))
        {
            if (viewModel is not null)
            {
                var registryDefinitions = viewModel.GetMonitorRegistryEntries(item)
                    .Select(static entry => entry.Definition.Clone())
                    .ToArray();
                if (registryDefinitions.Length > 0)
                {
                    return registryDefinitions;
                }
            }

            if (TryLoadCentralDefinitions(item, out var centralDefinitions))
            {
                return centralDefinitions;
            }
        }

        return MonitorDefinitionCodec.ParseDefinitions(item.MonitorDefinitions);
    }

    internal static MonitorRuntimeSettings ResolveRuntimeSettings(FolderItemModel item)
    {
        if (!TryLoadCentralDocument(item, out var document))
        {
            return new MonitorRuntimeSettings();
        }

        return new MonitorRuntimeSettings
        {
            StartupDelayMs = Math.Max(0, document.Runtime.StartupDelayMs)
        };
    }

    private static bool IsMonitorBrowserOwner(FolderItemModel item)
        => item.Kind == ControlKind.Monitor
           && string.Equals(item.Name, "MonitorBrowser", StringComparison.OrdinalIgnoreCase);

    private static bool TryLoadCentralDefinitions(FolderItemModel item, out IReadOnlyList<MonitorDefinition> definitions)
    {
        definitions = [];
        if (!TryLoadCentralDocument(item, out var document))
        {
            return false;
        }

        definitions = MonitorDefinitionCodec.FromDocuments(document.Rules, item.FolderName)
            .Length == 0
            ? []
            : MonitorDefinitionCodec.ParseDefinitions(MonitorDefinitionCodec.FromDocuments(document.Rules, item.FolderName))
                .Select(static definition => definition.Clone())
                .ToArray();
        return definitions.Count > 0;
    }

    private static bool TryLoadCentralDocument(FolderItemModel item, out MonitorDefinitionFileDocument document)
    {
        document = new MonitorDefinitionFileDocument();
        if (string.IsNullOrWhiteSpace(item.FolderLayoutPath)
            || string.IsNullOrWhiteSpace(item.FolderName))
        {
            return false;
        }

        var folderDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(item.FolderLayoutPath));
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            return false;
        }

        var monitorFilePath = MonitorDefinitionFileCodec.GetMonitorFilePath(folderDirectory);
        if (!System.IO.File.Exists(monitorFilePath))
        {
            return false;
        }

        var codec = new MonitorDefinitionFileCodec();
        document = codec.LoadDocument(folderDirectory, item.FolderName);
        return true;
    }

    private void ApplyDefinitions(FolderItemModel ownerItem, IReadOnlyList<MonitorDefinition> definitions, bool queuePersist)
    {
        if (IsMonitorBrowserOwner(ownerItem) && ViewModel is { } mainWindowViewModel)
        {
            mainWindowViewModel.TrySaveMonitorRegistryDefinitions(ownerItem, definitions);
            RebuildRules();
            return;
        }

        _suppressObservedItemRebuild++;
        try
        {
            ownerItem.MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(definitions);
        }
        finally
        {
            _suppressObservedItemRebuild--;
        }

        RebuildRules();
        if (queuePersist && ViewModel is { } viewModel)
        {
            if (!viewModel.TrySaveOwningPageYaml(ownerItem, out _))
            {
                viewModel.QueueSaveOwningPageYaml(ownerItem);
            }
        }
    }

    private async void OnAddRuleClicked(object? sender, RoutedEventArgs e)
    {
        var ownerItem = ResolveActionOwnerItem(_observedItem, Item);
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var definition = await MonitorEditorDialogWindow.ShowAsync(owner, ViewModel, ownerItem, null, MainWindowViewModel.EnumerateSignalSourceOptions(), GetProcessLogTargetOptions());
        if (definition is null)
        {
            return;
        }

        var definitions = ResolveEditableDefinitions(ownerItem).ToList();
        definitions.Add(definition);
        ApplyDefinitions(ownerItem, definitions, queuePersist: true);
        e.Handled = true;
    }

    private async void OnEditRuleClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetRuleRowFromActionSource(sender, out var row))
        {
            return;
        }

        var ownerItem = ResolveActionOwnerItem(_observedItem, Item);
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var definitions = ResolveEditableDefinitions(ownerItem).ToList();
        var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var updated = await MonitorEditorDialogWindow.ShowAsync(owner, ViewModel, ownerItem, definitions[index], MainWindowViewModel.EnumerateSignalSourceOptions(), GetProcessLogTargetOptions());
        if (updated is null)
        {
            return;
        }

        definitions[index] = updated;
        ApplyDefinitions(ownerItem, definitions, queuePersist: true);
        e.Handled = true;
    }

    private async void OnDeleteRuleClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryGetRuleRowFromActionSource(sender, out var row))
        {
            return;
        }

        var ownerItem = ResolveActionOwnerItem(_observedItem, Item);
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, $"Delete monitor rule '{row.Name}'?", "The monitor rule definition will be removed.", confirmText: "Delete", cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var definitions = ResolveEditableDefinitions(ownerItem)
            .Where(definition => !string.Equals(definition.Name, row.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ApplyDefinitions(ownerItem, definitions, queuePersist: true);
        e.Handled = true;
    }

    private void OnRuleActionsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.DataContext = button.Tag ?? button.DataContext;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Open();
        e.Handled = true;
    }

    private static bool TryGetRuleRowFromActionSource(object? source, out MonitorRuleRow row)
    {
        row = source switch
        {
            Button { CommandParameter: MonitorRuleRow commandRow } => commandRow,
            Button { Tag: MonitorRuleRow tagRow } => tagRow,
            Button { DataContext: MonitorRuleRow contextRow } => contextRow,
            MenuItem { CommandParameter: MonitorRuleRow commandRow } => commandRow,
            MenuItem { Tag: MonitorRuleRow tagRow } => tagRow,
            MenuItem { DataContext: MonitorRuleRow contextRow } => contextRow,
            _ => null!
        };

        return row is not null;
    }

    private static FolderItemModel? ResolveActionOwnerItem(FolderItemModel? observedItem, FolderItemModel? currentItem)
        => observedItem ?? currentItem;

    private static IEnumerable<string> GetProcessLogTargetOptions()
    {
        return HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display)
            .Select(key => HostRegistries.Data.TryGet(key, out var item) ? (Key: key, Item: item) : (Key: (string?)null, Item: null))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Item?.Value is ProcessLog)
            .Select(entry => entry.Key!)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MonitorDefinition> ResolveEditableDefinitions(FolderItemModel ownerItem)
    {
        if (IsMonitorBrowserOwner(ownerItem) && TryLoadCentralDefinitions(ownerItem, out var centralDefinitions))
        {
            return centralDefinitions.Select(static definition => definition.Clone()).ToArray();
        }

        return MonitorDefinitionCodec.ParseDefinitions(ownerItem.MonitorDefinitions)
            .Select(static definition => definition.Clone())
            .ToArray();
    }
}

/// <summary>
/// Presents one monitor runtime inside the Monitor browser UI.
/// </summary>
public sealed class MonitorRuleRow : ObservableObject, IDisposable
{
    private readonly FolderItemModel _ownerItem;
    private readonly MonitorRuleRuntime _runtime;
    private readonly Action? _stateChanged;
    private readonly bool _ownsRuntime;

    public MonitorRuleRow(FolderItemModel ownerItem, MonitorDefinition definition, Action stateChanged)
        : this(ownerItem, new MonitorRuleRuntime(ownerItem.FolderName, definition, stateChanged), stateChanged, ownsRuntime: true)
    {
    }

    internal MonitorRuleRow(FolderItemModel ownerItem, MonitorRuleRuntime runtime, Action? stateChanged = null)
        : this(ownerItem, runtime, stateChanged, ownsRuntime: false)
    {
    }

    private MonitorRuleRow(FolderItemModel ownerItem, MonitorRuleRuntime runtime, Action? stateChanged, bool ownsRuntime)
    {
        _ownerItem = ownerItem;
        _runtime = runtime;
        _stateChanged = stateChanged;
        _ownsRuntime = ownsRuntime;
        _runtime.StateChanged += OnRuntimeStateChanged;
    }

    public MonitorDefinition Definition => _runtime.Definition;

    public string Name => Definition.Name;

    public bool IsActive => _runtime.IsActive;

    public string EventIdText => Definition.EventId.ToString(CultureInfo.InvariantCulture);

    public string EventDisplayText => string.IsNullOrWhiteSpace(Definition.EventText) ? Name : Definition.EventText.Trim();

    public int SeveritySortOrder => GetSeveritySortOrder(Definition.LogLevel);

    public string SourceText => $"Source: {ValueOrPlaceholder(Definition.SourcePath)} | Mode: {Definition.Mode} | Every {Definition.RefreshRateMs} ms";

    public string StatusText => _runtime.StatusText;

    public string RowTooltip => $"{SourceText}{Environment.NewLine}{StatusText}";

    public string ActionTooltip => $"Rule actions: {Name}";

    public string RowBackground => IsActive
        ? GetActiveRowBackground()
        : _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => IsActive
        ? GetSeverityForeground()
        : _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
        RaisePropertyChanged(nameof(ActionTooltip));
    }

    public void Evaluate()
    {
        _runtime.Evaluate();
    }

    public void Dispose()
    {
        _runtime.StateChanged -= OnRuntimeStateChanged;
        if (_ownsRuntime)
        {
            _runtime.Dispose();
        }
    }

    public static string BuildRegistryPath(string? folderName, string? ownerName, string? ruleName)
    {
        return MonitorRegistry.BuildRulePath(folderName, ruleName);
    }

    public static string BuildMonitorRegistryPath(string? folderName, string? ownerName)
    {
        return MonitorRegistry.BuildAggregatePath(folderName);
    }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        void RaiseAll()
        {
            RaisePropertyChanged(nameof(IsActive));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(RowBackground));
            RaisePropertyChanged(nameof(RowBorderBrush));
            RaisePropertyChanged(nameof(RowTooltip));
            _stateChanged?.Invoke();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RaiseAll();
            return;
        }

        Dispatcher.UIThread.Post(RaiseAll);
    }

    private string GetActiveRowBackground()
    {
        var backgroundText = GetRowBlendBaseBackground();
        return TryBlendColors(
            backgroundText: backgroundText,
            accentText: GetSeverityForeground(),
            blendFactor: IsDarkColor(backgroundText) ? 0.26 : 0.32,
            blendedColorText: out var blendedColorText)
            ? blendedColorText
            : _ownerItem.EffectiveBodyBackground;
    }

    private string GetSeverityForeground()
    {
        var theme = IsDarkColor(GetRowBlendBaseBackground()) ? ThemePalette.Dark : ThemePalette.Light;
        return Definition.LogLevel switch
        {
            MonitorLogLevel.Debug => theme.LogDebugForeground,
            MonitorLogLevel.Info => theme.LogInfoForeground,
            MonitorLogLevel.Warning => theme.LogWarningForeground,
            MonitorLogLevel.Error => theme.LogErrorForeground,
            MonitorLogLevel.Fatal => theme.LogFatalForeground,
            _ => _ownerItem.EffectiveMutedForeground
        };
    }

    private string GetRowBlendBaseBackground()
    {
        if (TryGetOpaqueColorText(_ownerItem.EffectiveBodyBackground, out var bodyBackground))
        {
            return bodyBackground;
        }

        if (TryGetOpaqueColorText(_ownerItem.EffectiveInnerBackground, out var innerBackground))
        {
            return innerBackground;
        }

        return IsDarkColor(_ownerItem.EffectiveBackground)
            ? ThemePalette.Dark.CardBackground
            : ThemePalette.Light.CardBackground;
    }

    private static bool TryGetOpaqueColorText(string? colorText, out string opaqueColorText)
    {
        opaqueColorText = string.Empty;
        if (string.IsNullOrWhiteSpace(colorText)
            || !Color.TryParse(colorText, out var color)
            || color.A == byte.MinValue)
        {
            return false;
        }

        opaqueColorText = Color.FromArgb(byte.MaxValue, color.R, color.G, color.B).ToString();
        return true;
    }

    private static bool TryBlendColors(string? backgroundText, string? accentText, double blendFactor, out string blendedColorText)
    {
        blendedColorText = backgroundText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(backgroundText)
            || string.IsNullOrWhiteSpace(accentText)
            || !Color.TryParse(backgroundText, out var background)
            || !Color.TryParse(accentText, out var accent))
        {
            return false;
        }

        var clampedBlendFactor = Math.Clamp(blendFactor, 0d, 1d);
        var blended = Color.FromArgb(
            byte.MaxValue,
            BlendChannel(background.R, accent.R, clampedBlendFactor),
            BlendChannel(background.G, accent.G, clampedBlendFactor),
            BlendChannel(background.B, accent.B, clampedBlendFactor));

        blendedColorText = blended.ToString();
        return true;
    }

    private static byte BlendChannel(byte backgroundChannel, byte accentChannel, double blendFactor)
    {
        var blended = backgroundChannel + ((accentChannel - backgroundChannel) * blendFactor);
        return (byte)Math.Clamp((int)Math.Round(blended, MidpointRounding.AwayFromZero), byte.MinValue, byte.MaxValue);
    }

    private static int GetSeveritySortOrder(MonitorLogLevel level)
    {
        return level switch
        {
            MonitorLogLevel.Fatal => 0,
            MonitorLogLevel.Error => 1,
            MonitorLogLevel.Warning => 2,
            MonitorLogLevel.Info => 3,
            MonitorLogLevel.Debug => 4,
            _ => int.MaxValue
        };
    }

    private static bool IsDarkColor(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText) || !Color.TryParse(colorText, out var color))
        {
            return false;
        }

        var brightness = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return brightness < 140;
    }

    private static string ValueOrPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) ? "n/a" : value;
}
