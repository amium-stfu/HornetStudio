using Amium.Item.Server.MultimasterDemo.Models;

namespace Amium.Item.Server.MultimasterDemo.Controllers;

/// <summary>
/// Coordinates a reusable three-node multimaster mesh composed of local brokers, publishers, and observers.
/// </summary>
public sealed class MeshMultimasterDemoController : IAsyncDisposable
{
    private readonly IReadOnlyList<MeshNodeDefinition> _nodes;
    private readonly Dictionary<string, MeshDemoNodeController> _controllers;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshMultimasterDemoController"/> class.
    /// </summary>
    public MeshMultimasterDemoController()
    {
        _nodes =
        [
            new MeshNodeDefinition("server_a", "Server A", "127.0.0.1", 18831, "hornet_multimaster_mesh_server_a", "dyn_1", "stat_2"),
            new MeshNodeDefinition("server_b", "Server B", "127.0.0.1", 18832, "hornet_multimaster_mesh_server_b", "dyn_3", "stat_4"),
            new MeshNodeDefinition("server_c", "Server C", "127.0.0.1", 18833, "hornet_multimaster_mesh_server_c", "dyn_5", "stat_6"),
        ];

        _controllers = _nodes
            .Select(node => new MeshDemoNodeController(node, _nodes))
            .ToDictionary(controller => controller.NodeId, StringComparer.OrdinalIgnoreCase);

        foreach (var controller in _controllers.Values)
        {
            controller.SnapshotChanged += HandleNodeSnapshotChanged;
            controller.EventLogged += HandleNodeEventLogged;
        }
    }

    public event EventHandler<MeshNodeSnapshot>? NodeSnapshotChanged;

    public event EventHandler<DemoNodeEvent>? NodeEventLogged;

    public IReadOnlyList<MeshNodeDefinition> Nodes => _nodes;

    public IReadOnlyList<string> NodeIds => _nodes.Select(node => node.NodeId).ToArray();

    public IReadOnlyList<MeshNodeSnapshot> CurrentSnapshots => _controllers.Values.Select(controller => controller.Snapshot).ToArray();

    public IReadOnlyDictionary<string, string> InitialValuesByPath => _nodes
        .SelectMany(node => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [node.DynamicItemPath] = node.DynamicItemName,
            [node.StaticItemPath] = node.StaticItemName,
        })
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    public bool IsRunning => _controllers.Values.All(controller => controller.IsRunning);

    public string EndpointSummary => string.Join(
        " | ",
        _nodes.Select(node => $"{node.NodeId}={node.EndpointSummary}"));

    /// <summary>
    /// Starts all mesh brokers, publishers, observers, and optional dynamic updates.
    /// </summary>
    /// <param name="startDynamicUpdates">Whether dynamic item updates should start immediately.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(bool startDynamicUpdates = true, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            foreach (var controller in _controllers.Values)
            {
                await controller.StartLocalServerAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var controller in _controllers.Values)
            {
                await controller.StartLocalPublisherAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var controller in _controllers.Values)
            {
                await controller.ConnectPeerObserversAsync(cancellationToken).ConfigureAwait(false);
            }

            if (startDynamicUpdates)
            {
                await StartDynamicUpdatesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Starts periodic dynamic updates on all mesh nodes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartDynamicUpdatesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var controller in _controllers.Values)
        {
            await controller.StartDynamicUpdatesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the running mesh and disconnects all node resources.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        foreach (var controller in _controllers.Values)
        {
            await controller.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes a runtime item on the selected node.
    /// </summary>
    /// <param name="nodeId">The target node id.</param>
    /// <param name="path">The runtime item path.</param>
    /// <param name="value">The item value.</param>
    /// <param name="writable">Whether the runtime item should be writable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PublishRuntimeItemAsync(string nodeId, string path, string value, bool writable, CancellationToken cancellationToken = default)
        => GetRequiredController(nodeId).PublishRuntimeItemAsync(path, value, writable, cancellationToken);

    /// <summary>
    /// Updates an item that is owned by the selected node.
    /// </summary>
    /// <param name="nodeId">The owning node id.</param>
    /// <param name="path">The item path.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task UpdateOwnedItemAsync(string nodeId, string path, string value, CancellationToken cancellationToken = default)
        => GetRequiredController(nodeId).UpdateOwnedItemAsync(path, value, cancellationToken);

    /// <summary>
    /// Writes a value through one node to the owner of the target path.
    /// </summary>
    /// <param name="actorNodeId">The node performing the write.</param>
    /// <param name="path">The target item path.</param>
    /// <param name="value">The requested value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task WriteRemoteValueAsync(string actorNodeId, string path, string value, CancellationToken cancellationToken = default)
    {
        var ownerNodeId = ResolveOwnerNodeId(path);
        if (string.Equals(actorNodeId, ownerNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return GetRequiredController(actorNodeId).UpdateOwnedItemAsync(path, value, cancellationToken);
        }

        return GetRequiredController(actorNodeId).WriteRemoteValueAsync(ownerNodeId, path, value, cancellationToken);
    }

    /// <summary>
    /// Returns the observer-visible states for the specified path on all nodes.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <returns>The states keyed by observer node id.</returns>
    public IReadOnlyDictionary<string, ObservedItemState> GetObservedStates(string path)
        => _controllers.Values.ToDictionary(
            keySelector: controller => controller.NodeId,
            elementSelector: controller => controller.GetObservedItemState(path),
            comparer: StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the broker-visible states for the specified path on all nodes.
    /// </summary>
    /// <param name="path">The item path.</param>
    /// <returns>The states keyed by observing node id.</returns>
    public IReadOnlyDictionary<string, ObservedItemState> GetBrokerVisibleStates(string path)
        => _controllers.Values.ToDictionary(
            keySelector: controller => controller.NodeId,
            elementSelector: controller => controller.GetBrokerVisibleItemState(path),
            comparer: StringComparer.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        foreach (var controller in _controllers.Values)
        {
            controller.SnapshotChanged -= HandleNodeSnapshotChanged;
            controller.EventLogged -= HandleNodeEventLogged;
            await controller.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string ResolveOwnerNodeId(string path)
    {
        var segments = Amium.Items.ItemPath.Normalize(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "nodes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{path}' does not belong to a mesh node.");
        }

        if (_controllers.ContainsKey(segments[1]))
        {
            return segments[1];
        }

        throw new InvalidOperationException($"Unknown mesh node owner '{segments[1]}' for path '{path}'.");
    }

    private void HandleNodeSnapshotChanged(object? sender, MeshNodeSnapshot snapshot)
        => NodeSnapshotChanged?.Invoke(sender, snapshot);

    private void HandleNodeEventLogged(object? sender, DemoNodeEvent nodeEvent)
        => NodeEventLogged?.Invoke(sender, nodeEvent);

    private MeshDemoNodeController GetRequiredController(string nodeId)
    {
        if (_controllers.TryGetValue(nodeId, out var controller))
        {
            return controller;
        }

        throw new InvalidOperationException($"Unknown mesh node '{nodeId}'.");
    }
}