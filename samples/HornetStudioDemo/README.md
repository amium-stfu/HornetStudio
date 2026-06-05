# HornetStudio Demo Project

This sample project is the canonical repository-local demo used for manual QA, loader verification, and architecture migration work. It demonstrates the target resource architecture with clear naming and stable resource roots.

## Open the demo

1. Start HornetStudio.
2. Open `samples/HornetStudioDemo/project.aaep`.
3. Load the `main` folder and review the demo screens, workflows, and Python sample applications.

## Naming contract

All fachliche resources use predictable, folder-relative path roots:

| Root | Purpose |
|---|---|
| `enhanced_signals.*` | File-backed enhanced/filtered signals (`Signals/Enhanced/`) |
| `custom_signals.*` | File-backed custom input signals (`Signals/Custom/`) |
| `controllers.*` | File-backed controllers (`Controllers/`) once loader is ready |
| `applications.python.*` | Python application values (`Applications/Python/`) |
| `studio.main.logs.*` | Log targets referenced by full path in workflows |

**Intentionally avoided as resource roots:**
- Widget instance names (`application_explorer_1.*`, `custom_signals_browser.*`, `custom_signals_1.*`)
- Generated names (`dummy_*`, `inputt_*`, `button_1` through `button_9`)

## Resource layout

```
Folders/main/
  Folder.yaml                       UI layout only — no resource definitions
  Signals/Enhanced/
    filtered_1.yaml                 EnhancedSignal: source udl1.m001, Kalman filter
  Signals/Custom/
    demo_trigger.yaml               Boolean toggle — used by monitor and workflow
    demo_text.yaml                  Text input — append-text demo
    demo_number.yaml                Number input — setpoint override demo
  Controllers/
    pid_controller_1.yaml           PID controller example (file-backed)
  Scripts/Workflows/
    demo_workflow.yaml              Function with SetValue, Log, Delay, IfThenElse, While
  Applications/Python/
    raw/                            Python app: registers and publishes raw_a/b/c values
```

## What the demo contains

- A sanitized `project.aaep` file with empty credential values.
- A `Folders/main/Folder.yaml` with UI layout only; no embedded resource definitions.
- File-backed enhanced signal under `Signals/Enhanced/filtered_1.yaml`.
- File-backed custom signals under `Signals/Custom/` with stable snake_case names.
- A PID controller example under `Controllers/pid_controller_1.yaml`.
- A demo workflow under `Scripts/Workflows/demo_workflow.yaml`.
- A Python application under `Applications/Python/raw/`.

## Maintenance rules

- Keep all paths portable and repository-relative.
- Do not commit runtime logs, local caches, virtual environments, or generated Python bytecode.
- Do not commit secrets, real credentials, tokens, or machine-specific addresses.
- Keep resource files under their respective directories and out of `Folder.yaml`.
- Do not use widget instance names as fachliche resource roots.
- When the concept of a resource type moves to file-backed loading, update this demo immediately.
