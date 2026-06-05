# Chat Handoff: UI Lag Baseline Without Chart

## Context

We are working in `B:\HornetStudio`.

The current conversation became noisy because several topics were mixed:

- startup lag
- RealtimeChart lag
- RealtimeChart history loss on folder/page switches
- Monitor evaluation cost
- UDL startup/attach cost
- registry/timer hotpaths

The latest useful reset point is a benchmark run without an active chart.

## Latest Baseline

Latest log:

`B:\HornetStudio\src\HornetStudio\bin\Debug\net9.0-windows\logs\host-20260530.log`

Log timestamp:

- `LastWriteTime`: `2026-05-30 19:30:55`
- Size: approximately `213 KB`

The user ran the same scenario without Chart.

Observed summary:

- `ChartRenderCount=0`
- `ChartRenderMaxMs=0`
- `ChartRenderP95Ms=0`
- `SteadyStateChart=0`
- no `UI benchmark chart summary`
- benchmark still failed
- `MaxUiDelayMs=16010.7`
- `P95UiDelayMs=63.7`
- `Spike50Ms=28`
- `Spike100Ms=10`
- `Spike250Ms=7`

Comparison to prior run with Chart:

- P95 improved from about `154.9 ms` to `63.7 ms`
- spike count improved from `76` to `28`
- Chart sampling was a lag amplifier
- Chart was not the only cause
- the extreme max startup/freeze lag remains without Chart

## Current Diagnosis

Do not start by assuming RealtimeChart is the root cause.

The run without Chart shows two remaining problem areas:

1. UDL startup/attach cost
2. Monitor/MonitorView steady-state evaluation or view cost

RealtimeChart remains relevant, but should be treated later as a separate runtime/history ownership issue.

Important refinement:

The next chat should not assume that the Monitor runtime itself is the UI problem. A stronger current hypothesis is that the Monitor view/widget layer is doing work when no visible view or browser dialog should need it.

Expected separation:

- Monitor runtime may evaluate rules in the background.
- Monitor view widgets may update rows only when attached, visible, and relevant.
- Browser dialogs should not cause UI/browser-operation work when they are not open.

If the browser dialog is not open, Monitor runtime activity should not create continuous Avalonia row updates, `MonitorViewControl` rebuilds, or dispatcher work.

## Important Log Findings Without Chart

UDL startup/attach:

- `UdlClientControl[main3:udl_client_control_1].OnAttachedItemsRefreshTimerTick`
  - `MaxMs=1172.9`
- `UdlClientControl[main1:udl_client_control_1].OnAttachedItemsRefreshTimerTick`
  - `MaxMs=825.4`
- `UdlClientControl[main2:udl_client_control_1].OnAttachedItemsRefreshTimerTick`
  - `MaxMs=787.1`
- `UdlClientControl[main1:udl_client_control_1].PublishClientItems`
  - `MaxMs=397.4`
- `UdlClientControl[main1:udl_client_control_1].PublishClientItems.PostRuntimeUpdateUi`
  - `MaxMs=265`
- `UdlClientControl[main1:udl_client_control_1].RebuildAttachSectionRows`
  - `MaxMs=209.8`

Monitor steady-state:

- `MonitorRuleRow[monitor_rule_3].Evaluate`
  - `Count=104`
  - `MaxMs=359.5`
  - `P95Ms=167.7`
- `MonitorRuleRow[monitor_rule_2].Evaluate`
  - `Count=104`
  - `MaxMs=330.3`
  - `P95Ms=144.8`
- `MonitorRuleRow[monitor_rule_1].Evaluate`
  - `Count=105`
  - `MaxMs=164.7`
  - `P95Ms=94.5`
- `MonitorRuleRow[monitor_rule_3].EvaluateState`
  - `MaxMs=219.8`
  - `P95Ms=84.3`
- `MonitorRuleRow[monitor_rule_2].EvaluateState`
  - `MaxMs=208.1`
  - `P95Ms=71.9`

Monitor view/widget clue:

- `BrowserOperation Name=MonitorViewControl[main3].RebuildRows`
  - `Count=1`
  - `MaxMs=124.9`
- `BrowserOperation Name=MonitorViewControl[main2].RebuildRows`
  - `Count=1`
  - `MaxMs=12.7`
- `BrowserOperation Name=MonitorViewControl[main1].RebuildRows`
  - `Count=2`
  - `MaxMs=9.7`

This suggests the next investigation should explicitly distinguish:

- `MonitorRuleRow` as runtime/evaluation work
- `MonitorViewControl` as UI/browser view work
- browser dialog state
- widget visibility/attachment state

If `MonitorViewControl` is instantiated in the page even when the browser dialog is closed, it still must not process unrelated registry events or rebuild rows unless visible/active/relevant.

RealtimeChart notes:

- `SteadyStateChart=0`
- `ChartRenderCount=0`
- There are still many `[RealtimeChart] Sampling skipped` debug logs.
- Those logs show inactive/hidden/detached chart definitions still passing through lifecycle code, but no active chart render or sampling hotpath.

## Recommended New Chat Start

Start the new chat with STRUCTURE mode, not IMPLEMENT mode.

Suggested prompt:

```text
#struct
Baseline ohne Chart liegt vor. Der Chart ist nicht die alleinige Ursache.

Bitte strukturiere die nächsten Schritte für:
1. UDL Startup/Attach Lag
2. MonitorView/MonitorControl UI Traffic ohne geöffneten Browser Dialog
3. RealtimeChart später separat als Runtime/History Ownership Thema

Nutze:
B:\HornetStudio\docs\workitems\2026.05.30.1934-ui_lag_without_chart_baseline\ChatHandoff.md
B:\HornetStudio\src\HornetStudio\bin\Debug\net9.0-windows\logs\host-20260530.log
```

## Recommended Direction

Do not continue the old RealtimeChart-first plan as the next implementation.

Use this order:

1. Isolate UDL startup/attach work.
2. Isolate MonitorView/MonitorControl UI traffic and separate it from Monitor runtime evaluation.
3. Only then return to RealtimeChart runtime/history ownership.

Reason:

- Without Chart, the benchmark still fails.
- Startup max delay remains extreme.
- Monitor evaluation still shows high steady-state timings.
- Chart work is valid, but not the clean first cut anymore.

## Existing Related Workitems

Previous active Chart ownership workitem:

- `docs/workitems/2026.05.30.1035-realtime_chart_runtime_ownership/`

Previous timer/registry hotpath workitem:

- `docs/workitems/2026.05.30.1017-ui_lag_timer_registry_hotpaths/`

Earlier RealtimeChart lag workitem:

- `docs/workitems/2026.05.30.0939-realtime_chart_steady_state_lags/`

Treat these as background context, not as the next active implementation plan.

## Relevant Files

- `src/HornetStudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Monitor/MonitorControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs`
- `src/HornetStudio.Editor/Monitoring/UiResponsivenessDiagnostics.cs`
- `src/HornetStudio/bin/Debug/net9.0-windows/logs/host-20260530.log`

## Open Questions For New Chat

- Should the next plan target UDL startup first or Monitor steady-state first?
- Should RealtimeChart lifecycle skip logging be reduced now, or left until the Chart workitem?
- Is the next test expected to run without Chart again, or with Chart restored after UDL/Monitor changes?

## Suggested Decision

Plan UDL startup/attach and Monitor evaluation as separate workitems.

Recommended first implementation target:

`UDL startup/attach lag isolation`

Recommended second implementation target:

`MonitorView UI traffic isolation`

Recommended later target:

`RealtimeChart runtime ownership and history retention`

## MonitorView Hypothesis For New Chat

Question to validate:

Can the lag attributed to Monitor actually come from `MonitorViewControl` or Monitor widget UI work rather than the pure Monitor runtime?

Reasoning:

- A monitor runtime can evaluate in the background without visible UI work.
- If no browser dialog is open, browser/dialog-specific view work should not run.
- If monitor widgets are hidden or inactive, row rebuilds and dispatcher work should be gated.
- Existing solution rules require registry event filtering before scheduling UI work.

Concrete checks:

- Is `MonitorViewControl` instantiated even when the browser dialog is not open?
- Does `MonitorViewControl` subscribe to `HostRegistries.Data.ItemChanged` while hidden/inactive?
- Are `MonitorRuleRow` timers/evaluations owned by view rows instead of a UI-independent runtime state?
- Does `MonitorViewControl` rebuild rows after runtime publish events even when no row is visible?
- Is relevance filtering done before any `Dispatcher.UIThread.Post(...)` or row rebuild?
- Does `MonitorViewControl` maintain a cheap relevance snapshot for visible rows only?

Preferred structural target:

- Monitor runtime owns rule evaluation and runtime state.
- Monitor view owns only visible row rendering.
- MonitorViewControl ignores registry events when detached, hidden, inactive, or unrelated.
- Browser dialog/view code does no work when the dialog is closed.
