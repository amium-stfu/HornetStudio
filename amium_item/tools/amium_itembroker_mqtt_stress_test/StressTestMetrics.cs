using System.Collections.Concurrent;
using System.Diagnostics;

namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Aggregates MQTT stress test counters, ordering data, latency data, and throughput.
/// </summary>
public sealed class StressTestMetrics
{
    private const int MaxLatencySamples = 20_000;
    private readonly ConcurrentDictionary<string, SignalReceiveState> _signalStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<double> _latencySamples = new();
    private readonly Lock _latencyLock = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _published;
    private long _received;
    private long _duplicates;
    private long _outOfOrder;
    private long _maxPending;
    private long _maxLatencyTicks;

    /// <summary>
    /// Records one successfully published stress message.
    /// </summary>
    public void RecordPublished()
    {
        var published = Interlocked.Increment(ref _published);
        var received = Interlocked.Read(ref _received);
        UpdateMaxPending(published: published, received: received);
    }

    /// <summary>
    /// Records one received stress message and updates delivery metrics.
    /// </summary>
    /// <param name="path">The received item path.</param>
    /// <param name="payload">The parsed stress payload.</param>
    public void RecordReceived(string path, StressTestPayload payload)
    {
        var received = Interlocked.Increment(ref _received);
        var published = Interlocked.Read(ref _published);
        UpdateMaxPending(published: published, received: received);
        var state = _signalStates.GetOrAdd(path, _ => new SignalReceiveState());
        var receiveResult = state.Record(payload.Sequence);

        if (receiveResult == SignalReceiveResult.Duplicate)
        {
            Interlocked.Increment(ref _duplicates);
        }
        else if (receiveResult == SignalReceiveResult.OutOfOrder)
        {
            Interlocked.Increment(ref _outOfOrder);
        }

        var elapsed = Stopwatch.GetElapsedTime(payload.SentTimestamp, Stopwatch.GetTimestamp());
        RecordLatency(elapsed);
    }

    /// <summary>
    /// Creates a point-in-time metric snapshot for UI rendering.
    /// </summary>
    /// <returns>The current metric snapshot.</returns>
    public StressTestMetricsSnapshot CreateSnapshot()
    {
        var published = Interlocked.Read(ref _published);
        var received = Interlocked.Read(ref _received);
        var duplicates = Interlocked.Read(ref _duplicates);
        var outOfOrder = Interlocked.Read(ref _outOfOrder);
        var pending = Math.Max(0, published - received);
        var maxPending = Interlocked.Read(ref _maxPending);
        var elapsedSeconds = Math.Max(_elapsed.Elapsed.TotalSeconds, 0.001);
        var samples = GetLatencySamples();

        return new StressTestMetricsSnapshot(
            Published: published,
            Received: received,
            Pending: pending,
            MaxPending: maxPending,
            Duplicates: duplicates,
            OutOfOrder: outOfOrder,
            PublishRatePerSecond: published / elapsedSeconds,
            ReceiveRatePerSecond: received / elapsedSeconds,
            AverageLatencyMilliseconds: samples.Length == 0 ? 0 : samples.Average(),
            MaxLatencyMilliseconds: TimeSpan.FromTicks(Interlocked.Read(ref _maxLatencyTicks)).TotalMilliseconds,
            P95LatencyMilliseconds: Percentile(samples, percentile: 0.95),
            P99LatencyMilliseconds: Percentile(samples, percentile: 0.99),
            Elapsed: _elapsed.Elapsed);
    }

    private void UpdateMaxPending(long published, long received)
    {
        var pending = Math.Max(0, published - received);
        if (pending <= 0)
        {
            return;
        }

        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _maxPending);
            if (pending <= currentMax)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxPending, pending, currentMax) != currentMax);
    }

    private void RecordLatency(TimeSpan latency)
    {
        var latencyTicks = Math.Max(0, latency.Ticks);
        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _maxLatencyTicks);
            if (latencyTicks <= currentMax)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _maxLatencyTicks, latencyTicks, currentMax) != currentMax);

        lock (_latencyLock)
        {
            if (_latencySamples.Count >= MaxLatencySamples)
            {
                _latencySamples.Dequeue();
            }

            _latencySamples.Enqueue(TimeSpan.FromTicks(latencyTicks).TotalMilliseconds);
        }
    }

    private double[] GetLatencySamples()
    {
        lock (_latencyLock)
        {
            return _latencySamples.ToArray();
        }
    }

    private static double Percentile(double[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        Array.Sort(samples);
        var index = (int)Math.Ceiling(samples.Length * percentile) - 1;
        return samples[Math.Clamp(index, min: 0, max: samples.Length - 1)];
    }

    private sealed class SignalReceiveState
    {
        private readonly Lock _lock = new();
        private long _highestSequence;
        private readonly HashSet<long> _seenSequences = new();

        public SignalReceiveResult Record(long sequence)
        {
            lock (_lock)
            {
                if (!_seenSequences.Add(sequence))
                {
                    return SignalReceiveResult.Duplicate;
                }

                if (sequence < _highestSequence)
                {
                    return SignalReceiveResult.OutOfOrder;
                }

                _highestSequence = sequence;
                return SignalReceiveResult.Accepted;
            }
        }
    }

    private enum SignalReceiveResult
    {
        Accepted,
        Duplicate,
        OutOfOrder,
    }
}

/// <summary>
/// Represents a point-in-time stress test metric snapshot.
/// </summary>
/// <param name="Published">The total number of published messages.</param>
/// <param name="Received">The total number of received messages.</param>
/// <param name="Pending">The current receive backlog inferred from published minus received messages.</param>
/// <param name="MaxPending">The highest observed receive backlog during the run.</param>
/// <param name="Duplicates">The duplicate receive count.</param>
/// <param name="OutOfOrder">The out-of-order receive count.</param>
/// <param name="PublishRatePerSecond">The average publish throughput.</param>
/// <param name="ReceiveRatePerSecond">The average receive throughput.</param>
/// <param name="AverageLatencyMilliseconds">The average measured latency in milliseconds.</param>
/// <param name="MaxLatencyMilliseconds">The maximum measured latency in milliseconds.</param>
/// <param name="P95LatencyMilliseconds">The p95 measured latency in milliseconds.</param>
/// <param name="P99LatencyMilliseconds">The p99 measured latency in milliseconds.</param>
/// <param name="Elapsed">The elapsed run time.</param>
public sealed record StressTestMetricsSnapshot(
    long Published,
    long Received,
    long Pending,
    long MaxPending,
    long Duplicates,
    long OutOfOrder,
    double PublishRatePerSecond,
    double ReceiveRatePerSecond,
    double AverageLatencyMilliseconds,
    double MaxLatencyMilliseconds,
    double P95LatencyMilliseconds,
    double P99LatencyMilliseconds,
    TimeSpan Elapsed);
