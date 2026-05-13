UdlClientControl

Connection widget for UDL communication with an inline module list and module-scoped helper exposure editing.

- Manages host, port, and connection state
- Lists runtime or persisted modules directly in the widget
- Opens exposure editing per module with an `Edit` button and can remove persisted module helper configuration with `Delete`
- Can publish bitmask channels as bool helper items directly on the affected runtime channels; current runtime scope is focused on `Publish Bits`, the stored bit count, and the helper-bit rule `Read helper bits route to Set`
- Updates published helper bit values without rebuilding the attached UdlClient mirror on every bit click
- Shows module-scoped exposure editing in grouped `Main`, `Bitmask`, `Settings`, and `Adjust` areas, with the bitmask area focused on `Read / Set` and `Alert` as the active first-step workflow
- Suggests default counts for common bitmask channels and can route published `Read` helper bits to `Set`, with new UDL writes flowing through flat channel `write` properties
- Publishes new runtime and status branches as `runtime.udl_client.<client_name>` and `studio.<folder_name>.<client_name>.status`, while still discovering legacy mixed-case UDL paths for compatibility
- Resolves attached status and module edit channels from both live client items and published UDL runtime snapshots so attached modules stay editable against the canonical runtime branch

Best for:
Projects that need direct UDL connectivity and module-level helper exposure management in the editor UI.