using Amium.Items;

namespace Amium.ItemBroker;

/// <summary>
/// Resolves canonical broker item paths from item instances.
/// </summary>
public static class ItemBrokerItemPath
{
    /// <summary>
    /// Resolves the canonical broker path for an item.
    /// </summary>
    /// <param name="item">The item instance.</param>
    /// <returns>The normalized broker path.</returns>
    public static string Resolve(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemBrokerPath.Normalize(item.Path ?? item.Name ?? throw new ArgumentException("Item must provide a canonical path.", nameof(item)));
    }
}
