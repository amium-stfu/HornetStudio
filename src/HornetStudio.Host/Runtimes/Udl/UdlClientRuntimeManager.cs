using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Host.Runtimes.Udl.DemoMode;
using HornetStudio.Logging;

namespace HornetStudio.Host.Runtimes.Udl;

internal static class UdlClientRuntimeManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Dictionary<string, UdlClientDefinition>> DefinitionsByFolder = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, RuntimeSlot>> RuntimesByFolder = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, Task<UdlClientRuntime>>> PendingConnectsByFolder = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<UdlClientRuntime> SyncDefinitions(string folderName, IEnumerable<UdlClientDefinition> definitions, bool forceRecreate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definitions);

        var normalizedFolderName = folderName.Trim();
        var definitionList = definitions
            .Where(static definition => definition is not null && !string.IsNullOrWhiteSpace(definition.ClientId))
            .Select(static definition => definition with
            {
                AttachedItemPaths = definition.AttachedItemPaths.ToArray()
            })
            .OrderBy(static definition => definition.ClientId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<RuntimeSlot> slotsToDispose = [];
        List<(RuntimeSlot Slot, UdlClientDefinition Definition, string DefinitionKey)> slotsToRefresh = [];
        List<UdlClientDefinition> autoConnectDefinitions = [];
        List<UdlClientRuntime> activeRuntimes;

        lock (Sync)
        {
            var definitionsByClient = definitionList.ToDictionary(definition => definition.ClientId, StringComparer.OrdinalIgnoreCase);
            DefinitionsByFolder[normalizedFolderName] = definitionsByClient;

            if (!RuntimesByFolder.TryGetValue(normalizedFolderName, out var runtimesByClient))
            {
                runtimesByClient = new Dictionary<string, RuntimeSlot>(StringComparer.OrdinalIgnoreCase);
                RuntimesByFolder[normalizedFolderName] = runtimesByClient;
            }

            foreach (var existingClientId in runtimesByClient.Keys.ToArray())
            {
                if (!definitionsByClient.TryGetValue(existingClientId, out var definition) || !definition.Enabled)
                {
                    slotsToDispose.Add(runtimesByClient[existingClientId]);
                    runtimesByClient.Remove(existingClientId);
                    continue;
                }

                var definitionKey = CreateDefinitionKey(definition);
                if (forceRecreate || !string.Equals(runtimesByClient[existingClientId].DefinitionKey, definitionKey, StringComparison.Ordinal))
                {
                    slotsToDispose.Add(runtimesByClient[existingClientId]);
                    runtimesByClient.Remove(existingClientId);
                    continue;
                }

                slotsToRefresh.Add((runtimesByClient[existingClientId], definition, definitionKey));
            }

            foreach (var definition in definitionList)
            {
                if (!definition.Enabled)
                {
                    continue;
                }

                if (definition.AutoConnect
                    && !runtimesByClient.ContainsKey(definition.ClientId)
                    && !HasPendingConnect(normalizedFolderName, definition.ClientId))
                {
                    autoConnectDefinitions.Add(definition);
                }
            }

            activeRuntimes = runtimesByClient.Values
                .Select(static slot => slot.Runtime)
                .OrderBy(static runtime => runtime.ClientName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var slot in slotsToDispose)
        {
            slot.Dispose();
        }

        foreach (var refresh in slotsToRefresh)
        {
            refresh.Slot.UpdateDefinition(refresh.Definition, refresh.DefinitionKey);
        }

        foreach (var definition in autoConnectDefinitions)
        {
            _ = StartAutoConnectAsync(normalizedFolderName, definition);
        }

        if (definitionList.Length == 0)
        {
            ReleaseFolder(normalizedFolderName);
            return Array.Empty<UdlClientRuntime>();
        }

        return activeRuntimes;
    }

    internal static bool TryGetRuntime(string folderName, string clientId, out UdlClientRuntime? runtime)
    {
        runtime = null;
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        lock (Sync)
        {
            if (RuntimesByFolder.TryGetValue(folderName.Trim(), out var runtimesByClient)
                && runtimesByClient.TryGetValue(clientId.Trim(), out var slot))
            {
                runtime = slot.Runtime;
                return true;
            }
        }

        return false;
    }

    internal static Task<UdlClientRuntime> ConnectAsync(string folderName, UdlClientDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);

        var normalizedFolderName = folderName.Trim();
        var normalizedDefinition = definition with
        {
            AttachedItemPaths = definition.AttachedItemPaths.ToArray()
        };
        var definitionKey = CreateDefinitionKey(normalizedDefinition);
        RuntimeSlot? slotToDispose = null;
        Task<UdlClientRuntime>? pendingConnect = null;

        lock (Sync)
        {
            if (!DefinitionsByFolder.TryGetValue(normalizedFolderName, out var definitionsByClient))
            {
                definitionsByClient = new Dictionary<string, UdlClientDefinition>(StringComparer.OrdinalIgnoreCase);
                DefinitionsByFolder[normalizedFolderName] = definitionsByClient;
            }

            if (!definitionsByClient.TryGetValue(normalizedDefinition.ClientId, out var storedDefinition)
                || !string.Equals(CreateDefinitionKey(storedDefinition), definitionKey, StringComparison.Ordinal))
            {
                definitionsByClient[normalizedDefinition.ClientId] = normalizedDefinition;
            }

            if (!RuntimesByFolder.TryGetValue(normalizedFolderName, out var runtimesByClient))
            {
                runtimesByClient = new Dictionary<string, RuntimeSlot>(StringComparer.OrdinalIgnoreCase);
                RuntimesByFolder[normalizedFolderName] = runtimesByClient;
            }

            if (runtimesByClient.TryGetValue(normalizedDefinition.ClientId, out var existingSlot))
            {
                if (string.Equals(existingSlot.DefinitionKey, definitionKey, StringComparison.Ordinal))
                {
                    return Task.FromResult(existingSlot.Runtime);
                }

                slotToDispose = existingSlot;
                runtimesByClient.Remove(normalizedDefinition.ClientId);
            }

            if (TryGetPendingConnect(normalizedFolderName, normalizedDefinition.ClientId, out pendingConnect))
            {
                return pendingConnect ?? throw new InvalidOperationException($"UDL runtime connect task for '{normalizedDefinition.ClientId}' was not available.");
            }

            pendingConnect = ConnectPendingAsync(
                folderName: normalizedFolderName,
                clientId: normalizedDefinition.ClientId,
                slotToDispose: slotToDispose,
                cancellationToken: cancellationToken);
            RegisterPendingConnect(normalizedFolderName, normalizedDefinition.ClientId, pendingConnect);
        }

        return pendingConnect ?? throw new InvalidOperationException($"UDL runtime connect task for '{normalizedDefinition.ClientId}' was not created.");
    }

    internal static async Task DisconnectAsync(string folderName, string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        RuntimeSlot? slotToDispose = null;
        var normalizedFolderName = folderName.Trim();
        var normalizedClientId = clientId.Trim();

        lock (Sync)
        {
            if (RuntimesByFolder.TryGetValue(normalizedFolderName, out var runtimesByClient)
                && runtimesByClient.TryGetValue(normalizedClientId, out var slot))
            {
                slotToDispose = slot;
                runtimesByClient.Remove(normalizedClientId);
                if (runtimesByClient.Count == 0)
                {
                    RuntimesByFolder.Remove(normalizedFolderName);
                }
            }
        }

        if (slotToDispose is not null)
        {
            slotToDispose.DisposeProjection();
            await slotToDispose.Runtime.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            slotToDispose.Runtime.Dispose();
        }
    }

    internal static void ReleaseFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        Dictionary<string, RuntimeSlot>? runtimesByClient = null;
        var normalizedFolderName = folderName.Trim();

        lock (Sync)
        {
            DefinitionsByFolder.Remove(normalizedFolderName);
            PendingConnectsByFolder.Remove(normalizedFolderName);
            if (RuntimesByFolder.TryGetValue(normalizedFolderName, out var existing))
            {
                runtimesByClient = existing;
                RuntimesByFolder.Remove(normalizedFolderName);
            }
        }

        if (runtimesByClient is null)
        {
            return;
        }

        foreach (var runtime in runtimesByClient.Values)
        {
            runtime.Dispose();
        }
    }

    private static async Task StartAutoConnectAsync(string folderName, UdlClientDefinition definition)
    {
        try
        {
            await ConnectAsync(folderName, definition, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HostLogger.Log.Error(ex, "[UdlClientRuntimeManager] AutoConnect failed Folder={FolderName} ClientId={ClientId}.", folderName, definition.ClientId);
        }
    }

    private static UdlClientRuntime CreateRuntime(string folderName, UdlClientDefinition definition)
    {
        return new UdlClientRuntime(
            folderName: folderName,
            clientName: definition.ClientId,
            host: definition.Host,
            port: definition.Port,
            demoEnabled: definition.DemoEnabled,
            demoDefinitions: UdlDemoModuleDefinitionCodec.ParseDefinitions(definition.DemoModuleDefinitions));
    }

    private static async Task<UdlClientRuntime> ConnectPendingAsync(string folderName, string clientId, RuntimeSlot? slotToDispose, CancellationToken cancellationToken)
    {
        slotToDispose?.Dispose();

        try
        {
            if (!TryGetCurrentDefinition(folderName, clientId, out var definition))
            {
                throw new InvalidOperationException($"UDL runtime definition '{clientId}' is no longer available.");
            }

            var runtime = CreateRuntime(folderName, definition);
            UdlClientRegistryProjection? projection = null;

            try
            {
                await runtime.ConnectAsync(cancellationToken).ConfigureAwait(false);

                if (!TryGetCurrentDefinition(folderName, clientId, out definition))
                {
                    runtime.Dispose();
                    throw new InvalidOperationException($"UDL runtime definition '{clientId}' is no longer available.");
                }

                var definitionKey = CreateDefinitionKey(definition);
                projection = new UdlClientRegistryProjection(folderName, definition, runtime);

                lock (Sync)
                {
                    if (!RuntimesByFolder.TryGetValue(folderName, out var runtimesByClient))
                    {
                        runtimesByClient = new Dictionary<string, RuntimeSlot>(StringComparer.OrdinalIgnoreCase);
                        RuntimesByFolder[folderName] = runtimesByClient;
                    }

                    if (runtimesByClient.TryGetValue(clientId, out var existingSlot))
                    {
                        projection.Dispose();
                        runtime.Dispose();
                        return existingSlot.Runtime;
                    }

                    runtimesByClient[clientId] = new RuntimeSlot(runtime, definitionKey, projection);
                    return runtime;
                }
            }
            catch
            {
                projection?.Dispose();
                runtime.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            HostLogger.Log.Error(ex, "[UdlClientRuntimeManager] Failed to connect runtime Folder={FolderName} ClientId={ClientId}.", folderName, clientId);
            throw;
        }
        finally
        {
            RemovePendingConnect(folderName, clientId);
        }
    }

    private static bool TryGetCurrentDefinition(string folderName, string clientId, out UdlClientDefinition definition)
    {
        lock (Sync)
        {
            if (DefinitionsByFolder.TryGetValue(folderName, out var definitionsByClient)
                && definitionsByClient.TryGetValue(clientId, out var storedDefinition)
                && storedDefinition.Enabled)
            {
                definition = storedDefinition with
                {
                    AttachedItemPaths = storedDefinition.AttachedItemPaths.ToArray()
                };
                return true;
            }
        }

        definition = default!;
        return false;
    }

    private static bool HasPendingConnect(string folderName, string clientId)
    {
        return PendingConnectsByFolder.TryGetValue(folderName, out var pendingByClient)
            && pendingByClient.ContainsKey(clientId);
    }

    private static bool TryGetPendingConnect(string folderName, string clientId, out Task<UdlClientRuntime>? pendingConnect)
    {
        pendingConnect = null;
        if (!PendingConnectsByFolder.TryGetValue(folderName, out var pendingByClient)
            || !pendingByClient.TryGetValue(clientId, out var existingConnect))
        {
            return false;
        }

        pendingConnect = existingConnect;
        return true;
    }

    private static void RegisterPendingConnect(string folderName, string clientId, Task<UdlClientRuntime> pendingConnect)
    {
        if (!PendingConnectsByFolder.TryGetValue(folderName, out var pendingByClient))
        {
            pendingByClient = new Dictionary<string, Task<UdlClientRuntime>>(StringComparer.OrdinalIgnoreCase);
            PendingConnectsByFolder[folderName] = pendingByClient;
        }

        pendingByClient[clientId] = pendingConnect;
    }

    private static void RemovePendingConnect(string folderName, string clientId)
    {
        lock (Sync)
        {
            if (!PendingConnectsByFolder.TryGetValue(folderName, out var pendingByClient))
            {
                return;
            }

            pendingByClient.Remove(clientId);
            if (pendingByClient.Count == 0)
            {
                PendingConnectsByFolder.Remove(folderName);
            }
        }
    }

    private static string CreateDefinitionKey(UdlClientDefinition definition)
    {
        return string.Join("\n",
            definition.ClientId,
            definition.Host,
            definition.Port.ToString(),
            definition.DebugLogging.ToString(),
            definition.DemoEnabled.ToString(),
            definition.DemoModuleDefinitions);
    }

    private sealed class RuntimeSlot : IDisposable
    {
        public RuntimeSlot(UdlClientRuntime runtime, string definitionKey, UdlClientRegistryProjection projection)
        {
            Runtime = runtime;
            DefinitionKey = definitionKey;
            Projection = projection;
        }

        public UdlClientRuntime Runtime { get; }

        public string DefinitionKey { get; private set; }

        public UdlClientRegistryProjection Projection { get; }

        public void UpdateDefinition(UdlClientDefinition definition, string definitionKey)
        {
            DefinitionKey = definitionKey;
            Projection.UpdateDefinition(definition);
        }

        public void DisposeProjection()
        {
            Projection.Dispose();
        }

        public void Dispose()
        {
            Projection.Dispose();
            Runtime.Dispose();
        }
    }
}
