using System.Diagnostics;
using System.Globalization;
using Amium.Item.Server.MultimasterDemo.Controllers;
using Amium.Item.Server.MultimasterDemo.Logging;
using Amium.Item.Server.MultimasterDemo.Models;
using Amium.Items;

namespace Amium.Item.Server.MultimasterDemo.Testing;

internal sealed class MultimasterSelfTestRunner
{
    private readonly MultimasterDemoController _controller;
    private readonly MultimasterSelfTestOptions _options;
    private readonly string _runId;
    private readonly MultimasterTestLogWriter _logWriter;

    internal MultimasterSelfTestRunner(MultimasterDemoController controller, MultimasterSelfTestOptions options)
    {
        _controller = controller;
        _options = options;
        _runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_hhmmssfff", CultureInfo.InvariantCulture).ToLowerInvariant();
        _logWriter = new MultimasterTestLogWriter(_runId, _options.LogDirectory);
    }

    internal async Task<MultimasterSelfTestRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var results = new List<MultimasterSelfTestResult>();
        var notes = new List<string>();
        var controllerStarted = false;
        Exception? fatalException = null;

        _logWriter.RecordRunStarted(startedUtc, _controller.EndpointSummary, _controller.NodeIds);
        _controller.NodeEventLogged += HandleNodeEventLogged;

        try
        {
            await _controller.StartAsync(cancellationToken).ConfigureAwait(false);
            controllerStarted = true;

            var baselineResult = await WaitForBaselineAsync(cancellationToken).ConfigureAwait(false);
            results.Add(baselineResult);
            _logWriter.RecordCaseResult(baselineResult);

            if (baselineResult.Status == MultimasterSelfTestStatus.Passed)
            {
                await AppendResultsAsync(results, RunCrossWriteMatrixAsync, cancellationToken).ConfigureAwait(false);
                await AppendResultsAsync(results, RunRuntimeCreationAsync, cancellationToken).ConfigureAwait(false);
                await AppendResultsAsync(results, RunRuntimeUpdateAsync, cancellationToken).ConfigureAwait(false);
                await AppendResultsAsync(results, RunRuntimeRemoteWriteAsync, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                notes.Add("Baseline visibility failed. Remaining cases were skipped.");
                _logWriter.RecordNote(notes[^1]);
            }
        }
        catch (Exception exception)
        {
            fatalException = exception;
            notes.Add($"Fatal error: {exception.Message}");
            _logWriter.RecordFatal(exception);
        }
        finally
        {
            if (controllerStarted)
            {
                try
                {
                    await _controller.StopAsync().ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    notes.Add($"Stop failed: {stopException.Message}");
                    _logWriter.RecordNote(notes[^1]);
                    fatalException ??= stopException;
                }
            }

            _controller.NodeEventLogged -= HandleNodeEventLogged;
        }

        var endedUtc = DateTimeOffset.UtcNow;
        var isSuccess = fatalException is null && results.Count > 0 && results.All(result => result.Status == MultimasterSelfTestStatus.Passed);
        var runResult = new MultimasterSelfTestRunResult(
            RunId: _runId,
            IsSuccess: isSuccess,
            StartedUtc: startedUtc,
            EndedUtc: endedUtc,
            JsonLogPath: string.Empty,
            SummaryPath: string.Empty,
            Results: results,
            Notes: notes);

        _logWriter.RecordRunCompleted(runResult);
        var paths = await _logWriter.WriteAsync(runResult, cancellationToken).ConfigureAwait(false);

        return runResult with
        {
            JsonLogPath = paths.JsonLogPath,
            SummaryPath = paths.SummaryPath,
        };
    }

    private async Task AppendResultsAsync(
        ICollection<MultimasterSelfTestResult> destination,
        Func<CancellationToken, Task<IReadOnlyList<MultimasterSelfTestResult>>> producer,
        CancellationToken cancellationToken)
    {
        foreach (var result in await producer(cancellationToken).ConfigureAwait(false))
        {
            destination.Add(result);
            _logWriter.RecordCaseResult(result);
        }
    }

    private async Task<MultimasterSelfTestResult> WaitForBaselineAsync(CancellationToken cancellationToken)
    {
        var targetPaths = _controller.NodeIds
            .SelectMany(nodeId => new[]
            {
                DemoNodeController.GetDynamicItemPath(nodeId),
                DemoNodeController.GetWriteTestItemPath(nodeId),
            })
            .ToArray();
        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var latestStates = CaptureBaselineStates(targetPaths);

        while (stopwatch.Elapsed < _options.BaselineTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestStates = CaptureBaselineStates(targetPaths);

            if (latestStates.Values.All(state => state.IsAvailable))
            {
                stopwatch.Stop();
                return new MultimasterSelfTestResult(
                    Category: "baseline",
                    Name: "baseline_visibility",
                    ActorNodeId: "system",
                    TargetPath: string.Join(", ", targetPaths),
                    ExpectedValue: "available",
                    Status: MultimasterSelfTestStatus.Passed,
                    StartedUtc: startedUtc,
                    EndedUtc: DateTimeOffset.UtcNow,
                    Duration: stopwatch.Elapsed,
                    ObservedValues: latestStates.ToDictionary(
                        keySelector: item => item.Key,
                        elementSelector: item => FormatObservedState(item.Value),
                        comparer: StringComparer.OrdinalIgnoreCase),
                    ObservedUpdateCounts: latestStates.Keys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase),
                    MissingObservers: Array.Empty<string>(),
                    Notes: Array.Empty<string>(),
                    SuspiciousUpdateGrowth: false,
                    Required: true);
            }

            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new MultimasterSelfTestResult(
            Category: "baseline",
            Name: "baseline_visibility",
            ActorNodeId: "system",
            TargetPath: string.Join(", ", targetPaths),
            ExpectedValue: "available",
            Status: MultimasterSelfTestStatus.Failed,
            StartedUtc: startedUtc,
            EndedUtc: DateTimeOffset.UtcNow,
            Duration: stopwatch.Elapsed,
            ObservedValues: latestStates.ToDictionary(
                keySelector: item => item.Key,
                elementSelector: item => FormatObservedState(item.Value),
                comparer: StringComparer.OrdinalIgnoreCase),
            ObservedUpdateCounts: latestStates.Keys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase),
            MissingObservers: latestStates
                .Where(item => !item.Value.IsAvailable)
                .Select(item => item.Key)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Notes: new[] { "Not all baseline items became visible before the timeout." },
            SuspiciousUpdateGrowth: false,
            Required: true);
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunCrossWriteMatrixAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var actorNodeId in _controller.NodeIds)
        {
            foreach (var targetNodeId in _controller.NodeIds)
            {
                var targetPath = DemoNodeController.GetWriteTestItemPath(targetNodeId);
                var expectedValue = BuildValue("cross_write", actorNodeId, targetNodeId);
                var testCase = new MultimasterSelfTestCase(
                    Category: "cross_write",
                    Name: $"cross_write_{actorNodeId}_to_{targetNodeId}",
                    ActorNodeId: actorNodeId,
                    TargetPath: targetPath,
                    ExpectedValue: expectedValue,
                    Timeout: _options.OperationTimeout,
                    RequiredObserverNodeIds: _controller.NodeIds);

                results.Add(await ExecuteCaseAsync(
                    testCase,
                    action: token => _controller.WriteRemoteValueAsync(actorNodeId, targetPath, expectedValue, token),
                    cancellationToken: cancellationToken).ConfigureAwait(false));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunRuntimeCreationAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var entry in _controller.NodeIds.Select((nodeId, index) => (NodeId: nodeId, Index: index + 1)))
        {
            var path = BuildRuntimePath(entry.NodeId, "created", entry.Index);
            var expectedValue = BuildValue("runtime_create", entry.NodeId, entry.NodeId);
            var testCase = new MultimasterSelfTestCase(
                Category: "runtime_create",
                Name: $"runtime_create_{entry.NodeId}",
                ActorNodeId: entry.NodeId,
                TargetPath: path,
                ExpectedValue: expectedValue,
                Timeout: _options.OperationTimeout);

            results.Add(await ExecuteCaseAsync(
                testCase,
                action: token => _controller.PublishRuntimeItemAsync(entry.NodeId, path, expectedValue, writable: false, token),
                cancellationToken: cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunRuntimeUpdateAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var entry in _controller.NodeIds.Select((nodeId, index) => (NodeId: nodeId, Index: index + 1)))
        {
            var path = BuildRuntimePath(entry.NodeId, "created", entry.Index);
            var expectedValue = BuildValue("runtime_update", entry.NodeId, entry.NodeId);
            var testCase = new MultimasterSelfTestCase(
                Category: "runtime_update",
                Name: $"runtime_update_{entry.NodeId}",
                ActorNodeId: entry.NodeId,
                TargetPath: path,
                ExpectedValue: expectedValue,
                Timeout: _options.OperationTimeout);

            results.Add(await ExecuteCaseAsync(
                testCase,
                action: token => _controller.UpdateOwnedItemAsync(entry.NodeId, path, expectedValue, token),
                cancellationToken: cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunRuntimeRemoteWriteAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var entry in _controller.NodeIds.Select((nodeId, index) => (NodeId: nodeId, Index: index + 1)))
        {
            var path = BuildRuntimePath(entry.NodeId, "remote_write_probe", entry.Index);
            var initialValue = BuildValue("remote_write_init", entry.NodeId, entry.NodeId);
            var prepareCase = new MultimasterSelfTestCase(
                Category: "runtime_remote_write_prepare",
                Name: $"runtime_remote_write_prepare_{entry.NodeId}",
                ActorNodeId: entry.NodeId,
                TargetPath: path,
                ExpectedValue: initialValue,
                Timeout: _options.OperationTimeout);

            results.Add(await ExecuteCaseAsync(
                prepareCase,
                action: token => _controller.PublishRuntimeItemAsync(entry.NodeId, path, initialValue, writable: true, token),
                cancellationToken: cancellationToken).ConfigureAwait(false));

            foreach (var actorNodeId in _controller.NodeIds.Where(nodeId => !string.Equals(nodeId, entry.NodeId, StringComparison.OrdinalIgnoreCase)))
            {
                var expectedValue = BuildValue("runtime_remote_write", actorNodeId, entry.NodeId);
                var writeCase = new MultimasterSelfTestCase(
                    Category: "runtime_remote_write",
                    Name: $"runtime_remote_write_{actorNodeId}_to_{entry.NodeId}",
                    ActorNodeId: actorNodeId,
                    TargetPath: path,
                    ExpectedValue: expectedValue,
                    Timeout: _options.OperationTimeout,
                    RequiredObserverNodeIds: _controller.NodeIds);

                results.Add(await ExecuteCaseAsync(
                    writeCase,
                    action: token => _controller.WriteRemoteValueAsync(actorNodeId, path, expectedValue, token),
                    cancellationToken: cancellationToken).ConfigureAwait(false));
            }
        }

        return results;
    }

    private async Task<MultimasterSelfTestResult> ExecuteCaseAsync(
        MultimasterSelfTestCase testCase,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var requiredObservers = ResolveRequiredObservers(testCase);
        var lastStates = new Dictionary<string, ObservedItemState>(_controller.GetObservedStates(testCase.TargetPath), StringComparer.OrdinalIgnoreCase);
        var changeCounts = _controller.NodeIds.ToDictionary(nodeId => nodeId, _ => 0, StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, ObservedItemState> latestStates = lastStates;
        var notes = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException exception)
        {
            stopwatch.Stop();
            notes.Add(exception.Message);
            latestStates = _controller.GetObservedStates(testCase.TargetPath);
            return CreateResult(testCase, MultimasterSelfTestStatus.Blocked, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            notes.Add(exception.Message);
            latestStates = _controller.GetObservedStates(testCase.TargetPath);
            return CreateResult(testCase, MultimasterSelfTestStatus.Failed, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
        }

        while (stopwatch.Elapsed < testCase.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestStates = _controller.GetObservedStates(testCase.TargetPath);
            UpdateChangeCounts(lastStates, latestStates, changeCounts);

            if (requiredObservers.All(nodeId =>
                    latestStates.TryGetValue(nodeId, out var state)
                    && state.IsAvailable
                    && string.Equals(state.ValueText, testCase.ExpectedValue, StringComparison.Ordinal)))
            {
                stopwatch.Stop();
                return CreateResult(testCase, MultimasterSelfTestStatus.Passed, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
            }

            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        latestStates = _controller.GetObservedStates(testCase.TargetPath);
        UpdateChangeCounts(lastStates, latestStates, changeCounts);
        notes.Add("Timed out while waiting for all observers to reach the expected value.");
        return CreateResult(testCase, MultimasterSelfTestStatus.Failed, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
    }

    private MultimasterSelfTestResult CreateResult(
        MultimasterSelfTestCase testCase,
        MultimasterSelfTestStatus status,
        DateTimeOffset startedUtc,
        TimeSpan duration,
        IReadOnlyDictionary<string, ObservedItemState> latestStates,
        IReadOnlyDictionary<string, int> changeCounts,
        IReadOnlyList<string> notes,
        IReadOnlyList<string> requiredObservers)
    {
        var missingObservers = requiredObservers
            .Where(nodeId => !latestStates.TryGetValue(nodeId, out var state)
                || !state.IsAvailable
                || !string.Equals(state.ValueText, testCase.ExpectedValue, StringComparison.Ordinal))
            .OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suspicious = changeCounts.Values.Any(count => count > _options.SuspiciousUpdateGrowthThreshold);
        var allNotes = notes.ToList();

        if (suspicious)
        {
            allNotes.Add($"Observed change count exceeded the suspicion threshold of {_options.SuspiciousUpdateGrowthThreshold}.");
        }

        return new MultimasterSelfTestResult(
            Category: testCase.Category,
            Name: testCase.Name,
            ActorNodeId: testCase.ActorNodeId,
            TargetPath: testCase.TargetPath,
            ExpectedValue: testCase.ExpectedValue,
            Status: status,
            StartedUtc: startedUtc,
            EndedUtc: startedUtc + duration,
            Duration: duration,
            ObservedValues: latestStates.ToDictionary(
                keySelector: item => item.Key,
                elementSelector: item => FormatObservedState(item.Value),
                comparer: StringComparer.OrdinalIgnoreCase),
            ObservedUpdateCounts: new Dictionary<string, int>(changeCounts, StringComparer.OrdinalIgnoreCase),
            MissingObservers: missingObservers,
            Notes: allNotes,
            SuspiciousUpdateGrowth: suspicious,
            Required: testCase.Required);
    }

    private void HandleNodeEventLogged(object? sender, DemoNodeEvent nodeEvent)
        => _logWriter.RecordNodeEvent(nodeEvent);

    private IReadOnlyList<string> ResolveRequiredObservers(MultimasterSelfTestCase testCase)
        => testCase.RequiredObserverNodeIds ?? _controller.NodeIds;

    private IReadOnlyDictionary<string, ObservedItemState> CaptureBaselineStates(IEnumerable<string> paths)
    {
        var states = new Dictionary<string, ObservedItemState>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            foreach (var observedState in _controller.GetObservedStates(path))
            {
                states[$"{observedState.Key}:{path}"] = observedState.Value;
            }
        }

        return states;
    }

    private static string FormatObservedState(ObservedItemState state)
    {
        if (!state.IsAvailable)
        {
            return "(missing)";
        }

        var timestamp = state.LastUpdatedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "(no timestamp)";
        return $"{state.ValueText} @ {timestamp}";
    }

    private static void UpdateChangeCounts(
        IDictionary<string, ObservedItemState> previousStates,
        IReadOnlyDictionary<string, ObservedItemState> currentStates,
        IDictionary<string, int> changeCounts)
    {
        foreach (var currentState in currentStates)
        {
            if (previousStates.TryGetValue(currentState.Key, out var previousState)
                && HasStateChanged(previousState, currentState.Value))
            {
                changeCounts[currentState.Key] = changeCounts[currentState.Key] + 1;
            }

            previousStates[currentState.Key] = currentState.Value;
        }
    }

    private static bool HasStateChanged(ObservedItemState previous, ObservedItemState current)
        => previous.IsAvailable != current.IsAvailable
           || !string.Equals(previous.ValueText, current.ValueText, StringComparison.Ordinal)
           || previous.LastUpdatedUtc != current.LastUpdatedUtc;

    private string BuildRuntimePath(string ownerNodeId, string kind, int sequence)
        => $"nodes.{ownerNodeId}.runtime.{kind}_{sequence:000}_{_runId}";

    private string BuildValue(string category, string actorNodeId, string targetNodeId)
        => $"{_runId}|{category}|{actorNodeId}|{targetNodeId}";
}