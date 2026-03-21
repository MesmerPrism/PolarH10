---
title: Breathing Workflow
description: Run the app-side ACC breathing calibration flow, inspect tracker state, and recover from common signal issues.
summary: Use this page when ACC is already streaming and you want a reliable breathing calibration, state readout, and fast failure checklist.
nav_label: Breathing Workflow
nav_group: Task Guides
nav_order: 20
---

# Breathing Workflow

The `Breathing` tab is the app surface for the ACC-only breathing-volume
approximation. It exposes the tracker state, live telemetry, calibration
commands, and tuning parameters that were ported from the existing Unity-side
runtime.

This tracker is repository-specific code, not a cited external method paper.
It has not yet been externally validated, and a validation study is actively
being worked on.

<p>
  <a class="button primary" href="breathing-formulas.md">Open formula sheet</a>
  <a class="button" href="assets/reference-markdown/breathing-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/breathing-formulas.pdf">Download PDF</a>
</p>

## What The Page Exposes

- live output volume plus the current inhale/exhale/pause state
- tracker state such as transport connected, calibrated, calibrating, and bad
  tracking
- calibration commands for `Start calibration`, `Cancel`, and `Reset tracker`
- tuning controls for the default app settings and a live `Flip inhale/exhale`
  action
- telemetry for sample rate, useful-signal detection, bounds, axis, center,
  current volumes, and last calibration failure

## Manual App Test

1. Wear the strap and wet the electrodes.
2. Make sure Polar Beat, Polar Flow, or any other BLE client is not connected
   to the same H10.
3. Start the app and connect to the intended strap.
4. Confirm the main session comes up clean:
   - connection state moves to `Connected` or `Streaming`
   - HR updates begin
   - ACC frame counts increase on the Live tab
5. Open the `Breathing` tab.
6. Check the top status line:
   - before calibration it should report an uncalibrated or bad-tracking state
   - sample-rate and last-sample telemetry should update once ACC is flowing
7. Press `Start calibration`.
8. Breathe normally for the full calibration window without touching the strap.
9. Confirm calibration completes:
   - `Calibrated` should become true
   - the breathing chart should start moving
   - state should move between `Inhaling`, `Exhaling`, and `Pausing`
10. Use `Flip inhale/exhale` if the state direction is reversed for your strap
    orientation.
11. Adjust tuning only if needed:
    - use `Apply tuning` when you want a clean reset with new parameters
    - use `Restore defaults` to go back to the app defaults
12. If the signal becomes unreliable, inspect:
    - `Useful signal`
    - `Axis range`
    - `Tracking`
    - `Last failure`

## Fast Failure Checks

- If `Sample rate` stays low or `Last sample` is stale, ACC is not reaching the
  tracker.
- If `Useful signal` remains false after calibration, the strap motion on the
  chosen axis is too small for the current thresholds.
- If the chart moves but inhale/exhale is reversed, use `Flip inhale/exhale`
  instead of recalibrating.
- If `Last failure` mentions travel or sample-count limits, rerun calibration
  while breathing more clearly through the full window.

## Desktop App Run Paths

The canonical repo-local desktop build is:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Build-Workspace-App.ps1
.\out\workspace-app\PolarH10.App.exe
```

If Windows application-control policy blocks that normal multi-file workspace
launch, publish a single-file fallback instead:

```powershell
dotnet publish src/PolarH10.App/PolarH10.App.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=false `
  -o out/app-single
```

Then launch:

```powershell
.\out\app-single\PolarH10.App.exe
```

## Related Pages

- [App Overview](app-overview.md)
- [Getting Started on Windows](getting-started.md)
- [Formula Sheets](formula-sheets.md)
- [Platform Guides](platform-guides/index.md)
