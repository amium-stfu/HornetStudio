# Chat Handoff: High-Frequency Signal Control Updates

## 2026-06-04 Startup Spike Correlation Snapshots

Implemented change set:

- `src/HornetStudio.Editor/Monitoring/UiResponsivenessDiagnostics.cs`
  - Added bounded benchmark correlation snapshots for rare events only:
    - detected UI spikes,
    - the warmup-to-steady-state transition.
  - Each snapshot now emits the current phase plus compact stage/count summaries for:
    - `TargetRefreshExecuted*`,
    - `TargetValueRefreshExecuted*`,
    - `TargetRefreshCoalesced`,
    - `SignalValueRefreshCoalesced`,
    - `SignalTargetUnresolved`,
    - `SignalTargetAvailability*`.
  - Snapshots also include the latest known active signal stage/path/module and bounded live backlog counters.
- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Added minimal live backlog counter hooks for:
    - queued broad target refreshes,
    - queued broad follow-up refreshes,
    - queued value-only refreshes,
    - active unresolved-target gates.
  - No broad per-refresh attribution or extra dispatcher posting was added.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Added focused self-tests for non-negative backlog counter behavior and warmup snapshot payload capture.

Validation:

- Focused new editor diagnostics self-tests passed:
  - `HORNET_EDITOR_TEST_FILTER=UI diagnostics `
  - Test runner summary: `Editor tests passed: 2`
- Full solution build passed:
  - `dotnet build HornetStudio.sln --no-restore`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- Benchmark run with new snapshot output:
  - Scenario: `StartupSpikeCorrelation-60s`
  - Command:
    - `powershell -ExecutionPolicy Bypass -File scripts\run_ui_benchmark.ps1 -Seconds 60 -ProjectPath 'benchmarks\ui_lag_bench_project\project.aaep' -Scenario 'StartupSpikeCorrelation-60s' -NoBuild`
  - Result:
    - `Status=Warn`
    - `MaxUiDelayMs=4447.4`
    - `P95UiDelayMs=85.5`
    - `BudgetMaxUiDelayMs=240.3`
    - `BudgetP95UiDelayMs=68.3`
    - `BrowserDispatcherPosts=0`
    - `SignalTargetUnresolved`: `186`, about `3.1/s`
    - `TargetRefreshCoalesced`: `1364`, about `22.73/s`
    - `SignalValueRefreshCoalesced`: `211`, about `3.516/s`
    - `TargetRefreshExecuted[Warmup->Warmup]`: `223`, `P95=4361.5 ms`
    - `TargetValueRefreshExecuted[Warmup->Warmup]`: `106`, `P95=3794.1 ms`
    - `TargetRefreshExecuted[SteadyState->SteadyState]`: `2231`, `P95=213.1 ms`
    - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `7011`, `P95=52.1 ms`

Correlation snapshot highlights:

- First large reproduced spike:
  - `OffsetSeconds=6.4`
  - `DelayMs=4447.4`
  - `TargetRefreshQueued=21`
  - `TargetRefreshFollowUpQueued=21`
  - `TargetValueRefreshQueued=8`
  - `UnresolvedTargetGateCount=20`
  - `TargetRefreshCoalesced=60`
  - `SignalValueRefreshCoalesced=114`
  - `SignalTargetAvailabilityGateArmed=20`
  - No executed `TargetRefresh*` / `TargetValueRefresh*` counts were recorded yet at that snapshot.
- Warmup-to-steady-state transition snapshot:
  - `OffsetSeconds=10.005`
  - `DelayMs=207`
  - `TargetRefreshQueued=9`
  - `TargetRefreshFollowUpQueued=2`
  - `TargetValueRefreshQueued=6`
  - `TargetRefreshExecuted[Warmup->Warmup]=223`
  - `TargetValueRefreshExecuted[Warmup->Warmup]=106`
  - `TargetRefreshCoalesced=332`
  - `SignalValueRefreshCoalesced=149`
- Later steady-state spikes were smaller but still correlated with a persistent broad refresh backlog:
  - around `OffsetSeconds=14.844`:
    - `DelayMs=98.2`
    - `TargetRefreshQueued=11`
    - `TargetRefreshFollowUpQueued=5`
    - `TargetValueRefreshQueued=5`
    - `TargetRefreshExecuted[SteadyState->SteadyState]=360`
    - `TargetValueRefreshExecuted[SteadyState->SteadyState]=187`
    - `SignalTargetUnresolved=116`
  - around `OffsetSeconds=16.882`:
    - `DelayMs=81.4`
    - `TargetRefreshQueued=11`
    - `TargetRefreshFollowUpQueued=3`
    - `TargetValueRefreshQueued=6`
    - `TargetRefreshExecuted[SteadyState->SteadyState]=617`
    - `TargetValueRefreshExecuted[SteadyState->SteadyState]=316`
    - `SignalTargetUnresolved=116`

Observed result:

- The new snapshots successfully correlate spike windows with live backlog shape instead of only end-of-run aggregates.
- The strongest warmup spike happened before any meaningful refresh execution was completed and coincided with:
  - a full unresolved-target gate burst,
  - a full broad-refresh follow-up burst,
  - and a non-trivial queued value-refresh backlog.
- After warmup, spikes no longer aligned with unresolved gates being active, but they still aligned with a persistent broad refresh queue/follow-up backlog across modules such as `m009`, `m011`, `m012`, `m014`, `m018`, `m019`, and `m020`.

Interpretation:

- The snapshot data supports a two-phase startup problem:
  - early spike phase:
    - unresolved target availability and first broad refresh fan-out accumulate before execution catches up;
  - later phase:
    - the unresolved gate mostly clears, but broad refresh churn remains high enough to keep `TargetRefreshQueued` / follow-up backlog non-zero while steady-state value refreshes continue.
- The run did not preserve the earlier `StartupTargetAvailabilityGate-60s` baseline (`P95UiDelayMs=9.9` vs `68.3` after warmup in this run).
- Inference:
  - the correlation instrumentation itself stayed bounded and rare-event driven,
  - but this benchmark should not be treated as a clean apples-to-apples performance comparison against the old baseline without first accounting for the current working tree/state.

Recommended next step:

- Use the new snapshot output in `#debug` mode first, not as proof of an accepted low-regression implementation.
- The next focused investigation should start from the early warmup burst where:
  - `TargetRefreshQueued=21`,
  - `TargetRefreshFollowUpQueued=21`,
  - `UnresolvedTargetGateCount=20`,
  - and almost no refresh execution had completed yet.
- After that, inspect why steady-state broad refresh backlog still remains elevated for the repeated module cluster (`m009`, `m011`, `m012`, `m014`, `m018`, `m019`, `m020`) even once unresolved gates are mostly gone.

## 2026-06-04 Startup Warmup Signal Fallback Classification

Implemented change set:

- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Reclassified Signal target-resolution diagnostics so the normal effective public target path is no longer recorded as `SignalFallbackRegistryPathUsed`.
  - Added bounded Signal stages for `SignalEffectiveTargetPropertyUsed`, `SignalTargetUnresolved`, `SignalFallbackInvalidValueReference`, and `SignalFallbackSelectedPropertyMissing`.
  - Kept Signal value resolution behavior unchanged: effective public HostRegistry target still wins, runtime `valueRef` stays a compatibility fallback, and public `read` stays the final display fallback.
- `src/HornetStudio.Editor/Widgets/UdlClient/UdlHostRegistryProjection.cs`
  - Serialized `SynchronizeAttachments()` / `ClearAttachments()` with a narrow lifecycle lock so concurrent refreshes cannot dispose the active `UiFolderContext` while attachments are still being projected.
  - Preserved the existing projection behavior; the change only removes the reentrant dispose race around startup attachment synchronization.

Validation:

- Focused editor `value reference` self-tests passed on isolated output:
  - `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-startup-warmup-signal-fallback\`
  - `HORNET_EDITOR_TEST_FILTER=value reference`
  - Test runner summary: `Editor tests passed: 5`
- Focused editor `projection` self-tests passed on isolated output after the projection lock fix:
  - `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-startup-warmup-projection-fix\`
  - `HORNET_EDITOR_TEST_FILTER=projection`
  - Test runner summary: `Editor tests passed: 4`
- Full solution build passed twice:
  - `dotnet build HornetStudio.sln --no-restore`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- Fresh baseline benchmark before the fix:
  - Scenario: `StartupWarmupSignalFallback-60s`
  - `Status=Warn`
  - `MaxUiDelayMs=5080`
  - `P95UiDelayMs=46.4`
  - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - `SignalFallbackRegistryPathUsed`: `90158`, about `1502.439/s`
  - `SignalReceive`: `9322`, about `155.347/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `7441`, about `124.001/s`, `P95=15.7 ms`
  - `TargetRefreshExecuted[Warmup->Warmup]`: `293`, `P95=4663.1 ms`
  - `TargetValueRefreshExecuted[Warmup->Warmup]`: `88`, `P95=3666.2 ms`
- Benchmark after diagnostic reclassification and projection lock fix:
  - Scenario: `StartupWarmupSignalFallback-60s-ReclassifiedLock`
  - `Status=Warn`
  - `MaxUiDelayMs=5774.2`
  - `P95UiDelayMs=35.6`
  - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - `SignalEffectiveTargetPropertyUsed`: `86544`, about `1442.066/s`
  - `SignalFallbackInvalidValueReference`: `105`, about `1.750/s`
  - `SignalTargetUnresolved`: `605`, about `10.081/s`
  - `SignalReceive`: `8835`, about `147.216/s`
  - `RegistryPublish[SteadyState]`: `31746`, about `528.977/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `6535`, about `108.891/s`, `P95=22.2 ms`
  - `TargetRefreshExecuted[Warmup->Warmup]`: `274`, `P95=5289.8 ms`
  - `TargetValueRefreshExecuted[Warmup->Warmup]`: `37`, `P95=4637 ms`
  - No `ObjectDisposedException` from `UiFolderContext.Attach(...)` appeared in the final locked rerun.

Observed result:

- The old `SignalFallbackRegistryPathUsed` metric was mostly diagnostic noise. `ResolveTargetProperty()` recorded it even when the Signal already consumed the effective public HostRegistry target and the requested `read` property existed on that effective item.
- The new benchmark split shows the actual picture much more clearly:
  - the dominant getter-time path is `SignalEffectiveTargetPropertyUsed` (`~1442/s`), not real fallback;
  - real invalid-reference fallback is rare (`SignalFallbackInvalidValueReference` only `105` events over 60s);
  - there is still a measurable startup ordering gap (`SignalTargetUnresolved` `605` times).
- The projection lock removed the previously recurring reentrant `UiFolderContext` disposal failure from the comparable rerun, so the benchmark can again be interpreted as a real startup/warmup measurement instead of a broken startup run.

Interpretation:

- `SignalFallbackRegistryPathUsed` was not the warmup root cause. It primarily represented repeated display getter calls against already valid effective public targets.
- The first-seconds delay remains a startup/warmup scheduling problem in the target refresh path, not a steady-state fallback-rendering problem:
  - warmup `TargetRefreshExecuted` and `TargetValueRefreshExecuted` still queue for multiple seconds;
  - steady-state `TargetValueRefreshExecuted` remains materially healthier than warmup and stays in the expected low-double-digit millisecond range for `P95`.
- The remaining highest-value diagnostic leads are the unresolved-target startup window (`SignalTargetUnresolved`) and the large warmup queue/execution gap, not the old fallback counter.

Next step:

- Continue from startup target-resolution ordering and initial target refresh scheduling in `PageItemModel` / projection startup, using `SignalTargetUnresolved` plus warmup `TargetRefresh*` counters as the primary guides. Do not treat the removed raw fallback count as evidence of real fallback rendering anymore.

## 2026-06-04 Startup Warmup Target Refresh Coalescing

Implemented change set:

- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Coalesced broad Signal target refresh dispatcher posts so each widget keeps at most one queued broad refresh plus one latest-wins follow-up refresh.
  - Added bounded `TargetRefreshCoalesced` diagnostics to measure how often redundant broad refresh requests arrive while one refresh is already queued.
  - Preserved the existing value-only refresh path and its separate `TargetValueRefresh*` coalescing behavior.

Validation:

- Focused editor Signal ancestor refresh self-tests passed on isolated output:
  - `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-startup-warmup-coalesce\`
  - `HORNET_EDITOR_TEST_FILTER=Signal ancestor`
  - Test runner summary: `Editor tests passed: 2`
- Full solution build passed:
  - `dotnet build HornetStudio.sln --no-restore`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- Benchmark after broad target refresh coalescing:
  - Scenario: `StartupWarmupSignalFallback-Fix-60s`
  - `Status=Warn`
  - `MaxUiDelayMs=4750.6`
  - `P95UiDelayMs=11.2`
  - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - `SignalEffectiveTargetPropertyUsed`: `80256`, about `1337.459/s`
  - `SignalFallbackInvalidValueReference`: `70`, about `1.167/s`
  - `SignalTargetUnresolved`: `378`, about `6.299/s`
  - `TargetRefreshCoalesced`: `869`, about `14.482/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `7023`, about `117.038/s`, `P95=5.5 ms`
  - `TargetRefreshExecuted[Warmup->Warmup]`: `164`, `P95=4971.6 ms`
  - `TargetValueRefreshExecuted[Warmup->Warmup]`: `43`, `P95=4316.8 ms`

Observed result:

- The new `TargetRefreshCoalesced` counter confirms that warmup and steady-state both produced a large number of redundant broad refresh requests that previously would have been posted individually onto the UI dispatcher.
- The coalescing fix improved several important metrics compared to `StartupWarmupSignalFallback-60s-ReclassifiedLock`:
  - overall UI `P95UiDelayMs` dropped from `35.6` to `11.2`;
  - `TargetRefreshExecuted[Warmup->Warmup]` count dropped from `274` to `164`, with `P95` improving from `5289.8 ms` to `4971.6 ms`;
  - `TargetValueRefreshExecuted[Warmup->Warmup]` `P95` improved from `4637 ms` to `4316.8 ms`;
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]` `P95` improved from `22.2 ms` to `5.5 ms`;
  - `SignalTargetUnresolved` dropped from `605` to `378` and real invalid-reference fallback also stayed low.
- The fix did not remove the startup backlog entirely. Warmup broad refresh execution still spends multiple seconds in the queue/execution gap, so coalescing broad refresh posts is only a partial fix, not the final root cause elimination.

Interpretation:

- Broad `TriggerTargetRefresh()` posting pressure in `PageItemModel` was a real contributor to the warmup backlog.
- The remaining startup delay is now more clearly split between:
  - the unresolved-target startup window that still exists, and
  - the smaller but still multi-second warmup broad refresh backlog after projection/attach startup.
- Because `BrowserDispatcherPosts=0` and `ChartRenderCount=0` remained intact, the observed spikes still point at Signal/target-refresh startup work rather than browser/chart regressions.

Next step:

- Continue from startup ordering and initial target resolution in `PageItemModel` / UDL projection startup. The next change should explain why `SignalTargetUnresolved` and `TargetRefreshExecuted[Warmup->Warmup]` still remain in the multi-second range after broad refresh coalescing.

## 2026-06-04 Startup Target Availability Gate

Implemented change set:

- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Added a per-signal unresolved-target availability gate that arms when `ResolveTarget()` cannot resolve the configured public target.
  - `OnDataRegistryChanged(...)` now suppresses repeated broad refresh scheduling while the target is still unresolved and only lets the first event through that can actually resolve the target again, either via registry resolution or via an ancestor snapshot that contains the missing child.
  - `TryHandleSignalValueRefRegistryChange(...)` now ignores value-reference refresh pressure while the public signal target is still unresolved.
  - Added bounded diagnostic stages for the new gate path so future benchmark runs can distinguish unresolved waiting from actual availability.

Validation:

- Existing signal-focused Editor self-tests passed:
  - `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-startup-target-availability\`
  - `HORNET_EDITOR_TEST_FILTER=Signal `
  - Test runner summary: `Editor tests passed: 24`
- Full solution build passed:
  - `dotnet build HornetStudio.sln --no-restore`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` `CS0414` warning remained.
- Benchmark after unresolved-target gate:
  - Scenario: `StartupTargetAvailabilityGate-60s`
  - `Status=Pass`
  - `MaxUiDelayMs=3516.7`
  - `P95UiDelayMs=9.9`
  - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - `SignalTargetUnresolved`: `254`, about `4.233/s`
  - `TargetRefreshCoalesced`: `652`, about `10.866/s`
  - `TargetRefreshExecuted[Warmup->Warmup]`: `524`, `P95=3277.8 ms`
  - `TargetValueRefreshExecuted[Warmup->Warmup]`: `259`, `P95=274.6 ms`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `9358`, `P95=1.3 ms`

Observed result:

- The unresolved-target gate reduced the visible unresolved startup window again compared to `StartupWarmupSignalFallback-Fix-60s`:
  - `SignalTargetUnresolved` dropped from `378` to `254`;
  - overall UI `P95UiDelayMs` improved from `11.2` to `9.9`;
  - `TargetValueRefreshExecuted[Warmup->Warmup]` improved materially from `P95=4316.8 ms` to `P95=274.6 ms`;
  - steady-state value refresh remained healthy and improved further from `P95=5.5 ms` to `P95=1.3 ms`.
- The change did not yet reduce the total number of warmup broad refresh executions. `TargetRefreshExecuted[Warmup->Warmup]` count increased from `164` to `524`, even though its `P95` improved from `4971.6 ms` to `3277.8 ms`.
- This means the gate removed a meaningful part of the expensive unresolved wait/backlog, but it did not yet eliminate the remaining warmup full-refresh churn after target availability arrives.

Interpretation:

- The startup issue is not just raw queue wait anymore. The remaining cost has shifted toward repeated warmup full refreshes that still execute after targets become available.
- The current best local reading is:
  - unresolved target retries were a real contributor and are now materially reduced;
  - once availability arrives, `PageItemModel` still performs too many broad warmup refreshes for the same targets;
  - the next profitable slice is the transition from `SignalTargetAvailabilityDetected` / first availability into repeated `RequestTargetRefresh()` execution, not steady-state value delivery.

Next step:

- Continue from `PageItemModel` warmup refresh scheduling after target availability, using `StartupTargetAvailabilityGate-60s` as the new baseline. The next change should explain why warmup broad refresh execution count remains high even after unresolved-target retries are gated.

## 2026-06-04 UDL Reference-Backed HostRegistry

Implemented change set:

- `src/HornetStudio.Host/HostRegistries.cs`
  - Added a narrow `DataRegistryValueReference` contract owned by `HostRegistries.Data`.
  - Added registry APIs to register/remove public parameter references and to publish public-path `PropertyUpdated` notifications for referenced values.
  - Changed `TryResolve(...)` to return an effective item view for referenced public items while preserving the stored public snapshot as the fallback/writeback surface.
  - Changed internal write/update paths to mutate stored snapshots directly instead of going through the effective referenced view.
- `src/HornetStudio.Host/UiFolderContext.cs`
  - UDL attachments now register a public `...read` -> runtime `...read.Properties["read"]` reference during attach.
  - Coalesced runtime source updates now raise public-path reference notifications through `HostRegistries.Data.NotifyReferencedPropertyChanged(...)` instead of treating the public snapshot as the authoritative live-value store.
  - Attachment disposal now removes registered references before removing the public registry root.
- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Signal display resolution now prefers the effective public registry target view returned by the HostRegistry.
  - The old Signal-side `valueRef` handling is now demoted to compatibility fallback instead of being the primary live-value owner.
  - Runtime-path `valueRef` events are ignored when the effective public target already exposes the requested property.
- `src/HornetStudio.Editor/Widgets/UdlClient/UdlHostRegistryProjection.cs`
  - `ClearAttachments()` now clears the current `UiFolderContext` reference before disposal so reentrant projection refreshes cannot reuse a disposed context.
  - `valueRefPath` / `valueRefParameter` metadata remain on the public `read` channel as protected compatibility metadata.
- `src/HornetStudio.Host.Tests/Program.cs`
  - Added focused Host tests for effective public reference resolution and for public-path notifications that do not mutate the stored fallback snapshot.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Updated the affected Signal `value reference` tests to register HostRegistry-owned references explicitly and validate the new owner boundary.

Validation:

- Focused Host self-tests passed on isolated output:
  - `dotnet build src\HornetStudio.Host.Tests\HornetStudio.Host.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\host-tests-udl-value-ref\`
  - `dotnet b:\HornetStudio\artifacts\host-tests-udl-value-ref\HornetStudio.Host.Tests.dll`
  - Test runner summary: `Host registry tests passed: 65`
- Focused Editor `value reference` self-tests passed on isolated output:
  - `C:\Progra~1\dotnet\dotnet.exe build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-udl-value-ref-hostref\`
  - `HORNET_EDITOR_TEST_FILTER=value reference`
  - Test runner summary: `Editor tests passed: 5`
- Focused UDL projection lifecycle tests passed after the reentrant disposal fix:
  - `C:\Progra~1\dotnet\dotnet.exe build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-udl-projection-refhost\`
  - `HORNET_EDITOR_TEST_FILTER=projection`
  - Test runner summary: `Editor tests passed: 4`
- Full solution build passed:
  - `dotnet build HornetStudio.sln --no-restore`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.

Benchmark:

- Scenario: `UdlReferenceBackedHostRegistry-60s`
- Command:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run_ui_benchmark.ps1 -Seconds 60 -ProjectPath benchmarks\ui_lag_bench_project\project.aaep -Scenario UdlReferenceBackedHostRegistry-60s -NoBuild`
- Result:
  - `Status=Fail`
  - `DurationSeconds=53.5`
  - `MaxUiDelayMs=6363.8`
  - `P95UiDelayMs=307.8`
  - `BrowserDispatcherPosts=0`
  - `ChartRenderCount=0`
  - `DemoGenerate`: `4840`, about `90.469/s`
  - `RegistryPublish[SteadyState]`: `17150`, about `320.565/s`
  - `SignalReceive`: `4826`, about `90.207/s`
  - `SignalFallbackRegistryPathUsed`: `46700`, about `872.909/s`
  - `SignalLatestValueApplied`: `1154`, about `21.570/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `1150`, about `21.496/s`, `P95=294.9 ms`, `Max=536.3 ms`
  - `RegistryPublishDuration PropertyUpdated.read`: `Count=6271`, `P95=29.4 ms`, `Max=210.1 ms`

Observed result:

- The architectural ownership has moved as intended: HostRegistry now owns the effective read resolution for public UDL attachment paths, and widgets can consume the public path without needing the runtime path as the primary live-value source.
- Public fallback snapshots remain intact for missing/invalid references and for guarded public writeback state.
- The initial benchmark rerun exposed a reentrant projection-disposal failure (`ObjectDisposedException` from `UdlHostRegistryProjection.ClearAttachments()` / `UiFolderContext.Attach(...)`), which was fixed by clearing the active `UiFolderContext` reference before disposal and then revalidated with focused projection tests.
- The final benchmark still fails the UI delay budget. The data shows that `SignalFallbackRegistryPathUsed` remains extremely high, and steady-state target refresh throughput is still far below the intended range.

Interpretation:

- The HostRegistry-owned reference model is implemented and validated functionally, but the live benchmark path is still dominated by fallback display-path activity in `PageItemModel`.
- The remaining performance issue is no longer the ownership boundary itself; it is the remaining high-frequency fallback/display refresh path that still triggers far too often during the 20-module UDL benchmark.

Next step:

- Start from `PageItemModel.ResolveTargetProperty(...)` / `SignalFallbackRegistryPathUsed` and measure why the effective public registry target still falls back at high frequency during the live 20-signal scenario, despite the HostRegistry now exposing referenced values on the public path.

## 2026-06-04 UDL ValueRef Signal Decoupling

Implemented change set:

- `src/HornetStudio.Editor/Widgets/UdlClient/UdlHostRegistryProjection.cs`
  - Added `valueRefPath` and `valueRefParameter` properties on the public attached UDL `read` channel so the public item points back to the authoritative runtime `read` item.
- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Added a Signal-only live-value resolver that reads the latest value from `valueRefPath + valueRefParameter` when present.
  - Kept the existing public registry item as the writeback target and fallback display path.
  - Added runtime-path event handling for Signal `valueRef` updates without introducing a second live-value store.
  - Added aggregate diagnostics stages for `SignalValueRefResolved`, `SignalValueRefDirtyReceived`, `SignalValueRefLatestValueApplied`, and `SignalFallbackRegistryPathUsed`.
- `src/HornetStudio.Host/HostRegistries.cs`
  - Marked `valueRefPath` and `valueRefParameter` as protected host registry properties so they stay out of user-facing pickers and guarded writes.
- `src/HornetStudio.Host/UiFolderContext.cs`
  - Preserved protected target-side properties across attached-item snapshot refreshes so `valueRef*` metadata survives refresh-driven re-clones.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Added focused editor self-tests for UDL `valueRef` publication, `valueRef` survival after a snapshot refresh, Signal live-value resolution, fallback on invalid `valueRef`, and preservation of public writeback behavior.

Validation:

- Focused editor self-tests passed on isolated output:
  - `UDL read attachment publishes value reference metadata`
  - `UDL read attachment keeps value reference metadata after snapshot refresh`
  - `Signal target property resolves value reference runtime value`
  - `Signal target property falls back when value reference is invalid`
  - Test runner summary: `Editor tests passed: 4`
- Isolated solution build passed:
  - `dotnet build HornetStudio.sln --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\solution-value-ref-rerun\`
  - Only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- Benchmark reruns both failed:
  - `UdlSignalValueRef-60s`
    - `Status=Fail`
    - `DemoGenerate`: about `174.9/s`
    - `SignalReceive`: about `116.4/s`
    - `SignalFallbackRegistryPathUsed`: about `118.6/s`
    - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: about `11.0/s`, `P95=1022.4 ms`
    - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - `UdlSignalValueRef-60s-Rerun`
    - `Status=Fail`
    - `DemoGenerate`: about `176.0/s`
    - `SignalReceive`: about `107.2/s`
    - `SignalFallbackRegistryPathUsed`: about `83.1/s`
    - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: about `7.0/s`, `P95=2258.7 ms`
    - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`

Observed result:

- The code path for publishing and preserving `valueRef` metadata is now covered by focused tests and solution build validation.
- The real 20-signal benchmark still does not show the expected `valueRef`-driven improvement. The fallback registry path remains active at high frequency, and the benchmark throughput regressed badly enough to fail the UI delay budget.
- Because the new `valueRef` diagnostics do not appear in the benchmark summary while `SignalFallbackRegistryPathUsed` remains high, the remaining problem is likely still in the runtime/public target resolution path used by the live benchmark scenario rather than in basic metadata publication.

Debug follow-up:

- Added Signal `valueRef` cache state in `src/HornetStudio.Editor/Models/PageItemModel.cs` so Signal widgets cache `valueRefPath`, `valueRefParameter`, and the resolved runtime item when `Target` changes instead of reconstructing the resolution repeatedly from the current target path.
- Focused editor self-tests still passed after the cache change:
  - `UDL read attachment publishes value reference metadata`
  - `UDL read attachment keeps value reference metadata after snapshot refresh`
  - `Signal target property resolves value reference runtime value`
  - `Signal target property falls back when value reference is invalid`
  - `Signal write keeps public writeback with value reference`
  - Test runner summary: `Editor tests passed: 5`
- Benchmark `UdlSignalValueRef-DebugCache-60s` improved materially:
  - `Status=Warn` instead of `Fail`
  - `DemoGenerate`: about `194.7/s`
  - `SignalReceive`: about `181.9/s`
  - `SignalLatestValueApplied`: about `152.2/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: about `146.8/s`, `P95=109 ms`, `Max=281.5 ms`
  - `RegistryPublishDuration PropertyUpdated.read`: `P95=13.6 ms`
  - `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
  - Remaining issue: `SignalFallbackRegistryPathUsed` stayed very high at about `1385.3/s`, and warmup still showed multi-second delays (`TargetValueRefreshExecuted[Warmup->Warmup] P95 about 5194.8 ms`)
- A follow-up startup experiment that routed initial `ResolveTarget()` refreshes through `RequestTargetRefresh()` made the benchmark worse and was reverted:
  - Scenario `UdlSignalValueRef-StartupCoalesce-60s`
  - `Status=Fail`
  - `MaxUiDelayMs=9154.4`
  - `P95UiDelayMs=122.7`
  - `SignalLatestValueApplied`: about `122.7/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: about `122.3/s`, `P95=134.3 ms`
  - `TargetRefreshExecuted[Warmup->Warmup] P95 about 7786.5 ms`

Updated interpretation:

- The main steady-state regression was caused by repeated getter-time Signal `valueRef` reconstruction; caching the resolved `valueRef` state recovered most of the lost throughput.
- The remaining startup problem is not solved by simply coalescing the initial `ResolveTarget()` refresh path.
- The next targeted debug step should inspect where `ResolveTargetProperty(...)` records `SignalFallbackRegistryPathUsed` and determine whether the fallback counter represents real fallback rendering or repeated high-frequency display-property reads that still use fallback diagnostics.

Next step:

- Before further product changes, inspect why the benchmark Signal path still resolves the public registry fallback instead of the published `valueRef` metadata during the live 20-module scenario.

## 2026-06-04 Signal Registry Delivery Follow-up

Implemented change set:

- `src/HornetStudio.Editor/Models/PageItemModel.cs`
  - Added bounded signal-pipeline stages for `SignalRegistryEventRelevant`, `SignalRegistryEventIrrelevant`, `SignalLatestValueStored`, `SignalValueRefreshScheduled`, `SignalValueRefreshCoalesced`, and `SignalLatestValueApplied`.
  - Changed `IsTargetValueOnlyChange(...)` so a `PropertyUpdated.read` event on the parent item is treated as a value-only Signal refresh when the configured Signal target is the matching child path such as `studio.main.udl1.m001.read`.
  - Changed `QueueTargetValueRefresh(...)` to resolve and store the matching child item for ancestor property updates instead of queueing the parent item.
  - Reduced the first-pass irrelevant-event diagnostics overhead by recording `SignalRegistryEventIrrelevant` only inside the current Signal target scope rather than for every unrelated global registry event.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Added focused self-tests for the new ancestor-property value-only path and for queueing the resolved child target item.

Validation:

- Focused editor self-tests passed:
  - `Signal ancestor read property change uses value-only refresh`
  - `Signal ancestor read property queue stores child target`
  - Test runner summary: `Editor tests passed: 3` with `HORNET_EDITOR_TEST_FILTER=ancestor`
- File diagnostics on the edited `PageItemModel.cs` stayed clean after each patch.
- An isolated Editor build succeeded after both code patches; only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- The standard solution build hit an existing file lock from `.NET Host (20520)` on `src/HornetStudio.Editor.Tests/bin/Debug/net9.0-windows/*.dll`; this affected the final default-output rebuild but not the isolated build/test validation.

Measured benchmark state:

- Scenario: `SignalRegistryDeliveryFix-30s`
- Benchmark status: `Fail`
- `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
- `RegistryPublish[SteadyState]`: `10576`, `425.180/s`
- `SignalRegistryEventRelevant`: `2981`, `119.843/s`
- `SignalReceive`: `2981`, `119.843/s`
- `SignalLatestValueStored`: `2945`, `118.396/s`
- `SignalValueRefreshScheduled`: `1535`, `61.711/s`
- `SignalValueRefreshCoalesced`: `1410`, `56.685/s`
- `SignalLatestValueApplied`: `1515`, `60.906/s`
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `1495`, `60.102/s`, `P95=81.6 ms`, `Max=153.3 ms`
- `SignalRegistryEventIrrelevant`: `108634`, `4367.338/s`
- `RegistryPublishDuration PropertyUpdated.read`: `Count=6426`, `P95Ms=40.7`, `MaxMs=116.4`

Interpretation:

- The new counters confirmed that the first diagnostic pass was too broad: irrelevant-event aggregation alone created very high hot-path pressure (`SignalRegistryEventIrrelevant` above `4300/s`) and materially degraded both UI delay and Signal throughput.
- The ancestor `PropertyUpdated.read` path is now covered by focused tests and the queue now stores the resolved `...read` child target, so the logical fix itself is preserved.
- After observing the benchmark regression, the irrelevant-event diagnostics were narrowed to the current Signal target scope. The follow-up retry confirmed that this instrumentation fix recovered the lost delivery throughput without requiring another product-code change in the Signal target-update path.

Retry validation:

- Scenario: `SignalRegistryDeliveryFix-30s-Retry`
- Benchmark status: `Pass`
- `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
- `DemoGenerate`: `5660`, `188.659/s`
- `RegistryPublish[SteadyState]`: `14940`, `497.981/s`
- `RegistryPublish read`: `7523`, `250.757/s`
- `SignalRegistryEventRelevant`: `4836`, `161.194/s`
- `SignalReceive`: `4836`, `161.194/s`
- `SignalLatestValueStored`: `4816`, `160.527/s`
- `SignalValueRefreshScheduled`: `4017`, `133.895/s`
- `SignalValueRefreshCoalesced`: `799`, `26.632/s`
- `SignalLatestValueApplied`: `4016`, `133.862/s`
- `TargetRawValueChanged`: `4043`, `134.762/s`
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `3553`, `118.429/s`, `P95=11.0 ms`, `Max=55.6 ms`
- `RegistryPublishDuration PropertyUpdated.read`: `Count=10999`, `P95Ms=26.1`, `MaxMs=127.6`

Updated interpretation:

- The retry closed the regression introduced by the first broad irrelevant-event diagnostics pass. The benchmark returned to the expected range from the pre-regression baseline for both `SignalReceive` and `TargetValueRefreshExecuted[SteadyState->SteadyState]`.
- The first remaining measured gap is still between visible-path `RegistryPublish read` (`250.757/s`) and Signal relevance receipt (`161.194/s`), but this no longer blocks the active handoff acceptance because the retry moved the public-registry-to-Signal path back into the expected steady-state range and kept UI latency bounded.
- `SignalReceive` semantically matches accepted relevant registry events in the Signal pipeline, not final display application; that is now corroborated by the near-identical `SignalRegistryEventRelevant` and `SignalReceive` counts in the retry.
- The downstream apply path is also healthy again: `SignalLatestValueApplied` rose from about `60.9/s` in the regressed run to `133.9/s`, while `TargetValueRefreshExecuted[SteadyState->SteadyState]` recovered to `118.4/s` with low steady-state latency.

Next step:

- No additional `PageItemModel` product change is justified from this handoff alone. If later work wants to close the remaining gap toward `~200/s`, start from the earlier registry publication shape before Signal relevance matching rather than reopening the value-refresh coalescing path.

## 2026-06-04 Periodic Demo Scheduler Update

Implemented change set:

- `src/HornetStudio.Editor/Widgets/UdlClient/SimulatedHostUdlClient.cs`
  - Replaced the fixed post-work `Task.Delay(100)` loop with a drift-corrected periodic scheduler.
  - Added a named `DemoTickInterval` and a scheduled `nextTickUtc` cadence so loop work no longer adds another full `100 ms` delay after every tick.
  - Added bounded overrun/resync tracking and a summary diagnostic at loop shutdown instead of per-tick logging.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Added a focused self-test that injects `FrameReceived` handler load and verifies the simulated demo keeps periodic cadence under load.

Validation:

- `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-periodic-scheduler\`
  - Passed after adding the missing `System.Diagnostics` import required by the new test.
- Focused editor self-test passed:
  - `UDL simulated demo scheduler keeps periodic cadence under handler load`
- `dotnet build HornetStudio.sln --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\solution-periodic-scheduler\`
  - Passed; only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- `scripts\run_ui_benchmark.ps1 -Seconds 30 -ProjectPath 'benchmarks\ui_lag_bench_project\project.aaep' -Scenario 'UdlDemoPeriodicScheduler-30s'`
  - Passed.

Primary benchmark result:

- Scenario: `UdlDemoPeriodicScheduler-30s`
- Benchmark status: `Pass`
- `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
- `DemoGenerate`: `5700`, `189.911/s`, about `9.50 Hz` per module for 20 modules
- `SignalReceive`: `5016`, `167.122/s`
- `RegistryPublish[SteadyState]`: `15145`, `504.598/s`
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `3600`, `119.944/s`, `P95=6.8 ms`, `Max=15.8 ms`
- `RegistryPublishDuration PropertyUpdated.read`: `Count=11146`, `P95Ms=15.3`, `MaxMs=103.4`

Follow-up comparison run:

- Scenario: `UdlDemoPeriodicScheduler-DetailOn-30s`
- Benchmark status: `Pass`
- `DemoGenerate`: `5700`, `189.954/s`
- `SignalReceive`: `5227`, `174.192/s`
- `RegistryPublish[SteadyState]`: `15282`, `509.278/s`
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `3679`, `122.604/s`, `P95=10.1 ms`, `Max=19.1 ms`
- `BrowserDispatcherPosts=0`, `ChartRenderCount=0`
- The run label says `DetailOn`, but the log still reported `BenchmarkUdlDetailDiagnosticsEnabled=false`, so `DemoLoopTotal` remained filtered in that run.

Interpretation:

- The periodic scheduler recovered most of the remaining demo throughput gap and moved the simulator from the earlier `~170/s` range to `~190/s`, which is close to the `~200/s` target from the active handoff.
- The result supports the handoff hypothesis that fixed post-work delay semantics were a major part of the remaining gap.
- Steady-state UI refresh also improved materially relative to the reduced-diagnostics handoff snapshot (`TargetValueRefreshExecuted[SteadyState->SteadyState]` from about `115.8/s`, `P95=8.3 ms` to about `119.9/s`, `P95=6.8 ms`).
- Direct `DemoLoopTotal` confirmation is still missing from the benchmark summary because the detail switch did not actually enable fine-grained UDL stages in the follow-up run.

## 2026-06-04 UDL Diagnostic Overhead A/B Preparation

Implemented change set:

- `src/HornetStudio.Editor/Monitoring/UiResponsivenessDiagnostics.cs`
  - Added a cached benchmark-only switch `HORNETSTUDIO_UI_BENCHMARK_UDL_DETAIL` for fine-grained UDL signal-pipeline diagnostics.
  - During UI benchmark runs, fine-grained UDL stages are now filtered out by default before benchmark aggregation.
  - High-level benchmark stages such as `DemoGenerate`, `RegistryPublish`, `SignalReceive`, and `TargetValueRefreshExecuted` remain active.
  - Detailed UDL benchmark diagnostics can still be re-enabled explicitly for debug comparisons by setting `HORNETSTUDIO_UI_BENCHMARK_UDL_DETAIL=1`.

Filtered by default during benchmark runs:

- `DemoSetRead`
- `DemoSetOut`
- `DemoSetSet`
- `DemoSetState`
- `DemoSetAlert`
- `DemoApplyModuleSnapshot`
- `DemoComputeTargetValue`
- `DemoFrameReceived`
- `DemoLoopUpdateModules`
- `DemoLoopTotal`
- `UdlRuntimeFrameReceived`
- `UdlProjectionFrameReceived`
- `UdlSynchronizeBitValues`

Validation status:

- `dotnet build HornetStudio.sln --no-restore -c Debug`
  - Passed; only the pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- `scripts\run_ui_benchmark.ps1 -Seconds 30 -ProjectPath 'benchmarks\ui_lag_bench_project\project.aaep' -Scenario 'UdlReducedDiagnostics-30s' -NoBuild`
  - Passed.
- `HORNETSTUDIO_UI_BENCHMARK_UDL_DETAIL=1` + `scripts\run_ui_benchmark.ps1 -Seconds 30 -ProjectPath 'benchmarks\ui_lag_bench_project\project.aaep' -Scenario 'UdlReducedDiagnostics-DetailOn-30s' -NoBuild`
  - Passed.

Observed benchmark results:

- Baseline before this change, `UdlLoggingOverheadCheck-30s`
  - `DemoGenerate`: `165.729/s`
  - `SignalReceive`: `157.166/s`
  - `RegistryPublish[SteadyState]`: `471.931/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `115.684/s`, `P95=21.5 ms`
  - `DemoLoopTotal`: `8.263/s`, `P95=14.3 ms`
- Reduced detail default, `UdlReducedDiagnostics-30s`
  - Fine-grained UDL stages were absent from the benchmark summary as intended.
  - `DemoGenerate`: `170.609/s`
  - `SignalReceive`: `161.512/s`
  - `RegistryPublish[SteadyState]`: `474.974/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `115.828/s`, `P95=8.3 ms`
  - `DemoLoopTotal`: filtered out by design.
- Detail re-enabled on the same build, `UdlReducedDiagnostics-DetailOn-30s`
  - Fine-grained UDL stages returned as intended, for example `DemoSetRead`, `DemoLoopTotal`, and `UdlRuntimeFrameReceived`.
  - `DemoGenerate`: `173.500/s`
  - `SignalReceive`: `164.703/s`
  - `RegistryPublish[SteadyState]`: `470.786/s`
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `115.822/s`, `P95=14.4 ms`
  - `DemoLoopTotal`: `8.663/s`, `P95=8.2 ms`

Expected comparison outcome:

- The reduced-detail run improved slightly versus the older baseline, but not enough to explain the remaining gap to `~200/s`.
- The same-build detail-on comparison was not slower; it was slightly faster on this run, which means the remaining throughput gap is not mainly caused by the fine-grained UDL benchmark diagnostics.
- The new switch is still useful for measurement hygiene and future focused debugging, but the next optimization step should return to the product/runtime path rather than more diagnostic suppression.

## 2026-06-04 Empty UDL Exposure Fast Path Update

Implemented change set:

- `src/HornetStudio.Editor/UdlClients/UdlClientRegistryProjection.cs`
  - Caches parsed `UdlModuleExposures` definitions with the active runtime definition.
  - Tracks whether active bit exposures exist and skips per-frame bit synchronization when none are configured.
  - Passes cached exposure definitions into registry echo/writeback handling so value updates do not reparse or resynchronize empty exposure definitions.
- `src/HornetStudio.Editor/UdlClients/UdlClientExposureProjection.cs`
  - Added cached-definition overloads for exposure synchronization, bit writeback, and channel value updates.
  - `SynchronizeBitValues` now returns before diagnostics when no active bit exposures exist.
  - Existing non-empty bit exposure behavior remains covered by the focused runtime manager regression test.
- `src/HornetStudio.Editor/Widgets/UdlClient/SimulatedHostUdlClient.cs`
  - `set`, `state`, and `alert` read properties now skip unchanged assignments.
  - `read` and `out` telemetry assignments remain on the high-frequency path.
- `src/HornetStudio.Editor.Tests/Program.cs`
  - Added focused tests for active bit exposure detection and unchanged low-value simulator assignments.

Validation:

- `dotnet build src\HornetStudio.Editor.Tests\HornetStudio.Editor.Tests.csproj --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\editor-tests-udl-value-publish\`
  - Passed; only pre-existing `RealtimeChartControl._isAttachedToVisualTree` CS0414 warning remained.
- Focused editor self-tests passed:
  - `UDL runtime manager publishes exposed bits without widget`
  - `UDL exposure projection detects active bit exposures`
  - `UDL simulated demo low value reads skip unchanged assignments`
- `dotnet build HornetStudio.sln --no-restore -c Debug -p:OutDir=b:\HornetStudio\artifacts\solution-udl-value-publish\`
  - Passed; only the same pre-existing RealtimeChart warning remained.
- Standard benchmark script build initially hit a stale `HornetStudio.Editor.Tests` output lock from `.NET Host` PID `26096`; the process was identified as `dotnet src\HornetStudio.Editor.Tests\bin\Debug\net9.0-windows\HornetStudio.Editor.Tests.dll` and stopped before rerunning the benchmark.

Latest benchmark:

- Scenario: `UdlEmptyExposureFastPath-30s-Retry`
- Benchmark status: `Fail` because startup/warmup `MaxUiDelayMs=8019.1` exceeded the fail budget.
- `BrowserDispatcherPosts=0`, `ChartRenderCount=0`.
- `UdlSynchronizeBitValues`: absent from the final retry summary with `UdlModuleExposures: []`.
- `DemoGenerate`: `4660`, `155.318/s`, about `7.77 Hz` per module.
- `DemoLoopTotal`: `233`, `7.766/s`, `P95=22.2 ms`, `Max=47 ms`.
- `RegistryPublish[SteadyState]`: `13389`, `446.255/s`; top modules only `read` and `out`.
- `SignalReceive`: `4369`, `145.619/s`.
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `3267`, `108.889/s`, `P95=49.1 ms`, `Max=96 ms`.
- `RegistryPublishDuration PropertyUpdated.read`: `Count=9163`, `P95Ms=31.6`, `MaxMs=111.8`.

Interpretation:

- The empty-exposure work is now removed from the benchmark summary, satisfying the immediate fast-path acceptance criterion.
- Suppressing unchanged `set`, `state`, and `alert` assignments removed those channels from steady-state public registry publish top modules; steady-state registry publish is now dominated by `read` and `out`.
- Throughput did not move closer to `200/s` in this run; it stayed near the latest baseline around `155/s`, so the remaining limiter is likely outside empty exposure synchronization.
- Steady-state target refresh latency improved materially versus the handoff baseline (`P95=98.2 ms` -> `49.1 ms`), while the separate startup/warmup UI spike remains unresolved.

## 2026-06-04 UDL Publish Decoupling Update

Implemented change set:

- `src/HornetStudio.Host/UiFolderContext.cs`
  - Added a UDL-scoped latest-wins source-to-public publish queue for `runtime.udl_client.*` attachments.
  - Value and non-structural property updates from UDL sources no longer publish synchronously on every source change.
  - A background timer flushes pending public updates at `100 ms` cadence.
  - Structural snapshot publication remains immediate.
  - Public target writeback remains direct.
  - Pending source publishes are discarded on disposal and when a public target path is written back, to avoid stale replay after control writes.
  - `Dispose()` and timer flush handling are now reentrancy-safe for concurrent or repeated disposal.
- `src/HornetStudio.Editor/UdlClients/UdlClientRegistryProjection.cs`
  - Removed the per-event `SynchronizeAttachments()` call from the public registry echo path so runtime/public echo traffic no longer rebuilds attachment projections.
- `src/HornetStudio.Editor/Widgets/UdlClient/UdlClientRuntime.cs`
  - `TryResolveRuntimeItem(...)` now resolves directly from the live runtime tree instead of cloning the full runtime snapshot for each lookup.
- `src/HornetStudio.Host.Tests/Program.cs`
  - Added focused regression tests for:
    - latest-wins coalescing of rapid UDL value updates,
    - concurrent/reentrant `UiFolderContext.Dispose()` safety.

Current measurement state:

- Latest completed run in `src/HornetStudio/bin/Debug/net9.0-windows/logs/host-20260604.log` now uses the requested scenario name `UdlValuePublishDecoupling-Fix-30s`.
- Completed benchmark summary from `2026-06-04 11:36`:
  - `DemoGenerate`: `Count=3613`, `EventsPerSecond=123.047`
  - `SignalReceive`: `Count=3566`, `EventsPerSecond=121.446`
  - `RegistryPublish[SteadyState]`: `Count=19419`, `EventsPerSecond=661.346`
  - `RegistryPublishDuration PropertyUpdated.read`: `Count=17760`, `P95Ms=24.6`, `MaxMs=93.9`
  - `BrowserDispatcherPosts=0`
  - `ChartRenderCount=0`
  - Benchmark status: `Fail` because startup/warmup produced `MaxUiDelayMs=7208.7`.
- Relative to the baseline from the active handoff, this turn improved the measurable hot-path throughput substantially:
  - `DemoGenerate`: about `45.31/s` -> `123.05/s`
  - `SignalReceive`: about `39.27/s` -> `121.45/s`
  - `RegistryPublishDuration PropertyUpdated.read P95`: `297.8 ms` -> `24.6 ms`
- No fresh `Collection was modified; enumeration operation may not execute.` exception was observed in the completed `11:36` benchmark window.
- A second benchmark rerun was started at `2026-06-04 11:47`, but its final summary was not yet present in the log during this chat turn.

Interpretation:

- The completed benchmark confirms that the synchronous source-to-public publish coupling was a real bottleneck.
- The fix materially reduced synchronous public publish time and raised both runtime generation and received signal rates.
- The workitem is only partially accepted so far: the system is much faster and the dispose crash no longer reproduced in the completed run, but `DemoGenerate` is still well below the `~200/s` target and the benchmark still fails its warmup UI delay budget.

Recommended next step:

- Focus the next investigation on the remaining steady-state/runtime cost after public publish decoupling, plus the separate warmup UI spike budget failure.
- The second benchmark run that started at `11:47` should be checked first; if it completes with similar numbers, the next likely hotspots are runtime-side per-frame work or startup-time UI initialization rather than synchronous public publication.

## 2026-06-04 Root Cause Update

The latest implementation and benchmark run isolated the remaining multi-second target refresh delays:

- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `Count=771`, `DelayMaxMs=3.7`, `DelayP95Ms=0.7`
- `TargetRefreshExecuted[Warmup->Warmup]`: `Count=80`, `DelayMaxMs=3117.9`, `DelayP95Ms=3114.3`
- `TargetValueRefreshExecuted[Warmup->Warmup]`: `Count=141`, `DelayMaxMs=2989.4`, `DelayP95Ms=2806.3`
- No `Warmup->SteadyState` or `SteadyState->Warmup` target refresh outliers were observed.

Interpretation:

- The previous 4-5 second `TargetRefreshExecuted` / `TargetValueRefreshExecuted` numbers were not a steady-state Signal widget bottleneck.
- The remaining large delays come from UI work scheduled and executed entirely inside the benchmark warmup window during startup/runtime initialization.
- The value-only refresh optimization is behaving correctly in steady state for this scenario.

Current instrumentation improvement:

- `UiResponsivenessDiagnostics` now emits phase-aware signal pipeline stage names such as `TargetValueRefreshExecuted[Warmup->Warmup]` and `TargetValueRefreshExecuted[SteadyState->SteadyState]`.
- `PageItemModel` now preserves queue-time signal path/module context for target refresh diagnostics so executed events are attributed to the queued source more reliably.

Recommended next topic:

- Investigate startup/warmup UI cost from runtime/widget initialization, especially the burst of chart/runtime creation and startup-phase work.
- Do not spend the next chat on further steady-state Signal widget refresh optimization unless a new benchmark shows steady-state regressions.

## Context

We are working in `B:\HornetStudio`.

The current discussion isolated a new performance/architecture topic:

- The UDL client/demo runtime itself is not the suspected bottleneck.
- The user suspects the system path that updates visible controls cannot keep up with multiple public signal updates.
- Target scenario: show dashboards with around 20 UDL values at 10 Hz.

The next chat should start fresh and focus on the update chain from public `HostRegistries.Data` updates to SignalWidget/Chart rendering.

## Current Bench Project

Project:

```text
B:\HornetStudio\benchmarks\ui_lag_bench_project\project.aaep
```

Relevant files:

```text
B:\HornetStudio\benchmarks\ui_lag_bench_project\Folders\main\Folder.yaml
B:\HornetStudio\benchmarks\ui_lag_bench_project\Folders\main\Clients\Udl\udl1.yaml
```

Current intended structure:

- one `UdlClientControl`
- 20 UDL demo modules: `m001` to `m020`
- 20 `Signal` widgets reading `udl1.m001.read` to `udl1.m020.read`
- optional chart in the current working copy may read a subset like `m001` and `m003`
- no EnhancedSignal, Monitor, Controller, or MonitorView entries should be part of this bench scenario

Important user observation:

- When all 20 values are attached/public, visible signal updates appear much slower than expected.
- When some modules are detached from the HostRegistry/public projection, remaining controls update faster.
- Detached modules still exist as DemoModules; only their public HostRegistry projection was removed.
- Widgets for detached paths show fallback/text-like values such as `M004`, `M007`, `M008`, `M014`, `M020`, which is consistent with public paths no longer receiving values.

## Key Interpretation

This is probably not a UDL client throughput problem.

The suspected bottleneck is:

```text
UDL runtime/demo value update
 -> attached public HostRegistry item update
 -> HostRegistries.Data.ItemChanged
 -> SignalWidget/PageItemModel target refresh
 -> Avalonia binding/text render
```

20 values at 10 Hz means about 200 public value updates per second. That is not a high raw-data rate, but it can become expensive if every update causes dispatcher work, formatting, binding invalidation, target refresh, or structure work.

## Important Code References

UDL demo simulation:

```text
src/HornetStudio.Editor/Widgets/UdlClient/SimulatedHostUdlClient.cs
```

Current known behavior:

- Demo modules are converted into `_moduleStates`.
- `SimulationLoopAsync` iterates all `_moduleStates`.
- It updates module state and raises `FrameReceived`.
- The simulation is based on DemoModules, not on `AttachedItemPaths`.
- Attach/detach controls only public projection, not whether the demo module exists internally.

UDL projection and attach path:

```text
src/HornetStudio.Editor/UdlClients/UdlClientRegistryProjection.cs
src/HornetStudio.Editor/Widgets/UdlClient/UdlHostRegistryProjection.cs
src/HornetStudio.Host/UiFolderContext.cs
```

SignalWidget/PageItemModel target update path:

```text
src/HornetStudio.Editor/Models/PageItemModel.cs
```

Relevant methods to inspect:

- `OnDataRegistryChanged`
- `RequestTargetRefresh`
- `TriggerTargetRefresh`
- `RefreshTargetBindings`

Chart path:

```text
src/HornetStudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs
src/HornetStudio.Editor/Widgets/RealtimeChart/ChartDataProvider.cs
src/HornetStudio.Editor/Widgets/RealtimeChart/SignalHistoryRuntimeManager.cs
```

Diagnostics:

```text
src/HornetStudio.Editor/Monitoring/UiResponsivenessDiagnostics.cs
src/HornetStudio.Editor/Monitoring/UdlSignalGapDiagnostics.cs
scripts/run_ui_benchmark.ps1
```

## Questions To Answer Next

Measure and separate these stages:

1. Does the demo runtime generate all 20 values at 10 Hz?
2. Does public `HostRegistries.Data` publish all attached values at 10 Hz?
3. Do all 20 SignalWidgets receive the events at 10 Hz?
4. Do widgets intentionally or accidentally coalesce/throttle visible updates?
5. Does each public value update enqueue dispatcher work?
6. Does `RefreshTargetBindings` do too much work for value-only updates?
7. Does Chart sampling/rendering add meaningful load, or is the SignalWidget path enough to reproduce the slowdown?

## Recommended New Chat Prompt

Use this in a new chat:

```text
#debug
Wir untersuchen die High-Frequency-Control-Update-Kette. Bitte anhand von docs/workitems/ChatHandoff.md starten.

Ziel: 20 UDL DemoModules mit 10 Hz als 20 SignalWidgets sichtbar darstellen.
Der UDL-Client ist nicht der Hauptverdacht. Verdacht ist HostRegistry -> SignalWidget/PageItemModel -> Avalonia UI Update.

Bitte zuerst messen und erklären:
1. DemoGenerate rate
2. HostRegistry public publish rate
3. SignalWidget receive/refresh/render rate
4. Dispatcher posts / UI delay
5. Unterschied mit 1, 5, 10, 20 attached public values

Erst danach Lösung vorschlagen. Keine breite Refactorings.
```

## Recommended Analysis Order

1. Run a fresh benchmark against `benchmarks\ui_lag_bench_project\project.aaep`.
2. Start with a small matrix:
   - 1 attached signal
   - 5 attached signals
   - 10 attached signals
   - 20 attached signals
   - with and without chart if needed
3. Inspect latest log around the benchmark window.
4. Count rates for:
   - `DemoGenerate`
   - `RegistryPublish`
   - SignalWidget/PageItemModel target refresh
   - Dispatcher posts
   - Chart render/sample
5. Identify the first stage where expected 10 Hz per signal drops.
6. Only then propose implementation.

## Likely Solution Shape

Expected direction, not yet verified:

```text
Runtime/Registry:
  keep high-frequency values complete and scoped

SignalWidget/PageItemModel:
  value-only fast path
  store latest value per target
  mark dirty
  coalesce visible UI updates
  avoid one dispatcher post per value update
  avoid structure refresh/rebuild for value-only changes

Chart:
  keep independent sampling/coalescing
  avoid coupling chart sampling to SignalWidget visible update frequency
```

Acceptance target:

- 20 SignalWidgets
- 10 Hz input values
- visible updates stable and configurable, ideally around 10 Hz for this bench
- no growing dispatcher backlog
- no broad structure rebuilds on value-only updates
- chart remains smooth when included

## 2026-06-04 UDL Runtime Root Signature Follow-Up

Implemented a focused runtime optimization in `UdlClientRuntime` after adding bounded stage diagnostics around the demo loop and adjacent UDL frame callbacks.

Instrumentation added:

- `DemoLoopTotal`
- `DemoLoopUpdateModules`
- `DemoComputeTargetValue`
- `DemoApplyModuleSnapshot`
- `DemoSetRead`
- `DemoSetSet`
- `DemoSetOut`
- `DemoSetState`
- `DemoSetAlert`
- `DemoFrameReceived`
- `UdlRuntimeFrameReceived`
- `UdlProjectionFrameReceived`
- `UdlSynchronizeBitValues`

Measured isolation run:

- Scenario: `UdlLoopThroughputIsolation-30s`
- `DemoGenerate`: `123.329/s` total, about `6.17 Hz` per module.
- `SignalReceive`: `122.596/s`.
- `RegistryPublish[SteadyState]`: `668.979/s`.
- `RegistryPublishDuration PropertyUpdated.read`: `P95=23.3 ms`, `Max=146.6 ms`.
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `95.297/s`, `P95=51.9 ms`, `Max=170.1 ms`.
- `DemoFrameReceived`: `P95=6.9 ms`.
- `UdlRuntimeFrameReceived`: `P95=6.8 ms`.
- `UdlProjectionFrameReceived`: `P95=0.1 ms`.
- `UdlSynchronizeBitValues`: `P95=0.1 ms`.
- `DemoApplyModuleSnapshot`: `P95=0.2 ms`.

Conclusion from isolation run:

- The remaining cost was not in `ApplyModuleSnapshot` and not in `SynchronizeBitValues`.
- The dominant residual cost sat in `UdlClientRuntime.OnClientFrameReceived`.
- `NotifyRuntimeStructureChangedIfNeeded()` was rebuilding a root signature from a full runtime item snapshot on every frame.

Applied fix:

- `GetReceivedRootPaths()` now derives roots directly from the top-level runtime item dictionary instead of traversing the full runtime tree.
- `NotifyRuntimeStructureChangedIfNeeded()` now computes its signature from those root items only.
- Behavior remains the same for root-path discovery and structure change detection, but the per-frame steady-state cost is reduced substantially.

Measured follow-up run:

- Scenario: `UdlLoopThroughputFix-30s`
- `DemoGenerate`: `163.311/s` total, about `8.17 Hz` per module.
- `SignalReceive`: `154.279/s`.
- `RegistryPublish[SteadyState]`: `812.224/s`.
- `RegistryPublishDuration PropertyUpdated.read`: `P95=25.4 ms`, `Max=114.5 ms`.
- `TargetValueRefreshExecuted[SteadyState->SteadyState]`: `112.352/s`, `P95=59.6 ms`, `Max=274.5 ms`.
- `DemoFrameReceived`: `P95=0.6 ms`.
- `UdlRuntimeFrameReceived`: `P95=0.6 ms`.
- `UdlProjectionFrameReceived`: `P95=0 ms`.
- `UdlSynchronizeBitValues`: `P95=0 ms`.
- `DemoLoopTotal`: `163.311/s` total, loop `P95=28.6 ms`.
- `BrowserDispatcherPosts=0`.
- `ChartRenderCount=0`.
- No fatal shutdown exception observed.

Delta:

- `DemoGenerate`: `123.329/s` -> `163.311/s`.
- `SignalReceive`: `122.596/s` -> `154.279/s`.
- `UdlRuntimeFrameReceived P95`: `6.8 ms` -> `0.6 ms`.
- `DemoFrameReceived P95`: `6.9 ms` -> `0.6 ms`.

Remaining gap and next suspicion:

- The target of about `200/s` total and about `10 Hz` per module is still not reached.
- The next visible limiter is no longer the runtime frame callback itself.
- The remaining throughput gap now appears downstream in public publish plus signal refresh handling:
  - `RegistryPublishDuration PropertyUpdated.read` is still about `25 ms` P95.
  - `TargetValueRefreshExecuted[SteadyState->SteadyState]` is still about `112/s` with `59.6 ms` P95.
- The next chat should start from the post-runtime numbers above and inspect the signal refresh / visible update path instead of the UDL runtime loop.
