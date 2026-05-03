using System;

namespace Amium.Items;

/// <summary>
/// Provides helpers for updating item paths recursively.
/// </summary>
public static class ItemPathExtensions
{
    /// <summary>
    /// Rewrites the path metadata of the specified item and all descendants.
    /// </summary>
    /// <param name="item">The root item whose paths should be updated.</param>
    /// <param name="absolutePath">The new absolute path to assign.</param>
    /// <returns>The same <see cref="Item"/> instance for fluent usage.</returns>
    public static Item Repath(this Item item, string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        ApplyPath(item, NormalizePath(absolutePath));
        return item;
    }

    private static void ApplyPath(Item item, string absolutePath)
    {
        item._path = absolutePath;
        item.Params["Name"].Value = GetLastSegment(absolutePath);
        item.Params["Path"].Value = absolutePath;

        foreach (var parameterEntry in item.Params.Dictionary)
        {
            parameterEntry.Value.Path = $"{absolutePath}.{parameterEntry.Key}";
        }

        foreach (var childEntry in item.Dictionary)
        {
            var child = childEntry.Value;
            var childName = child.Name ?? childEntry.Key;
            ApplyPath(child, $"{absolutePath}.{childName}");
        }
    }

    private static string NormalizePath(string value)
        => value.Replace('\\', '.').Replace('/', '.').Trim('.');

    private static string GetLastSegment(string path)
    {
        var lastSeparatorIndex = Math.Max(path.LastIndexOf('/'), Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('.')));
        return lastSeparatorIndex >= 0 ? path[(lastSeparatorIndex + 1)..] : path;
    }
}
