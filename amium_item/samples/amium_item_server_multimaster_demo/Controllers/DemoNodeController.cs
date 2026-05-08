using System.Globalization;
using Amium.Item.Client.Mqtt;
using Amium.Item.Server.MultimasterDemo.Models;
using Amium.Items;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Server.MultimasterDemo.Controllers;

internal sealed class DemoNodeController : IAsyncDisposable
{
    private static readonly TimeSpan DynamicUpdateInterval = TimeSpan.FromMilliseconds(900);
    private const string SharedRemoteClientId = "shared";

    private readonly object _sync = new();
    private readonly string _nodeId;
    private readonly string _displayName;
    private readonly int _nodeIndex;
    private readonly string _host;
    private readonly int _port;
    private readonly string _baseTopic;
    private readonly IReadOnlyList<(string NodeId, string DisplayName)> _allNodes;
    private readonly IReadOnlyDictionary<string, string> _publisherClientIdsByNodeId;
    private readonly ItemModel _dynamicItem;
    private readonly ItemModel _writeTestItem;
    private readonly Dictionary<string, ItemModel> _publishedItemsByPath;

    private CancellationTokenSource? _runCancellation;
    private Task? _dynamicLoopTask;
    private MqttItemClientSession? _publisherSession;
    private MqttRemoteItemClient? _observerClient;
    private DemoNodeSnapshot _snapshot;

    internal DemoNodeController(
        string nodeId,
        string displayName,
        int nodeIndex,
        string host,
        int port,
        string baseTopic,
        IReadOnlyList<(string NodeId, string DisplayName)> allNodes,
        IReadOnlyDictionary<string, string> publisherClientIdsByNodeId)
    {
        _nodeId = nodeId;
        _displayName = displayName;
        _nodeIndex = nodeIndex;
        _host = host;
        _port = port;
        _baseTopic = baseTopic;
        _allNodes = allNodes;
        _publisherClientIdsByNodeId = publisherClientIdsByNodeId;

        _dynamicItem = new ItemModel("dynamic_value", 0.0).Repath(GetDynamicItemPath(nodeId));
        _dynamicItem.Properties["format"].Value = "0.000";
        _dynamicItem.Properties["description"].Value = $"Live value source for {displayName}.";

        _writeTestItem = new ItemModel("write_test", "ready").Repath(GetWriteTestItemPath(nodeId));
        _writeTestItem.Properties["writable"].Value = true;
        _writeTestItem.Properties["description"].Value = $"Cross-node write probe for {displayName}.";

        _publishedItemsByPath = new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase);
        ResetPublishedItems();

        _snapshot = new DemoNodeSnapshot(
            NodeId: _nodeId,
            DisplayName: _displayName,
            IsConnected: false,
            StatusText: "Stopped",
            LocalDynamicValue: 0.0,
            LocalDynamicUpdatedUtc: null,
            LocalDynamicSequence: 0,
            LocalWriteTestValue: Convert.ToString(_writeTestItem.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            LocalWriteUpdatedUtc: null,
            LocalWriteSequence: 0,
            ObservedNodes: CreateEmptyObservedNodes());
    }

    internal event EventHandler<DemoNodeSnapshot>? SnapshotChanged;

    internal event EventHandler<DemoNodeEvent>? EventLogged;

    internal DemoNodeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    internal bool IsRunning => _dynamicLoopTask is not null;

    internal string NodeId => _nodeId;

    internal string DisplayName => _displayName;

    internal static string GetDynamicItemPath(string nodeId) => $"nodes.{nodeId}.dynamic_value";

    internal static string GetWriteTestItemPath(string nodeId) => $"nodes.{nodeId}.write_test";

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            LogEvent("Node is already running.");
            return;
        }

        _publisherSession = new MqttItemClientSession(CreatePublisherOptions());
        _observerClient = new MqttRemoteItemClient(CreateObserverOptions());
        _observerClient.RemoteItemsChanged += HandleRemoteItemsChanged;
        _observerClient.Diagnostic += HandleDiagnostic;

        await _publisherSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _observerClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _publisherSession.PublishSnapshotAsync(_dynamicItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _publisherSession.PublishSnapshotAsync(_writeTestItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        UpdateSnapshot(snapshot => snapshot with
        {
            IsConnected = true,
            StatusText = "Connected",
        });
        RefreshObservedNodes();

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _dynamicLoopTask = RunDynamicLoopAsync(_runCancellation.Token);
        LogEvent($"{_displayName} connected on {_host}:{_port} with base topic '{_baseTopic}'.");
    }

    internal async Task StopAsync()
    {
        if (_dynamicLoopTask is not null)
        {
            try
            {
                if (_runCancellation is not null)
                {
                    await _runCancellation.CancelAsync().ConfigureAwait(false);
                }

                await _dynamicLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _runCancellation?.Dispose();
                _runCancellation = null;
                _dynamicLoopTask = null;
            }
        }

        if (_observerClient is not null)
        {
            _observerClient.RemoteItemsChanged -= HandleRemoteItemsChanged;
            _observerClient.Diagnostic -= HandleDiagnostic;
            await _observerClient.DisposeAsync().ConfigureAwait(false);
            _observerClient = null;
        }

        if (_publisherSession is not null)
        {
            await _publisherSession.DisposeAsync().ConfigureAwait(false);
            _publisherSession = null;
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            IsConnected = false,
            StatusText = "Stopped",
            ObservedNodes = CreateEmptyObservedNodes(),
        });
        ResetPublishedItems();
        LogEvent($"{_displayName} stopped.");
    }

    internal async Task WriteTestAsync(string value, CancellationToken cancellationToken = default)
    {
        if (_publisherSession is null)
        {
            throw new InvalidOperationException($"{_displayName} is not connected.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        _writeTestItem.Value = value;
        await _publisherSession.UpdateValueAsync(_writeTestItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        UpdateSnapshot(snapshot => snapshot with
        {
            LocalWriteTestValue = value,
            LocalWriteUpdatedUtc = timestamp,
            LocalWriteSequence = snapshot.LocalWriteSequence + 1,
        });
        RefreshObservedNodes();
        LogEvent($"write_test published: {value}");
    }

    internal async Task PublishRuntimeItemAsync(
        string path,
        string value,
        bool writable,
        CancellationToken cancellationToken = default)
    {
        var session = GetRequiredPublisherSession();
        var normalizedPath = NormalizePath(path);
        var item = new ItemModel(GetItemName(normalizedPath), value).Repath(normalizedPath);
        item.Properties["description"].Value = $"Self-test runtime item owned by {_displayName}.";

        if (writable)
        {
            item.Properties["writable"].Value = true;
        }

        await session.PublishSnapshotAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        RegisterPublishedItem(item);
        LogEvent($"Runtime item published: {normalizedPath} = {FormatValue(value)} (writable={writable.ToString(CultureInfo.InvariantCulture)})");
    }

    internal async Task UpdateOwnedItemAsync(string path, string value, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredPublisherSession();
        var item = GetRequiredPublishedItem(path);
        item.Value = value;

        await session.UpdateValueAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        LogEvent($"Owned item updated: {NormalizePath(path)} = {FormatValue(value)}");
    }

    internal async Task WriteRemoteValueAsync(string path, string value, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredPublisherSession();
        var normalizedPath = NormalizePath(path);
        var item = new ItemModel(GetItemName(normalizedPath), value).Repath(normalizedPath);

        await session.UpdateValueAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        LogEvent($"Remote write published: {normalizedPath} = {FormatValue(value)}");
    }

    internal ObservedItemState GetObservedItemState(string path)
    {
        var normalizedPath = NormalizePath(path);
        var observer = _observerClient;
        if (observer is null)
        {
            return new ObservedItemState(_nodeId, normalizedPath, false, "(missing)", null);
        }

        var snapshots = observer.GetRemoteItemSnapshots();
        if (snapshots.TryGetValue(SharedRemoteClientId, out var sharedRoot)
            && TryFindItem(sharedRoot, normalizedPath, out var sharedItem))
        {
            return new ObservedItemState(_nodeId, normalizedPath, true, FormatValue(sharedItem.Value), GetReadTimestamp(sharedItem));
        }

        foreach (var root in snapshots.Values)
        {
            if (TryFindItem(root, normalizedPath, out var item))
            {
                return new ObservedItemState(_nodeId, normalizedPath, true, FormatValue(item.Value), GetReadTimestamp(item));
            }
        }

        return new ObservedItemState(_nodeId, normalizedPath, false, "(missing)", null);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private MqttItemClientOptions CreatePublisherOptions()
        => new()
        {
            Host = _host,
            Port = _port,
            BaseTopic = _baseTopic,
            ClientId = _publisherClientIdsByNodeId[_nodeId],
        };

    private MqttItemClientOptions CreateObserverOptions()
        => new()
        {
            Host = _host,
            Port = _port,
            BaseTopic = _baseTopic,
            ClientId = $"{_nodeId}-observer",
        };

    private async Task RunDynamicLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(DynamicUpdateInterval);
        var sequence = 0;

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_publisherSession is null)
            {
                return;
            }

            sequence++;
            var value = Math.Round(
                (_nodeIndex + 1) * 100.0 + sequence * 0.5 + Math.Sin(sequence * 0.45 + _nodeIndex) * 5.0,
                digits: 3);
            var timestamp = DateTimeOffset.UtcNow;
            _dynamicItem.Value = value;

            await _publisherSession.UpdateValueAsync(_dynamicItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

            UpdateSnapshot(snapshot => snapshot with
            {
                LocalDynamicValue = value,
                LocalDynamicUpdatedUtc = timestamp,
                LocalDynamicSequence = snapshot.LocalDynamicSequence + 1,
            });
            RefreshObservedNodes();
        }
    }

    private void HandleRemoteItemsChanged()
    {
        RefreshObservedNodes();
    }

    private void HandleDiagnostic(string message)
    {
        LogEvent($"Diagnostic: {message}");
    }

    private void RefreshObservedNodes()
    {
        var observer = _observerClient;
        if (observer is null)
        {
            return;
        }

        var remoteSnapshots = observer.GetRemoteItemSnapshots();
        ItemModel? sharedRoot = null;
        remoteSnapshots.TryGetValue(SharedRemoteClientId, out sharedRoot);
        var previousSnapshot = Snapshot;
        var observedNodes = new List<DemoObservedNodeState>(_allNodes.Count);

        foreach (var node in _allNodes)
        {
            var previousNodeState = previousSnapshot.ObservedNodes.FirstOrDefault(entry => string.Equals(entry.NodeId, node.NodeId, StringComparison.OrdinalIgnoreCase));
            var dynamicValue = CreateObservedValue(
                root: sharedRoot,
                path: GetDynamicItemPath(node.NodeId),
                sourceClientId: node.NodeId,
                previous: previousNodeState?.DynamicValue);
            var writeValue = CreateObservedValue(
                root: sharedRoot,
                path: GetWriteTestItemPath(node.NodeId),
                sourceClientId: node.NodeId,
                previous: previousNodeState?.WriteTestValue);

            observedNodes.Add(new DemoObservedNodeState(node.NodeId, node.DisplayName, dynamicValue, writeValue));
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            IsConnected = IsRunning && observer.IsConnected,
            StatusText = IsRunning
                ? observer.IsConnected ? "Connected" : "Publisher running / observer reconnecting"
                : "Stopped",
            ObservedNodes = observedNodes,
        });
    }

    private DemoObservedValue CreateObservedValue(
        ItemModel? root,
        string path,
        string sourceClientId,
        DemoObservedValue? previous)
    {
        if (!TryFindItem(root, path, out var item))
        {
            return new DemoObservedValue(
                ValueText: "(missing)",
                LastUpdatedUtc: previous?.LastUpdatedUtc,
                UpdateCount: previous?.UpdateCount ?? 0,
                SourceClientId: sourceClientId,
                IsAvailable: false);
        }

        var valueText = FormatValue(item.Value);
        var lastUpdatedUtc = GetReadTimestamp(item);
        var updateCount = previous is null
            ? 1
            : HasObservedChange(previous, valueText, lastUpdatedUtc)
                ? previous.UpdateCount + 1
                : previous.UpdateCount;

        return new DemoObservedValue(
            ValueText: valueText,
            LastUpdatedUtc: lastUpdatedUtc,
            UpdateCount: updateCount,
            SourceClientId: sourceClientId,
            IsAvailable: true);
    }

    private void UpdateSnapshot(Func<DemoNodeSnapshot, DemoNodeSnapshot> updater)
    {
        DemoNodeSnapshot snapshot;
        lock (_sync)
        {
            _snapshot = updater(_snapshot);
            snapshot = _snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private IReadOnlyList<DemoObservedNodeState> CreateEmptyObservedNodes()
        => _allNodes
            .Select(node => new DemoObservedNodeState(
                NodeId: node.NodeId,
                DisplayName: node.DisplayName,
                DynamicValue: new DemoObservedValue("(missing)", null, 0, _publisherClientIdsByNodeId[node.NodeId], false),
                WriteTestValue: new DemoObservedValue("(missing)", null, 0, _publisherClientIdsByNodeId[node.NodeId], false)))
            .ToArray();

    private void LogEvent(string message)
    {
        EventLogged?.Invoke(
            this,
            new DemoNodeEvent(
                NodeId: _nodeId,
                DisplayName: _displayName,
                TimestampUtc: DateTimeOffset.UtcNow,
                Message: message));
    }

    private static bool HasObservedChange(DemoObservedValue previous, string valueText, DateTimeOffset? lastUpdatedUtc)
        => !string.Equals(previous.ValueText, valueText, StringComparison.Ordinal)
           || previous.LastUpdatedUtc != lastUpdatedUtc;

    private static DateTimeOffset? GetReadTimestamp(ItemModel item)
    {
        if (item.Properties.Has("read"))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)item.Properties["read"].LastUpdate);
        }

        if (item.Properties.Has("value"))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)item.Properties["value"].LastUpdate);
        }

        return null;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "(null)",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static bool TryFindItem(ItemModel? root, string path, out ItemModel item)
    {
        item = null!;
        if (root is null)
        {
            return false;
        }

        var current = root;
        foreach (var segment in ItemPath.Normalize(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!current.GetDictionary().TryGetValue(segment, out var child))
            {
                return false;
            }

            current = child;
        }

        item = current;
        return true;
    }

    private MqttItemClientSession GetRequiredPublisherSession()
        => _publisherSession ?? throw new InvalidOperationException($"{_displayName} is not connected.");

    private MqttRemoteItemClient GetRequiredObserverClient()
        => _observerClient ?? throw new InvalidOperationException($"{_displayName} observer is not connected.");

    private ItemModel GetRequiredPublishedItem(string path)
    {
        var normalizedPath = NormalizePath(path);
        lock (_sync)
        {
            if (_publishedItemsByPath.TryGetValue(normalizedPath, out var item))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"{_displayName} does not own item '{normalizedPath}'.");
    }

    private void RegisterPublishedItem(ItemModel item)
    {
        var normalizedPath = NormalizePath(item.Path ?? throw new InvalidOperationException("Published item path is missing."));
        lock (_sync)
        {
            _publishedItemsByPath[normalizedPath] = item;
        }
    }

    private void ResetPublishedItems()
    {
        lock (_sync)
        {
            _publishedItemsByPath.Clear();
            _publishedItemsByPath[NormalizePath(_dynamicItem.Path ?? GetDynamicItemPath(_nodeId))] = _dynamicItem;
            _publishedItemsByPath[NormalizePath(_writeTestItem.Path ?? GetWriteTestItemPath(_nodeId))] = _writeTestItem;
        }
    }

    private static string NormalizePath(string path) => ItemPath.Normalize(path);

    private static string GetItemName(string path)
        => NormalizePath(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
}
