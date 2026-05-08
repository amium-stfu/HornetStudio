# CameraControl Help

## Widget Type

`CameraControl`

## Overview

The CameraControl widget subscribes to a configured camera source and displays incoming frames in the page.

## Properties

### CameraName

Defines which registered camera source should be used.

### CameraResolution

Stores the desired camera resolution setting used by the widget.

### CameraOverlayText

Optional overlay text associated with the camera display.

### Name

When the name changes, the widget can align its caption accordingly.

## Functions and Behavior

### Update camera subscription

The widget unsubscribes from the previous camera source and subscribes to the currently selected one.

### Apply camera resolution

When the configured resolution changes, the widget applies the requested camera sizing behavior.

### Receive frames

New frames are pushed into the current bitmap displayed by the control.

## Runtime Notes

Camera subscription is bound to the current model and updates whenever the configured camera source changes.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/CameraControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/CameraControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Camera/EditorCameraControl.axaml.cs`