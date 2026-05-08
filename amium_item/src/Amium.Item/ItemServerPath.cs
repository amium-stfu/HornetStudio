using Amium.Items;

namespace Amium.Item.Server;

/// <summary>
/// Provides canonical path handling for broker item addressing.
/// </summary>
public static class ItemServerPath
{
    /// <summary>
    /// Gets the reserved subscription token that matches the complete item server path space.
    /// </summary>
    public const string GlobalSubscriptionPath = "*";

    /// <summary>
    /// Normalizes an item path for canonical broker addressing.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path using dot separators.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.Equals(path.Trim(), GlobalSubscriptionPath, StringComparison.Ordinal))
        {
            return GlobalSubscriptionPath;
        }

        return ItemPath.Normalize(path);
    }

    /// <summary>
    /// Determines whether two paths are equal after canonical normalization.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when both paths address the same item.</returns>
    public static bool Equals(string left, string right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a candidate path matches a subscription path.
    /// </summary>
    /// <param name="subscriptionPath">The subscription root path.</param>
    /// <param name="candidatePath">The candidate item path.</param>
    /// <param name="recursive">Whether descendants should match.</param>
    /// <returns><see langword="true"/> when the candidate path is covered by the subscription.</returns>
    public static bool Matches(string subscriptionPath, string candidatePath, bool recursive)
    {
        var normalizedSubscriptionPath = Normalize(subscriptionPath);
        var normalizedCandidatePath = Normalize(candidatePath);

        if (string.Equals(normalizedSubscriptionPath, GlobalSubscriptionPath, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(normalizedSubscriptionPath, normalizedCandidatePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return recursive
            && normalizedCandidatePath.StartsWith(normalizedSubscriptionPath + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a retained path is the same path or a descendant path of a removed root.
    /// </summary>
    /// <param name="rootPath">The removed root path.</param>
    /// <param name="candidatePath">The retained candidate path.</param>
    /// <returns><see langword="true"/> when the candidate should be removed with the root.</returns>
    public static bool IsSelfOrDescendant(string rootPath, string candidatePath)
        => Matches(rootPath, candidatePath, recursive: true);
}