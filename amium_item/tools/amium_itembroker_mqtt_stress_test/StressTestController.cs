namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Coordinates the publisher, raw subscriber, metrics, and run lifecycle.
/// </summary>
public sealed class StressTestController : IAsyncDisposable
{
    private static readonly TimeSpan CompletionSettleDelay = TimeSpan.FromMilliseconds(500);
    private readonly SemaphoreSlim _lifecycleLock = new(initialCount: 1, maxCount: 1);
    private CancellationTokenSource? _runCancellation;
    private StressTestPublisher? _publisher;
    private RawMqttStressSubscriber? _subscriber;
    private Task? _runTask;
    private StressTestMetrics _metrics = new();
    private StressTestRunSettings? _currentSettings;

    /// <summary>
    /// Occurs when the controller emits a log message.
    /// </summary>
    public event EventHandler<string>? LogMessage;

    /// <summary>
    /// Occurs when the running state changes.
    /// </summary>
    public event EventHandler<bool>? RunningChanged;

    /// <summary>
    /// Gets a value indicating whether a stress run is active.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts a new stress test run.
    /// </summary>
    /// <param name="settings">The validated run settings.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(StressTestRunSettings settings)
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A stress test is already running.");
            }

            _metrics = new StressTestMetrics();
            _currentSettings = settings;
            var runId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
            _runCancellation = new CancellationTokenSource();
            _subscriber = new RawMqttStressSubscriber(settings, _metrics, runId);
            await _subscriber.StartAsync(_runCancellation.Token).ConfigureAwait(false);

            var catalog = new RandomSignalCatalog(settings.StressRootPath, settings.SignalCount);
            _publisher = new StressTestPublisher(settings, catalog, _metrics, runId);
            _runTask = RunPublisherAsync(_publisher, _runCancellation.Token);
            IsRunning = true;
            LogMessage?.Invoke(this, $"Started stress test run {runId}.");
            RunningChanged?.Invoke(this, true);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Stops the active stress test run.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                return;
            }

            _runCancellation?.Cancel();
            if (_runTask is not null && !_runTask.IsCompleted)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    LogMessage?.Invoke(this, "Stress test cancellation completed.");
                }
            }

            var settings = _currentSettings;
            var snapshot = _metrics.CreateSnapshot();
            await DisposeRunObjectsAsync().ConfigureAwait(false);
            IsRunning = false;
            LogMessage?.Invoke(this, "Stopped stress test run.");
            if (settings is not null)
            {
                LogAssessment(settings, snapshot, completed: false);
            }

            RunningChanged?.Invoke(this, false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Resets metrics when no run is active.
    /// </summary>
    public void Reset()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Stop the stress test before resetting metrics.");
        }

        _metrics = new StressTestMetrics();
        LogMessage?.Invoke(this, "Reset stress test metrics.");
    }

    /// <summary>
    /// Creates a metric snapshot for UI rendering.
    /// </summary>
    /// <returns>The current metric snapshot.</returns>
    public StressTestMetricsSnapshot CreateSnapshot()
        => _metrics.CreateSnapshot();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }

    private async Task RunPublisherAsync(StressTestPublisher publisher, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.RunAsync(cancellationToken).ConfigureAwait(false);
            await FinishCompletedRunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke(this, "Stress test publisher stopped.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Stress test failed: {ex.Message}");
            await FinishCompletedRunAsync().ConfigureAwait(false);
        }
    }

    private async Task FinishCompletedRunAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                return;
            }

            await Task.Delay(CompletionSettleDelay).ConfigureAwait(false);
            var settings = _currentSettings;
            var snapshot = _metrics.CreateSnapshot();
            _runCancellation?.Cancel();
            await DisposeRunObjectsAsync().ConfigureAwait(false);
            IsRunning = false;
            LogMessage?.Invoke(this, "Stress test run completed.");
            if (settings is not null)
            {
                LogAssessment(settings, snapshot, completed: true);
            }

            RunningChanged?.Invoke(this, false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task DisposeRunObjectsAsync()
    {
        if (_publisher is not null)
        {
            await _publisher.DisposeAsync().ConfigureAwait(false);
            _publisher = null;
        }

        if (_subscriber is not null)
        {
            await _subscriber.DisposeAsync().ConfigureAwait(false);
            _subscriber = null;
        }

        _runCancellation?.Dispose();
        _runCancellation = null;
        _runTask = null;
        _currentSettings = null;
    }

    private void LogAssessment(
        StressTestRunSettings settings,
        StressTestMetricsSnapshot snapshot,
        bool completed)
    {
        var finalLost = snapshot.Pending;
        var runState = completed ? "completed" : "stopped";
        var deliveryStatus = finalLost > 0 || snapshot.Duplicates > 0 || snapshot.OutOfOrder > 0
            ? "FAIL"
            : "PASS";
        var latencyStatus = GetLatencyStatus(snapshot.P99LatencyMilliseconds);
        var achievedRatePercent = settings.MessagesPerSecond <= 0
            ? 0
            : snapshot.PublishRatePerSecond * 100.0 / settings.MessagesPerSecond;
        var throughputStatus = GetThroughputStatus(achievedRatePercent);
        var backlogThreshold = GetHighMaxPendingThreshold(settings);
        var backlogStatus = GetBacklogStatus(
            finalPending: snapshot.Pending,
            maxPending: snapshot.MaxPending,
            highMaxPendingThreshold: backlogThreshold);
        var overallStatus = GetOverallStatus(
            deliveryStatus: deliveryStatus,
            throughputStatus: throughputStatus,
            latencyStatus: latencyStatus,
            backlogStatus: backlogStatus);
        var averageUpdatesPerValue = settings.SignalCount <= 0
            ? 0
            : (double)settings.MessagesPerSecond / settings.SignalCount;

        LogMessage?.Invoke(
            this,
            $"Assessment overall: {overallStatus} - run {runState}, published {snapshot.Published:N0}, received {snapshot.Received:N0}, final lost {finalLost:N0}, final pending {snapshot.Pending:N0}, max pending {snapshot.MaxPending:N0}.");
        LogMessage?.Invoke(
            this,
            $"Assessment delivery: {deliveryStatus} - final lost {finalLost:N0}, duplicates {snapshot.Duplicates:N0}, out-of-order {snapshot.OutOfOrder:N0}.");
        LogMessage?.Invoke(
            this,
            $"Assessment throughput: {throughputStatus} - target total updates {settings.MessagesPerSecond:N0}/s, publish {snapshot.PublishRatePerSecond:N1}/s ({achievedRatePercent:N1}%), receive {snapshot.ReceiveRatePerSecond:N1}/s.");
        LogMessage?.Invoke(
            this,
            $"Assessment latency: {latencyStatus} - avg {snapshot.AverageLatencyMilliseconds:N2} ms, p95 {snapshot.P95LatencyMilliseconds:N2} ms, p99 {snapshot.P99LatencyMilliseconds:N2} ms, max {snapshot.MaxLatencyMilliseconds:N2} ms.");
        LogMessage?.Invoke(
            this,
            $"Assessment backlog: {backlogStatus} - final pending {snapshot.Pending:N0}, max pending {snapshot.MaxPending:N0}, high threshold {backlogThreshold:N0}.");
        LogMessage?.Invoke(
            this,
            $"Assessment load profile: signal values {settings.SignalCount:N0}, total updates {settings.MessagesPerSecond:N0}/s, avg updates per value {averageUpdatesPerValue:N1}/s.");
    }

    private static string GetThroughputStatus(double achievedRatePercent)
    {
        if (achievedRatePercent >= 99)
        {
            return "PASS";
        }

        if (achievedRatePercent >= 95)
        {
            return "WARN";
        }

        return "FAIL";
    }

    private static string GetLatencyStatus(double p99LatencyMilliseconds)
    {
        if (p99LatencyMilliseconds <= 10)
        {
            return "EXCELLENT";
        }

        if (p99LatencyMilliseconds <= 50)
        {
            return "GOOD";
        }

        if (p99LatencyMilliseconds <= 200)
        {
            return "ELEVATED";
        }

        return "HIGH";
    }

    private static long GetHighMaxPendingThreshold(StressTestRunSettings settings)
        => Math.Max(1, settings.MessagesPerSecond);

    private static string GetBacklogStatus(long finalPending, long maxPending, long highMaxPendingThreshold)
    {
        if (finalPending > 0)
        {
            return "FAIL";
        }

        if (maxPending > highMaxPendingThreshold)
        {
            return "WARN";
        }

        return "GOOD";
    }

    private static string GetOverallStatus(
        string deliveryStatus,
        string throughputStatus,
        string latencyStatus,
        string backlogStatus)
    {
        if (deliveryStatus == "FAIL" || throughputStatus == "FAIL" || backlogStatus == "FAIL")
        {
            return "FAIL";
        }

        if (throughputStatus == "WARN" || latencyStatus == "HIGH" || backlogStatus == "WARN")
        {
            return "WARN";
        }

        return "PASS";
    }
}
