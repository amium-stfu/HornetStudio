using System.Diagnostics;
using System.Globalization;
using Amium.Item.Server.MultimasterDemo.Controllers;
using Amium.Item.Server.MultimasterDemo.Logging;
using Amium.Item.Server.MultimasterDemo.Models;

namespace Amium.Item.Server.MultimasterDemo.Testing;

internal sealed class MeshMultimasterSelfTestRunner
{
    private readonly MeshMultimasterDemoController _controller;
    private readonly MultimasterSelfTestOptions _options;
    private readonly string _runId;
    private readonly MultimasterTestLogWriter _logWriter;

    internal MeshMultimasterSelfTestRunner(MeshMultimasterDemoController controller, MultimasterSelfTestOptions options)
    {
        _controller = controller;
        _options = options;
        _runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_hhmmssfff", CultureInfo.InvariantCulture).ToLowerInvariant();
        _logWriter = new MultimasterTestLogWriter(_runId, _options.LogDirectory, "Multimaster Mesh Self-Test Summary");
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
            await _controller.StartAsync(startDynamicUpdates: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            controllerStarted = true;

            var observerBaselineResults = await WaitForBaselineAsync(cancellationToken).ConfigureAwait(false);
            foreach (var result in observerBaselineResults)
            {
                results.Add(result);
                _logWriter.RecordCaseResult(result);
            }

            var brokerBaselineResults = await WaitForBrokerMirrorBaselineAsync(cancellationToken).ConfigureAwait(false);
            foreach (var result in brokerBaselineResults)
            {
                results.Add(result);
                _logWriter.RecordCaseResult(result);
            }

            if (observerBaselineResults.All(result => result.Status == MultimasterSelfTestStatus.Passed)
                && brokerBaselineResults.All(result => result.Status == MultimasterSelfTestStatus.Passed))
            {
                await _controller.StartDynamicUpdatesAsync(cancellationToken).ConfigureAwait(false);
                await AppendResultsAsync(results, RunCrossWriteMatrixAsync, cancellationToken).ConfigureAwait(false);
                await AppendResultsAsync(results, RunRuntimeCreationAsync, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                notes.Add("Baseline visibility failed. Remaining mesh cases were skipped.");
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

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> WaitForBaselineAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var expectedEntry in _controller.InitialValuesByPath.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var testCase = new MultimasterSelfTestCase(
                Category: "baseline",
                Name: $"baseline_{expectedEntry.Key.Replace(".", "_", StringComparison.Ordinal)}",
                ActorNodeId: "system",
                TargetPath: expectedEntry.Key,
                ExpectedValue: expectedEntry.Value,
                Timeout: _options.BaselineTimeout,
                RequiredObserverNodeIds: _controller.NodeIds);

            results.Add(await ExecuteCaseAsync(testCase, action: _ => Task.CompletedTask, cancellationToken: cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> WaitForBrokerMirrorBaselineAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var expectedEntry in _controller.InitialValuesByPath.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var testCase = new MultimasterSelfTestCase(
                Category: "broker_mirror_baseline",
                Name: $"broker_mirror_baseline_{expectedEntry.Key.Replace(".", "_", StringComparison.Ordinal)}",
                ActorNodeId: "system",
                TargetPath: expectedEntry.Key,
                ExpectedValue: expectedEntry.Value,
                Timeout: _options.BaselineTimeout,
                RequiredObserverNodeIds: _controller.NodeIds);

            results.Add(await ExecuteCaseAsync(
                testCase,
                action: _ => Task.CompletedTask,
                cancellationToken: cancellationToken,
                stateProvider: _controller.GetBrokerVisibleStates).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunCrossWriteMatrixAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var actorNodeId in _controller.NodeIds)
        {
            foreach (var targetNode in _controller.Nodes)
            {
                var expectedValue = BuildValue("cross_write", actorNodeId, targetNode.NodeId);
                var testCase = new MultimasterSelfTestCase(
                    Category: "cross_write",
                    Name: $"cross_write_{actorNodeId}_to_{targetNode.NodeId}",
                    ActorNodeId: actorNodeId,
                    TargetPath: targetNode.StaticItemPath,
                    ExpectedValue: expectedValue,
                    Timeout: _options.OperationTimeout,
                    RequiredObserverNodeIds: _controller.NodeIds);

                results.Add(await ExecuteCaseAsync(
                    testCase,
                    action: token => _controller.WriteRemoteValueAsync(actorNodeId, targetNode.StaticItemPath, expectedValue, token),
                    cancellationToken: cancellationToken).ConfigureAwait(false));

                results.Add(await ExecuteCaseAsync(
                    testCase with
                    {
                        Category = "broker_mirror_cross_write",
                        Name = $"broker_mirror_cross_write_{actorNodeId}_to_{targetNode.NodeId}",
                    },
                    action: _ => Task.CompletedTask,
                    cancellationToken: cancellationToken,
                    stateProvider: _controller.GetBrokerVisibleStates).ConfigureAwait(false));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<MultimasterSelfTestResult>> RunRuntimeCreationAsync(CancellationToken cancellationToken)
    {
        var results = new List<MultimasterSelfTestResult>();

        foreach (var entry in _controller.NodeIds.Select((nodeId, index) => (NodeId: nodeId, Index: index + 1)))
        {
            var path = BuildRuntimePath(entry.NodeId, entry.Index);
            var expectedValue = BuildValue("runtime_create", entry.NodeId, entry.NodeId);
            var testCase = new MultimasterSelfTestCase(
                Category: "runtime_create",
                Name: $"runtime_create_{entry.NodeId}",
                ActorNodeId: entry.NodeId,
                TargetPath: path,
                ExpectedValue: expectedValue,
                Timeout: _options.OperationTimeout,
                RequiredObserverNodeIds: _controller.NodeIds);

            results.Add(await ExecuteCaseAsync(
                testCase,
                action: token => _controller.PublishRuntimeItemAsync(entry.NodeId, path, expectedValue, writable: false, token),
                cancellationToken: cancellationToken).ConfigureAwait(false));

            results.Add(await ExecuteCaseAsync(
                testCase with
                {
                    Category = "broker_mirror_runtime_create",
                    Name = $"broker_mirror_runtime_create_{entry.NodeId}",
                },
                action: _ => Task.CompletedTask,
                cancellationToken: cancellationToken,
                stateProvider: _controller.GetBrokerVisibleStates).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<MultimasterSelfTestResult> ExecuteCaseAsync(
        MultimasterSelfTestCase testCase,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        Func<string, IReadOnlyDictionary<string, ObservedItemState>>? stateProvider = null)
    {
        var resolveStates = stateProvider ?? _controller.GetObservedStates;
        var startedUtc = DateTimeOffset.UtcNow;
        var requiredObservers = ResolveRequiredObservers(testCase);
        var lastStates = new Dictionary<string, ObservedItemState>(resolveStates(testCase.TargetPath), StringComparer.OrdinalIgnoreCase);
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
            latestStates = resolveStates(testCase.TargetPath);
            return CreateResult(testCase, MultimasterSelfTestStatus.Blocked, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            notes.Add(exception.Message);
            latestStates = resolveStates(testCase.TargetPath);
            return CreateResult(testCase, MultimasterSelfTestStatus.Failed, startedUtc, stopwatch.Elapsed, latestStates, changeCounts, notes, requiredObservers);
        }

        while (stopwatch.Elapsed < testCase.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestStates = resolveStates(testCase.TargetPath);
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
        latestStates = resolveStates(testCase.TargetPath);
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

    private string BuildRuntimePath(string ownerNodeId, int sequence)
        => $"nodes.{ownerNodeId}.runtime.mesh_created_{sequence:000}_{_runId}";

    private string BuildValue(string category, string actorNodeId, string targetNodeId)
        => $"{_runId}|{category}|{actorNodeId}|{targetNodeId}";
}