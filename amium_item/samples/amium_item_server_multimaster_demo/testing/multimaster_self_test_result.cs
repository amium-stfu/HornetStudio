namespace Amium.Item.Server.MultimasterDemo.Testing;

internal enum MultimasterSelfTestStatus
{
    Passed,
    Failed,
    Blocked,
}

internal sealed record MultimasterSelfTestResult(
    string Category,
    string Name,
    string ActorNodeId,
    string TargetPath,
    string ExpectedValue,
    MultimasterSelfTestStatus Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> ObservedValues,
    IReadOnlyDictionary<string, int> ObservedUpdateCounts,
    IReadOnlyList<string> MissingObservers,
    IReadOnlyList<string> Notes,
    bool SuspiciousUpdateGrowth,
    bool Required);

internal sealed record MultimasterSelfTestRunResult(
    string RunId,
    bool IsSuccess,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    string JsonLogPath,
    string SummaryPath,
    IReadOnlyList<MultimasterSelfTestResult> Results,
    IReadOnlyList<string> Notes);