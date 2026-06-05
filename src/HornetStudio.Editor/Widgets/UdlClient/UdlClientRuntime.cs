using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.UdlClients;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.Widgets;

internal sealed class UdlClientRuntime : IDisposable
{
    private static readonly double StopwatchTickToMilliseconds = 1000d / Stopwatch.Frequency;
    private readonly Func<IHostUdlClient> _clientFactory;
    private readonly List<ItemModel> _subscribedRuntimeItems = [];
    private readonly object _sync = new();
    private IHostUdlClient? _client;
    private string _lastRootPathSignature = string.Empty;
    private string _lastDiagnostic = string.Empty;
    private long _messageCounter;
    private long _rxCounter;
    private long _txCounter;

    public UdlClientRuntime(
        string folderName,
        string clientName,
        string host,
        int port,
        bool demoEnabled,
        IEnumerable<UdlDemoModuleDefinition>? demoDefinitions,
        Func<IHostUdlClient>? clientFactory = null)
    {
        FolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        ClientName = UdlPathHelper.NormalizeClientName(clientName);
        Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        Port = port <= 0 ? 9001 : port;
        DemoEnabled = demoEnabled;
        DemoDefinitions = (demoDefinitions ?? [])
            .Where(static definition => definition is not null)
            .Select(static definition => definition.Clone())
            .ToArray();
        _clientFactory = clientFactory ?? CreateClient;
    }

    public string FolderName { get; }

    public string ClientName { get; }

    public string Host { get; }

    public int Port { get; }

    public bool DemoEnabled { get; }

    public IReadOnlyList<UdlDemoModuleDefinition> DemoDefinitions { get; }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _client is not null;
            }
        }
    }

    public int LocalPort
    {
        get
        {
            lock (_sync)
            {
                return _client?.LocalPort ?? 0;
            }
        }
    }

    public string ConnectedClientName
    {
        get
        {
            lock (_sync)
            {
                return _client?.Name ?? ClientName;
            }
        }
    }

    public event Action<uint, byte, byte[]>? FrameReceived;

    public event Action<string>? Diagnostic;

    public event Action? RuntimeStructureChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_client is not null)
            {
                return;
            }
        }

        var client = _clientFactory();
        client.FrameReceived += OnClientFrameReceived;
        client.Diagnostic += OnClientDiagnostic;

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                if (_client is not null)
                {
                    client.FrameReceived -= OnClientFrameReceived;
                    client.Diagnostic -= OnClientDiagnostic;
                    client.Dispose();
                    return;
                }

                _client = client;
                _lastRootPathSignature = string.Empty;
                _lastDiagnostic = string.Empty;
                Interlocked.Exchange(ref _messageCounter, 0);
                Interlocked.Exchange(ref _rxCounter, 0);
                Interlocked.Exchange(ref _txCounter, 0);
                ResetLiveValueSubscriptions(client);
            }

            NotifyRuntimeStructureChangedIfNeeded();
        }
        catch
        {
            client.FrameReceived -= OnClientFrameReceived;
            client.Diagnostic -= OnClientDiagnostic;
            client.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IHostUdlClient? client;
        lock (_sync)
        {
            client = _client;
            _client = null;
            _lastRootPathSignature = string.Empty;
            _lastDiagnostic = string.Empty;
            Interlocked.Exchange(ref _messageCounter, 0);
            Interlocked.Exchange(ref _rxCounter, 0);
            Interlocked.Exchange(ref _txCounter, 0);
            ResetLiveValueSubscriptions(client: null);
        }

        if (client is null)
        {
            return;
        }

        client.FrameReceived -= OnClientFrameReceived;
        client.Diagnostic -= OnClientDiagnostic;

        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
        }
    }

    public IReadOnlyList<ItemModel> GetRuntimeItemsSnapshot()
    {
        var client = GetClient();
        if (client is null)
        {
            return [];
        }

        var items = new List<ItemModel>();
        foreach (var root in client.Items.GetDictionary().Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AppendItem(root, items);
        }

        return items;
    }

    public int GetRootItemCount()
    {
        var client = GetClient();
        return client?.Items.GetDictionary().Count ?? 0;
    }

    public IReadOnlyList<string> GetReceivedRootPaths()
    {
        var client = GetClient();
        if (client is null)
        {
            return [];
        }

        return client.Items.GetDictionary().Values
            .Select(static item => UdlPathHelper.GetRelativeRuntimePath(item.Path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryResolveRuntimeItem(string relativePath, out ItemModel? resolved)
    {
        var normalizedRelativePath = TargetPathHelper.NormalizeConfiguredTargetPath(relativePath);
        var client = GetClient();
        if (client is null)
        {
            resolved = null;
            return false;
        }

        resolved = TryResolveRuntimeItemCore(client.Items.GetDictionary(), normalizedRelativePath);
        return resolved is not null;
    }

    public IReadOnlyList<UdlAttachmentProjectionInput> GetAttachmentProjectionInput(string? serializedAttachedPaths)
    {
        var client = GetClient();
        if (client is null)
        {
            return [];
        }

        var attachedPaths = ParseAttachedPaths(serializedAttachedPaths);
        if (attachedPaths.Count == 0)
        {
            return [];
        }

        var attachments = new List<UdlAttachmentProjectionInput>();
        foreach (var relativePath in attachedPaths)
        {
            if (!TryResolveRuntimeItem(relativePath, out var runtimeItem) || runtimeItem?.Path is null)
            {
                continue;
            }

            attachments.Add(new UdlAttachmentProjectionInput(
                relativePath,
                TargetPathHelper.NormalizeConfiguredTargetPath(relativePath),
                runtimeItem));
        }

        return attachments;
    }

    public UdlClientRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        lock (_sync)
        {
            return new UdlClientRuntimeDiagnosticsSnapshot(
                IsConnected: _client is not null,
                Host: Host,
                Port: Port,
                LocalPort: _client?.LocalPort ?? 0,
                RootItemCount: _client?.Items.GetDictionary().Count ?? 0,
                MessageCount: Interlocked.Read(ref _messageCounter),
                RxCount: Interlocked.Read(ref _rxCounter),
                TxCount: Interlocked.Read(ref _txCounter),
                LastDiagnostic: _lastDiagnostic);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    private IHostUdlClient? GetClient()
    {
        lock (_sync)
        {
            return _client;
        }
    }

    private void ResetLiveValueSubscriptions(IHostUdlClient? client)
    {
        foreach (var item in _subscribedRuntimeItems)
        {
            item.Changed -= OnRuntimeItemChanged;
        }

        _subscribedRuntimeItems.Clear();
        UdlClientLiveValueStore.ClearScope(FolderName, ClientName);

        if (client is null)
        {
            return;
        }

        foreach (var root in client.Items.GetDictionary().Values)
        {
            SubscribeRuntimeTree(root);
            PublishRuntimeTree(root);
        }
    }

    private IHostUdlClient CreateClient()
    {
        if (DemoEnabled)
        {
            return new SimulatedHostUdlClient(ClientName, Host, Port, DemoDefinitions);
        }

        return new HostUdlClient(ClientName, Host, Port);
    }

    private void OnClientFrameReceived(uint id, byte dlc, byte[] data)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _messageCounter);
        Interlocked.Increment(ref _rxCounter);
        FrameReceived?.Invoke(id, dlc, data);
        NotifyRuntimeStructureChangedIfNeeded();

        if (!UiResponsivenessDiagnostics.IsEnabled)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * StopwatchTickToMilliseconds;
        UiResponsivenessDiagnostics.RecordSignalPipelineDelay(
            stage: "UdlRuntimeFrameReceived",
            delay: TimeSpan.FromMilliseconds(elapsedMilliseconds));
    }

    private void OnClientDiagnostic(string message)
    {
        lock (_sync)
        {
            _lastDiagnostic = message?.Trim() ?? string.Empty;
        }

        Diagnostic?.Invoke(message ?? string.Empty);
    }

    private void NotifyRuntimeStructureChangedIfNeeded()
    {
        var client = GetClient();
        var signature = client is null
            ? string.Empty
            : string.Join(
                "|",
                client.Items.GetDictionary().Values
                    .Select(static item => UdlPathHelper.GetRelativeRuntimePath(item.Path))
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        var changed = false;

        lock (_sync)
        {
            if (!string.Equals(_lastRootPathSignature, signature, StringComparison.Ordinal))
            {
                _lastRootPathSignature = signature;
                ResetLiveValueSubscriptions(client);
                changed = true;
            }
        }

        if (changed)
        {
            RuntimeStructureChanged?.Invoke();
        }
    }

    private static HashSet<string> ParseAttachedPaths(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return serialized
            .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ItemModel? TryResolveRuntimeItemCore(IReadOnlyDictionary<string, ItemModel> roots, string normalizedRelativePath)
    {
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return null;
        }

        ItemModel? current = null;
        var segments = TargetPathHelper.SplitPathSegments(normalizedRelativePath);
        foreach (var segment in segments)
        {
            if (current is null)
            {
                var rootMatch = roots.FirstOrDefault(entry => string.Equals(entry.Key, segment, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(rootMatch.Key))
                {
                    return null;
                }

                current = rootMatch.Value;
                continue;
            }

            var next = current.GetDictionary()
                .FirstOrDefault(entry => string.Equals(entry.Key, segment, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(next.Key))
            {
                return null;
            }

            current = next.Value;
        }

        return current;
    }

    private void SubscribeRuntimeTree(ItemModel item)
    {
        _subscribedRuntimeItems.Add(item);
        item.Changed += OnRuntimeItemChanged;

        foreach (var child in item.GetDictionary().Values)
        {
            SubscribeRuntimeTree(child);
        }
    }

    private void PublishRuntimeTree(ItemModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        foreach (var propertyEntry in item.Properties.GetDictionary())
        {
            PublishRuntimeProperty(item.Path!, propertyEntry.Key, propertyEntry.Value.Value);
        }

        foreach (var child in item.GetDictionary().Values)
        {
            PublishRuntimeTree(child);
        }
    }

    private void OnRuntimeItemChanged(object? sender, ItemChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Item.Path))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            PublishRuntimeProperty(e.Item.Path!, "value", e.Item.Value);
            return;
        }

        if (!e.Item.Properties.Has(e.PropertyName))
        {
            return;
        }

        PublishRuntimeProperty(e.Item.Path!, e.PropertyName, e.Item.Properties[e.PropertyName].Value);
    }

    private void PublishRuntimeProperty(string itemPath, string parameterName, object? value)
    {
        UdlClientLiveValueStore.UpdateProperty(
            folderName: FolderName,
            clientName: ClientName,
            itemPath: itemPath,
            parameterName: parameterName,
            value: value);

        if (!UiResponsivenessDiagnostics.IsEnabled)
        {
            return;
        }

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: "SignalLiveValueStoreUpdate",
            path: itemPath,
            module: GetModuleName(itemPath));
    }

    private static string GetModuleName(string? itemPath)
    {
        var relativePath = UdlPathHelper.GetRelativeRuntimePath(itemPath);
        var segments = TargetPathHelper.SplitPathSegments(relativePath);
        return segments.Count > 0 ? segments[0] : string.Empty;
    }

    private static void AppendItem(ItemModel item, ICollection<ItemModel> items)
    {
        items.Add(item);
        foreach (var child in item.GetDictionary().Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            AppendItem(child, items);
        }
    }
}

internal sealed record UdlClientRuntimeDiagnosticsSnapshot(
    bool IsConnected,
    string Host,
    int Port,
    int LocalPort,
    int RootItemCount,
    long MessageCount,
    long RxCount,
    long TxCount,
    string LastDiagnostic);
