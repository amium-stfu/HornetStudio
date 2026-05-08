CustomSignals

Widget for defining and exposing custom calculated or manual signals.

- Bundles multiple custom signal definitions in one widget
- Useful for derived values and manual helper signals
- Keeps signal creation close to the page context
- Publishes signals below `studio.{FolderName}.{WidgetName}.{SignalName}`
- Supports optional alternate write targets for external direct or request-based backends

Best for:
Project-specific helper values and derived runtime signals.