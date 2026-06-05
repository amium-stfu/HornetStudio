# Benchmark Workflow

## Scope

- Use this mode for repeatable local UI benchmark runs.
- Do not change product code unless the user explicitly asks for benchmark infrastructure changes.
- Keep benchmark execution reproducible and compare runs against the same project and duration.

## Command Shape

- `#bench` runs the default UI benchmark duration.
- `#bench <seconds>` runs the UI benchmark for the requested duration.
- If no project is specified by the user, use `benchmarks/ui_lag_bench_project/project.aaep`.

## Workflow

1. Confirm the benchmark project exists.
2. Build the solution when the existing output is missing or stale enough that the run would be misleading.
3. Run `scripts/run_ui_benchmark.ps1` with the requested duration and benchmark project.
4. Wait for the script to finish.
5. Summarize the benchmark result in German.
6. Separate benchmark-window findings from later process runtime findings when the log contains both.

## Result Summary

Include these values when available:

- project path
- scenario label
- duration
- configuration
- benchmark window or benchmark summary timestamp
- benchmark status
- max UI delay
- p95 UI delay
- UI spike counts
- average and peak CPU
- start, end, delta, and peak working set
- start, end, delta, and peak private memory
- peak working set
- peak private memory
- error, fatal, exception, and warning counts from the run log
- relevant benchmark summary, browser summary, chart summary, and timing summary lines
- log file path

## Phase Assessment

Classify findings by phase before naming a suspect:

- `Startup`: operations logged as `StartupPhase`, first connection, project load, first publish, first chart attach, or first browser/context initialization.
- `Warmup`: benchmark warmup period and first visible UI stabilization after startup.
- `SteadyState`: repeated runtime/value updates after startup and warmup have completed.
- `Shutdown`: close/dispose/release logs and spikes after shutdown was requested.

Do not describe a startup-only or shutdown-only cost as a steady-state lag source.

## Suspect Detection

Call out these patterns explicitly when present:

- `PublishClientItems Count > 1` for the same client during one benchmark run.
- `PublishClientItems` with a reason other than startup/connect while value updates are expected.
- `BrowserDispatcherPosts > 0`, especially when paired with high spike counts.
- `UI benchmark browser summary` entries with high dispatcher posts, high accepted registry events, or low rejected-event filtering.
- `ChartRenderCount > 0` with high `ChartRenderP95Ms` or `ChartRenderMaxMs`.
- Timing summary lines with both high `Count` and high `P95Ms`.
- Any UI-thread operation above 50 ms in steady state, above 100 ms as a likely suspect, and above 250 ms as a primary suspect.
- Repeated attach, rebuild, refresh, synchronize, or project operations after startup.
- Errors, fatal logs, exceptions, or warning bursts that overlap the benchmark window.

If a spike exists but no instrumented operation is close enough to explain it, mark the cause as `Unknown` instead of guessing.

## Correlation Rules

For every spike above 100 ms, compare nearby log lines in the same run:

- Prefer operations within 1 second before the spike timestamp.
- Include operations up to 250 ms after the spike only when they are clearly part of the same blocked UI sequence.
- Prefer UI-thread timing lines over background-thread timing lines for UI delay attribution.
- Use background-thread timing only as indirect evidence unless it posts or blocks UI work.
- Mention when the benchmark summary is emitted at shutdown but represents an earlier fixed benchmark window.

## Conclusion Levels

End the summary with one of these levels:

- `Green`: benchmark passed and no repeated steady-state suspect is visible.
- `Yellow`: benchmark passed but startup, shutdown, or unknown spikes remain.
- `Red`: benchmark failed or repeated steady-state work has a clear owner.
- `Unknown`: benchmark data is incomplete or spikes cannot be correlated with available instrumentation.

## Constraints

- Do not run unrelated manual UI workflows during a benchmark.
- Do not modify the user's configured start project for a benchmark run.
- Prefer the dedicated benchmark project over ad hoc samples.
- Treat benchmark failures as diagnostic output unless the user asks for a fix.
