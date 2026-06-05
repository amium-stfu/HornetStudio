# UdlClientControl Widget

## Type

`UdlClientControl`

## Purpose

Observes and manages a folder-level UDL client definition, shows connection state, lists discovered or persisted modules inline, and can publish UdlClient-owned helper items for selected module channels.

For folder-wide management without placing a widget, the Admin `Browser` area now includes an `UDL Clients` browser entry. That browser reads the same file-backed definitions from `Clients/Udl/<client-id>.yaml`, provides the two-pane list/detail workflow, and uses the same folder-scoped runtime manager as the widget.

## Typical Use Cases

- Monitor UDL connectivity
- Open the Admin `Browser` -> `UDL Clients` dialog to inspect or edit all folder-local UDL clients from one place
- Review modules directly inside the widget and edit one module at a time
- Attach runtime items to a project page
- Observe the headless connection and item-count status projection in the registry
- Publish bit helper items for selected bitmask channels directly from the UdlClient

## Key Configuration

- Client definitions are stored in `Clients/Udl/<client-id>.yaml`, with the file name as the technical client id and `Text` as the optional display label
- Host and port
- Auto-connect behavior
- Debug logging
- Attached item paths and demo modules
- Optional `UdlModuleExposures` definitions in the same client YAML for bitmask-oriented helper items; in the current first step the runtime-active options are `Publish Bits`, the explicit bit count, and the helper-bit rule `Read helper bits route to Set`
- Per-module actions through the inline module list `Edit` and `Delete` buttons

Set-driven demo modules treat `Set.write` as the requested setpoint, mirror it on `Set.read`, and publish the simulated process feedback on `Read.read`.

## Runtime Notes

The long-running UDL runtime is folder-scoped and survives browser, tab, and visual attach changes. The widget is a management and observation surface; it no longer owns the runtime lifetime.

For file-backed clients from `Clients/Udl/<client-id>.yaml`, registry publication is also folder-scoped. Status items and explicitly attached module roots are projected by the runtime manager even when no `UdlClientControl` is visible. The widget can still manage or inspect that runtime, but it is no longer the required publisher.

The widget body shows a module list similar to EnhancedSignals. Each row can open a module-scoped exposure editor or remove the persisted helper configuration for that module, while socket and runtime status stay in the widget footer. The Admin `ClientsBrowser` uses the same file-backed module exposure data and can edit it even when no widget instance exists in the folder.

The attach area separates live runtime discovery from persisted page attachments. `Received Items` is built only from live runtime root modules and stays stable after attach or detach. Attaching a received module keeps the row visible, switches its dot from gray to green, and disables the row action. `Attached Items` is built from persisted `UdlAttachedItemPaths` and reports whether each saved path currently resolves to a live runtime item.

Attached-row status checks and module-scoped `Edit` channel discovery resolve against both the current live client item tree and the published UDL runtime snapshots below `runtime.udl_client.<client_name>`. This keeps attached-state indicators and bitmask channel choices available even when the runtime source is reached through the canonical runtime registry branch.

When a single module is edited from that list, the exposure dialog is organized into `Main`, `Bitmask`, `Settings`, and `Adjust` sections. `Main` shows the module identity, `Bitmask` exposes one helper row per bit-capable channel such as `Read`, `Set`, `Alert`, or other detected bit channels, and `Settings` plus `Adjust` are prepared as follow-up areas for later source parameterization. The `Publish Bits` switch stays visible even without format editing, the amount of helper items is controlled directly through the stored `Count` value for each channel, and the helper-bit rule `Read helper bits route to Set` stays scoped to this bitmask area.

Common bitmask channels such as `Read`, `Set`, `State`, and `Alert` receive a suggested default count of `4` when no explicit count is stored yet. The helper-bit option `Read helper bits route to Set` redirects writes from published `Read` helper bits to the module `Set` channel. New UDL runtime channels write through their flat `write` property directly on the channel item.

The runtime/projection layer now publishes canonical snake_case runtime and status paths. Runtime items live below `runtime.udl_client.<client_name>`, status items live below `studio.<folder_name>.<client_name>.status`, and attach-option discovery lives below `studio.<folder_name>.<client_name>.status.attach_options`.

Status items such as `endpoint`, `connection`, `item_count`, `message_counter`, and `auto_connect` are published on that snake_case status branch. For configured module/channel exposures, the folder-scoped runtime projection adds `Bits.Bit0...BitN` helper items directly to the matching runtime channel, so attached UdlClient paths expose those bool helper items naturally inside the project tree even without a visible widget.

Signal source pickers can select attached UDL module paths and generated helper bits from those attached trees. Widget status paths stay out of those signal source option lists.

For discovery and migration, the widget still tolerates legacy mixed-case branches such as `runtime.UdlClient.<client_name>` and `...Status.AttachOptions`, but newly published UDL items use the canonical snake_case paths only.

Runtime bit value updates on those published helper items are kept separate from structural exposure changes. Toggling a helper bit updates the mirrored value without republishing the full attached UdlClient subtree on every click.

When helper bits write back into a numeric runtime channel, the UdlClient preserves the target channel value type so request-oriented channels that use floating-point values keep their original runtime type.

The exposure dialog may already show additional fields for future source parameterization, but in the current first step the active runtime behavior is intentionally limited to publishing bitmask helpers, deriving their bit count, and optionally routing `Read` input to `Set`.

## Source

- `src/Hornetstudio.Editor/Widgets/UdlClient/`
- `src/Hornetstudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/UdlClientControl.help.md`
