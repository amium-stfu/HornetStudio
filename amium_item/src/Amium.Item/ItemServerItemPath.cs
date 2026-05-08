using ItemModel = Amium.Items.Item;

namespace Amium.Item.Server;

/// <summary>
/// Resolves canonical broker item paths from item instances.
/// </summary>
public static class ItemServerItemPath
{
    /// <summary>
    /// Resolves the canonical broker path for an item.
    /// </summary>
    /// <param name="item">The item instance.</param>
    /// <returns>The normalized broker path.</returns>
    public static string Resolve(ItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemServerPath.Normalize(item.Path ?? item.Name ?? throw new ArgumentException("ItemModel must provide a canonical path.", nameof(item)));
    }
}