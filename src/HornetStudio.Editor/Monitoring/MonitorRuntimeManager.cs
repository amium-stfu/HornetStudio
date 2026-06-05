using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Maintains folder-level monitor runtime evaluation independently from the Monitor browser widget.
/// </summary>
public static class MonitorRuntimeManager
{
    private static readonly ConcurrentDictionary<string, MonitorFolderRuntimeState> StatesByFolder = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions AggregateMetaJsonOptions = new();

    /// <summary>
    /// Synchronizes the folder-level monitor runtime for one folder.
    /// </summary>
    /// <param name="folderName">The technical folder name.</param>
    /// <param name="definitions">The current monitor definitions.</param>
    /// <param name="forceRecreate">When true, existing runtimes are rebuilt even if the definition signature matches.</param>
    /// <returns>The active runtime rules.</returns>
    public static IReadOnlyList<MonitorRuleRuntime> SyncDefinitions(string folderName, IEnumerable<MonitorDefinition> definitions, bool forceRecreate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definitions);

        var normalizedFolderName = folderName.Trim();
        var definitionList = definitions
            .Where(static definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Name))
            .Select(static definition => definition.Clone())
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (definitionList.Length == 0)
        {
            ReleaseFolder(normalizedFolderName);
            return [];
        }

        var state = StatesByFolder.AddOrUpdate(
            normalizedFolderName,
            static (key, defs) => new MonitorFolderRuntimeState(key, defs),
            static (key, existing, defs) =>
            {
                existing.UpdateDefinitions(defs, forceRecreate: false);
                return existing;
            },
            definitionList);

        if (forceRecreate)
        {
            state.UpdateDefinitions(definitionList, forceRecreate: true);
        }

        return state.Runtimes;
    }

    /// <summary>
    /// Releases all runtime state for one folder.
    /// </summary>
    /// <param name="folderName">The technical folder name.</param>
    public static void ReleaseFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var normalizedFolderName = folderName.Trim();
        if (StatesByFolder.TryRemove(normalizedFolderName, out var state))
        {
            state.Dispose();
        }

        HostRegistries.Data.Remove(MonitorRegistry.BuildAggregatePath(normalizedFolderName));
    }

    private sealed class MonitorFolderRuntimeState : IDisposable
    {
        private readonly object _syncRoot = new();
        private MonitorRuleRuntime[] _runtimes = [];
        private string _definitionSignature = string.Empty;

        public MonitorFolderRuntimeState(string folderName, IReadOnlyList<MonitorDefinition> definitions)
        {
            PageName = folderName;
            UpdateDefinitions(definitions, forceRecreate: true);
        }

        public string PageName { get; }

        public IReadOnlyList<MonitorRuleRuntime> Runtimes => _runtimes;

        public void UpdateDefinitions(IReadOnlyList<MonitorDefinition> definitions, bool forceRecreate)
        {
            lock (_syncRoot)
            {
                var nextSignature = BuildDefinitionSignature(definitions);
                if (!forceRecreate && string.Equals(_definitionSignature, nextSignature, StringComparison.Ordinal))
                {
                    return;
                }

                foreach (var runtime in _runtimes)
                {
                    runtime.Dispose();
                }

                _runtimes = definitions
                    .Select(definition => new MonitorRuleRuntime(PageName, definition, PublishAggregateRuntime))
                    .ToArray();
                _definitionSignature = nextSignature;

                PublishAggregateRuntime();
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                foreach (var runtime in _runtimes)
                {
                    runtime.Dispose();
                }

                _runtimes = [];
                _definitionSignature = string.Empty;
            }
        }

        private void PublishAggregateRuntime()
        {
            var runtimePath = MonitorRegistry.BuildAggregatePath(PageName);
            var segments = TargetPathHelper.SplitPathSegments(runtimePath);
            if (segments.Count == 0)
            {
                return;
            }

            var nameSegment = segments[^1];
            var parentPath = segments.Count > 1 ? string.Join('.', segments.Take(segments.Count - 1)) : string.Empty;
            var active = _runtimes.Any(static runtime => runtime.IsActive);
            var snapshot = string.IsNullOrWhiteSpace(parentPath)
                ? new ItemModel(nameSegment, active)
                : new ItemModel(nameSegment, active, parentPath);

            snapshot.Properties["path"].Value = runtimePath;
            snapshot.Properties["kind"].Value = "MonitorAggregate";
            snapshot.Properties["text"].Value = "Monitor";
            snapshot.Properties["title"].Value = "Monitor";
            snapshot["active"].Value = active;
            snapshot["active"].Properties["text"].Value = "Active";
            snapshot["active_count"].Value = _runtimes.Count(static runtime => runtime.IsActive);
            snapshot["active_count"].Properties["text"].Value = "ActiveCount";

            foreach (var aggregate in BuildActiveEventIdAggregates(_runtimes))
            {
                snapshot[aggregate.ItemName].Value = aggregate.EventIds;
                snapshot[aggregate.ItemName].Properties["text"].Value = aggregate.ItemName;
                snapshot[aggregate.ItemName].Properties["meta"].Value = aggregate.MetaJson;
            }

            HostRegistries.Data.UpsertSnapshot(runtimePath, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
        }

        private static string BuildDefinitionSignature(IReadOnlyList<MonitorDefinition> definitions)
            => MonitorDefinitionCodec.SerializeDefinitions(definitions);

        private static IReadOnlyList<MonitorAggregateItem> BuildActiveEventIdAggregates(IEnumerable<MonitorRuleRuntime> runtimes)
        {
            var result = new List<MonitorAggregateItem>();
            foreach (var level in Enum.GetValues<MonitorLogLevel>())
            {
                var itemName = $"{TargetPathHelper.NormalizePathSegment(level.ToString(), level.ToString().ToLowerInvariant())}_active";
                var events = runtimes
                    .Where(runtime => runtime.IsActive && runtime.Definition.LogLevel == level)
                    .OrderBy(runtime => runtime.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(runtime => new MonitorAggregateEvent(runtime.Definition.EventId, runtime.Definition.EventText ?? string.Empty))
                    .ToArray();
                result.Add(new MonitorAggregateItem(
                    itemName,
                    string.Join(',', events.Select(static entry => entry.EventId.ToString(CultureInfo.InvariantCulture))),
                    JsonSerializer.Serialize(new MonitorAggregateMeta(events), AggregateMetaJsonOptions)));
            }

            return result;
        }

        private sealed record MonitorAggregateItem(string ItemName, string EventIds, string MetaJson);

        private sealed record MonitorAggregateMeta(IReadOnlyList<MonitorAggregateEvent> Events);

        private sealed record MonitorAggregateEvent(int EventId, string Text);
    }
}