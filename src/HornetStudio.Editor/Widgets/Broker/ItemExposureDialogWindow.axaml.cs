using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Edits source-neutral item exposure metadata for broker-discovered items.
/// </summary>
public partial class ItemExposureDialogWindow : Window
{
    private static readonly IReadOnlyList<string> ParameterFormatOptions = ["Text", "Numeric", "Hex", "bool", "EpochToDatetime", "b4", "b8", "b16"];
    private string _rawDefinitions = string.Empty;
    private string _itemPath = string.Empty;
    private readonly DialogViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemExposureDialogWindow"/> class.
    /// </summary>
    public ItemExposureDialogWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemExposureDialogWindow"/> class.
    /// </summary>
    /// <param name="ownerViewModel">The owning view model.</param>
    /// <param name="rawDefinitions">The raw exposure definitions.</param>
    /// <param name="itemPath">The edited item path.</param>
    public ItemExposureDialogWindow(MainWindowViewModel ownerViewModel, string rawDefinitions, string itemPath)
    {
        InitializeComponent();

        _rawDefinitions = rawDefinitions ?? string.Empty;
        _itemPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
        _viewModel = new DialogViewModel(
            ownerViewModel,
            ItemExposureDefinitionCodec.ParseDefinitions(_rawDefinitions),
            _itemPath);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Shows the dialog for a single broker item exposure definition.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The owning main view model.</param>
    /// <param name="rawDefinitions">The raw exposure definitions.</param>
    /// <param name="itemPath">The edited item path.</param>
    /// <returns>The updated exposure definition JSON, or <see langword="null"/> when cancelled.</returns>
    public static Task<string?> ShowAsync(Window owner, MainWindowViewModel viewModel, string rawDefinitions, string itemPath)
    {
        var dialog = new ItemExposureDialogWindow(viewModel, rawDefinitions, itemPath);
        return dialog.ShowDialog<string?>(owner);
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildResult(_rawDefinitions, _itemPath, out var result))
        {
            return;
        }

        Close(result);
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private sealed class DialogViewModel : NotifyBase
    {
        private ItemExposureEditorRow? _selectedRow;
        private string _errorMessage = string.Empty;

        public DialogViewModel(
            MainWindowViewModel ownerViewModel,
            IReadOnlyList<ItemExposureDefinition> definitions,
            string itemPath)
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

            Rows = new ObservableCollection<ItemExposureEditorRow>(BuildRows(definitions, itemPath));
            SelectedRow = Rows.FirstOrDefault();
        }

        public string TitleText => "Item Exposure";

        public string DescriptionText => "Configure metadata and optional bit helper items for the selected broker item.";

        public ObservableCollection<ItemExposureEditorRow> Rows { get; }

        public IReadOnlyList<string> FormatOptions => ParameterFormatOptions;

        public ItemExposureEditorRow? SelectedRow
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

        public bool TryBuildResult(string rawDefinitions, string itemPath, out string result)
        {
            ErrorMessage = string.Empty;
            foreach (var row in Rows)
            {
                if (row.ExposeBits && row.EffectiveBitCount <= 0)
                {
                    result = string.Empty;
                    ErrorMessage = $"{row.DisplayName}: bit count must be greater than zero when Expose Bits is enabled.";
                    return false;
                }
            }

            result = ItemExposureDefinitionCodec.UpsertDefinition(rawDefinitions, itemPath, Rows[0].ToDefinition());
            return true;
        }

        private static IReadOnlyList<ItemExposureEditorRow> BuildRows(
            IReadOnlyList<ItemExposureDefinition> definitions,
            string itemPath)
        {
            var normalizedPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
            var relativePath = TargetPathHelper.ToRelativeItemServerPath(normalizedPath);
            var definition = definitions.FirstOrDefault(definition =>
                string.Equals(TargetPathHelper.ToFlatItemServerPath(definition.ItemPath), normalizedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(TargetPathHelper.ToFlatItemServerPath(definition.ItemPath), relativePath, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                return [new ItemExposureEditorRow(normalizedPath, string.Empty, string.Empty, 0, false, string.Empty)];
            }

            return [new ItemExposureEditorRow(normalizedPath, definition.Format, definition.Unit, definition.BitCount, definition.ExposeBits, definition.BitLabels)];
        }
    }
}

/// <summary>
/// Represents one editable item exposure row.
/// </summary>
public sealed class ItemExposureEditorRow : NotifyBase
{
    private string _format;
    private string _unit;
    private int _bitCount;
    private bool _exposeBits;
    private string _bitLabels;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemExposureEditorRow"/> class.
    /// </summary>
    /// <param name="itemPath">The source-relative item path.</param>
    /// <param name="format">The display format.</param>
    /// <param name="unit">The unit text.</param>
    /// <param name="bitCount">The bit count.</param>
    /// <param name="exposeBits">Whether bit helper items are exposed.</param>
    /// <param name="bitLabels">The bit labels.</param>
    public ItemExposureEditorRow(string itemPath, string format, string unit, int bitCount, bool exposeBits, string bitLabels)
    {
        ItemPath = TargetPathHelper.ToFlatItemServerPath(itemPath);
        _format = format?.Trim() ?? string.Empty;
        _unit = unit?.Trim() ?? string.Empty;
        _bitCount = NormalizeBitCount(bitCount);
        _exposeBits = exposeBits;
        _bitLabels = bitLabels?.Trim() ?? string.Empty;
    }

    public string ItemPath { get; }

    public string DisplayName => ItemPath;

    public string Summary => ExposeBits
        ? $"Count {EffectiveBitCount} | Unit {EffectiveUnit} | bit helpers active"
        : $"Count {EffectiveBitCount} | Unit {EffectiveUnit} | no helper items";

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
                OnPropertyChanged(nameof(EffectiveBitCount));
                OnPropertyChanged(nameof(Summary));
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

    public int BitCount
    {
        get => _bitCount;
        set
        {
            if (SetProperty(ref _bitCount, NormalizeBitCount(value)))
            {
                OnPropertyChanged(nameof(EffectiveBitCount));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string BitCountText
    {
        get => BitCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set => BitCount = int.TryParse(value?.Trim(), out var parsedValue) ? parsedValue : 0;
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

    public bool ExposeBits
    {
        get => _exposeBits;
        set
        {
            if (SetProperty(ref _exposeBits, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string BitLabels
    {
        get => _bitLabels;
        set => SetProperty(ref _bitLabels, value?.Trim() ?? string.Empty);
    }

    public ItemExposureDefinition ToDefinition()
    {
        return new ItemExposureDefinition
        {
            ItemPath = ItemPath,
            Format = Format,
            Unit = Unit,
            ExposeBits = ExposeBits,
            BitCount = EffectiveBitCount,
            BitLabels = BitLabels
        };
    }

    private static int NormalizeBitCount(int value)
        => value <= 0 ? 0 : Math.Clamp(value, 1, 32);

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
            return ("EpochToDatetime", trimmed[16..].Trim());
        }

        if (string.Equals(trimmed, "EpochToDatetime", StringComparison.OrdinalIgnoreCase))
        {
            return ("EpochToDatetime", "UtcDefault");
        }

        var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries);
        return (string.IsNullOrWhiteSpace(parts[0]) ? "Text" : parts[0], parts.Length > 1 ? parts[1] : string.Empty);
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

        return string.IsNullOrWhiteSpace(normalizedProperty) ? normalizedKind : $"{normalizedKind}:{normalizedProperty}";
    }
}
