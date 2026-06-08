# Troubleshooting

This chapter will collect common operator and authoring issues once the help workflows are more stable.

## Planned Topics

- missing data in the UI
- configuration mismatches
- widget-specific help lookup
- Python integration diagnostics

## UI Responsiveness Diagnostics

Use the built-in responsiveness diagnostics only when you need to investigate short UI stalls such as brief chart freezes or dialog-open pauses. The diagnostics are disabled by default.

Enable the diagnostics in one of these ways before starting HornetStudio:

- set the environment variable `HORNETSTUDIO_UI_DIAGNOSTICS=1`
- start the application with the command line switch `--ui-diagnostics`

When enabled, the application logs only meaningful UI thread latency spikes and slow diagnostic scopes. The logs include the measured delay, active window context, open dialog count, and compact timing data for dialog opening, browser-widget refresh work, registry event handling, Dispatcher posts, and realtime chart rendering.

### Manual Validation Workflow

1. Start HornetStudio with diagnostics disabled and confirm that no responsiveness-specific log lines are written during normal use.
2. Restart HornetStudio with diagnostics enabled.
3. Reproduce the suspected issue in these situations: idle main window, a page without browser widgets, a page with browser widgets, a visible realtime chart, and opening browser dialogs such as Functions, Monitor, Custom Signals, or Controllers.
4. Note the timestamps of any `UI responsiveness spike`, `UI diagnostic timing`, or `Browser UI diagnostics` log entries.
5. Find the running HornetStudio process id.
6. Run `dotnet-counters monitor --process-id <PID> System.Runtime` in a separate terminal.
7. Compare the counter output with the application log timestamps to see whether allocation rate or GC activity changes when UI spikes occur.

This workflow is intended to distinguish between Dispatcher backlog, browser-widget registry event bursts, expensive UI-thread work, and garbage-collection-related pauses without relying on a profiler.

## UI Benchmark Mode

Use the benchmark mode when you need a compact, repeatable summary for manual UI performance checks. Benchmark mode is disabled by default and automatically enables the responsiveness diagnostics.

Enable the benchmark in one of these ways before starting HornetStudio:

- set the environment variable `HORNETSTUDIO_UI_BENCHMARK=1`
- start the application with the command line switch `--ui-benchmark`
- use the VS Code launch entry `.NET Launch (HornetStudio UI Benchmark)`

Optional benchmark configuration:

- set `HORNETSTUDIO_UI_BENCHMARK_SECONDS=<seconds>` to change the default `60` second session length
- set `HORNETSTUDIO_UI_BENCHMARK_SCENARIO=<label>` to replace the default `Manual` scenario label in the log output
- start the application with `--start-project <path>` to load a specific project without changing the saved start project configuration

When benchmark mode is active, HornetStudio logs a `UI benchmark enabled` line at startup, records aggregate metrics during the session, writes a `UI benchmark summary` when the configured duration ends, and writes another shutdown summary when the application exits.

The summary includes these fields:

- scenario label and summary reason
- measured duration and benchmark warmup window
- max UI delay and p95 UI delay
- post-warmup UI budget values used for status evaluation
- spike counts above `50`, `100`, and `250` milliseconds
- total browser Dispatcher posts
- browser registry event rate, ignored ratio, and top key/prefix hot spots per browser source
- chart render count, max, and p95
- overall result status as `Pass`, `Warn`, or `Fail`

Additional detail lines are logged per browser source, per chart name, per startup phase, and per timed dialog or browser operation category.

### Manual Benchmark Workflow

1. Start HornetStudio with `.NET Launch (HornetStudio UI Benchmark)` or an equivalent benchmark environment configuration.
2. Confirm that the host log contains `UI benchmark enabled` and the expected scenario label.
3. Reproduce a manual scenario for at least the configured duration, for example opening a page with browser widgets, driving a PID signal flow, and keeping a realtime chart visible.
4. Check the `UI benchmark summary` line after the duration elapses.
5. Inspect the startup phase timing lines first when a run shows a large early stall during folder runtime connect/startup.
6. Inspect the browser, chart, and timing detail lines that follow the summary.
7. Close the application and confirm that a shutdown summary is written.

Interpret the initial soft budgets like this:

- `Pass`: the measured post-warmup UI delay and chart p95 stayed within the current soft budgets and browser Dispatcher posts remained low
- `Warn`: at least one soft budget was exceeded and should be tracked across comparable manual runs
- `Fail`: a metric exceeded the current fail threshold and likely indicates a strong regression or a benchmark run dominated by a visible stall

For browser-heavy pages, inspect the `UI benchmark browser summary` lines together with the overall summary. Browser control sources now include the owning folder or browser scope in brackets, for example `CustomSignalsBrowserControl[main2]`, so multi-folder runs can be compared per visible surface instead of only per control class. High `RegistryEventsPerSecond` values indicate that one browser control still receives too much runtime traffic. High `IgnoredRatio` values indicate that the control receives many scoped events that are still filtered locally and may need tighter source/runtime prefixes. `TopRegistryPrefixes` and `TopRegistryKeys` identify the dominant folder/domain and exact hot keys behind the load.

Treat startup attribution and steady-state responsiveness as separate readings during multi-folder runs. Use the `UI startup phase` lines and the `StartupPhase` timing summaries to identify which connect, registration, or status propagation phase caused an early stall. Then use the post-warmup `UI benchmark summary` values to judge whether steady-state responsiveness recovered after startup settled.

The benchmark currently records startup and warmup behavior for context, but evaluates the main UI delay budgets only after the initial warmup window. Dialog timing entries represent measured scope lifetime, not a dedicated open-cost profiler.

### Automated UI Lag Benchmark

Use `scripts/run_ui_benchmark.ps1` for repeatable UI lag, CPU, memory, and log-error checks. The script starts HornetStudio with benchmark diagnostics enabled, loads `benchmarks/ui_lag_bench_project/project.aaep` by default, samples process CPU and memory while the application runs, closes the application, and prints the relevant benchmark log lines.

Example:

```powershell
scripts/run_ui_benchmark.ps1 -Seconds 60
```

The dedicated benchmark project contains a UDL demo client, enhanced signal processing, monitor rules, signal controls, and a realtime chart. It is intended to run without manual interaction so repeated runs remain comparable.
