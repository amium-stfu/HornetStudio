using System;
using System.Collections.Generic;
using System.Linq;
using HornetStudio.Editor.Models;
using HornetStudio.Host.Runtimes.EnhancedSignal;

namespace HornetStudio.Host.Runtimes.Controller;

/// <summary>
/// Synchronizes controller runtimes for a folder based on persisted controller definitions.
/// </summary>
public static class ControllerRuntimeManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Dictionary<string, PidControllerRuntime>> RuntimesByFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Synchronizes folder runtimes to the provided controller definitions.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="rawDefinitions">The raw controller definitions.</param>
    /// <param name="forceRecreate">A value indicating whether existing runtimes should always be recreated.</param>
    /// <returns>The active runtimes for the folder.</returns>
    public static IReadOnlyList<PidControllerRuntime> SyncDefinitions(string folderName, string? rawDefinitions, bool forceRecreate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);
        var definitions = ControllerDefinitionJsonCodec.ParseDefinitions(rawDefinitions)
            .Where(static definition => definition.Enabled && !string.IsNullOrWhiteSpace(definition.Name))
            .Select(static definition => definition.Clone().Normalize())
            .ToArray();

        lock (Sync)
        {
            if (!RuntimesByFolder.TryGetValue(normalizedFolder, out var runtimes))
            {
                runtimes = new Dictionary<string, PidControllerRuntime>(StringComparer.OrdinalIgnoreCase);
                RuntimesByFolder[normalizedFolder] = runtimes;
            }

            var desiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                var path = PidControllerRuntime.BuildRegistryPath(normalizedFolder, definition);
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

                runtimes[path] = new PidControllerRuntime(normalizedFolder, definition);
            }

            foreach (var stale in runtimes.Keys.Where(path => !desiredPaths.Contains(path)).ToArray())
            {
                runtimes[stale].Dispose();
                runtimes.Remove(stale);
            }

            if (runtimes.Count == 0)
            {
                RuntimesByFolder.Remove(normalizedFolder);
                return Array.Empty<PidControllerRuntime>();
            }

            return runtimes.Values.OrderBy(runtime => runtime.Definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Releases all controller runtimes for a folder.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
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
        }
    }

    private static bool DefinitionsEqual(ControllerDefinition left, ControllerDefinition right)
    {
        return ControllerDefinitionJsonCodec.SerializeDefinitions([left])
            .Equals(ControllerDefinitionJsonCodec.SerializeDefinitions([right]), StringComparison.Ordinal);
    }
}