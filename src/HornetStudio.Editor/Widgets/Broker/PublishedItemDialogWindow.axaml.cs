using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Amium.Items;
using Avalonia.Controls;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Edits local registry item publishing definitions for a Broker widget root.
/// </summary>
public partial class PublishedItemDialogWindow : Window
{
    private readonly DialogViewModel _viewModel;
    private readonly string _rawDefinitions = string.Empty;
    private readonly string _rootPath = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedItemDialogWindow"/> class.
    /// </summary>
    public PublishedItemDialogWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedItemDialogWindow"/> class.
    /// </summary>
    /// <param name="ownerViewModel">The owning view model.</param>
    /// <param name="rawDefinitions">The raw publish definitions.</param>
    /// <param name="rootPath">The selected local root path.</param>
    public PublishedItemDialogWindow(MainWindowViewModel ownerViewModel, string rawDefinitions, string rootPath)
    {
        InitializeComponent();
        _rawDefinitions = rawDefinitions ?? string.Empty;
        _rootPath = TargetPathHelper.NormalizeConfiguredTargetPath(rootPath);
        _viewModel = new DialogViewModel(ownerViewModel, BrokerPublishedItemDefinitionCodec.ParseDefinitions(_rawDefinitions), _rootPath);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Shows the dialog for a local publish root.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The owning main view model.</param>
    /// <param name="rawDefinitions">The raw publish definitions.</param>
    /// <param name="rootPath">The selected local root path.</param>
    /// <returns>The updated publish definition JSON, or <see langword="null"/> when cancelled.</returns>
    public static Task<string?> ShowAsync(Window owner, MainWindowViewModel viewModel, string rawDefinitions, string rootPath)
    {
        var dialog = new PublishedItemDialogWindow(viewModel, rawDefinitions, rootPath);
        return dialog.ShowDialog<string?>(owner);
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.TryBuildResult(_rawDefinitions, _rootPath, out var result))
        {
            Close(result);
        }
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private sealed class DialogViewModel : NotifyBase
    {
        private PublishedItemEditorRow? _selectedRow;
        private string _errorMessage = string.Empty;

        public DialogViewModel(
            MainWindowViewModel ownerViewModel,
            IReadOnlyList<BrokerPublishedItemDefinition> definitions,
            string rootPath)
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

            RootPath = rootPath;
            Rows = new ObservableCollection<PublishedItemEditorRow>(BuildRows(definitions, rootPath));
            SelectedRow = Rows.FirstOrDefault();
        }

        public string TitleText => "Published Items";

        public string DescriptionText => "Configure which local registry entries are published from HornetStudio to the broker.";

        public string RootPath { get; }

        public ObservableCollection<PublishedItemEditorRow> Rows { get; }

        public IReadOnlyList<string> PublishModeOptions { get; } =
        [
            BrokerPublishedItemPublishModes.OnChanged,
            BrokerPublishedItemPublishModes.Interval
        ];

        public PublishedItemEditorRow? SelectedRow
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

        public bool TryBuildResult(string rawDefinitions, string rootPath, out string result)
        {
            ErrorMessage = string.Empty;
            foreach (var row in Rows)
            {
                if (row.PublishIntervalMs <= 0)
                {
                    result = string.Empty;
                    ErrorMessage = $"{row.DisplayName}: PublishIntervalMs must be greater than zero.";
                    return false;
                }
            }

            var existingOtherRoots = BrokerPublishedItemDefinitionCodec.ParseDefinitions(rawDefinitions)
                .Where(definition => !string.Equals(definition.LocalRootPath, rootPath, StringComparison.OrdinalIgnoreCase));
            var rootDefinitions = Rows.Select(row => row.ToDefinition(rootPath));
            result = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(existingOtherRoots.Concat(rootDefinitions));
            return true;
        }

        private static IReadOnlyList<PublishedItemEditorRow> BuildRows(
            IReadOnlyList<BrokerPublishedItemDefinition> definitions,
            string rootPath)
        {
            var rootDefinitions = definitions
                .Where(definition => string.Equals(definition.LocalRootPath, rootPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definition.LocalPath, rootPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static definition => definition.LocalPath, StringComparer.OrdinalIgnoreCase);

            var paths = EnumerateLocalPaths(rootPath)
                .Concat(rootDefinitions.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Count(static character => character == '.'))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return paths.Select(path =>
            {
                var definition = rootDefinitions.TryGetValue(path, out var existing)
                    ? existing
                    : BrokerPublishedItemDefinitionCodec.CreateDefault(path);
                return new PublishedItemEditorRow(definition);
            }).ToArray();
        }

        private static IEnumerable<string> EnumerateLocalPaths(string rootPath)
        {
            if (!HostRegistries.Data.TryResolve(rootPath, out var rootItem) || rootItem is null)
            {
                yield break;
            }

            foreach (var path in EnumerateItemPaths(rootItem, rootPath))
            {
                yield return path;
            }
        }

        private static IEnumerable<string> EnumerateItemPaths(Item item, string fallbackPath)
        {
            var path = TargetPathHelper.NormalizeConfiguredTargetPath(item.Path);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = fallbackPath;
            }

            yield return path;
            foreach (var child in item.GetDictionary().Values.OrderBy(child => child.Path, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var childPath in EnumerateItemPaths(child, path))
                {
                    yield return childPath;
                }
            }
        }
    }
}

/// <summary>
/// Represents one editable publish definition row.
/// </summary>
public sealed class PublishedItemEditorRow : NotifyBase
{
    private bool _active;
    private string _publishMode;
    private int _publishIntervalMs;
    private bool _writable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedItemEditorRow"/> class.
    /// </summary>
    /// <param name="definition">The source definition.</param>
    public PublishedItemEditorRow(BrokerPublishedItemDefinition definition)
    {
        LocalPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.LocalPath);
        var brokerPath = TargetPathHelper.NormalizeConfiguredTargetPath(definition.BrokerPath);
        BrokerPath = string.IsNullOrWhiteSpace(brokerPath)
            ? BrokerPublishedItemDefinitionCodec.BuildDefaultBrokerPath(LocalPath)
            : brokerPath;
        _active = definition.Active;
        _publishMode = BrokerPublishedItemPublishModes.Normalize(definition.PublishMode);
        _publishIntervalMs = Math.Max(1, definition.PublishIntervalMs);
        _writable = definition.Writable;
    }

    /// <summary>
    /// Gets the local registry path.
    /// </summary>
    public string LocalPath { get; }

    /// <summary>
    /// Gets the flat broker path.
    /// </summary>
    public string BrokerPath { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName => LocalPath;

    /// <summary>
    /// Gets the row summary.
    /// </summary>
    public string Summary => Active
        ? $"{PublishMode} | {PublishIntervalMs} ms | Active"
        : $"{PublishMode} | {PublishIntervalMs} ms | Inactive";

    /// <summary>
    /// Gets or sets a value indicating whether publishing is active.
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (SetProperty(ref _active, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// Gets or sets the publish mode.
    /// </summary>
    public string PublishMode
    {
        get => _publishMode;
        set
        {
            if (SetProperty(ref _publishMode, BrokerPublishedItemPublishModes.Normalize(value)))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// Gets or sets the publish interval in milliseconds.
    /// </summary>
    public int PublishIntervalMs
    {
        get => _publishIntervalMs;
        set
        {
            if (SetProperty(ref _publishIntervalMs, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(PublishIntervalMsText));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// Gets or sets the publish interval text.
    /// </summary>
    public string PublishIntervalMsText
    {
        get => PublishIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set => PublishIntervalMs = int.TryParse(value?.Trim(), out var parsedValue) ? parsedValue : 1;
    }

    /// <summary>
    /// Gets or sets a value indicating whether future write-back may treat the item as writable.
    /// </summary>
    public bool Writable
    {
        get => _writable;
        set => SetProperty(ref _writable, value);
    }

    /// <summary>
    /// Converts the editor row into a publish definition.
    /// </summary>
    /// <param name="rootPath">The owning local root path.</param>
    /// <returns>The publish definition.</returns>
    public BrokerPublishedItemDefinition ToDefinition(string rootPath)
    {
        return new BrokerPublishedItemDefinition
        {
            LocalRootPath = rootPath,
            LocalPath = LocalPath,
            BrokerPath = BrokerPath,
            Active = Active,
            PublishMode = PublishMode,
            PublishIntervalMs = PublishIntervalMs,
            Writable = Writable
        };
    }
}
