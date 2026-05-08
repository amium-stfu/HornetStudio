using System.Collections.Concurrent;
using Amium.Item.Server;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Item.Server.Monitor.Hosting;

internal sealed class MonitorRegistryBridge : IItemServerClient, IAsyncDisposable
{
    private readonly IDataRegistry _registry;
    private readonly ConcurrentDictionary<string, byte> _mirroredRootPaths = new(StringComparer.OrdinalIgnoreCase);
    private IItemSubscription? _rootSubscription;
    private IItemSubscription? _systemSubscription;

    public MonitorRegistryBridge(IDataRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string ClientId => "item-server-monitor-registry-bridge";

    public async Task StartAsync(IItemServer broker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);

        _rootSubscription = await broker.SubscribeAsync(
            client: this,
            path: ItemServerPath.GlobalSubscriptionPath,
            options: new ItemSubscriptionOptions
            {
                Recursive = true,
                IncludeRetained = true,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _systemSubscription = await broker.SubscribeAsync(
            client: this,
            path: ItemServerHealthPaths.Root,
            options: new ItemSubscriptionOptions
            {
                Recursive = true,
                IncludeRetained = true,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case ItemSnapshotMessage snapshot:
                UpsertSnapshot(snapshot.Path, snapshot.ItemModel.Clone().Repath(snapshot.Path));
                break;
            case ItemValueChangedMessage valueChanged:
                UpdateValue(valueChanged.Path, valueChanged.Value, (ulong)Math.Max(0, valueChanged.Timestamp.ToUnixTimeMilliseconds()));
                break;
            case ItemPropertyChangedMessage propertyChanged:
                UpdateProperty(propertyChanged.Path, propertyChanged.PropertyName, propertyChanged.Value, (ulong)Math.Max(0, propertyChanged.Timestamp.ToUnixTimeMilliseconds()));
                break;
            case ItemRemoveMessage remove:
                RemovePath(remove.Path);
                break;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_rootSubscription is not null)
        {
            await _rootSubscription.DisposeAsync().ConfigureAwait(false);
            _rootSubscription = null;
        }

        if (_systemSubscription is not null)
        {
            await _systemSubscription.DisposeAsync().ConfigureAwait(false);
            _systemSubscription = null;
        }

        foreach (var path in _mirroredRootPaths.Keys.ToArray())
        {
            _registry.Remove(path);
        }

        _mirroredRootPaths.Clear();
    }

    private void UpsertSnapshot(string path, ItemModel item)
    {
        _registry.UpsertSnapshot(path, item, ResolveMetadata(path), pruneMissingMembers: true);
        _mirroredRootPaths[path] = 0;
    }

    private void UpdateValue(string path, object? value, ulong timestamp)
    {
        if (_registry.UpdateValue(path, value, timestamp))
        {
            return;
        }

        var item = new ItemModel(GetItemName(path), value).Repath(path);
        UpsertSnapshot(path, item);
    }

    private void UpdateProperty(string path, string propertyName, object? value, ulong timestamp)
    {
        if (_registry.UpdateProperty(path, propertyName, value, timestamp))
        {
            return;
        }

        var item = new ItemModel(GetItemName(path)).Repath(path);
        var normalizedPropertyName = ItemPath.ToSnakeCaseSegment(propertyName);
        item.Properties[normalizedPropertyName].Value = value!;
        item.Properties[normalizedPropertyName].LastUpdate = timestamp;
        UpsertSnapshot(path, item);
    }

    private void RemovePath(string path)
    {
        foreach (var mirroredPath in _mirroredRootPaths.Keys.ToArray())
        {
            if (!ItemServerPath.IsSelfOrDescendant(path, mirroredPath))
            {
                continue;
            }

            _registry.Remove(mirroredPath);
            _mirroredRootPaths.TryRemove(mirroredPath, out _);
        }
    }

    private static DataRegistryItemMetadata ResolveMetadata(string path)
        => string.Equals(path, ItemServerHealthPaths.Root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(ItemServerHealthPaths.Root + ".", StringComparison.OrdinalIgnoreCase)
            ? DataRegistryItemMetadata.WidgetStatus()
            : DataRegistryItemMetadata.BrokerReceivedData();

    private static string GetItemName(string path)
    {
        var segments = path.Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 ? segments[^1] : path;
    }

}