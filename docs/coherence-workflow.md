---
title: Coherence Workflow
description: Use the dedicated coherence window to inspect RR-derived coherence, confidence, stabilization, and tuning.
summary: Open the coherence window after HR and RR are already live, wait for the RR window to stabilize, and use the dedicated telemetry view when the Live tab summary plot is not enough.
nav_label: Coherence Workflow
nav_group: Task Guides
nav_order: 22
---

# Coherence Workflow

The `Coherence window` is the RR-derived coherence operator surface. It uses the
accepted RR interval stream, builds a rolling resonance window, and surfaces a
smoothed normalized coherence value with confidence and underlying band-power telemetry.

## Reference Model

The fixed spectral method follows McCraty, Atkinson, Tomasino, and Bradley,
*The Coherent Heart: Heart-Brain Interactions, Psychophysiological Coherence,
and the Emergence of System-Wide Order* (Institute of HeartMath, 2006).

The app keeps these paper-defined constants fixed:

- search the maximum spectral peak in `0.04-0.26 Hz`
- integrate peak power in a `0.030 Hz` window centered on that peak
- integrate total power across `0.0033-0.4 Hz`
- compute the paper ratio as `(Peak Power / (Total Power - Peak Power))^2`

The app also keeps the AstralKarateDojo-compatible normalized score that is used
for the headline readout and summary charts:

- normalized score = `Peak Power / Total Power`, clamped to `0..1`

The paper defines the spectral calculation above, but it does not define the
operator-side runtime controls exposed by this app.

Real-vs-synthetic alignment note:

- on a physical H10, the RR intervals feeding coherence come from the strap's
  device-side beat timing, which is derived from ECG
- on the sibling `SyntheticBio` transport, the coherence tracker still solves
  from RR, but the transport now also emits synthetic PMD ECG frames that stay
  synchronized with that RR schedule so the waveform view does not drift away
  from the interval view during synthetic sessions

<p>
  <a class="button primary" href="coherence-formulas.md">Open formula sheet</a>
  <a class="button" href="assets/reference-markdown/coherence-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/coherence-formulas.pdf">Download PDF</a>
</p>

## What The Window Exposes

- headline `Coherence` value plus `Confidence`
- tracker readiness, stale state, stabilization progress, and RR sample count
- current heartbeat BPM and the last accepted RR interval
- peak frequency, peak band power, total band power, and raw coherence
- reset, apply-tuning, and restore-defaults controls for the current device
- a dedicated chart and log stream for the selected device

## Manual App Test

1. Wear the strap and wet the electrodes.
2. Start the app and connect to the intended strap.
3. Confirm HR updates and RR intervals are moving on the `Telemetry` tab.
4. Open `Coherence window` from the `Telemetry` tab.
5. Check the top status line:
   - before the RR window stabilizes, coherence can stay unavailable
   - confidence should remain low until enough accepted RR data accumulates
6. Keep the strap still enough for clean RR timing while the warmup runs.
7. Confirm the coherence window starts updating:
   - the coherence headline leaves `--`
   - confidence rises above zero
   - RR sample count and coverage increase
   - peak frequency and band-power fields populate once a coherence sample exists
8. Use the shared telemetry selector if you want the `Telemetry` tab to plot:
   - `Coherence`
   - `Coherence confidence`
9. Adjust tuning only if needed:
   - `Apply tuning` resets the tracker with the new RR window settings
   - `Restore defaults` returns to the app defaults
   - `Reset tracker` clears the current RR-derived window state

## Tuning Parameters

- `Min RR samples`: accepted RR intervals required before the tracker attempts a spectral solve
- `Window (s)`: rolling RR history length used for spline resampling and PSD analysis
- `Smoothing speed`: exponential smoothing speed applied to the displayed normalized score
- `Stale timeout (s)`: freshness timeout for the `Tracking` state once RR updates stop

The following coherence constants are intentionally not user-tunable in the app:

- peak-search band `0.04-0.26 Hz`
- peak-window width `0.030 Hz`
- total-band range `0.0033-0.4 Hz`

## Fast Failure Checks

- If the window stays on `No RR` or `Awaiting device selection`, HR may be present but usable RR intervals are not reaching the tracker yet.
- If the status says it is still building the RR coherence window, keep the stream running longer before treating it as a failure.
- If coherence becomes available and then goes stale, the RR input is no longer fresh enough for the configured timeout.
- If confidence stays near zero, the tracker has not collected enough stable RR coverage yet.

## Related Pages

- [Getting Started on Windows](getting-started.md)
- [App Overview](app-overview.md)
- [HRV Workflow](hrv-workflow.md)
- [Breathing Dynamics Workflow](breathing-dynamics-workflow.md)
- [Formula Sheets](formula-sheets.md)
- [Troubleshooting](troubleshooting.md)
- [References](references.md)
