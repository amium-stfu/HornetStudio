using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HornetStudio.Host;

namespace HornetStudio.Editor.Widgets;

internal sealed class ChartDataProvider : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _folderName;
    private List<RealtimeChartRuntimeManager.ChartSeriesConfiguration> _seriesConfigurations = [];
    private ChartRenderSnapshot _currentSnapshot = ChartRenderSnapshot.Empty;
    private int _viewSeconds = 30;
    private int _maxRenderPoints = 1024;
    private bool _refreshRequested;
    private int _refreshInProgress;
    private bool _isDisposed;

    internal ChartDataProvider(string folderName)
    {
        _folderName = folderName?.Trim() ?? string.Empty;
    }

    internal event EventHandler? SnapshotUpdated;

    internal void UpdateSeriesConfigurations(IReadOnlyList<RealtimeChartRuntimeManager.ChartSeriesConfiguration> seriesConfigurations)
    {
        ArgumentNullException.ThrowIfNull(seriesConfigurations);

        lock (_syncRoot)
        {
            _seriesConfigurations = [.. seriesConfigurations];
            _refreshRequested = true;
        }

        TryStartRefreshLoop();
    }

    internal void RequestSnapshotRefresh(int viewSeconds, int maxRenderPoints)
    {
        lock (_syncRoot)
        {
            _viewSeconds = Math.Max(1, viewSeconds);
            _maxRenderPoints = Math.Max(16, maxRenderPoints);
            _refreshRequested = true;
        }

        TryStartRefreshLoop();
    }

    internal ChartRenderSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _currentSnapshot;
        }
    }

    internal bool TryGetNearestPoint(string key, double xPosition, out ChartNearestPoint point)
    {
        var snapshot = GetSnapshot();
        var series = snapshot.SeriesSnapshots.FirstOrDefault(candidate => string.Equals(candidate.Configuration.Key, key, StringComparison.Ordinal));
        if (series is null || series.Timestamps.Length == 0)
        {
            point = default;
            return false;
        }

        var timestamps = series.Timestamps;
        var values = series.Values;
        var low = 0;
        var high = timestamps.Length - 1;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (timestamps[mid] < xPosition)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        var upperIndex = low;
        var lowerIndex = Math.Max(0, upperIndex - 1);
        var upperDistance = Math.Abs(timestamps[upperIndex] - xPosition);
        var lowerDistance = Math.Abs(timestamps[lowerIndex] - xPosition);
        var selectedIndex = lowerDistance <= upperDistance ? lowerIndex : upperIndex;
        point = new ChartNearestPoint(DateTime.FromOADate(timestamps[selectedIndex]), values[selectedIndex]);
        return true;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _isDisposed = true;
            _seriesConfigurations = [];
            _currentSnapshot = ChartRenderSnapshot.Empty;
            _refreshRequested = false;
        }
    }

    private void TryStartRefreshLoop()
    {
        if (_isDisposed || Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(RefreshLoopAsync);
    }

    private void RefreshLoopAsync()
    {
        try
        {
            while (true)
            {
                ProviderRequest request;
                lock (_syncRoot)
                {
                    if (_isDisposed || !_refreshRequested)
                    {
                        break;
                    }

                    _refreshRequested = false;
                    request = new ProviderRequest(_viewSeconds, _maxRenderPoints, [.. _seriesConfigurations]);
                }

                var snapshot = BuildSnapshot(request);
                lock (_syncRoot)
                {
                    if (_isDisposed)
                    {
                        break;
                    }

                    _currentSnapshot = snapshot;
                }

                SnapshotUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Core.LogError($"[RealtimeChart] ChartDataProvider refresh failed. Folder={_folderName}. {ex}");
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
            lock (_syncRoot)
            {
                if (!_isDisposed && _refreshRequested)
                {
                    TryStartRefreshLoop();
                }
            }
        }
    }

    private ChartRenderSnapshot BuildSnapshot(ProviderRequest request)
    {
        var visibleTo = DateTime.Now;
        var visibleFrom = visibleTo.AddSeconds(-request.ViewSeconds);
        var historyRuntimeAvailable = SignalHistoryRuntimeManager.TryGetRuntime(_folderName, out var historyRuntime) && historyRuntime is not null;

        var seriesSnapshots = request.SeriesConfigurations
            .Select(configuration =>
            {
                var points = historyRuntimeAvailable
                    ? historyRuntime!.GetPoints(configuration.TargetPath, configuration.PageName, visibleFrom, visibleTo)
                    : [];
                var (timestamps, values) = ToRenderArrays(points, request.MaxRenderPoints);
                return new ChartRenderSeriesSnapshot(configuration, timestamps, values);
            })
            .ToArray();

        return new ChartRenderSnapshot(visibleFrom, visibleTo, seriesSnapshots);
    }

    private static (double[] Timestamps, double[] Values) ToRenderArrays(
        IReadOnlyList<SignalHistoryRuntimeManager.HistoryPoint> points,
        int maxRenderPoints)
    {
        if (points.Count == 0)
        {
            return ([], []);
        }

        if (points.Count <= maxRenderPoints)
        {
            var timestamps = new double[points.Count];
            var values = new double[points.Count];
            for (var index = 0; index < points.Count; index++)
            {
                timestamps[index] = points[index].Timestamp.ToOADate();
                values[index] = points[index].Value;
            }

            return (timestamps, values);
        }

        var sampleCount = Math.Max(2, maxRenderPoints);
        var downsampledTimestamps = new double[sampleCount];
        var downsampledValues = new double[sampleCount];
        var step = (double)(points.Count - 1) / (sampleCount - 1);
        for (var index = 0; index < sampleCount; index++)
        {
            var sourceIndex = index == sampleCount - 1
                ? points.Count - 1
                : (int)Math.Round(index * step, MidpointRounding.AwayFromZero);
            sourceIndex = Math.Clamp(sourceIndex, 0, points.Count - 1);
            downsampledTimestamps[index] = points[sourceIndex].Timestamp.ToOADate();
            downsampledValues[index] = points[sourceIndex].Value;
        }

        return (downsampledTimestamps, downsampledValues);
    }

    private readonly record struct ProviderRequest(
        int ViewSeconds,
        int MaxRenderPoints,
        IReadOnlyList<RealtimeChartRuntimeManager.ChartSeriesConfiguration> SeriesConfigurations);
}

internal sealed record ChartRenderSeriesSnapshot(
    RealtimeChartRuntimeManager.ChartSeriesConfiguration Configuration,
    double[] Timestamps,
    double[] Values);

internal sealed record ChartRenderSnapshot(
    DateTime VisibleFrom,
    DateTime VisibleTo,
    IReadOnlyList<ChartRenderSeriesSnapshot> SeriesSnapshots)
{
    internal static ChartRenderSnapshot Empty { get; } = new(DateTime.MinValue, DateTime.MinValue, Array.Empty<ChartRenderSeriesSnapshot>());
}

internal readonly record struct ChartNearestPoint(DateTime Timestamp, double Value);