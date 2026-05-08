using Amium.Item.Server.Mqtt;
using Amium.Item.Server.MultimasterDemo.Models;

namespace Amium.Item.Server.MultimasterDemo.Controllers;

internal sealed class MultimasterDemoController : IAsyncDisposable
{
    private const string BrokerHost = "127.0.0.1";
    private const int BrokerPort = 18830;
    private const string BrokerBaseTopic = "hornet_multimaster_demo";

    private readonly IReadOnlyList<(string NodeId, string DisplayName)> _nodes =
        new[]
        {
            (NodeId: "server_a", DisplayName: "Server A"),
            (NodeId: "server_b", DisplayName: "Server B"),
            (NodeId: "server_c", DisplayName: "Server C"),
        };

    private readonly Dictionary<string, string> _publisherClientIdsByNodeId;
    private readonly Dictionary<string, DemoNodeController> _controllers;
    private MqttItemServerHost? _host;

    internal MultimasterDemoController()
    {
        _publisherClientIdsByNodeId = _nodes.ToDictionary(
            keySelector: node => node.NodeId,
            elementSelector: node => $"{node.NodeId}-publisher",
            comparer: StringComparer.OrdinalIgnoreCase);

        _controllers = _nodes
            .Select((node, index) => new DemoNodeController(
                nodeId: node.NodeId,
                displayName: node.DisplayName,
                nodeIndex: index,
                host: BrokerHost,
                port: BrokerPort,
                baseTopic: BrokerBaseTopic,
                allNodes: _nodes,
                publisherClientIdsByNodeId: _publisherClientIdsByNodeId))
            .ToDictionary(controller => controller.NodeId, StringComparer.OrdinalIgnoreCase);

        foreach (var controller in _controllers.Values)
        {
            controller.SnapshotChanged += HandleNodeSnapshotChanged;
            controller.EventLogged += HandleNodeEventLogged;
        }
    }

    internal event EventHandler<DemoNodeSnapshot>? NodeSnapshotChanged;

    internal event EventHandler<DemoNodeEvent>? NodeEventLogged;

    internal bool IsRunning => _host?.IsRunning == true;

    internal string EndpointSummary => $"{BrokerHost}:{BrokerPort} / {BrokerBaseTopic}";

    internal IReadOnlyList<(string NodeId, string DisplayName)> Nodes => _nodes;

    internal IReadOnlyList<string> NodeIds => _nodes.Select(node => node.NodeId).ToArray();

    internal IReadOnlyList<DemoNodeSnapshot> CurrentSnapshots => _controllers.Values.Select(controller => controller.Snapshot).ToArray();

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        _host = new MqttItemServerHost(
            new MqttItemServerOptions
            {
                Enabled = true,
                Host = BrokerHost,
                Port = BrokerPort,
                BaseTopic = BrokerBaseTopic,
                SubscriptionRootPath = "nodes",
                PublishHealth = false,
            });

        try
        {
            await _host.StartAsync(cancellationToken).ConfigureAwait(false);

            foreach (var node in _nodes)
            {
                await _controllers[node.NodeId].StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task StopAsync()
    {
        foreach (var node in _nodes)
        {
            await _controllers[node.NodeId].StopAsync().ConfigureAwait(false);
        }

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
    }

    internal Task WriteTestAsync(string nodeId, string value, CancellationToken cancellationToken = default)
    {
        if (!_controllers.TryGetValue(nodeId, out var controller))
        {
            throw new InvalidOperationException($"Unknown node '{nodeId}'.");
        }

        return controller.WriteTestAsync(value, cancellationToken);
    }

    internal Task PublishRuntimeItemAsync(
        string nodeId,
        string path,
        string value,
        bool writable,
        CancellationToken cancellationToken = default)
        => GetRequiredController(nodeId).PublishRuntimeItemAsync(path, value, writable, cancellationToken);

    internal Task UpdateOwnedItemAsync(
        string nodeId,
        string path,
        string value,
        CancellationToken cancellationToken = default)
        => GetRequiredController(nodeId).UpdateOwnedItemAsync(path, value, cancellationToken);

    internal Task WriteRemoteValueAsync(
        string actorNodeId,
        string path,
        string value,
        CancellationToken cancellationToken = default)
        => GetRequiredController(actorNodeId).WriteRemoteValueAsync(path, value, cancellationToken);

    internal IReadOnlyDictionary<string, ObservedItemState> GetObservedStates(string path)
        => _controllers.Values.ToDictionary(
            keySelector: controller => controller.NodeId,
            elementSelector: controller => controller.GetObservedItemState(path),
            comparer: StringComparer.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        foreach (var controller in _controllers.Values)
        {
            controller.SnapshotChanged -= HandleNodeSnapshotChanged;
            controller.EventLogged -= HandleNodeEventLogged;
            await controller.DisposeAsync().ConfigureAwait(false);
        }

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
    }

    private void HandleNodeSnapshotChanged(object? sender, DemoNodeSnapshot snapshot)
        => NodeSnapshotChanged?.Invoke(sender, snapshot);

    private void HandleNodeEventLogged(object? sender, DemoNodeEvent nodeEvent)
        => NodeEventLogged?.Invoke(sender, nodeEvent);

    private DemoNodeController GetRequiredController(string nodeId)
    {
        if (_controllers.TryGetValue(nodeId, out var controller))
        {
            return controller;
        }

        throw new InvalidOperationException($"Unknown node '{nodeId}'.");
    }
}