# CameraControl Widget

## Type

`CameraControl`

## Purpose

Shows frames from a configured camera source inside the editor and runtime surface.

## Typical Use Cases

- Live camera preview
- Visual inspection views
- Overlay display for camera-bound information

## Key Configuration

- Camera name
- Camera resolution
- Optional overlay text
- Standard widget layout and theme properties

## Runtime Notes

The widget subscribes to the selected camera source and updates its displayed frame when new images arrive.

## Source

- `src/Hornetstudio.Editor/Widgets/Camera/`
- `src/Hornetstudio.Editor/Widgets/Camera/EditorCameraControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/CameraControl.help.md`