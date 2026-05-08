namespace Amium.Item.Server.MultimasterDemo.Testing;

internal sealed record MultimasterSelfTestOptions(
    TimeSpan BaselineTimeout,
    TimeSpan OperationTimeout,
    TimeSpan PollInterval,
    int SuspiciousUpdateGrowthThreshold,
    string LogDirectory)
{
    internal static MultimasterSelfTestOptions CreateDefault()
        => new(
            BaselineTimeout: TimeSpan.FromSeconds(20),
            OperationTimeout: TimeSpan.FromSeconds(12),
            PollInterval: TimeSpan.FromMilliseconds(150),
            SuspiciousUpdateGrowthThreshold: 8,
            LogDirectory: ResolveDefaultLogDirectory("multimaster_self_test"));

    internal static MultimasterSelfTestOptions CreateMeshDefault()
        => new(
            BaselineTimeout: TimeSpan.FromSeconds(20),
            OperationTimeout: TimeSpan.FromSeconds(12),
            PollInterval: TimeSpan.FromMilliseconds(150),
            SuspiciousUpdateGrowthThreshold: 8,
            LogDirectory: ResolveDefaultLogDirectory("multimaster_mesh_self_test"));

    private static string ResolveDefaultLogDirectory(string directoryName)
    {
        foreach (var startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var workspaceRoot = TryFindWorkspaceRoot(startDirectory);
            if (workspaceRoot is not null)
            {
                return Path.Combine(workspaceRoot, "logs", directoryName);
            }
        }

        return Path.Combine(Environment.CurrentDirectory, "logs", directoryName);
    }

    private static string? TryFindWorkspaceRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HornetStudio.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}