---
title: Synthetic Showcase Coherence
description: Publication-facing coherence examples that connect accepted RR intervals, the PSD solve, and the final normalized coherence telemetry.
summary: Use this page when you need a deterministic high-vs-low coherence pair plus the off-resonance appendix sweep behind the published figure set.
nav_label: Showcase Coherence
nav_group: Internals
nav_order: 76
---

# Coherence Showcase

This page pairs `coherence_high` and `coherence_low` with the appendix sweep so
the published figures can show exactly how the accepted RR window and spectral
shape drive the normalized coherence score.

<p>
  <a class="button primary" href="../coherence-formulas.md">Open formula sheet</a>
  <a class="button" href="../assets/synthetic-showcase/coherence-derivation.svg">Download derivation SVG</a>
  <a class="button" href="../assets/synthetic-showcase/coherence-appendix.svg">Download appendix SVG</a>
</p>

![Coherence derivation](../assets/synthetic-showcase/coherence-derivation.png)

## Canonical Pair

- [coherence_high RR window](../data/synthetic-showcase/scenarios/coherence_high/hr_rr.csv)
- [coherence_high analysis](../data/synthetic-showcase/scenarios/coherence_high/analysis.json)
- [coherence_low RR window](../data/synthetic-showcase/scenarios/coherence_low/hr_rr.csv)
- [coherence_low analysis](../data/synthetic-showcase/scenarios/coherence_low/analysis.json)

## Computation Path

```text
AcceptedRrSamples
-> ResampledTachogram
-> PowerSpectrum
-> PeakBandPower / TotalBandPower
-> CurrentCoherence01
```

- `Coherence.Diagnostics.AcceptedRrSamples` exposes the accepted RR window used
  for the solve.
- `Coherence.Diagnostics.ResampledTachogram` exposes the spline-resampled
  tachogram that feeds the PSD step.
- `Coherence.Diagnostics.PowerSpectrum`, `PeakWindowLowerHz`,
  `PeakWindowUpperHz`, `PeakBandPower`, and `TotalBandPower` expose the peak
  search and integral terms.
- `Coherence.Telemetry.CurrentCoherence01`, `PeakFrequencyHz`, and
  `PaperCoherenceRatio` expose the final downstream values surfaced by the app.

## Appendix Sweep

The appendix figure keeps the resonance and off-resonance comparison visible in
the same publication bundle.

- [resonance_010hz analysis](../data/synthetic-showcase/scenarios/resonance_010hz/analysis.json)
- [off_10bpm analysis](../data/synthetic-showcase/scenarios/off_10bpm/analysis.json)
- [off_12bpm analysis](../data/synthetic-showcase/scenarios/off_12bpm/analysis.json)
- [off_18bpm analysis](../data/synthetic-showcase/scenarios/off_18bpm/analysis.json)
- [off_24bpm analysis](../data/synthetic-showcase/scenarios/off_24bpm/analysis.json)
- [irregular_rr analysis](../data/synthetic-showcase/scenarios/irregular_rr/analysis.json)

![Coherence appendix](../assets/synthetic-showcase/coherence-appendix.png)

## Related Pages

- [Synthetic Showcase Overview](index.md)
- [Coherence Formula Sheet](../coherence-formulas.md)
- [Coherence Workflow](../coherence-workflow.md)
- [Formula Sheets](../formula-sheets.md)
