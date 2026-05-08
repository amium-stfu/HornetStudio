namespace Amium.Item.Server.MultimasterDemo.Models;

internal sealed record DemoObservedValue(
    string ValueText,
    DateTimeOffset? LastUpdatedUtc,
    int UpdateCount,
    string SourceClientId,
    bool IsAvailable);

internal sealed record DemoObservedNodeState(
    string NodeId,
    string DisplayName,
    DemoObservedValue DynamicValue,
    DemoObservedValue WriteTestValue);

internal sealed record DemoNodeSnapshot(
    string NodeId,
    string DisplayName,
    bool IsConnected,
    string StatusText,
    double LocalDynamicValue,
    DateTimeOffset? LocalDynamicUpdatedUtc,
    int LocalDynamicSequence,
    string LocalWriteTestValue,
    DateTimeOffset? LocalWriteUpdatedUtc,
    int LocalWriteSequence,
    IReadOnlyList<DemoObservedNodeState> ObservedNodes);