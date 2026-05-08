# Amium.Item

`Amium.Item` provides a lightweight hierarchical item model with named item properties, child items, JSON serialization helpers, cloning helpers, and path normalization utilities.

## Included Types

- `Item` represents a node in the hierarchy and exposes `Name`, `Path`, `Value`, child items, and item properties. `Value` reads from the `read` property and writes to `write` when that channel exists.
- `ItemProperty` stores a named value together with its path and last update timestamp.
- `ItemDictionary` and `ItemPropertyDictionary` provide keyed access to child items and item properties.
- `ItemExtension` contains JSON serialization, JSON deserialization, and cloning helpers.
- `ItemPathExtensions` provides recursive path rewriting through `Repath`.

## Typical Usage

```csharp
var root = new Item("Root");
root["Motor"].Properties["Speed"].Value = 1200;
root["Motor"].Properties["Enabled"].Value = true;

string json = root.ToJsonString();
Item clone = root.Clone();
clone.Repath("Plant.Line1.Root");
```

## Notes

- Item paths are normalized to dot-separated segments.
- New items contain a `read` property by default and only contain a `write` property when constructed with `hasWriteChannel: true`.
- Item properties raise `Changed` when their value changes.
- Items forward property changes through the `Item.Changed` event.
- JSON payloads include the property name, value, last update timestamp, and runtime type.
