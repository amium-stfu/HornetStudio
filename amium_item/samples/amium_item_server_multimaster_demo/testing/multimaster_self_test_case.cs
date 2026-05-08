namespace Amium.Item.Server.MultimasterDemo.Testing;

internal sealed record MultimasterSelfTestCase(
    string Category,
    string Name,
    string ActorNodeId,
    string TargetPath,
    string ExpectedValue,
    TimeSpan Timeout,
    bool Required = true,
    IReadOnlyList<string>? RequiredObserverNodeIds = null);