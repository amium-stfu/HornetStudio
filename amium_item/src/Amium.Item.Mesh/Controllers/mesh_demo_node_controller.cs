using System.Globalization;
using Amium.Item.Client.Mqtt;
using Amium.Item.Server.Mqtt;
using Amium.Item.Server.MultimasterDemo.Models;
using Amium.Items;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Server.MultimasterDemo.Controllers;

internal sealed class MeshDemoNodeController : IAsyncDisposable
{
    private const string SharedRemoteClientId = "shared";
    private static readonly TimeSpan DynamicUpdateInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MirrorRefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _mirrorRefreshGate = new(1, 1);
    private readonly MeshNodeDefinition _definition;
    private readonly IReadOnlyList<MeshNodeDefinition> _allNodes;
    private readonly Dictionary<string, MeshNodeDefinition> _nodeDefinitionsById;
    private readonly ItemModel _dynamicItem;
    private readonly ItemModel _staticItem;
    private readonly Dictionary<string, ItemModel> _ownedItemsByPath;
    private readonly Dictionary<string, MirroredItemState> _mirroredStatesBySourceAndPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MqttRemoteItemClient> _peerObservers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MqttItemClientSession> _peerWriterSessions = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _dynamicLoopCancellation;
    private Task? _dynamicLoopTask;
    private CancellationTokenSource? _mirrorLoopCancellation;
    private Task? _mirrorLoopTask;
    private MqttItemServerHost? _host;
    private MqttItemClientSession? _localPublisherSession;
    private MqttItemClientSession? _localMirrorSession;
    private MqttRemoteItemClient? _localObserver;
    private MeshNodeSnapshot _snapshot;

    internal MeshDemoNodeController(MeshNodeDefinition definition, IReadOnlyList<MeshNodeDefinition> allNodes)
    {
        _definition = definition;
        _allNodes = allNodes;
        _nodeDefinitionsById = allNodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);
        _dynamicItem = new ItemModel(definition.DynamicItemName, definition.DynamicItemName).Repath(definition.DynamicItemPath);
        _dynamicItem.Properties["description"].Value = $"Dynamic mesh source for {definition.DisplayName}.";

        _staticItem = new ItemModel(definition.StaticItemName, definition.StaticItemName).Repath(definition.StaticItemPath);
        _staticItem.Properties["description"].Value = $"Stable mesh value for {definition.DisplayName}.";
        _staticItem.Properties["writable"].Value = true;

        _ownedItemsByPath = new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase);
        RegisterOwnedItem(_dynamicItem);
        RegisterOwnedItem(_staticItem);

        _snapshot = new MeshNodeSnapshot(
            NodeId: _definition.NodeId,
            DisplayName: _definition.DisplayName,
            EndpointSummary: _definition.EndpointSummary,
            IsConnected: false,
            StatusText: "Stopped",
            LocalDynamicPath: _definition.DynamicItemPath,
            LocalDynamicValueText: _definition.DynamicItemName,
            LocalDynamicUpdatedUtc: null,
            LocalDynamicSequence: 0,
            LocalStaticPath: _definition.StaticItemPath,
            LocalStaticValueText: _definition.StaticItemName,
            LocalStaticUpdatedUtc: null,
            LocalStaticSequence: 0,
            VisibleItemsText: "(stopped)");
    }

    internal event EventHandler<MeshNodeSnapshot>? SnapshotChanged;

    internal event EventHandler<DemoNodeEvent>? EventLogged;

    internal string NodeId => _definition.NodeId;

    internal string DisplayName => _definition.DisplayName;

    internal MeshNodeDefinition Definition => _definition;

    internal MeshNodeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    internal bool IsRunning => _host?.IsRunning == true;

    internal async Task StartLocalServerAsync(CancellationToken cancellationToken = default)
    {
        if (_host?.IsRunning == true)
        {
            return;
        }

        _host = new MqttItemServerHost(
            new MqttItemServerOptions
            {
                Enabled = true,
                Host = _definition.Host,
                Port = _definition.Port,
                BaseTopic = _definition.BaseTopic,
                SubscriptionRootPath = "nodes",
                PublishHealth = false,
            });

        await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        LogEvent($"Local broker started on {_definition.EndpointSummary}.");
    }

    internal async Task StartLocalPublisherAsync(CancellationToken cancellationToken = default)
    {
        _localPublisherSession ??= new MqttItemClientSession(CreateOptions($"{_definition.NodeId}-publisher", _definition));
        _localMirrorSession ??= new MqttItemClientSession(CreateOptions($"{_definition.NodeId}-mirror", _definition));
        _localObserver ??= new MqttRemoteItemClient(CreateOptions($"{_definition.NodeId}-local-monitor", _definition));

        _localObserver.RemoteItemsChanged += HandleRemoteItemsChanged;
        _localObserver.Diagnostic += HandleDiagnostic;

        await _localPublisherSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _localMirrorSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _localObserver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _localPublisherSession.PublishSnapshotAsync(_dynamicItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _localPublisherSession.PublishSnapshotAsync(_staticItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        RefreshSnapshot();
        LogEvent($"Local publisher connected for {_definition.DisplayName}.");
    }

    internal async Task ConnectPeerObserversAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _allNodes.Where(node => !string.Equals(node.NodeId, _definition.NodeId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!_peerObservers.ContainsKey(peer.NodeId))
            {
                var observer = new MqttRemoteItemClient(CreateOptions($"{_definition.NodeId}-observe-{peer.NodeId}", peer));
                observer.RemoteItemsChanged += HandleRemoteItemsChanged;
                observer.Diagnostic += HandleDiagnostic;
                _peerObservers[peer.NodeId] = observer;
            }

            if (!_peerWriterSessions.ContainsKey(peer.NodeId))
            {
                _peerWriterSessions[peer.NodeId] = new MqttItemClientSession(CreateOptions($"{_definition.NodeId}-write-{peer.NodeId}", peer));
            }
        }

        foreach (var observer in _peerObservers.Values)
        {
            await observer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var writer in _peerWriterSessions.Values)
        {
            await writer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        await SynchronizeMirrorsAsync(cancellationToken).ConfigureAwait(false);

        if (_mirrorLoopTask is null)
        {
            _mirrorLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _mirrorLoopTask = RunMirrorLoopAsync(_mirrorLoopCancellation.Token);
        }

        RefreshSnapshot();
        LogEvent($"Peer observers connected for {_definition.DisplayName}.");
    }

    internal Task StartDynamicUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_dynamicLoopTask is not null)
        {
            return Task.CompletedTask;
        }

        _dynamicLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _dynamicLoopTask = RunDynamicLoopAsync(_dynamicLoopCancellation.Token);
        LogEvent($"Dynamic updates enabled for {_definition.DynamicItemPath}.");
        return Task.CompletedTask;
    }

    internal async Task StopAsync()
    {
        if (_mirrorLoopTask is not null)
        {
            try
            {
                if (_mirrorLoopCancellation is not null)
                {
                    await _mirrorLoopCancellation.CancelAsync().ConfigureAwait(false);
                }

                await _mirrorLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _mirrorLoopCancellation?.Dispose();
                _mirrorLoopCancellation = null;
                _mirrorLoopTask = null;
            }
        }

        if (_dynamicLoopTask is not null)
        {
            try
            {
                if (_dynamicLoopCancellation is not null)
                {
                    await _dynamicLoopCancellation.CancelAsync().ConfigureAwait(false);
                }

                await _dynamicLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _dynamicLoopCancellation?.Dispose();
                _dynamicLoopCancellation = null;
                _dynamicLoopTask = null;
            }
        }

        foreach (var observer in _peerObservers.Values)
        {
            observer.RemoteItemsChanged -= HandleRemoteItemsChanged;
            observer.Diagnostic -= HandleDiagnostic;
            await observer.DisposeAsync().ConfigureAwait(false);
        }

        _peerObservers.Clear();

        foreach (var writer in _peerWriterSessions.Values)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }

        _peerWriterSessions.Clear();

        if (_localObserver is not null)
        {
            _localObserver.RemoteItemsChanged -= HandleRemoteItemsChanged;
            _localObserver.Diagnostic -= HandleDiagnostic;
            await _localObserver.DisposeAsync().ConfigureAwait(false);
            _localObserver = null;
        }

        if (_localPublisherSession is not null)
        {
            await _localPublisherSession.DisposeAsync().ConfigureAwait(false);
            _localPublisherSession = null;
        }

        if (_localMirrorSession is not null)
        {
            await _localMirrorSession.DisposeAsync().ConfigureAwait(false);
            _localMirrorSession = null;
        }

        _mirroredStatesBySourceAndPath.Clear();

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            IsConnected = false,
            StatusText = "Stopped",
            VisibleItemsText = "(stopped)",
        });
        LogEvent($"{_definition.DisplayName} stopped.");
    }

    internal async Task PublishRuntimeItemAsync(string path, string value, bool writable, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredLocalPublisherSession();
        var normalizedPath = NormalizePath(path);
        var item = new ItemModel(GetItemName(normalizedPath), value).Repath(normalizedPath);
        item.Properties["description"].Value = $"Runtime mesh item owned by {_definition.DisplayName}.";
        if (writable)
        {
            item.Properties["writable"].Value = true;
        }

        await session.PublishSnapshotAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        RegisterOwnedItem(item);
        LogEvent($"Runtime item published: {normalizedPath} = {FormatValue(value)} (writable={writable.ToString(CultureInfo.InvariantCulture)})");
    }

    internal async Task UpdateOwnedItemAsync(string path, string value, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredLocalPublisherSession();
        var item = GetRequiredOwnedItem(path);
        item.Value = value;

        await session.UpdateValueAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        LogEvent($"Owned item updated: {NormalizePath(path)} = {FormatValue(value)}");
    }

    internal async Task WriteRemoteValueAsync(string targetNodeId, string path, string value, CancellationToken cancellationToken = default)
    {
        if (!_peerWriterSessions.TryGetValue(targetNodeId, out var session))
        {
            throw new InvalidOperationException($"{_definition.DisplayName} has no writer session for peer '{targetNodeId}'.");
        }

        var normalizedPath = NormalizePath(path);
        var item = new ItemModel(GetItemName(normalizedPath), value).Repath(normalizedPath);
        await session.UpdateValueAsync(item, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        LogEvent($"Peer write published to {targetNodeId}: {normalizedPath} = {FormatValue(value)}");
    }

    internal ObservedItemState GetObservedItemState(string path)
    {
        var normalizedPath = NormalizePath(path);
        var ownerNodeId = TryResolveOwnerNodeId(normalizedPath);

        if (ownerNodeId is not null && string.Equals(ownerNodeId, _definition.NodeId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateObservedItemState(_localObserver, normalizedPath);
        }

        if (ownerNodeId is not null && _peerObservers.TryGetValue(ownerNodeId, out var peerObserver))
        {
            return CreateObservedItemState(peerObserver, normalizedPath);
        }

        foreach (var observer in _peerObservers.Values)
        {
            var state = CreateObservedItemState(observer, normalizedPath);
            if (state.IsAvailable)
            {
                return state;
            }
        }

        return new ObservedItemState(_definition.NodeId, normalizedPath, false, "(missing)", null);
    }

    internal ObservedItemState GetBrokerVisibleItemState(string path)
        => CreateObservedItemState(_localObserver, NormalizePath(path));

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private static MqttItemClientOptions CreateOptions(string clientId, MeshNodeDefinition node)
        => new()
        {
            ClientId = clientId,
            Host = node.Host,
            Port = node.Port,
            BaseTopic = node.BaseTopic,
        };

    private async Task RunDynamicLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(DynamicUpdateInterval);
        var sequence = 0;

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var session = _localPublisherSession;
            if (session is null)
            {
                return;
            }

            sequence++;
            _dynamicItem.Value = $"{_definition.DynamicItemName}|{sequence:000}";
            await session.UpdateValueAsync(_dynamicItem, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunMirrorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(MirrorRefreshInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await SynchronizeMirrorsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void HandleRemoteItemsChanged()
    {
        _ = SynchronizeMirrorsAsync();
        RefreshSnapshot();
    }

    private void HandleDiagnostic(string message)
    {
        LogEvent($"Diagnostic: {message}");
    }

    private void RefreshSnapshot()
    {
        var localDynamic = CreateObservedItemState(_localObserver, _definition.DynamicItemPath);
        var localStatic = CreateObservedItemState(_localObserver, _definition.StaticItemPath);
        var previous = Snapshot;

        var nextDynamicSequence = HasObservedStateChanged(
            previousValue: previous.LocalDynamicValueText,
            previousTimestamp: previous.LocalDynamicUpdatedUtc,
            current: localDynamic)
            ? previous.LocalDynamicSequence + 1
            : previous.LocalDynamicSequence;
        var nextStaticSequence = HasObservedStateChanged(
            previousValue: previous.LocalStaticValueText,
            previousTimestamp: previous.LocalStaticUpdatedUtc,
            current: localStatic)
            ? previous.LocalStaticSequence + 1
            : previous.LocalStaticSequence;

        UpdateSnapshot(snapshot => snapshot with
        {
            IsConnected = _host?.IsRunning == true
                && _localPublisherSession is not null
                && _localObserver?.IsConnected == true
                && _peerObservers.Values.All(observer => observer.IsConnected),
            StatusText = BuildStatusText(),
            LocalDynamicValueText = localDynamic.ValueText,
            LocalDynamicUpdatedUtc = localDynamic.LastUpdatedUtc,
            LocalDynamicSequence = nextDynamicSequence,
            LocalStaticValueText = localStatic.ValueText,
            LocalStaticUpdatedUtc = localStatic.LastUpdatedUtc,
            LocalStaticSequence = nextStaticSequence,
            VisibleItemsText = BuildVisibleItemsText(),
        });
    }

    private string BuildStatusText()
    {
        if (_host?.IsRunning != true)
        {
            return "Stopped";
        }

        if (_localPublisherSession is null || _localObserver is null)
        {
            return "Starting local services";
        }

        if (!_peerObservers.Values.All(observer => observer.IsConnected))
        {
            return "Connected locally / peers reconnecting";
        }

        return _dynamicLoopTask is null ? "Connected" : "Connected / dynamic active";
    }

    private string BuildVisibleItemsText()
    {
        var builder = new System.Text.StringBuilder();

        foreach (var node in _allNodes)
        {
            builder.AppendLine(node.DisplayName);
            foreach (var entry in GetVisibleItemsForNode(node.NodeId))
            {
                builder.AppendLine($"  {entry.Path} = {entry.ValueText} @ {FormatTimestamp(entry.LastUpdatedUtc)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private async Task SynchronizeMirrorsAsync(CancellationToken cancellationToken = default)
    {
        if (_localMirrorSession is null || _peerObservers.Count == 0)
        {
            return;
        }

        await _mirrorRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var entry in _peerObservers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                await MirrorPeerItemsAsync(entry.Key, entry.Value, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogEvent($"Mirror refresh failed: {exception.Message}");
        }
        finally
        {
            _mirrorRefreshGate.Release();
        }
    }

    private async Task MirrorPeerItemsAsync(string peerNodeId, MqttRemoteItemClient observer, CancellationToken cancellationToken)
    {
        var session = _localMirrorSession;
        if (session is null)
        {
            return;
        }

        var mirrorCandidates = GetMirrorCandidates(peerNodeId, observer);
        if (mirrorCandidates.Count == 0)
        {
            return;
        }

        foreach (var item in mirrorCandidates)
        {
            var normalizedPath = NormalizePath(item.Path ?? throw new InvalidOperationException("Mirror candidate path is missing."));
            var signature = BuildMirrorSignature(item);
            var sourceVersion = GetLatestObservedUpdateUnixMilliseconds(item);

            var mirrorStateKey = CreateMirrorStateKey(peerNodeId, normalizedPath);
            if (_mirroredStatesBySourceAndPath.TryGetValue(mirrorStateKey, out var existingState))
            {
                if (string.Equals(existingState.Signature, signature, StringComparison.Ordinal))
                {
                    continue;
                }

                if (existingState.SourceVersionUnixMilliseconds.HasValue
                    && sourceVersion.HasValue
                    && sourceVersion.Value < existingState.SourceVersionUnixMilliseconds.Value)
                {
                    continue;
                }
            }

            await session.PublishSnapshotAsync(item.Clone(), retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            _mirroredStatesBySourceAndPath[mirrorStateKey] = new MirroredItemState(peerNodeId, signature, sourceVersion);
        }
    }

    private IReadOnlyList<ItemModel> GetMirrorCandidates(string peerNodeId, MqttRemoteItemClient observer)
    {
        var snapshots = observer.GetRemoteItemSnapshots();
        IEnumerable<(string Path, ItemModel Item)> candidates;

        if (snapshots.TryGetValue(SharedRemoteClientId, out var sharedRoot))
        {
            candidates = EnumerateMirrorItems(sharedRoot);
        }
        else
        {
            candidates = snapshots.Values.SelectMany(EnumerateMirrorItems);
        }

        return candidates
            .Where(entry => string.Equals(TryResolveOwnerNodeId(entry.Path), peerNodeId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => GetLatestObservedUpdateUnixMilliseconds(entry.Item) ?? long.MinValue)
                .First())
            .Select(entry => entry.Item.Clone().Repath(entry.Path))
            .ToArray();
    }

    private static IEnumerable<(string Path, ItemModel Item)> EnumerateMirrorItems(ItemModel root)
    {
        var stack = new Stack<(string Path, ItemModel Item)>();
        stack.Push((Path: string.Empty, Item: root));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var child in current.Item.GetDictionary().OrderByDescending(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var childPath = string.IsNullOrWhiteSpace(current.Path)
                    ? child.Key
                    : ItemPath.Combine(current.Path, child.Key);
                stack.Push((childPath, child.Value));
            }

            if (string.IsNullOrWhiteSpace(current.Path))
            {
                continue;
            }

            if (current.Item.Value is null)
            {
                continue;
            }

            yield return (NormalizePath(current.Path), current.Item);
        }
    }

    private IReadOnlyList<(string Path, string ValueText, DateTimeOffset? LastUpdatedUtc)> GetVisibleItemsForNode(string nodeId)
    {
        var observer = string.Equals(nodeId, _definition.NodeId, StringComparison.OrdinalIgnoreCase)
            ? _localObserver
            : _peerObservers.GetValueOrDefault(nodeId);
        if (observer is null)
        {
            return [];
        }

        var snapshots = observer.GetRemoteItemSnapshots();
        if (snapshots.TryGetValue(SharedRemoteClientId, out var sharedRoot))
        {
            return EnumerateVisibleItems(sharedRoot)
                .Where(entry => string.Equals(TryResolveOwnerNodeId(entry.Path), nodeId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return snapshots.Values
            .SelectMany(EnumerateVisibleItems)
            .Where(entry => string.Equals(TryResolveOwnerNodeId(entry.Path), nodeId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.LastUpdatedUtc).First())
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Path, string ValueText, DateTimeOffset? LastUpdatedUtc)> EnumerateVisibleItems(ItemModel root)
    {
        var stack = new Stack<ItemModel>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var child in current.GetDictionary().Values.OrderByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                stack.Push(child);
            }

            if (string.IsNullOrWhiteSpace(current.Path))
            {
                continue;
            }

            if (current.Value is null && !current.Properties.Has("read") && !current.Properties.Has("value"))
            {
                continue;
            }

            yield return (NormalizePath(current.Path), FormatValue(current.Value), GetReadTimestamp(current));
        }
    }

    private ObservedItemState CreateObservedItemState(MqttRemoteItemClient? observer, string path)
    {
        if (observer is null)
        {
            return new ObservedItemState(_definition.NodeId, path, false, "(missing)", null);
        }

        var snapshots = observer.GetRemoteItemSnapshots();
        if (snapshots.TryGetValue(SharedRemoteClientId, out var sharedRoot)
            && TryFindItem(sharedRoot, path, out var sharedItem))
        {
            return new ObservedItemState(_definition.NodeId, path, true, FormatValue(sharedItem.Value), GetReadTimestamp(sharedItem));
        }

        foreach (var root in snapshots.Values)
        {
            if (TryFindItem(root, path, out var item))
            {
                return new ObservedItemState(_definition.NodeId, path, true, FormatValue(item.Value), GetReadTimestamp(item));
            }
        }

        return new ObservedItemState(_definition.NodeId, path, false, "(missing)", null);
    }

    private void UpdateSnapshot(Func<MeshNodeSnapshot, MeshNodeSnapshot> updater)
    {
        MeshNodeSnapshot snapshot;
        lock (_sync)
        {
            _snapshot = updater(_snapshot);
            snapshot = _snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void RegisterOwnedItem(ItemModel item)
    {
        _ownedItemsByPath[NormalizePath(item.Path ?? throw new InvalidOperationException("Item path is missing."))] = item;
    }

    private ItemModel GetRequiredOwnedItem(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_ownedItemsByPath.TryGetValue(normalizedPath, out var item))
        {
            return item;
        }

        throw new InvalidOperationException($"{_definition.DisplayName} does not own item '{normalizedPath}'.");
    }

    private void LogEvent(string message)
    {
        EventLogged?.Invoke(
            this,
            new DemoNodeEvent(
                NodeId: _definition.NodeId,
                DisplayName: _definition.DisplayName,
                TimestampUtc: DateTimeOffset.UtcNow,
                Message: message));
    }

    private MqttItemClientSession GetRequiredLocalPublisherSession()
        => _localPublisherSession ?? throw new InvalidOperationException($"{_definition.DisplayName} publisher is not connected.");

    private string? TryResolveOwnerNodeId(string path)
    {
        var segments = NormalizePath(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "nodes", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _nodeDefinitionsById.ContainsKey(segments[1]) ? segments[1] : null;
    }

    private static bool HasObservedStateChanged(string previousValue, DateTimeOffset? previousTimestamp, ObservedItemState current)
        => !string.Equals(previousValue, current.ValueText, StringComparison.Ordinal)
           || previousTimestamp != current.LastUpdatedUtc;

    private static long? GetLatestObservedUpdateUnixMilliseconds(ItemModel item)
    {
        long? latest = null;

        foreach (var property in item.Properties.GetDictionary().Values)
        {
            var propertyUpdate = Convert.ToInt64(property.LastUpdate, CultureInfo.InvariantCulture);
            latest = latest.HasValue
                ? (propertyUpdate > latest.Value ? propertyUpdate : latest.Value)
                : propertyUpdate;
        }

        return latest;
    }

    private static string BuildMirrorSignature(ItemModel item)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(NormalizePath(item.Path ?? string.Empty));
        builder.Append('|');
        builder.Append(FormatValue(item.Value));

        foreach (var property in item.Properties.GetDictionary().OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|');
            builder.Append(property.Key);
            builder.Append('=');
            builder.Append(FormatValue(property.Value.Value));
        }

        return builder.ToString();
    }

    private static string CreateMirrorStateKey(string sourceNodeId, string path)
        => string.Join('|', sourceNodeId, NormalizePath(path));

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

    private static string NormalizePath(string path) => ItemPath.Normalize(path);

    private static string GetItemName(string path)
        => NormalizePath(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();

    private static string FormatValue(object? value)
        => value switch
        {
            null => "(null)",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";

    private sealed record MirroredItemState(
        string SourceNodeId,
        string Signature,
        long? SourceVersionUnixMilliseconds);
}
