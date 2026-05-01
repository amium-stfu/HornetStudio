using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class TargetTreeSelectionDialogWindow : Window, INotifyPropertyChanged
{
    private readonly EditorDialogField? _field;
    private readonly string _folderName = string.Empty;
    private MainWindowViewModel? _viewModel;
    private TargetSelectionTreeNode? _selectedNode;
    private string _selectedValue = string.Empty;
    private string _dialogBackground = "#E3E5EE";
    private string _borderColor = "#D5D9E0";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";
    private string _buttonBackground = "#F8FAFC";
    private string _buttonBorderBrush = "#CBD5E1";
    private string _buttonForeground = "#111827";
    private string _editorDialogSectionContentBackground = "#EEF3F8";
    private string _parameterEditBackgrundColor = "#FFFFFF";
    private string _tabSelectBackColor = "#FFF1C4";
    private string _tabSelectForeColor = "#000000";
    private string _scopePath = "Showing all available targets";
    private string _scopeDescription = "Choose a target inside the active folder.";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public TargetTreeSelectionDialogWindow()
    {
        RootNodes = [];
        InitializeComponent();
        DataContext = this;
    }

    public TargetTreeSelectionDialogWindow(MainWindowViewModel? viewModel, EditorDialogField field)
    {
        _field = field;
        _folderName = ExtractPageName(field.Parameter.Path);
        RootNodes = [];
        InitializeComponent();
        DataContext = this;
        _selectedValue = field.Value;
        RebuildTree(field.Options, field.Value, _folderName);
        AttachToViewModel(viewModel);
    }

    public TargetTreeSelectionDialogWindow(MainWindowViewModel? viewModel, IEnumerable<string> options, string selectedValue, string pageName)
    {
        _folderName = pageName ?? string.Empty;
        RootNodes = [];
        InitializeComponent();
        DataContext = this;
        _selectedValue = selectedValue ?? string.Empty;
        RebuildTree(options, _selectedValue, _folderName);
        AttachToViewModel(viewModel);
    }

    public ObservableCollection<TargetSelectionTreeNode> RootNodes { get; }

    public TargetSelectionTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            _selectedNode = value;
            RaisePropertyChanged(nameof(SelectedNode));
            RaisePropertyChanged(nameof(CanSaveSelection));
            RaisePropertyChanged(nameof(SelectionPreviewPath));
        }
    }

    public bool CanSaveSelection => SelectedNode?.IsSelectable == true;

    public string SelectionPreviewPath => SelectedNode?.ActualPath ?? SelectedNode?.FullPath ?? _selectedValue;

    public string CommittedSelection { get; private set; } = string.Empty;

    public string DialogBackground
    {
        get => _dialogBackground;
        private set => SetAndRaise(ref _dialogBackground, value, nameof(DialogBackground));
    }

    public string BorderColor
    {
        get => _borderColor;
        private set => SetAndRaise(ref _borderColor, value, nameof(BorderColor));
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetAndRaise(ref _primaryTextBrush, value, nameof(PrimaryTextBrush));
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetAndRaise(ref _secondaryTextBrush, value, nameof(SecondaryTextBrush));
    }

    public string ButtonBackground
    {
        get => _buttonBackground;
        private set => SetAndRaise(ref _buttonBackground, value, nameof(ButtonBackground));
    }

    public string ButtonBorderBrush
    {
        get => _buttonBorderBrush;
        private set => SetAndRaise(ref _buttonBorderBrush, value, nameof(ButtonBorderBrush));
    }

    public string ButtonForeground
    {
        get => _buttonForeground;
        private set => SetAndRaise(ref _buttonForeground, value, nameof(ButtonForeground));
    }

    public string EditorDialogSectionContentBackground
    {
        get => _editorDialogSectionContentBackground;
        private set => SetAndRaise(ref _editorDialogSectionContentBackground, value, nameof(EditorDialogSectionContentBackground));
    }

    public string ParameterEditBackgrundColor
    {
        get => _parameterEditBackgrundColor;
        private set => SetAndRaise(ref _parameterEditBackgrundColor, value, nameof(ParameterEditBackgrundColor));
    }

    public string TabSelectBackColor
    {
        get => _tabSelectBackColor;
        private set => SetAndRaise(ref _tabSelectBackColor, value, nameof(TabSelectBackColor));
    }

    public string TabSelectForeColor
    {
        get => _tabSelectForeColor;
        private set => SetAndRaise(ref _tabSelectForeColor, value, nameof(TabSelectForeColor));
    }

    public string ScopePath
    {
        get => _scopePath;
        private set => SetAndRaise(ref _scopePath, value, nameof(ScopePath));
    }

    public string ScopeDescription
    {
        get => _scopeDescription;
        private set => SetAndRaise(ref _scopeDescription, value, nameof(ScopeDescription));
    }

    protected override void OnClosed(EventArgs e)
    {
        AttachToViewModel(null);
        base.OnClosed(e);
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<TreeView>("TargetTreeView")?.SelectedItem is TargetSelectionTreeNode selectedNode)
        {
            SelectedNode = selectedNode;
        }

        if (SelectedNode?.IsSelectable == true)
        {
            CommittedSelection = TargetPathHelper.ToPersistedLayoutTargetPath(SelectedNode.ActualPath, _folderName);
            _selectedValue = CommittedSelection;
        }

        if (_field is not null && !string.IsNullOrWhiteSpace(CommittedSelection))
        {
            _field.Value = CommittedSelection;
        }

        Close();
        e.Handled = true;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView { SelectedItem: TargetSelectionTreeNode selectedNode })
        {
            SelectedNode = selectedNode;
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateThemeBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DialogBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionContentBackground)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterEditBackgrundColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectBackColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        {
            UpdateThemeBindings();
        }
    }

    private void UpdateThemeBindings()
    {
        DialogBackground = _viewModel?.DialogBackground ?? "#E3E5EE";
        BorderColor = _viewModel?.CardBorderBrush ?? "#D5D9E0";
        PrimaryTextBrush = _viewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = _viewModel?.SecondaryTextBrush ?? "#5E6777";
        ButtonBackground = _viewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = _viewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = _viewModel?.PrimaryTextBrush ?? "#111827";
        EditorDialogSectionContentBackground = _viewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
        ParameterEditBackgrundColor = _viewModel?.ParameterEditBackgrundColor ?? "#FFFFFF";
        TabSelectBackColor = _viewModel?.TabSelectBackColor ?? "#FFF1C4";
        TabSelectForeColor = _viewModel?.TabSelectForeColor ?? "#000000";
        UpdateWindowIcon();
    }

    private void UpdateWindowIcon()
    {
        if (_viewModel is null)
        {
            return;
        }

        var iconName = _viewModel.IsDarkTheme ? "listDark.png" : "listLight.png";
        var uri = new Uri($"avares://HornetStudio.Editor/EditorIcons/{iconName}");

        try
        {
            using var stream = AssetLoader.Open(uri);
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch
        {
            // Keep the default window icon if the themed asset is unavailable.
        }
    }

    private void RebuildTree(IEnumerable<string> options, string selectedValue, string pageName)
    {
        var sourceOptions = options
            .Where(static option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static option => option, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedValue) && !ContainsEquivalentPath(sourceOptions, selectedValue, pageName))
        {
            sourceOptions.Add(selectedValue);
            sourceOptions = sourceOptions
                .OrderBy(static option => option, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var displayPrefix = DetermineDisplayPrefix(sourceOptions, selectedValue, pageName);
        var filteredOptions = string.IsNullOrWhiteSpace(displayPrefix)
            ? sourceOptions
            : sourceOptions.Where(path => IsWithinDisplayScope(path, displayPrefix)).ToList();

        ScopePath = string.IsNullOrWhiteSpace(displayPrefix)
            ? "No folder prefix detected"
            : displayPrefix.TrimEnd('.');
        ScopeDescription = string.IsNullOrWhiteSpace(pageName)
            ? "Choose a target from the available tree."
            : $"Showing targets within folder '{pageName}'.";

        RootNodes.Clear();
        foreach (var node in BuildTree(filteredOptions, displayPrefix))
        {
            RootNodes.Add(node);
        }

        SelectedNode = FindNodeByFullPath(RootNodes, selectedValue, pageName);
    }

    private static IReadOnlyList<TargetSelectionTreeNode> BuildTree(IReadOnlyList<string> paths, string displayPrefix)
    {
        var roots = new List<TargetSelectionTreeNode>();

        foreach (var fullPath in paths)
        {
            var normalizedFullPath = TargetPathHelper.ToFlatItemBrokerPath(NormalizePath(fullPath));
            if (string.IsNullOrWhiteSpace(normalizedFullPath))
            {
                continue;
            }

            var relativePath = RemovePrefix(normalizedFullPath, displayPrefix);
            var segments = TargetPathHelper.SplitPathSegments(relativePath);
            if (segments.Count == 0)
            {
                continue;
            }

            var fullSegments = TargetPathHelper.SplitPathSegments(normalizedFullPath);
            IList<TargetSelectionTreeNode> currentList = roots;
            var prefixSegmentCount = fullSegments.Count - segments.Count;

            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var segmentKey = string.Join('.', fullSegments.Take(prefixSegmentCount + index + 1));
                var node = currentList.FirstOrDefault(candidate => string.Equals(candidate.FullPath, segmentKey, StringComparison.OrdinalIgnoreCase));
                if (node is null)
                {
                    node = new TargetSelectionTreeNode
                    {
                        DisplayName = segment,
                        FullPath = segmentKey,
                        ActualPath = index == segments.Count - 1 ? normalizedFullPath : segmentKey
                    };
                    currentList.Add(node);
                }

                if (index == segments.Count - 1)
                {
                    node.IsSelectable = true;
                }

                currentList = node.Children;
            }
        }

        SortTree(roots);

        return roots;
    }

    private static void SortTree(IEnumerable<TargetSelectionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            var orderedChildren = node.Children.OrderBy(child => child.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            if (orderedChildren.Count != node.Children.Count || !orderedChildren.SequenceEqual(node.Children))
            {
                node.Children.Clear();
                foreach (var child in orderedChildren)
                {
                    node.Children.Add(child);
                }
            }

            SortTree(node.Children);
        }
    }

    private static TargetSelectionTreeNode? FindNodeByFullPath(IEnumerable<TargetSelectionTreeNode> nodes, string? fullPath, string pageName)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var candidatePaths = TargetPathHelper.EnumerateResolutionCandidates(fullPath, pageName)
            .Concat(TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(fullPath))
            .Select(TargetPathHelper.ToFlatItemBrokerPath)
            .Select(NormalizePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var node in nodes)
        {
            var comparableNodePath = TargetPathHelper.NormalizeComparablePath(string.IsNullOrWhiteSpace(node.ActualPath) ? node.FullPath : node.ActualPath);
            if (candidatePaths.Contains(comparableNodePath, StringComparer.OrdinalIgnoreCase))
            {
                return node;
            }

            var match = FindNodeByFullPath(node.Children, fullPath, pageName);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool ContainsEquivalentPath(IEnumerable<string> options, string selectedValue, string pageName)
    {
        var normalizedOptions = options
            .Select(TargetPathHelper.ToFlatItemBrokerPath)
            .Select(TargetPathHelper.NormalizeComparablePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        foreach (var candidate in TargetPathHelper.EnumerateResolutionCandidates(selectedValue, pageName)
            .Concat(TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(selectedValue))
            .Select(TargetPathHelper.ToFlatItemBrokerPath))
        {
            var normalizedCandidate = TargetPathHelper.NormalizeComparablePath(candidate);
            if (normalizedOptions.Contains(normalizedCandidate, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string DetermineDisplayPrefix(IReadOnlyList<string> options, string? currentValue, string pageName)
    {
        if (!string.IsNullOrWhiteSpace(pageName))
        {
            var availablePrefixes = options
                .Select(path => TryExtractPagePrefix(path, pageName))
                .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var preferredProjectPrefix = $"Studio.{pageName}.";
            var hasProjectPrefix = availablePrefixes.Any(prefix => string.Equals(prefix, preferredProjectPrefix, StringComparison.OrdinalIgnoreCase));

            if (hasProjectPrefix)
            {
                return preferredProjectPrefix;
            }

            var currentPrefix = TryExtractPagePrefix(currentValue, pageName);
            if (!string.IsNullOrWhiteSpace(currentPrefix) && availablePrefixes.Contains(currentPrefix, StringComparer.OrdinalIgnoreCase))
            {
                return currentPrefix!;
            }

            var candidate = availablePrefixes
                .OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string ExtractPageName(string? parameterPath)
    {
        if (string.IsNullOrWhiteSpace(parameterPath))
        {
            return string.Empty;
        }

        var normalizedPath = parameterPath.Replace('/', '.').Trim();
        var firstSeparator = normalizedPath.IndexOf('.');
        if (firstSeparator <= 0)
        {
            return normalizedPath;
        }

        return normalizedPath[..firstSeparator].Trim();
    }

    private static string? TryExtractPagePrefix(string? path, string pageName)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(pageName))
        {
            return null;
        }

        var segments = TargetPathHelper.SplitPathSegments(normalizedPath);
        for (var index = 1; index < segments.Count; index++)
        {
            if (string.Equals(segments[index], pageName, StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Take(index + 1)) + ".";
            }
        }

        return null;
    }

    private static string RemovePrefix(string fullPath, string prefix)
    {
        var normalizedFullPath = NormalizePath(fullPath);
        var normalizedPrefix = NormalizePath(prefix).TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return normalizedFullPath;
        }

        return normalizedFullPath.StartsWith(normalizedPrefix + ".", StringComparison.OrdinalIgnoreCase)
            ? normalizedFullPath[(normalizedPrefix.Length + 1)..]
            : normalizedFullPath;
    }

    private static bool IsWithinDisplayScope(string path, string prefix)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedPrefix = NormalizePath(prefix).TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return true;
        }

        return string.Equals(normalizedPath, normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedPrefix + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
        => TargetPathHelper.NormalizeComparablePath(path);

    private void SetAndRaise(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        RaisePropertyChanged(propertyName);
    }

    private void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
