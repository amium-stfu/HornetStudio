# UdlClientControl Help

## Widget Type

`UdlClientControl`

## Overview

The UdlClientControl widget connects to a configured UDL endpoint, presents connection state, and publishes runtime status, attached item information, and optional UdlClient-owned helper items for configured module channels.

The widget body shows a module list. Each module row has its own `Edit` action so exposure rules are configured module-by-module instead of through one global button, and a `Delete` action to remove persisted helper definitions for that module after confirmation. Socket and status information are shown in the footer.

## Properties

### UdlClientHost

Configured host name or address.

### UdlClientPort

Configured endpoint port.

### UdlClientAutoConnect

Controls whether the client should connect automatically.

### UdlClientDebugLogging

Enables additional diagnostic logging.

### UdlAttachedItemPaths

Stores configured attached runtime items.

### UdlDemoModuleDefinitions

Stores configured demo module definitions.

For set-driven demo modules, `Set.write` is the requested setpoint. `Set.read` mirrors that requested setpoint, and the simulated feedback value is published on `Read.read`.

### UdlModuleExposureDefinitions

Stores configured module/channel exposure rules owned by the UdlClient widget.

### SocketText / ConnectionStateText / AutoConnectText / ItemCountText / ModuleCountText

Presentation-facing status values shown by the control.

## Functions and Behavior

### Connect and disconnect

The widget manages the connection lifecycle against the configured endpoint.

### Publish status values

Connection and item-related status values are published into canonical snake_case runtime paths.

- Runtime base: `runtime.udl_client.<client_name>`
- Status base: `studio.<folder_name>.<client_name>.status`
- Attach-option discovery base: `studio.<folder_name>.<client_name>.status.attach_options`

Newly published status leaf items use snake_case names such as `endpoint`, `connection`, `item_count`, `message_counter`, and `auto_connect`.

The widget still reads legacy mixed-case UDL paths such as `runtime.UdlClient.<client_name>` and `...Status.AttachOptions` so older persisted data can continue to resolve.

### Publish module exposures

Configured module exposures extend the matching UDL runtime channels with `Bits.Bit0...BitN` helper items and can also apply helper-bit routing rules such as `Read helper bits route to Set`.

The inline module editor filters the dialog to the selected module and merges the result back into the stored global definition list.

The dialog can already contain additional fields for later source parameterization, but in the current first step the runtime-active options are `Publish Bits`, the stored bit count, and the `Read helper bits route to Set` rule.

The module-scoped editor is grouped into `Main`, `Bitmask`, `Settings`, and `Adjust`. The bitmask section currently focuses on the operational helper rows `Read / Set` and `Alert`, contains the helper-bit rule `Read helper bits route to Set`, and always exposes the `Publish Bits` switch plus an editable `Count`, so publishing no longer depends on selecting a format inside the dialog.

Common bitmask channels start with a suggested default count of `4` so the first setup step stays short. If `Read helper bits route to Set` is enabled, writes that originate from published `Read` helper bits are redirected to the module `Set` channel instead of writing back into `Read` directly. New UDL channels write through the flat `write` property on the channel item itself.

The inline module delete action removes all persisted exposure definitions for the selected module. Runtime-only modules stay visible while they are available at runtime, but the delete action is only enabled when persisted helper configuration exists.

For bit formats `b4`, `b8`, and `b16`, the widget can publish `Bits.Bit0...BitN` helper items as bool values.

When a UdlClient runtime path is attached to the page, those `Bits` children appear under the attached project path as part of the normal mirrored channel tree. This keeps signal target selection on the UdlClient path instead of introducing a separate visible `UdlClientRuntime` helper branch.

Bit clicks on those published helper items update the helper value directly and no longer trigger a full attached-item republish cycle unless the exposure structure itself changed.

Writes to those bool helper items are converted back into the underlying channel `write` property, with legacy `Request` routing kept only as a compatibility fallback.

During that writeback, the widget preserves the numeric value type of the underlying runtime channel so floating-point request values remain floating-point instead of switching to an integer mask type.

Published helper bits always use `Format=bool` for their value handling. General presentation overrides for raw UDL channels are intentionally not applied in this first-step scope so raw values stay raw.

### Synchronize attached items

The widget can keep attached item state aligned with runtime data.

`Received Items` stays driven by live runtime root modules only. When a module is attached, the row remains visible, the status indicator turns green, and the primary action changes from `Attach` to a disabled `Attached` state.

`Attached Items` stays driven by persisted `UdlAttachedItemPaths`. Attached module trees and generated helper bits are available in signal source pickers, while widget status roots are excluded from those signal source options.

Attached-state checks and the module-scoped `Edit` dialog resolve runtime modules and channels from both the active client item tree and the published UDL runtime snapshots. This preserves green attached indicators and bitmask channel choices when the canonical runtime registry branch is the available source.

### Monitor runtime state

Background monitoring updates visible status and connection state.

## Runtime Notes

Published runtime status items include endpoint, connection, item count, message counter, and auto-connect state.

Published exposure helper items are removed automatically when the client disconnects, the format is no longer compatible, or the exposure rule is disabled.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/UdlClientControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/UdlClientControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs`
