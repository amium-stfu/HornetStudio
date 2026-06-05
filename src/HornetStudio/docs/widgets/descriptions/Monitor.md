Monitor

Rule-based monitoring widget for item values and states.

- Browses and edits folder-local monitor rules from `Monitoring/Monitor.yaml`
- Uses a folder-level background runtime that stays active independently from the Monitor browser UI
- Watches timeouts, thresholds, and custom boolean formulas
- Publishes runtime status entries per rule even without a placed Monitor widget
- Shows configured rules as a compact severity-sorted event list with active rows emphasized by severity-colored styling
- Can execute transition-based actions such as `WriteLog`
- Publishes aggregate active event-id lists per severity or log level
- Each rule owns its own cadence and monitoring settings
- Custom mode uses variable pickers and formula buttons similar to the Custom Signal editor

Best for:
Pages that need visible runtime supervision beyond a single displayed signal.
