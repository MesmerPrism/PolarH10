---
title: HRV Workflow
description: Use the dedicated HRV tab to inspect short-term RR-derived HRV, with RMSSD as the headline value and supporting time-domain telemetry alongside it.
summary: Open the HRV tab after RR is already live, let the short-term RR window fill, and treat RMSSD, SDNN, and pNN50 as short-term RR-derived telemetry rather than 24-hour norms.
nav_label: HRV Workflow
nav_group: Task Guides
nav_order: 24
---

# HRV Workflow

The `HRV` tab is the app's short-term RR-derived HRV surface. It uses the
accepted RR interval stream, fills a rolling short-term window, and publishes
RMSSD as the headline value with SDNN, pNN50, SD1, mean NN, and mean HR as
supporting telemetry.

## Reference Model

This workflow follows the short-term HRV guidance summarized by:

- **Shaffer, F.; Ginsberg, J. P.** "An Overview of Heart Rate Variability
  Metrics and Norms." *Frontiers in Public Health* 5:258, 2017.
  <https://doi.org/10.3389/fpubh.2017.00258>

The implementation work for this repository was reviewed against the local PDF
reference file `C:\Users\tillh\Downloads\fpubh-05-00258.pdf`.

The app uses the paper in these specific ways:

- the default short-term HRV window is `300 s` because five minutes is the
  conventional short-term recording standard discussed by the paper
- `RMSSD` is the headline value because the paper describes it as the primary
  time-domain estimate for vagally mediated beat-to-beat change
- `SDNN`, `pNN50`, `SD1`, `mean NN`, and `mean HR` stay visible as supporting
  context instead of replacing the headline
- the tab explicitly treats these as short-term RR-derived metrics, not as a
  substitute for `24 h` norms or artifact-corrected Holter analysis

The tracker keeps the pNN threshold fixed at `50 ms`, and the `SD1` line is the
standard `RMSSD / sqrt(2)` relationship noted by the paper.

Real-vs-synthetic alignment note:

- on a physical H10, the RR intervals feeding the HRV tab come from the
  strap's own beat timing, which is derived from ECG inside the device
- on the sibling `SyntheticBio` transport, the HRV solve remains RR-based, but
  the transport now also emits synthetic PMD ECG frames that stay synchronized
  with that RR schedule so operator tests can compare the waveform and the RR
  telemetry against the same synthetic beat sequence

## What The Tab Exposes

- headline `RMSSD` value from the rolling RR window
- readiness, stale state, accepted RR count, and buffered window coverage
- current heartbeat BPM and the last accepted RR interval
- supporting short-term metrics:
  - `SDNN`
  - `pNN50`
  - `SD1`
  - `mean NN`
  - `mean HR`
  - `lnRMSSD`
- reset, apply-tuning, and restore-defaults controls for the current device
- a dedicated chart for `RMSSD` and `SDNN` on the selected device

## Manual App Test

1. Wear the strap and wet the electrodes.
2. Start the app and connect to the intended strap.
3. Confirm HR updates and RR intervals are moving on the `Telemetry` tab.
4. Open `HRV`.
5. Check the top status line:
   - before the RR window fills, RMSSD can stay unavailable
   - `Waiting for RR` means the transport is connected but accepted RR input has not started
   - `Warming up` means RR is flowing but the short-term window is not full enough yet
6. Keep the session running until the short-term window completes.
7. Confirm the HRV tab starts updating:
   - the `RMSSD` headline leaves `--`
   - `SDNN` and `pNN50` populate
   - accepted RR count and coverage continue to update
8. Optionally switch one shared telemetry plot to `HRV (RMSSD)` if you want the
   overview page to track the rolling headline value.
9. Adjust tuning only if needed:
   - `Apply tuning` resets the tracker with the new RR window settings
   - `Restore defaults` returns to the five-minute app defaults
   - `Reset tracker` clears the current RR-derived window state

## Tuning Parameters

- `Min RR samples`: accepted RR intervals required before the tracker publishes the first solve
- `Window (s)`: rolling RR history length used for the short-term HRV solve
- `Stale timeout (s)`: freshness timeout for the `Tracking` state once RR updates stop

The following HRV choices stay fixed in the app:

- headline metric: `RMSSD`
- pNN threshold: `50 ms`
- supporting `SD1` relationship: `RMSSD / sqrt(2)`

## Interpretation Notes

- A shorter window can be useful for operator preview, but it is not equivalent
  to the default five-minute short-term setting.
- `SDNN` on a short-term window is useful as local context in the tab, but it
  should not be treated as the same thing as a `24 h` SDNN norm.
- The tracker uses accepted RR intervals from the live BLE stream. It does not
  claim full clinical artifact correction.

## Fast Failure Checks

- If the tab stays on `Waiting for RR`, HR may be present but usable RR intervals are not reaching the tracker yet.
- If the state stays on `Warming up`, the RR stream is live but the short-term window is not full enough yet.
- If RMSSD becomes available and then goes stale, RR input stopped arriving inside the configured freshness timeout.
- If the values look implausible, confirm the strap fit first and then inspect the raw RR stream before changing tuning.

## Related Pages

- [Getting Started on Windows](getting-started.md)
- [App Overview](app-overview.md)
- [Coherence Workflow](coherence-workflow.md)
- [Troubleshooting](troubleshooting.md)
- [References](references.md)
