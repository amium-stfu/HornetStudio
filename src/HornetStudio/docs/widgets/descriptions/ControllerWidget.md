ControllerWidget

PID controller widget for configured runtime control loops.

- Adds, edits, and deletes PID controller definitions
- Keeps `Source` as an external readable picker and `Out` as an external writable picker
- Publishes an owned direct-value `set` item on each controller runtime for the PID setpoint
- Starts and stops each controller through the direct value of `run`
- Applies scaling, clamping, and guarded numeric validation
- Shows PID editor tooltips for parameter meaning and runtime path fields
- Supports file-backed `Controllers/*.yaml` definitions managed via the Admin Controllers browser

Best for:
Folder-level PID control loops that need runtime visibility, direct runtime start/stop control, and a controller-owned setpoint item.
