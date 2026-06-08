using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Host.Registries;

namespace HornetStudio.Host;

public sealed class UiFolderContext : IDisposable
{
    private const string StudioRootSegment = "studio";
    private readonly object _linksLock = new();
    private readonly List<AttachedItemLink> _links = [];
    private readonly string _folderPath;
    private bool _disposed;

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

    public ItemModel Attach(ItemModel source, string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var itemName = string.IsNullOrWhiteSpace(alias) ? source.Name : NormalizePath(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);

        var targetPath = $"{_folderPath}.{itemName}";

        lock (_linksLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var link in _links)
            {
                if (link.Matches(source, targetPath))
                {
                    return link.AttachedItem;
                }
            }

            var attached = ItemExtension.CloneWithPath(source, targetPath);
            _links.Add(new AttachedItemLink(source, attached, targetPath));
            return attached;
        }
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
        AttachedItemLink[] links;
        lock (_linksLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            links = [.. _links];
            _links.Clear();
        }

        foreach (var link in links)
        {
            link.Dispose();
        }
    }

    private sealed class AttachedItemLink : IDisposable
    {
        private static readonly TimeSpan CoalescedPublishInterval = TimeSpan.FromMilliseconds(100);
        private const string UdlRuntimeRootPrefix = "runtime.udl_client.";
        private readonly ItemModel _attachedItem;
        private readonly ItemModel _source;
        private readonly List<DataRegistryValueReference> _registeredValueReferences = [];
        private readonly HashSet<string> _registeredValueReferenceKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ItemModel> _subscribedSourceItems = [];
        private readonly Dictionary<string, PendingSourcePublication> _pendingSourcePublications = new(StringComparer.Ordinal);
        private readonly object _pendingSourcePublicationsLock = new();
        private bool _isSyncingFromSource;
        private bool _isSyncingFromTarget;
        private bool _initialTargetSnapshotObserved;
        private readonly bool _coalesceSourcePublishes;
        private readonly string _targetPath;
        private readonly Timer? _pendingPublishTimer;
        private int _disposed;
        private int _flushActive;

        public AttachedItemLink(ItemModel source, ItemModel attachedItem, string targetPath)
        {
            _source = source;
            _attachedItem = attachedItem;
            _targetPath = targetPath;
            _coalesceSourcePublishes = ShouldCoalesceSourcePublishes(source);
            _pendingPublishTimer = _coalesceSourcePublishes
                ? new Timer(static state => ((AttachedItemLink)state!).FlushPendingSourcePublications(), this, CoalescedPublishInterval, CoalescedPublishInterval)
                : null;
            RegisterValueReferences();
            SubscribeSourceTree(_source);
            HostRegistries.Data.ItemChanged += OnTargetChanged;
        }

        public ItemModel AttachedItem => _attachedItem;

        public bool Matches(ItemModel source, string targetPath)
            => ReferenceEquals(_source, source)
                && string.Equals(_targetPath, targetPath, StringComparison.Ordinal);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _pendingPublishTimer?.Dispose();
            ClearPendingSourcePublications();
            UnsubscribeSourceTree();
            HostRegistries.Data.ItemChanged -= OnTargetChanged;
            RemoveValueReferences();
            HostRegistries.Data.Remove(_targetPath);
        }

        private void OnSourceChanged(object? sender, ItemChangedEventArgs e)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (_isSyncingFromTarget)
            {
                return;
            }

            if (IsStructuralParameter(e.PropertyName))
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

                var parameterName = e.PropertyName;
                if (string.Equals(parameterName, "value", StringComparison.Ordinal))
                {
                    var valueTimestamp = GetItemEpoch(_source);
                    if (TryQueueSourceValueUpdate(_targetPath, _source.Value, valueTimestamp))
                    {
                        return;
                    }

                    PublishValueUpdate(_targetPath, _source.Value, valueTimestamp);
                    return;
                }

                if (_source.Properties.Has(parameterName) && target.Properties.Has(parameterName))
                {
                    var sourceParameter = _source.Properties[parameterName];
                    if (TryQueueSourcePropertyUpdate(_targetPath, parameterName, sourceParameter.Value, GetItemEpoch(_source)))
                    {
                        return;
                    }

                    PublishPropertyUpdate(_targetPath, parameterName, sourceParameter.Value, GetItemEpoch(_source));
                    return;
                }

                ClearPendingSourcePublications();
                var snapshot = ItemExtension.CloneWithPath(_source, _targetPath);
                PublishSnapshotUpsert(_targetPath, snapshot);
            }
            finally
            {
                _isSyncingFromSource = false;
            }
        }

        private void SyncSourceChildChangeToTarget(string relativePath, ItemChangedEventArgs e)
        {
            var targetChildPath = $"{_targetPath}.{relativePath}";
            var parameterName = e.PropertyName;
            if (string.Equals(parameterName, "value", StringComparison.Ordinal))
            {
                var valueTimestamp = GetItemEpoch(e.Item);
                if (TryQueueSourceValueUpdate(targetChildPath, e.Item.Value, valueTimestamp))
                {
                    return;
                }

                if (!PublishValueUpdate(targetChildPath, e.Item.Value, valueTimestamp))
                {
                    ClearPendingSourcePublications();
                    var treeSnapshot = ItemExtension.CloneWithPath(_source, _targetPath);
                    PublishSnapshotUpsert(_targetPath, treeSnapshot);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(parameterName)
                && !IsStructuralParameter(parameterName)
                && e.Item.Properties.Has(parameterName))
            {
                var sourceParameter = e.Item.Properties[parameterName];
                var epoch = GetItemEpoch(e.Item);
                if (TryQueueSourcePropertyUpdate(targetChildPath, parameterName, sourceParameter.Value, epoch))
                {
                    return;
                }

                if (HasRegisteredValueReference(targetChildPath, parameterName))
                {
                    if (!HostRegistries.Data.NotifyReferencedPropertyChanged(targetChildPath, parameterName, epoch))
                    {
                        ClearPendingSourcePublications();
                        var treeSnapshot = ItemExtension.CloneWithPath(_source, _targetPath);
                        PublishSnapshotUpsert(_targetPath, treeSnapshot);
                    }

                    return;
                }

                if (!PublishPropertyUpdate(targetChildPath, parameterName, sourceParameter.Value, epoch))
                {
                    ClearPendingSourcePublications();
                    var treeSnapshot = ItemExtension.CloneWithPath(_source, _targetPath);
                    PublishSnapshotUpsert(_targetPath, treeSnapshot);
                }
            }
        }

        private bool TryQueueSourceValueUpdate(string path, object? value, ulong? epoch)
            => TryQueueSourcePublication(path, new PendingSourcePublication(
                Path: path,
                ChangeKind: DataChangeKind.ValueUpdated,
                ParameterName: null,
                Value: value,
                Epoch: epoch,
                UsesRegisteredReference: false));

        private bool TryQueueSourcePropertyUpdate(string path, string parameterName, object? value, ulong? epoch)
            => TryQueueSourcePublication(path, new PendingSourcePublication(
                Path: path,
                ChangeKind: DataChangeKind.PropertyUpdated,
                ParameterName: parameterName,
                Value: HasRegisteredValueReference(path, parameterName) ? null : value,
                Epoch: epoch,
                UsesRegisteredReference: HasRegisteredValueReference(path, parameterName)));

        private bool TryQueueSourcePublication(string path, PendingSourcePublication publication)
        {
            if (!_coalesceSourcePublishes)
            {
                return false;
            }

            lock (_pendingSourcePublicationsLock)
            {
                _pendingSourcePublications[BuildPendingPublicationKey(path, publication.ParameterName)] = publication;
            }

            return true;
        }

        private void FlushPendingSourcePublications()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _flushActive, 1) != 0)
            {
                return;
            }

            try
            {
            if (_isSyncingFromTarget)
            {
                return;
            }

            PendingSourcePublication[] pendingPublications;
            lock (_pendingSourcePublicationsLock)
            {
                if (_pendingSourcePublications.Count == 0)
                {
                    return;
                }

                pendingPublications = [.. _pendingSourcePublications.Values];
                _pendingSourcePublications.Clear();
            }

            _isSyncingFromSource = true;
            try
            {
                var requiresSnapshotRefresh = false;
                foreach (var publication in pendingPublications)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        return;
                    }

                    switch (publication.ChangeKind)
                    {
                        case DataChangeKind.ValueUpdated:
                            if (!PublishValueUpdate(publication.Path, publication.Value, publication.Epoch))
                            {
                                requiresSnapshotRefresh = true;
                            }

                            break;
                        case DataChangeKind.PropertyUpdated:
                            if (string.IsNullOrWhiteSpace(publication.ParameterName))
                            {
                                requiresSnapshotRefresh = true;
                                break;
                            }

                            if (publication.UsesRegisteredReference)
                            {
                                if (!HostRegistries.Data.NotifyReferencedPropertyChanged(publication.Path, publication.ParameterName, publication.Epoch))
                                {
                                    requiresSnapshotRefresh = true;
                                }

                                break;
                            }

                            if (!PublishPropertyUpdate(publication.Path, publication.ParameterName, publication.Value, publication.Epoch))
                            {
                                requiresSnapshotRefresh = true;
                            }

                            break;
                    }
                }

                if (requiresSnapshotRefresh)
                {
                    ClearPendingSourcePublications();
                    var treeSnapshot = ItemExtension.CloneWithPath(_source, _targetPath);
                    PublishSnapshotUpsert(_targetPath, treeSnapshot);
                }
            }
            finally
            {
                _isSyncingFromSource = false;
            }
            }
            finally
            {
                Interlocked.Exchange(ref _flushActive, 0);
            }
        }

        private void ClearPendingSourcePublications()
        {
            lock (_pendingSourcePublicationsLock)
            {
                _pendingSourcePublications.Clear();
            }
        }

        private void DiscardPendingSourcePublicationsForPath(string path)
        {
            lock (_pendingSourcePublicationsLock)
            {
                if (_pendingSourcePublications.Count == 0)
                {
                    return;
                }

                var exactKey = BuildPendingPublicationKey(path, parameterName: null);
                _pendingSourcePublications.Remove(exactKey);

                var prefixedPath = path + ".";
                var preserveDerivedBitsPrefix = ShouldPreserveDerivedBitsPublications()
                    ? path + ".bits."
                    : string.Empty;
                var keysToRemove = new List<string>();
                foreach (var key in _pendingSourcePublications.Keys)
                {
                    if (key.StartsWith(prefixedPath, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(preserveDerivedBitsPrefix)
                            && key.StartsWith(preserveDerivedBitsPrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _pendingSourcePublications.Remove(key);
                }
            }
        }

        private bool ShouldPreserveDerivedBitsPublications()
            => !string.IsNullOrWhiteSpace(_source.Path)
               && _source.Path.StartsWith(UdlRuntimeRootPrefix, StringComparison.Ordinal);

        private static string BuildPendingPublicationKey(string path, string? parameterName)
            => string.IsNullOrWhiteSpace(parameterName)
                ? path
                : string.Concat(path, "|", parameterName);

        private static bool ShouldCoalesceSourcePublishes(ItemModel source)
            => !string.IsNullOrWhiteSpace(source.Path)
                && source.Path.StartsWith(UdlRuntimeRootPrefix, StringComparison.Ordinal);

        private static bool PublishValueUpdate(string path, object? value, ulong? epoch)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublished(path, DataChangeKind.ValueUpdated);
            var updated = HostRegistries.Data.UpdateValue(path, value, epoch);
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublishCompleted(path, DataChangeKind.ValueUpdated, parameterName: null, Stopwatch.GetElapsedTime(startTimestamp));
            return updated;
        }

        private static bool PublishPropertyUpdate(string path, string parameterName, object? value, ulong? epoch)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublished(path, DataChangeKind.PropertyUpdated, parameterName);
            var updated = HostRegistries.Data.UpdateProperty(path, parameterName, value, epoch);
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublishCompleted(path, DataChangeKind.PropertyUpdated, parameterName, Stopwatch.GetElapsedTime(startTimestamp));
            return updated;
        }

        private static void PublishSnapshotUpsert(string path, ItemModel snapshot)
        {
            if (HostRegistries.Data.TryResolve(path, out var existing) && existing is not null)
            {
                PreserveProtectedProperties(snapshot, existing);
            }

            var startTimestamp = Stopwatch.GetTimestamp();
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublished(path, DataChangeKind.SnapshotUpserted);
            HostRegistries.Data.UpsertSnapshot(path, snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
            DataRegistryDiagnosticsHooks.NotifyPublicDataPublishCompleted(path, DataChangeKind.SnapshotUpserted, parameterName: null, Stopwatch.GetElapsedTime(startTimestamp));
        }

        private static void PreserveProtectedProperties(ItemModel snapshot, ItemModel existing)
        {
            foreach (var propertyEntry in existing.Properties.GetDictionary())
            {
                if (HostRegistryPropertyPolicy.IsProtectedProperty(propertyEntry.Key)
                    && !snapshot.Properties.Has(propertyEntry.Key))
                {
                    snapshot.Properties[propertyEntry.Key].Value = propertyEntry.Value.Value;
                }
            }

            foreach (var childEntry in snapshot.GetDictionary())
            {
                if (existing.Has(childEntry.Key))
                {
                    PreserveProtectedProperties(childEntry.Value, existing[childEntry.Key]);
                }
            }
        }

        private bool TryGetSourceRelativePath(ItemModel item, out string relativePath)
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
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

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

            if (isDirectTarget
                && !_initialTargetSnapshotObserved
                && e.ChangeKind == DataChangeKind.SnapshotUpserted)
            {
                _initialTargetSnapshotObserved = true;
                return;
            }

            _isSyncingFromTarget = true;
            try
            {
                DiscardPendingSourcePublicationsForPath(e.Key);

                if (isChildTarget)
                {
                    var relativePath = e.Key[(_targetPath.Length + 1)..];
                    ApplyChildTargetChange(relativePath, e);
                    if (e.ChangeKind == DataChangeKind.SnapshotUpserted)
                    {
                        RepublishDerivedUdlChildValues(relativePath);
                    }

                    return;
                }

                if (string.Equals(e.ParameterName, "value", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
                {
                    SetItemValueIfChanged(_source, e.ItemModel.Value);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(e.ParameterName)
                    && !IsStructuralParameter(e.ParameterName)
                    && e.ItemModel.Properties.Has(e.ParameterName))
                {
                    SetParameterValueIfChanged(_source.Properties[e.ParameterName], e.ItemModel.Properties[e.ParameterName].Value);
                    return;
                }

                ApplySnapshotToSource(_source, e.ItemModel);
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

            if (string.Equals(e.ParameterName, "value", StringComparison.Ordinal) || e.ChangeKind == DataChangeKind.ValueUpdated)
            {
                SetItemValueIfChanged(current, e.ItemModel.Value);
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.ParameterName)
                && !IsStructuralParameter(e.ParameterName)
                && e.ItemModel.Properties.Has(e.ParameterName)
                && current.Properties.Has(e.ParameterName))
            {
                SetParameterValueIfChanged(current.Properties[e.ParameterName], e.ItemModel.Properties[e.ParameterName].Value);
            }
        }

        private void RepublishDerivedUdlChildValues(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || string.IsNullOrWhiteSpace(_source.Path)
                || !_source.Path.StartsWith(UdlRuntimeRootPrefix, StringComparison.Ordinal)
                || !TryResolveSourceRelativeChild(relativePath, out var sourceChild)
                || sourceChild is null
                || !sourceChild.Has("bits"))
            {
                return;
            }

            _isSyncingFromSource = true;
            try
            {
                foreach (var bitEntry in sourceChild["bits"].GetDictionary())
                {
                    var bitPath = string.Concat(_targetPath, ".", relativePath, ".bits.", bitEntry.Key);
                    PublishValueUpdate(bitPath, bitEntry.Value.Value, GetItemEpoch(bitEntry.Value));
                }
            }
            finally
            {
                _isSyncingFromSource = false;
            }
        }

        private bool TryResolveSourceRelativeChild(string relativePath, out ItemModel? item)
        {
            var current = _source;
            foreach (var segment in SplitPathSegments(relativePath))
            {
                if (!current.Has(segment))
                {
                    item = null;
                    return false;
                }

                current = current[segment];
            }

            item = current;
            return true;
        }

        private static void ApplySnapshotToSource(ItemModel sourceItem, ItemModel snapshotItem)
        {
            foreach (var parameterEntry in snapshotItem.Properties.GetDictionary())
            {
                if (IsStructuralParameter(parameterEntry.Key))
                {
                    continue;
                }

                SetParameterValueIfChanged(sourceItem.Properties[parameterEntry.Key], parameterEntry.Value.Value);
            }

            foreach (var childEntry in snapshotItem.GetDictionary())
            {
                if (ShouldSkipTargetToSourceSnapshotChild(sourceItem, childEntry.Key))
                {
                    continue;
                }

                var sourceChild = sourceItem[childEntry.Key];
                ApplySnapshotToSource(sourceChild, childEntry.Value);
            }
        }

        private static bool ShouldSkipTargetToSourceSnapshotChild(ItemModel sourceItem, string childName)
            => string.Equals(childName, "bits", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(sourceItem.Path)
               && sourceItem.Path.StartsWith(UdlRuntimeRootPrefix, StringComparison.Ordinal);

        private static bool IsStructuralParameter(string? parameterName)
            => string.Equals(parameterName, "Path", StringComparison.Ordinal)
                || string.Equals(parameterName, "Name", StringComparison.Ordinal);

        private static void SetItemValueIfChanged(ItemModel item, object? value)
        {
            if (ValuesEqual(item.Value, value))
            {
                return;
            }

            item.Value = value!;
        }

        private static void SetParameterValueIfChanged(ItemProperty parameter, object? value)
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

        private static ulong? GetItemEpoch(ItemModel item)
        {
            if (!item.Properties.Has("epoch"))
            {
                return null;
            }

            return item.Properties["epoch"].Value switch
            {
                ulong timestamp => timestamp,
                _ when ulong.TryParse(Convert.ToString(item.Properties["epoch"].Value, System.Globalization.CultureInfo.InvariantCulture), out ulong parsedTimestamp) => parsedTimestamp,
                _ => null,
            };
        }

        private void SubscribeSourceTree(ItemModel item)
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

        private void RegisterValueReferences()
        {
            if (!TryCreateUdlReadReference(_source, _targetPath, out var reference))
            {
                return;
            }

            if (!HostRegistries.Data.RegisterValueReference(reference))
            {
                return;
            }

            _registeredValueReferences.Add(reference);
            _registeredValueReferenceKeys.Add(BuildValueReferenceKey(reference.PublicItemPath, reference.PublicParameterName));
        }

        private void RemoveValueReferences()
        {
            foreach (var reference in _registeredValueReferences)
            {
                HostRegistries.Data.RemoveValueReference(reference.PublicItemPath, reference.PublicParameterName);
            }

            _registeredValueReferences.Clear();
            _registeredValueReferenceKeys.Clear();
        }

        private bool HasRegisteredValueReference(string publicItemPath, string publicParameterName)
            => _registeredValueReferenceKeys.Contains(BuildValueReferenceKey(publicItemPath, publicParameterName));

        private static bool TryCreateUdlReadReference(ItemModel source, string targetPath, out DataRegistryValueReference reference)
        {
            reference = null!;
            if (string.IsNullOrWhiteSpace(source.Path)
                || !source.Path.StartsWith(UdlRuntimeRootPrefix, StringComparison.Ordinal)
                || !source.Has("read")
                || string.IsNullOrWhiteSpace(source["read"].Path)
                || !source["read"].Properties.Has("read"))
            {
                return false;
            }

            reference = new DataRegistryValueReference(
                PublicItemPath: $"{targetPath}.read",
                PublicParameterName: "read",
                SourceItemPath: source["read"].Path!,
                SourceParameterName: "read");
            return true;
        }

        private static string BuildValueReferenceKey(string publicItemPath, string publicParameterName)
            => string.Concat(publicItemPath, "|", HostPathSegmentNormalizer.Normalize(publicParameterName));

        private sealed record PendingSourcePublication(string Path, DataChangeKind ChangeKind, string? ParameterName, object? Value, ulong? Epoch, bool UsesRegisteredReference);
    }

    private static string NormalizePath(string value)
    {
        var segments = value
            .Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HostPathSegmentNormalizer.Normalize)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment));
        return string.Join('.', segments);
    }

    private static string NormalizeProjectRoot(string? value)
        => StudioRootSegment;
}
