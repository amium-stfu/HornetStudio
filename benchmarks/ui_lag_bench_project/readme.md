# UI Lag Bench Project

This project is the default input for `scripts/run_ui_benchmark.ps1`.

It is intentionally focused on UDL client and signal widget UI traffic. The main folder contains:

- one demo UDL client
- 20 attached UDL demo modules from `m001` to `m020`
- 20 signal widgets reading `udl1.m001.read` to `udl1.m020.read`

The project intentionally does not include enhanced signals, monitor rules, monitor views, controller widgets, or realtime charts.

Run the benchmark from the repository root:

```powershell
scripts/run_ui_benchmark.ps1 -Seconds 60
```

The runner starts HornetStudio with `--ui-benchmark` and `--start-project`, waits for the requested duration, closes the app, and prints CPU, memory, error counts, and benchmark summary lines.
