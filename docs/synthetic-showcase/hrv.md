---
title: Synthetic Showcase HRV
description: Publication-facing HRV examples that connect accepted RR intervals, adjacent RR deltas, and the final time-domain HRV telemetry.
summary: Use this page when you need a deterministic high-vs-low HRV pair behind the published RMSSD, SDNN, pNN50, and lnRMSSD figures.
nav_label: Showcase HRV
nav_group: Internals
nav_order: 77
---

# HRV Showcase

This page uses `hrv_high` and `hrv_low` as the canonical pair for explaining
how the accepted RR window becomes adjacent RR deltas and then the published
time-domain HRV metrics.

<p>
  <a class="button primary" href="../hrv-formulas.md">Open formula sheet</a>
  <a class="button" href="../assets/synthetic-showcase/hrv-derivation.svg">Download derivation SVG</a>
  <a class="button" href="../assets/synthetic-showcase/synthetic-showcase-figure-pack.pdf">Download figure pack PDF</a>
</p>

![HRV derivation](../assets/synthetic-showcase/hrv-derivation.png)

## Canonical Pair

- [hrv_high RR window](../data/synthetic-showcase/scenarios/hrv_high/hr_rr.csv)
- [hrv_high analysis](../data/synthetic-showcase/scenarios/hrv_high/analysis.json)
- [hrv_low RR window](../data/synthetic-showcase/scenarios/hrv_low/hr_rr.csv)
- [hrv_low analysis](../data/synthetic-showcase/scenarios/hrv_low/analysis.json)

## Computation Path

```text
AcceptedRrSamples
-> AdjacentRrDeltas
-> CurrentRmssdMs / SdnnMs / Pnn50Percent / LnRmssd
```

- `Hrv.Diagnostics.AcceptedRrSamples` exposes the RR window accepted by the
  tracker.
- `Hrv.Diagnostics.AdjacentRrDeltas` exposes the beat-to-beat delta series used
  by `RMSSD` and the related time-domain solves.
- `Hrv.Telemetry.CurrentRmssdMs`, `SdnnMs`, `Pnn50Percent`, and `LnRmssd`
  expose the final documentation values shown in the figure.

## Showcase Preset

The `showcase-v1` preset keeps the HRV tracker on a deterministic `120 s`
window with a `32`-sample minimum so the exported figures stay reproducible and
still map cleanly onto the longer-window operator guidance in the main workflow
docs.

## Related Pages

- [Synthetic Showcase Overview](index.md)
- [HRV Formula Sheet](../hrv-formulas.md)
- [HRV Workflow](../hrv-workflow.md)
- [Formula Sheets](../formula-sheets.md)
