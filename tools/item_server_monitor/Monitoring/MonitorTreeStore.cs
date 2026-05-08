using System.Collections.Generic;
using System.Linq;
using Item.Server.Monitor.ViewModels;

namespace Item.Server.Monitor.Monitoring;

public sealed class MonitorTreeStore
{
    private readonly HashSet<string> _expandedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandablePaths = new(StringComparer.OrdinalIgnoreCase);

    public void ToggleExpanded(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_expandablePaths.Contains(path))
        {
            return;
        }

        if (!_expandedPaths.Add(path))
        {
            _expandedPaths.Remove(path);
        }
    }

    public void ExpandAll()
    {
        foreach (var path in _expandablePaths)
        {
            _expandedPaths.Add(path);
        }
    }

    public void CollapseAll()
    {
        _expandedPaths.Clear();
    }

    public IReadOnlyList<MonitorTreeRowViewModel> BuildVisibleRows(IReadOnlyCollection<MonitorItemSnapshot> snapshots, string? filterText)
    {
        var root = BuildTree(snapshots);
        _expandablePaths.Clear();

        var rows = new List<MonitorTreeRowViewModel>();
        var normalizedFilter = filterText?.Trim();

        foreach (var child in root.Children.Values.OrderBy(static child => child.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendRows(child, depth: 0, normalizedFilter, rows);
        }

        return rows;
    }

    private bool AppendRows(TreeNode node, int depth, string? filterText, ICollection<MonitorTreeRowViewModel> rows)
    {
        var filterActive = !string.IsNullOrWhiteSpace(filterText);
        var childRows = new List<MonitorTreeRowViewModel>();
        var descendantIncluded = false;

        foreach (var child in node.Children.Values.OrderBy(static child => child.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (AppendRows(child, depth + 1, filterText, childRows))
            {
                descendantIncluded = true;
            }
        }

        var selfIncluded = !filterActive || Matches(node, filterText!);
        if (!selfIncluded && !descendantIncluded)
        {
            return false;
        }

        var hasChildren = node.Children.Count > 0;
        if (hasChildren)
        {
            _expandablePaths.Add(node.Path);
        }

        var isExpanded = filterActive || _expandedPaths.Contains(node.Path);
        rows.Add(CreateRow(node, depth, hasChildren, isExpanded));

        if (hasChildren && isExpanded)
        {
            foreach (var childRow in childRows)
            {
                rows.Add(childRow);
            }
        }

        return true;
    }

    private static bool Matches(TreeNode node, string filterText)
    {
        return node.Path.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || node.ValueText.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private static MonitorTreeRowViewModel CreateRow(TreeNode node, int depth, bool hasChildren, bool isExpanded)
        => new(
            path: node.Path,
            displayName: node.Name,
            valueText: node.ValueText,
            depth: depth,
            hasChildren: hasChildren,
            isExpanded: isExpanded,
            hasSnapshot: node.Snapshot is not null);

    private static TreeNode BuildTree(IReadOnlyCollection<MonitorItemSnapshot> snapshots)
    {
        var root = new TreeNode(path: string.Empty, name: string.Empty);
        foreach (var snapshot in snapshots.OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AddSnapshot(root, snapshot);
        }

        return root;
    }

    private static void AddSnapshot(TreeNode root, MonitorItemSnapshot snapshot)
    {
        var path = snapshot.Path;
        var segments = path.Split(['.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = root;
        var currentPath = string.Empty;

        foreach (var segment in segments)
        {
            currentPath = string.IsNullOrWhiteSpace(currentPath)
                ? segment
                : $"{currentPath}.{segment}";

            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new TreeNode(currentPath, segment);
                current.Children[segment] = child;
            }

            current = child;
        }

        current.Snapshot = snapshot;
        current.ValueText = MonitorValueFormatter.Format(snapshot.ItemModel.Value);
    }

    private sealed class TreeNode
    {
        public TreeNode(string path, string name)
        {
            Path = path;
            Name = name;
        }

        public string Path { get; }

        public string Name { get; }

        public string ValueText { get; set; } = string.Empty;

        public MonitorItemSnapshot? Snapshot { get; set; }

        public Dictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}