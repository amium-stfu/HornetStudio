MonitorView

Explicit single-line alarm widget for selected folder monitor rules.

- Reads central monitor rules from `Monitoring/Monitor.yaml`
- Displays only selected rule names from `SelectedMonitorIds`
- Lets the editor pick rules from the folder registry instead of free-text ids
- Ignores duplicate selected rule names
- Optional `OnActiveColor` highlights active rows with a custom color
- Uses the shared folder monitor runtime
- Each selected rule row binds only to its exact rule root path and updates when that root value is published later
- No add, edit, delete, or show-all overview actions

Best for:
Overview or operator pages that should show only important alarms. Use Monitor Browser for management and debug overviews.
