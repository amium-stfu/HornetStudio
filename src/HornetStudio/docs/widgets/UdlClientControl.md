# UdlClientControl Widget

## Type

`UdlClientControl`

## Purpose

Connects to a UDL endpoint, shows connection state, lists discovered or persisted modules inline, and can publish UdlClient-owned helper items for selected module channels.

## Typical Use Cases

- Monitor UDL connectivity
- Review modules directly inside the widget and edit one module at a time
- Attach runtime items to a project page
- Expose connection and item-count status to the registry
- Publish bit helper items for selected bitmask channels directly from the UdlClient

## Key Configuration

- Host and port
- Auto-connect behavior
- Debug logging
- Attached item paths and demo modules
- Optional module exposure definitions for bitmask-oriented helper items; in the current first step the runtime-active options are `Publish Bits`, the explicit bit count, and the helper-bit rule `Read helper bits route to Set`
- Per-module actions through the inline module list `Edit` and `Delete` buttons

## Runtime Notes

The widget body shows a module list similar to EnhancedSignals. Each row can open a module-scoped exposure editor or remove the persisted helper configuration for that module, while socket and runtime status stay in the widget footer.

When a single module is edited from that list, the exposure dialog is organized into `Main`, `Bitmask`, `Settings`, and `Adjust` sections. `Main` shows the module identity, `Bitmask` is the active area for helper toggles such as `Read / Set` and `Alert`, and `Settings` plus `Adjust` are prepared as follow-up areas for later source parameterization. The `Publish Bits` switch stays visible even without format editing, the amount of helper items is controlled directly through the stored `Count` value, and the helper-bit rule `Read helper bits route to Set` stays scoped to this bitmask area.

Common bitmask channels such as `Read`, `Set`, `State`, and `Alert` receive a suggested default count of `4` when no explicit count is stored yet. The helper-bit option `Read helper bits route to Set` redirects writes from published `Read` helper bits to the module `Set` channel. New UDL runtime channels write through their flat `write` property directly on the channel item.

The widget now publishes new runtime and status paths in canonical snake_case form. Runtime items live below `runtime.udl_client.<client_name>`, status items live below `studio.<folder_name>.<client_name>.status`, and attach-option discovery lives below `studio.<folder_name>.<client_name>.status.attach_options`.

Status items such as `endpoint`, `connection`, `item_count`, `message_counter`, and `auto_connect` are published on that snake_case status branch. For configured module/channel exposures the widget adds `Bits.Bit0...BitN` helper items directly to the matching runtime channel, so attached UdlClient paths expose those bool helper items naturally inside the project tree.

For discovery and migration, the widget still tolerates legacy mixed-case branches such as `runtime.UdlClient.<client_name>` and `...Status.AttachOptions`, but newly published UDL items use the canonical snake_case paths only.

Runtime bit value updates on those published helper items are kept separate from structural exposure changes. Toggling a helper bit updates the mirrored value without republishing the full attached UdlClient subtree on every click.

When helper bits write back into a numeric runtime channel, the UdlClient preserves the target channel value type so request-oriented channels that use floating-point values keep their original runtime type.

The exposure dialog may already show additional fields for future source parameterization, but in the current first step the active runtime behavior is intentionally limited to publishing bitmask helpers, deriving their bit count, and optionally routing `Read` input to `Set`.

## Source

- `src/Hornetstudio.Editor/Widgets/UdlClient/`
- `src/Hornetstudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/UdlClientControl.help.md`