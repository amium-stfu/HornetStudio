# Registry Interaction

Registry interaction describes how data becomes visible to the application and how UI-facing systems should work with that state.

## Core Idea

Items do not become generally visible just because a producer created them.
They become visible and updateable for the rest of the application after they are submitted to the host registry path.

## Producer to Host Registry Flow

1. An internal or external producer creates or updates item data.
2. The producer submits that item state to the host registry.
3. The host registry becomes the shared path for visibility, lookup, and updates.
4. UI-facing systems observe or query the registered host state instead of maintaining a competing source of truth.

This keeps the host registry as the central path for shared item visibility.

## Visualization and Updates

If an item should be visualized, attached to widgets, or updated through UI-driven workflows, the relevant systems should work through the host registry path.

That means:

- visualization should use host-registered item state
- update paths should target host-registered items
- UI-specific lookup layers should reflect the host state instead of replacing it

## Target Metadata Convention

Writable or UI-addressable registry targets should publish semantic metadata through a dedicated `type` property.

- `type` describes the target value semantics used for validation, compatible source filtering, and editor behavior.
- `format` remains a separate display-oriented property and should not be used as the primary semantic type.
- Known canonical `type` values are `unknown`, `string`, `bool`, `int`, `long`, `float`, `double`, `decimal`, `epoch`, `bits`, and `object`.
- New targets created or published by HornetStudio runtime code should include `type` when they are intended to be writable.
- Legacy or external targets may still omit `type`; consumers should then fall back to runtime value inference.

For current UDL modules, the channel mapping is:

- `read.type = float`
- `set.type = float`
- `out.type = float`
- `state.type = int`
- `alert.type = int`

UDL client connections themselves are now folder-scoped resources loaded from `Clients/Udl/<client-id>.yaml`. Their status branch stays available below `studio.<folder_name>.<client_id>.status`, while runtime module data remains below `runtime.udl_client.<client_id>` and is not published into the general project tree until attach or publish configuration explicitly requests it.

For file-backed UDL clients, the folder/client-scoped runtime manager now owns that projection headlessly. A visible `UdlClientControl` widget is no longer required for `status.*` items, explicitly attached module roots such as `studio.<folder_name>.<client_id>.m001`, or helper bits projected from file-backed `UdlModuleExposures` to appear in the shared registry.

## Function Registry Relationship

`FunctionRegistry` is not presented here as a child registry owned by items.
It is an independent registry for callable functionality.
Those functions may read from items, write to items, or coordinate workflows that involve items, but that does not make the function registry structurally owned by item instances.

In the editor, this independence is now reflected through an Admin-only Function Browser in the right folder sidebar instead of requiring a normal placeable `Functions` canvas widget for day-to-day function management.

## Handbook Guidance

When documenting workflows elsewhere in the manual:

- describe where producers submit state into the host registry
- describe UI behavior in terms of observing or updating host-registered state
- avoid implying that a UI-only registry is the primary source of truth
- avoid implying that `FunctionRegistry` belongs to item ownership

## Related Technical Sources

- `../data-flow-and-signals.md`
- `../../../HornetStudio.Host/Python/Integration/python-system-overview.md`
