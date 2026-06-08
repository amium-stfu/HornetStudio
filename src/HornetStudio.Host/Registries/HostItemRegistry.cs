using Amium.Items;

namespace HornetStudio.Host.Registries;

public sealed class HostItemRegistry
{
    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Paths
        => _items.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyCollection<Item> Items
        => _items.Values;

    public void Register(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(NormalizePath(item.Path), item);
    }

    public bool Remove(string path)
        => _items.Remove(NormalizePath(path));

    public bool TryGetItem(string path, out Item? item)
        => _items.TryGetValue(NormalizePath(path), out item);

    public bool TryRead(string path, out object? value)
    {
        value = null;

        if (!_items.TryGetValue(NormalizePath(path), out var item))
        {
            return false;
        }

        value = item.Value;
        return true;
    }

    public bool TryWrite(string path, object? value)
    {
        if (!_items.TryGetValue(NormalizePath(path), out var item))
        {
            return false;
        }
        item.Value = value;
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The host item path must not be empty.", nameof(path));
        }

        return path.Trim();
    }
}
