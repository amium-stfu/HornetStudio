using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host.Registries;
using HornetStudio.Logging;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.Widgets;

internal static class SignalHistoryRuntimeManager
{
    private static readonly ConcurrentDictionary<string, SignalHistoryFolderRuntime> StatesByFolder = new(StringComparer.OrdinalIgnoreCase);

    internal static void SyncDefinitions(string folderName, IEnumerable<SignalHistorySourceDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definitions);

        var normalizedFolderName = folderName.Trim();
        var definitionList = definitions
            .Where(static definition => definition is not null
                && definition.HistorySeconds > 0
                && definition.RefreshRateMs > 0
                && !string.IsNullOrWhiteSpace(definition.TargetPath))
            .Select(static definition => definition.Normalize())
            .GroupBy(static definition => definition.SourceKey, StringComparer.Ordinal)
            .Select(static group => MergeDefinitions(group))
            .OrderBy(static definition => definition.SourceKey, StringComparer.Ordinal)
            .ToArray();

        if (definitionList.Length == 0)
        {
            ReleaseFolder(normalizedFolderName);
            return;
        }

        StatesByFolder.AddOrUpdate(
            normalizedFolderName,
            static (_, defs) => new SignalHistoryFolderRuntime(defs),
            static (_, existing, defs) =>
            {
                existing.UpdateDefinitions(defs);
                return existing;
            },
            definitionList);
    }

    internal static bool TryGetRuntime(string? folderName, out SignalHistoryFolderRuntime? runtime)
    {
        runtime = null;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        return StatesByFolder.TryGetValue(folderName.Trim(), out runtime);
    }

    internal static void ReleaseFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        if (StatesByFolder.TryRemove(folderName.Trim(), out var runtime))
        {
            runtime.Dispose();
        }
    }

    internal sealed record SignalHistorySourceDefinition(
        string TargetPath,
        string PageName,
        string DisplayName,
        int HistorySeconds,
        int RefreshRateMs,
        string SourceKey)
    {
        internal static SignalHistorySourceDefinition Create(
            string targetPath,
            string? pageName,
            string? displayName,
            int historySeconds,
            int refreshRateMs)
        {
            var normalizedTargetPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
            var normalizedPageName = pageName?.Trim() ?? string.Empty;
            return new SignalHistorySourceDefinition(
                TargetPath: normalizedTargetPath,
                PageName: normalizedPageName,
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? normalizedTargetPath : displayName.Trim(),
                HistorySeconds: Math.Max(0, historySeconds),
                RefreshRateMs: Math.Max(0, refreshRateMs),
                SourceKey: BuildSourceKey(normalizedTargetPath, normalizedPageName));
        }

        internal SignalHistorySourceDefinition Normalize()
            => Create(TargetPath, PageName, DisplayName, HistorySeconds, RefreshRateMs);
    }

    internal readonly record struct HistoryPoint(DateTime Timestamp, double Value);

    internal sealed class SignalHistoryFolderRuntime : IDisposable
    {
        private static readonly TimeSpan MissingBindingRetryInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ResolvedBindingRefreshInterval = TimeSpan.FromSeconds(5);

        private readonly object _syncRoot = new();
        private SignalSourceState[] _sources = [];
        private string _definitionSignature = string.Empty;
        private System.Threading.Timer? _sampleTimer;
        private int _sampleInProgress;

        internal SignalHistoryFolderRuntime(IReadOnlyList<SignalHistorySourceDefinition> definitions)
        {
            UpdateDefinitions(definitions);
        }

        internal void UpdateDefinitions(IReadOnlyList<SignalHistorySourceDefinition> definitions)
        {
            lock (_syncRoot)
            {
                var signature = BuildDefinitionSignature(definitions);
                if (string.Equals(_definitionSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                var existingByKey = _sources.ToDictionary(static source => source.Definition.SourceKey, StringComparer.Ordinal);
                var nextSources = new List<SignalSourceState>(definitions.Count);
                foreach (var definition in definitions)
                {
                    if (!existingByKey.TryGetValue(definition.SourceKey, out var existing))
                    {
                        nextSources.Add(new SignalSourceState(definition));
                        continue;
                    }

                    existing.Definition = definition;
                    nextSources.Add(existing);
                }

                _sources = [.. nextSources];
                _definitionSignature = signature;

                var now = DateTime.Now;
                foreach (var source in _sources)
                {
                    TrimSourceLocked(source, now);
                }

                UpdateTimerLocked();
            }
        }

        internal HistoryPoint[] GetPoints(string targetPath, string? pageName, DateTime visibleFrom, DateTime visibleTo)
        {
            var sourceKey = BuildSourceKey(targetPath, pageName);
            lock (_syncRoot)
            {
                var source = _sources.FirstOrDefault(candidate => string.Equals(candidate.Definition.SourceKey, sourceKey, StringComparison.Ordinal));
                if (source is null)
                {
                    return [];
                }

                TrimSourceLocked(source, DateTime.Now);
                return source.Points
                    .Where(point => point.Timestamp >= visibleFrom && point.Timestamp <= visibleTo)
                    .ToArray();
            }
        }

        internal void ClearSeries(IEnumerable<RealtimeChartRuntimeManager.ChartSeriesConfiguration> seriesConfigurations)
        {
            ArgumentNullException.ThrowIfNull(seriesConfigurations);

            var sourceKeys = seriesConfigurations
                .Select(configuration => BuildSourceKey(configuration.TargetPath, configuration.PageName))
                .ToHashSet(StringComparer.Ordinal);

            lock (_syncRoot)
            {
                foreach (var source in _sources)
                {
                    if (sourceKeys.Contains(source.Definition.SourceKey))
                    {
                        source.Points.Clear();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _sampleTimer?.Dispose();
                _sampleTimer = null;
                _sources = [];
                _definitionSignature = string.Empty;
            }
        }

        private void OnSampleTimerTick(object? state)
        {
            if (Interlocked.Exchange(ref _sampleInProgress, 1) == 1)
            {
                return;
            }

            try
            {
                SampleDueSources();
            }
            finally
            {
                Volatile.Write(ref _sampleInProgress, 0);
            }
        }

        private void SampleDueSources()
        {
            var updates = new List<SampleUpdate>();
            List<SignalHistorySourceDefinition> dueDefinitions;
            var now = DateTime.Now;
            var nowUtc = DateTime.UtcNow;

            lock (_syncRoot)
            {
                dueDefinitions = _sources
                    .Where(source => now >= source.NextSampleAt)
                    .Select(source => source.Definition)
                    .ToList();
            }

            if (dueDefinitions.Count == 0)
            {
                return;
            }

            foreach (var definition in dueDefinitions)
            {
                ItemModel? resolvedItem;
                DateTime nextResolveUtc;

                lock (_syncRoot)
                {
                    var source = _sources.FirstOrDefault(candidate => string.Equals(candidate.Definition.SourceKey, definition.SourceKey, StringComparison.Ordinal));
                    if (source is null)
                    {
                        continue;
                    }

                    resolvedItem = source.ResolvedItem;
                    nextResolveUtc = source.NextResolveUtc;
                }

                if (resolvedItem is null || nowUtc >= nextResolveUtc)
                {
                    if (TryResolveSourceItem(definition.TargetPath, definition.PageName, out var nextResolvedItem) && nextResolvedItem is not null)
                    {
                        resolvedItem = nextResolvedItem;
                        nextResolveUtc = nowUtc + ResolvedBindingRefreshInterval;
                    }
                    else
                    {
                        resolvedItem = null;
                        nextResolveUtc = nowUtc + MissingBindingRetryInterval;
                    }
                }

                var value = 0d;
                var hasSample = resolvedItem is not null && TryResolveNumericValue(resolvedItem, out value);
                if (!hasSample)
                {
                    resolvedItem = null;
                    nextResolveUtc = nowUtc + MissingBindingRetryInterval;
                }

                updates.Add(new SampleUpdate(
                    SourceKey: definition.SourceKey,
                    ResolvedItem: resolvedItem,
                    NextResolveUtc: nextResolveUtc,
                    NextSampleAt: now + TimeSpan.FromMilliseconds(definition.RefreshRateMs),
                    Timestamp: now,
                    HasSample: hasSample,
                    Value: value));
            }

            if (updates.Count == 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                foreach (var update in updates)
                {
                    var source = _sources.FirstOrDefault(candidate => string.Equals(candidate.Definition.SourceKey, update.SourceKey, StringComparison.Ordinal));
                    if (source is null)
                    {
                        continue;
                    }

                    source.ResolvedItem = update.ResolvedItem;
                    source.NextResolveUtc = update.NextResolveUtc;
                    source.NextSampleAt = update.NextSampleAt;
                    if (update.HasSample)
                    {
                        source.Points.Add(new HistoryPoint(update.Timestamp, update.Value));
                    }

                    TrimSourceLocked(source, update.Timestamp);
                }
            }
        }

        private void UpdateTimerLocked()
        {
            if (_sources.Length == 0)
            {
                _sampleTimer?.Dispose();
                _sampleTimer = null;
                return;
            }

            var intervalMs = _sources.Min(source => Math.Max(1, source.Definition.RefreshRateMs));
            _sampleTimer ??= new System.Threading.Timer(OnSampleTimerTick, null, Timeout.Infinite, Timeout.Infinite);
            _sampleTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
        }

        private static void TrimSourceLocked(SignalSourceState source, DateTime now)
        {
            if (source.Definition.HistorySeconds <= 0)
            {
                source.Points.Clear();
                return;
            }

            var cutoff = now.AddSeconds(-source.Definition.HistorySeconds);
            source.Points.RemoveAll(point => point.Timestamp < cutoff);
        }

        private static string BuildDefinitionSignature(IEnumerable<SignalHistorySourceDefinition> definitions)
            => string.Join(Environment.NewLine, definitions.Select(static definition => $"{definition.SourceKey}|{definition.HistorySeconds}|{definition.RefreshRateMs}"));

        private sealed class SignalSourceState
        {
            internal SignalSourceState(SignalHistorySourceDefinition definition)
            {
                Definition = definition;
                NextSampleAt = DateTime.MinValue;
                NextResolveUtc = DateTime.MinValue;
            }

            internal SignalHistorySourceDefinition Definition { get; set; }

            internal List<HistoryPoint> Points { get; } = [];

            internal ItemModel? ResolvedItem { get; set; }

            internal DateTime NextResolveUtc { get; set; }

            internal DateTime NextSampleAt { get; set; }
        }

        private readonly record struct SampleUpdate(
            string SourceKey,
            ItemModel? ResolvedItem,
            DateTime NextResolveUtc,
            DateTime NextSampleAt,
            DateTime Timestamp,
            bool HasSample,
            double Value);
    }

    internal static string BuildSourceKey(string? targetPath, string? pageName)
        => $"{TargetPathHelper.NormalizeConfiguredTargetPath(targetPath)}|{pageName?.Trim() ?? string.Empty}";

    private static SignalHistorySourceDefinition MergeDefinitions(IGrouping<string, SignalHistorySourceDefinition> group)
    {
        var first = group.First();
        return first with
        {
            DisplayName = group.Select(static definition => definition.DisplayName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? first.TargetPath,
            HistorySeconds = group.Max(static definition => definition.HistorySeconds),
            RefreshRateMs = group.Min(static definition => definition.RefreshRateMs)
        };
    }

    private static bool TryResolveSourceItem(string targetPath, string? pageName, out ItemModel? item)
    {
        foreach (var candidatePath in TargetPathHelper.EnumerateResolutionCandidates(targetPath, pageName))
        {
            if (HostRegistries.Data.TryResolve(candidatePath, out item) && item is not null)
            {
                return true;
            }
        }

        item = null;
        return false;
    }

    private static bool TryResolveNumericValue(ItemModel item, out double value)
    {
        value = 0;
        var rawValue = item.Value;
        if (rawValue is null)
        {
            return false;
        }

        switch (rawValue)
        {
            case byte byteValue:
                value = byteValue;
                break;
            case sbyte sbyteValue:
                value = sbyteValue;
                break;
            case short shortValue:
                value = shortValue;
                break;
            case ushort ushortValue:
                value = ushortValue;
                break;
            case int intValue:
                value = intValue;
                break;
            case uint uintValue:
                value = uintValue;
                break;
            case long longValue:
                value = longValue;
                break;
            case ulong ulongValue:
                value = ulongValue;
                break;
            case float floatValue:
                value = floatValue;
                break;
            case double doubleValue:
                value = doubleValue;
                break;
            case decimal decimalValue:
                value = (double)decimalValue;
                break;
            case string textValue:
                if (!double.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
                    && !double.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
                {
                    return false;
                }

                break;
            default:
                if (rawValue is not IConvertible convertible)
                {
                    return false;
                }

                try
                {
                    value = convertible.ToDouble(CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    HostLogger.Log.Debug(ex, "[SignalHistory] Failed to convert value.");
                    return false;
                }

                break;
        }

        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
