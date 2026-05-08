# EnhancedSignals Widget

## Type

`EnhancedSignals`

## Purpose

Provides structured enhanced signal definitions and publishes them as runtime-accessible signals.

## Typical Use Cases

- Grouped signal definitions
- Richer signal configuration than legacy filtered signals
- Runtime-accessible signal publishing

## Key Configuration

- Enhanced signal definitions stored on the widget
- Signal naming and path configuration
- Theme-aware signal row presentation
- Optional write routing using `IsWritable`, `WriteMode`, and `WritePath`

## Runtime Notes

The widget rebuilds and refreshes published runtimes when the stored definitions or naming context change.
Published enhanced signals can expose a default request-based write endpoint through their internal `Set` channel or a custom configured target path.

## Source

- `src/Hornetstudio.Editor/Widgets/EnhancedSignals/`
- `src/Hornetstudio.Editor/Widgets/EnhancedSignals/EnhancedSignalsControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/EnhancedSignals.help.md`