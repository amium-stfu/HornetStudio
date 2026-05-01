using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EnhancedSignalsControl : EditorTemplateControl
{
    public static readonly DirectProperty<EnhancedSignalsControl, bool> HasNoSignalsProperty =
        AvaloniaProperty.RegisterDirect<EnhancedSignalsControl, bool>(nameof(HasNoSignals), control => control.HasNoSignals);

    private FolderItemModel? _observedItem;
    private bool _hasNoSignals = true;
    private int _runtimeRefreshQueued;
    private int _runtimeRebuildQueued;
    private int _suppressObservedItemRebuild;

    public EnhancedSignalsControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
        Signals.CollectionChanged += OnSignalsCollectionChanged;
    }

    public ObservableCollection<EnhancedSignalRow> Signals { get; } = [];

    public bool HasNoSignals
    {
        get => _hasNoSignals;
        private set => SetAndRaise(HasNoSignalsProperty, ref _hasNoSignals, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
        RebuildRuntimes();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RebuildRuntimes();
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, Item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = Item;
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
            || e.PropertyName == nameof(FolderItemModel.EnhancedSignalDefinitions)
            || e.PropertyName == nameof(FolderItemModel.Name)
            || e.PropertyName == nameof(FolderItemModel.Path)
            || e.PropertyName == nameof(FolderItemModel.FolderName))
        {
            if (Volatile.Read(ref _suppressObservedItemRebuild) == 0)
            {
                QueueRuntimeRebuild();
            }

            return;
        }

        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveMutedForeground))
        {
            foreach (var row in Signals)
            {
                row.RefreshTheme();
            }
        }
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            QueueRuntimeRefresh();
            return;
        }

        foreach (var row in Signals)
        {
            if (row.Runtime.MatchesPath(e.Key))
            {
                row.RefreshFromRuntime();
            }
        }
    }

    private void QueueRuntimeRefresh()
    {
        if (Interlocked.Exchange(ref _runtimeRefreshQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _runtimeRefreshQueued, 0);
            foreach (var row in Signals)
            {
                row.RefreshFromRuntime();
            }
        }, DispatcherPriority.Background);
    }

    private void OnSignalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasNoSignals = Signals.Count == 0;
    }

    private void QueueRuntimeRebuild()
    {
        if (Interlocked.Exchange(ref _runtimeRebuildQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _runtimeRebuildQueued, 0);
            RebuildRuntimes();
        }, DispatcherPriority.Background);
    }

    private void ApplyEnhancedSignalDefinitions(FolderItemModel ownerItem, string rawDefinitions, bool queuePersist)
    {
        Interlocked.Increment(ref _suppressObservedItemRebuild);
        try
        {
            ownerItem.EnhancedSignalDefinitions = rawDefinitions;
        }
        finally
        {
            Interlocked.Decrement(ref _suppressObservedItemRebuild);
        }

        QueueRuntimeRebuild();
        if (queuePersist)
        {
            if (ViewModel is { } viewModel)
            {
                if (!viewModel.TrySaveOwningPageYaml(ownerItem, out _))
                {
                    viewModel.QueueSaveOwningPageYaml(ownerItem);
                }
            }
        }
    }

    private static string SummarizeDefinitions(string? rawDefinitions)
    {
        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(rawDefinitions);
        if (definitions.Count == 0)
        {
            return "count=0";
        }

        return string.Join("; ", definitions.Select(definition =>
            $"name={definition.Name},mode={definition.Adjustment.MappingMode},enabled={definition.Adjustment.Enabled},offset={definition.Adjustment.Offset.ToString(CultureInfo.InvariantCulture)},gain={definition.Adjustment.Gain.ToString(CultureInfo.InvariantCulture)},spline={definition.Adjustment.SplinePoints.Count},inverse={definition.Adjustment.SupportsInverseMapping}"));
    }

    private void RebuildRuntimes()
    {
        var item = _observedItem;
        if (item is null)
        {
            Signals.Clear();
            return;
        }

        var runtimes = ViewModel?.GetEnhancedSignalRuntimes(item, forceRecreate: false)
            ?? EnhancedSignalRuntimeManager.SyncDefinitions(
                item.FolderName,
                item.EnhancedSignalDefinitions,
                forceRecreate: false,
                rawDefinitionsGetter: () => item.EnhancedSignalDefinitions,
                rawDefinitionsSetter: rawDefinitions =>
                {
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        item.EnhancedSignalDefinitions = rawDefinitions;
                        return;
                    }

                    Dispatcher.UIThread.Post(() => item.EnhancedSignalDefinitions = rawDefinitions);
                });

        Signals.Clear();
        foreach (var runtime in runtimes)
        {
            Signals.Add(new EnhancedSignalRow(item, runtime));
        }

        UpdateFooter(item, Signals.Count);
    }

    private static void UpdateFooter(FolderItemModel ownerItem, int count)
    {
        ownerItem.Footer = count == 0
            ? "No enhanced signals configured"
            : $"{count} enhanced signal module{(count == 1 ? string.Empty : "s")} published";
    }

    private async void OnAddSignalClicked(object? sender, RoutedEventArgs e)
    {
        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || viewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var definition = await EnhancedSignalEditorDialogWindow.ShowAsync(owner, viewModel, ownerItem, null, GetSourceOptions());
        if (definition is null)
        {
            return;
        }

        if (Signals.Any(row => string.Equals(row.Definition.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions).ToList();
        definitions.Add(definition);
        ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        e.Handled = true;
    }

    private async void OnEditSignalClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: EnhancedSignalRow row })
        {
            return;
        }

        var ownerItem = _observedItem;
        var viewModel = ViewModel;
        if (ownerItem is null || viewModel is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions).ToList();
        var currentDefinition = definitions.FirstOrDefault(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        if (currentDefinition is null)
        {
            return;
        }

        var updated = await EnhancedSignalEditorDialogWindow.ShowAsync(owner, viewModel, ownerItem, currentDefinition, GetSourceOptions());
        if (updated is null)
        {
            return;
        }

        if (Signals.Any(candidate => !ReferenceEquals(candidate, row) && string.Equals(candidate.Definition.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var index = definitions.FindIndex(candidate => string.Equals(candidate.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            definitions[index] = updated;
            ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        }

        e.Handled = true;
    }

    private async void OnDeleteSignalClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: EnhancedSignalRow row })
        {
            return;
        }

        var ownerItem = _observedItem;
        if (ownerItem is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(owner, $"Delete enhanced signal '{row.Name}'?", "The enhanced signal definition will be removed.", confirmText: "Delete", cancelText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var definitions = ExtendedSignalDefinitionCodec.ParseDefinitions(ownerItem.EnhancedSignalDefinitions)
            .Where(definition => !string.Equals(definition.Name, row.Definition.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ApplyEnhancedSignalDefinitions(ownerItem, ExtendedSignalDefinitionCodec.SerializeDefinitions(definitions), queuePersist: true);
        e.Handled = true;
    }

    private static IEnumerable<string> GetSourceOptions()
    {
        return HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class EnhancedSignalRow : ObservableObject
{
    private readonly FolderItemModel _ownerItem;

    public EnhancedSignalRow(FolderItemModel ownerItem, EnhancedSignalRuntime runtime)
    {
        _ownerItem = ownerItem;
        Runtime = runtime;
    }

    public EnhancedSignalRuntime Runtime { get; }

    public ExtendedSignalDefinition Definition => Runtime.Definition;

    public string Name => Definition.Name;

    public string SourceText => $"Source: {ValueOrPlaceholder(Definition.SourcePath)}";

    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    public string AlertText => FormatAlert(Runtime.CurrentAlertValue);

    public string RowBackground => _ownerItem.EffectiveBodyBackground;

    public string RowBorderBrush => _ownerItem.EffectiveBodyBorder;

    public string PrimaryForeground => _ownerItem.EffectiveBodyForeground;

    public string SecondaryForeground => _ownerItem.EffectiveMutedForeground;

    public void RefreshFromRuntime()
    {
        RaisePropertyChanged(nameof(AlertText));
        RaisePropertyChanged(nameof(HasAlert));
    }

    public void RefreshTheme()
    {
        RaisePropertyChanged(nameof(RowBackground));
        RaisePropertyChanged(nameof(RowBorderBrush));
        RaisePropertyChanged(nameof(PrimaryForeground));
        RaisePropertyChanged(nameof(SecondaryForeground));
    }

    private static string ValueOrPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) ? "n/a" : value;

    private static string FormatAlert(object? value)
        => value?.ToString() ?? string.Empty;
}

public partial class EditorEnhancedSignalsWidget : EnhancedSignalsControl
{
}
