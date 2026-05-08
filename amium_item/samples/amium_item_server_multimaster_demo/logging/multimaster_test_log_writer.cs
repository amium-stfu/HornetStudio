using System.Text;
using System.Text.Json;
using Amium.Item.Server.MultimasterDemo.Models;
using Amium.Item.Server.MultimasterDemo.Testing;

namespace Amium.Item.Server.MultimasterDemo.Logging;

internal sealed class MultimasterTestLogWriter
{
    private readonly object _sync = new();
    private readonly string _runId;
    private readonly string _logDirectory;
    private readonly string _summaryTitle;
    private readonly List<object> _entries = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    internal MultimasterTestLogWriter(string runId, string logDirectory, string summaryTitle = "Multimaster Self-Test Summary")
    {
        _runId = runId;
        _logDirectory = logDirectory;
        _summaryTitle = summaryTitle;
    }

    internal void RecordRunStarted(DateTimeOffset startedUtc, string endpointSummary, IReadOnlyList<string> nodeIds)
        => Record("run_started", new
        {
            runId = _runId,
            startedUtc,
            endpointSummary,
            nodeIds,
        });

    internal void RecordNodeEvent(DemoNodeEvent nodeEvent)
        => Record("node_event", new
        {
            nodeEvent.NodeId,
            nodeEvent.DisplayName,
            nodeEvent.TimestampUtc,
            nodeEvent.Message,
        });

    internal void RecordCaseResult(MultimasterSelfTestResult result)
        => Record("case_result", new
        {
            result.Category,
            result.Name,
            Status = result.Status.ToString(),
            result.ActorNodeId,
            result.TargetPath,
            result.ExpectedValue,
            durationMs = (long)result.Duration.TotalMilliseconds,
            result.ObservedValues,
            result.ObservedUpdateCounts,
            result.MissingObservers,
            result.Notes,
            result.SuspiciousUpdateGrowth,
            result.Required,
        });

    internal void RecordNote(string note)
        => Record("note", new { note });

    internal void RecordFatal(Exception exception)
        => Record("fatal", new
        {
            message = exception.Message,
            exception = exception.ToString(),
        });

    internal void RecordRunCompleted(MultimasterSelfTestRunResult result)
        => Record("run_completed", new
        {
            result.RunId,
            result.IsSuccess,
            result.StartedUtc,
            result.EndedUtc,
            durationMs = (long)(result.EndedUtc - result.StartedUtc).TotalMilliseconds,
            total = result.Results.Count,
            passed = result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Passed),
            failed = result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Failed),
            blocked = result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Blocked),
            result.Notes,
        });

    internal async Task<(string JsonLogPath, string SummaryPath)> WriteAsync(
        MultimasterSelfTestRunResult result,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_logDirectory);

        var timestamp = result.StartedUtc.ToString("yyyyMMdd_HHmmss");
        var baseFileName = $"{timestamp}_{_runId}";
        var jsonLogPath = Path.Combine(_logDirectory, $"{baseFileName}.jsonl");
        var summaryPath = Path.Combine(_logDirectory, $"{baseFileName}_summary.md");

        List<object> entries;
        lock (_sync)
        {
            entries = new List<object>(_entries);
        }

        await using (var stream = new FileStream(jsonLogPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, _jsonOptions)).ConfigureAwait(false);
            }
        }

        await File.WriteAllTextAsync(
            path: summaryPath,
            contents: BuildSummary(result, jsonLogPath),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        File.Copy(jsonLogPath, Path.Combine(_logDirectory, "latest.jsonl"), overwrite: true);
        File.Copy(summaryPath, Path.Combine(_logDirectory, "latest_summary.md"), overwrite: true);

        return (jsonLogPath, summaryPath);
    }

    private void Record(string entryType, object payload)
    {
        lock (_sync)
        {
            _entries.Add(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                entryType,
                payload,
            });
        }
    }

    private string BuildSummary(MultimasterSelfTestRunResult result, string jsonLogPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {_summaryTitle}");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{Escape(result.RunId)}`");
        builder.AppendLine($"- Status: `{(result.IsSuccess ? "passed" : "failed")}`");
        builder.AppendLine($"- Started (UTC): `{result.StartedUtc:O}`");
        builder.AppendLine($"- Ended (UTC): `{result.EndedUtc:O}`");
        builder.AppendLine($"- Duration: `{(result.EndedUtc - result.StartedUtc).TotalSeconds:F2}s`");
        builder.AppendLine($"- JSONL log: `{Escape(jsonLogPath)}`");
        builder.AppendLine($"- Total cases: `{result.Results.Count}`");
        builder.AppendLine($"- Passed: `{result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Passed)}`");
        builder.AppendLine($"- Failed: `{result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Failed)}`");
        builder.AppendLine($"- Blocked: `{result.Results.Count(item => item.Status == MultimasterSelfTestStatus.Blocked)}`");
        builder.AppendLine();

        if (result.Notes.Count > 0)
        {
            builder.AppendLine("## Run Notes");
            builder.AppendLine();
            foreach (var note in result.Notes)
            {
                builder.AppendLine($"- {Escape(note)}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Case Results");
        builder.AppendLine();

        foreach (var caseResult in result.Results)
        {
            builder.AppendLine($"### {Escape(caseResult.Name)}");
            builder.AppendLine();
            builder.AppendLine($"- Category: `{Escape(caseResult.Category)}`");
            builder.AppendLine($"- Status: `{caseResult.Status}`");
            builder.AppendLine($"- Actor: `{Escape(caseResult.ActorNodeId)}`");
            builder.AppendLine($"- Target path: `{Escape(caseResult.TargetPath)}`");
            builder.AppendLine($"- Expected value: `{Escape(caseResult.ExpectedValue)}`");
            builder.AppendLine($"- Duration: `{caseResult.Duration.TotalMilliseconds:F0} ms`");
            builder.AppendLine($"- Suspicious update growth: `{caseResult.SuspiciousUpdateGrowth}`");

            if (caseResult.MissingObservers.Count > 0)
            {
                builder.AppendLine($"- Missing observers: `{Escape(string.Join(", ", caseResult.MissingObservers))}`");
            }

            if (caseResult.Notes.Count > 0)
            {
                builder.AppendLine("- Notes:");
                foreach (var note in caseResult.Notes)
                {
                    builder.AppendLine($"  - {Escape(note)}");
                }
            }

            builder.AppendLine("- Observed values:");
            foreach (var observed in caseResult.ObservedValues.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var updateCount = caseResult.ObservedUpdateCounts.TryGetValue(observed.Key, out var value) ? value : 0;
                builder.AppendLine($"  - {Escape(observed.Key)}: `{Escape(observed.Value)}` (changes during wait: `{updateCount}`)");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Escape(string value)
        => value.Replace("`", "'", StringComparison.Ordinal);
}