using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.ViewModels;

public sealed class ItemTreeWindowViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<string, ItemTreeNodeViewModel> _nodeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPaths = new(StringComparer.OrdinalIgnoreCase);
    private MainWindowViewModel? _hostViewModel;
    private FolderModel? _folder;
    private ItemTreeNodeViewModel? _selectedNode;
    private string _windowTitle = "ItemTree";
    private string _scopeDescription = "Showing registered items.";
    private string _scopePath = "All registered paths";
    private string _selectionPath = string.Empty;
    private string _windowBackground = "#1F2937";
    private string _cardBackground = "#111827";
    private string _cardBorderBrush = "#374151";
    private string _primaryTextBrush = "#F9FAFB";
    private string _secondaryTextBrush = "#D1D5DB";
    private string _tabSelectBackColor = "#1E3A8A";
    private string _tabSelectForeColor = "#DBEAFE";
    private bool _refreshQueued;
    private bool _disposed;

    public ItemTreeWindowViewModel()
    {
        HostRegistries.Data.ItemChanged += OnDataRegistryChanged;
        QueueRefresh();
    }

    public ObservableCollection<ItemTreeNodeViewModel> RootNodes { get; } = [];

    public ItemTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                SelectionPath = value?.FullPath ?? ScopePath;
            }
        }
    }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string ScopeDescription
    {
        get => _scopeDescription;
        private set => SetProperty(ref _scopeDescription, value);
    }

    public string ScopePath
    {
        get => _scopePath;
        private set => SetProperty(ref _scopePath, value);
    }

    public string SelectionPath
    {
        get => _selectionPath;
        private set => SetProperty(ref _selectionPath, value);
    }

    public string WindowBackground
    {
        get => _windowBackground;
        private set => SetProperty(ref _windowBackground, value);
    }

    public string CardBackground
    {
        get => _cardBackground;
        private set => SetProperty(ref _cardBackground, value);
    }

    public string CardBorderBrush
    {
        get => _cardBorderBrush;
        private set => SetProperty(ref _cardBorderBrush, value);
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetProperty(ref _primaryTextBrush, value);
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetProperty(ref _secondaryTextBrush, value);
    }

    public string TabSelectBackColor
    {
        get => _tabSelectBackColor;
        private set => SetProperty(ref _tabSelectBackColor, value);
    }

    public string TabSelectForeColor
    {
        get => _tabSelectForeColor;
        private set => SetProperty(ref _tabSelectForeColor, value);
    }

    public void Attach(MainWindowViewModel? hostViewModel)
    {
        if (ReferenceEquals(_hostViewModel, hostViewModel))
        {
            return;
        }

        if (_hostViewModel is not null)
        {
            _hostViewModel.PropertyChanged -= OnHostViewModelPropertyChanged;
        }

        _hostViewModel = hostViewModel;

        if (_hostViewModel is not null)
        {
            _hostViewModel.PropertyChanged += OnHostViewModelPropertyChanged;
        }

        UpdateThemeBindings();
    }

    public void SetFolder(FolderModel folder)
    {
        _folder = folder;
        WindowTitle = $"ItemTree - {folder.TabTitle}";
        QueueRefresh();
    }

    public void ShowProjectScope()
    {
        _folder = null;
        WindowTitle = "ItemTree - Studio";
        QueueRefresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HostRegistries.Data.ItemChanged -= OnDataRegistryChanged;

        if (_hostViewModel is not null)
        {
            _hostViewModel.PropertyChanged -= OnHostViewModelPropertyChanged;
            _hostViewModel = null;
        }
    }

    private void OnHostViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.WindowBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectBackColor)
            || e.PropertyName == nameof(MainWindowViewModel.TabSelectForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        {
            UpdateThemeBindings();
        }
    }

    private void UpdateThemeBindings()
    {
        WindowBackground = _hostViewModel?.WindowBackground ?? "#1F2937";
        CardBackground = _hostViewModel?.CardBackground ?? "#111827";
        CardBorderBrush = _hostViewModel?.CardBorderBrush ?? "#374151";
        PrimaryTextBrush = _hostViewModel?.PrimaryTextBrush ?? "#F9FAFB";
        SecondaryTextBrush = _hostViewModel?.SecondaryTextBrush ?? "#D1D5DB";
        TabSelectBackColor = _hostViewModel?.TabSelectBackColor ?? "#1E3A8A";
        TabSelectForeColor = _hostViewModel?.TabSelectForeColor ?? "#DBEAFE";
    }

    private void OnDataRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        if (_folder is null)
        {
            QueueRefresh();
            return;
        }

        var scopePrefixes = BuildScopePrefixes(_folder.Name);
        if (scopePrefixes.Count == 0
            || scopePrefixes.Any(prefix => PathsEqual(e.Key, prefix)
                || IsDescendantPath(e.Key, prefix)
                || IsDescendantPath(prefix, e.Key)))
        {
            QueueRefresh();
        }
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || _disposed)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (_disposed)
            {
                return;
            }

            RefreshTree();
        }, DispatcherPriority.Background);
    }

    private void RefreshTree()
    {
        var selectedPath = SelectedNode?.FullPath ?? string.Empty;
        var allKeys = HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(NormalizePath)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scopedKeys = FilterKeysByScope(allKeys, out var fallbackToAll);
        UpdateScopeText(fallbackToAll);
        UpdateTree(scopedKeys);

        SelectedNode = FindNode(RootNodes, selectedPath)
            ?? FindScopeNode(RootNodes)
            ?? RootNodes.FirstOrDefault();
    }

    private IReadOnlyList<string> FilterKeysByScope(IReadOnlyList<string> allKeys, out bool fallbackToAll)
    {
        fallbackToAll = false;
        if (_folder is null)
        {
            var projectKeys = allKeys
                .Where(key => PathsEqual(key, "Studio")
                    || IsDescendantPath(key, "Studio"))
                .ToList();

            if (projectKeys.Count > 0)
            {
                return projectKeys;
            }

            fallbackToAll = true;
            return allKeys;
        }

        var scopePrefixes = BuildScopePrefixes(_folder.Name);
        if (scopePrefixes.Count == 0)
        {
            return allKeys;
        }

        var matches = allKeys
            .Where(key => scopePrefixes.Any(prefix => PathsEqual(key, prefix)
                || IsDescendantPath(key, prefix)
                || IsDescendantPath(prefix, key)))
            .ToList();

        if (matches.Count > 0)
        {
            return matches;
        }

        fallbackToAll = true;
        return allKeys;
    }

    private void UpdateScopeText(bool fallbackToAll)
    {
        if (_folder is null)
        {
            WindowTitle = "ItemTree";
            ScopeDescription = "Showing registered runtime items.";
            ScopePath = "Studio";
            return;
        }

        var scopePrefixes = BuildScopePrefixes(_folder.Name);
        WindowTitle = $"ItemTree - {_folder.TabTitle}";
        ScopePath = scopePrefixes.Count == 0
            ? $"studio.{_folder.Name}"
            : string.Join(" | ", scopePrefixes);
        ScopeDescription = fallbackToAll
            ? $"No matching Studio runtime branch was found for folder '{_folder.Name}'. Showing all registered items instead."
            : $"Showing registered runtime items for folder '{_folder.Name}'.";
    }

    private static IReadOnlyList<string> BuildScopePrefixes(string? folderName)
    {
        var normalizedFolderName = NormalizePath(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return [];
        }

        return new[]
        {
            $"studio.{normalizedFolderName}",
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    private void UpdateTree(IReadOnlyList<string> keys)
    {
        var rootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            var normalizedKey = NormalizePath(key);
            var segments = SplitPathSegments(normalizedKey);
            if (segments.Count == 0)
            {
                continue;
            }

            var keyNode = EnsureNodePath(segments, visitedPaths, rootPaths);
            if (keyNode is not null)
            {
                keyNode.ValueText = HostRegistries.Data.TryResolve(normalizedKey, out var item) && item is not null
                    ? FormatValue(item.Value)
                    : string.Empty;

                if (item is not null)
                {
                    AddItemSubTree(item, visitedPaths, rootPaths);
                }
            }
        }

        PruneUnusedNodes(visitedPaths);
        RebuildRootCollection(rootPaths);
    }

    private ItemTreeNodeViewModel? EnsureNodePath(
        IReadOnlyList<string> segments,
        ISet<string> visitedPaths,
        ISet<string> rootPaths)
    {
        if (segments.Count == 0)
        {
            return null;
        }

        ItemTreeNodeViewModel? parent = null;
        for (var index = 0; index < segments.Count; index++)
        {
            var currentPath = string.Join('.', segments.Take(index + 1));
            var node = EnsureNode(currentPath, segments[index], parent, visitedPaths, rootPaths);
            parent = node;
        }

        return parent;
    }

    private ItemTreeNodeViewModel EnsureNode(
        string fullPath,
        string displayName,
        ItemTreeNodeViewModel? parent,
        ISet<string> visitedPaths,
        ISet<string> rootPaths)
    {
        visitedPaths.Add(fullPath);

        if (!_nodeCache.TryGetValue(fullPath, out var node))
        {
            node = new ItemTreeNodeViewModel(displayName, fullPath);
            node.ExpansionChanged += OnNodeExpansionChanged;
            _nodeCache[fullPath] = node;
        }

        node.DisplayName = displayName;
        node.IsExpanded = _expandedPaths.Contains(fullPath);

        if (parent is null)
        {
            rootPaths.Add(fullPath);
        }
        else if (!parent.Children.Contains(node))
        {
            parent.Children.Add(node);
        }

        return node;
    }

    private void AddItemSubTree(ItemModel item, ISet<string> visitedPaths, ISet<string> rootPaths)
    {
        var normalizedItemPath = NormalizePath(item.Path);
        var segments = SplitPathSegments(normalizedItemPath);
        if (segments.Count == 0)
        {
            return;
        }

        var currentNode = EnsureNodePath(segments, visitedPaths, rootPaths);
        if (currentNode is null)
        {
            return;
        }

        currentNode.ValueText = FormatValue(item.Value);

        foreach (var childEntry in item.GetDictionary().OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (childEntry.Value is null)
            {
                continue;
            }

            AddItemSubTree(childEntry.Value, visitedPaths, rootPaths);
        }

        foreach (var parameterEntry in item.Properties.GetDictionary().OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = parameterEntry.Key;
            if (string.Equals(parameterName, "Name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "Path", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "Value", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameterPath = string.IsNullOrWhiteSpace(normalizedItemPath)
                ? parameterName
                : $"{normalizedItemPath}.{parameterName}";

            var parameterNode = EnsureNode(parameterPath, parameterName, currentNode, visitedPaths, rootPaths);
            parameterNode.ValueText = FormatValue(parameterEntry.Value.Value);
        }
    }

    private void PruneUnusedNodes(HashSet<string> visitedPaths)
    {
        var obsoletePaths = _nodeCache.Keys
            .Where(path => !visitedPaths.Contains(path))
            .OrderByDescending(path => path.Count(static c => c == '.'))
            .ToList();

        foreach (var obsoletePath in obsoletePaths)
        {
            if (!_nodeCache.TryGetValue(obsoletePath, out var node))
            {
                continue;
            }

            node.ExpansionChanged -= OnNodeExpansionChanged;
            _expandedPaths.Remove(obsoletePath);

            var parentPath = GetParentPath(obsoletePath);
            if (!string.IsNullOrWhiteSpace(parentPath) && _nodeCache.TryGetValue(parentPath, out var parent))
            {
                parent.Children.Remove(node);
            }

            _nodeCache.Remove(obsoletePath);
        }
    }

    private void RebuildRootCollection(HashSet<string> rootPaths)
    {
        var desiredRoots = rootPaths
            .Select(path => _nodeCache[path])
            .OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SyncCollection(RootNodes, desiredRoots);

        foreach (var root in desiredRoots)
        {
            SortChildrenRecursive(root);
        }
    }

    private static void SortChildrenRecursive(ItemTreeNodeViewModel node)
    {
        var ordered = node.Children
            .OrderBy(static child => child.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SyncCollection(node.Children, ordered);

        foreach (var child in ordered)
        {
            if (child.Children.Count > 0)
            {
                SortChildrenRecursive(child);
            }
        }
    }

    private static void SyncCollection(ObservableCollection<ItemTreeNodeViewModel> target, IReadOnlyList<ItemTreeNodeViewModel> desired)
    {
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var node = desired[index];
            if (index < target.Count && ReferenceEquals(target[index], node))
            {
                continue;
            }

            var existingIndex = target.IndexOf(node);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
                continue;
            }

            target.Insert(index, node);
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static ItemTreeNodeViewModel? FindNode(IEnumerable<ItemTreeNodeViewModel> nodes, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var node in nodes)
        {
            if (PathsEqual(node.FullPath, path))
            {
                return node;
            }

            var child = FindNode(node.Children, path);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void OnNodeExpansionChanged(object? sender, EventArgs e)
    {
        if (sender is not ItemTreeNodeViewModel node)
        {
            return;
        }

        if (node.IsExpanded)
        {
            _expandedPaths.Add(node.FullPath);
        }
        else
        {
            _expandedPaths.Remove(node.FullPath);
        }
    }

    private static string GetParentPath(string path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lastDotIndex = normalized.LastIndexOf('.');
        return lastDotIndex <= 0 ? string.Empty : normalized[..lastDotIndex];
    }

    private ItemTreeNodeViewModel? FindScopeNode(IEnumerable<ItemTreeNodeViewModel> nodes)
    {
        if (_folder is null)
        {
            return FindNode(nodes, "Studio");
        }

        foreach (var prefix in BuildScopePrefixes(_folder.Name))
        {
            var node = FindNode(nodes, prefix);
            if (node is not null)
            {
                return node;
            }
        }

        return null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var segments = path.Trim()
            .Replace('\\', '/')
            .Trim('/', '.')
            .Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0
            ? string.Empty
            : string.Join('.', NormalizeStudioRoot(segments));
    }

    private static IReadOnlyList<string> SplitPathSegments(string? path)
    {
        var normalized = NormalizePath(path);
        return string.IsNullOrWhiteSpace(normalized)
            ? []
            : normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool PathsEqual(string? left, string? right)
        => string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendantPath(string? path, string? prefix)
    {
        var pathSegments = SplitPathSegments(path);
        var prefixSegments = SplitPathSegments(prefix);
        if (pathSegments.Count == 0 || prefixSegments.Count == 0 || pathSegments.Count <= prefixSegments.Count)
        {
            return false;
        }

        for (var index = 0; index < prefixSegments.Count; index++)
        {
            if (!string.Equals(pathSegments[index], prefixSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> NormalizeStudioRoot(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            yield break;
        }

        if (string.Equals(segments[0], "Project", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], "UdlProject", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], "UdlBook", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Studio";
            foreach (var segment in segments.Skip(1))
            {
                yield return segment;
            }

            yield break;
        }

        if (string.Equals(segments[0], "Studio", StringComparison.OrdinalIgnoreCase)
            && segments.Count > 1
            && (string.Equals(segments[1], "Project", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[1], "UdlProject", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[1], "UdlBook", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Studio";
            foreach (var segment in segments.Skip(2))
            {
                yield return segment;
            }

            yield break;
        }

        foreach (var segment in segments)
        {
            yield return segment;
        }
    }
}

public sealed class ItemTreeNodeViewModel : ObservableObject
{
    private string _displayName;
    private string _valueText = string.Empty;
    private bool _isExpanded;

    public ItemTreeNodeViewModel(string displayName, string fullPath)
    {
        _displayName = displayName;
        FullPath = fullPath;
    }

    public event EventHandler? ExpansionChanged;

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string FullPath { get; }

    public ObservableCollection<ItemTreeNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                ExpansionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }
}
