using System;
using System.Collections.Generic;
using Amium.Items;

namespace HornetStudio.Host;

public sealed class UiFolderContext : IDisposable
{
    private const string StudioRootSegment = "Studio";
    private readonly List<AttachedItemLink> _links = [];
    private readonly string _folderPath;

    public UiFolderContext(string folderName, string? projectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        FolderName = NormalizePath(folderName);
        ProjectName = NormalizeProjectRoot(projectName);
        _folderPath = $"{ProjectName}.{FolderName}";
    }

    public string FolderName { get; }
    public string? ProjectName { get; }
    public string FolderPath => _folderPath;

    public Item Attach(Item source, string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var itemName = string.IsNullOrWhiteSpace(alias) ? source.Name : NormalizePath(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);

        var targetPath = $"{_folderPath}.{itemName}";

        foreach (var link in _links)
        {
            if (link.Matches(source, targetPath))
            {
                return link.AttachedItem;
            }
        }

        var attached = source.Clone().Repath(targetPath);
        _links.Add(new AttachedItemLink(source, attached, targetPath));
        return attached;
    }

    public HostCommand CreateCommand(string name, Action action, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);

        var commandPath = $"{_folderPath}.Commands.{NormalizePath(name)}";
        return new HostCommand(commandPath, _ => action(), description: description);
    }

    public HostCommand AttachCommand(string name, Action action, string? description = null)
        => CreateCommand(name, action, description);

    public void Dispose()
    {
        foreach (var link in _links)
        {
            link.Dispose();
        }

        _links.Clear();
    }

    private sealed class AttachedItemLink : IDisposable
    {
        private readonly Item _attachedItem;
        private readonly Item _source;
        private readonly List<Item> _subscribedSourceItems = [];
        private bool _isSyncingFromSource;
        private bool _isSyncingFromTarget;
        private readonly string _targetPath;

        public AttachedItemLink(Item source, Item attachedItem, string targetPath)
        {
            _source = source;
            _attachedItem = attachedItem;
            _targetPath = targetPath;
            SubscribeSourceTree(_source);
            HostRegistries.Data.ItemChanged += OnTargetChanged;
        }

        public Item AttachedItem => _attachedItem;

        public bool Matches(Item source, string targetPath)
            => ReferenceEquals(_source, source)
                && string.Equals(_targetPath, targetPath, StringComparison.Ordinal);

        public void Dispose()
        {
            UnsubscribeSourceTree();
            HostRegistries.Data.ItemChanged -= OnTargetChanged;
            HostRegistries.Data.Remove(_targetPath);
        }

        private void OnSourceChanged(object? sender, ItemChangedEventArgs e)
        {
            if (_isSyncingFromTarget)
            {
                return;
            }

            if (IsStructuralParameter(e.ParameterName))
            {
                return;
            }

            if (!HostRegistries.Data.TryResolve(_targetPath, out var target) || target is null)
            {
                return;
            }

            _isSyncingFromSource = true;
            try
            {
                if (!string.Equals(e.Item.Path, _source.Path, StringComparison.Ordinal))
                {
                    if (TryGetSourceRelativePath(e.Item, out var relativePath))
                    {
                        SyncSourceChildChangeToTarget(relativePath, e);
                    }

                    return;
                }

                var parameterName = e.ParameterName;
                if (string.Equals(parameterName, "Value", StringComparison.Ordinal))
                {
                    var valueTimestamp = _source.Params.Has("Value") ? _source.Params["Value"].LastUpdate : (ulong?)null;
                    HostRegistries.Data.UpdateValue(_targetPath, _source.Value, valueTimestamp);
                    return;
                }

                if (_source.Params.Has(parameterName) && target.Params.Has(parameterName))
                {
                    var sourceParameter = _source.Params[parameterName];
                    HostRegistries.Data.UpdateParameter(_targetPath, parameterName, sourceParameter.Value, sourceParameter.LastUpdate);
                    return;
                }

                var snapshot = _source.Clone().Repath(_targetPath);
                HostRegistries.Data.UpsertSnapshot(_targetPath, snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
            }
            finally
            {
                _isSyncingFromSource = false;
            }
        }

        private void SyncSourceChildChangeToTarget(string relativePath, ItemChangedEventArgs e)
        {
            var targetChildPath = $"{_targetPath}.{relativePath}";
            var parameterName = e.ParameterName;
            if (string.Equals(parameterName, "Value", StringComparison.Ordinal))
            {
                var valueTimestamp = e.Item.Params.Has("Value") ? e.Item.Params["Value"].LastUpdate : (ulong?)null;
                if (!HostRegistries.Data.UpdateValue(targetChildPath, e.Item.Value, valueTimestamp))
                {
                    var treeSnapshot = _source.Clone().Repath(_targetPath);
                    HostRegistries.Data.UpsertSnapshot(_targetPath, treeSnapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(parameterName)
                && !IsStructuralParameter(parameterName)
                && e.Item.Params.Has(parameterName))
            {
                var sourceParameter = e.Item.Params[parameterName];
                if (!HostRegistries.Data.UpdateParameter(targetChildPath, parameterName, sourceParameter.Value, sourceParameter.LastUpdate))
                {
                    var treeSnapshot = _source.Clone().Repath(_targetPath);
                    HostRegistries.Data.UpsertSnapshot(_targetPath, treeSnapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
                }
            }
        }

        private bool TryGetSourceRelativePath(Item item, out string relativePath)
        {
            relativePath = string.Empty;
            var sourcePath = _source.Path;
            var itemPath = item.Path;
            if (string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(itemPath)
                || !itemPath.StartsWith(sourcePath + ".", StringComparison.Ordinal))
            {
                return false;
            }

            relativePath = itemPath[(sourcePath.Length + 1)..];
            return !string.IsNullOrWhiteSpace(relativePath);
        }

        private void OnTargetChanged(object? sender, DataChangedEventArgs e)
        {
            if (_isSyncingFromSource)
            {
                return;
            }

            var isDirectTarget = string.Equals(e.Key, _targetPath, StringComparison.Ordinal);
            var isChildTarget = e.Key.StartsWith(_targetPath + ".", StringComparison.Ordinal);
            if (!isDirectTarget && !isChildTarget)
            {
                return;
            }

            _isSyncingFromTarget = true;
            try
            {
                if (isChildTarget)
                {
                    ApplyChildTargetChange(e.Key[(_targetPath.Length + 1)..], e);
                    return;
                }

                if (string.Equals(e.ParameterName, "Value", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
                {
                    SetItemValueIfChanged(_source, e.Item.Value);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(e.ParameterName)
                    && !IsStructuralParameter(e.ParameterName)
                    && e.Item.Params.Has(e.ParameterName))
                {
                    SetParameterValueIfChanged(_source.Params[e.ParameterName], e.Item.Params[e.ParameterName].Value);
                    return;
                }

                ApplySnapshotToSource(_source, e.Item);
            }
            finally
            {
                _isSyncingFromTarget = false;
            }
        }

        private void ApplyChildTargetChange(string relativePath, DataChangedEventArgs e)
        {
            var current = _source;
            foreach (var segment in SplitPathSegments(relativePath))
            {
                if (!current.Has(segment))
                {
                    return;
                }

                current = current[segment];
            }

            if (string.Equals(e.ParameterName, "Value", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
            {
                SetItemValueIfChanged(current, e.Item.Value);
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.ParameterName)
                && !IsStructuralParameter(e.ParameterName)
                && e.Item.Params.Has(e.ParameterName)
                && current.Params.Has(e.ParameterName))
            {
                SetParameterValueIfChanged(current.Params[e.ParameterName], e.Item.Params[e.ParameterName].Value);
            }
        }

        private static void ApplySnapshotToSource(Item sourceItem, Item snapshotItem)
        {
            foreach (var parameterEntry in snapshotItem.Params.GetDictionary())
            {
                if (IsStructuralParameter(parameterEntry.Key))
                {
                    continue;
                }

                SetParameterValueIfChanged(sourceItem.Params[parameterEntry.Key], parameterEntry.Value.Value);
            }

            foreach (var childEntry in snapshotItem.GetDictionary())
            {
                var sourceChild = sourceItem[childEntry.Key];
                ApplySnapshotToSource(sourceChild, childEntry.Value);
            }
        }

        private static bool IsStructuralParameter(string? parameterName)
            => string.Equals(parameterName, "Path", StringComparison.Ordinal)
                || string.Equals(parameterName, "Name", StringComparison.Ordinal);

        private static void SetItemValueIfChanged(Item item, object? value)
        {
            if (ValuesEqual(item.Value, value))
            {
                return;
            }

            item.Value = value!;
        }

        private static void SetParameterValueIfChanged(Parameter parameter, object? value)
        {
            if (ValuesEqual(parameter.Value, value))
            {
                return;
            }

            parameter.Value = value!;
        }

        private static bool ValuesEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            if (left is double leftDouble && right is double rightDouble)
            {
                return leftDouble.Equals(rightDouble) || (double.IsNaN(leftDouble) && double.IsNaN(rightDouble));
            }

            if (left is float leftFloat && right is float rightFloat)
            {
                return leftFloat.Equals(rightFloat) || (float.IsNaN(leftFloat) && float.IsNaN(rightFloat));
            }

            return Equals(left, right);
        }

        private void SubscribeSourceTree(Item item)
        {
            _subscribedSourceItems.Add(item);
            item.Changed += OnSourceChanged;

            foreach (var child in item.GetDictionary().Values)
            {
                SubscribeSourceTree(child);
            }
        }

        private void UnsubscribeSourceTree()
        {
            foreach (var item in _subscribedSourceItems)
            {
                item.Changed -= OnSourceChanged;
            }

            _subscribedSourceItems.Clear();
        }

        private static IEnumerable<string> SplitPathSegments(string value)
            => value.Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '.').Replace('/', '.').Trim('.');
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NormalizeProjectRoot(string? value)
        => StudioRootSegment;
}
