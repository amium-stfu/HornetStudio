using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using ScottPlot;

namespace HornetStudio.Editor.Widgets;

internal static class RealtimeChartRuntimeManager
{
    private static readonly ConcurrentDictionary<string, RealtimeChartRuntimeState> StatesByIdentity = new(StringComparer.Ordinal);

    internal static RealtimeChartRuntimeState? GetOrCreate(FolderItemModel? item)
    {
        if (item is null || !TryBuildIdentity(item, out var identity))
        {
            return null;
        }

        var configuration = CreateChartStateConfiguration(item);
        var state = StatesByIdentity.AddOrUpdate(
            identity,
            key =>
            {
                var created = new RealtimeChartRuntimeState(key, configuration);
                Core.LogDebug($"[RealtimeChart] Runtime created. ChartId={key}");
                return created;
            },
            (key, existing) =>
            {
                existing.UpdateConfiguration(configuration);
                Core.LogDebug($"[RealtimeChart] Runtime attached. ChartId={key}");
                return existing;
            });

        return state;
    }

    internal static List<ChartSeriesConfiguration> GetSeriesConfigurations(FolderItemModel? item)
    {
        return item is null
            ? []
            : CreateChartStateConfiguration(item).SeriesConfigurations;
    }

    internal static void ReleaseFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var folderPrefix = NormalizeFolderName(folderName) + "|";
        foreach (var entry in StatesByIdentity)
        {
            if (!entry.Key.StartsWith(folderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (StatesByIdentity.TryRemove(entry.Key, out var state))
            {
                state.Dispose();
                Core.LogDebug($"[RealtimeChart] Runtime released. ChartId={entry.Key}");
            }
        }
    }

    private static bool TryBuildIdentity(FolderItemModel? item, out string identity)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            identity = string.Empty;
            return false;
        }

        identity = NormalizeFolderName(item.FolderName) + "|" + item.Id.Trim();
        return true;
    }

    private static string NormalizeFolderName(string? folderName)
    {
        return string.IsNullOrWhiteSpace(folderName)
            ? string.Empty
            : folderName.Trim();
    }

    private static ChartStateConfiguration CreateChartStateConfiguration(FolderItemModel item)
    {
        var seriesConfigurations = ParseSeriesDefinitions(item.ChartSeriesDefinitions, item.FolderName);
        if (seriesConfigurations.Count == 0 && !string.IsNullOrWhiteSpace(item.TargetPath))
        {
            seriesConfigurations = [CreateSeriesConfiguration(item.TargetPath, item.FolderName, 1)];
        }

        return new ChartStateConfiguration(
            FolderName: NormalizeFolderName(item.FolderName),
            SeriesConfigurations: seriesConfigurations);
    }

    private static List<ChartSeriesConfiguration> ParseSeriesDefinitions(string? raw, string? pageName)
    {
        var result = new List<ChartSeriesConfiguration>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        var lines = raw.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var axisIndex = 1;
            if (parts.Length > 1)
            {
                var axisText = parts[1].StartsWith("Y", StringComparison.OrdinalIgnoreCase) ? parts[1][1..] : parts[1];
                if (!int.TryParse(axisText, NumberStyles.Integer, CultureInfo.InvariantCulture, out axisIndex))
                {
                    axisIndex = 1;
                }
            }

            var connectStyle = parts.Length > 2 ? ParseConnectStyle(parts[2]) : ConnectStyle.Straight;
            result.Add(CreateSeriesConfiguration(TargetPathHelper.NormalizeConfiguredTargetPath(parts[0]), pageName, axisIndex, connectStyle));
        }

        return result;
    }

    private static ChartSeriesConfiguration CreateSeriesConfiguration(string targetPath, string? pageName, int axisIndex, ConnectStyle connectStyle = ConnectStyle.Straight)
    {
        targetPath = TargetPathHelper.NormalizeConfiguredTargetPath(targetPath);
        var normalizedAxis = Math.Clamp(axisIndex, 1, 4);
        var styleKey = connectStyle switch
        {
            ConnectStyle.StepHorizontal => "Step",
            ConnectStyle.StepVertical => "StepVertical",
            _ => "Line"
        };

        return new ChartSeriesConfiguration(
            TargetPath: targetPath,
            PageName: pageName ?? string.Empty,
            AxisIndex: normalizedAxis,
            ConnectStyle: connectStyle,
            Key: $"{targetPath}|Y{normalizedAxis}|{styleKey}",
            DisplayName: targetPath);
    }

    private static ConnectStyle ParseConnectStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return ConnectStyle.Straight;
        }

        return style.Trim().ToLowerInvariant() switch
        {
            "step" => ConnectStyle.StepHorizontal,
            "stephorizontal" => ConnectStyle.StepHorizontal,
            "stepvertical" => ConnectStyle.StepVertical,
            "line" => ConnectStyle.Straight,
            "straight" => ConnectStyle.Straight,
            _ => ConnectStyle.Straight
        };
    }

    internal sealed record ChartSeriesConfiguration(string TargetPath, string PageName, int AxisIndex, ConnectStyle ConnectStyle, string Key, string DisplayName);

    internal sealed record ChartStateConfiguration(string FolderName, List<ChartSeriesConfiguration> SeriesConfigurations);

    internal sealed class RealtimeChartRuntimeState : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly ChartDataProvider _provider;
        private List<ChartSeriesConfiguration> _seriesConfigurations = [];
        private string _folderName;

        internal RealtimeChartRuntimeState(string chartIdentity, ChartStateConfiguration configuration)
        {
            _folderName = configuration.FolderName;
            _provider = new ChartDataProvider(configuration.FolderName);
            UpdateConfiguration(configuration);
        }

        internal event EventHandler? SnapshotUpdated
        {
            add => _provider.SnapshotUpdated += value;
            remove => _provider.SnapshotUpdated -= value;
        }

        internal void UpdateConfiguration(ChartStateConfiguration configuration)
        {
            lock (_syncRoot)
            {
                _folderName = configuration.FolderName;
                _seriesConfigurations = configuration.SeriesConfigurations;
            }

            _provider.UpdateSeriesConfigurations(configuration.SeriesConfigurations);
        }

        internal void RequestSnapshotRefresh(int viewSeconds, int maxRenderPoints)
        {
            _provider.RequestSnapshotRefresh(viewSeconds, maxRenderPoints);
        }

        internal ChartRenderSnapshot GetRenderSnapshot()
        {
            return _provider.GetSnapshot();
        }

        internal bool TryGetNearestPoint(string key, double xPosition, out ChartNearestPoint point)
        {
            return _provider.TryGetNearestPoint(key, xPosition, out point);
        }

        internal void Clear()
        {
            List<ChartSeriesConfiguration> seriesConfigurations;
            lock (_syncRoot)
            {
                seriesConfigurations = [.. _seriesConfigurations];
            }

            if (SignalHistoryRuntimeManager.TryGetRuntime(_folderName, out var historyRuntime) && historyRuntime is not null)
            {
                historyRuntime.ClearSeries(seriesConfigurations);
            }
        }

        public void Dispose()
        {
            _provider.Dispose();
        }
    }
}