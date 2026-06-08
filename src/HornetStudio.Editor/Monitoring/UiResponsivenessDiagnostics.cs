using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host.Registries;
using HornetStudio.Logging;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Provides opt-in UI responsiveness diagnostics and benchmark aggregation for the desktop UI.
/// </summary>
public static class UiResponsivenessDiagnostics
{
    private const string DiagnosticsEnvironmentVariableName = "HORNETSTUDIO_UI_DIAGNOSTICS";
    private const string BenchmarkEnvironmentVariableName = "HORNETSTUDIO_UI_BENCHMARK";
    private const string BenchmarkDurationSecondsEnvironmentVariableName = "HORNETSTUDIO_UI_BENCHMARK_SECONDS";
    private const string BenchmarkScenarioEnvironmentVariableName = "HORNETSTUDIO_UI_BENCHMARK_SCENARIO";
    private const string BenchmarkUdlDetailDiagnosticsEnvironmentVariableName = "HORNETSTUDIO_UI_BENCHMARK_UDL_DETAIL";
    private const string DiagnosticsCommandLineSwitch = "--ui-diagnostics";
    private const string BenchmarkCommandLineSwitch = "--ui-benchmark";
    private const int DefaultBenchmarkDurationSeconds = 60;
    private const int BenchmarkWarmupSeconds = 10;
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan[] ProbeThresholds =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250)
    ];

    private static readonly TimeSpan DialogTimingThreshold = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ChartRenderTimingThreshold = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan BrowserActivityFlushInterval = TimeSpan.FromSeconds(5);
    private const int BrowserRegistryTrackedEntryLimit = 32;
    private const int BrowserRegistryTopEntryLimit = 5;
    private const int SignalPipelineTrackedEntryLimit = 32;
    private const int SignalPipelineTopEntryLimit = 5;
    private const int BenchmarkCorrelationSnapshotLimit = 8;
    private static readonly TimeSpan MaxUiDelayBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxUiDelayFailBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan P95UiDelayBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan P95UiDelayFailBudget = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ChartRenderP95Budget = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan ChartRenderP95FailBudget = TimeSpan.FromMilliseconds(66);
    private static readonly TimeSpan BenchmarkSpikeSnapshotMinimumInterval = TimeSpan.FromSeconds(2);
    private static readonly object SyncLock = new();
    private static readonly object BrowserActivityLock = new();
    private static readonly object BenchmarkLock = new();
    private static readonly Dictionary<string, BrowserActivityCounters> BrowserActivityBySource = new(StringComparer.Ordinal);

    private static Timer? _probeTimer;
    private static Timer? _benchmarkTimer;
    private static UiBenchmarkConfiguration _benchmarkConfiguration = UiBenchmarkConfiguration.Disabled;
    private static UiBenchmarkSession? _benchmarkSession;
    private static long _probeSequence;
    private static long _queuedProbeSequence;
    private static long _queuedProbeTicksUtc;
    private static int _isEnabled;
    private static int _isStarted;
    private static int _probePostQueued;
    private static int _captureBenchmarkUdlDetailDiagnostics;
    private static int _activeTargetRefreshQueuedCount;
    private static int _activeTargetRefreshFollowUpCount;
    private static int _activeTargetValueRefreshQueuedCount;
    private static int _activeUnresolvedSignalTargetCount;
    private static DateTimeOffset _lastBrowserActivityFlushUtc = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets a value indicating whether UI responsiveness diagnostics are enabled.
    /// </summary>
    public static bool IsEnabled => Volatile.Read(ref _isEnabled) == 1;

    /// <summary>
    /// Gets a value indicating whether benchmark aggregation is enabled.
    /// </summary>
    public static bool IsBenchmarkEnabled => _benchmarkConfiguration.Enabled;

    /// <summary>
    /// Initializes diagnostics and benchmark configuration from environment variables and command line arguments.
    /// </summary>
    /// <param name="args">Startup arguments passed to the application.</param>
    public static void InitializeFromEnvironment(string[]? args)
    {
        _benchmarkConfiguration = GetBenchmarkConfiguration(args);
        var captureBenchmarkUdlDetailDiagnostics = IsTruthyEnvironmentVariable(BenchmarkUdlDetailDiagnosticsEnvironmentVariableName);
        Volatile.Write(ref _captureBenchmarkUdlDetailDiagnostics, captureBenchmarkUdlDetailDiagnostics ? 1 : 0);

        var diagnosticsEnabled = IsEnabledByConfiguration(args) || _benchmarkConfiguration.Enabled;
        Volatile.Write(ref _isEnabled, diagnosticsEnabled ? 1 : 0);
        DataRegistryDiagnosticsHooks.PublicDataPublished = diagnosticsEnabled
            ? static publication => RecordPublicRegistryPublish(publication)
            : null;
        DataRegistryDiagnosticsHooks.PublicDataPublishCompleted = diagnosticsEnabled
            ? static publication => RecordPublicRegistryPublishTiming(publication)
            : null;

        HostLogger.Log.Information(
            "UI responsiveness diagnostics configured. Enabled={Enabled} BenchmarkEnabled={BenchmarkEnabled} EnvironmentVariable={EnvironmentVariable} BenchmarkEnvironmentVariable={BenchmarkEnvironmentVariable} BenchmarkScenario={BenchmarkScenario} BenchmarkDurationSeconds={BenchmarkDurationSeconds} BenchmarkUdlDetailDiagnosticsEnabled={BenchmarkUdlDetailDiagnosticsEnabled} BenchmarkUdlDetailDiagnosticsEnvironmentVariable={BenchmarkUdlDetailDiagnosticsEnvironmentVariable}",
            diagnosticsEnabled,
            _benchmarkConfiguration.Enabled,
            DiagnosticsEnvironmentVariableName,
            BenchmarkEnvironmentVariableName,
            _benchmarkConfiguration.Scenario,
            _benchmarkConfiguration.Duration.TotalSeconds,
            captureBenchmarkUdlDetailDiagnostics,
            BenchmarkUdlDetailDiagnosticsEnvironmentVariableName);
    }

    /// <summary>
    /// Starts the background UI probe and the optional benchmark session.
    /// </summary>
    public static void StartProbe()
    {
        if (!IsEnabled || Interlocked.Exchange(ref _isStarted, 1) != 0)
        {
            return;
        }

        lock (SyncLock)
        {
            if (_probeTimer is not null)
            {
                return;
            }

            _probeTimer = new Timer(
                callback: static _ => OnProbeTimerElapsed(),
                state: null,
                dueTime: ProbeInterval,
                period: ProbeInterval);
        }

        StartBenchmarkSessionIfNeeded();

        HostLogger.Log.Information(
            "UI responsiveness probe started. IntervalMs={IntervalMs}",
            ProbeInterval.TotalMilliseconds);
    }

    /// <summary>
    /// Stops the UI probe and flushes any active benchmark session.
    /// </summary>
    public static void StopProbe()
    {
        if (Interlocked.Exchange(ref _isStarted, 0) == 0)
        {
            return;
        }

        lock (SyncLock)
        {
            if (_probeTimer is not null)
            {
                _probeTimer.Dispose();
                _probeTimer = null;
            }
        }

        StopBenchmarkSession(summaryReason: "Shutdown");

        HostLogger.Log.Information("UI responsiveness probe stopped.");
    }

    /// <summary>
    /// Measures dialog open lifetime for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="owner">Owning window.</param>
    /// <param name="dialogName">Dialog identifier.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackDialogOpen(Window? owner, string dialogName)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: "DialogOpen",
            name: dialogName,
            threshold: DialogTimingThreshold,
            owner: owner,
            stateFactory: static () => null);
    }

    /// <summary>
    /// Measures browser dialog opening lifetime for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="owner">Owning window.</param>
    /// <param name="dialogName">Dialog identifier.</param>
    /// <param name="folderName">Selected folder name.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackBrowserDialogOpen(Window? owner, string dialogName, string folderName)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: "BrowserDialogOpen",
            name: dialogName,
            threshold: DialogTimingThreshold,
            owner: owner,
            stateFactory: () => new Dictionary<string, object?>
            {
                ["Folder"] = folderName
            });
    }

    /// <summary>
    /// Measures browser operations for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="owner">Owning window.</param>
    /// <param name="source">Control or dialog source.</param>
    /// <param name="operationName">Operation name.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackBrowserOperation(Window? owner, string source, string operationName)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: "BrowserOperation",
            name: $"{source}.{operationName}",
            threshold: DialogTimingThreshold,
            owner: owner,
            stateFactory: static () => null);
    }

    /// <summary>
    /// Measures chart rendering for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="owner">Owning window.</param>
    /// <param name="chartName">Chart identifier.</param>
    /// <param name="seriesCount">Rendered series count.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackChartRender(Window? owner, string chartName, int seriesCount)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: "RealtimeChartRender",
            name: chartName,
            threshold: ChartRenderTimingThreshold,
            owner: owner,
            stateFactory: () => new Dictionary<string, object?>
            {
                ["SeriesCount"] = seriesCount
            });
    }

    /// <summary>
    /// Measures recurring steady-state operations after the startup warmup window.
    /// </summary>
    /// <param name="owner">Owning window when available.</param>
    /// <param name="category">Steady-state timing category.</param>
    /// <param name="name">Operation name.</param>
    /// <param name="threshold">Threshold for per-event diagnostic logging.</param>
    /// <param name="stateFactory">Optional contextual state provider.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackSteadyStateOperation(
        Window? owner,
        string category,
        string name,
        TimeSpan threshold,
        Func<IReadOnlyDictionary<string, object?>?>? stateFactory = null)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        if (!ShouldCaptureSteadyStateTiming())
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: category,
            name: name,
            threshold: threshold,
            owner: owner,
            stateFactory: stateFactory ?? (() => null));
    }

    /// <summary>
    /// Measures startup-related phases for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="owner">Owning window when available.</param>
    /// <param name="source">Phase source name.</param>
    /// <param name="phaseName">Startup phase name.</param>
    /// <param name="stateFactory">Optional contextual state provider.</param>
    /// <returns>A disposable timing scope.</returns>
    public static IDisposable TrackStartupPhase(
        Window? owner,
        string source,
        string phaseName,
        Func<IReadOnlyDictionary<string, object?>?>? stateFactory = null)
    {
        if (!IsEnabled)
        {
            return NoopScope.Instance;
        }

        return new TimingScope(
            category: "StartupPhase",
            name: $"{source}.{phaseName}",
            threshold: TimeSpan.Zero,
            owner: owner,
            stateFactory: stateFactory ?? (() => null),
            alwaysLog: true);
    }

    /// <summary>
    /// Records a browser registry event for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="source">Control source name.</param>
    /// <param name="key">Registry key.</param>
    /// <param name="accepted">True when the event was relevant for the control.</param>
    public static void RecordBrowserRegistryEvent(string source, string? key, bool accepted)
    {
        if (!IsEnabled)
        {
            return;
        }

        RecordBenchmarkBrowserRegistryEvent(source: source, key: key, accepted: accepted);

        lock (BrowserActivityLock)
        {
            var counters = GetBrowserActivityCountersLocked(source);
            counters.RecordRegistryEvent(key: key, accepted: accepted);
            FlushBrowserActivityIfDueLocked(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Records a browser dispatcher post for diagnostics and benchmark summaries.
    /// </summary>
    /// <param name="source">Control source name.</param>
    /// <param name="reason">Dispatcher post reason.</param>
    public static void RecordBrowserDispatcherPost(string source, string reason)
    {
        if (!IsEnabled)
        {
            return;
        }

        RecordBenchmarkBrowserDispatcherPost(source, reason);

        lock (BrowserActivityLock)
        {
            var counters = GetBrowserActivityCountersLocked(source);
            counters.DispatcherPosts++;
            counters.LastDispatcherReason = reason;
            FlushBrowserActivityIfDueLocked(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Records a high-frequency signal pipeline event for benchmark summaries.
    /// </summary>
    /// <param name="stage">Pipeline stage name.</param>
    /// <param name="path">Associated registry or target path when available.</param>
    /// <param name="module">Associated module or item name when available.</param>
    /// <param name="queuedAtUtc">Optional queue timestamp used for phase-aware grouping.</param>
    /// <param name="executedAtUtc">Optional execution timestamp used for phase-aware grouping.</param>
    public static void RecordSignalPipelineEvent(
        string stage,
        string? path = null,
        string? module = null,
        DateTimeOffset? queuedAtUtc = null,
        DateTimeOffset? executedAtUtc = null)
    {
        if (!ShouldCaptureSignalPipelineStage(stage))
        {
            return;
        }

        RecordBenchmarkSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module,
            delay: null,
            queuedAtUtc: queuedAtUtc,
            executedAtUtc: executedAtUtc);
    }

    /// <summary>
    /// Records a high-frequency signal pipeline event with a measured delay for benchmark summaries.
    /// </summary>
    /// <param name="stage">Pipeline stage name.</param>
    /// <param name="delay">Measured delay.</param>
    /// <param name="path">Associated registry or target path when available.</param>
    /// <param name="module">Associated module or item name when available.</param>
    /// <param name="queuedAtUtc">Optional queue timestamp used for phase-aware grouping.</param>
    /// <param name="executedAtUtc">Optional execution timestamp used for phase-aware grouping.</param>
    public static void RecordSignalPipelineDelay(
        string stage,
        TimeSpan delay,
        string? path = null,
        string? module = null,
        DateTimeOffset? queuedAtUtc = null,
        DateTimeOffset? executedAtUtc = null)
    {
        if (!ShouldCaptureSignalPipelineStage(stage))
        {
            return;
        }

        RecordBenchmarkSignalPipelineEvent(
            stage: stage,
            path: path,
            module: module,
            delay: delay,
            queuedAtUtc: queuedAtUtc,
            executedAtUtc: executedAtUtc);
    }

    /// <summary>
    /// Adjusts bounded live signal refresh backlog counters used by rare benchmark correlation snapshots.
    /// </summary>
    /// <param name="targetRefreshQueuedDelta">Delta for active broad target refresh queue entries.</param>
    /// <param name="targetRefreshFollowUpDelta">Delta for active broad target refresh follow-up entries.</param>
    /// <param name="targetValueRefreshQueuedDelta">Delta for active value-only target refresh queue entries.</param>
    /// <param name="unresolvedTargetGateDelta">Delta for active unresolved target gate entries.</param>
    public static void AdjustSignalRefreshBacklog(
        int targetRefreshQueuedDelta = 0,
        int targetRefreshFollowUpDelta = 0,
        int targetValueRefreshQueuedDelta = 0,
        int unresolvedTargetGateDelta = 0)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (targetRefreshQueuedDelta != 0)
        {
            AddNonNegative(ref _activeTargetRefreshQueuedCount, targetRefreshQueuedDelta);
        }

        if (targetRefreshFollowUpDelta != 0)
        {
            AddNonNegative(ref _activeTargetRefreshFollowUpCount, targetRefreshFollowUpDelta);
        }

        if (targetValueRefreshQueuedDelta != 0)
        {
            AddNonNegative(ref _activeTargetValueRefreshQueuedCount, targetValueRefreshQueuedDelta);
        }

        if (unresolvedTargetGateDelta != 0)
        {
            AddNonNegative(ref _activeUnresolvedSignalTargetCount, unresolvedTargetGateDelta);
        }
    }

    private static UiBenchmarkConfiguration GetBenchmarkConfiguration(string[]? args)
    {
        var enabled = ContainsCommandLineSwitch(args, BenchmarkCommandLineSwitch)
            || IsTruthyEnvironmentVariable(BenchmarkEnvironmentVariableName);

        if (!enabled)
        {
            return UiBenchmarkConfiguration.Disabled;
        }

        var durationSeconds = GetPositiveIntEnvironmentVariable(
            name: BenchmarkDurationSecondsEnvironmentVariableName,
            fallbackValue: DefaultBenchmarkDurationSeconds);
        var scenario = Environment.GetEnvironmentVariable(BenchmarkScenarioEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(scenario))
        {
            scenario = "Manual";
        }

        return new UiBenchmarkConfiguration(
            Enabled: true,
            Scenario: scenario.Trim(),
            Duration: TimeSpan.FromSeconds(durationSeconds));
    }

    private static bool IsEnabledByConfiguration(string[]? args)
    {
        if (ContainsCommandLineSwitch(args, DiagnosticsCommandLineSwitch))
        {
            return true;
        }

        return IsTruthyEnvironmentVariable(DiagnosticsEnvironmentVariableName);
    }

    private static bool ContainsCommandLineSwitch(string[]? args, string value)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        return args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTruthyEnvironmentVariable(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPositiveIntEnvironmentVariable(string name, int fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallbackValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallbackValue;
    }

    private static void StartBenchmarkSessionIfNeeded()
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        lock (BenchmarkLock)
        {
            if (_benchmarkSession is not null)
            {
                return;
            }

            _benchmarkSession = new UiBenchmarkSession(
                scenario: _benchmarkConfiguration.Scenario,
                configuredDuration: _benchmarkConfiguration.Duration,
                warmupDuration: TimeSpan.FromSeconds(BenchmarkWarmupSeconds));

            _benchmarkTimer?.Dispose();
            _benchmarkTimer = new Timer(
                callback: static _ => OnBenchmarkDurationElapsed(),
                state: null,
                dueTime: _benchmarkConfiguration.Duration,
                period: Timeout.InfiniteTimeSpan);
        }

        HostLogger.Log.Information(
            "UI benchmark enabled. Scenario={Scenario} DurationSeconds={DurationSeconds} WarmupSeconds={WarmupSeconds}",
            _benchmarkConfiguration.Scenario,
            _benchmarkConfiguration.Duration.TotalSeconds,
            BenchmarkWarmupSeconds);
    }

    private static void StopBenchmarkSession(string summaryReason)
    {
        UiBenchmarkSummary? summary = null;

        lock (BenchmarkLock)
        {
            _benchmarkTimer?.Dispose();
            _benchmarkTimer = null;

            if (_benchmarkSession is null)
            {
                return;
            }

            _benchmarkSession.Complete(DateTimeOffset.UtcNow);
            summary = _benchmarkSession.CreateSummary(reason: summaryReason, generatedAtUtc: DateTimeOffset.UtcNow);
            _benchmarkSession = null;
        }

        if (summary is not null)
        {
            LogBenchmarkSummary(summary.Value);
        }
    }

    private static void OnBenchmarkDurationElapsed()
    {
        UiBenchmarkSummary? summary = null;

        lock (BenchmarkLock)
        {
            _benchmarkTimer?.Dispose();
            _benchmarkTimer = null;

            if (_benchmarkSession is null)
            {
                return;
            }

            _benchmarkSession.Complete(DateTimeOffset.UtcNow);
            summary = _benchmarkSession.CreateSummary(reason: "DurationElapsed", generatedAtUtc: DateTimeOffset.UtcNow);
        }

        if (summary is not null)
        {
            LogBenchmarkSummary(summary.Value);
        }
    }

    private static void RecordBenchmarkUiDelay(TimeSpan delay, DateTimeOffset observedAtUtc)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        var backlogSnapshot = CaptureSignalRefreshBacklogSnapshot();
        BenchmarkCorrelationSnapshot? correlationSnapshot = null;
        var scenario = string.Empty;
        lock (BenchmarkLock)
        {
            if (_benchmarkSession is null)
            {
                return;
            }

            scenario = _benchmarkSession.Scenario;
            correlationSnapshot = _benchmarkSession.RecordUiDelay(
                delay: delay,
                observedAtUtc: observedAtUtc,
                backlogSnapshot: backlogSnapshot);
        }

        if (correlationSnapshot.HasValue)
        {
            LogBenchmarkCorrelationSnapshot(
                scenario: scenario,
                snapshot: correlationSnapshot.Value);
        }
    }

    private static void RecordBenchmarkTiming(string category, string name, TimeSpan elapsed)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        lock (BenchmarkLock)
        {
            _benchmarkSession?.RecordTiming(category: category, name: name, elapsed: elapsed);
        }
    }

    private static void RecordBenchmarkBrowserRegistryEvent(string source, string? key, bool accepted)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        lock (BenchmarkLock)
        {
            _benchmarkSession?.RecordBrowserRegistryEvent(source: source, key: key, accepted: accepted);
        }
    }

    private static void RecordBenchmarkBrowserDispatcherPost(string source, string reason)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        lock (BenchmarkLock)
        {
            _benchmarkSession?.RecordBrowserDispatcherPost(source, reason);
        }
    }

    private static void RecordBenchmarkSignalPipelineEvent(
        string stage,
        string? path,
        string? module,
        TimeSpan? delay,
        DateTimeOffset? queuedAtUtc,
        DateTimeOffset? executedAtUtc)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        lock (BenchmarkLock)
        {
            _benchmarkSession?.RecordSignalPipelineEvent(
                stage: stage,
                path: path,
                module: module,
                delay: delay,
                queuedAtUtc: queuedAtUtc,
                executedAtUtc: executedAtUtc);
        }
    }

    private static void RecordPublicRegistryPublish(DataRegistryPublicationEvent publication)
    {
        if (!IsEnabled)
        {
            return;
        }

        var path = NormalizeRegistryKey(publication.Key);
        RecordBenchmarkSignalPipelineEvent(
            stage: "RegistryPublish",
            path: path,
            module: NormalizeSignalModule(GetLastPathSegment(path)),
            delay: null,
            queuedAtUtc: null,
            executedAtUtc: DateTimeOffset.UtcNow);
    }

    private static void RecordPublicRegistryPublishTiming(DataRegistryPublicationTimingEvent publication)
    {
        if (!IsEnabled)
        {
            return;
        }

        RecordBenchmarkTiming(
            category: "RegistryPublishDuration",
            name: publication.ChangeKind switch
            {
                DataChangeKind.ValueUpdated => "ValueUpdated",
                DataChangeKind.PropertyUpdated => string.IsNullOrWhiteSpace(publication.ParameterName)
                    ? "PropertyUpdated"
                    : $"PropertyUpdated.{publication.ParameterName}",
                _ => "SnapshotUpserted"
            },
            elapsed: publication.Elapsed);
    }

    private static bool ShouldCaptureSteadyStateTiming()
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return true;
        }

        lock (BenchmarkLock)
        {
            if (_benchmarkSession is null)
            {
                return false;
            }

            return DateTimeOffset.UtcNow - _benchmarkSession.StartedUtc >= _benchmarkSession.WarmupDuration;
        }
    }

    private static bool ShouldCaptureSignalPipelineStage(string? stage)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(stage))
        {
            return false;
        }

        if (!_benchmarkConfiguration.Enabled)
        {
            return true;
        }

        if (Volatile.Read(ref _captureBenchmarkUdlDetailDiagnostics) == 1)
        {
            return true;
        }

        return !IsBenchmarkFilteredUdlDetailStage(stage);
    }

    private static bool IsBenchmarkFilteredUdlDetailStage(string stage)
    {
        return stage.Trim() switch
        {
            "DemoSetRead" => true,
            "DemoSetOut" => true,
            "DemoSetSet" => true,
            "DemoSetState" => true,
            "DemoSetAlert" => true,
            "DemoApplyModuleSnapshot" => true,
            "DemoComputeTargetValue" => true,
            "DemoFrameReceived" => true,
            "DemoLoopUpdateModules" => true,
            "DemoLoopTotal" => true,
            "UdlRuntimeFrameReceived" => true,
            "UdlProjectionFrameReceived" => true,
            "UdlSynchronizeBitValues" => true,
            _ => false
        };
    }

    private static void OnProbeTimerElapsed()
    {
        if (!IsEnabled || Volatile.Read(ref _isStarted) == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _probePostQueued, 1, 0) != 0)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _probeSequence);
        var scheduledAtUtc = DateTimeOffset.UtcNow.UtcTicks;
        Volatile.Write(ref _queuedProbeSequence, sequence);
        Volatile.Write(ref _queuedProbeTicksUtc, scheduledAtUtc);

        Dispatcher.UIThread.Post(
            action: static () => FlushQueuedProbeResult(),
            priority: DispatcherPriority.Background);
    }

    private static void FlushQueuedProbeResult()
    {
        var sequence = Volatile.Read(ref _queuedProbeSequence);
        var scheduledAtUtcTicks = Volatile.Read(ref _queuedProbeTicksUtc);
        Interlocked.Exchange(ref _probePostQueued, 0);
        LogProbeResult(sequence: sequence, scheduledAtUtc: new DateTimeOffset(scheduledAtUtcTicks, TimeSpan.Zero));
    }

    private static void LogProbeResult(long sequence, DateTimeOffset scheduledAtUtc)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        var delay = observedAtUtc - scheduledAtUtc;
        RecordBenchmarkUiDelay(delay: delay, observedAtUtc: observedAtUtc);

        var threshold = GetTriggeredThreshold(delay);
        if (threshold is null)
        {
            return;
        }

        var context = CollectUiContext(owner: null);
        TryCaptureBenchmarkSpikeSnapshot(
            delay: delay,
            threshold: threshold.Value,
            sequence: sequence,
            observedAtUtc: observedAtUtc,
            context: context);
        HostLogger.Log.Warning(
            "UI responsiveness spike. DelayMs={DelayMs} ThresholdMs={ThresholdMs} IntervalMs={IntervalMs} Sequence={Sequence} ThreadId={ThreadId} TimestampUtc={TimestampUtc} ActiveWindow={ActiveWindow} WindowCount={WindowCount} OpenDialogs={OpenDialogs} OpenDialogNames={OpenDialogNames}",
            Math.Round(delay.TotalMilliseconds, 1),
            threshold.Value.TotalMilliseconds,
            ProbeInterval.TotalMilliseconds,
            sequence,
            Environment.CurrentManagedThreadId,
            observedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            context.ActiveWindow,
            context.WindowCount,
            context.OpenDialogs,
            context.OpenDialogNames);
    }

    private static void TryCaptureBenchmarkSpikeSnapshot(
        TimeSpan delay,
        TimeSpan threshold,
        long sequence,
        DateTimeOffset observedAtUtc,
        UiContextSnapshot context)
    {
        if (!_benchmarkConfiguration.Enabled)
        {
            return;
        }

        var backlogSnapshot = CaptureSignalRefreshBacklogSnapshot();
        BenchmarkCorrelationSnapshot? correlationSnapshot = null;
        var scenario = string.Empty;
        lock (BenchmarkLock)
        {
            if (_benchmarkSession is null)
            {
                return;
            }

            scenario = _benchmarkSession.Scenario;
            correlationSnapshot = _benchmarkSession.CaptureUiSpikeSnapshot(
                observedAtUtc: observedAtUtc,
                delay: delay,
                threshold: threshold,
                sequence: sequence,
                context: context,
                backlogSnapshot: backlogSnapshot);
        }

        if (correlationSnapshot.HasValue)
        {
            LogBenchmarkCorrelationSnapshot(
                scenario: scenario,
                snapshot: correlationSnapshot.Value);
        }
    }

    private static TimeSpan? GetTriggeredThreshold(TimeSpan elapsed)
    {
        for (var index = ProbeThresholds.Length - 1; index >= 0; index--)
        {
            var threshold = ProbeThresholds[index];
            if (elapsed >= threshold)
            {
                return threshold;
            }
        }

        return null;
    }

    private static BrowserActivityCounters GetBrowserActivityCountersLocked(string source)
    {
        if (!BrowserActivityBySource.TryGetValue(source, out var counters))
        {
            counters = new BrowserActivityCounters();
            BrowserActivityBySource[source] = counters;
        }

        return counters;
    }

    private static void FlushBrowserActivityIfDueLocked(DateTimeOffset now)
    {
        if (now - _lastBrowserActivityFlushUtc < BrowserActivityFlushInterval)
        {
            return;
        }

        _lastBrowserActivityFlushUtc = now;
        foreach (var entry in BrowserActivityBySource)
        {
            var counters = entry.Value;
            if (counters.RegistryEvents == 0 && counters.DispatcherPosts == 0)
            {
                continue;
            }

            HostLogger.Log.Information(
                "Browser UI diagnostics. Source={Source} RegistryEvents={RegistryEvents} RegistryEventsPerSecond={RegistryEventsPerSecond} AcceptedRegistryEvents={AcceptedRegistryEvents} IgnoredRegistryEvents={IgnoredRegistryEvents} IgnoredRatio={IgnoredRatio} DispatcherPosts={DispatcherPosts} TopRegistryPrefixes={TopRegistryPrefixes} TopRegistryKeys={TopRegistryKeys} LastRegistryKey={LastRegistryKey} LastDispatcherReason={LastDispatcherReason}",
                entry.Key,
                counters.RegistryEvents,
                counters.RegistryEventsPerSecond,
                counters.AcceptedRegistryEvents,
                counters.IgnoredRegistryEvents,
                counters.IgnoredRatio,
                counters.DispatcherPosts,
                counters.TopPrefixSummary,
                counters.TopKeySummary,
                counters.LastRegistryKey,
                counters.LastDispatcherReason);

            counters.Reset();
        }
    }

    private static UiContextSnapshot CollectUiContext(Window? owner)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return new UiContextSnapshot(
                ActiveWindow: string.Empty,
                WindowCount: 0,
                OpenDialogs: 0,
                OpenDialogNames: string.Empty);
        }

        var application = Application.Current;
        if (application?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return new UiContextSnapshot(
                ActiveWindow: owner?.Title ?? string.Empty,
                WindowCount: 0,
                OpenDialogs: 0,
                OpenDialogNames: string.Empty);
        }

        var windows = desktop.Windows;
        var activeWindow = owner ?? desktop.MainWindow ?? windows.FirstOrDefault(static window => window.IsActive);
        var dialogTitles = windows
            .Where(static window => window.Owner is not null)
            .Select(static window => string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new UiContextSnapshot(
            ActiveWindow: activeWindow is null
                ? string.Empty
                : string.IsNullOrWhiteSpace(activeWindow.Title) ? activeWindow.GetType().Name : activeWindow.Title,
            WindowCount: windows.Count,
            OpenDialogs: dialogTitles.Length,
            OpenDialogNames: string.Join(", ", dialogTitles));
    }

    private static void LogBenchmarkSummary(UiBenchmarkSummary summary)
    {
        HostLogger.Log.Information(
            "UI benchmark summary. Scenario={Scenario} Reason={Reason} Status={Status} BudgetNotes={BudgetNotes} DurationSeconds={DurationSeconds} WarmupSeconds={WarmupSeconds} MaxUiDelayMs={MaxUiDelayMs} P95UiDelayMs={P95UiDelayMs} BudgetMaxUiDelayMs={BudgetMaxUiDelayMs} BudgetP95UiDelayMs={BudgetP95UiDelayMs} Spike50Ms={Spike50Ms} Spike100Ms={Spike100Ms} Spike250Ms={Spike250Ms} BrowserDispatcherPosts={BrowserDispatcherPosts} ChartRenderCount={ChartRenderCount} ChartRenderMaxMs={ChartRenderMaxMs} ChartRenderP95Ms={ChartRenderP95Ms}",
            summary.Scenario,
            summary.Reason,
            summary.Status,
            summary.BudgetNotes,
            Math.Round(summary.Duration.TotalSeconds, 1),
            Math.Round(summary.WarmupDuration.TotalSeconds, 1),
            summary.UiDelay.MaxMs,
            summary.UiDelay.P95Ms,
            summary.UiDelayAfterWarmup.MaxMs,
            summary.UiDelayAfterWarmup.P95Ms,
            summary.Spike50Count,
            summary.Spike100Count,
            summary.Spike250Count,
            summary.TotalBrowserDispatcherPosts,
            summary.ChartRender.Count,
            summary.ChartRender.MaxMs,
            summary.ChartRender.P95Ms);

        foreach (var browser in summary.BrowserSummaries.OrderByDescending(static item => item.RegistryEvents).ThenBy(static item => item.Source, StringComparer.Ordinal))
        {
            HostLogger.Log.Information(
                "UI benchmark browser summary. Scenario={Scenario} Source={Source} RegistryEvents={RegistryEvents} RegistryEventsPerSecond={RegistryEventsPerSecond} AcceptedRegistryEvents={AcceptedRegistryEvents} IgnoredRegistryEvents={IgnoredRegistryEvents} IgnoredRatio={IgnoredRatio} DispatcherPosts={DispatcherPosts} TopRegistryPrefixes={TopRegistryPrefixes} TopRegistryKeys={TopRegistryKeys} LastRegistryKey={LastRegistryKey} LastDispatcherReason={LastDispatcherReason}",
                summary.Scenario,
                browser.Source,
                browser.RegistryEvents,
                browser.RegistryEventsPerSecond,
                browser.AcceptedRegistryEvents,
                browser.IgnoredRegistryEvents,
                browser.IgnoredRatio,
                browser.DispatcherPosts,
                browser.TopPrefixSummary,
                browser.TopKeySummary,
                browser.LastRegistryKey,
                browser.LastDispatcherReason);
        }

        foreach (var stage in summary.SignalPipelineSummaries.OrderByDescending(static item => item.Count).ThenBy(static item => item.Stage, StringComparer.Ordinal))
        {
            HostLogger.Log.Information(
                "UI benchmark signal pipeline summary. Scenario={Scenario} Stage={Stage} Count={Count} EventsPerSecond={EventsPerSecond} DelayCount={DelayCount} DelayMaxMs={DelayMaxMs} DelayP95Ms={DelayP95Ms} TopPaths={TopPaths} TopModules={TopModules} LastPath={LastPath} LastModule={LastModule}",
                summary.Scenario,
                stage.Stage,
                stage.Count,
                stage.EventsPerSecond,
                stage.DelayMetrics.Count,
                stage.DelayMetrics.MaxMs,
                stage.DelayMetrics.P95Ms,
                stage.TopPathSummary,
                stage.TopModuleSummary,
                stage.LastPath,
                stage.LastModule);
        }

        foreach (var snapshot in summary.CorrelationSnapshots.OrderBy(static item => item.OffsetSeconds).ThenBy(static item => item.Event, StringComparer.Ordinal))
        {
            LogBenchmarkCorrelationSnapshot(
                scenario: summary.Scenario,
                snapshot: snapshot);
        }

        foreach (var chart in summary.ChartSummaries.OrderByDescending(static item => item.Metrics.P95Ms).ThenBy(static item => item.Name, StringComparer.Ordinal))
        {
            HostLogger.Log.Information(
                "UI benchmark chart summary. Scenario={Scenario} Name={Name} Count={Count} MaxMs={MaxMs} P95Ms={P95Ms}",
                summary.Scenario,
                chart.Name,
                chart.Metrics.Count,
                chart.Metrics.MaxMs,
                chart.Metrics.P95Ms);
        }

        foreach (var timing in summary.TimingSummaries.OrderByDescending(static item => item.Metrics.P95Ms).ThenBy(static item => item.Category, StringComparer.Ordinal).ThenBy(static item => item.Name, StringComparer.Ordinal))
        {
            HostLogger.Log.Information(
                "UI benchmark timing summary. Scenario={Scenario} Category={Category} Name={Name} Count={Count} MaxMs={MaxMs} P95Ms={P95Ms}",
                summary.Scenario,
                timing.Category,
                timing.Name,
                timing.Metrics.Count,
                timing.Metrics.MaxMs,
                timing.Metrics.P95Ms);
        }
    }

    private static BenchmarkStatus EvaluateStatus(
        MetricSummary uiDelayAfterWarmup,
        MetricSummary chartRender,
        BrowserBenchmarkSummary[] browserSummaries,
        int totalBrowserDispatcherPosts,
        out string budgetNotes)
    {
        var notes = new List<string>();
        var status = BenchmarkStatus.Pass;

        if (uiDelayAfterWarmup.Count > 0)
        {
            EvaluateThreshold(
                value: uiDelayAfterWarmup.MaxMs,
                warnThreshold: MaxUiDelayBudget.TotalMilliseconds,
                failThreshold: MaxUiDelayFailBudget.TotalMilliseconds,
                warnNote: "MaxUiDelayMs exceeded soft budget",
                failNote: "MaxUiDelayMs exceeded fail budget",
                notes: notes,
                status: ref status);

            EvaluateThreshold(
                value: uiDelayAfterWarmup.P95Ms,
                warnThreshold: P95UiDelayBudget.TotalMilliseconds,
                failThreshold: P95UiDelayFailBudget.TotalMilliseconds,
                warnNote: "P95UiDelayMs exceeded soft budget",
                failNote: "P95UiDelayMs exceeded fail budget",
                notes: notes,
                status: ref status);
        }
        else
        {
            notes.Add("Warmup window covered the full run; UI budgets not evaluated");
        }

        if (chartRender.Count > 0)
        {
            EvaluateThreshold(
                value: chartRender.P95Ms,
                warnThreshold: ChartRenderP95Budget.TotalMilliseconds,
                failThreshold: ChartRenderP95FailBudget.TotalMilliseconds,
                warnNote: "ChartRenderP95Ms exceeded soft budget",
                failNote: "ChartRenderP95Ms exceeded fail budget",
                notes: notes,
                status: ref status);
        }

        foreach (var browser in browserSummaries)
        {
            EvaluateThreshold(
                value: browser.RegistryEventsPerSecond,
                warnThreshold: 50d,
                failThreshold: 150d,
                warnNote: $"{browser.Source} registry event rate exceeded soft budget",
                failNote: $"{browser.Source} registry event rate exceeded fail budget",
                notes: notes,
                status: ref status);

            if (browser.RegistryEvents >= 100)
            {
                EvaluateThreshold(
                    value: browser.IgnoredRatio,
                    warnThreshold: 0.5d,
                    failThreshold: 0.8d,
                    warnNote: $"{browser.Source} ignored registry ratio remained high",
                    failNote: $"{browser.Source} ignored registry ratio remained very high",
                    notes: notes,
                    status: ref status);
            }
        }

        EvaluateThreshold(
            value: totalBrowserDispatcherPosts,
            warnThreshold: 25,
            failThreshold: 100,
            warnNote: "BrowserDispatcherPosts were elevated",
            failNote: "BrowserDispatcherPosts were high",
            notes: notes,
            status: ref status);

        budgetNotes = notes.Count == 0 ? "Within soft budgets" : string.Join("; ", notes);
        return status;
    }

    private static void EvaluateThreshold(
        double value,
        double warnThreshold,
        double failThreshold,
        string warnNote,
        string failNote,
        List<string> notes,
        ref BenchmarkStatus status)
    {
        if (value > failThreshold)
        {
            notes.Add(failNote);
            status = BenchmarkStatus.Fail;
            return;
        }

        if (value > warnThreshold && status != BenchmarkStatus.Fail)
        {
            notes.Add(warnNote);
            status = BenchmarkStatus.Warn;
        }
    }

    private static MetricSummary CreateMetricSummary(IEnumerable<double> values)
    {
        var samples = values.OrderBy(static value => value).ToArray();
        if (samples.Length == 0)
        {
            return MetricSummary.Empty;
        }

        var max = samples[^1];
        var p95 = Percentile(samples, 0.95d);
        return new MetricSummary(
            Count: samples.Length,
            MaxMs: RoundMilliseconds(max),
            P95Ms: RoundMilliseconds(p95));
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0d;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var clampedPercentile = Math.Clamp(percentile, 0d, 1d);
        var index = clampedPercentile * (sortedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = index - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    private static double RoundMilliseconds(double value)
    {
        return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private static double RoundRatio(double value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeRegistryKey(string? key)
    {
        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(key);
        return string.IsNullOrWhiteSpace(normalized) ? "<empty>" : normalized;
    }

    private static string NormalizeRegistryDomainPrefix(string? key)
    {
        var normalized = NormalizeRegistryKey(key);
        if (string.Equals(normalized, "<empty>", StringComparison.Ordinal))
        {
            return normalized;
        }

        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return normalized;
        }

        if (string.Equals(segments[0], "studio", StringComparison.OrdinalIgnoreCase) && segments.Length >= 3)
        {
            if (segments.Length >= 4
                && string.Equals(segments[2], "signals", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[3], "custom", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Take(4));
            }

            return string.Join('.', segments.Take(3));
        }

        return string.Join('.', segments.Take(Math.Min(3, segments.Length)));
    }

    private static string NormalizeSignalModule(string? module)
    {
        return string.IsNullOrWhiteSpace(module)
            ? "<unknown>"
            : module.Trim();
    }

    private static SignalRefreshBacklogSnapshot CaptureSignalRefreshBacklogSnapshot()
    {
        return new SignalRefreshBacklogSnapshot(
            TargetRefreshQueued: Math.Max(0, Volatile.Read(ref _activeTargetRefreshQueuedCount)),
            TargetRefreshFollowUpQueued: Math.Max(0, Volatile.Read(ref _activeTargetRefreshFollowUpCount)),
            TargetValueRefreshQueued: Math.Max(0, Volatile.Read(ref _activeTargetValueRefreshQueuedCount)),
            UnresolvedTargetGateCount: Math.Max(0, Volatile.Read(ref _activeUnresolvedSignalTargetCount)));
    }

    private static void AddNonNegative(ref int target, int delta)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            var next = Math.Max(0, current + delta);
            if (Interlocked.CompareExchange(ref target, next, current) == current)
            {
                return;
            }
        }
    }

    private static void LogBenchmarkCorrelationSnapshot(string scenario, BenchmarkCorrelationSnapshot snapshot)
    {
        HostLogger.Log.Information(
            "UI benchmark correlation snapshot. Scenario={Scenario} Event={Event} Phase={Phase} OffsetSeconds={OffsetSeconds} DelayMs={DelayMs} ThresholdMs={ThresholdMs} Sequence={Sequence} TargetRefreshQueued={TargetRefreshQueued} TargetRefreshFollowUpQueued={TargetRefreshFollowUpQueued} TargetValueRefreshQueued={TargetValueRefreshQueued} UnresolvedTargetGateCount={UnresolvedTargetGateCount} TargetRefreshExecuted={TargetRefreshExecuted} TargetValueRefreshExecuted={TargetValueRefreshExecuted} TargetRefreshCoalesced={TargetRefreshCoalesced} SignalValueRefreshCoalesced={SignalValueRefreshCoalesced} SignalTargetUnresolved={SignalTargetUnresolved} SignalTargetAvailability={SignalTargetAvailability} TargetRefreshTopModules={TargetRefreshTopModules} TargetValueRefreshTopModules={TargetValueRefreshTopModules} SignalTargetTopModules={SignalTargetTopModules} LastSignalStage={LastSignalStage} LastSignalPath={LastSignalPath} LastSignalModule={LastSignalModule} ActiveWindow={ActiveWindow} WindowCount={WindowCount} OpenDialogs={OpenDialogs} OpenDialogNames={OpenDialogNames}",
            scenario,
            snapshot.Event,
            snapshot.Phase,
            snapshot.OffsetSeconds,
            snapshot.DelayMs,
            snapshot.ThresholdMs,
            snapshot.Sequence,
            snapshot.TargetRefreshQueued,
            snapshot.TargetRefreshFollowUpQueued,
            snapshot.TargetValueRefreshQueued,
            snapshot.UnresolvedTargetGateCount,
            snapshot.TargetRefreshExecuted,
            snapshot.TargetValueRefreshExecuted,
            snapshot.TargetRefreshCoalesced,
            snapshot.SignalValueRefreshCoalesced,
            snapshot.SignalTargetUnresolved,
            snapshot.SignalTargetAvailability,
            snapshot.TargetRefreshTopModules,
            snapshot.TargetValueRefreshTopModules,
            snapshot.SignalTargetTopModules,
            snapshot.LastSignalStage,
            snapshot.LastSignalPath,
            snapshot.LastSignalModule,
            snapshot.ActiveWindow,
            snapshot.WindowCount,
            snapshot.OpenDialogs,
            snapshot.OpenDialogNames);
    }

    private static string GetLastPathSegment(string? path)
    {
        var normalizedPath = NormalizeRegistryKey(path);
        var lastSeparator = normalizedPath.LastIndexOf('.');
        return lastSeparator >= 0 && lastSeparator < normalizedPath.Length - 1
            ? normalizedPath[(lastSeparator + 1)..]
            : normalizedPath;
    }

    private sealed class TimingScope : IDisposable
    {
        private readonly string _category;
        private readonly string _name;
        private readonly TimeSpan _threshold;
        private readonly Window? _owner;
        private readonly Func<IReadOnlyDictionary<string, object?>?> _stateFactory;
        private readonly bool _alwaysLog;
        private readonly Stopwatch _stopwatch;
        private int _disposed;

        public TimingScope(
            string category,
            string name,
            TimeSpan threshold,
            Window? owner,
            Func<IReadOnlyDictionary<string, object?>?> stateFactory,
            bool alwaysLog = false)
        {
            _category = category;
            _name = name;
            _threshold = threshold;
            _owner = owner;
            _stateFactory = stateFactory;
            _alwaysLog = alwaysLog;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopwatch.Stop();
            var elapsed = _stopwatch.Elapsed;
            RecordBenchmarkTiming(category: _category, name: _name, elapsed: elapsed);
            if (!_alwaysLog && elapsed < _threshold)
            {
                return;
            }

            var context = CollectUiContext(_owner);
            var state = _stateFactory();
            var isUiThread = Dispatcher.UIThread.CheckAccess();
            if (_alwaysLog)
            {
                HostLogger.Log.Information(
                    "UI startup phase. Category={Category} Name={Name} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} ThreadId={ThreadId} IsUiThread={IsUiThread} ActiveWindow={ActiveWindow} WindowCount={WindowCount} OpenDialogs={OpenDialogs} OpenDialogNames={OpenDialogNames} State={State}",
                    _category,
                    _name,
                    Math.Round(elapsed.TotalMilliseconds, 1),
                    _threshold.TotalMilliseconds,
                    Environment.CurrentManagedThreadId,
                    isUiThread,
                    context.ActiveWindow,
                    context.WindowCount,
                    context.OpenDialogs,
                    context.OpenDialogNames,
                    FormatState(state));
                return;
            }

            HostLogger.Log.Warning(
                "UI diagnostic timing. Category={Category} Name={Name} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} ThreadId={ThreadId} IsUiThread={IsUiThread} ActiveWindow={ActiveWindow} WindowCount={WindowCount} OpenDialogs={OpenDialogs} OpenDialogNames={OpenDialogNames} State={State}",
                _category,
                _name,
                Math.Round(elapsed.TotalMilliseconds, 1),
                _threshold.TotalMilliseconds,
                Environment.CurrentManagedThreadId,
                isUiThread,
                context.ActiveWindow,
                context.WindowCount,
                context.OpenDialogs,
                context.OpenDialogNames,
                FormatState(state));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class BrowserActivityCounters
    {
        private readonly BoundedRegistrySummary _keySummary = new(BrowserRegistryTrackedEntryLimit);
        private readonly BoundedRegistrySummary _prefixSummary = new(BrowserRegistryTrackedEntryLimit);

        public int RegistryEvents { get; set; }

        public int AcceptedRegistryEvents { get; set; }

        public int IgnoredRegistryEvents { get; set; }

        public int DispatcherPosts { get; set; }

        public string LastRegistryKey { get; set; } = string.Empty;

        public string LastDispatcherReason { get; set; } = string.Empty;

        public double RegistryEventsPerSecond => BrowserActivityFlushInterval.TotalSeconds <= 0d
            ? 0d
            : RoundRatio(RegistryEvents / BrowserActivityFlushInterval.TotalSeconds);

        public double IgnoredRatio => RegistryEvents == 0
            ? 0d
            : RoundRatio((double)IgnoredRegistryEvents / RegistryEvents);

        public string TopKeySummary => _keySummary.FormatTopEntries(BrowserRegistryTopEntryLimit);

        public string TopPrefixSummary => _prefixSummary.FormatTopEntries(BrowserRegistryTopEntryLimit);

        public void RecordRegistryEvent(string? key, bool accepted)
        {
            RegistryEvents++;
            if (accepted)
            {
                AcceptedRegistryEvents++;
            }
            else
            {
                IgnoredRegistryEvents++;
            }

            LastRegistryKey = key ?? string.Empty;
            _keySummary.Record(NormalizeRegistryKey(key), accepted);
            _prefixSummary.Record(NormalizeRegistryDomainPrefix(key), accepted);
        }

        public void Reset()
        {
            RegistryEvents = 0;
            AcceptedRegistryEvents = 0;
            IgnoredRegistryEvents = 0;
            DispatcherPosts = 0;
            LastRegistryKey = string.Empty;
            LastDispatcherReason = string.Empty;
            _keySummary.Reset();
            _prefixSummary.Reset();
        }
    }

    private sealed class UiBenchmarkSession
    {
        private readonly object _sync = new();
        private readonly List<UiDelaySample> _uiDelaySamples = [];
        private readonly List<double> _chartRenderMs = [];
        private readonly List<BenchmarkCorrelationSnapshot> _correlationSnapshots = [];
        private readonly Dictionary<string, BenchmarkBrowserCounters> _browserBySource = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SignalPipelineStageCollector> _signalPipelineByStage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BenchmarkTimingCollector> _chartByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BenchmarkTimingCollector> _timingsByKey = new(StringComparer.Ordinal);
        private DateTimeOffset? _completedUtc;
        private DateTimeOffset _lastSpikeSnapshotUtc = DateTimeOffset.MinValue;
        private string _lastSignalStage = string.Empty;
        private string _lastSignalPath = string.Empty;
        private string _lastSignalModule = string.Empty;
        private bool _warmupTransitionSnapshotCaptured;

        public UiBenchmarkSession(string scenario, TimeSpan configuredDuration, TimeSpan warmupDuration)
        {
            Scenario = scenario;
            ConfiguredDuration = configuredDuration;
            WarmupDuration = warmupDuration;
            StartedUtc = DateTimeOffset.UtcNow;
        }

        public string Scenario { get; }

        public TimeSpan ConfiguredDuration { get; }

        public TimeSpan WarmupDuration { get; }

        public DateTimeOffset StartedUtc { get; }

        public BenchmarkCorrelationSnapshot? RecordUiDelay(
            TimeSpan delay,
            DateTimeOffset observedAtUtc,
            SignalRefreshBacklogSnapshot backlogSnapshot)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return null;
                }

                var offset = observedAtUtc - StartedUtc;
                if (offset < TimeSpan.Zero)
                {
                    offset = TimeSpan.Zero;
                }

                _uiDelaySamples.Add(new UiDelaySample(
                    Offset: offset,
                    DelayMs: delay.TotalMilliseconds));

                if (_warmupTransitionSnapshotCaptured || offset < WarmupDuration)
                {
                    return null;
                }

                _warmupTransitionSnapshotCaptured = true;
                return CreateCorrelationSnapshot(
                    eventName: "WarmupSteadyStateTransition",
                    observedAtUtc: observedAtUtc,
                    delay: delay,
                    threshold: null,
                    sequence: null,
                    context: null,
                    backlogSnapshot: backlogSnapshot);
            }
        }

        public void RecordTiming(string category, string name, TimeSpan elapsed)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return;
                }

                var elapsedMs = elapsed.TotalMilliseconds;
                if (string.Equals(category, "RealtimeChartRender", StringComparison.Ordinal))
                {
                    _chartRenderMs.Add(elapsedMs);
                    GetTimingCollector(_chartByName, key: name, category: category, name: name).Add(elapsedMs);
                    return;
                }

                var operationKey = string.Concat(category, "|", name);
                GetTimingCollector(_timingsByKey, key: operationKey, category: category, name: name).Add(elapsedMs);
            }
        }

        public void RecordBrowserRegistryEvent(string source, string? key, bool accepted)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return;
                }

                var counters = GetBrowserCounters(source);
                counters.RecordRegistryEvent(key: key, accepted: accepted);
            }
        }

        public void RecordBrowserDispatcherPost(string source, string reason)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return;
                }

                var counters = GetBrowserCounters(source);
                counters.RecordDispatcherPost(reason);
            }
        }

        public void RecordSignalPipelineEvent(
            string stage,
            string? path,
            string? module,
            TimeSpan? delay,
            DateTimeOffset? queuedAtUtc,
            DateTimeOffset? executedAtUtc)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return;
                }

                var normalizedStage = BuildSignalPipelineStageKey(
                    stage: stage,
                    queuedAtUtc: queuedAtUtc,
                    executedAtUtc: executedAtUtc);
                if (normalizedStage.Length == 0)
                {
                    return;
                }

                if (!_signalPipelineByStage.TryGetValue(normalizedStage, out var collector))
                {
                    collector = new SignalPipelineStageCollector();
                    _signalPipelineByStage[normalizedStage] = collector;
                }

                _lastSignalStage = normalizedStage;
                _lastSignalPath = NormalizeRegistryKey(path);
                _lastSignalModule = NormalizeSignalModule(module);
                collector.Record(
                    path: _lastSignalPath,
                    module: _lastSignalModule,
                    delayMs: delay?.TotalMilliseconds);
            }
        }

        public BenchmarkCorrelationSnapshot? CaptureUiSpikeSnapshot(
            DateTimeOffset observedAtUtc,
            TimeSpan delay,
            TimeSpan threshold,
            long sequence,
            UiContextSnapshot context,
            SignalRefreshBacklogSnapshot backlogSnapshot)
        {
            lock (_sync)
            {
                if (_completedUtc is not null)
                {
                    return null;
                }

                if (_lastSpikeSnapshotUtc != DateTimeOffset.MinValue
                    && observedAtUtc - _lastSpikeSnapshotUtc < BenchmarkSpikeSnapshotMinimumInterval)
                {
                    return null;
                }

                var snapshot = CreateCorrelationSnapshot(
                    eventName: "UiSpike",
                    observedAtUtc: observedAtUtc,
                    delay: delay,
                    threshold: threshold,
                    sequence: sequence,
                    context: context,
                    backlogSnapshot: backlogSnapshot);
                if (!snapshot.HasValue)
                {
                    return null;
                }

                _lastSpikeSnapshotUtc = observedAtUtc;
                return snapshot;
            }
        }

        private string BuildSignalPipelineStageKey(string stage, DateTimeOffset? queuedAtUtc, DateTimeOffset? executedAtUtc)
        {
            var normalizedStage = stage.Trim();
            if (normalizedStage.Length == 0)
            {
                return string.Empty;
            }

            var queuePhase = queuedAtUtc.HasValue ? GetSignalPipelinePhaseName(queuedAtUtc.Value) : string.Empty;
            var executePhase = executedAtUtc.HasValue ? GetSignalPipelinePhaseName(executedAtUtc.Value) : string.Empty;
            if (queuePhase.Length == 0 && executePhase.Length == 0)
            {
                return normalizedStage;
            }

            if (queuePhase.Length == 0)
            {
                return string.Concat(normalizedStage, "[", executePhase, "]");
            }

            if (executePhase.Length == 0)
            {
                return string.Concat(normalizedStage, "[", queuePhase, "]");
            }

            return string.Concat(normalizedStage, "[", queuePhase, "->", executePhase, "]");
        }

        private string GetSignalPipelinePhaseName(DateTimeOffset timestampUtc)
        {
            var offset = timestampUtc - StartedUtc;
            if (offset < TimeSpan.Zero)
            {
                return "PreBenchmark";
            }

            return offset < WarmupDuration
                ? "Warmup"
                : "SteadyState";
        }

        public void Complete(DateTimeOffset completedUtc)
        {
            lock (_sync)
            {
                _completedUtc ??= completedUtc;
            }
        }

        public UiBenchmarkSummary CreateSummary(string reason, DateTimeOffset generatedAtUtc)
        {
            lock (_sync)
            {
                var completedUtc = _completedUtc ?? generatedAtUtc;
                var duration = completedUtc - StartedUtc;
                if (duration < TimeSpan.Zero)
                {
                    duration = TimeSpan.Zero;
                }

                var uiDelaySummary = CreateMetricSummary(_uiDelaySamples.Select(static sample => sample.DelayMs));
                var uiDelayAfterWarmupSummary = CreateMetricSummary(_uiDelaySamples
                    .Where(sample => sample.Offset >= WarmupDuration)
                    .Select(static sample => sample.DelayMs));
                var chartRenderSummary = CreateMetricSummary(_chartRenderMs);
                var browserSummaries = _browserBySource
                    .Select(pair => new BrowserBenchmarkSummary(
                        Source: pair.Key,
                        RegistryEvents: pair.Value.RegistryEvents,
                        RegistryEventsPerSecond: duration.TotalSeconds <= 0d ? 0d : RoundRatio(pair.Value.RegistryEvents / duration.TotalSeconds),
                        AcceptedRegistryEvents: pair.Value.AcceptedRegistryEvents,
                        IgnoredRegistryEvents: pair.Value.IgnoredRegistryEvents,
                        IgnoredRatio: pair.Value.RegistryEvents == 0 ? 0d : RoundRatio((double)pair.Value.IgnoredRegistryEvents / pair.Value.RegistryEvents),
                        DispatcherPosts: pair.Value.DispatcherPosts,
                        TopPrefixSummary: pair.Value.TopPrefixSummary,
                        TopKeySummary: pair.Value.TopKeySummary,
                        LastRegistryKey: pair.Value.LastRegistryKey,
                        LastDispatcherReason: pair.Value.LastDispatcherReason))
                    .ToArray();
                var chartSummaries = _chartByName
                    .Select(static pair => new TimingBenchmarkSummary(
                        Category: pair.Value.Category,
                        Name: pair.Value.Name,
                        Metrics: CreateMetricSummary(pair.Value.Values)))
                    .ToArray();
                var signalPipelineSummaries = _signalPipelineByStage
                    .Select(pair => new SignalPipelineBenchmarkSummary(
                        Stage: pair.Key,
                        Count: pair.Value.Count,
                        EventsPerSecond: duration.TotalSeconds <= 0d ? 0d : RoundRatio(pair.Value.Count / duration.TotalSeconds),
                        DelayMetrics: CreateMetricSummary(pair.Value.DelayValues),
                        TopPathSummary: pair.Value.GetTopPathSummary(duration.TotalSeconds),
                        TopModuleSummary: pair.Value.GetTopModuleSummary(duration.TotalSeconds),
                        LastPath: pair.Value.LastPath,
                        LastModule: pair.Value.LastModule))
                    .ToArray();
                var timingSummaries = _timingsByKey
                    .Select(static pair => new TimingBenchmarkSummary(
                        Category: pair.Value.Category,
                        Name: pair.Value.Name,
                        Metrics: CreateMetricSummary(pair.Value.Values)))
                    .ToArray();
                var correlationSnapshots = _correlationSnapshots.ToArray();
                var totalBrowserDispatcherPosts = browserSummaries.Sum(static item => item.DispatcherPosts);
                var spike50Count = _uiDelaySamples.Count(static sample => sample.DelayMs >= ProbeThresholds[0].TotalMilliseconds);
                var spike100Count = _uiDelaySamples.Count(static sample => sample.DelayMs >= ProbeThresholds[1].TotalMilliseconds);
                var spike250Count = _uiDelaySamples.Count(static sample => sample.DelayMs >= ProbeThresholds[2].TotalMilliseconds);
                var status = EvaluateStatus(
                    uiDelayAfterWarmup: uiDelayAfterWarmupSummary,
                    chartRender: chartRenderSummary,
                    browserSummaries: browserSummaries,
                    totalBrowserDispatcherPosts: totalBrowserDispatcherPosts,
                    budgetNotes: out var budgetNotes);

                return new UiBenchmarkSummary(
                    Scenario: Scenario,
                    Reason: reason,
                    Duration: duration,
                    WarmupDuration: WarmupDuration,
                    UiDelay: uiDelaySummary,
                    UiDelayAfterWarmup: uiDelayAfterWarmupSummary,
                    Spike50Count: spike50Count,
                    Spike100Count: spike100Count,
                    Spike250Count: spike250Count,
                    TotalBrowserDispatcherPosts: totalBrowserDispatcherPosts,
                    ChartRender: chartRenderSummary,
                    BrowserSummaries: browserSummaries,
                    SignalPipelineSummaries: signalPipelineSummaries,
                    CorrelationSnapshots: correlationSnapshots,
                    ChartSummaries: chartSummaries,
                    TimingSummaries: timingSummaries,
                    Status: status,
                    BudgetNotes: budgetNotes);
            }
        }

        private BenchmarkCorrelationSnapshot? CreateCorrelationSnapshot(
            string eventName,
            DateTimeOffset observedAtUtc,
            TimeSpan? delay,
            TimeSpan? threshold,
            long? sequence,
            UiContextSnapshot? context,
            SignalRefreshBacklogSnapshot backlogSnapshot)
        {
            if (_correlationSnapshots.Count >= BenchmarkCorrelationSnapshotLimit)
            {
                return null;
            }

            var offset = observedAtUtc - StartedUtc;
            if (offset < TimeSpan.Zero)
            {
                offset = TimeSpan.Zero;
            }

            var durationSeconds = Math.Max(0d, offset.TotalSeconds);
            var snapshot = new BenchmarkCorrelationSnapshot(
                Event: eventName,
                Phase: GetSignalPipelinePhaseName(observedAtUtc),
                OffsetSeconds: RoundRatio(durationSeconds),
                DelayMs: delay.HasValue ? RoundMilliseconds(delay.Value.TotalMilliseconds) : null,
                ThresholdMs: threshold.HasValue ? RoundMilliseconds(threshold.Value.TotalMilliseconds) : null,
                Sequence: sequence,
                TargetRefreshQueued: backlogSnapshot.TargetRefreshQueued,
                TargetRefreshFollowUpQueued: backlogSnapshot.TargetRefreshFollowUpQueued,
                TargetValueRefreshQueued: backlogSnapshot.TargetValueRefreshQueued,
                UnresolvedTargetGateCount: backlogSnapshot.UnresolvedTargetGateCount,
                TargetRefreshExecuted: GetStageCountSummary("TargetRefreshExecuted"),
                TargetValueRefreshExecuted: GetStageCountSummary("TargetValueRefreshExecuted"),
                TargetRefreshCoalesced: GetStageCountSummary("TargetRefreshCoalesced"),
                SignalValueRefreshCoalesced: GetStageCountSummary("SignalValueRefreshCoalesced"),
                SignalTargetUnresolved: GetStageCountSummary("SignalTargetUnresolved"),
                SignalTargetAvailability: GetStageCountSummary("SignalTargetAvailability"),
                TargetRefreshTopModules: GetStageTopModuleSummary("TargetRefreshExecuted", durationSeconds),
                TargetValueRefreshTopModules: GetStageTopModuleSummary("TargetValueRefreshExecuted", durationSeconds),
                SignalTargetTopModules: GetStageTopModuleSummary("SignalTarget", durationSeconds),
                LastSignalStage: _lastSignalStage,
                LastSignalPath: _lastSignalPath,
                LastSignalModule: _lastSignalModule,
                ActiveWindow: context?.ActiveWindow ?? string.Empty,
                WindowCount: context?.WindowCount ?? 0,
                OpenDialogs: context?.OpenDialogs ?? 0,
                OpenDialogNames: context?.OpenDialogNames ?? string.Empty);
            _correlationSnapshots.Add(snapshot);
            return snapshot;
        }

        private string GetStageCountSummary(string stagePrefix)
        {
            var entries = _signalPipelineByStage
                .Where(pair => pair.Key.StartsWith(stagePrefix, StringComparison.Ordinal))
                .OrderByDescending(static pair => pair.Value.Count)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(4)
                .Select(static pair => $"{pair.Key}={pair.Value.Count}")
                .ToArray();
            return entries.Length == 0 ? string.Empty : string.Join(", ", entries);
        }

        private string GetStageTopModuleSummary(string stagePrefix, double durationSeconds)
        {
            var entries = _signalPipelineByStage
                .Where(pair => pair.Key.StartsWith(stagePrefix, StringComparison.Ordinal))
                .OrderByDescending(static pair => pair.Value.Count)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(3)
                .Select(pair =>
                {
                    var summary = pair.Value.GetTopModuleSummary(durationSeconds);
                    return string.IsNullOrWhiteSpace(summary)
                        ? string.Empty
                        : $"{pair.Key}:{summary}";
                })
                .Where(static summary => !string.IsNullOrWhiteSpace(summary))
                .ToArray();
            return entries.Length == 0 ? string.Empty : string.Join("; ", entries);
        }

        private BenchmarkBrowserCounters GetBrowserCounters(string source)
        {
            if (!_browserBySource.TryGetValue(source, out var counters))
            {
                counters = new BenchmarkBrowserCounters();
                _browserBySource[source] = counters;
            }

            return counters;
        }

        private static BenchmarkTimingCollector GetTimingCollector(
            Dictionary<string, BenchmarkTimingCollector> dictionary,
            string key,
            string category,
            string name)
        {
            if (!dictionary.TryGetValue(key, out var collector))
            {
                collector = new BenchmarkTimingCollector(category: category, name: name);
                dictionary[key] = collector;
            }

            return collector;
        }
    }

    private sealed class BenchmarkBrowserCounters
    {
        private readonly BoundedRegistrySummary _keySummary = new(BrowserRegistryTrackedEntryLimit);
        private readonly BoundedRegistrySummary _prefixSummary = new(BrowserRegistryTrackedEntryLimit);

        public int RegistryEvents { get; set; }

        public int AcceptedRegistryEvents { get; set; }

        public int IgnoredRegistryEvents { get; set; }

        public int DispatcherPosts { get; set; }

        public string LastRegistryKey { get; set; } = string.Empty;

        public string LastDispatcherReason { get; set; } = string.Empty;

        public string TopKeySummary => _keySummary.FormatTopEntries(BrowserRegistryTopEntryLimit);

        public string TopPrefixSummary => _prefixSummary.FormatTopEntries(BrowserRegistryTopEntryLimit);

        public void RecordRegistryEvent(string? key, bool accepted)
        {
            RegistryEvents++;
            if (accepted)
            {
                AcceptedRegistryEvents++;
            }
            else
            {
                IgnoredRegistryEvents++;
            }

            LastRegistryKey = key ?? string.Empty;
            _keySummary.Record(NormalizeRegistryKey(key), accepted);
            _prefixSummary.Record(NormalizeRegistryDomainPrefix(key), accepted);
        }

        public void RecordDispatcherPost(string reason)
        {
            DispatcherPosts++;
            LastDispatcherReason = reason;
        }
    }

    private sealed class BoundedRegistrySummary
    {
        private readonly int _capacity;
        private readonly Dictionary<string, RegistrySummaryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private int _otherCount;

        public BoundedRegistrySummary(int capacity)
        {
            _capacity = capacity;
        }

        public void Record(string key, bool accepted)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.Record(accepted);
                return;
            }

            if (_entries.Count >= _capacity)
            {
                _otherCount++;
                return;
            }

            var created = new RegistrySummaryEntry();
            created.Record(accepted);
            _entries[key] = created;
        }

        public string FormatTopEntries(int topN)
        {
            var entries = _entries
                .OrderByDescending(static pair => pair.Value.Total)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(topN)
                .Select(static pair => $"{pair.Key}({pair.Value.Total},a={pair.Value.Accepted},i={pair.Value.Ignored})")
                .ToList();

            if (_otherCount > 0)
            {
                entries.Add($"<other>({_otherCount})");
            }

            return entries.Count == 0 ? string.Empty : string.Join(", ", entries);
        }

        public void Reset()
        {
            _entries.Clear();
            _otherCount = 0;
        }
    }

    private sealed class RegistrySummaryEntry
    {
        public int Total { get; private set; }

        public int Accepted { get; private set; }

        public int Ignored { get; private set; }

        public void Record(bool accepted)
        {
            Total++;
            if (accepted)
            {
                Accepted++;
            }
            else
            {
                Ignored++;
            }
        }
    }

    private sealed class BenchmarkTimingCollector
    {
        public BenchmarkTimingCollector(string category, string name)
        {
            Category = category;
            Name = name;
        }

        public string Category { get; }

        public string Name { get; }

        public List<double> Values { get; } = [];

        public void Add(double value)
        {
            Values.Add(value);
        }
    }

    private sealed class SignalPipelineStageCollector
    {
        private readonly BoundedCountSummary _pathSummary = new(SignalPipelineTrackedEntryLimit);
        private readonly BoundedCountSummary _moduleSummary = new(SignalPipelineTrackedEntryLimit);

        public int Count { get; private set; }

        public string LastPath { get; private set; } = string.Empty;

        public string LastModule { get; private set; } = string.Empty;

        public List<double> DelayValues { get; } = [];

        public string GetTopPathSummary(double durationSeconds)
            => _pathSummary.FormatTopEntries(SignalPipelineTopEntryLimit, durationSeconds);

        public string GetTopModuleSummary(double durationSeconds)
            => _moduleSummary.FormatTopEntries(SignalPipelineTopEntryLimit, durationSeconds);

        public void Record(string path, string module, double? delayMs)
        {
            Count++;
            LastPath = path;
            LastModule = module;
            _pathSummary.Record(path);
            _moduleSummary.Record(module);
            if (delayMs is double value)
            {
                DelayValues.Add(value);
            }
        }
    }

    private sealed class BoundedCountSummary
    {
        private readonly int _capacity;
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private int _otherCount;

        public BoundedCountSummary(int capacity)
        {
            _capacity = capacity;
        }

        public void Record(string key)
        {
            if (_counts.TryGetValue(key, out var count))
            {
                _counts[key] = count + 1;
                return;
            }

            if (_counts.Count >= _capacity)
            {
                _otherCount++;
                return;
            }

            _counts[key] = 1;
        }

        public string FormatTopEntries(int topN, double durationSeconds = 0d)
        {
            var entries = _counts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(topN)
                .Select(pair => durationSeconds > 0d
                    ? $"{pair.Key}({pair.Value},{RoundRatio(pair.Value / durationSeconds)}/s)"
                    : $"{pair.Key}({pair.Value})")
                .ToList();

            if (_otherCount > 0)
            {
                entries.Add($"<other>({_otherCount})");
            }

            return entries.Count == 0 ? string.Empty : string.Join(", ", entries);
        }
    }

    private readonly record struct UiContextSnapshot(
        string ActiveWindow,
        int WindowCount,
        int OpenDialogs,
        string OpenDialogNames);

    private readonly record struct UiDelaySample(TimeSpan Offset, double DelayMs);

    private readonly record struct UiBenchmarkConfiguration(bool Enabled, string Scenario, TimeSpan Duration)
    {
        public static UiBenchmarkConfiguration Disabled { get; } = new(false, "Manual", TimeSpan.Zero);
    }

    private readonly record struct BrowserBenchmarkSummary(
        string Source,
        int RegistryEvents,
        double RegistryEventsPerSecond,
        int AcceptedRegistryEvents,
        int IgnoredRegistryEvents,
        double IgnoredRatio,
        int DispatcherPosts,
        string TopPrefixSummary,
        string TopKeySummary,
        string LastRegistryKey,
        string LastDispatcherReason);

    private readonly record struct SignalPipelineBenchmarkSummary(
        string Stage,
        int Count,
        double EventsPerSecond,
        MetricSummary DelayMetrics,
        string TopPathSummary,
        string TopModuleSummary,
        string LastPath,
        string LastModule);

    private readonly record struct TimingBenchmarkSummary(
        string Category,
        string Name,
        MetricSummary Metrics);

    private readonly record struct UiBenchmarkSummary(
        string Scenario,
        string Reason,
        TimeSpan Duration,
        TimeSpan WarmupDuration,
        MetricSummary UiDelay,
        MetricSummary UiDelayAfterWarmup,
        int Spike50Count,
        int Spike100Count,
        int Spike250Count,
        int TotalBrowserDispatcherPosts,
        MetricSummary ChartRender,
        BrowserBenchmarkSummary[] BrowserSummaries,
        SignalPipelineBenchmarkSummary[] SignalPipelineSummaries,
        BenchmarkCorrelationSnapshot[] CorrelationSnapshots,
        TimingBenchmarkSummary[] ChartSummaries,
        TimingBenchmarkSummary[] TimingSummaries,
        BenchmarkStatus Status,
        string BudgetNotes);

    private readonly record struct SignalRefreshBacklogSnapshot(
        int TargetRefreshQueued,
        int TargetRefreshFollowUpQueued,
        int TargetValueRefreshQueued,
        int UnresolvedTargetGateCount);

    private readonly record struct BenchmarkCorrelationSnapshot(
        string Event,
        string Phase,
        double OffsetSeconds,
        double? DelayMs,
        double? ThresholdMs,
        long? Sequence,
        int TargetRefreshQueued,
        int TargetRefreshFollowUpQueued,
        int TargetValueRefreshQueued,
        int UnresolvedTargetGateCount,
        string TargetRefreshExecuted,
        string TargetValueRefreshExecuted,
        string TargetRefreshCoalesced,
        string SignalValueRefreshCoalesced,
        string SignalTargetUnresolved,
        string SignalTargetAvailability,
        string TargetRefreshTopModules,
        string TargetValueRefreshTopModules,
        string SignalTargetTopModules,
        string LastSignalStage,
        string LastSignalPath,
        string LastSignalModule,
        string ActiveWindow,
        int WindowCount,
        int OpenDialogs,
        string OpenDialogNames);

    private readonly record struct MetricSummary(int Count, double MaxMs, double P95Ms)
    {
        public static MetricSummary Empty { get; } = new(0, 0d, 0d);
    }

    private enum BenchmarkStatus
    {
        Pass,
        Warn,
        Fail
    }

    private static string FormatState(IReadOnlyDictionary<string, object?>? state)
    {
        if (state is null || state.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", state.Select(static pair => $"{pair.Key}={FormatValue(pair.Value)}"));
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value switch
        {
            IFormattable formattable => formattable.ToString(format: null, formatProvider: CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
