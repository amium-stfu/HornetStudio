# DEBUG REPORT

## Problem
BrokerWidget internal attach-option snapshots duplicate the option path in `Status.AttachOptions`.

## Expected Behavior
The attach option `BrokerWidget1.Mqtt.Edm1.Pressure` should be registered under `Studio.Folder1.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Pressure`, while the received live item should be exposed as `Studio.Folder1.BrokerWidget1.Mqtt.Edm1.Pressure`.

## Actual Behavior
The attach option was registered as `Studio.Folder1.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Pressure.BrokerWidget1.Mqtt.Edm1.Pressure`, because the full option path was passed as the `Item` constructor parent path and the option name was appended a second time.

## Error Messages / Logs
- `[DataRegistry] Added key="Studio.Folder1.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Pressure.BrokerWidget1.Mqtt.Edm1.Pressure" itemPath="Studio.Folder1.BrokerWidget1.Status.AttachOptions.BrokerWidget1.Mqtt.Edm1.Pressure.BrokerWidget1.Mqtt.Edm1.Pressure" name="BrokerWidget1.Mqtt.Edm1.Pressure"`
- UI row indicates `saved attachment | no helper items configured` and `Saved attachment is not currently live.`

## Relevant Code
- `src/HornetStudio.Editor/Widgets/Broker/BrokerClientControl.axaml.cs`: builds received MQTT runtime paths and persisted attach identities.
- `src/HornetStudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs`: reference implementation for attach-option snapshot construction.
- `src/HornetStudio.Editor/ViewModels/EditorDialogField.cs`: displays live and missing BrokerWidget attach identities.
- `src/HornetStudio.Editor/Helpers/TargetPathHelper.cs`: normalizes legacy and visible broker attach paths.

## Attempted Fixes
- Current fix: collapse nested `<WidgetName>.Mqtt...` fragments while building BrokerWidget received runtime paths and attach identities.
- Current fix: update `TargetPathHelper.ToBrokerReceivedAttachIdentity(...)` so any prefix before `<WidgetName>.Mqtt...` is stripped during attach-option normalization.
- Current fix: align BrokerWidget attach-option snapshot creation with UdlClient by constructing `new Item(option, path: attachOptionsBasePath)` instead of passing the full option path as the parent path.
- Current fix: add editor tests for runtime path, attach identity, and central attach normalization.

## Current Hypothesis
- The immediate duplicate `Status.AttachOptions...BrokerWidget1.Mqtt...BrokerWidget1.Mqtt...` key was caused by incorrect `Item` constructor usage in BrokerWidget, not by DataRegistry key filtering.
