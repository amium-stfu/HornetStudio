using System;
using System.Collections.Generic;
using System.Linq;
using HornetStudio.Editor.Models;

namespace HornetStudio.Host;

public static class EnhancedSignalRuntimeManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Dictionary<string, EnhancedSignalRuntime>> RuntimesByFolder = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DefinitionStore> DefinitionStores = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<EnhancedSignalRuntime> SyncDefinitions(string folderName, string? rawDefinitions)
        => SyncDefinitions(folderName, rawDefinitions, forceRecreate: false, rawDefinitionsGetter: null, rawDefinitionsSetter: null);

    public static IReadOnlyList<EnhancedSignalRuntime> SyncDefinitions(string folderName, string? rawDefinitions, bool forceRecreate)
        => SyncDefinitions(folderName, rawDefinitions, forceRecreate, rawDefinitionsGetter: null, rawDefinitionsSetter: null);

    public static IReadOnlyList<EnhancedSignalRuntime> SyncDefinitions(
        string folderName,
        string? rawDefinitions,
        bool forceRecreate,
        Func<string?>? rawDefinitionsGetter,
        Action<string>? rawDefinitionsSetter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);
        var definitions = ExtendedSignalDefinitionJsonCodec.ParseDefinitions(rawDefinitions)
            .Where(static definition => definition.Enabled && !string.IsNullOrWhiteSpace(definition.Name))
            .Select(static definition => definition.Clone())
            .ToArray();

        lock (Sync)
        {
            if (rawDefinitionsGetter is not null && rawDefinitionsSetter is not null)
            {
                DefinitionStores[normalizedFolder] = new DefinitionStore(rawDefinitionsGetter, rawDefinitionsSetter);
            }

            if (!RuntimesByFolder.TryGetValue(normalizedFolder, out var runtimes))
            {
                runtimes = new Dictionary<string, EnhancedSignalRuntime>(StringComparer.OrdinalIgnoreCase);
                RuntimesByFolder[normalizedFolder] = runtimes;
            }

            var desiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                var path = EnhancedSignalRuntime.BuildRegistryPath(normalizedFolder, definition);
                desiredPaths.Add(path);

                if (runtimes.TryGetValue(path, out var existing))
                {
                    if (!forceRecreate && DefinitionsEqual(existing.Definition, definition))
                    {
                        continue;
                    }

                    existing.Dispose();
                    runtimes.Remove(path);
                }

                runtimes[path] = new EnhancedSignalRuntime(normalizedFolder, definition);
            }

            foreach (var stale in runtimes.Keys.Where(path => !desiredPaths.Contains(path)).ToArray())
            {
                runtimes[stale].Dispose();
                runtimes.Remove(stale);
            }

            if (runtimes.Count == 0)
            {
                RuntimesByFolder.Remove(normalizedFolder);
                return Array.Empty<EnhancedSignalRuntime>();
            }

            return runtimes.Values.OrderBy(runtime => runtime.Definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public static void ReleaseFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);
        lock (Sync)
        {
            if (!RuntimesByFolder.TryGetValue(normalizedFolder, out var runtimes))
            {
                return;
            }

            foreach (var runtime in runtimes.Values)
            {
                runtime.Dispose();
            }

            RuntimesByFolder.Remove(normalizedFolder);
            DefinitionStores.Remove(normalizedFolder);
        }
    }

    public static bool TryUpdateDefinition(string folderName, ExtendedSignalDefinition updatedDefinition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(updatedDefinition);

        string? newRawDefinitions = null;
        Action<string>? setter = null;
        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);

        lock (Sync)
        {
            if (!DefinitionStores.TryGetValue(normalizedFolder, out var store))
            {
                return false;
            }

            var definitions = ExtendedSignalDefinitionJsonCodec.ParseDefinitions(store.RawDefinitionsGetter())
                .Select(static definition => definition.Clone())
                .ToList();

            var index = definitions.FindIndex(definition => string.Equals(definition.Name, updatedDefinition.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            definitions[index] = updatedDefinition.Clone();
            newRawDefinitions = ExtendedSignalDefinitionJsonCodec.SerializeDefinitions(definitions);
            setter = store.RawDefinitionsSetter;
        }

        setter?.Invoke(newRawDefinitions ?? string.Empty);
        return true;
    }

    public static bool TryGetRuntime(string registryPath, out EnhancedSignalRuntime? runtime)
    {
        lock (Sync)
        {
            foreach (var folder in RuntimesByFolder.Values)
            {
                if (folder.TryGetValue(registryPath, out var found))
                {
                    runtime = found;
                    return true;
                }
            }
        }

        runtime = null;
        return false;
    }

    private static bool DefinitionsEqual(ExtendedSignalDefinition left, ExtendedSignalDefinition right)
    {
        return ExtendedSignalDefinitionJsonCodec.SerializeDefinitions([left])
            .Equals(ExtendedSignalDefinitionJsonCodec.SerializeDefinitions([right]), StringComparison.Ordinal);
    }

    private sealed record DefinitionStore(Func<string?> RawDefinitionsGetter, Action<string> RawDefinitionsSetter);
}
